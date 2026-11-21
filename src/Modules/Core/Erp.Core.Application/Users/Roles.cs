using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Users;

/// <summary>O permisiune din catalog (Modul.Resursa.Actiune); lista vine din cod, nu se editeaza.</summary>
public sealed record PermissionDto(int Id, string Code, string ModuleCode, string Description);

/// <summary>
/// Un rol cu permisiunile lui (macheta 07e). HasAllPermissions = rolul de sistem ADMIN, care are toate drepturile, inclusiv cele
/// adaugate ulterior, si nu se editeaza. UserCount = utilizatorii cu rolul in vederea ceruta (firma sau toate firmele).
/// </summary>
public sealed record RoleDto(int Id, string Code, string Name, string? Description, bool IsSystem, bool HasAllPermissions, int UserCount,
    byte[] RowVersion, IReadOnlyList<int> PermissionIds);

/// <summary>Un utilizator din vederea „Roluri și utilizatori”, cu rolurile lui pe firma aleasa (sau pe oricare firma).</summary>
public sealed record MatrixUserDto(int Id, string DisplayName, string UserName, bool IsActive, IReadOnlyList<int> RoleIds);

/// <summary>Exceptia unui utilizator: G = drept adaugat, R = drept revocat; CompanyId null = pe toate firmele.</summary>
public sealed record PermissionOverrideDto(int UserId, int PermissionId, int? CompanyId, string Effect, string? Reason);

/// <summary>
/// Configurare › Roluri: catalogul (doar modulele licentiate), rolurile, firmele, utilizatorii cu roluri in vedere si exceptiile lor.
/// Pe o firma se vad exceptiile ei si cele pentru toate firmele; pe toate firmele (CompanyId null) doar cele pentru toate firmele.
/// </summary>
public sealed record RoleMatrixDto(int? CompanyId, IReadOnlyList<PermissionDto> Permissions, IReadOnlyList<RoleDto> Roles,
    IReadOnlyList<CompanyOptionDto> Companies, IReadOnlyList<MatrixUserDto> Users, IReadOnlyList<PermissionOverrideDto> Overrides);

/// <summary>O exceptie trimisa la salvare: Effect „G” (adaugat, cu motiv obligatoriu) sau „R” (revocat).</summary>
public sealed record PermissionOverrideInput(int PermissionId, string Effect, string? Reason);

public sealed record RoleRecord(int? Id, string Code, string Name, string? Description, IReadOnlyList<int> PermissionIds, byte[]? RowVersion);

/// <summary>Accesul la date pentru roluri si exceptiile de drepturi (implementat in Infrastructure, NET-06).</summary>
public interface IRoleStore
{
    Task<RoleMatrixDto> GetMatrixAsync(int? companyId, CancellationToken ct);

    Task<SavedEntity> SaveAsync(RoleRecord role, int actorUserId, CancellationToken ct);

    Task DeleteAsync(int id, byte[] rowVersion, CancellationToken ct);

    Task SaveOverridesAsync(int userId, int? companyId, IReadOnlyList<PermissionOverrideInput> overrides, int actorUserId, CancellationToken ct);
}

/// <summary>Configurare › Roluri (02.01, 8 si 11): ambele vederi, „Pe rol” (macheta 07e) si „Roluri și utilizatori”.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.RoleManage)]
public sealed record GetRoleMatrix(int? CompanyId = null) : IQuery<RoleMatrixDto>;

/// <summary>Creare sau modificare de rol: lista de permisiuni inlocuieste permisiunile rolului (macheta 07e).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.RoleManage)]
[Audited("Core.Role.Save")]
public sealed record SaveRole(int? Id, string Code, string Name, string? Description, IReadOnlyList<int> PermissionIds, byte[]? RowVersion)
    : ICommand<SavedEntity>;

/// <summary>Stergerea unui rol al clientului, neatribuit; rolurile de sistem nu se sterg.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.RoleManage)]
[Audited("Core.Role.Delete")]
public sealed record DeleteRole(int Id, byte[] RowVersion) : ICommand<bool>;

/// <summary>
/// Exceptiile de drepturi ale unui utilizator pe o firma (CompanyId null = toate firmele), din vederea „Roluri și utilizatori”
/// (deciziile lui Aurelian din 21.11.2026): lista inlocuieste exceptiile lui pe acea firma. Un drept adaugat cere motivul; revocarea
/// castiga asupra rolurilor. Nimeni nu-si schimba propriile drepturi.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.RoleManage)]
[Audited("Core.User.PermissionOverride")]
public sealed record SaveUserPermissionOverrides(int UserId, int? CompanyId, IReadOnlyList<PermissionOverrideInput> Overrides) : ICommand<bool>;

