using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Partners;

/// <summary>O pereche posibila de dubluri; Reasons: Name, Email, Phone, Iban (separate prin virgula).</summary>
public sealed record DuplicatePairDto(int FirstId, int SecondId, string Reasons, string FirstCode, string FirstName, string? FirstFiscalCode,
    string SecondCode, string SecondName, string? SecondFiscalCode);

/// <summary>Accesul la date pentru dubluri (core.usp_Duplicate_Find, usp_DuplicateIgnore_Save, usp_Partner_Merge, usp_Item_Merge).</summary>
public interface IDuplicateStore
{
    Task<IReadOnlyList<DuplicatePairDto>> FindAsync(byte entityType, CancellationToken ct);

    Task IgnoreAsync(byte entityType, int firstId, int secondId, int userId, CancellationToken ct);

    Task<SavedEntity> MergeAsync(byte entityType, int keepId, int removeId, int userId, CancellationToken ct);
}

/// <summary>Tipul inregistrarilor: 1 parteneri (Core.Partner.Manage), 2 articole (Core.Item.Manage).</summary>
public static class DuplicateEntity
{
    public const byte Partner = 1;
    public const byte Item = 2;

    internal static void EnsureAllowed(ICurrentContext context, byte entityType)
    {
        var permission = entityType == Item ? CorePermissions.ItemManage : CorePermissions.PartnerManage;
        if (entityType is not (Partner or Item))
        {
            throw new ErpException(ErpErrorKind.Validation, "CORE_VALIDATION", "Alegeți partenerii sau articolele.");
        }

        if (!context.HasPermission(permission))
        {
            throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, "Nu aveți dreptul să modificați aceste înregistrări.");
        }
    }
}

/// <summary>Configurare › Dubluri (3.6 punctul 7): perechile propuse, cele mai sigure intai.</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record FindDuplicates(byte EntityType) : IQuery<IReadOnlyList<DuplicatePairDto>>;

/// <summary>„Nu sunt dubluri” pentru doi parteneri: perechea nu se mai propune.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerManage)]
[Audited("Core.Duplicate.Ignore")]
public sealed record IgnorePartnerDuplicate(int FirstId, int SecondId) : ICommand<int>;

/// <summary>„Nu sunt dubluri” pentru doua articole.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ItemManage)]
[Audited("Core.Duplicate.Ignore")]
public sealed record IgnoreItemDuplicate(int FirstId, int SecondId) : ICommand<int>;

/// <summary>Unirea a doi parteneri: RemoveId trece in KeepId si se dezactiveaza; modulele primesc PartenerUnificat.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerManage)]
[Audited("Core.Duplicate.Merge")]
public sealed record MergePartners(int KeepId, int RemoveId) : ICommand<SavedEntity>;

/// <summary>Unirea a doua articole; modulele primesc ArticolUnificat.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ItemManage)]
[Audited("Core.Duplicate.Merge")]
public sealed record MergeItems(int KeepId, int RemoveId) : ICommand<SavedEntity>;

internal sealed class MergePartnersValidator : AbstractValidator<MergePartners>
{
    public MergePartnersValidator() => RuleFor(x => x.RemoveId).NotEqual(x => x.KeepId).WithMessage("Alegeți doi parteneri diferiți.");
}

internal sealed class MergeItemsValidator : AbstractValidator<MergeItems>
{
    public MergeItemsValidator() => RuleFor(x => x.RemoveId).NotEqual(x => x.KeepId).WithMessage("Alegeți două articole diferite.");
}

internal sealed class FindDuplicatesHandler(IDuplicateStore store, ICurrentContext context) : IQueryHandler<FindDuplicates, IReadOnlyList<DuplicatePairDto>>
{
    public Task<IReadOnlyList<DuplicatePairDto>> Handle(FindDuplicates request, CancellationToken cancellationToken)
    {
        DuplicateEntity.EnsureAllowed(context, request.EntityType);
        return store.FindAsync(request.EntityType, cancellationToken);
    }
}

internal sealed class DuplicateActions(IDuplicateStore store, ICurrentContext context, IOwnerAccess owners, IOutbox outbox, IAuditScope audit)
{
    public async Task<int> IgnoreAsync(byte entityType, int firstId, int secondId, CancellationToken ct)
    {
        await store.IgnoreAsync(entityType, firstId, secondId, context.UserId, ct).ConfigureAwait(false);
        audit.Describe(entityType == DuplicateEntity.Item ? "Core.Item" : "Core.Partner", firstId, new { NotDuplicateOf = secondId });
        return 1;
    }

    public async Task<SavedEntity> MergeAsync(byte entityType, int keepId, int removeId, CancellationToken ct)
    {
        var (entity, permission, auditType) = entityType == DuplicateEntity.Item
            ? (OwnedEntities.Item, CorePermissions.ItemManage, "Core.Item") : (OwnedEntities.Partner, CorePermissions.PartnerManage, "Core.Partner");
        await owners.EnsureManageAsync(entity, keepId, null, permission, ct).ConfigureAwait(false);
        await owners.EnsureManageAsync(entity, removeId, null, permission, ct).ConfigureAwait(false);

        var saved = await store.MergeAsync(entityType, keepId, removeId, context.UserId, ct).ConfigureAwait(false);
        IntegrationEvent evt = entityType == DuplicateEntity.Item
            ? new ArticolUnificat(keepId, removeId) { CompanyId = context.CompanyId }
            : new PartenerUnificat(keepId, removeId) { CompanyId = context.CompanyId };
        await outbox.EnqueueAsync(evt, ct).ConfigureAwait(false);
        audit.Describe(auditType, keepId, new { MergedFrom = removeId });
        audit.Describe(auditType, removeId, new { MergedInto = keepId, IsActive = false });
        return saved;
    }
}

internal sealed class IgnorePartnerDuplicateHandler(DuplicateActions actions) : ICommandHandler<IgnorePartnerDuplicate, int>
{
    public Task<int> Handle(IgnorePartnerDuplicate request, CancellationToken cancellationToken) =>
        actions.IgnoreAsync(DuplicateEntity.Partner, request.FirstId, request.SecondId, cancellationToken);
}

internal sealed class IgnoreItemDuplicateHandler(DuplicateActions actions) : ICommandHandler<IgnoreItemDuplicate, int>
{
    public Task<int> Handle(IgnoreItemDuplicate request, CancellationToken cancellationToken) =>
        actions.IgnoreAsync(DuplicateEntity.Item, request.FirstId, request.SecondId, cancellationToken);
}

internal sealed class MergePartnersHandler(DuplicateActions actions) : ICommandHandler<MergePartners, SavedEntity>
{
    public Task<SavedEntity> Handle(MergePartners request, CancellationToken cancellationToken) =>
        actions.MergeAsync(DuplicateEntity.Partner, request.KeepId, request.RemoveId, cancellationToken);
}

internal sealed class MergeItemsHandler(DuplicateActions actions) : ICommandHandler<MergeItems, SavedEntity>
{
    public Task<SavedEntity> Handle(MergeItems request, CancellationToken cancellationToken) =>
        actions.MergeAsync(DuplicateEntity.Item, request.KeepId, request.RemoveId, cancellationToken);
}
