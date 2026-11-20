using System.Globalization;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Items;

/// <summary>Cota TVA (02.01, 3.5). Percent ca sir zecimal invariant („21.00”); ValidTo null = in vigoare. UsageCount = articole care o folosesc.</summary>
public sealed record VatRateDto(int Id, string Code, string Name, string Percent, string Category, DateOnly ValidFrom, DateOnly? ValidTo, int UsageCount,
    byte[] RowVersion);

public sealed record UnitOfMeasureDto(int Id, string Code, string Name, string UneceCode, bool IsActive, int UsageCount, byte[] RowVersion);

/// <summary>O linie de set de UM sau o exceptie a articolului; FactorToBase ca sir zecimal invariant (cate UM de baza are o UM).</summary>
public sealed record UomLineDto(int UomId, string FactorToBase, bool IsBase, bool IsDefaultSales, bool IsDefaultPurchase, bool IsDefaultPos);

public sealed record UomSetDto(int Id, string Code, string Name, bool IsActive, int UsageCount, byte[] RowVersion, IReadOnlyList<UomLineDto> Lines);

public sealed record ItemGroupDto(int Id, string Code, string Name, int? ParentId, int SortOrder, bool IsActive, int UsageCount, byte[] RowVersion);

/// <summary>Nomenclatoarele fisei de articol si ale ecranului Configurare › Nomenclatoare, plus proprietatile articolelor.</summary>
public sealed record ItemLookupsDto(IReadOnlyList<VatRateDto> VatRates, IReadOnlyList<UnitOfMeasureDto> Units, IReadOnlyList<UomSetDto> UomSets,
    IReadOnlyList<ItemGroupDto> Groups, IReadOnlyList<PropertyDefinitionDto> Properties, IReadOnlyList<PropertyOptionDto> Options,
    IReadOnlyList<UserOptionDto> Users)
{
    /// <summary>Forma folosita de validarea proprietatilor (aceleasi reguli ca la parteneri).</summary>
    public PartnerLookupsDto PropertyLookups => new([], Properties, Options, Users, []);
}

/// <summary>Un rand din tabelul cotelor TVA editat in pagina (macheta 07); Id null = cota noua, RowVersion pentru cele existente.</summary>
public sealed record VatRateInput(int? Id, string Code, string Name, string Percent, string Category, DateOnly ValidFrom, DateOnly? ValidTo, byte[]? RowVersion);

public sealed record UnitOfMeasureInput(int? Id, string Code, string Name, string UneceCode, bool IsActive, byte[]? RowVersion);

[Module("Core")]
[RequiresPermission(CorePermissions.ItemView)]
public sealed record GetItemLookups : IQuery<ItemLookupsDto>;

/// <summary>Cotele TVA editate in tabel, salvate impreuna (P6: o cota folosita nu se modifica, doar se inchide).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NomenclatureManage)]
[Audited("Core.VatRate.Save")]
public sealed record SaveVatRates(IReadOnlyList<VatRateInput> Rows) : ICommand<int>;

[Module("Core")]
[RequiresPermission(CorePermissions.NomenclatureManage)]
[Audited("Core.UnitOfMeasure.Save")]
public sealed record SaveUnitsOfMeasure(IReadOnlyList<UnitOfMeasureInput> Rows) : ICommand<int>;

/// <summary>Setul de UM cu liniile lui (o singura UM de baza cu factor 1).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NomenclatureManage)]
[Audited("Core.UomSet.Save")]
public sealed record SaveUomSet(int? Id, string Code, string Name, bool IsActive, IReadOnlyList<UomLineDto> Lines, byte[]? RowVersion) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.NomenclatureManage)]
[Audited("Core.ItemGroup.Save")]
public sealed record SaveItemGroup(int? Id, string Code, string Name, int? ParentId, int SortOrder, bool IsActive, byte[]? RowVersion) : ICommand<SavedEntity>;

/// <summary>Categoriile UBL ale cotelor TVA (e-Factura).</summary>
public static class VatCategories
{
    public static readonly IReadOnlyList<string> All = ["S", "Z", "E", "AE", "K", "G", "O"];
}

public static class Decimals
{
    public static bool TryParse(string? value, int maxDecimals, out decimal result)
    {
        result = 0;
        return value is not null
            && System.Text.RegularExpressions.Regex.IsMatch(value, $"^[0-9]{{1,13}}(\\.[0-9]{{1,{maxDecimals}}})?$")
            && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
    }

    public static string Format(decimal value, int decimals) => value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Fara zerourile de la final („6.000000” → „6”), pentru factori si greutati.</summary>
    public static string Trim(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}

internal sealed class GetItemLookupsHandler(IItemStore store, ICurrentContext context) : IQueryHandler<GetItemLookups, ItemLookupsDto>
{
    public Task<ItemLookupsDto> Handle(GetItemLookups request, CancellationToken cancellationToken) => store.GetLookupsAsync(context.CompanyId, cancellationToken);
}

internal sealed class SaveVatRatesValidator : AbstractValidator<SaveVatRates>
{
    public SaveVatRatesValidator()
    {
        RuleFor(x => x.Rows).NotEmpty().WithMessage("Nu este nimic de salvat.");
        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.Code).NotEmpty().Matches("^[A-Za-z0-9]{1,10}$").WithMessage("Codul are 1–10 litere sau cifre.");
            row.RuleFor(r => r.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(100);
            row.RuleFor(r => r.Percent).Must(p => Decimals.TryParse(p, 2, out var value) && value <= 100)
                .WithMessage("Procentul este între 0 și 100, cu cel mult 2 zecimale.");
            row.RuleFor(r => r.Category).Must(c => VatCategories.All.Contains(c)).WithMessage("Categoria UBL e obligatorie (e-Factura).");
            row.RuleFor(r => r).Must(r => r.Category == "S" || (Decimals.TryParse(r.Percent, 2, out var value) && value == 0))
                .WithMessage("Doar categoria S are procent diferit de zero.").OverridePropertyName("Percent");
            row.RuleFor(r => r.ValidTo).Must((r, to) => to is null || to >= r.ValidFrom).WithMessage("„Valabil până la” e după „Valabil de la”.");
            row.RuleFor(r => r.RowVersion).NotNull().When(r => r.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        });
    }
}

