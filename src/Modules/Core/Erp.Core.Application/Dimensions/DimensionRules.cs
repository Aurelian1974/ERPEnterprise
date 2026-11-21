using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Dimensions;

/// <summary>O regula salvata pe o sursa (partener, articol, locatie) a firmei.</summary>
public sealed record DefaultDimensionRule(DimensionSource Source, string DimensionType, int? ValueId, DimensionPosting Posting);

/// <summary>O combinatie blocata: doua tipuri (fara valori) sau o pereche de valori.</summary>
public sealed record BlockedCombination(string TypeA, int? ValueAId, string TypeB, int? ValueBId);

/// <summary>
/// Combinarea regulilor si validarea (02.01, 3.6 punctul 14), fara acces la date. Valoarea propusa vine din prima sursa care are una
/// (articolul, apoi partenerul, apoi locatia); regula e cea mai stricta dintre surse (fixa, obligatorie, optionala).
/// </summary>
public static class DimensionRuleEngine
{
    private static int Rank(DimensionSource source) => source switch
    {
        DimensionSource.Item => 0,
        DimensionSource.Partner => 1,
        _ => 2,
    };

    public static IReadOnlyList<DimensionDefault> Merge(IEnumerable<DefaultDimensionRule> rules) =>
        [.. rules.GroupBy(r => r.DimensionType).Select(g =>
        {
            var ordered = g.OrderBy(r => Rank(r.Source)).ToList();
            var withValue = ordered.FirstOrDefault(r => r.ValueId is not null && r.Posting == DimensionPosting.Fixed)
                ?? ordered.FirstOrDefault(r => r.ValueId is not null);
            return new DimensionDefault(g.Key, withValue?.ValueId, ordered.Max(r => r.Posting), withValue?.Source ?? ordered[0].Source);
        }).OrderBy(d => d.DimensionType, StringComparer.Ordinal)];

    public static IReadOnlyList<DimensionIssue> Validate(IReadOnlyDictionary<string, int?> values, IEnumerable<DefaultDimensionRule> rules,
        IEnumerable<BlockedCombination> combinations)
    {
        var issues = new List<DimensionIssue>();
        var list = rules.ToList();
        foreach (var group in list.GroupBy(r => r.DimensionType))
        {
            var value = values.TryGetValue(group.Key, out var v) ? v : null;
            var fixedValues = group.Where(r => r.Posting == DimensionPosting.Fixed).Select(r => r.ValueId).Distinct().ToList();
            if (fixedValues.Count > 1)
            {
                issues.Add(new DimensionIssue(group.Key, "CORE_DIMENSION_CONFLICT",
                    $"Partenerul, articolul sau locația cer valori fixe diferite pentru dimensiunea {group.Key}."));
            }
            else if (fixedValues.Count == 1 && value != fixedValues[0])
            {
                issues.Add(new DimensionIssue(group.Key, "CORE_DIMENSION_FIXED", $"Dimensiunea {group.Key} are o valoare fixă care nu se poate schimba."));
            }
            else if (value is null && group.Any(r => r.Posting == DimensionPosting.Mandatory))
            {
                issues.Add(new DimensionIssue(group.Key, "CORE_DIMENSION_REQUIRED", $"Dimensiunea {group.Key} este obligatorie."));
            }
        }

        foreach (var c in combinations)
        {
            var a = values.TryGetValue(c.TypeA, out var va) ? va : null;
            var b = values.TryGetValue(c.TypeB, out var vb) ? vb : null;
            var blocked = c.ValueAId is null ? a is not null && b is not null : a == c.ValueAId && b == c.ValueBId;
            if (blocked)
            {
                issues.Add(new DimensionIssue(c.TypeA, "CORE_DIMENSION_COMBINATION_BLOCKED", c.ValueAId is null
                    ? $"Dimensiunile {c.TypeA} și {c.TypeB} nu se folosesc împreună."
                    : $"Combinația de valori pentru {c.TypeA} și {c.TypeB} este blocată."));
            }
        }

        return issues;
    }
}

