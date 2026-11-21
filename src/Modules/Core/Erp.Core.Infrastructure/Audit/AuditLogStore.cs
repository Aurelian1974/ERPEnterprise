using System.Data;
using Dapper;
using Erp.Core.Application.Abstractions;
using Erp.Core.Application.Audit;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Audit;

/// <summary>Jurnalul de audit si de autentificari prin core.usp_Audit_*, core.usp_LoginEvent_Search si core.usp_Company_History (NET-06).</summary>
public sealed class AuditLogStore(IDbSession session) : IAuditLogStore
{
    public async Task<AuditPageDto> SearchAsync(AuditSearchCriteria criteria, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_Audit_Search", new
        {
            criteria.UserId, criteria.From, criteria.To, criteria.ActorUserId, EntityType = Text(criteria.EntityType), criteria.EntityId,
            Action = Text(criteria.Action), Search = Text(criteria.Search), criteria.Offset, criteria.PageSize,
        }, ct)).ConfigureAwait(false);
        var all = await grid.ReadSingleAsync<bool>().ConfigureAwait(false);
        var rows = (await grid.ReadAsync<EntryRow>().ConfigureAwait(false)).ToList();
        var users = (await grid.ReadAsync<UserOptionDto>().ConfigureAwait(false)).ToList();
        var types = (await grid.ReadAsync<string>().ConfigureAwait(false)).ToList();
        var actions = (await grid.ReadAsync<string>().ConfigureAwait(false)).ToList();
        var entries = rows.Select(r => new AuditEntryItemDto(r.Id, Utc(r.OccurredAt), r.UserId, r.UserName, r.Action, r.EntityType, r.EntityId,
            r.RecordLabel, r.CompanyName, r.IsFirst)).ToList();
        return new AuditPageDto(entries, rows.Count == 0 ? 0 : rows[0].TotalCount, all, users, types, actions);
    }

    public async Task<AuditEntryDetailDto?> GetAsync(int userId, long id, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command("core.usp_Audit_Get", new { UserId = userId, Id = id }, ct))
            .ConfigureAwait(false);
        return row is null ? null : new AuditEntryDetailDto(row.Id, Utc(row.OccurredAt), row.UserId, row.UserName, row.Login, row.CompanyId,
            row.CompanyName, row.Action, row.EntityType, row.EntityId, row.RecordLabel, row.Changes, row.PreviousChanges,
            row.PreviousAt is { } p ? Utc(p) : null, row.IpAddress, row.CorrelationId);
    }

    public async Task<LoginEventPageDto> SearchLoginsAsync(LoginEventCriteria criteria, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_LoginEvent_Search", new
        {
            criteria.UserId, criteria.From, criteria.To, Search = Text(criteria.Search), criteria.Success, criteria.Offset, criteria.PageSize,
        }, ct)).ConfigureAwait(false);
        var all = await grid.ReadSingleAsync<bool>().ConfigureAwait(false);
        var rows = (await grid.ReadAsync<LoginRow>().ConfigureAwait(false)).ToList();
        var events = rows.Select(r => new LoginEventDto(r.Id, Utc(r.OccurredAt), r.UserName, r.UserId, r.DisplayName, r.Success, r.FailureReason,
            r.IpAddress, r.UserAgent)).ToList();
        return new LoginEventPageDto(events, rows.Count == 0 ? 0 : rows[0].TotalCount, all);
    }

    public async Task<IReadOnlyList<PartnerHistoryEntryDto>> GetCompanyHistoryAsync(int companyId, CancellationToken ct) =>
        [.. (await session.Connection.QueryAsync<HistoryRow>(Command("core.usp_Company_History", new { Id = companyId }, ct)).ConfigureAwait(false))
            .Select(h => new PartnerHistoryEntryDto(h.Id, Utc(h.OccurredAt), h.Action, h.EntityType, h.EntityId, h.UserName, h.Changes))];

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DbString Text(string? value) => new() { Value = value, Length = 100 };

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record EntryRow(long Id, DateTime OccurredAt, int UserId, string UserName, string Action, string EntityType, long? EntityId,
        string? RecordLabel, string? CompanyName, bool IsFirst, int TotalCount);

    private sealed record DetailRow(long Id, DateTime OccurredAt, int UserId, string UserName, string Login, int? CompanyId, string? CompanyName,
        string Action, string EntityType, long? EntityId, string? RecordLabel, string? Changes, string? PreviousChanges, DateTime? PreviousAt,
        string? IpAddress, Guid CorrelationId);

    private sealed record LoginRow(long Id, DateTime OccurredAt, string UserName, int? UserId, string? DisplayName, bool Success, string? FailureReason,
        string? IpAddress, string? UserAgent, int TotalCount);

    private sealed record HistoryRow(long Id, DateTime OccurredAt, string Action, string EntityType, long? EntityId, string UserName, string? Changes);
}
