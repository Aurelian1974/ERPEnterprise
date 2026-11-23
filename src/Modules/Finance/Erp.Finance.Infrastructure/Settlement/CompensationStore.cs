using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>PV-urile de compensare prin procedurile fin.usp_Compensation_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class CompensationStore(IDbSession session) : ICompensationStore
{
    public async Task<FinanceSaved> SaveAsync(int companyId, int? id, DateOnly documentDate, CompensationKind kind, string? externalRef, string currencyCode,
        string? note, IReadOnlyList<CompensationLineRecord> lines, byte[]? rowVersion, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("LineNumber", typeof(short));
        table.Columns.Add("OpenItemId", typeof(long));
        table.Columns.Add("Amount", typeof(decimal));
        foreach (var l in lines)
        {
            table.Rows.Add(l.LineNumber, l.OpenItemId, l.Amount);
        }

        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_Compensation_Save", new
        {
            Id = id, CompanyId = companyId, DocumentDate = Day(documentDate), Kind = (byte)kind, ExternalRef = externalRef, CurrencyCode = currencyCode, Note = note,
            Lines = table.AsTableValuedParameter("fin.CompensationLineList"), RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public Task DeleteAsync(int companyId, int id, byte[] rowVersion, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_Compensation_Delete", new { Id = id, CompanyId = companyId, RowVersion = rowVersion }, ct));

    public async Task<CompensationDetailDto?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_Compensation_Get", new { Id = id, CompanyId = companyId }, ct))
            .ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>().ConfigureAwait(false);
        if (h is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false))
            .Select(l => new CompensationLineDto(l.LineNumber, l.OpenItemId, l.PartnerId, l.PartnerName, (PartnerRole)l.Role, (OpenItemKind)l.Kind, l.DocumentNo,
                DateOnly.FromDateTime(l.DocumentDate), DateOnly.FromDateTime(l.DueDate), Money.Format(l.ItemAmount), Money.Format(l.OpenAmount), Money.Format(l.Amount),
                l.IsVatOnCollection)).ToList();
        return new CompensationDetailDto(h.Id, h.Number, DateOnly.FromDateTime(h.DocumentDate), (CompensationKind)h.Kind, h.ExternalRef, h.CurrencyCode,
            Money.Format(h.TotalAmount), (CompensationStatus)h.Status, h.SignedAttachmentId, h.Note, h.ValidatedAt, h.ReversedAt, h.CreatedAt, h.CreatedByName, lines,
            h.RowVersion);
    }

    public async Task<IReadOnlyList<CompensationItemDto>> ListAsync(int companyId, int? year, CompensationStatus? status, bool withoutSigned, string? search,
        CancellationToken ct) =>
        (await session.Connection.QueryAsync<ListRow>(Command("fin.usp_Compensation_List", new
        {
            CompanyId = companyId, Year = year, Status = (byte?)status, WithoutSigned = withoutSigned, Search = search,
        }, ct)).ConfigureAwait(false))
        .Select(r => new CompensationItemDto(r.Id, r.Number, DateOnly.FromDateTime(r.DocumentDate), (CompensationKind)r.Kind, r.CurrencyCode, Money.Format(r.TotalAmount),
            (CompensationStatus)r.Status, r.HasSignedScan, r.Partners)).ToList();

    public async Task<IReadOnlyList<CompensationValidatedLine>> ValidateAsync(int companyId, int id, string number, byte[] rowVersion, int userId,
        CancellationToken ct) =>
        (await session.Connection.QueryAsync<(short LineNumber, long OpenItemId, decimal Amount, long TechnicalOpenItemId, int PartnerId, byte Role)>(Command(
            "fin.usp_Compensation_Validate", new { Id = id, CompanyId = companyId, Number = number, RowVersion = rowVersion, UserId = userId }, ct)).ConfigureAwait(false))
        .Select(l => new CompensationValidatedLine(l.LineNumber, l.OpenItemId, l.Amount, l.TechnicalOpenItemId, l.PartnerId, (PartnerRole)l.Role)).ToList();

    public async Task<(IReadOnlyList<long> Allocations, IReadOnlyList<(long Id, decimal Amount)> TechnicalItems)> ReverseAsync(int companyId, int id,
        byte[] rowVersion, int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_Compensation_Reverse",
            new { Id = id, CompanyId = companyId, RowVersion = rowVersion, UserId = userId }, ct)).ConfigureAwait(false);
        var allocations = (await grid.ReadAsync<long>().ConfigureAwait(false)).ToList();
        var technical = (await grid.ReadAsync<(long Id, decimal Amount)>().ConfigureAwait(false)).ToList();
        return (allocations, technical);
    }

    public Task<byte[]> SetSignedAsync(int companyId, int id, int attachmentId, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]>(Command("fin.usp_Compensation_SetSigned",
            new { Id = id, CompanyId = companyId, AttachmentId = attachmentId, UserId = userId }, ct));

    public async Task<IReadOnlyList<CompensationProposalDto>> ProposalsAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int PartnerId, string PartnerName, string CurrencyCode, decimal Amount)>(Command("fin.usp_Compensation_Proposals",
            new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(p => new CompensationProposalDto(p.PartnerId, p.PartnerName, p.CurrencyCode, Money.Format(p.Amount))).ToList();

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record HeaderRow(int Id, string? Number, DateTime DocumentDate, byte Kind, string? ExternalRef, string CurrencyCode, decimal TotalAmount, byte Status,
        int? SignedAttachmentId, string? Note, DateTime? ValidatedAt, DateTime? ReversedAt, DateTime CreatedAt, string? CreatedByName, byte[] RowVersion);

    private sealed record LineRow(short LineNumber, long OpenItemId, int PartnerId, string PartnerName, byte Role, byte Kind, string DocumentNo, DateTime DocumentDate,
        DateTime DueDate, decimal ItemAmount, decimal OpenAmount, decimal Amount, bool IsVatOnCollection, decimal VatAmountRon, decimal ExchangeRate);

    private sealed record ListRow(int Id, string? Number, DateTime DocumentDate, byte Kind, string CurrencyCode, decimal TotalAmount, byte Status, bool HasSignedScan,
        string? Partners);
}
