using Erp.Core.Contracts;
using Erp.Inventory.Contracts;

namespace Erp.Inventory.Application.Movements;

/// <summary>Accesul la registru pentru ecranul Registrul de miscari (inv.usp_StockMovement_Search).</summary>
public interface IStockMovementQueries
{
    Task<IReadOnlyList<(StockMovementDto Movement, int WarehouseDimension)>> SearchAsync(int companyId, int? warehouseId, int? itemVariantId, DateOnly? from,
        DateOnly? to, long? afterSeq, int top, CancellationToken ct);
}

/// <summary>Registrul de miscari (S2 10; macheta s2-05), pe gestiunile permise, paginat dupa PostingSeq (AfterSeq = ultimul rand al paginii).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
public sealed record GetStockMovements(int? WarehouseId = null, int? ItemVariantId = null, DateOnly? DateFrom = null, DateOnly? DateTo = null,
    long? AfterSeq = null, int Top = 200) : IQuery<IReadOnlyList<StockMovementDto>>;

internal sealed class GetStockMovementsHandler(IStockMovementQueries queries, ICurrentContext context)
    : IQueryHandler<GetStockMovements, IReadOnlyList<StockMovementDto>>
{
    public async Task<IReadOnlyList<StockMovementDto>> Handle(GetStockMovements request, CancellationToken cancellationToken) =>
        (await queries.SearchAsync(context.CompanyId, request.WarehouseId, request.ItemVariantId, request.DateFrom, request.DateTo, request.AfterSeq,
            Math.Clamp(request.Top, 1, 1000), cancellationToken).ConfigureAwait(false))
        .Where(r => context.HasPermission(InventoryPermissions.DocumentView, new DimensionRef(DimensionTypes.Warehouse, r.WarehouseDimension)))
        .Select(r => r.Movement)
        .ToList();
}

/// <summary>O varianta stocabila pentru liniile documentelor, cu stocul din gestiunea aleasa (text, UI).</summary>
public sealed record StockVariantOptionDto(int Id, string Code, string Name, int ItemId, string BaseUomCode, string? OnHand);

/// <summary>Cautarea variantelor stocabile pe liniile documentelor de stoc (S2 4.2 pasul 1).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
public sealed record SearchStockVariants(string? Search = null, int? WarehouseId = null) : IQuery<IReadOnlyList<StockVariantOptionDto>>;

public interface IStockVariantSearch
{
    Task<IReadOnlyList<StockVariantOptionDto>> SearchAsync(int companyId, string? search, int? warehouseId, CancellationToken ct);
}

internal sealed class SearchStockVariantsHandler(IStockVariantSearch search, ICurrentContext context)
    : IQueryHandler<SearchStockVariants, IReadOnlyList<StockVariantOptionDto>>
{
    public Task<IReadOnlyList<StockVariantOptionDto>> Handle(SearchStockVariants request, CancellationToken cancellationToken)
    {
        var text = string.IsNullOrWhiteSpace(request.Search) ? null
            : request.Search.Trim().Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("_", "[_]", StringComparison.Ordinal);
        return search.SearchAsync(context.CompanyId, text, request.WarehouseId, cancellationToken);
    }
}
