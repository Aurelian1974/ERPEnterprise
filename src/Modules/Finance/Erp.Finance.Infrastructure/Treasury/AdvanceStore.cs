using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Treasury;

namespace Erp.Finance.Infrastructure.Treasury;

/// <summary>Avansurile si deconturile prin fin.usp_CashAdvance_* si fin.usp_ExpenseReport_* (NET-06).</summary>
public sealed class AdvanceStore(IDbSession session) : IAdvanceStore
{
    public async Task<IReadOnlyList<CashAdvanceItemDto>> ListAdvancesAsync(int companyId, byte filter, DateOnly today, CancellationToken ct) =>
        (await session.Connection.QueryAsync<AdvanceRow>(Proc("fin.usp_CashAdvance_List", new { CompanyId = companyId, Filter = filter, Today = D(today) }, ct))
            .ConfigureAwait(false))
        .Select(a => new CashAdvanceItemDto(a.Id, a.EmployeeId, a.EmployeeName, (AdvancePurpose)a.Purpose, a.CurrencyCode.Trim(), Money.Format(a.Amount),
            Money.Format(a.SettledAmount), Money.Format(a.ReturnedAmount), Money.Format(a.PaidDifference), Money.Format(a.Balance),
            DateOnly.FromDateTime(a.SettlementDueDate), Day(a.TripStartDate), Day(a.TripEndDate), (AdvanceStatus)a.Status, a.Description, a.GrantedByNumber,
            a.ExpenseReportId, a.ExpenseReportNumber, a.DaysOverdue)).ToList();

