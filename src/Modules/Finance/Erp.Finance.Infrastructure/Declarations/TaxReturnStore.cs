using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Declarations;

namespace Erp.Finance.Infrastructure.Declarations;

/// <summary>Declaratiile prin fin.usp_TaxReturn_*, fin.usp_TaxCalendar_*, fin.usp_VatRegister_Summary.</summary>
public sealed class TaxReturnStore(IDbSession session) : ITaxReturnStore
{
    public async Task<(IReadOnlyList<VatSummaryRow> Rows, int Hash)> VatSummaryAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_VatRegister_Summary", new { CompanyId = companyId, From = D(from), To = D(to) }, ct))
            .ConfigureAwait(false);
        var rows = (await grid.ReadAsync<VatSummaryRow>().ConfigureAwait(false)).ToList();
        var hash = await grid.ReadSingleAsync<(int Hash, int Rows)>().ConfigureAwait(false);
        return (rows, hash.Hash);
    }

    public async Task<IReadOnlyList<PartnerVatRow>> VatByPartnerAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<PartnerVatRow>(Command("fin.usp_VatRegister_ByPartner", new { CompanyId = companyId, From = D(from), To = D(to) }, ct))
            .ConfigureAwait(false)).ToList();

    public Task SavePartnerLinesAsync(int companyId, int id, IReadOnlyList<PartnerLine> lines, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_TaxReturn_SavePartnerLines", new
        {
            CompanyId = companyId, Id = id,
            Lines = JsonSerializer.Serialize(lines.Select(l => new
            {
                section = l.Section, partnerId = l.PartnerId, partnerFiscalCode = l.PartnerFiscalCode, partnerCountryCode = l.PartnerCountryCode,
                partnerName = l.PartnerName, vatRatePercent = l.VatRatePercent, documents = l.Documents, baseRon = l.BaseRon, vatRon = l.VatRon, issue = l.Issue,
            })),
        }, ct));

    public Task<int> SaveAsync(int companyId, string formCode, DateOnly from, DateOnly to, DateOnly? dueDate, TaxComputation computation, int hash, int userId,
        CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_TaxReturn_Save", new
        {
            CompanyId = companyId, FormCode = formCode, PeriodFrom = D(from), PeriodTo = D(to), DueDate = dueDate is { } d ? D(d) : (DateTime?)null,
            Lines = JsonSerializer.Serialize(computation.Lines.Select(l => new
            {
                rowCode = l.RowCode, columnCode = l.ColumnCode, label = l.Label, sortOrder = l.SortOrder, isManual = false, calculatedValue = l.Value,
            })),
            Hash = hash, computation.AmountDue, computation.AmountRefundable, UserId = userId,
        }, ct));

    public async Task<TaxReturnDto?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_TaxReturn_Get", new { CompanyId = companyId, Id = id }, ct)).ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<Header>().ConfigureAwait(false);
        if (h is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false))
            .Select(l => new TaxReturnLineDto(l.RowCode, l.ColumnCode, l.Label, l.IsManual, Money.Format(l.CalculatedValue),
                l.ManualValue is { } m ? Money.Format(m) : null, l.ManualReason)).ToList();
        var files = (await grid.ReadAsync<TaxReturnFileDto>().ConfigureAwait(false)).ToList();
        var comparison = (await grid.ReadAsync<ComparisonRow>().ConfigureAwait(false)).Select(c => new TaxComparisonDto(c.RowCode, c.ColumnCode,
            Money.Format(c.DeclaredValue), Money.Format(c.PrecompletedValue), Money.Format(c.DeclaredValue - c.PrecompletedValue), c.IsOverThreshold, c.Explanation)).ToList();
        var partners = (await grid.ReadAsync<PartnerLine>().ConfigureAwait(false)).Select(l => new PartnerLineDto(l.Section, l.PartnerId, l.PartnerFiscalCode,
            l.PartnerCountryCode, l.PartnerName, l.VatRatePercent, l.Documents, Money.Format(l.BaseRon), Money.Format(l.VatRon), l.Issue)).ToList();
        return new TaxReturnDto(h.Id, h.FormCode, h.FormName, DateOnly.FromDateTime(h.PeriodFrom), DateOnly.FromDateTime(h.PeriodTo), h.Kind, h.Status, h.NeedsReview,
            h.DueDate is { } due ? DateOnly.FromDateTime(due) : null, h.CalculatedAt, h.GeneratedAt, h.SubmittedAt, h.RegistrationNumber, Money.Format(h.AmountDue),
            Money.Format(h.AmountRefundable), h.PrecompletedAt, h.RowVersion, lines, files, comparison, partners);
    }

    public Task<int?> FindAsync(int companyId, string formCode, DateOnly periodFrom, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<int?>(Command("fin.usp_TaxReturn_Find", new { CompanyId = companyId, FormCode = formCode, PeriodFrom = D(periodFrom) }, ct));

    public async Task<TaxReturnState> TransitionAsync(int companyId, int id, byte[] rowVersion, string action, int? hash, string? registrationNumber, int userId,
        CancellationToken ct)
    {
        var s = await session.Connection.QuerySingleAsync<StateRow>(Command("fin.usp_TaxReturn_Transition", new
        {
            CompanyId = companyId, Id = id, RowVersion = rowVersion, Action = action, Hash = hash, RegistrationNumber = registrationNumber, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new TaxReturnState(s.Id, s.FormCode, DateOnly.FromDateTime(s.PeriodFrom), DateOnly.FromDateTime(s.PeriodTo), s.Status, s.AmountDue,
            s.DueDate is { } due ? DateOnly.FromDateTime(due) : null, s.RowVersion);
    }

    public Task SaveComparisonAsync(int companyId, int id, IReadOnlyList<PrecompletedDifference> rows, bool imported, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_TaxReturn_SaveComparison", new
        {
            CompanyId = companyId, Id = id, Imported = imported, UserId = userId,
            Rows = JsonSerializer.Serialize(rows.Select(r => new
            {
                rowCode = r.RowCode, columnCode = r.ColumnCode, declared = r.Declared, precompleted = r.Precompleted, isOverThreshold = r.IsOverThreshold,
            })),
        }, ct));

    public Task ExplainAsync(int companyId, int id, string rowCode, string columnCode, string explanation, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_TaxReturn_Explain", new
        {
            CompanyId = companyId, Id = id, RowCode = rowCode, ColumnCode = columnCode, Explanation = explanation, UserId = userId,
        }, ct));

    public Task SaveFileAsync(int companyId, int id, byte kind, string fileName, byte[] content, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_TaxReturn_SaveFile", new
        {
            CompanyId = companyId, Id = id, Kind = kind, FileName = fileName, Content = content, UserId = userId,
        }, ct));

    public async Task<TaxFile?> GetFileAsync(int companyId, int fileId, CancellationToken ct)
    {
        var f = await session.Connection.QuerySingleOrDefaultAsync<(string FileName, byte[] Content)?>(Command("fin.usp_TaxReturn_GetFile",
            new { CompanyId = companyId, FileId = fileId }, ct)).ConfigureAwait(false);
        return f is { } v ? new TaxFile(v.FileName, v.Content) : null;
    }

    public async Task<IReadOnlyList<(int Id, string FormCode, DateOnly From, DateOnly To, int? Hash)>> MarkReviewAsync(int companyId, DateOnly from, DateOnly to,
        bool reopened, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int Id, string FormCode, DateTime From, DateTime To, int? Hash)>(Command("fin.usp_TaxReturn_MarkReview", new
        {
            CompanyId = companyId, From = D(from), To = D(to), Reopened = reopened,
        }, ct)).ConfigureAwait(false)).Select(r => (r.Id, r.FormCode, DateOnly.FromDateTime(r.From), DateOnly.FromDateTime(r.To), r.Hash)).ToList();

    public Task ClearReviewAsync(int companyId, int id, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_TaxReturn_ClearReview", new { CompanyId = companyId, Id = id }, ct));

    public Task SaveCalendarAsync(int companyId, IReadOnlyList<TaxDeadline> items, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_TaxCalendar_Save", new
        {
            CompanyId = companyId,
            Items = JsonSerializer.Serialize(items.Select(i => new { formCode = i.FormCode, periodFrom = D(i.PeriodFrom), periodTo = D(i.PeriodTo), dueDate = D(i.DueDate) })),
        }, ct));

    public async Task<IReadOnlyList<TaxCalendarItemDto>> CalendarAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<CalendarRow>(Command("fin.usp_TaxCalendar_List", new { CompanyId = companyId, From = D(from), To = D(to) }, ct))
            .ConfigureAwait(false)).Select(r => new TaxCalendarItemDto(r.FormCode, r.FormName, DateOnly.FromDateTime(r.PeriodFrom), DateOnly.FromDateTime(r.PeriodTo),
            DateOnly.FromDateTime(r.DueDate), r.ReturnId, r.Status, r.NeedsReview ?? false, r.AmountDue is { } a ? Money.Format(a) : null)).ToList();

    private static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record Header(int Id, string FormCode, string FormName, DateTime PeriodFrom, DateTime PeriodTo, byte Kind, byte Status, bool NeedsReview,
        DateTime? DueDate, DateTime? CalculatedAt, int? CalculationHash, DateTime? GeneratedAt, DateTime? SubmittedAt, string? RegistrationNumber, decimal AmountDue,
        decimal AmountRefundable, DateTime? PrecompletedAt, byte[] RowVersion);

    private sealed record ComparisonRow(string RowCode, string ColumnCode, decimal DeclaredValue, decimal PrecompletedValue, bool IsOverThreshold, string? Explanation);

    private sealed record LineRow(string RowCode, string ColumnCode, string Label, bool IsManual, decimal CalculatedValue, decimal? ManualValue, string? ManualReason);

    private sealed record StateRow(int Id, string FormCode, DateTime PeriodFrom, DateTime PeriodTo, byte Status, decimal AmountDue, DateTime? DueDate, byte[] RowVersion);

    private sealed record CalendarRow(string FormCode, string FormName, DateTime PeriodFrom, DateTime PeriodTo, DateTime DueDate, int? ReturnId, byte? Status,
        bool? NeedsReview, decimal? AmountDue);
}

/// <summary>Redeschiderea unei luni (C4 4.1, C4-A11): declaratiile depuse ale perioadei devin „De verificat”.</summary>
public sealed class MarkTaxReturnsOnReopen(TaxReturnService service) : IIntegrationEventHandler<Erp.Finance.Contracts.Periods.PerioadaContabilaRedeschisa>
{
    public Task HandleAsync(Erp.Finance.Contracts.Periods.PerioadaContabilaRedeschisa evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return service.RecheckAsync(evt.CompanyId, evt.StartDate, evt.EndDate, reopened: true, ct);
    }
}

/// <summary>Reinchiderea lunii (C4-A11): declaratiile „De verificat” cu datele neschimbate isi pierd marcajul.</summary>
public sealed class RecheckTaxReturnsOnClose(TaxReturnService service) : IIntegrationEventHandler<Erp.Finance.Contracts.Periods.PerioadaContabilaInchisa>
{
    public Task HandleAsync(Erp.Finance.Contracts.Periods.PerioadaContabilaInchisa evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return service.RecheckAsync(evt.CompanyId, evt.StartDate, evt.EndDate, reopened: false, ct);
    }
}