public sealed record DeleteRoleBody(byte[] RowVersion);

public sealed record SaveUserPermissionOverridesBody(int? CompanyId, IReadOnlyList<PermissionOverrideInput> Overrides);

internal static class PermissionOverrideEffects
{
    public const string Grant = "G";
    public const string Revoke = "R";
}

internal sealed class SaveRoleValidator : AbstractValidator<SaveRole>
{
    public SaveRoleValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.")
            .Matches("^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$").WithMessage("Codul are 1–50 de caractere: litere, cifre, „.”, „_” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200).WithMessage("Denumirea are cel mult 200 de caractere.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Descrierea are cel mult 500 de caractere.");
        RuleFor(x => x.PermissionIds).NotNull().Must(p => p is null || p.Distinct().Count() == p.Count).WithMessage("Un drept apare de două ori.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class DeleteRoleValidator : AbstractValidator<DeleteRole>
{
    public DeleteRoleValidator() => RuleFor(x => x.RowVersion).NotEmpty().WithMessage("Lipsește versiunea înregistrării.");
}

internal sealed class SaveUserPermissionOverridesValidator : AbstractValidator<SaveUserPermissionOverrides>
{
    public SaveUserPermissionOverridesValidator()
    {
        RuleFor(x => x.Overrides).NotNull().Must(o => o is null || o.Select(x => x.PermissionId).Distinct().Count() == o.Count)
            .WithMessage("Un drept apare de două ori.");
        RuleForEach(x => x.Overrides).ChildRules(o =>
        {
            o.RuleFor(x => x.Effect).Must(e => e is PermissionOverrideEffects.Grant or PermissionOverrideEffects.Revoke)
                .WithMessage("Excepția este fie un drept adăugat, fie unul revocat.");
            o.RuleFor(x => x.Reason).NotEmpty().When(x => x.Effect == PermissionOverrideEffects.Grant)
                .WithMessage("Motivul este obligatoriu pentru un drept adăugat.");
            o.RuleFor(x => x.Reason).MaximumLength(500).WithMessage("Motivul are cel mult 500 de caractere.");
        });
    }
}

internal sealed class GetRoleMatrixHandler(IRoleStore store, ILicenseInfo license) : IQueryHandler<GetRoleMatrix, RoleMatrixDto>
{
    public async Task<RoleMatrixDto> Handle(GetRoleMatrix request, CancellationToken cancellationToken)
    {
        var matrix = await store.GetMatrixAsync(request.CompanyId, cancellationToken).ConfigureAwait(false);
        // Modulele nelicentiate nu apar in matrice (02.01, 11); drepturile lor raman salvate pe roluri.
        return matrix with { Permissions = [.. matrix.Permissions.Where(p => license.IsModuleActive(p.ModuleCode))] };
    }
}

internal sealed class SaveRoleHandler(IRoleStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveRole, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveRole request, CancellationToken cancellationToken)
    {
        var record = new RoleRecord(request.Id, request.Code.Trim(), request.Name.Trim(), UserRules.Clean(request.Description),
            request.PermissionIds, request.RowVersion);
        var saved = await store.SaveAsync(record, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.Role", saved.Id, new { record.Code, record.Name, record.Description, PermissionIds = record.PermissionIds.Order() });
        return saved;
    }
}

internal sealed class DeleteRoleHandler(IRoleStore store, IAuditScope audit) : ICommandHandler<DeleteRole, bool>
{
    public async Task<bool> Handle(DeleteRole request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Id, request.RowVersion, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.Role", request.Id, new { Deleted = true });
        return true;
    }
}

internal sealed class SaveUserPermissionOverridesHandler(IRoleStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveUserPermissionOverrides, bool>
{
    public async Task<bool> Handle(SaveUserPermissionOverrides request, CancellationToken cancellationToken)
    {
        // Separarea drepturilor (02.01, 8): nimeni nu-si acorda singur drepturi si nu-si schimba propriile exceptii.
        if (request.UserId == context.UserId)
        {
            throw ErpException.Conflict("CORE_USER_SELF_ROLES", "Nu vă puteți schimba propriile drepturi; cereți altui administrator.");
        }

        var overrides = request.Overrides
            .Select(o => o with { Reason = UserRules.Clean(o.Reason) })
            .ToList();
        await store.SaveOverridesAsync(request.UserId, request.CompanyId, overrides, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.User", request.UserId, new
        {
            PermissionOverrides = new
            {
                request.CompanyId,
                Overrides = overrides.OrderBy(o => o.PermissionId).Select(o => new { o.PermissionId, o.Effect, o.Reason }),
            },
        });
        return true;
    }
}
