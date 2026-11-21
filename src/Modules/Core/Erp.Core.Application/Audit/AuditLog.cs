using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Application.Audit;

/// <summary>
/// Un rand din Configurare › Audit (macheta 07h): cand, cine, actiunea, entitatea si inregistrarea. IsFirst = prima intrare a
/// inregistrarii (o salvare care a creat-o).
/// </summary>
public sealed record AuditEntryItemDto(long Id, DateTime OccurredAt, int UserId, string UserName, string Action, string EntityType, long? EntityId,
    string? RecordLabel, string? CompanyName, bool IsFirst);

/// <summary>
/// Pagina jurnalului si valorile filtrelor din intervalul ales. AllCompanies = utilizatorul are dreptul de audit pe toate firmele,
/// deci vede si intrarile fara firma si jurnalul de autentificari.
/// </summary>
public sealed record AuditPageDto(IReadOnlyList<AuditEntryItemDto> Entries, int Total, bool AllCompanies, IReadOnlyList<UserOptionDto> Users,
    IReadOnlyList<string> EntityTypes, IReadOnlyList<string> Actions);

/// <summary>
/// Detaliul unei intrari (doar citire). Changes = valorile de dupa modificare (JSON); PreviousChanges = intrarea anterioara a aceleiasi
/// inregistrari, din care interfata face tabelul Camp / Inainte / Dupa.
/// </summary>
public sealed record AuditEntryDetailDto(long Id, DateTime OccurredAt, int UserId, string UserName, string Login, int? CompanyId, string? CompanyName,
    string Action, string EntityType, long? EntityId, string? RecordLabel, string? Changes, string? PreviousChanges, DateTime? PreviousAt,
    string? IpAddress, Guid CorrelationId);

/// <summary>O incercare de autentificare (core.LoginEvent; 02.01, 9).</summary>
public sealed record LoginEventDto(long Id, DateTime OccurredAt, string UserName, int? UserId, string? DisplayName, bool Success, string? FailureReason,
    string? IpAddress, string? UserAgent);

public sealed record LoginEventPageDto(IReadOnlyList<LoginEventDto> Events, int Total, bool AllCompanies);

public sealed record AuditSearchCriteria(int UserId, DateTime? From, DateTime? To, int? ActorUserId, string? EntityType, long? EntityId, string? Action,
    string? Search, int Offset, int PageSize);

public sealed record LoginEventCriteria(int UserId, DateTime? From, DateTime? To, string? Search, bool? Success, int Offset, int PageSize);

/// <summary>Citirea jurnalului de audit si a jurnalului de autentificari (core.usp_Audit_*, core.usp_LoginEvent_Search; NET-06).</summary>
public interface IAuditLogStore
{
    Task<AuditPageDto> SearchAsync(AuditSearchCriteria criteria, CancellationToken ct);

    Task<AuditEntryDetailDto?> GetAsync(int userId, long id, CancellationToken ct);

    Task<LoginEventPageDto> SearchLoginsAsync(LoginEventCriteria criteria, CancellationToken ct);

    Task<IReadOnlyList<PartnerHistoryEntryDto>> GetCompanyHistoryAsync(int companyId, CancellationToken ct);
}

/// <summary>Configurare › Audit: cautare paginata, cele mai noi intai. From inclusiv, To exclusiv; fara From = de la inceput.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.AuditView)]
public sealed record SearchAudit(DateTimeOffset? From = null, DateTimeOffset? To = null, int? UserId = null, string? EntityType = null,
    long? EntityId = null, string? Action = null, string? Search = null, int Page = 1, int PageSize = 50) : IQuery<AuditPageDto>;

/// <summary>Detaliul unei intrari de audit, cu valorile de dinainte (macheta 07h, panoul din dreapta).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.AuditView)]
public sealed record GetAuditEntry(long Id) : IQuery<AuditEntryDetailDto>;

/// <summary>Jurnalul de autentificari (02.01, 9): incercarile reusite si esuate, cu IP-ul si motivul refuzului.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.AuditView)]
public sealed record SearchLoginEvents(DateTimeOffset? From = null, DateTimeOffset? To = null, string? Search = null, bool? Success = null,
    int Page = 1, int PageSize = 50) : IQuery<LoginEventPageDto>;

/// <summary>Tab-ul Istoric al fisei firmei (02.01, 11): firma si punctele de lucru, locatiile, adresele, contactele, conturile, seriile, dimensiunile.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.CompanyView)]
public sealed record GetCompanyHistory(int Id) : IQuery<IReadOnlyList<PartnerHistoryEntryDto>>;

internal static class AuditSearchText
{
    /// <summary>Textul cautat, protejat pentru LIKE; null daca e gol.</summary>
    public static string? Like(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var text = search.Trim();
        text = text.Length > 100 ? text[..100] : text;
        return text.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);
    }

    public static (int Offset, int PageSize) Page(int page, int pageSize)
    {
        var size = Math.Clamp(pageSize, 1, 200);
        return ((Math.Max(page, 1) - 1) * size, size);
    }
}

internal sealed class SearchAuditHandler(IAuditLogStore store, ICurrentContext context) : IQueryHandler<SearchAudit, AuditPageDto>
{
    public Task<AuditPageDto> Handle(SearchAudit request, CancellationToken cancellationToken)
    {
        var (offset, size) = AuditSearchText.Page(request.Page, request.PageSize);
        return store.SearchAsync(new AuditSearchCriteria(context.UserId, request.From?.UtcDateTime, request.To?.UtcDateTime, request.UserId,
            string.IsNullOrWhiteSpace(request.EntityType) ? null : request.EntityType.Trim(), request.EntityId,
            string.IsNullOrWhiteSpace(request.Action) ? null : request.Action.Trim(), AuditSearchText.Like(request.Search), offset, size), cancellationToken);
    }
}

internal sealed class GetAuditEntryHandler(IAuditLogStore store, ICurrentContext context) : IQueryHandler<GetAuditEntry, AuditEntryDetailDto>
{
    public async Task<AuditEntryDetailDto> Handle(GetAuditEntry request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.UserId, request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Intrarea de audit nu există.");
}

internal sealed class SearchLoginEventsHandler(IAuditLogStore store, ICurrentContext context) : IQueryHandler<SearchLoginEvents, LoginEventPageDto>
{
    public Task<LoginEventPageDto> Handle(SearchLoginEvents request, CancellationToken cancellationToken)
    {
        var (offset, size) = AuditSearchText.Page(request.Page, request.PageSize);
        return store.SearchLoginsAsync(new LoginEventCriteria(context.UserId, request.From?.UtcDateTime, request.To?.UtcDateTime,
            AuditSearchText.Like(request.Search), request.Success, offset, size), cancellationToken);
    }
}

internal sealed class GetCompanyHistoryHandler(IAuditLogStore store, ICurrentContext context) : IQueryHandler<GetCompanyHistory, IReadOnlyList<PartnerHistoryEntryDto>>
{
    public Task<IReadOnlyList<PartnerHistoryEntryDto>> Handle(GetCompanyHistory request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.Id);
        return store.GetCompanyHistoryAsync(request.Id, cancellationToken);
    }
}
