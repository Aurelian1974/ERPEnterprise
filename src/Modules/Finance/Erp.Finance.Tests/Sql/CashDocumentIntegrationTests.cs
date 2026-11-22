using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Treasury;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Documentele de casa pe SQL Server real (02.02, F1 3.3, 3.4, 3.6, 4.1; criteriile F1-A1…A6, A9, A13): numerotarea fara goluri, soldul
/// nenegativ in toate zilele ulterioare, serializarea validarilor pe casa, plafoanele pe firma si persoana, ziua inchisa, stornarea in ziua
/// curenta si evenimentele din outbox. Fiecare test lucreaza pe casa lui; casiera Elena Dumitru e atribuita cu drept de validare.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CashDocumentIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Day1 = new(2026, 11, 2);
    private static readonly DateOnly Day2 = new(2026, 11, 3);
    private static readonly DateOnly Day3 = new(2026, 11, 4);

    public CashDocumentIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.CashView, FinancePermissions.CashCreate, FinancePermissions.CashValidate, FinancePermissions.CashReverse]);
    }

    private int Cashier => Database.CashierId;

    private async Task<int> NewCash(int? companyId = null, int? branchId = null)
    {
        var saved = await Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Cash, Unique("CASA-"), "Casa de test", branchId ?? Database.HeadOfficeId, "RON",
            null, null, Database.CashierId, null, null, null, null, CashCloseMode.OnDemand, null, true,
            [new TreasuryCashierInput(Database.CashierId, true, null, null)], null), companyId: companyId);
        return saved.Id;
    }

    private async Task<int> OperationType(string code) => await Query<int>("SELECT Id FROM fin.TreasuryOperationType WHERE Code = @Code", new { Code = code });

    private async Task<FinanceSaved> Draft(int account, CashDocumentKind kind, DateOnly day, string operation, string amount, int? partnerId = null,
        int? employeeId = null, string? personName = null, int? counterAccount = null, int? companyId = null) =>
        await Send(new SaveCashDocument(null, account, kind, day, partnerId, employeeId, personName, personName is null ? null : "CI XB 123456", null, null,
            [new CashLineInput(await OperationType(operation), amount, counterAccount, null, null, null, null)], null), Cashier, companyId);

    private async Task<CashValidatedDto> Validate(FinanceSaved draft, bool confirm = false, int? companyId = null) =>
        await Send(new ValidateCashDocument(draft.Id, draft.RowVersion, confirm), Cashier, companyId);

    private async Task<CashValidatedDto> CashIn(int account, DateOnly day, string amount) =>
        await Validate(await Draft(account, CashDocumentKind.CashIn, day, "INC-DIVERSE", amount));

    private async Task<CashValidatedDto> CashOut(int account, DateOnly day, string amount) =>
        await Validate(await Draft(account, CashDocumentKind.CashOut, day, "PLATA-DIVERSE", amount));

    private Task<decimal> Closing(int account, DateOnly day) =>
        Query<decimal>("SELECT ClosingBalance FROM fin.CashRegisterDay WHERE TreasuryAccountId = @Account AND [Day] = @Day",
            new { Account = account, Day = day.ToDateTime(TimeOnly.MinValue) });

    [Fact]
    [Trait("Criteriu", "02.02-F1-A1")]
    public async Task Validated_receipts_take_the_next_series_number_without_gaps_even_after_a_rolled_back_validation()
    {
        var account = await NewCash();
        await CashIn(account, Day1, "100");
        var first = await Validate(await Draft(account, CashDocumentKind.Receipt, Day1, "INC-CLIENT", "200", Database.LegalPartnerId));
        var firstPayment = await CashOut(account, Day1, "20");

        // O plata care ar face soldul negativ: validarea face rollback, inclusiv alocarea numarului din seria platilor.
        var refused = await Assert.ThrowsAsync<ErpException>(() => CashOut(account, Day1, "1000"));
        Assert.Equal("FIN_CASH_NEGATIVE_BALANCE", refused.Code);
        var paid = await CashOut(account, Day1, "30");
        var second = await Validate(await Draft(account, CashDocumentKind.Receipt, Day1, "INC-CLIENT", "10", Database.LegalPartnerId));

        Assert.StartsWith("CH-2026-", first.Number, StringComparison.Ordinal);
        Assert.Equal(Next(first.Number!), second.Number);
        Assert.Equal(Next(firstPayment.Number!), paid.Number);
        Assert.Equal(260m, await Closing(account, Day1));
    }

    private static string Next(string number)
    {
        var parts = number.Split('-');
        return $"{parts[0]}-{parts[1]}-{(long.Parse(parts[2]) + 1).ToString(new string('0', parts[2].Length))}";
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A2")]
    public async Task A_payment_that_would_make_a_later_open_day_negative_is_refused()
    {
        var account = await NewCash();
        await CashIn(account, Day1, "500");
        await CashOut(account, Day3, "400");

        // Pe 3 noiembrie ziua ar avea 500 - 300 = 200, dar pe 4 noiembrie ar ramane 200 - 400 = -200.
        var refused = await Assert.ThrowsAsync<ErpException>(() => CashOut(account, Day2, "300"));
        Assert.Equal("FIN_CASH_NEGATIVE_BALANCE", refused.Code);
        Assert.Contains("04.11.2026", refused.Message, StringComparison.Ordinal);
        Assert.Equal(100m, await Closing(account, Day3));

        await CashOut(account, Day2, "100");
        Assert.Equal((400m, 0m), (await Closing(account, Day2), await Closing(account, Day3)));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A3")]
    public async Task Ten_simultaneous_validations_on_the_same_cash_leave_the_day_balance_equal_to_the_sum()
    {
        var account = await NewCash();
        var drafts = new List<FinanceSaved>();
        for (var i = 1; i <= 10; i++)
        {
            drafts.Add(await Draft(account, CashDocumentKind.CashIn, Day1, "INC-DIVERSE", $"{i}.10"));
        }

        await Task.WhenAll(drafts.Select(d => Validate(d)));

        Assert.Equal(56m, await Closing(account, Day1));
        Assert.Equal(10, await Query<int>("SELECT COUNT(DISTINCT Number) FROM fin.CashDocument WHERE TreasuryAccountId = @Account AND Status = 2",
            new { Account = account }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A4")]
    public async Task Cash_receipts_from_the_same_legal_person_are_limited_per_company_and_day_by_the_rule_in_force()
    {
        var company = Database.LimitCompanyId;
        var first = await NewCash(company, Database.LimitBranchId);
        var second = await NewCash(company, Database.LimitBranchId);

        // Plafonul legal de 5.000 lei: 3.000 la o casa, apoi 3.000 la alta casa a aceleiasi firme, in aceeasi zi = 6.000 > 5.000.
        await Validate(await Draft(first, CashDocumentKind.Receipt, Day1, "INC-CLIENT", "3000", Database.LegalPartnerId, companyId: company), companyId: company);
        var checks = await Query<int>("SELECT COUNT(*) FROM fin.CashLimitCheck WHERE CompanyId = @Company AND Confirmed = 0", new { Company = company });
        var blocked = await Assert.ThrowsAsync<ErpException>(async () =>
            await Validate(await Draft(second, CashDocumentKind.Receipt, Day1, "INC-CLIENT", "3000", Database.LegalPartnerId, companyId: company),
                companyId: company));
        Assert.Equal("FIN_CASH_LIMIT_EXCEEDED", blocked.Code);
        // Refuzul ramane in jurnal, desi tranzactia validarii s-a anulat.
        Assert.Equal(checks + 1, await Query<int>("SELECT COUNT(*) FROM fin.CashLimitCheck WHERE CompanyId = @Company AND Confirmed = 0",
            new { Company = company }));

        // Din 15.11.2026 firma are un plafon propriu mai strict, de 2.000 lei (alt ValidFrom): 1.500 + 1.500 e refuzat, 1.500 singur nu.
        await Validate(await Draft(first, CashDocumentKind.Receipt, new DateOnly(2026, 11, 16), "INC-CLIENT", "1500", Database.LegalPartnerId,
            companyId: company), companyId: company);
        var stricter = await Assert.ThrowsAsync<ErpException>(async () =>
            await Validate(await Draft(second, CashDocumentKind.Receipt, new DateOnly(2026, 11, 16), "INC-CLIENT", "1500", Database.LegalPartnerId,
                companyId: company), companyId: company));
        Assert.Equal("FIN_CASH_LIMIT_EXCEEDED", stricter.Code);
        Assert.Contains("2.000", stricter.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A5")]
    public async Task A_warning_limit_must_be_confirmed_and_the_confirmation_is_audited()
    {
        var company = Database.LimitCompanyId;
        var account = await NewCash(company, Database.LimitBranchId);
        var day = new DateOnly(2026, 11, 20);
        var draft = await Draft(account, CashDocumentKind.Receipt, day, "INC-CLIENT", "150", personName: "Popescu Maria", companyId: company);

        var unconfirmed = await Assert.ThrowsAsync<ErpException>(() => Validate(draft, companyId: company));
        Assert.Equal("FIN_CASH_LIMIT_WARNING_UNCONFIRMED", unconfirmed.Code);

        var validated = await Validate(draft, confirm: true, companyId: company);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM fin.CashLimitCheck WHERE CashDocumentId = @Id AND Confirmed = 1 AND Action = 2",
            new { Id = validated.CashDocumentId }));
        var changes = await Query<string>("""
            SELECT TOP (1) Changes FROM core.AuditEntry WHERE Action = N'Fin.Cash.Validate' AND EntityId = @Id ORDER BY Id DESC
            """, new { Id = validated.CashDocumentId });
        Assert.Contains("IncasarePF", changes, StringComparison.Ordinal);
        // Actul de identitate e criptat, nu in clar.
        Assert.Equal(0, await Query<int>("""
            SELECT COUNT(*) FROM fin.CashDocument WHERE Id = @Id AND CAST(PersonIdEncrypted AS varbinary(max)) LIKE CAST('%123456%' AS varbinary(max))
            """, new { Id = validated.CashDocumentId }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A6")]
    public async Task A_document_dated_in_a_closed_day_is_refused()
    {
        var account = await NewCash();
        await CashIn(account, Day1, "100");
        await Execute("UPDATE fin.CashRegisterDay SET Status = 2 WHERE TreasuryAccountId = @Account", new { Account = account });

        var closed = await Assert.ThrowsAsync<ErpException>(() => CashIn(account, Day1, "10"));
        Assert.Equal("FIN_CASH_DAY_CLOSED", closed.Code);
        // O zi anterioara zilei inchise nu mai primeste documente: ar schimba soldul de inceput al zilei inchise.
        var earlier = await Assert.ThrowsAsync<ErpException>(() => CashIn(account, new DateOnly(2026, 11, 1), "10"));
        Assert.Equal("FIN_CASH_DAY_CLOSED", earlier.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A9")]
    public async Task Reversing_a_document_of_a_closed_day_creates_a_negative_document_today_and_leaves_the_old_day_unchanged()
    {
        var account = await NewCash();
        var original = await CashIn(account, Day1, "300");
        await Execute("UPDATE fin.CashRegisterDay SET Status = 2 WHERE TreasuryAccountId = @Account AND [Day] = @Day",
            new { Account = account, Day = Day1.ToDateTime(TimeOnly.MinValue) });

        var reversal = await Send(new ReverseCashDocument(original.CashDocumentId, original.RowVersion), Cashier);

        var today = BusinessDay.Today(TimeProvider.System);
        Assert.Equal(300m, await Closing(account, Day1));
        Assert.Equal(0m, await Closing(account, today));
        var detail = await Send(new GetCashDocument(reversal.CashDocumentId), Cashier);
        Assert.Equal((today, "-300.00", original.CashDocumentId, CashDocumentStatus.Validated), (detail.DocumentDate, detail.TotalAmount, detail.ReversalOfId,
            detail.Status));
        var before = await Send(new GetCashDocument(original.CashDocumentId), Cashier);
        Assert.Equal((CashDocumentStatus.Reversed, reversal.CashDocumentId), (before.Status, before.ReversedById));

        var again = await Assert.ThrowsAsync<ErpException>(() => Send(new ReverseCashDocument(before.Id, before.RowVersion), Cashier));
        Assert.Equal("FIN_CASH_ALREADY_REVERSED", again.Code);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%DocumentTrezorerieStornat' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"reversalDocumentId\":{reversal.CashDocumentId}%" }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A13")]
    public async Task Validation_writes_the_receipt_event_with_its_lines_to_the_outbox()
    {
        var account = await NewCash();
        var validated = await Validate(await Draft(account, CashDocumentKind.Receipt, Day1, "INC-CLIENT", "123.45", Database.LegalPartnerId));

        var payload = await Query<string>("SELECT Payload FROM core.Outbox WHERE EventType LIKE N'%IncasareInregistrata' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"documentId\":{validated.CashDocumentId},%" });
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        Assert.Equal(("Finance.Receipt", validated.Number, 123.45m, Database.LegalPartnerId), (root.GetProperty("documentType").GetString(),
            root.GetProperty("number").GetString(), root.GetProperty("totalAmountRon").GetDecimal(), root.GetProperty("partnerId").GetInt32()));
        Assert.Equal("INC-CLIENT", root.GetProperty("lines")[0].GetProperty("operationTypeCode").GetString());
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.3")]
    public async Task Drafts_follow_the_document_rules_and_only_assigned_cashiers_work_on_a_cash()
    {
        var account = await NewCash();
        var mismatch = await Assert.ThrowsAsync<ErpException>(() => Draft(account, CashDocumentKind.CashIn, Day1, "PLATA-DIVERSE", "10"));
        Assert.Equal("FIN_CASH_LINE_DIRECTION_MISMATCH", mismatch.Code);

        var notAssigned = await Assert.ThrowsAsync<ErpException>(async () => await Send(new SaveCashDocument(null, account, CashDocumentKind.CashIn, Day1,
            null, null, null, null, null, null, [new CashLineInput(await OperationType("INC-DIVERSE"), "10", null, null, null, null, null)], null),
            Database.AccountantId));
        Assert.Equal("FIN_CASHIER_NOT_ASSIGNED", notAssigned.Code);

        var draft = await Draft(account, CashDocumentKind.CashIn, Day1, "INC-DIVERSE", "10");
        await Send(new DeleteCashDocument(draft.Id, draft.RowVersion), Cashier);
        Assert.Equal(0, await Query<int>("SELECT COUNT(*) FROM fin.CashDocument WHERE Id = @Id", new { draft.Id }));

        var validated = await CashIn(account, Day1, "10");
        var notDraft = await Assert.ThrowsAsync<ErpException>(() => Send(new DeleteCashDocument(validated.CashDocumentId, validated.RowVersion), Cashier));
        Assert.Equal("FIN_CASH_DOCUMENT_NOT_DRAFT", notDraft.Code);

        var desk = await Send(new GetCashDesk(account, Day1), Cashier);
        Assert.Equal(("10.00", "0.00", "10.00"), (desk.Day!.TotalIn, desk.Day.TotalOut, desk.Day.ClosingBalance));
        Assert.Contains(desk.Lookups.OperationTypes, t => t.Code == "PLATA-DEPUNERE-BANCA" && t.RequiresCounterAccount);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.3")]
    public async Task The_opening_balance_comes_once_before_any_other_document()
    {
        var account = await NewCash();
        var opening = await Send(new SaveCashDocument(null, account, CashDocumentKind.OpeningBalance, new DateOnly(2026, 10, 31), null, null, null, null,
            "Sold la pornire", "1500.50", [], null), Cashier);
        var validated = await Validate(opening);
        Assert.Null(validated.Number);
        Assert.Equal("1500.50", validated.DayClosingBalance);

        await CashOut(account, Day1, "1500.50");
        var second = await Send(new SaveCashDocument(null, account, CashDocumentKind.OpeningBalance, Day2, null, null, null, null, null, "10", [], null),
            Cashier);
        var refused = await Assert.ThrowsAsync<ErpException>(() => Validate(second));
        Assert.Equal("FIN_OPENING_BALANCE_EXISTS", refused.Code);
    }

    private async Task Execute(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }
}
