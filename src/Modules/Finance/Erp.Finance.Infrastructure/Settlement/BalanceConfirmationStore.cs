using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Confirmarile de sold prin procedurile fin.usp_BalanceConfirmation_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class BalanceConfirmationStore(IDbSession session) : IBalanceConfirmationStore
{
    public async Task<(int BatchId, IReadOnlyList<BalanceConfirmationDto> Items)> GenerateAsync(int companyId, string number, DateOnly asOf, PartnerRole? role,
        decimal minBalance, IReadOnlyList<int>? partnerIds, int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_BalanceConfirmation_Generate", new
        {
            CompanyId = companyId, Number = number, AsOf = Day(asOf), Role = (byte?)role, MinBalance = minBalance,
            PartnerIds = partnerIds is null ? null : JsonSerializer.Serialize(partnerIds), UserId = userId,
        }, ct)).ConfigureAwait(false);
        var batchId = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        return (batchId, (await grid.ReadAsync<Row>().ConfigureAwait(false)).Select(Map).ToList());
    }

    public async Task<IReadOnlyList<OpenItemAsOf>> OpenItemsAsOfAsync(int companyId, int partnerId, PartnerRole role, DateOnly asOf, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(long OpenItemId, byte Kind, string DocumentNo, DateTime DocumentDate, DateTime DueDate, string CurrencyCode,
            decimal Amount, decimal OpenAsOf, decimal OpenRon)>(Command("fin.usp_OpenItem_AsOf",
            new { CompanyId = companyId, PartnerId = partnerId, Role = (byte)role, AsOf = Day(asOf) }, ct)).ConfigureAwait(false))
        .Select(i => new OpenItemAsOf(i.OpenItemId, (OpenItemKind)i.Kind, i.DocumentNo, DateOnly.FromDateTime(i.DocumentDate), DateOnly.FromDateTime(i.DueDate),
            i.CurrencyCode, i.Amount, i.OpenAsOf, i.OpenRon)).ToList();

    public Task<byte[]> UpdateAsync(int companyId, ConfirmationUpdate update, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]>(Command("fin.usp_BalanceConfirmation_Update", new
        {
            update.Id, CompanyId = companyId, update.GeneratedAttachmentId, update.SentTo, update.EmailMessageId, ResponseStatus = (byte?)update.Status,
            ResponseDate = update.ResponseDate is { } d ? Day(d) : (DateTime?)null, update.PartnerBalance, update.ResponseNote, update.ResponseAttachmentId,
            update.IsResolved, update.RowVersion, UserId = userId,
        }, ct));

    public Task MarkBatchSentAsync(int companyId, int batchId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_BalanceConfirmationBatch_MarkSent", new { CompanyId = companyId, BatchId = batchId }, ct));

    public async Task<BalanceConfirmationsDto> ListAsync(int companyId, int? batchId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_BalanceConfirmation_List", new { CompanyId = companyId, BatchId = batchId }, ct))
            .ConfigureAwait(false);
        var batches = (await grid.ReadAsync<BatchRow>().ConfigureAwait(false))
            .Select(b => new BalanceConfirmationBatchDto(b.Id, b.Number, DateOnly.FromDateTime(b.AsOfDate), (PartnerRole?)b.Role, Money.Format(b.MinBalance), b.SentAt,
                b.Total, b.Confirmed ?? 0, b.Contested ?? 0, b.Unanswered ?? 0)).ToList();
        var items = (await grid.ReadAsync<Row>().ConfigureAwait(false)).Select(Map).ToList();
        return new BalanceConfirmationsDto(batches, items);
    }

    public async Task<IReadOnlyList<BalanceConfirmationDto>> GetAsync(int companyId, int? id, int? batchId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<Row>(Command("fin.usp_BalanceConfirmation_Get", new { CompanyId = companyId, Id = id, BatchId = batchId }, ct))
            .ConfigureAwait(false)).Select(Map).ToList();

    private static BalanceConfirmationDto Map(Row r) =>
        new(r.Id, r.BatchId, r.PartnerId, r.PartnerName, r.FiscalCode, (PartnerRole)r.Role, r.Number, DateOnly.FromDateTime(r.AsOfDate), Money.Format(r.OurBalance),
            r.GeneratedAttachmentId, r.SentAt, r.SentTo, (ConfirmationStatus)r.ResponseStatus, r.ResponseDate is { } d ? DateOnly.FromDateTime(d) : null,
            r.PartnerBalance is { } p ? Money.Format(p) : null, r.Difference is { } x ? Money.Format(x) : null, r.ResponseNote, r.ResponseAttachmentId, r.IsResolved,
            r.Email, r.EmailStatus, r.EmailError, r.RowVersion);

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record BatchRow(int Id, string Number, DateTime AsOfDate, byte? Role, decimal MinBalance, DateTime? SentAt, int Total, int? Confirmed,
        int? Contested, int? Unanswered);

    private sealed record Row(int Id, int BatchId, int PartnerId, string PartnerName, string? FiscalCode, byte Role, string Number, DateTime AsOfDate,
        decimal OurBalance, int? GeneratedAttachmentId, DateTime? SentAt, string? SentTo, byte ResponseStatus, DateTime? ResponseDate, decimal? PartnerBalance,
        decimal? Difference, string? ResponseNote, int? ResponseAttachmentId, bool IsResolved, string? Email, byte? EmailStatus, string? EmailError,
        byte[] RowVersion);
}
