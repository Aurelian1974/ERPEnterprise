using Erp.Core.Application.Partners;
using Erp.Core.Application.Settings;
using Erp.Core.Contracts;

namespace Erp.Core.Application.Users;

/// <summary>
/// Lista utilizatorilor (macheta 06): cautare dupa nume, nume de utilizator, email sau marca. Kind: 1 angajati, 2 externi, 3 conturi de
/// sistem. Filter: 1 inactivi, 2 expira curand (60 de zile), 3 fara 2FA. CompanyId / RoleId: au un rol pe firma / au rolul.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
public sealed record SearchUsers(string? Search = null, byte? Kind = null, byte? Filter = null, int? CompanyId = null, int? RoleId = null,
    int Page = 1, int PageSize = 50) : IQuery<UserPageDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
public sealed record GetUser(int Id) : IQuery<UserDetailDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
public sealed record GetUserLookups : IQuery<UserLookupsDto>;

/// <summary>Tab-ul Istoric: modificarile contului si actiunile de securitate.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
public sealed record GetUserHistory(int Id) : IQuery<IReadOnlyList<PartnerHistoryEntryDto>>;

/// <summary>Tab-ul Sesiuni: sesiunile active ale contului de autentificare.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
public sealed record GetUserSessions(int Id) : IQuery<IReadOnlyList<IdentityAccountSession>>;

internal sealed class SearchUsersHandler(IUserStore store, ILicenseInfo license, TimeProvider clock) : IQueryHandler<SearchUsers, UserPageDto>
{
    public Task<UserPageDto> Handle(SearchUsers request, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : Escape(request.Search.Trim());
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(request.Page, 1);
        var criteria = new UserSearchCriteria(search, request.Kind is >= 1 and <= 3 ? request.Kind : null,
            request.Filter is >= 1 and <= 3 ? request.Filter : null, request.CompanyId, request.RoleId,
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), (page - 1) * pageSize, pageSize);
        return store.SearchAsync(criteria, license.GetLimit("users"), cancellationToken);
    }

    // Textul cautat ajunge intr-un LIKE: caracterele speciale se cauta literal.
    private static string Escape(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);
}

internal sealed class GetUserHandler(IUserStore store) : IQueryHandler<GetUser, UserDetailDto>
{
    public async Task<UserDetailDto> Handle(GetUser request, CancellationToken cancellationToken) =>
        await store.GetAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Utilizatorul nu există.");
}

internal sealed class GetUserLookupsHandler(IUserStore store, ISettingsStore settings) : IQueryHandler<GetUserLookups, UserLookupsDto>
{
    public async Task<UserLookupsDto> Handle(GetUserLookups request, CancellationToken cancellationToken) =>
        await store.GetLookupsAsync(await UserRules.ExternalAccessMaxMonthsAsync(settings, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
}

internal sealed class GetUserHistoryHandler(IUserStore store) : IQueryHandler<GetUserHistory, IReadOnlyList<PartnerHistoryEntryDto>>
{
    public Task<IReadOnlyList<PartnerHistoryEntryDto>> Handle(GetUserHistory request, CancellationToken cancellationToken) =>
        store.GetHistoryAsync(request.Id, cancellationToken);
}

internal sealed class GetUserSessionsHandler(IUserStore store, IIdentityAccountAdmin identity)
    : IQueryHandler<GetUserSessions, IReadOnlyList<IdentityAccountSession>>
{
    public async Task<IReadOnlyList<IdentityAccountSession>> Handle(GetUserSessions request, CancellationToken cancellationToken)
    {
        var user = await UserAccounts.GetAsync(store, request.Id, cancellationToken).ConfigureAwait(false);
        return await identity.GetSessionsAsync(user.SubjectId, cancellationToken).ConfigureAwait(false);
    }
}
