using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;

namespace Erp.Core.Application.Users;

/// <summary>Tipurile de acces ale unui utilizator extern (macheta 06c); TECHNICAL = cont tehnic, fara parola interactiva.</summary>
public static class ExternalAccessTypes
{
    public const string Auditor = "AUDITOR";
    public const string Consultant = "CONSULTANT";
    public const string Accountant = "ACCOUNTANT";
    public const string Technical = "TECHNICAL";

    public static readonly IReadOnlyList<string> All = [Auditor, Consultant, Accountant, Technical];
}

/// <summary>O atribuire de rol in randul din lista (macheta 06, coloana „Firme și roluri”); CompanyId null = toata instalarea.</summary>
public sealed record UserRoleSummaryDto(int? CompanyId, string? CompanyName, string RoleCode, string RoleName);

/// <summary>
/// Un rand din lista utilizatorilor (macheta 06). TwoFactorEnabled null = fara cont de autentificare. EmployeeLeaveDate = data
/// incetarii contractului angajatului legat.
/// </summary>
public sealed record UserListItemDto(int Id, string UserName, string DisplayName, string? Email, bool IsActive, bool IsSystem, bool IsEmployee,
    string? EmployeeNo, DateOnly? EmployeeLeaveDate, string? ExternalAccessType, string? OrganizationName, DateOnly? ValidUntil,
    bool? TwoFactorEnabled, DateTime? LastLoginAt, IReadOnlyList<UserRoleSummaryDto> Roles);

/// <summary>
/// Indicatorii din antetul listei: activi (fara conturile de sistem, cei care consuma licenta) din LicensedUsers (null = fara limita),
/// angajati, externi, externi care expira in 60 de zile, activi fara 2FA, inactivi.
/// </summary>
public sealed record UserCountersDto(int ActiveCount, int? LicensedUsers, int EmployeeCount, int ExternalCount, int ExpiringCount,
    int WithoutTwoFactorCount, int InactiveCount);

public sealed record UserPageDto(IReadOnlyList<UserListItemDto> Items, int Total, UserCountersDto Counters);

public sealed record UserRoleAssignmentDto(int Id, int RoleId, string RoleCode, string RoleName, int? CompanyId, string? CompanyName,
    string? DimensionScope);

/// <summary>Atribuirea trimisa la salvare: rolul pe firma (null = toata instalarea, doar ADMIN).</summary>
public sealed record UserRoleInput(int RoleId, int? CompanyId);

/// <summary>
/// Fisa utilizatorului (machetele 06b, 06c). Datele angajatului legat sunt doar pentru citire (vin din fisa lui). Securitatea vine
/// din contul de autentificare: HasAccount = false pentru un utilizator fara cont ids (de exemplu creat inainte de administrare).
/// </summary>
public sealed record UserDetailDto(int Id, string UserName, string DisplayName, string? FirstName, string? LastName, string? Email, string? Phone,
    bool IsActive, bool IsSystem, bool IsEmployee, int? EmployeeId, string? EmployeeNo, int? EmployeeCompanyId, string? EmployeeCompanyName,
    string? EmployeeBranchName, string? EmployeeDepartment, string? EmployeeJobTitle, string? EmployeePhone, DateOnly? EmployeeHireDate,
    DateOnly? EmployeeLeaveDate, bool? EmployeeIsActive, string? ExternalAccessType, int? ExternalPartnerId, string? ExternalPartnerName,
    string? ExternalPartnerFiscalCode, int? SponsorUserId, string? SponsorName, string? AccessReason, DateOnly? ValidFrom, DateOnly? ValidUntil,
    string Language, int? LastCompanyId, DateTime? LastLoginAt, DateTime CreatedAt, string CreatedByName, DateTime LastChangedAt,
    string ModifiedByName, bool HasAccount, bool HasPassword, DateTime? PasswordChangedAt, bool MustChangePassword, bool TwoFactorEnabled,
    bool TwoFactorRequired, DateTime? LockedUntil, int ActiveSessions, byte[] RowVersion, IReadOnlyList<UserRoleAssignmentDto> Roles,
    IReadOnlyList<NotificationPreferenceDto> NotificationPreferences);

