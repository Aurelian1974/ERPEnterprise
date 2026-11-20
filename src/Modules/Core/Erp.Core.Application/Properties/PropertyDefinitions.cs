using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Properties;

/// <summary>O optiune a unei proprietati de tip lista; Id null = optiune noua.</summary>
public sealed record PropertyOptionInput(int? Id, string Code, string Label, int SortOrder, bool IsActive);

public sealed record PropertyOptionAdminDto(int Id, int PropertyDefinitionId, string Code, string Label, int SortOrder, bool IsActive);

/// <summary>
/// Definitia unei proprietati in Configurare › Proprietati (macheta 07l). DataType: 1 text, 2 numar, 3 data, 4 da/nu, 5 lista,
/// 6 lista multipla, 7 utilizator; AppliesTo: 1 client, 2 furnizor, 3 ambele; CompanyId null = tot grupul. UsageCount = cate
/// inregistrari au valori (tipul unei proprietati folosite nu se mai schimba).
/// </summary>
public sealed record PropertyDefinitionAdminDto(int Id, string EntityType, string Code, string Name, byte DataType, byte AppliesTo, string Section,
    int SortOrder, bool IsRequired, bool IsFilterable, int? CompanyId, bool IsActive, int UsageCount, byte[] RowVersion,
    IReadOnlyList<PropertyOptionAdminDto> Options);

public interface IPropertyDefinitionStore
{
    Task<IReadOnlyList<PropertyDefinitionAdminDto>> ListAsync(string entityType, CancellationToken ct);

    Task<SavedEntity> SaveAsync(SavePropertyDefinition command, int userId, CancellationToken ct);
}

public static class PropertyEntityTypes
{
    public const string Partner = "Partner";
    public const string Item = "Item";
}

[Module("Core")]
[RequiresPermission(CorePermissions.PropertyManage)]
public sealed record ListPropertyDefinitions(string EntityType) : IQuery<IReadOnlyList<PropertyDefinitionAdminDto>>;

/// <summary>Creare sau modificare de proprietate cu optiunile ei (o singura salvare, macheta 07l).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PropertyManage)]
[Audited("Core.PropertyDefinition.Save")]
public sealed record SavePropertyDefinition(int? Id, string EntityType, string Code, string Name, byte DataType, byte AppliesTo, string Section,
    int SortOrder, bool IsRequired, bool IsFilterable, int? CompanyId, bool IsActive, IReadOnlyList<PropertyOptionInput> Options, byte[]? RowVersion)
    : ICommand<SavedEntity>;

internal sealed class ListPropertyDefinitionsHandler(IPropertyDefinitionStore store) : IQueryHandler<ListPropertyDefinitions, IReadOnlyList<PropertyDefinitionAdminDto>>
{
    public Task<IReadOnlyList<PropertyDefinitionAdminDto>> Handle(ListPropertyDefinitions request, CancellationToken cancellationToken) =>
        request.EntityType is PropertyEntityTypes.Partner or PropertyEntityTypes.Item
            ? store.ListAsync(request.EntityType, cancellationToken)
            : throw new ErpValidationException(new Dictionary<string, string[]> { ["entityType"] = ["Entitatea nu are proprietăți."] });
}

internal sealed class SavePropertyDefinitionValidator : AbstractValidator<SavePropertyDefinition>
{
    public SavePropertyDefinitionValidator()
    {
        RuleFor(x => x.EntityType).Must(e => e is PropertyEntityTypes.Partner or PropertyEntityTypes.Item).WithMessage("Entitatea nu are proprietăți.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.").Matches("^[A-Za-z][A-Za-z0-9_]{0,29}$")
            .WithMessage("Codul are 1–30 caractere: litere, cifre sau „_”, începând cu literă.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Eticheta este obligatorie.").MaximumLength(100).WithMessage("Eticheta are cel mult 100 de caractere.");
        RuleFor(x => x.Section).NotEmpty().WithMessage("Secțiunea este obligatorie.").MaximumLength(50).WithMessage("Secțiunea are cel mult 50 de caractere.");
        RuleFor(x => x.DataType).InclusiveBetween((byte)1, (byte)7).WithMessage("Alegeți tipul de date.");
        RuleFor(x => x.AppliesTo).InclusiveBetween((byte)1, (byte)3).WithMessage("Alegeți cui se aplică.");
        RuleFor(x => x.AppliesTo).Equal((byte)3).When(x => x.EntityType == PropertyEntityTypes.Item)
            .WithMessage("Proprietățile articolelor se aplică tuturor articolelor.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 9999).WithMessage("Ordinea este între 0 și 9999.");
        // Lista partenerilor filtreaza dupa o optiune sau dupa da/nu (core.usp_Partner_Search).
        RuleFor(x => x.IsFilterable).Equal(false).When(x => x.DataType is not (4 or 5 or 6))
            .WithMessage("Ca filtru se pot folosi doar listele și proprietățile da/nu.");
        RuleFor(x => x.Options).Must(o => o.Any(x => x.IsActive)).When(x => x.DataType is 5 or 6)
            .WithMessage("O listă are cel puțin o opțiune activă.");
        RuleFor(x => x.Options).Empty().When(x => x.DataType is not (5 or 6)).WithMessage("Doar listele au opțiuni.");
        RuleForEach(x => x.Options).ChildRules(option =>
        {
            option.RuleFor(o => o.Code).NotEmpty().Matches("^[A-Za-z0-9][A-Za-z0-9_-]{0,29}$")
                .WithMessage("Codul opțiunii are 1–30 caractere: litere, cifre, „_” sau „-”.");
            option.RuleFor(o => o.Label).NotEmpty().WithMessage("Eticheta opțiunii este obligatorie.").MaximumLength(100);
        });
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SavePropertyDefinitionHandler(IPropertyDefinitionStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SavePropertyDefinition, SavedEntity>
{
    public async Task<SavedEntity> Handle(SavePropertyDefinition request, CancellationToken cancellationToken)
    {
        var command = request with
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Section = request.Section.Trim(),
            Options = [.. request.Options.Select(o => o with { Code = o.Code.Trim().ToUpperInvariant(), Label = o.Label.Trim() })],
        };
        var saved = await store.SaveAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.PropertyDefinition", saved.Id, new
        {
            command.EntityType, command.Code, command.Name, command.DataType, command.AppliesTo, command.Section, command.IsRequired,
            command.IsFilterable, command.CompanyId, command.IsActive, Options = command.Options.Select(o => $"{o.Code}={o.Label}{(o.IsActive ? "" : " (inactivă)")}"),
        });
        return saved;
    }
}
