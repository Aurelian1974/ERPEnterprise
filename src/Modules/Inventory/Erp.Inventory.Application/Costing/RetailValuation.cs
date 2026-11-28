using Erp.Core.Contracts;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Inventory.Domain.Costing;
using FluentValidation;

namespace Erp.Inventory.Application.Costing;

public sealed record RetailPriceChangeLineDto(int LineNo, int ItemVariantId, string VariantCode, string VariantName, string NewPrice, string? OldPrice,
    string? Qty, decimal? VatPercent, string? DiffAmount, string? DiffVatAmount);

/// <summary>Documentul MP (S3 3.7; macheta s3-04). Status: 1 Ciorna, 2 Validat, 4 Anulat.</summary>
public sealed record RetailPriceChangeDto(int Id, string? Number, int WarehouseId, string WarehouseCode, DateOnly DocumentDate, byte Status, string? Notes,
    string? DiffAmount, string? DiffVatAmount, IReadOnlyList<RetailPriceChangeLineDto> Lines, byte[] RowVersion);

public sealed record RetailPriceChangeItemDto(int Id, string? Number, int WarehouseId, string WarehouseCode, DateOnly DocumentDate, byte Status,
    string? DiffAmount, string? DiffVatAmount, int LineCount);

/// <summary>Descarcarea unei luni (S3 3.7; macheta s3-03): simularea (Id null) sau documentul DA validat.</summary>
public sealed record RetailDischargeDto(int? Id, string? Number, int WarehouseId, DateOnly PeriodStart, string OpeningRetail, string RetailIn, string RetailOut,
    string OpeningMarkup, string MarkupIn, string OpeningVat, string VatIn, string SalesVat, string SalesNet, decimal K, string Markup, string Cost);

/// <summary>Datele brute ale descarcarii (inv.usp_RetailDischarge_Compute).</summary>
public sealed record RetailDischargeData(RetailDischargeInput Input, bool AlreadyDischarged, int? CostCenterId = null);

/// <summary>Accesul la date pentru MP si DA (inv.usp_RetailPriceChange_*, inv.usp_RetailDischarge_*, inv.usp_RetailDischargeMethod_Set).</summary>
public interface IRetailValuationStore
{
    Task<InventorySaved> SavePriceChangeAsync(int companyId, int? id, int warehouseId, DateOnly documentDate, string? notes,
        IReadOnlyList<(int ItemVariantId, decimal NewPrice)> lines, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<RetailPriceChangeDto?> GetPriceChangeAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<RetailPriceChangeItemDto>> ListPriceChangesAsync(int companyId, int? warehouseId, byte? status, CancellationToken ct);

    Task<InventorySaved> ValidatePriceChangeAsync(int companyId, int id, byte[] rowVersion, string number, int userId, CancellationToken ct);

    Task<InventorySaved> CancelPriceChangeAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct);

    Task<RetailDischargeData> ComputeDischargeAsync(int companyId, int warehouseId, DateOnly periodStart, CancellationToken ct);

    Task<int> SaveDischargeAsync(int companyId, int warehouseId, DateOnly periodStart, string number, RetailDischargeInput input, RetailDischargeResult result,
        int userId, CancellationToken ct);

    Task<IReadOnlyList<RetailDischargeDto>> ListDischargesAsync(int companyId, int? warehouseId, CancellationToken ct);

    Task SetDischargeMethodAsync(int companyId, int? warehouseId, byte? method, DateOnly today, CancellationToken ct);
}

/// <summary>Modificarile de pret ale firmei curente (macheta s3-04).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
public sealed record GetRetailPriceChanges(int? WarehouseId = null, byte? Status = null) : IQuery<IReadOnlyList<RetailPriceChangeItemDto>>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
public sealed record GetRetailPriceChange(int Id) : IQuery<RetailPriceChangeDto>;

public sealed record RetailPriceChangeLineInput(int ItemVariantId, string NewPrice);

/// <summary>Salveaza ciorna documentului MP: preturile noi cu TVA pe variante (S3 3.7).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.RetailPriceEdit)]
[Audited("Inv.RetailPriceChange.Save")]
public sealed record SaveRetailPriceChange(int? Id, int WarehouseId, DateOnly DocumentDate, string? Notes, IReadOnlyList<RetailPriceChangeLineInput> Lines,
    byte[]? RowVersion) : ICommand<InventorySaved>;

/// <summary>Valideaza documentul MP: stocul se reevalueaza la pretul nou, pretul intra in vigoare, se publica ModificarePretAmanuntValidata.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.RetailPriceValidate)]
[Audited("Inv.RetailPriceChange.Validate")]
public sealed record ValidateRetailPriceChange(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.RetailPriceEdit)]
[Audited("Inv.RetailPriceChange.Cancel")]
public sealed record CancelRetailPriceChange(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Simularea descarcarii lunii pentru o gestiune la pret cu amanuntul cu metoda 1 (S3 4.4; macheta s3-03).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostView)]
public sealed record SimulateRetailDischarge(int WarehouseId, DateOnly PeriodStart) : IQuery<RetailDischargeDto>;

