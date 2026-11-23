using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Efectele comerciale prin procedurile fin.usp_CommercialPaper_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class CommercialPaperStore(IDbSession session) : ICommercialPaperStore
{
    public async Task<(int Id, long OpenItemId)> RegisterAsync(int companyId, CommercialPaperRecord paper, int userId, CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<(int Id, long OpenItemId)>(Command("fin.usp_CommercialPaper_Register", new
        {
            CompanyId = companyId, paper.PartnerId, Direction = (byte)paper.Direction, Kind = (byte)paper.Kind, paper.Series, paper.Number, paper.IssuerBank,
            paper.IssuerAccount, paper.Amount, paper.CurrencyCode, paper.ExchangeRate, IssueDate = Day(paper.IssueDate), MaturityDate = Day(paper.MaturityDate),
            paper.Note, UserId = userId,
        }, ct)).ConfigureAwait(false);

    public async Task<(PaperStatus Previous, PaperDirection Direction, long OpenItemId, byte[] RowVersion)> ChangeStatusAsync(int companyId, PaperStatusChange change,
        int userId, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleAsync<(byte PreviousStatus, byte Direction, long OpenItemId, byte[] RowVersion)>(Command(
            "fin.usp_CommercialPaper_ChangeStatus", new
            {
                change.Id, CompanyId = companyId, Status = (byte)change.Status, EventDate = Day(change.EventDate), change.TreasuryAccountId, change.BankStatementLineId,
                change.EndorsedToPartnerId, change.Note, change.RowVersion, UserId = userId,
            }, ct)).ConfigureAwait(false);
        return ((PaperStatus)r.PreviousStatus, (PaperDirection)r.Direction, r.OpenItemId, r.RowVersion);
    }

    public async Task<CommercialPaperDetailDto?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_CommercialPaper_Get", new { Id = id, CompanyId = companyId }, ct))
            .ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>().ConfigureAwait(false);
        if (h is null)
        {
            return null;
        }

        var events = (await grid.ReadAsync<EventRow>().ConfigureAwait(false))
            .Select(e => new CommercialPaperEventDto((PaperStatus)e.Status, DateOnly.FromDateTime(e.EventDate), e.TreasuryAccountCode, e.BankStatementLineId, e.Note,
                e.CreatedAt, e.CreatedByName)).ToList();
        var invoices = (await grid.ReadAsync<(long OpenItemId, string DocumentNo, DateTime DocumentDate, decimal Amount, byte AllocationStatus)>().ConfigureAwait(false))
            .Select(i => new CommercialPaperInvoiceDto(i.OpenItemId, i.DocumentNo, DateOnly.FromDateTime(i.DocumentDate), Money.Format(i.Amount), i.AllocationStatus == 1))
            .ToList();
        return new CommercialPaperDetailDto(h.Id, h.PartnerId, h.PartnerName, (PaperDirection)h.Direction, (PaperKind)h.Kind, h.Series, h.Number, h.IssuerBank,
            h.IssuerAccount, Money.Format(h.Amount), h.CurrencyCode, DateOnly.FromDateTime(h.IssueDate), DateOnly.FromDateTime(h.MaturityDate), h.TreasuryAccountId,
            h.TreasuryAccountCode, (PaperStatus)h.Status, h.GlAccountCode, h.OpenItemId, h.EndorsedToPartnerId, h.EndorsedToPartnerName, h.Note, events, invoices,
            h.RowVersion);
    }

    public async Task<CommercialPaperListDto> ListAsync(int companyId, PaperDirection? direction, bool activeOnly, DateOnly? maturityTo, int? year,
        CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_CommercialPaper_List", new
        {
            CompanyId = companyId, Direction = (byte?)direction, ActiveOnly = activeOnly, MaturityTo = maturityTo is { } m ? Day(m) : (DateTime?)null, Year = year,
        }, ct)).ConfigureAwait(false);
        var items = (await grid.ReadAsync<ListRow>().ConfigureAwait(false))
            .Select(r => new CommercialPaperItemDto(r.Id, r.PartnerId, r.PartnerName, (PaperDirection)r.Direction, (PaperKind)r.Kind, r.Series, r.Number,
                Money.Format(r.Amount), r.CurrencyCode, DateOnly.FromDateTime(r.IssueDate), DateOnly.FromDateTime(r.MaturityDate), (PaperStatus)r.Status,
                r.TreasuryAccountCode)).ToList();
        var t = await grid.ReadSingleAsync<(decimal InPortfolio, int InPortfolioCount, decimal Deposited, int DepositedCount, decimal IssuedToPay, int IssuedToPayCount,
            decimal Refused, int RefusedCount)>().ConfigureAwait(false);
        return new CommercialPaperListDto(new CommercialPaperTotalsDto(Money.Format(t.InPortfolio), t.InPortfolioCount, Money.Format(t.Deposited), t.DepositedCount,
            Money.Format(t.IssuedToPay), t.IssuedToPayCount, Money.Format(t.Refused), t.RefusedCount), items);
    }

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record HeaderRow(int Id, int PartnerId, string PartnerName, byte Direction, byte Kind, string Series, string Number, string? IssuerBank,
        string? IssuerAccount, decimal Amount, string CurrencyCode, DateTime IssueDate, DateTime MaturityDate, int? TreasuryAccountId, string? TreasuryAccountCode,
        byte Status, string? GlAccountCode, long? OpenItemId, int? EndorsedToPartnerId, string? EndorsedToPartnerName, string? Note, byte[] RowVersion);

    private sealed record EventRow(byte Status, DateTime EventDate, string? TreasuryAccountCode, long? BankStatementLineId, string? Note, DateTime CreatedAt,
        string? CreatedByName);

    private sealed record ListRow(int Id, int PartnerId, string PartnerName, byte Direction, byte Kind, string Series, string Number, decimal Amount, string CurrencyCode,
        DateTime IssueDate, DateTime MaturityDate, byte Status, string? TreasuryAccountCode);
}
