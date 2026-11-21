using Erp.Core.Application.Organization;
using Erp.Core.Application.Settings;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Users;

/// <summary>
/// Creare sau modificare de utilizator (02.01, 3.4; machetele 06b, 06c): contul din Nucleu si contul de autentificare (ids), in
/// aceeasi tranzactie. Un utilizator nou primeste o parola temporara (schimbare obligatorie la prima autentificare), aratata o
/// singura data; un cont tehnic (extern cu tip TECHNICAL) nu are parola. Kind: 1 angajat, 2 extern, 3 cont de sistem (doar
/// administratorul integrat, creat de installer; nu se creeaza de aici). Roles = null lasa rolurile neschimbate; altfel le
/// inlocuieste pe toate (tab-ul „Roluri și firme”). NotificationPreferences = null lasa preferintele de notificare neschimbate;
/// altfel le inlocuieste (tab-ul Preferinte; tipurile lipsa revin la implicitele catalogului).
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
[Audited("Core.User.Save")]
public sealed record SaveUser(int? Id, string UserName, byte Kind, int? EmployeeId, string? FirstName, string? LastName, string? Email,
    string? Phone, string? ExternalAccessType, int? ExternalPartnerId, int? SponsorUserId, string? AccessReason, DateOnly? ValidFrom,
    DateOnly? ValidUntil, string Language, int? LastCompanyId, bool IsActive, byte[]? RowVersion, IReadOnlyList<UserRoleInput>? Roles = null,
    IReadOnlyList<NotificationPreferenceInput>? NotificationPreferences = null) : ICommand<UserSavedDto>;

/// <summary>„Resetează parola” (macheta 06b): parola temporara noua, aratata o singura data; sesiunile utilizatorului se inchid.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
[Audited("Core.User.ResetPassword")]
public sealed record ResetUserPassword(int Id) : ICommand<TemporaryPasswordDto>;

/// <summary>Deblocarea contului blocat dupa incercari gresite (02.01, 8: Core.User.Manage).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
[Audited("Core.User.Unlock")]
public sealed record UnlockUser(int Id) : ICommand<bool>;

/// <summary>Resetarea 2FA (telefon pierdut): utilizatorul activeaza din nou aplicatia de autentificare la urmatoarea autentificare.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
[Audited("Core.User.ResetTwoFactor")]
public sealed record ResetUserTwoFactor(int Id) : ICommand<bool>;

/// <summary>Inchide o sesiune a utilizatorului (SessionId) sau pe toate (null), din tab-ul Sesiuni.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.UserManage)]
[Audited("Core.User.RevokeSessions")]
public sealed record RevokeUserSessions(int Id, Guid? SessionId) : ICommand<bool>;

internal static class UserRules
{
    public const byte Employee = 1;
    public const byte External = 2;
    public const byte SystemAccount = 3;

    public const string UserNamePattern = "^[A-Za-z0-9][A-Za-z0-9._@-]{1,49}$";
    public const int DefaultExternalAccessMaxMonths = 12;
    public static readonly string[] Languages = ["ro", "en"];

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>2FA obligatoriu pentru externi si pentru rolul Administrator (3.4); contul tehnic nu se autentifica interactiv.</summary>
    public static bool TwoFactorRequired(bool isEmployee, bool isSystem, string? externalAccessType, bool isAdmin) =>
        externalAccessType != ExternalAccessTypes.Technical && (isAdmin || (!isEmployee && !isSystem));

    public static async Task<int> ExternalAccessMaxMonthsAsync(ISettingsStore settings, CancellationToken ct)
    {
        var value = (await settings.GetInstallationAsync(SettingKeys.ExternalAccessMaxMonths, ct).ConfigureAwait(false)).Value;
        return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var months)
            && months is >= 1 and <= 120 ? months : DefaultExternalAccessMaxMonths;
    }

    public static ErpValidationException Field(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}

