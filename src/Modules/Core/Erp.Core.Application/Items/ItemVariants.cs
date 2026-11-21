using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Items;

public sealed record ItemAttributeDto(int Id, string Code, string Name, int SortOrder, bool IsActive, byte[] RowVersion);

public sealed record ItemAttributeValueDto(int Id, int AttributeId, string Code, string Name, int SortOrder, bool IsActive);

/// <summary>Atributele de varianta si valorile lor (02.01, 3.6 punctul 13).</summary>
public sealed record ItemAttributePageDto(IReadOnlyList<ItemAttributeDto> Attributes, IReadOnlyList<ItemAttributeValueDto> Values);

/// <summary>O valoare in salvarea atributului; Id null = valoare noua.</summary>
public sealed record ItemAttributeValueInput(int? Id, string Code, string Name, int SortOrder, bool IsActive);

/// <summary>O varianta a articolului; ValueIds = valorile ei („3,12”).</summary>
public sealed record ItemVariantItemDto(int Id, int ItemId, string Code, string Name, string ValueIds, bool IsActive, int BarcodeCount, byte[] RowVersion);

/// <summary>Variantele articolului si atributele pe care le folosesc.</summary>
public sealed record ItemVariantPageDto(IReadOnlyList<ItemVariantItemDto> Variants, IReadOnlyList<int> AttributeIds);

/// <summary>Accesul la date pentru atribute si variante (core.usp_ItemAttribute_*, usp_ItemVariant_*; NET-06).</summary>
public interface IItemVariantStore
{
    Task<ItemAttributePageDto> ListAttributesAsync(CancellationToken ct);

    Task<SavedEntity> SaveAttributeAsync(SaveItemAttribute command, CancellationToken ct);

    Task<ItemVariantPageDto> ListVariantsAsync(int itemId, CancellationToken ct);

    Task<int> GenerateAsync(int itemId, IReadOnlyList<int> valueIds, int userId, CancellationToken ct);

    Task<SavedEntity> SaveVariantAsync(SaveItemVariant command, int userId, CancellationToken ct);
}

[Module("Core")]
[AuthenticatedUser]
public sealed record GetItemAttributes : IQuery<ItemAttributePageDto>;

/// <summary>Atribut de varianta cu valorile lui; valorile nu se sterg, se dezactiveaza.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NomenclatureManage)]
[Audited("Core.ItemAttribute.Save")]
public sealed record SaveItemAttribute(int? Id, string Code, string Name, int SortOrder, bool IsActive, byte[]? RowVersion,
    IReadOnlyList<ItemAttributeValueInput> Values) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.ItemView)]
public sealed record GetItemVariants(int ItemId) : IQuery<ItemVariantPageDto>;

/// <summary>Genereaza combinatiile lipsa din valorile alese; intoarce numarul variantelor noi.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ItemManage)]
[Audited("Core.ItemVariant.Generate")]
public sealed record GenerateItemVariants(int ItemId, IReadOnlyList<int> ValueIds) : ICommand<int>;

[Module("Core")]
[RequiresPermission(CorePermissions.ItemManage)]
[Audited("Core.ItemVariant.Save")]
public sealed record SaveItemVariant(int Id, int ItemId, string Code, string Name, bool IsActive, byte[] RowVersion) : ICommand<SavedEntity>;

internal sealed class SaveItemAttributeValidator : AbstractValidator<SaveItemAttribute>
{
    public SaveItemAttributeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.").Matches("^[A-Za-z0-9_]{1,20}$").WithMessage("Codul are 1–20 litere, cifre sau „_”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(100).WithMessage("Denumirea are cel mult 100 de caractere.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        RuleForEach(x => x.Values).ChildRules(v =>
        {
            v.RuleFor(x => x.Code).NotEmpty().WithMessage("Codul valorii este obligatoriu.").Matches("^[A-Za-z0-9_]{1,20}$")
                .WithMessage("Codul valorii are 1–20 litere, cifre sau „_” (apare în codul variantei).");
            v.RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea valorii este obligatorie.").MaximumLength(100).WithMessage("Denumirea are cel mult 100 de caractere.");
        });
    }
}

internal sealed class GenerateItemVariantsValidator : AbstractValidator<GenerateItemVariants>
{
    public GenerateItemVariantsValidator() => RuleFor(x => x.ValueIds).NotEmpty().WithMessage("Alegeți valorile atributelor.");
}

internal sealed class SaveItemVariantValidator : AbstractValidator<SaveItemVariant>
{
    public SaveItemVariantValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.").MaximumLength(50).WithMessage("Codul are cel mult 50 de caractere.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(250).WithMessage("Denumirea are cel mult 250 de caractere.");
    }
}

internal sealed class GetItemAttributesHandler(IItemVariantStore store) : IQueryHandler<GetItemAttributes, ItemAttributePageDto>
{
    public Task<ItemAttributePageDto> Handle(GetItemAttributes request, CancellationToken cancellationToken) => store.ListAttributesAsync(cancellationToken);
}

internal sealed class SaveItemAttributeHandler(IItemVariantStore store, IAuditScope audit) : ICommandHandler<SaveItemAttribute, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveItemAttribute request, CancellationToken cancellationToken)
    {
        var command = request with
        {
            Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim(),
            Values = [.. request.Values.Select(v => v with { Code = v.Code.Trim().ToUpperInvariant(), Name = v.Name.Trim() })],
        };
        var saved = await store.SaveAttributeAsync(command, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ItemAttribute", saved.Id, new { command.Code, command.Name, command.IsActive, Values = command.Values.Select(v => v.Code) });
        return saved;
    }
}

internal sealed class GetItemVariantsHandler(IItemVariantStore store, IOwnerAccess owners) : IQueryHandler<GetItemVariants, ItemVariantPageDto>
{
    public async Task<ItemVariantPageDto> Handle(GetItemVariants request, CancellationToken cancellationToken)
    {
        await owners.EnsureVisibleAsync(OwnedEntities.Item, request.ItemId, CorePermissions.ItemView, cancellationToken).ConfigureAwait(false);
        return await store.ListVariantsAsync(request.ItemId, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GenerateItemVariantsHandler(IItemVariantStore store, IOwnerAccess owners, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<GenerateItemVariants, int>
{
    public async Task<int> Handle(GenerateItemVariants request, CancellationToken cancellationToken)
    {
        await owners.EnsureManageAsync(OwnedEntities.Item, request.ItemId, null, CorePermissions.ItemManage, cancellationToken).ConfigureAwait(false);
        var created = await store.GenerateAsync(request.ItemId, [.. request.ValueIds.Distinct()], context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.Item", request.ItemId, new { VariantsCreated = created, request.ValueIds });
        return created;
    }
}

internal sealed class SaveItemVariantHandler(IItemVariantStore store, IOwnerAccess owners, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveItemVariant, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveItemVariant request, CancellationToken cancellationToken)
    {
        await owners.EnsureManageAsync(OwnedEntities.Item, request.ItemId, null, CorePermissions.ItemManage, cancellationToken).ConfigureAwait(false);
        var command = request with { Code = request.Code.Trim(), Name = request.Name.Trim() };
        var saved = await store.SaveVariantAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ItemVariant", saved.Id, new { command.ItemId, command.Code, command.Name, command.IsActive });
        return saved;
    }
}
