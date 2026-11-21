using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Items;

/// <summary>Un cod al articolului la un partener, pentru tab-ul „Coduri la parteneri” (ReferenceType: 1 client, 2 furnizor).</summary>
public sealed record ItemReferenceItemDto(int Id, int ItemId, int PartnerId, string PartnerCode, string PartnerName, byte ReferenceType, string Code,
    string? Description, int? UomId, bool IsActive, byte[] RowVersion);

/// <summary>O traducere a denumirii articolului (LanguageCode ISO 639-1, fara „ro”).</summary>
public sealed record ItemTranslationDto(string LanguageCode, string Name);

/// <summary>Accesul la date pentru codurile la parteneri si traduceri (core.usp_ItemReference_*, usp_ItemTranslation_*, NET-06).</summary>
public interface IItemCatalogStore
{
    Task<IReadOnlyList<ItemReferenceItemDto>> ListReferencesAsync(int itemId, CancellationToken ct);

    Task<SavedEntity> SaveReferenceAsync(SaveItemReference command, int userId, CancellationToken ct);

    Task<IReadOnlyList<ItemTranslationDto>> ListTranslationsAsync(int itemId, CancellationToken ct);

    Task SaveTranslationsAsync(int itemId, IReadOnlyList<ItemTranslationDto> translations, int userId, CancellationToken ct);

    Task<ItemReferenceDto?> FindReferenceAsync(int partnerId, byte referenceType, string? code, int? itemId, CancellationToken ct);

    Task<string?> GetNameAsync(int itemId, string? language, CancellationToken ct);
}

[Module("Core")]
[RequiresPermission(CorePermissions.ItemView)]
public sealed record GetItemReferences(int ItemId) : IQuery<IReadOnlyList<ItemReferenceItemDto>>;

/// <summary>Codul articolului la un client sau furnizor; nu se sterge, se dezactiveaza.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ItemManage)]
[Audited("Core.ItemReference.Save")]
public sealed record SaveItemReference(int? Id, int ItemId, int PartnerId, byte ReferenceType, string Code, string? Description, int? UomId, bool IsActive,
    byte[]? RowVersion) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.ItemView)]
public sealed record GetItemTranslations(int ItemId) : IQuery<IReadOnlyList<ItemTranslationDto>>;

/// <summary>Traducerile articolului, salvate impreuna: lista inlocuieste traducerile existente.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ItemManage)]
[Audited("Core.ItemTranslation.Save")]
public sealed record SaveItemTranslations(int ItemId, IReadOnlyList<ItemTranslationDto> Translations) : ICommand<int>;

internal sealed class SaveItemReferenceValidator : AbstractValidator<SaveItemReference>
{
    public SaveItemReferenceValidator()
    {
        RuleFor(x => x.ReferenceType).InclusiveBetween((byte)1, (byte)2).WithMessage("Alegeți dacă e codul unui client sau al unui furnizor.");
        RuleFor(x => x.PartnerId).GreaterThan(0).WithMessage("Alegeți partenerul.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.").MaximumLength(50).WithMessage("Codul are cel mult 50 de caractere.");
        RuleFor(x => x.Description).MaximumLength(250).WithMessage("Denumirea are cel mult 250 de caractere.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveItemTranslationsValidator : AbstractValidator<SaveItemTranslations>
{
    public SaveItemTranslationsValidator()
    {
        RuleFor(x => x.Translations).Must(list => list.Select(t => t.LanguageCode.Trim().ToLowerInvariant()).Distinct().Count() == list.Count)
            .WithMessage("O limbă apare de două ori.");
        RuleForEach(x => x.Translations).ChildRules(t =>
        {
            t.RuleFor(x => x.LanguageCode).Must(c => c?.Trim().ToLowerInvariant() is { Length: 2 } l && l.All(char.IsAsciiLetterLower) && l != "ro")
                .WithMessage("Limba e un cod ISO de două litere, alta decât „ro”.");
            t.RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea tradusă este obligatorie.")
                .MaximumLength(250).WithMessage("Denumirea are cel mult 250 de caractere.");
        });
    }
}

internal sealed class GetItemReferencesHandler(IItemCatalogStore store, IOwnerAccess owners) : IQueryHandler<GetItemReferences, IReadOnlyList<ItemReferenceItemDto>>
{
    public async Task<IReadOnlyList<ItemReferenceItemDto>> Handle(GetItemReferences request, CancellationToken cancellationToken)
    {
        await owners.EnsureVisibleAsync(OwnedEntities.Item, request.ItemId, CorePermissions.ItemView, cancellationToken).ConfigureAwait(false);
        return await store.ListReferencesAsync(request.ItemId, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SaveItemReferenceHandler(IItemCatalogStore store, IOwnerAccess owners, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveItemReference, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveItemReference request, CancellationToken cancellationToken)
    {
        await owners.EnsureManageAsync(OwnedEntities.Item, request.ItemId, null, CorePermissions.ItemManage, cancellationToken).ConfigureAwait(false);
        var command = request with { Code = request.Code.Trim(), Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim() };
        var saved = await store.SaveReferenceAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ItemPartnerReference", saved.Id, new
        {
            command.ItemId, command.PartnerId, command.ReferenceType, command.Code, command.Description, command.UomId, command.IsActive,
        });
        return saved;
    }
}

internal sealed class GetItemTranslationsHandler(IItemCatalogStore store, IOwnerAccess owners)
    : IQueryHandler<GetItemTranslations, IReadOnlyList<ItemTranslationDto>>
{
    public async Task<IReadOnlyList<ItemTranslationDto>> Handle(GetItemTranslations request, CancellationToken cancellationToken)
    {
        await owners.EnsureVisibleAsync(OwnedEntities.Item, request.ItemId, CorePermissions.ItemView, cancellationToken).ConfigureAwait(false);
        return await store.ListTranslationsAsync(request.ItemId, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SaveItemTranslationsHandler(IItemCatalogStore store, IOwnerAccess owners, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveItemTranslations, int>
{
    public async Task<int> Handle(SaveItemTranslations request, CancellationToken cancellationToken)
    {
        await owners.EnsureManageAsync(OwnedEntities.Item, request.ItemId, null, CorePermissions.ItemManage, cancellationToken).ConfigureAwait(false);
        var list = request.Translations.Select(t => new ItemTranslationDto(t.LanguageCode.Trim().ToLowerInvariant(), t.Name.Trim())).ToList();
        await store.SaveTranslationsAsync(request.ItemId, list, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.Item", request.ItemId, new { Translations = list.ToDictionary(t => t.LanguageCode, t => t.Name) });
        return list.Count;
    }
}

/// <summary>IItemCatalog (02.01, 6) pentru module, peste aceleasi proceduri.</summary>
public sealed class ItemCatalog(IItemCatalogStore store) : IItemCatalog
{
    public Task<ItemReferenceDto?> FindByPartnerCodeAsync(int partnerId, byte referenceType, string code, CancellationToken ct) =>
        store.FindReferenceAsync(partnerId, referenceType, code.Trim(), null, ct);

    public Task<ItemReferenceDto?> GetPartnerCodeAsync(int itemId, int partnerId, byte referenceType, CancellationToken ct) =>
        store.FindReferenceAsync(partnerId, referenceType, null, itemId, ct);

    public async Task<string> GetItemNameAsync(int itemId, string? language, CancellationToken ct) =>
        await store.GetNameAsync(itemId, string.IsNullOrWhiteSpace(language) || language == "ro" ? null : language, ct).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Articolul nu există.");
}