internal sealed class SaveUserValidator : AbstractValidator<SaveUser>
{
    public SaveUserValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithMessage("Numele de utilizator este obligatoriu.")
            .Matches(UserRules.UserNamePattern).WithMessage("Numele de utilizator are 2–50 de caractere: litere, cifre, „.”, „_”, „-” sau „@”.");
        RuleFor(x => x.Language).Must(l => UserRules.Languages.Contains(l)).WithMessage("Alegeți limba.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        RuleFor(x => x.Kind).InclusiveBetween(UserRules.Employee, UserRules.SystemAccount).WithMessage("Alegeți tipul utilizatorului.");
        RuleFor(x => x.EmployeeId).NotNull().When(x => x.Kind == UserRules.Employee).WithMessage("Alegeți angajatul.");
        RuleFor(x => x.FirstName).MaximumLength(100).WithMessage("Prenumele are cel mult 100 de caractere.");
        RuleFor(x => x.LastName).MaximumLength(100).WithMessage("Numele are cel mult 100 de caractere.");

        When(x => x.Kind == UserRules.External, () =>
        {
            RuleFor(x => x.FirstName).NotEmpty().WithMessage("Prenumele este obligatoriu.").MaximumLength(100);
            RuleFor(x => x.LastName).NotEmpty().WithMessage("Numele este obligatoriu.").MaximumLength(100);
            RuleFor(x => x.ExternalAccessType).Must(t => t is not null && ExternalAccessTypes.All.Contains(t))
                .WithMessage("Alegeți tipul accesului.");
            RuleFor(x => x.Email).NotEmpty().When(x => x.ExternalAccessType != ExternalAccessTypes.Technical)
                .WithMessage("E-mailul este obligatoriu pentru un utilizator extern.");
            RuleFor(x => x.ExternalPartnerId).NotNull().WithMessage("Alegeți organizația (partenerul).");
            RuleFor(x => x.SponsorUserId).NotNull().WithMessage("Alegeți responsabilul intern.");
            RuleFor(x => x.AccessReason).NotEmpty().WithMessage("Motivul accesului este obligatoriu.")
                .MaximumLength(500).WithMessage("Motivul are cel mult 500 de caractere.");
            RuleFor(x => x.ValidFrom).NotNull().WithMessage("Data de început a accesului este obligatorie.");
            RuleFor(x => x.ValidUntil).NotNull().WithMessage("Data de expirare a accesului este obligatorie.");
            RuleFor(x => x.ValidUntil).GreaterThanOrEqualTo(x => x.ValidFrom).When(x => x.ValidFrom is not null && x.ValidUntil is not null)
                .WithMessage("Accesul nu poate expira înainte să înceapă.");
        });

        RuleFor(x => x.Roles).Must(r => r is null || r.Select(Key).Distinct().Count() == r.Count)
            .WithMessage("Același rol apare de două ori pe același nod.");
        RuleForEach(x => x.Roles).Must(a => a.CompanyGroupId is null || (a.CompanyId is null && a.BranchId is null && a.LocationId is null))
            .WithMessage("Un rol se atribuie fie pe un grup, fie pe o firmă (cu punctul de lucru sau locația ei).")
            .Must(a => a.BranchId is null || a.LocationId is null).WithMessage("Alegeți fie punctul de lucru, fie locația.");
        this.ContactDetails(x => x.Phone, x => x.Email);
    }

    /// <summary>Nodul atribuirii; punctul de lucru si locatia contin firma, asa ca firma se ignora langa ele.</summary>
    internal static (int RoleId, int? GroupId, int? CompanyId, int? BranchId, int? LocationId) Key(UserRoleInput a) =>
        (a.RoleId, a.CompanyGroupId, a.BranchId is null && a.LocationId is null ? a.CompanyId : null, a.BranchId, a.LocationId);
}

