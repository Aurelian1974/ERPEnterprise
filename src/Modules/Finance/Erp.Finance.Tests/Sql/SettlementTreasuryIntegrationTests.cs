using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Domain.Treasury;
using Erp.Finance.Tests.Bank;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Legarea platilor din casa si banca de decontari (02.02, F1 4.1 pasul 5, F2 4.2 / 4.4, F3 4.2): chitanta, OP-ul si linia de extras sting
/// facturile indicate (SettlementRef „OI:id”) in tranzactia lor, stornarea le redeschide, iar avansurile raman nealocate.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SettlementTreasuryIntegrationTests : BankTestBase, IClassFixture<FinanceDatabase>
{
    private static long invoiceSequence = DateTime.UtcNow.Ticks % 1_000_000_000 + 500_000_000;

    public SettlementTreasuryIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.CashView, FinancePermissions.CashCreate, FinancePermissions.CashValidate, FinancePermissions.CashReverse,
            FinancePermissions.BankView, FinancePermissions.BankStatementImport, FinancePermissions.BankStatementReconcile,
            FinancePermissions.BankStatementValidate, FinancePermissions.BankStatementUnvalidate, FinancePermissions.PaymentOrderCreate,
            FinancePermissions.PaymentOrderValidate, FinancePermissions.PaymentOrderReverse, FinancePermissions.PartnerBalanceView]);
    }

    private async Task<(int Partner, int BankAccount, string Iban)> NewPartner()
    {
        var iban = StatementFiles.RomanianIban("BTRL", $"{Random.Shared.NextInt64(1_000_000_000_000_000, 9_999_999_999_999_999)}");
        await using var connection = new SqlConnection(Database.ConnectionString);
        var partner = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Partener Plăți SRL', 1, @FiscalCode, 1, 1, 1, N'Str. Mică 2', N'Sibiu', N'SB', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("PP-"), FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var account = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.PartnerBankAccount (PartnerId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy)
            VALUES (@Partner, @Iban, N'Banca Transilvania', 'RON', 1, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Partner = partner, Iban = iban });
        return (partner, account, iban);
    }

    private Task<long> Sale(int partner, decimal total) =>
        InTransaction(sp => sp.GetRequiredService<InvoiceOpenItems>().RegisterAsync(new FacturaEmisa(Interlocked.Increment(ref invoiceSequence), partner,
            $"FV-{invoiceSequence}", Day, Day, "RON", 1m, total, total, 0, 0, false, false, null, false, [new InvoiceDueDate(Day.AddDays(15), total)], null, null,
            EInvoiceStatus.Accepted, null), Ct));

    private Task<long> Purchase(int partner, decimal total, EInvoiceStatus status = EInvoiceStatus.ReceivedFromSpv) =>
        InTransaction(sp => sp.GetRequiredService<InvoiceOpenItems>().RegisterAsync(new FacturaFurnizorValidata(Interlocked.Increment(ref invoiceSequence),
            partner, $"FF-{invoiceSequence}", Day, Day, "RON", 1m, total, total, 0, 0, false, false, null, false, [new InvoiceDueDate(Day.AddDays(30), total)],
            null, null, status, null), Ct));

    private Task<decimal> Open(long id) => Query<decimal>("SELECT OpenAmount FROM fin.OpenItem WHERE Id = @Id", new { Id = id });

    private async Task<int> NewCash() =>
        (await Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Cash, Unique("CASA-"), "Casa decontări", Database.HeadOfficeId, "RON", null, null,
            Database.CashierId, null, null, null, null, CashCloseMode.OnDemand, null, true, [new TreasuryCashierInput(Database.CashierId, true, null, null)], null)))
        .Id;

    [Fact]
    [Trait("Criteriu", "02.02-F3-A3")]
    public async Task A_cash_receipt_settles_the_indicated_invoice_and_its_reversal_reopens_it()
    {
        var (partner, _, _) = await NewPartner();
        var invoice = await Sale(partner, 300m);
        var cash = await NewCash();
        var draft = await Send(new SaveCashDocument(null, cash, CashDocumentKind.Receipt, Day, partner, null, null, null, null, null,
            [new CashLineInput(await OperationType("INC-CLIENT"), "300", null, TreasurySettlement.Reference([invoice]), null, null, null)], null), Database.CashierId);
        var validated = await Send(new ValidateCashDocument(draft.Id, draft.RowVersion), Database.CashierId);
        Assert.Equal(0m, await Open(invoice));

        await Send(new ReverseCashDocument(draft.Id, validated.RowVersion), Database.CashierId);
        Assert.Equal(300m, await Open(invoice));
        Assert.Equal(0m, Assert.Single(await Send(new GetPartnerBalance(partner, PartnerRole.Customer, Day.AddDays(60)))).Balance - 300m);
    }

    [Fact]
    public async Task A_customer_advance_stays_unallocated_until_an_invoice_consumes_it()
    {
        var (partner, _, _) = await NewPartner();
        var older = await Sale(partner, 100m);
        var cash = await NewCash();
        var draft = await Send(new SaveCashDocument(null, cash, CashDocumentKind.Receipt, Day, partner, null, null, null, null, null,
            [new CashLineInput(await OperationType("INC-AVANS-CLIENT"), "250", null, null, null, null, null)], null), Database.CashierId);
        await Send(new ValidateCashDocument(draft.Id, draft.RowVersion), Database.CashierId);
        Assert.Equal(100m, await Open(older)); // avansul nu stinge factura veche

        var newer = await Sale(partner, 400m);
        Assert.Equal(150m, await Open(newer)); // factura noua consuma avansul (FIFO, 4.1 pasul 3)
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A8")]
    public async Task A_payment_order_settles_the_supplier_invoice_with_the_spv_warning_and_its_reversal_reopens_it()
    {
        var (account, _) = await NewBank();
        var (supplier, bankAccount, _) = await NewPartner();
        var invoice = await Purchase(supplier, 800m, EInvoiceStatus.NotReceivedFromSpv);
        var draft = await Send(new SavePaymentOrder(null, account, Day, Day, false, PaymentPurpose.Suppliers, supplier, null, bankAccount, null, null, null, null,
            null, null, [new PaymentOrderLineInput(await OperationType("PLATA-FURNIZOR"), "800", TreasurySettlement.Reference([invoice]), null, null)], null));

        var warning = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidatePaymentOrder(draft.Id, draft.RowVersion)));
        Assert.Equal("FIN_OPEN_ITEM_NOT_IN_SPV_UNCONFIRMED", warning.Code);
        Assert.Equal(-800m, await Open(invoice)); // tranzactia OP-ului s-a anulat cu totul
        var validated = await Send(new ValidatePaymentOrder(draft.Id, draft.RowVersion, ConfirmWarnings: true));
        Assert.Equal(0m, await Open(invoice));

        await Send(new ReversePaymentOrder(draft.Id, validated.RowVersion));
        Assert.Equal(-800m, await Open(invoice));
    }

    [Fact]
    public async Task A_bank_receipt_settles_the_invoice_and_unvalidating_then_revalidating_the_line_keeps_one_settlement()
    {
        var (partner, _, partnerIban) = await NewPartner();
        var invoice = await Sale(partner, 1200m);
        var (account, iban) = await NewBank();
        var imported = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 0m,
            [new TestEntry(Day, 1200m, Unique("IN"), "Partener Plăți SRL", partnerIban, "plata factura")], "1"))).Statements);
        var line = Assert.Single((await Send(new GetBankStatement(imported.BankStatementId))).Lines);

        var explained = await Send(new ExplainBankStatementLine(line.Id, [new BankLinePartInput(await OperationType("INC-CLIENT"), "1200", partner, null,
            TreasurySettlement.Reference([invoice]), null, null, null, null, null, null)], line.RowVersion));
        var validated = Assert.Single(await Send(new ValidateBankStatementLines([new BankLineVersion(line.Id, explained.RowVersion)])));
        Assert.True(validated.Validated);
        Assert.Equal(0m, await Open(invoice));

        var undone = await Send(new UnvalidateBankStatementLine(line.Id, "Linie validată greșit", validated.RowVersion!));
        Assert.Equal(1200m, await Open(invoice));

        var again = Assert.Single(await Send(new ValidateBankStatementLines([new BankLineVersion(line.Id, undone.RowVersion)])));
        Assert.True(again.Validated);
        Assert.Equal(0m, await Open(invoice));
        Assert.Equal(0m, await Query<decimal>("SELECT SUM(OpenAmount) FROM fin.OpenItem WHERE SourceDocumentType = 'Finance.BankStatementLine' AND SourceDocumentId = @Id",
            new { line.Id }));
        Assert.Equal(1200m, -await Query<decimal>(
            "SELECT SUM(Amount) FROM fin.OpenItem WHERE SourceDocumentType = 'Finance.BankStatementLine' AND SourceDocumentId = @Id", new { line.Id }));
    }
}
