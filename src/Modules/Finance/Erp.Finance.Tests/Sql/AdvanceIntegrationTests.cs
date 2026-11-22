using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Treasury;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Avansurile spre decontare si deconturile pe SQL Server real (02.02, F1 3.5, 3.7, 4.4; criteriile F1-A11, F1-A12): avansul nou pentru
/// un angajat cu avans depasit doar cu confirmare auditata, plata avansului care il acorda, decontul cu diurna, separarea sarcinilor si
/// dispozitia de incasare a diferentei care inchide avansul.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AdvanceIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Day = new(2026, 11, 11);

    public AdvanceIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.CashView, FinancePermissions.CashCreate, FinancePermissions.CashValidate, FinancePermissions.AdvanceManage,
            FinancePermissions.ExpenseReportCreate, FinancePermissions.ExpenseReportApprove, FinancePermissions.ExpenseReportValidate]);
    }

    private async Task<int> CashWithMoney()
    {
        var account = (await Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Cash, Unique("CASA-"), "Casa avansuri", Database.HeadOfficeId, "RON", null,
            null, Database.CashierId, null, null, null, null, CashCloseMode.OnDemand, null, true,
            [new TreasuryCashierInput(Database.CashierId, true, null, null)], null))).Id;
        var type = await Query<int>("SELECT Id FROM fin.TreasuryOperationType WHERE Code = N'INC-DIVERSE'");
        var draft = await Send(new SaveCashDocument(null, account, CashDocumentKind.CashIn, new DateOnly(2026, 11, 2), null, null, null, null, null, null,
            [new CashLineInput(type, "10000", null, null, null, null, null)], null), Database.CashierId);
        await Send(new ValidateCashDocument(draft.Id, draft.RowVersion), Database.CashierId);
        return account;
    }

    private async Task<int> NewEmployee(string name)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Employee (CompanyId, EmployeeNo, FirstName, LastName, IsActive, CreatedBy) VALUES (@CompanyId, @No, @Name, N'Test', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Database.CompanyId, No = Unique("M"), Name = name });
    }

    private async Task<CashValidatedDto> ValidateDraft(int cashDocumentId)
    {
        var rowVersion = await Query<byte[]>("SELECT RowVersion FROM fin.CashDocument WHERE Id = @Id", new { Id = cashDocumentId });
        return await Send(new ValidateCashDocument(cashDocumentId, rowVersion), Database.CashierId);
    }

    private Task<byte> AdvanceStatus(int id) => Query<byte>("SELECT Status FROM fin.CashAdvance WHERE Id = @Id", new { Id = id });

    [Fact]
    [Trait("Criteriu", "02.02-F1-A11")]
    public async Task A_new_advance_for_an_employee_with_an_overdue_one_needs_a_confirmation_that_is_audited()
    {
        var cash = await CashWithMoney();
        var employee = await NewEmployee("Mihai");
        var first = await Send(new CreateCashAdvance(employee, AdvancePurpose.CurrentExpenses, "300", cash, Day, null, null, "Rechizite"));
        Assert.Equal(new DateOnly(2026, 11, 16), first.SettlementDueDate);
        Assert.Equal((byte)0, await AdvanceStatus(first.CashAdvanceId));

        // Validarea dispozitiei de plata acorda avansul si publica AvansDecontareAcordat.
        await ValidateDraft(first.CashDocumentId);
        Assert.Equal((byte)1, await AdvanceStatus(first.CashAdvanceId));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%AvansDecontareAcordat' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"cashAdvanceId\":{first.CashAdvanceId},%" }));

        // Termenul trece (16.11 < azi): jobul il marcheaza Depasit.
        Assert.True(await new Infrastructure.Treasury.CashAdvanceOverdue(new Infrastructure.Treasury.FinanceConnection(Database.ConnectionString),
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Infrastructure.Treasury.CashAdvanceOverdue>.Instance).RunAsync(Ct) >= 1);
        Assert.Equal((byte)4, await AdvanceStatus(first.CashAdvanceId));

        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new CreateCashAdvance(employee, AdvancePurpose.CurrentExpenses, "100", cash, Day,
            null, null, null)));
        Assert.Equal("FIN_ADVANCE_OVERDUE_UNCONFIRMED", refused.Code);

        var second = await Send(new CreateCashAdvance(employee, AdvancePurpose.CurrentExpenses, "100", cash, Day, null, null, null, ConfirmOverdue: true));
        Assert.True(await Query<bool>("SELECT OverdueConfirmed FROM fin.CashAdvance WHERE Id = @Id", new { Id = second.CashAdvanceId }));
        Assert.Equal("true", await Query<string>("""
            SELECT TOP (1) JSON_VALUE(Changes, '$.overdueConfirmed') FROM core.AuditEntry WHERE Action = N'Fin.Advance.Create' AND EntityId = @Id
            """, new { Id = second.CashAdvanceId }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A12")]
    public async Task An_expense_report_below_the_advance_generates_the_refund_whose_validation_closes_the_advance()
    {
        var cash = await CashWithMoney();
        var employee = await NewEmployee("Ana");
        var advance = await Send(new CreateCashAdvance(employee, AdvancePurpose.DomesticTrip, "1500", cash, Day, new DateOnly(2026, 11, 11),
            new DateOnly(2026, 11, 13), "Deplasare Cluj"));
        Assert.Equal(new DateOnly(2026, 11, 18), advance.SettlementDueDate);
        await ValidateDraft(advance.CashDocumentId);

        var types = await Query<(int Cazare, int Diurna)>("""
            SELECT (SELECT Id FROM fin.ExpenseType WHERE Code = N'CAZARE'), (SELECT Id FROM fin.ExpenseType WHERE Code = N'DIURNA-INT')
            """);
        // Cazare 1.190 lei + diurna 3 zile × 50 = 150 lei: total 1.340, diferenta -160 (angajatul restituie).
        var saved = await Send(new SaveExpenseReport(null, employee, advance.CashAdvanceId, new DateOnly(2026, 11, 13), "Deplasare Cluj",
        [
            new ExpenseReportLineInput(types.Cazare, JustificationKind.Receipt, "BF 0231", new DateOnly(2026, 11, 12), null, null, "1190", "190", null, null, null, null),
            new ExpenseReportLineInput(types.Diurna, JustificationKind.PerDiem, null, null, null, null, null, null, null, null, "3", null),
        ], null), Database.AccountantId);
        var report = await Send(new GetExpenseReport(saved.Id));
        Assert.Equal(("1340.00", "-160.00"), (report.TotalJustified, report.Difference));
        var perDiem = report.Lines.Single(l => l.IsPerDiem);
        Assert.Equal(("150.00", "150.00", "0.00"), (perDiem.Amount, perDiem.NonTaxableAmount, perDiem.TaxableAmount));

        var submitted = await Send(new SubmitExpenseReport(saved.Id, report.RowVersion), Database.AccountantId);
        // Cine a intocmit decontul nu il aproba (separarea sarcinilor).
        var self = await Assert.ThrowsAsync<ErpException>(() => Send(new ApproveExpenseReport(saved.Id, submitted.RowVersion), Database.AccountantId));
        Assert.Equal("FIN_EXPENSE_REPORT_SELF_APPROVAL", self.Code);
        var approved = await Send(new ApproveExpenseReport(saved.Id, submitted.RowVersion), Database.CashierId);

        var noCash = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new ValidateExpenseReport(saved.Id, approved.RowVersion, null)));
        Assert.Contains("settlementTreasuryAccountId", noCash.Errors.Keys);
        var validated = await Send(new ValidateExpenseReport(saved.Id, approved.RowVersion, cash));
        Assert.Equal("-160.00", validated.Difference);
        Assert.Equal((byte)2, await AdvanceStatus(advance.CashAdvanceId));

        var refund = await Send(new GetCashDocument(validated.SettlementCashDocumentId!.Value), Database.CashierId);
        Assert.Equal((CashDocumentKind.CashIn, "160.00", "INC-RESTITUIRE-AVANS", employee), (refund.Kind, refund.TotalAmount, refund.Lines[0].OperationTypeCode,
            refund.EmployeeId));
        await ValidateDraft(refund.Id);
        Assert.Equal((byte)3, await AdvanceStatus(advance.CashAdvanceId));
        Assert.Equal(0m, await Query<decimal>("SELECT Balance FROM fin.CashAdvance WHERE Id = @Id", new { Id = advance.CashAdvanceId }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%DecontCheltuieliValidat' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"expenseReportId\":{saved.Id},%" }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.6")]
    public async Task Cash_advances_without_a_trip_are_limited_per_person_and_day()
    {
        var cash = await CashWithMoney();
        var employee = await NewEmployee("Radu");
        var first = await Send(new CreateCashAdvance(employee, AdvancePurpose.CurrentExpenses, "4000", cash, Day, null, null, null));
        await ValidateDraft(first.CashDocumentId);
        // Primul avans e deja dupa termen fata de ziua de azi: avansul nou cere confirmarea.
        var second = await Send(new CreateCashAdvance(employee, AdvancePurpose.Purchase, "1500", cash, Day, null, null, null, ConfirmOverdue: true));
        var blocked = await Assert.ThrowsAsync<ErpException>(() => ValidateDraft(second.CashDocumentId));
        Assert.Equal("FIN_CASH_LIMIT_EXCEEDED", blocked.Code);

        // Deplasarile nu intra in plafonul avansurilor pe zi si persoana.
        var trip = await Send(new CreateCashAdvance(employee, AdvancePurpose.DomesticTrip, "1500", cash, Day, Day, Day, null, ConfirmOverdue: true));
        await ValidateDraft(trip.CashDocumentId);
    }
}