public sealed record RoleOptionDto(int Id, string Code, string Name, bool IsSystem);

public sealed record CompanyOptionDto(int Id, string Code, string Name);

/// <summary>
/// Listele fisei: roluri, firme active, responsabili interni posibili, numarul maxim de luni al accesului extern si catalogul
/// tipurilor de notificare (tab-ul Preferinte).
/// </summary>
public sealed record UserLookupsDto(IReadOnlyList<RoleOptionDto> Roles, IReadOnlyList<CompanyOptionDto> Companies,
    IReadOnlyList<UserOptionDto> Sponsors, int ExternalAccessMaxMonths, IReadOnlyList<NotificationTypeDto> NotificationTypes);

/// <summary>Rezultatul crearii sau al unei resetari: parola temporara se arata o singura data administratorului (null = fara parola).</summary>
public sealed record UserSavedDto(int Id, byte[] RowVersion, string? TemporaryPassword);

public sealed record TemporaryPasswordDto(string TemporaryPassword);

public sealed record UserSearchCriteria(string? Search, byte? Kind, byte? Filter, int? CompanyId, int? RoleId, DateOnly Today, int Offset, int PageSize);

/// <summary>
/// Utilizatorul pregatit pentru salvare (numele de utilizator cu litere mici, campurile tipului curatate). NotificationPreferences =
/// JSON-ul preferintelor, scris doar cu SaveNotificationPreferences = true.
/// </summary>
public sealed record UserRecord(int? Id, Guid? SubjectId, string UserName, bool IsEmployee, int? EmployeeId, string? FirstName, string? LastName,
    string? Email, string? Phone, string? ExternalAccessType, int? ExternalPartnerId, int? SponsorUserId, string? AccessReason, DateOnly? ValidFrom,
    DateOnly? ValidUntil, string Language, int? LastCompanyId, bool IsActive, byte[]? RowVersion, bool SaveNotificationPreferences = false,
    string? NotificationPreferences = null);

/// <summary>Ce a salvat core.usp_User_Save: datele contului de autentificare de sincronizat si starea anterioara.</summary>
public sealed record UserSaveResult(int Id, byte[] RowVersion, Guid SubjectId, string UserName, string DisplayName, string? Email, bool IsActive,
    bool WasActive, bool IsSystem, bool IsEmployee, string? ExternalAccessType, bool IsAdmin);

/// <summary>Contul unui utilizator, pentru actiunile de securitate (fara date personale).</summary>
public sealed record UserAccountRef(int Id, Guid SubjectId, string UserName, string? Email, bool IsActive, bool IsSystem, bool IsEmployee,
    string? ExternalAccessType, bool IsAdmin, byte[] RowVersion);

/// <summary>Accesul la date pentru utilizatori (implementat in Infrastructure, NET-06).</summary>
public interface IUserStore
{
    Task<UserPageDto> SearchAsync(UserSearchCriteria criteria, int? licensedUsers, CancellationToken ct);

    Task<UserDetailDto?> GetAsync(int id, CancellationToken ct);

    Task<UserAccountRef?> GetAccountAsync(int id, CancellationToken ct);

    Task<UserLookupsDto> GetLookupsAsync(int externalAccessMaxMonths, CancellationToken ct);

    Task<IReadOnlyList<PartnerHistoryEntryDto>> GetHistoryAsync(int id, CancellationToken ct);

    Task<UserSaveResult> SaveAsync(UserRecord user, int? maxActiveUsers, int actorUserId, CancellationToken ct);

    /// <summary>Inlocuieste rolurile utilizatorului; intoarce true daca are dupa salvare rolul ADMIN.</summary>
    Task<bool> SaveRolesAsync(int userId, IReadOnlyList<UserRoleInput> roles, int actorUserId, CancellationToken ct);
}
