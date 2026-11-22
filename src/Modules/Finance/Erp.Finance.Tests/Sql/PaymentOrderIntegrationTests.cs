using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Tests.Bank;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Ordinele de plata pe SQL Server real (02.02, F2 3.6, 4.2, 4.2a; criteriile F2-A6, A8 partial, A17, A18, A20): validarea la legarea de
/// obiectul platii, patru ochi, confirmarea pe extras (si in transe) fara a doua plata, OP nou din extras, storno-ul.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PaymentOrderIntegrationTests : BankTestBase, IClassFixture<FinanceDatabase>
{
    public PaymentOrderIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.BankView, FinancePermissions.BankStatementImport, FinancePermissions.BankStatementReconcile,
            FinancePermissions.BankStatementValidate, FinancePermissions.BankStatementUnvalidate, FinancePermissions.PaymentOrderCreate,
            FinancePermissions.PaymentOrderValidate, FinancePermissions.PaymentOrderApprove, FinancePermissions.PaymentOrderReverse]);
    }

    /// <summary>Un partener nou (optional inactiv la ANAF) cu un cont bancar activ; intoarce partenerul, contul si IBAN-ul.</summary>
    private async Task<(int PartnerId, int BankAccountId, string Iban)> Supplier(bool anafInactive = false)
    {
        var iban = StatementFiles.RomanianIban("BTRL", $"{Random.Shared.NextInt64(1_000_000_000_000_000, 9_999_999_999_999_999)}");
        await using var connection = new SqlConnection(Database.ConnectionString);
        var partner = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive,
                AnafInactive, CreatedBy)
            VALUES (@Code, N'Furnizor Test SRL', 1, @FiscalCode, 0, 1, 1, N'Str. Fabricii 1', N'Brașov', N'BV', 'RO', 1, @Inactive, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("F-"), FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Inactive = anafInactive });
        var account = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.PartnerBankAccount (PartnerId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy)
            VALUES (@Partner, @Iban, N'Banca Transilvania', 'RON', 1, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Partner = partner, Iban = iban });
        return (partner, account, iban);
    }

    private async Task<FinanceSaved> Draft(int account, int partner, int bankAccount, string amount, string? invoice = "FA-123", int? id = null,
        byte[]? version = null, PaymentPurpose purpose = PaymentPurpose.Suppliers, string code = "PLATA-FURNIZOR") =>
        await Send(new SavePaymentOrder(id, account, Day, Day, false, purpose, partner, null, bankAccount, null, null, null, null, null, null,
            [new PaymentOrderLineInput(await OperationType(code), amount, invoice, null, null)], version));

    private async Task<(int Account, string Iban, int Partner, string PartnerIban, int OrderId, byte[] Version)> ValidatedOrder(string amount = "1000")
    {
        var (account, iban) = await NewBank();
        var (partner, bankAccount, partnerIban) = await Supplier();
        var draft = await Draft(account, partner, bankAccount, amount);
        var validated = await Send(new ValidatePaymentOrder(draft.Id, draft.RowVersion));
        return (account, iban, partner, partnerIban, draft.Id, validated.RowVersion);
    }

    private Task<int> Events(string type, string pattern) =>
        Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE @Type AND Payload LIKE @Pattern", new { Type = "%" + type, Pattern = pattern });

    [Fact]
    [Trait("Criteriu", "02.02-F2-A17")]
    public async Task An_order_is_validated_only_when_its_lines_are_linked_to_what_they_pay_and_then_publishes_the_payment()
    {
        var (account, _) = await NewBank();
        var (partner, bankAccount, partnerIban) = await Supplier();
        var draft = await Draft(account, partner, bankAccount, "1250.50", invoice: null);

        var notLinked = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidatePaymentOrder(draft.Id, draft.RowVersion)));
        Assert.Equal("FIN_PAYMENT_ORDER_NOT_LINKED", notLinked.Code);

        var linked = await Draft(account, partner, bankAccount, "1250.50", "FA-123", draft.Id, draft.RowVersion);
        await Send(new ValidatePaymentOrder(draft.Id, linked.RowVersion));
        var order = await Send(new GetPaymentOrder(draft.Id));
        Assert.Equal((PaymentOrderStatus.Validated, PaymentConfirmationStatus.Unconfirmed), (order.Status, order.ConfirmationStatus));
        Assert.StartsWith("OP", order.Number, StringComparison.Ordinal);
        Assert.Equal((partnerIban, "Furnizor Test SRL", false), (order.BeneficiaryIban, order.BeneficiaryName, order.IsFreeIban));
        Assert.Equal(("Plata fact. FA-123", $"C{Database.CompanyId}-OP{draft.Id}"), (order.RemittanceInfo, order.EndToEndId));
        Assert.Equal(1, await Events("PlataInregistrata", $"%\"documentType\":\"Finance.PaymentOrder\",\"documentId\":{draft.Id},%"));

        var locked = await Assert.ThrowsAsync<ErpException>(() => Draft(account, partner, bankAccount, "1", "FA-1", draft.Id, order.RowVersion));
        Assert.Equal("FIN_PAYMENT_ORDER_NOT_DRAFT", locked.Code);
        Assert.Contains(await Send(new GetPaymentOrders(account, Unconfirmed: true)), o => o.Id == draft.Id && !o.IsApproved);
    }

    [Fact]
    public async Task The_iban_comes_from_the_partner_file_unless_free_ibans_are_allowed_and_budget_payments_need_their_references()
    {
        var (account, _) = await NewBank();
        var type = await OperationType("PLATA-DIVERSE");
        var valid = StatementFiles.RomanianIban("RNCB", "0082004530120001");
        var spaced = string.Join(' ', Enumerable.Range(0, 6).Select(i => valid.Substring(i * 4, 4)));
        var free = new SavePaymentOrder(null, account, Day, Day, false, PaymentPurpose.Other, null, null, null, "Chiriaș Ion", spaced,
            null, null, null, null, [new PaymentOrderLineInput(type, "100", null, null, null)], null);
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(free));
        Assert.Equal("FIN_PAYMENT_ORDER_IBAN_INVALID", refused.Code);

        Permissions.Add(FinancePermissions.PaymentOrderFreeIban);
        var invalid = await Assert.ThrowsAsync<ErpValidationException>(() => Send(free with { BeneficiaryIban = "RO00RNCB0082004530120001" }));
        Assert.Contains("beneficiaryIban", invalid.Errors.Keys);
        var saved = await Send(free);
        var freeOrder = await Send(new GetPaymentOrder(saved.Id));
        Assert.Equal((valid, true), (freeOrder.BeneficiaryIban, freeOrder.IsFreeIban));

        var budget = await Send(new SavePaymentOrder(null, account, Day, Day, false, PaymentPurpose.StateBudget, null, null, null, "Trezoreria Statului",
            valid, null, null, null, null, [new PaymentOrderLineInput(await OperationType("BANK-PLATA-BUGET"), "500", null, null, null)], null));
        var missing = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidatePaymentOrder(budget.Id, budget.RowVersion)));
        Assert.Equal("FIN_PAYMENT_ORDER_BUDGET_REF_MISSING", missing.Code);
    }

    [Fact]
    public async Task Paying_a_partner_that_is_inactive_at_anaf_needs_an_explicit_confirmation()
    {
        var (account, _) = await NewBank();
        var (partner, bankAccount, _) = await Supplier(anafInactive: true);
        var draft = await Draft(account, partner, bankAccount, "300");

        var unconfirmed = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidatePaymentOrder(draft.Id, draft.RowVersion)));
        Assert.Equal("FIN_PAYMENT_ORDER_ANAF_INACTIVE_UNCONFIRMED", unconfirmed.Code);
        await Send(new ValidatePaymentOrder(draft.Id, draft.RowVersion, ConfirmAnafInactive: true));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM fin.PaymentOrder WHERE Id = @Id AND AnafInactiveConfirmed = 1", new { draft.Id }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A8")]
    public async Task Whoever_validated_an_order_cannot_approve_it_while_four_eyes_is_on()
    {
        var (_, _, _, _, orderId, version) = await ValidatedOrder();

        var self = await Assert.ThrowsAsync<ErpException>(() => Send(new ApprovePaymentOrders([new PaymentOrderVersion(orderId, version)])));
        Assert.Equal("FIN_PAYMENT_ORDER_SELF_APPROVAL", self.Code);

        var approved = Assert.Single(await Send(new ApprovePaymentOrders([new PaymentOrderVersion(orderId, version)]), Database.AccountantId));
        var order = await Send(new GetPaymentOrder(orderId));
        Assert.NotNull(order.ApprovedAt);
        var withdrawn = Assert.Single(await Send(new UnapprovePaymentOrders([approved]), Database.AccountantId));
        Assert.Null((await Send(new GetPaymentOrder(orderId))).ApprovedAt);

        // Cu patru ochi oprit pe firma, acelasi utilizator poate aproba.
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            await connection.ExecuteAsync("""
                MERGE fin.CompanySetting AS t USING (SELECT @Company AS CompanyId) AS s ON t.CompanyId = s.CompanyId AND t.Code = 'Fin.PaymentOrder.RequireApproval'
                WHEN NOT MATCHED THEN INSERT (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, 'Fin.PaymentOrder.RequireApproval', N'false', 1);
                """, new { Company = Database.CompanyId });
        }

        try
        {
            Assert.Single(await Send(new ApprovePaymentOrders([withdrawn])));
        }
        finally
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.ExecuteAsync("DELETE FROM fin.CompanySetting WHERE CompanyId = @Company", new { Company = Database.CompanyId });
        }
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A6")]
    public async Task A_validated_order_found_on_the_statement_is_confirmed_without_publishing_the_payment_again()
    {
        var (account, iban, partner, partnerIban, orderId, _) = await ValidatedOrder("1000");
        var order = await Send(new GetPaymentOrder(orderId));
        var imported = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 5_000m,
            [new TestEntry(Day.AddDays(1), -1000m, Unique("OPX"), "Furnizor Test SRL", partnerIban, order.RemittanceInfo, order.EndToEndId)], "1"))).Statements);
        var line = (await Send(new GetBankStatement(imported.BankStatementId))).Lines.Single();
        Assert.Equal(order.EndToEndId, line.EndToEndId);

        var explained = await Send(new ExplainBankStatementLine(line.Id,
            [new BankLinePartInput(await OperationType("PLATA-FURNIZOR"), "-1000", partner, null, "FA-123", null, null, null, null, null, null, orderId)],
            line.RowVersion));
        var validated = Assert.Single((await Send(new ValidateBankStatementLines([new BankLineVersion(line.Id, explained.RowVersion)]))));

        var confirmed = await Send(new GetPaymentOrder(orderId));
        Assert.Equal((PaymentConfirmationStatus.Confirmed, line.Id), (confirmed.ConfirmationStatus, confirmed.BankStatementLineId));
        Assert.Equal(1, await Events("PlataInregistrata", $"%\"documentType\":\"Finance.PaymentOrder\",\"documentId\":{orderId},%"));
        Assert.Equal(0, await Events("PlataInregistrata", $"%\"documentType\":\"Finance.BankStatementLine\",\"documentId\":{line.Id},%"));
        Assert.Equal(1, await Events("OrdinPlataConfirmat", $"%\"paymentOrderId\":{orderId},%"));

        // Un OP confirmat nu se storneaza; dupa desfacerea validarii liniei redevine Neconfirmat si se poate storna.
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new ReversePaymentOrder(orderId, confirmed.RowVersion)));
        Assert.Equal("FIN_PAYMENT_ORDER_ALREADY_CONFIRMED", refused.Code);
        await Send(new UnvalidateBankStatementLine(line.Id, "OP greșit", validated.RowVersion));
        var unconfirmed = await Send(new GetPaymentOrder(orderId));
        Assert.Equal(PaymentConfirmationStatus.Unconfirmed, unconfirmed.ConfirmationStatus);
        var reversal = await Send(new ReversePaymentOrder(orderId, unconfirmed.RowVersion));
        var reversed = await Send(new GetPaymentOrder(reversal.Id));
        Assert.Equal(("-1000.00", orderId, PaymentOrderStatus.Validated), (reversed.Amount, reversed.ReversalOfId, reversed.Status));
        Assert.Equal(PaymentOrderStatus.Reversed, (await Send(new GetPaymentOrder(orderId))).Status);
        Assert.Equal(1, await Events("DocumentTrezorerieStornat", $"%\"reversalDocumentId\":{reversal.Id},%"));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A20")]
    public async Task An_order_executed_in_two_tranches_is_confirmed_only_after_the_second_line()
    {
        var (account, iban, partner, partnerIban, orderId, _) = await ValidatedOrder("10000");
        var prefix = Unique("TR");
        var imported = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 50_000m,
        [
            new TestEntry(Day.AddDays(1), -6000m, $"{prefix}-1", "Furnizor Test SRL", partnerIban, "FA-123 transa 1"),
            new TestEntry(Day.AddDays(2), -4000m, $"{prefix}-2", "Furnizor Test SRL", partnerIban, "FA-123 transa 2"),
            new TestEntry(Day.AddDays(2), -1m, $"{prefix}-3", "Furnizor Test SRL", partnerIban, "in plus"),
        ], "1"))).Statements);
        var lines = (await Send(new GetBankStatement(imported.BankStatementId))).Lines;
        var type = await OperationType("PLATA-FURNIZOR");
        BankLinePartInput Part(string amount) => new(type, amount, partner, null, "FA-123", null, null, null, null, null, null, orderId);

        var first = await Send(new ExplainBankStatementLine(lines[0].Id, [Part("-6000")], lines[0].RowVersion));
        await Send(new ValidateBankStatementLines([new BankLineVersion(lines[0].Id, first.RowVersion)]));
        Assert.Equal(PaymentConfirmationStatus.Unconfirmed, (await Send(new GetPaymentOrder(orderId))).ConfirmationStatus);

        var second = await Send(new ExplainBankStatementLine(lines[1].Id, [Part("-4000")], lines[1].RowVersion));
        var over = await Assert.ThrowsAsync<ErpException>(() => Send(new ExplainBankStatementLine(lines[2].Id, [Part("-1")], lines[2].RowVersion)));
        Assert.Equal("FIN_BANK_LINE_PAYMENT_ORDER_TAKEN", over.Code);
        await Send(new ValidateBankStatementLines([new BankLineVersion(lines[1].Id, second.RowVersion)]));
        var order = await Send(new GetPaymentOrder(orderId));
        Assert.Equal((PaymentConfirmationStatus.Confirmed, lines[1].Id), (order.ConfirmationStatus, order.BankStatementLineId));
        Assert.Equal(1, await Events("OrdinPlataConfirmat", $"%\"paymentOrderId\":{orderId},%"));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A18")]
    public async Task A_new_order_from_a_statement_line_is_validated_and_confirmed_in_the_same_step_without_approval()
    {
        var (account, iban) = await NewBank();
        var (partner, bankAccount, partnerIban) = await Supplier();
        var imported = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 9_000m,
            [new TestEntry(Day, -2400m, Unique("NOP"), "Furnizor Test SRL", partnerIban, "Plata FA-77 din internet banking")], "1"))).Statements);
        var line = (await Send(new GetBankStatement(imported.BankStatementId))).Lines.Single();
        var type = await OperationType("PLATA-FURNIZOR");

        var mismatch = await Assert.ThrowsAsync<ErpException>(() => Send(new CreatePaymentOrderFromStatementLine(line.Id, line.RowVersion, PaymentPurpose.Suppliers,
            partner, null, bankAccount, null, null, [new PaymentOrderLineInput(type, "2000", "FA-77", null, null)])));
        Assert.Equal("FIN_BANK_LINE_PARTS_MISMATCH", mismatch.Code);

        var created = await Send(new CreatePaymentOrderFromStatementLine(line.Id, line.RowVersion, PaymentPurpose.Suppliers, partner, null, bankAccount, null, null,
            [new PaymentOrderLineInput(type, "2000", "FA-77", null, null), new PaymentOrderLineInput(type, "400", "FA-78", null, null)]));

        var order = await Send(new GetPaymentOrder(created.Id));
        Assert.Equal((PaymentOrderOrigin.BankStatement, PaymentOrderStatus.Validated, PaymentConfirmationStatus.Confirmed, line.Id, Day),
            (order.Origin, order.Status, order.ConfirmationStatus, order.BankStatementLineId, order.DocumentDate));
        Assert.Null(order.ApprovedAt);
        var after = await Send(new GetBankStatementLine(line.Id));
        Assert.Equal(BankLineMatchStatus.Validated, after.MatchStatus);
        Assert.All(after.Parts, p => Assert.Equal(created.Id, p.PaymentOrderId));
        Assert.Equal(1, await Events("PlataInregistrata", $"%\"documentType\":\"Finance.PaymentOrder\",\"documentId\":{created.Id},%"));
        Assert.Equal(0, await Events("PlataInregistrata", $"%\"documentType\":\"Finance.BankStatementLine\",\"documentId\":{line.Id},%"));
        Assert.Equal(1, await Events("OrdinPlataConfirmat", $"%\"paymentOrderId\":{created.Id},%"));
    }
}
