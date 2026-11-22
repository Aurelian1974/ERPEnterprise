using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Tests.Bank;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Loturile de plati si restituirile pe SQL Server real (02.02, F2 3.6, 4.2 si 4.7; criteriul F2-A8): un lot cu OP-uri neaprobate nu se
/// exporta, reexportul cere drept si motiv si pastreaza EndToEndId-urile, anularea elibereaza OP-urile, restituirea bancii respinge OP-ul.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PaymentBatchIntegrationTests : BankTestBase, IClassFixture<FinanceDatabase>
{
    public PaymentBatchIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.BankView, FinancePermissions.BankStatementImport, FinancePermissions.BankStatementReconcile,
            FinancePermissions.BankStatementValidate, FinancePermissions.PaymentOrderCreate, FinancePermissions.PaymentOrderValidate,
            FinancePermissions.PaymentOrderApprove, FinancePermissions.PaymentOrderReverse, FinancePermissions.PaymentOrderExport,
            FinancePermissions.PaymentOrderCancel]);
    }

    private async Task<(int PartnerId, int BankAccountId, string Iban)> Supplier()
    {
        var iban = StatementFiles.RomanianIban("BTRL", $"{Random.Shared.NextInt64(1_000_000_000_000_000, 9_999_999_999_999_999)}");
        await using var connection = new SqlConnection(Database.ConnectionString);
        var partner = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Furnizor Lot SRL', 1, @FiscalCode, 0, 1, 1, N'Str. Morii 2', N'Sibiu', N'SB', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("FL-"), FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var account = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.PartnerBankAccount (PartnerId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy)
            VALUES (@Partner, @Iban, N'Banca Transilvania', 'RON', 1, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Partner = partner, Iban = iban });
        return (partner, account, iban);
    }

    private async Task<int> ValidatedOrder(int account, string amount, string invoice)
    {
        var (partner, bankAccount, _) = await Supplier();
        var draft = await Send(new SavePaymentOrder(null, account, Day, Day.AddDays(1), false, PaymentPurpose.Suppliers, partner, null, bankAccount, null, null,
            null, null, null, null, [new PaymentOrderLineInput(await OperationType("PLATA-FURNIZOR"), amount, invoice, null, null)], null));
        await Send(new ValidatePaymentOrder(draft.Id, draft.RowVersion));
        return draft.Id;
    }

    private async Task<IReadOnlyList<PaymentOrderVersion>> Versions(params int[] ids)
    {
        var versions = new List<PaymentOrderVersion>();
        foreach (var id in ids)
        {
            versions.Add(new PaymentOrderVersion(id, (await Send(new GetPaymentOrder(id))).RowVersion));
        }

        return versions;
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A8")]
    public async Task A_batch_with_unapproved_orders_is_not_exported_and_a_reexport_needs_the_right_and_a_reason()
    {
        var (account, _) = await NewBank();
        var first = await ValidatedOrder(account, "1250.50", "FA-123");
        var second = await ValidatedOrder(account, "300", "FA-130");
        var batch = await Send(new SavePaymentBatch(null, account, [first, second], null));

        var notReady = await Assert.ThrowsAsync<ErpException>(() => Send(new ExportPaymentBatch(batch.Id, batch.RowVersion)));
        Assert.Equal("FIN_PAYMENT_BATCH_NOT_READY", notReady.Code);

        await Send(new ApprovePaymentOrders(await Versions(first, second)), Database.AccountantId);
        var file = await Send(new ExportPaymentBatch(batch.Id, batch.RowVersion));
        var xml = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(file.ContentBase64));
        Assert.Contains($"<EndToEndId>C{Database.CompanyId}-OP{first}</EndToEndId>", xml, StringComparison.Ordinal);
        Assert.Contains("<CtrlSum>1550.50</CtrlSum>", xml, StringComparison.Ordinal);
        var exported = await Send(new GetPaymentBatch(batch.Id));
        Assert.Equal((PaymentBatchStatus.Exported, 1, file.Sha256, PaymentFileFormat.Pain001V03),
            (exported.Status, exported.ExportCount, exported.FileSha256, exported.Format));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%OrdinPlataExportat' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"paymentBatchId\":{batch.Id},%" }));

        var noRight = await Assert.ThrowsAsync<ErpException>(() => Send(new ExportPaymentBatch(batch.Id, exported.RowVersion, "Banca a pierdut fișierul")));
        Assert.Equal(CoreErrorCodes.Forbidden, noRight.Code);
        Permissions.Add(FinancePermissions.PaymentOrderReExport);
        var noReason = await Assert.ThrowsAsync<ErpException>(() => Send(new ExportPaymentBatch(batch.Id, exported.RowVersion)));
        Assert.Equal("FIN_PAYMENT_ORDER_REEXPORT_REASON", noReason.Code);
        var again = await Send(new ExportPaymentBatch(batch.Id, exported.RowVersion, "Banca a pierdut fișierul"));
        var xmlAgain = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(again.ContentBase64));
        Assert.Contains($"<EndToEndId>C{Database.CompanyId}-OP{first}</EndToEndId>", xmlAgain, StringComparison.Ordinal);
        Assert.Equal(2, (await Send(new GetPaymentBatch(batch.Id))).ExportCount);

        // Retragerea aprobarii unui OP dintr-un lot exportat nu e permisa; descarcarea da ultimul fisier.
        var withdraw = await Assert.ThrowsAsync<ErpException>(async () => await Send(new UnapprovePaymentOrders(await Versions(first)), Database.AccountantId));
        Assert.Equal("FIN_PAYMENT_BATCH_NOT_READY", withdraw.Code);
        Assert.Equal(again.Sha256, (await Send(new GetPaymentBatchFile(batch.Id))).Sha256);
    }

    [Fact]
    public async Task An_order_is_in_one_batch_at_a_time_and_cancelling_a_batch_frees_its_orders()
    {
        var (account, _) = await NewBank();
        var order = await ValidatedOrder(account, "75", "FA-1");
        var batch = await Send(new SavePaymentBatch(null, account, [order], null));
        var taken = await Assert.ThrowsAsync<ErpException>(() => Send(new SavePaymentBatch(null, account, [order], null)));
        Assert.Equal("FIN_PAYMENT_BATCH_NOT_READY", taken.Code);

        var noReason = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new CancelPaymentBatch(batch.Id, "", batch.RowVersion)));
        Assert.Contains("reason", noReason.Errors.Keys);
        await Send(new CancelPaymentBatch(batch.Id, "Fișierul nu a fost încărcat la bancă.", batch.RowVersion));
        Assert.Null((await Send(new GetPaymentOrder(order))).PaymentBatchId);
        var next = await Send(new SavePaymentBatch(null, account, [order], null));
        Assert.Equal(1, (await Send(new GetPaymentBatch(next.Id))).OrderCount);

        var emptied = await Send(new SavePaymentBatch(next.Id, account, [], next.RowVersion));
        Assert.Equal(("0.00", 0), ((await Send(new GetPaymentBatch(next.Id))).TotalAmount, (await Send(new GetPaymentBatch(emptied.Id))).OrderCount));
    }

    [Fact]
    public async Task A_payment_returned_by_the_bank_rejects_the_order_which_can_then_be_reversed()
    {
        var (account, iban) = await NewBank();
        var order = await ValidatedOrder(account, "640", "FA-55");
        var detail = await Send(new GetPaymentOrder(order));
        var imported = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 1_000m,
            [new TestEntry(Day.AddDays(2), 640m, Unique("RET"), detail.BeneficiaryName, detail.BeneficiaryIban, "Retur: cont inchis", detail.EndToEndId)], "1")))
            .Statements);
        var line = (await Send(new GetBankStatement(imported.BankStatementId))).Lines.Single();

        var explained = await Send(new ExplainBankStatementLine(line.Id,
            [new BankLinePartInput(await OperationType("BANK-RESTITUIRE"), "640", null, null, null, null, null, null, null, null, null, order)], line.RowVersion));
        await Send(new ValidateBankStatementLines([new BankLineVersion(line.Id, explained.RowVersion)]));

        var rejected = await Send(new GetPaymentOrder(order));
        Assert.Equal((PaymentConfirmationStatus.Rejected, line.Id), (rejected.ConfirmationStatus, rejected.BankStatementLineId));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%OrdinPlataRespins' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"paymentOrderId\":{order},%" }));
        Assert.Equal(0, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%IncasareInregistrata' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"documentType\":\"Finance.BankStatementLine\",\"documentId\":{line.Id},%" }));
        var reversal = await Send(new ReversePaymentOrder(order, rejected.RowVersion));
        Assert.Equal("-640.00", (await Send(new GetPaymentOrder(reversal.Id))).Amount);
    }
}