/// <summary>Valideaza descarcarea lunii (documentul DA) si publica DescarcareAmanuntValidata.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.RetailDischargeValidate)]
[Audited("Inv.RetailDischarge.Validate")]
public sealed record ValidateRetailDischarge(int WarehouseId, DateOnly PeriodStart) : ICommand<RetailDischargeDto>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostView)]
public sealed record GetRetailDischarges(int? WarehouseId = null) : IQuery<IReadOnlyList<RetailDischargeDto>>;

/// <summary>Metoda de descarcare pe firma (WarehouseId null) sau pe gestiune (Method null = ca pe firma), de la inceputul lunii (S3 3.7).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.SettingsManage)]
[Audited("Inv.RetailDischargeMethod.Set")]
public sealed record SetRetailDischargeMethod(int? WarehouseId, byte? Method) : ICommand<bool>;

internal sealed class SaveRetailPriceChangeValidator : AbstractValidator<SaveRetailPriceChange>
{
    public SaveRetailPriceChangeValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Documentul are cel puțin o variantă.");
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.ItemVariantId).Distinct().Count() == l.Count).WithMessage("O variantă apare de două ori.");
        RuleForEach(x => x.Lines).Must(l => Quantity.TryParse(l.NewPrice, out var p) && p >= 0)
            .WithMessage("Prețul nou este o valoare pozitivă, cu cel mult șase zecimale.");
        RuleFor(x => x.Notes).MaximumLength(1000).WithMessage("Observațiile au cel mult 1000 de caractere.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SetRetailDischargeMethodValidator : AbstractValidator<SetRetailDischargeMethod>
{
    public SetRetailDischargeMethodValidator()
    {
        RuleFor(x => x.Method).Must(m => m is null or 1 or 2).WithMessage("Metoda de descărcare este 1 (lunar, prin K) sau 2 (pe fiecare vânzare).");
        RuleFor(x => x.Method).NotNull().When(x => x.WarehouseId is null).WithMessage("Metoda de descărcare a firmei este obligatorie.");
    }
}

/// <summary>
/// Gestiunile la pret cu amanuntul (02.05, S3 3.7): documentul MP (reevaluarea stocului la pretul nou, cu diferenta cu TVA-ul inclus), descarcarea
/// lunara prin K (documentul DA, doar metoda 1) si metoda de descarcare. Valorile cu amanuntul ale miscarilor le scrie inv.usp_StockMovement_Post.
/// </summary>
internal sealed class RetailValuationHandler(IRetailValuationStore store, INumberSeriesService numbers, IOutbox outbox, IPeriodLock periods,
    ICurrentContext context, TimeProvider time, IAuditScope audit)
    : IQueryHandler<GetRetailPriceChanges, IReadOnlyList<RetailPriceChangeItemDto>>, IQueryHandler<GetRetailPriceChange, RetailPriceChangeDto>,
    ICommandHandler<SaveRetailPriceChange, InventorySaved>, ICommandHandler<ValidateRetailPriceChange, InventorySaved>,
    ICommandHandler<CancelRetailPriceChange, InventorySaved>, IQueryHandler<SimulateRetailDischarge, RetailDischargeDto>,
    ICommandHandler<ValidateRetailDischarge, RetailDischargeDto>, IQueryHandler<GetRetailDischarges, IReadOnlyList<RetailDischargeDto>>,
    ICommandHandler<SetRetailDischargeMethod, bool>
{
    public const string PriceChangeSeries = "Inventory.RetailPriceChange";

    public const string DischargeSeries = "Inventory.RetailDischarge";

    public Task<IReadOnlyList<RetailPriceChangeItemDto>> Handle(GetRetailPriceChanges request, CancellationToken cancellationToken) =>
        store.ListPriceChangesAsync(context.CompanyId, request.WarehouseId, request.Status, cancellationToken);

    public async Task<RetailPriceChangeDto> Handle(GetRetailPriceChange request, CancellationToken cancellationToken) =>
        await store.GetPriceChangeAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();

    public async Task<InventorySaved> Handle(SaveRetailPriceChange request, CancellationToken cancellationToken)
    {
        var saved = await store.SavePriceChangeAsync(context.CompanyId, request.Id, request.WarehouseId, request.DocumentDate,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            request.Lines.Select(l => (l.ItemVariantId, Quantity.ParseOptional(l.NewPrice) ?? 0)).ToList(), request.RowVersion, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.RetailPriceChange", saved.Id, new { request.WarehouseId, request.DocumentDate, Lines = request.Lines.Count });
        return saved;
    }

    public async Task<InventorySaved> Handle(ValidateRetailPriceChange request, CancellationToken cancellationToken)
    {
        var document = await store.GetPriceChangeAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        await EnsureOpenAsync(document.DocumentDate, cancellationToken).ConfigureAwait(false);
        var number = await numbers.NextAsync(context.CompanyId, PriceChangeSeries, document.DocumentDate, cancellationToken).ConfigureAwait(false);
        var saved = await store.ValidatePriceChangeAsync(context.CompanyId, request.Id, request.RowVersion, number.Formatted, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        var validated = await store.GetPriceChangeAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        var change = new ModificarePretAmanuntValidata(validated.WarehouseId, validated.Id, number.Formatted, validated.DocumentDate,
            validated.Lines.Select(l => new RetailPriceChangeEventLine(l.ItemVariantId, Quantity.ParseOptional(l.Qty) ?? 0, Quantity.ParseOptional(l.OldPrice),
                Quantity.ParseOptional(l.NewPrice) ?? 0, LandedCostService.Parse(l.DiffAmount), LandedCostService.Parse(l.DiffVatAmount))).ToList())
        {
            CompanyId = context.CompanyId,
        };
        await outbox.EnqueueAsync(change with { Accounting = StockAccounting.PriceChange(change) }, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.RetailPriceChange", request.Id, new { Number = number.Formatted, validated.DiffAmount });
        return saved;
    }

    public async Task<InventorySaved> Handle(CancelRetailPriceChange request, CancellationToken cancellationToken)
    {
        var saved = await store.CancelPriceChangeAsync(context.CompanyId, request.Id, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.RetailPriceChange", request.Id, new { Cancelled = true });
        return saved;
    }

    public async Task<RetailDischargeDto> Handle(SimulateRetailDischarge request, CancellationToken cancellationToken)
    {
        var start = MonthStart(request.PeriodStart);
        var data = await store.ComputeDischargeAsync(context.CompanyId, request.WarehouseId, start, cancellationToken).ConfigureAwait(false);
        return Dto(null, null, request.WarehouseId, start, data.Input, RetailDischargeCalculator.Calculate(data.Input));
    }

    public async Task<RetailDischargeDto> Handle(ValidateRetailDischarge request, CancellationToken cancellationToken)
    {
        var start = MonthStart(request.PeriodStart);
        var end = start.AddMonths(1).AddDays(-1);
        await EnsureOpenAsync(end, cancellationToken).ConfigureAwait(false);
        var data = await store.ComputeDischargeAsync(context.CompanyId, request.WarehouseId, start, cancellationToken).ConfigureAwait(false);
        if (data.AlreadyDischarged)
        {
            throw ErpException.Conflict("INV_RETAIL_DISCHARGE_EXISTS", "Descărcarea la preț cu amănuntul pentru această gestiune și lună există deja.");
        }

        var result = RetailDischargeCalculator.Calculate(data.Input);
        var number = await numbers.NextAsync(context.CompanyId, DischargeSeries, end, cancellationToken).ConfigureAwait(false);
        var id = await store.SaveDischargeAsync(context.CompanyId, request.WarehouseId, start, number.Formatted, data.Input, result, context.UserId,
            cancellationToken).ConfigureAwait(false);
        var discharge = new DescarcareAmanuntValidata(request.WarehouseId, id, number.Formatted, start, end, result.K, result.SalesNet, result.Markup,
            data.Input.SalesVat, result.Cost) { CompanyId = context.CompanyId };
        await outbox.EnqueueAsync(discharge with { Accounting = StockAccounting.Discharge(discharge, data.CostCenterId) }, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.RetailDischarge", id, new { request.WarehouseId, PeriodStart = start, result.K, result.Markup });
        return Dto(id, number.Formatted, request.WarehouseId, start, data.Input, result);
    }

    public Task<IReadOnlyList<RetailDischargeDto>> Handle(GetRetailDischarges request, CancellationToken cancellationToken) =>
        store.ListDischargesAsync(context.CompanyId, request.WarehouseId, cancellationToken);

    public async Task<bool> Handle(SetRetailDischargeMethod request, CancellationToken cancellationToken)
    {
        await store.SetDischargeMethodAsync(context.CompanyId, request.WarehouseId, request.Method, BusinessDay.Today(time), cancellationToken).ConfigureAwait(false);
        audit.Describe(request.WarehouseId is null ? "Inv.StockSettings" : "Inv.Warehouse", request.WarehouseId ?? context.CompanyId,
            new { RetailDischargeMethod = request.Method });
        return true;
    }

    private async Task EnsureOpenAsync(DateOnly date, CancellationToken ct)
    {
        if (await periods.IsClosedAsync(context.CompanyId, date, ct).ConfigureAwait(false))
        {
            throw ErpException.Conflict(CoreErrorCodes.PeriodClosed, $"Luna datei {date:dd.MM.yyyy} este închisă.");
        }
    }

    private static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    private static string Money(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    internal static RetailDischargeDto Dto(int? id, string? number, int warehouseId, DateOnly start, RetailDischargeInput i, RetailDischargeResult r) =>
        new(id, number, warehouseId, start, Money(i.OpeningRetail), Money(i.RetailIn), Money(i.RetailOut), Money(i.OpeningMarkup), Money(i.MarkupIn),
            Money(i.OpeningVat), Money(i.VatIn), Money(i.SalesVat), Money(r.SalesNet), r.K, Money(r.Markup), Money(r.Cost));

    private static ErpException NotFound() => ErpException.NotFound("INV_RETAIL_PRICE_CHANGE_NOT_FOUND", "Documentul de modificare de preț nu există în firma curentă.");
}