internal sealed class SaveUserHandler(IUserStore store, IIdentityAccountAdmin identity, ISettingsStore settings, ILicenseInfo license,
    ICurrentContext context, IAuditScope audit, IOutbox outbox) : ICommandHandler<SaveUser, UserSavedDto>
{
    public async Task<UserSavedDto> Handle(SaveUser request, CancellationToken cancellationToken)
    {
        var userName = request.UserName.Trim().ToLowerInvariant();
        var external = request.Kind == UserRules.External;
        var employee = request.Kind == UserRules.Employee;
        if (external && request.ValidFrom is { } from && request.ValidUntil is { } until)
        {
            var maxMonths = await UserRules.ExternalAccessMaxMonthsAsync(settings, cancellationToken).ConfigureAwait(false);
            if (until > from.AddMonths(maxMonths))
            {
                throw UserRules.Field("validUntil", $"Accesul extern durează cel mult {maxMonths} luni; pentru mai mult se prelungește ulterior.");
            }
        }

        if (request.Id is null && request.Kind == UserRules.SystemAccount)
        {
            throw ErpException.Conflict("CORE_SYSTEM_USER_PROTECTED", "Conturile de sistem se creează doar la instalare.");
        }

        if (request.NotificationPreferences is not null && NotificationPreferenceRules.Validate(request.NotificationPreferences) is { Count: > 0 } errors)
        {
            throw new ErpValidationException(errors);
        }

        UserAccountRef? previous = null;
        if (request.Id is { } id)
        {
            previous = await store.GetAccountAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Utilizatorul nu există.");
            if (previous.IsSystem != (request.Kind == UserRules.SystemAccount))
            {
                throw ErpException.Conflict("CORE_SYSTEM_USER_PROTECTED", "Tipul unui cont de sistem nu se schimbă.");
            }
            if (id == context.UserId)
            {
                // Separarea drepturilor (02.01, 8): nimeni nu-si schimba singur rolurile si nu-si inchide singur contul.
                if (!request.IsActive)
                {
                    throw UserRules.Field("isActive", "Nu vă puteți dezactiva propriul cont.");
                }

                if (request.Roles is not null && !await SameRolesAsync(id, request.Roles, cancellationToken).ConfigureAwait(false))
                {
                    throw ErpException.Conflict("CORE_USER_SELF_ROLES", "Nu vă puteți schimba propriile roluri; cereți altui administrator.");
                }
            }
        }

        var record = new UserRecord(request.Id, request.Id is null ? Guid.NewGuid() : null, userName, employee,
            employee ? request.EmployeeId : null, UserRules.Clean(request.FirstName), UserRules.Clean(request.LastName),
            UserRules.Clean(request.Email)?.ToLowerInvariant(), external ? UserRules.Clean(request.Phone) : null,
            external ? request.ExternalAccessType : null, external ? request.ExternalPartnerId : null, external ? request.SponsorUserId : null,
            external ? UserRules.Clean(request.AccessReason) : null, external ? request.ValidFrom : null, external ? request.ValidUntil : null,
            request.Language, request.LastCompanyId, request.IsActive, request.RowVersion, request.NotificationPreferences is not null,
            request.NotificationPreferences is null ? null : NotificationPreferenceRules.Serialize(request.NotificationPreferences));
        var saved = await store.SaveAsync(record, license.GetLimit("users"), context.UserId, cancellationToken).ConfigureAwait(false);

        var isAdmin = saved.IsAdmin;
        if (request.Roles is not null)
        {
            isAdmin = await store.SaveRolesAsync(saved.Id, request.Roles, context.UserId, cancellationToken).ConfigureAwait(false);
        }

        var twoFactor = UserRules.TwoFactorRequired(saved.IsEmployee, saved.IsSystem, saved.ExternalAccessType, isAdmin);
        string? temporaryPassword = null;
        if (previous is null)
        {
            temporaryPassword = await identity.CreateAsync(saved.SubjectId, saved.UserName, saved.Email,
                interactive: saved.ExternalAccessType != ExternalAccessTypes.Technical, twoFactor, context.UserId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var invalidate = (saved.WasActive && !saved.IsActive) || !string.Equals(previous.UserName, saved.UserName, StringComparison.Ordinal);
            await identity.UpdateAsync(saved.SubjectId, saved.UserName, saved.Email, saved.IsActive, twoFactor, invalidate, context.UserId,
                cancellationToken).ConfigureAwait(false);
        }

        if (saved.WasActive && !saved.IsActive)
        {
            await outbox.EnqueueAsync(new UtilizatorDezactivat(saved.Id, saved.SubjectId), cancellationToken).ConfigureAwait(false);
        }

        // Fara date personale in audit (NET-18): numele, emailul si telefonul nu se scriu.
        audit.Describe("Core.User", saved.Id, new
        {
            saved.UserName,
            Kind = saved.IsSystem ? "System" : saved.IsEmployee ? "Employee" : "External",
            record.EmployeeId, record.ExternalAccessType, record.ExternalPartnerId, record.SponsorUserId, record.ValidFrom, record.ValidUntil,
            record.Language, record.LastCompanyId, saved.IsActive, TwoFactorRequired = twoFactor,
            Roles = request.Roles?.Select(r => new { r.RoleId, r.CompanyId }),
            NotificationPreferences = request.NotificationPreferences?.Select(p => new { p.Code, p.InApp, p.Email, p.EmailMode }),
            AccountCreated = previous is null ? true : (bool?)null,
            TemporaryPasswordIssued = temporaryPassword is null ? (bool?)null : true,
        });
        return new UserSavedDto(saved.Id, saved.RowVersion, temporaryPassword);
    }

    private async Task<bool> SameRolesAsync(int id, IReadOnlyList<UserRoleInput> roles, CancellationToken ct)
    {
        var current = (await store.GetAsync(id, ct).ConfigureAwait(false))?.Roles ?? [];
        return current.Select(r => SaveUserValidator.Key(new UserRoleInput(r.RoleId, r.CompanyId, r.CompanyGroupId, r.BranchId, r.LocationId)))
            .ToHashSet().SetEquals(roles.Select(SaveUserValidator.Key));
    }
}

/// <summary>Contul pe care se aplica o actiune de securitate; contul tehnic „system” nu se deschide.</summary>
internal static class UserAccounts
{
    public static async Task<UserAccountRef> GetAsync(IUserStore store, int id, CancellationToken ct) =>
        await store.GetAccountAsync(id, ct).ConfigureAwait(false) ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Utilizatorul nu există.");
}

internal sealed class ResetUserPasswordHandler(IUserStore store, IIdentityAccountAdmin identity, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ResetUserPassword, TemporaryPasswordDto>
{
    public async Task<TemporaryPasswordDto> Handle(ResetUserPassword request, CancellationToken cancellationToken)
    {
        var user = await UserAccounts.GetAsync(store, request.Id, cancellationToken).ConfigureAwait(false);
        if (user.ExternalAccessType == ExternalAccessTypes.Technical)
        {
            throw ErpException.Conflict("CORE_USER_TECHNICAL_ACCOUNT", "Un cont tehnic nu are parolă; se folosește cu o cheie API.");
        }

        var password = await identity.ResetPasswordAsync(user.SubjectId, user.UserName, user.Email,
            UserRules.TwoFactorRequired(user.IsEmployee, user.IsSystem, user.ExternalAccessType, user.IsAdmin), context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Core.User", user.Id, new { user.UserName, TemporaryPasswordIssued = true, SessionsRevoked = true });
        return new TemporaryPasswordDto(password);
    }
}

internal sealed class UnlockUserHandler(IUserStore store, IIdentityAccountAdmin identity, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<UnlockUser, bool>
{
    public async Task<bool> Handle(UnlockUser request, CancellationToken cancellationToken)
    {
        var user = await UserAccounts.GetAsync(store, request.Id, cancellationToken).ConfigureAwait(false);
        await identity.UnlockAsync(user.SubjectId, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.User", user.Id, new { user.UserName, Unlocked = true });
        return true;
    }
}

internal sealed class ResetUserTwoFactorHandler(IUserStore store, IIdentityAccountAdmin identity, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ResetUserTwoFactor, bool>
{
    public async Task<bool> Handle(ResetUserTwoFactor request, CancellationToken cancellationToken)
    {
        var user = await UserAccounts.GetAsync(store, request.Id, cancellationToken).ConfigureAwait(false);
        await identity.ResetTwoFactorAsync(user.SubjectId, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.User", user.Id, new { user.UserName, TwoFactorReset = true, SessionsRevoked = true });
        return true;
    }
}

internal sealed class RevokeUserSessionsHandler(IUserStore store, IIdentityAccountAdmin identity, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<RevokeUserSessions, bool>
{
    public async Task<bool> Handle(RevokeUserSessions request, CancellationToken cancellationToken)
    {
        var user = await UserAccounts.GetAsync(store, request.Id, cancellationToken).ConfigureAwait(false);
        await identity.RevokeSessionsAsync(user.SubjectId, request.SessionId, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.User", user.Id, new { user.UserName, request.SessionId, AllSessions = request.SessionId is null ? true : (bool?)null });
        return true;
    }
}