public sealed record DimensionRuleRows(IReadOnlyList<DefaultDimensionRule> Rules, IReadOnlyList<BlockedCombination> Combinations);

/// <summary>Un rand din editorul dimensiunilor implicite: tipul activ, valoarea si regula salvate (Posting null = fara regula).</summary>
public sealed record DefaultDimensionRowDto(string DimensionType, string TypeName, int? DimensionValueId, byte? ValuePosting);

public sealed record DimensionValueOptionDto(int Id, string DimensionType, string Code, string Name);

public sealed record DefaultDimensionPageDto(IReadOnlyList<DefaultDimensionRowDto> Rows, IReadOnlyList<DimensionValueOptionDto> Values);

public sealed record DefaultDimensionInput(string DimensionType, int? DimensionValueId, byte ValuePosting);

public sealed record DimensionCombinationDto(int Id, string TypeA, string TypeAName, int? ValueAId, string? ValueACode, string TypeB, string TypeBName,
    int? ValueBId, string? ValueBCode, string? Note);

/// <summary>Accesul la date pentru tipuri, reguli implicite si combinatii (NET-06).</summary>
public interface IDimensionRuleStore
{
    Task<SavedEntity> SaveTypeAsync(SaveDimensionType command, CancellationToken ct);

    Task<DefaultDimensionPageDto> ListDefaultsAsync(int companyId, DimensionSource source, int sourceId, CancellationToken ct);

    Task SaveDefaultsAsync(int companyId, DimensionSource source, int sourceId, IReadOnlyList<DefaultDimensionInput> rows, int userId, CancellationToken ct);

    Task<IReadOnlyList<DimensionCombinationDto>> ListCombinationsAsync(int companyId, CancellationToken ct);

    Task<SavedEntity> SaveCombinationAsync(int companyId, SaveDimensionCombination command, int userId, CancellationToken ct);

    Task DeleteCombinationAsync(int companyId, int id, CancellationToken ct);

    Task<DimensionRuleRows> GetRulesAsync(int companyId, int? partnerId, int? itemId, int? locationId, CancellationToken ct);
}

/// <summary>Tip de dimensiune definit de client (3.6 punctul 15); codul e fix dupa creare. IsNew = creare.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.DimensionManage)]
[Audited("Core.DimensionType.Save")]
public sealed record SaveDimensionType(string Code, string Name, int SortOrder, bool IsActive, bool IsNew, byte[]? RowVersion) : ICommand<SavedEntity>;

/// <summary>Dimensiunile implicite ale unui partener, articol sau locatii pe firma curenta.</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record GetDefaultDimensions(byte Source, int SourceId) : IQuery<DefaultDimensionPageDto>;

/// <summary>Salvarea dimensiunilor implicite ale sursei pe firma curenta (lista inlocuieste regulile).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.DimensionManage)]
[Audited("Core.DefaultDimension.Save")]
public sealed record SaveDefaultDimensions(byte Source, int SourceId, IReadOnlyList<DefaultDimensionInput> Rows) : ICommand<int>;

[Module("Core")]
[RequiresPermission(CorePermissions.DimensionManage)]
public sealed record GetDimensionCombinations : IQuery<IReadOnlyList<DimensionCombinationDto>>;

[Module("Core")]
[RequiresPermission(CorePermissions.DimensionManage)]
[Audited("Core.DimensionCombination.Save")]
public sealed record SaveDimensionCombination(string TypeA, int? ValueAId, string TypeB, int? ValueBId, string? Note) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.DimensionManage)]
[Audited("Core.DimensionCombination.Delete")]
public sealed record DeleteDimensionCombination(int Id) : ICommand<int>;

internal static class DimensionTypeCode
{
    public static bool IsValid(string? code) => code is { Length: >= 2 and <= 20 } && char.IsAsciiLetterUpper(code[0])
        && code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');
}