    public async Task<IReadOnlyList<ExpenseTypeRates>> GetExpenseTypesAsync(DateOnly day, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Proc("fin.usp_ExpenseReport_Lookups", new { CompanyId = 0, Day = D(day) }, ct))
            .ConfigureAwait(false);
        return (await grid.ReadAsync<ExpenseTypeRates>().ConfigureAwait(false)).ToList();
    }

    public async Task<IReadOnlyList<ExpenseReportItemDto>> ListReportsAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Proc("fin.usp_ExpenseReport_Lookups", new { CompanyId = companyId, Day = D(DateOnly.MinValue) },
            ct)).ConfigureAwait(false);
        await grid.ReadAsync<ExpenseTypeRates>().ConfigureAwait(false);
        return (await grid.ReadAsync<ReportItemRow>().ConfigureAwait(false)).Select(r => new ExpenseReportItemDto(r.Id, r.Number,
            DateOnly.FromDateTime(r.DocumentDate), r.EmployeeName, r.CashAdvanceId, Money.Format(r.TotalJustified), Money.Format(r.Difference),
            (ExpenseReportStatus)r.Status)).ToList();
    }

    public Task<bool> HasOverdueAsync(int companyId, int employeeId, DateOnly today, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<bool>(Proc("fin.usp_CashAdvance_HasOverdue", new { CompanyId = companyId, EmployeeId = employeeId, Today = D(today) }, ct));

    public Task<int> CreateAdvanceAsync(CashAdvanceRecord advance, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Proc("fin.usp_CashAdvance_Create", new
        {
            advance.CompanyId, advance.EmployeeId, Purpose = (byte)advance.Purpose,
            CurrencyCode = new DbString { Value = advance.CurrencyCode, Length = 3, IsAnsi = true, IsFixedLength = true }, advance.Amount, advance.Description,
            TripStartDate = N(advance.TripStartDate), TripEndDate = N(advance.TripEndDate), SettlementDueDate = D(advance.SettlementDueDate),
            advance.OverdueConfirmed, UserId = userId,
        }, ct));

    public Task<int> GetOperationTypeIdAsync(string code, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(new CommandDefinition("SELECT Id FROM fin.TreasuryOperationType WHERE CodeKey = UPPER(@Code)",
            new { Code = code }, session.Transaction, cancellationToken: ct));

    public Task<FinanceSaved> SaveReportAsync(ExpenseReportRecord report, int userId, CancellationToken ct)
    {
        var lines = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("LineNumber", typeof(short)), ("ExpenseTypeId", typeof(int)), ("JustificationKind", typeof(byte)), ("DocumentNo", typeof(string)),
            ("DocumentDate", typeof(DateTime)), ("SupplierFiscalCode", typeof(string)), ("SupplierInvoiceId", typeof(int)), ("Amount", typeof(decimal)),
            ("VatAmount", typeof(decimal)), ("CostCenterId", typeof(int)), ("ProjectId", typeof(int)), ("Days", typeof(decimal)), ("DailyAmount", typeof(decimal)),
            ("NonTaxableAmount", typeof(decimal)), ("TaxableAmount", typeof(decimal)), ("Note", typeof(string)),
        })
        {
            lines.Columns.Add(name, type);
        }

        foreach (var l in report.Lines)
        {
            lines.Rows.Add(l.LineNumber, l.ExpenseTypeId, (byte)l.JustificationKind, V(l.DocumentNo), V(N(l.DocumentDate)), V(l.SupplierFiscalCode),
                V(l.SupplierInvoiceId), l.Amount, l.VatAmount, V(l.CostCenterId), V(l.ProjectId), V(l.Days), V(l.DailyAmount), V(l.NonTaxableAmount),
                V(l.TaxableAmount), V(l.Note));
        }

        return session.Connection.QuerySingleAsync<FinanceSaved>(Proc("fin.usp_ExpenseReport_Save", new
        {
            report.Id, report.CompanyId, report.EmployeeId, report.CashAdvanceId, DocumentDate = D(report.DocumentDate), report.Description, report.TotalJustified,
            Lines = lines.AsTableValuedParameter("fin.ExpenseReportLineList"), report.RowVersion, UserId = userId,
        }, ct));
    }

    public async Task<ExpenseReportDetailDto?> GetReportAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Proc("fin.usp_ExpenseReport_Get", new { CompanyId = companyId, Id = id }, ct)).ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<ReportRow>().ConfigureAwait(false);
        if (h is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<ReportLineRow>().ConfigureAwait(false)).Select(l => new ExpenseReportLineDto(l.Id, l.LineNumber, l.ExpenseTypeId,
            l.ExpenseTypeCode, l.ExpenseTypeName, l.IsPerDiem, (JustificationKind)l.JustificationKind, l.DocumentNo, Day(l.DocumentDate), l.SupplierFiscalCode,
            l.SupplierInvoiceId, Money.Format(l.Amount), Money.Format(l.VatAmount), l.CostCenterId, l.ProjectId,
            l.Days?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Money.Format(l.DailyAmount), Money.Format(l.NonTaxableAmount),
            Money.Format(l.TaxableAmount), l.Note)).ToList();
        return new ExpenseReportDetailDto(h.Id, h.EmployeeId, h.EmployeeName, h.CashAdvanceId, Money.Format(h.AdvanceGranted), h.Number,
            DateOnly.FromDateTime(h.DocumentDate), h.CurrencyCode.Trim(), Money.Format(h.TotalJustified), Money.Format(h.AdvanceAmount), Money.Format(h.Difference),
            h.Description, (ExpenseReportStatus)h.Status, h.CreatedBy, h.CreatedByName, h.CreatedAt, h.SubmittedAt, h.SubmittedByName, h.ApprovedAt,
            h.ApprovedByName, h.ValidatedAt, h.SettlementCashDocumentId, h.SettlementNumber, (CashDocumentStatus?)h.SettlementStatus, lines, h.RowVersion);
    }

    public async Task<ExpenseReportStep> TransitionAsync(int companyId, int id, byte[] rowVersion, string action, string? number, bool separateDuties, int userId,
        CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<StepRow>(Proc("fin.usp_ExpenseReport_Transition", new
        {
            Id = id, CompanyId = companyId, RowVersion = rowVersion, Action = new DbString { Value = action, Length = 10, IsAnsi = true }, Number = number,
            SeparateDuties = separateDuties, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new ExpenseReportStep(row.Id, row.Number, (ExpenseReportStatus)row.Status, row.Difference, row.CashAdvanceId, row.RowVersion);
    }

    public Task SetSettlementAsync(int reportId, int cashDocumentId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Proc("fin.usp_ExpenseReport_SetSettlement", new { Id = reportId, CashDocumentId = cashDocumentId }, ct));

    private CommandDefinition Proc(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateTime D(DateOnly value) => value.ToDateTime(TimeOnly.MinValue);

    private static DateTime? N(DateOnly? value) => value?.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? Day(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    private static object V<T>(T? value) => value is null ? DBNull.Value : value;

    private sealed record AdvanceRow(int Id, int EmployeeId, string EmployeeName, byte Purpose, string CurrencyCode, decimal Amount, decimal SettledAmount,
        decimal ReturnedAmount, decimal PaidDifference, decimal Balance, DateTime SettlementDueDate, DateTime? TripStartDate, DateTime? TripEndDate, byte Status,
        string? Description, string? GrantedByNumber, int? ExpenseReportId, string? ExpenseReportNumber, int DaysOverdue, byte[] RowVersion);

    private sealed record ReportItemRow(int Id, string? Number, DateTime DocumentDate, string EmployeeName, int? CashAdvanceId, decimal TotalJustified,
        decimal Difference, byte Status);

    private sealed record ReportRow(int Id, int EmployeeId, string EmployeeName, int? CashAdvanceId, decimal? AdvanceGranted, byte? AdvancePurpose,
        string? Number, DateTime DocumentDate, string CurrencyCode, decimal TotalJustified, decimal AdvanceAmount, decimal Difference, string? Description,
        byte Status, int CreatedBy, string? CreatedByName, DateTime CreatedAt, DateTime? SubmittedAt, string? SubmittedByName, DateTime? ApprovedAt,
        string? ApprovedByName, DateTime? ValidatedAt, int? SettlementCashDocumentId, string? SettlementNumber, byte? SettlementStatus, byte[] RowVersion);

    private sealed record ReportLineRow(long Id, short LineNumber, int ExpenseTypeId, string ExpenseTypeCode, string ExpenseTypeName, bool IsPerDiem,
        byte JustificationKind, string? DocumentNo, DateTime? DocumentDate, string? SupplierFiscalCode, int? SupplierInvoiceId, decimal Amount, decimal VatAmount,
        int? CostCenterId, int? ProjectId, decimal? Days, decimal? DailyAmount, decimal? NonTaxableAmount, decimal? TaxableAmount, string? Note);

    private sealed record StepRow(int Id, string? Number, byte Status, decimal Difference, int? CashAdvanceId, byte[] RowVersion);
}