internal sealed class SaveVatRatesHandler(IItemStore store, ICurrentContext context, IAuditScope audit, IOutbox outbox)
    : ICommandHandler<SaveVatRates, int>
{
    public async Task<int> Handle(SaveVatRates request, CancellationToken cancellationToken)
    {
        var rows = request.Rows.Select(r => r with { Code = r.Code.Trim().ToUpperInvariant(), Name = r.Name.Trim() }).ToList();
        var count = await store.SaveVatRatesAsync(rows, context.UserId, cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            audit.Describe("Core.VatRate", row.Id, new { row.Code, row.Name, row.Percent, row.Category, row.ValidFrom, row.ValidTo });
            await outbox.EnqueueAsync(new NomenclatorFiscalModificat("VatRate", row.Code, row.ValidFrom) { CompanyId = context.CompanyId }, cancellationToken)
                .ConfigureAwait(false);
        }

        return count;
    }
}

internal sealed class SaveUnitsOfMeasureValidator : AbstractValidator<SaveUnitsOfMeasure>
{
    public SaveUnitsOfMeasureValidator()
    {
        RuleFor(x => x.Rows).NotEmpty().WithMessage("Nu este nimic de salvat.");
        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.Code).NotEmpty().MaximumLength(10).WithMessage("Codul are 1–10 caractere.");
            row.RuleFor(r => r.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(100);
            row.RuleFor(r => r.UneceCode).Matches("^[A-Z0-9]{2,3}$").WithMessage("Codul UN/ECE are 2–3 litere mari sau cifre (de exemplu H87, KGM).");
            row.RuleFor(r => r.RowVersion).NotNull().When(r => r.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
        });
    }
}

internal sealed class SaveUnitsOfMeasureHandler(IItemStore store, ICurrentContext context, IAuditScope audit, IOutbox outbox)
    : ICommandHandler<SaveUnitsOfMeasure, int>
{
    public async Task<int> Handle(SaveUnitsOfMeasure request, CancellationToken cancellationToken)
    {
        var rows = request.Rows.Select(r => r with { Code = r.Code.Trim(), Name = r.Name.Trim(), UneceCode = r.UneceCode.Trim().ToUpperInvariant() }).ToList();
        var count = await store.SaveUnitsAsync(rows, context.UserId, cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            audit.Describe("Core.UnitOfMeasure", row.Id, new { row.Code, row.Name, row.UneceCode, row.IsActive });
            await outbox.EnqueueAsync(new NomenclatorFiscalModificat("Uom", row.UneceCode, null) { CompanyId = context.CompanyId }, cancellationToken)
                .ConfigureAwait(false);
        }

        return count;
    }
}

internal sealed class SaveUomSetValidator : AbstractValidator<SaveUomSet>
{
    public SaveUomSetValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches("^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$").WithMessage("Codul are 1–20 caractere: litere, cifre, „.”, „_” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(100);
        RuleFor(x => x.Lines).Must(l => l.Count(x => x.IsBase) == 1).WithMessage("Setul are exact o UM de bază.");
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.UomId).Distinct().Count() == l.Count).WithMessage("Fiecare UM apare o singură dată.");
        RuleFor(x => x.Lines).Must(l => l.Count(x => x.IsDefaultSales) <= 1 && l.Count(x => x.IsDefaultPurchase) <= 1 && l.Count(x => x.IsDefaultPos) <= 1)
            .WithMessage("Cel mult o UM implicită pentru vânzare, una pentru achiziție și una pentru POS.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.FactorToBase).Must(f => Decimals.TryParse(f, 6, out var value) && value > 0)
                .WithMessage("Factorul e un număr pozitiv cu cel mult 6 zecimale.");
            line.RuleFor(l => l.FactorToBase).Must(f => Decimals.TryParse(f, 6, out var value) && value == 1).When(l => l.IsBase)
                .WithMessage("UM de bază are factorul 1.");
        });
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveUomSetHandler(IItemStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveUomSet, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveUomSet request, CancellationToken cancellationToken)
    {
        var command = request with { Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim() };
        var saved = await store.SaveUomSetAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.UomSet", saved.Id, new { command.Code, command.Name, command.IsActive, Lines = command.Lines.Select(l => $"{l.UomId}×{l.FactorToBase}") });
        return saved;
    }
}

internal sealed class SaveItemGroupValidator : AbstractValidator<SaveItemGroup>
{
    public SaveItemGroupValidator()
    {
        RuleFor(x => x.Code).Code();
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(100);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 9999).WithMessage("Ordinea este între 0 și 9999.");
        RuleFor(x => x.ParentId).NotEqual(x => x.Id).When(x => x.Id is not null).WithMessage("Grupa nu poate fi propria părinte.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveItemGroupHandler(IItemStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveItemGroup, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveItemGroup request, CancellationToken cancellationToken)
    {
        var command = request with { Code = request.Code.Trim(), Name = request.Name.Trim() };
        var saved = await store.SaveItemGroupAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ItemGroup", saved.Id, new { command.Code, command.Name, command.ParentId, command.IsActive });
        return saved;
    }
}