internal sealed class SaveDimensionTypeValidator : AbstractValidator<SaveDimensionType>
{
    public SaveDimensionTypeValidator()
    {
        RuleFor(x => x.Code).Must(DimensionTypeCode.IsValid)
            .WithMessage("Codul are 2–20 caractere: litere mari, cifre sau „_”, începând cu o literă (de exemplu REGIUNE).");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(100).WithMessage("Denumirea are cel mult 100 de caractere.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 9999).WithMessage("Ordinea e între 0 și 9999.");
        RuleFor(x => x.RowVersion).NotNull().When(x => !x.IsNew).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveDefaultDimensionsValidator : AbstractValidator<SaveDefaultDimensions>
{
    public SaveDefaultDimensionsValidator()
    {
        RuleFor(x => x.Source).InclusiveBetween((byte)1, (byte)3).WithMessage("Sursa nu e validă.");
        RuleFor(x => x.Rows).Must(r => r.Select(x => x.DimensionType).Distinct(StringComparer.Ordinal).Count() == r.Count)
            .WithMessage("Un tip de dimensiune apare de două ori.");
        RuleForEach(x => x.Rows).ChildRules(r =>
        {
            r.RuleFor(x => x.ValuePosting).InclusiveBetween((byte)1, (byte)3).WithMessage("Regula nu e validă.");
            r.RuleFor(x => x.DimensionValueId).NotNull().When(x => x.ValuePosting == (byte)DimensionPosting.Fixed)
                .WithMessage("Regula „fixă” cere o valoare.");
        });
    }
}

internal sealed class SaveDimensionCombinationValidator : AbstractValidator<SaveDimensionCombination>
{
    public SaveDimensionCombinationValidator()
    {
        RuleFor(x => x.TypeA).NotEmpty().WithMessage("Alegeți primul tip.");
        RuleFor(x => x.TypeB).NotEmpty().WithMessage("Alegeți al doilea tip.").NotEqual(x => x.TypeA).WithMessage("Alegeți două tipuri diferite.");
        RuleFor(x => x.ValueBId).NotNull().When(x => x.ValueAId is not null).WithMessage("Alegeți ambele valori sau niciuna.");
        RuleFor(x => x.ValueAId).NotNull().When(x => x.ValueBId is not null).WithMessage("Alegeți ambele valori sau niciuna.");
        RuleFor(x => x.Note).MaximumLength(200).WithMessage("Nota are cel mult 200 de caractere.");
    }
}

internal static class DimensionCompany
{
    public static int Current(ICurrentContext context) => context.CompanyId != 0 ? context.CompanyId
        : throw new ErpException(ErpErrorKind.Validation, "CORE_COMPANY_REQUIRED", "Dimensiunile sunt pe firmă: alegeți firma din antet.");
}

internal sealed class SaveDimensionTypeHandler(IDimensionRuleStore store, IAuditScope audit) : ICommandHandler<SaveDimensionType, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveDimensionType request, CancellationToken cancellationToken)
    {
        var command = request with { Code = request.Code.Trim(), Name = request.Name.Trim() };
        var saved = await store.SaveTypeAsync(command, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.DimensionType", null, new { command.Code, command.Name, command.SortOrder, command.IsActive });
        return saved;
    }
}

internal sealed class GetDefaultDimensionsHandler(IDimensionRuleStore store, ICurrentContext context, IOwnerAccess owners)
    : IQueryHandler<GetDefaultDimensions, DefaultDimensionPageDto>
{
    public async Task<DefaultDimensionPageDto> Handle(GetDefaultDimensions request, CancellationToken cancellationToken)
    {
        var company = DimensionCompany.Current(context);
        await DefaultDimensionAccess.EnsureVisibleAsync(owners, context, (DimensionSource)request.Source, request.SourceId, cancellationToken).ConfigureAwait(false);
        return await store.ListDefaultsAsync(company, (DimensionSource)request.Source, request.SourceId, cancellationToken).ConfigureAwait(false);
    }
}

internal static class DefaultDimensionAccess
{
    /// <summary>Partenerul sau articolul trebuie sa fie vizibil (proprietarul, 3.4); locatia e verificata in procedura (a firmei).</summary>
    public static Task EnsureVisibleAsync(IOwnerAccess owners, ICurrentContext context, DimensionSource source, int id, CancellationToken ct) => source switch
    {
        DimensionSource.Partner => owners.EnsureVisibleAsync(OwnedEntities.Partner, id, CorePermissions.PartnerView, ct),
        DimensionSource.Item => owners.EnsureVisibleAsync(OwnedEntities.Item, id, CorePermissions.ItemView, ct),
        _ => context.HasPermission(CorePermissions.CompanyView) || context.HasPermission(CorePermissions.DimensionManage) ? Task.CompletedTask
            : throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, "Nu aveți dreptul să vedeți locațiile firmei."),
    };
}

internal sealed class SaveDefaultDimensionsHandler(IDimensionRuleStore store, ICurrentContext context, IOwnerAccess owners, IAuditScope audit)
    : ICommandHandler<SaveDefaultDimensions, int>
{
    public async Task<int> Handle(SaveDefaultDimensions request, CancellationToken cancellationToken)
    {
        var company = DimensionCompany.Current(context);
        var source = (DimensionSource)request.Source;
        await DefaultDimensionAccess.EnsureVisibleAsync(owners, context, source, request.SourceId, cancellationToken).ConfigureAwait(false);
        await store.SaveDefaultsAsync(company, source, request.SourceId, request.Rows, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe(source switch { DimensionSource.Partner => "Core.Partner", DimensionSource.Item => "Core.Item", _ => "Core.Location" }, request.SourceId,
            new { CompanyId = company, DefaultDimensions = request.Rows.ToDictionary(r => r.DimensionType, r => new { r.DimensionValueId, r.ValuePosting }) });
        return request.Rows.Count;
    }
}

internal sealed class GetDimensionCombinationsHandler(IDimensionRuleStore store, ICurrentContext context)
    : IQueryHandler<GetDimensionCombinations, IReadOnlyList<DimensionCombinationDto>>
{
    public Task<IReadOnlyList<DimensionCombinationDto>> Handle(GetDimensionCombinations request, CancellationToken cancellationToken) =>
        store.ListCombinationsAsync(DimensionCompany.Current(context), cancellationToken);
}

internal sealed class SaveDimensionCombinationHandler(IDimensionRuleStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveDimensionCombination, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveDimensionCombination request, CancellationToken cancellationToken)
    {
        var company = DimensionCompany.Current(context);
        var saved = await store.SaveCombinationAsync(company, request, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.DimensionCombination", saved.Id, new { CompanyId = company, request.TypeA, request.ValueAId, request.TypeB, request.ValueBId, request.Note });
        return saved;
    }
}

internal sealed class DeleteDimensionCombinationHandler(IDimensionRuleStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<DeleteDimensionCombination, int>
{
    public async Task<int> Handle(DeleteDimensionCombination request, CancellationToken cancellationToken)
    {
        await store.DeleteCombinationAsync(DimensionCompany.Current(context), request.Id, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.DimensionCombination", request.Id, new { Deleted = true });
        return request.Id;
    }
}

/// <summary>IDimensionRules (02.01, 6) peste core.usp_DimensionRules_Get si DimensionRuleEngine.</summary>
public sealed class DimensionRules(IDimensionRuleStore store) : IDimensionRules
{
    public async Task<IReadOnlyList<DimensionDefault>> GetDefaultsAsync(int companyId, int? partnerId, int? itemId, int? locationId, CancellationToken ct) =>
        DimensionRuleEngine.Merge((await store.GetRulesAsync(companyId, partnerId, itemId, locationId, ct).ConfigureAwait(false)).Rules);

    public async Task<IReadOnlyList<DimensionIssue>> ValidateAsync(int companyId, IReadOnlyDictionary<string, int?> values, int? partnerId, int? itemId,
        int? locationId, CancellationToken ct)
    {
        var rows = await store.GetRulesAsync(companyId, partnerId, itemId, locationId, ct).ConfigureAwait(false);
        return DimensionRuleEngine.Validate(values, rows.Rules, rows.Combinations);
    }
}
