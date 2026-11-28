namespace Erp.Core.Contracts;

/// <summary>
/// Portul de stoc (02.05, S2 6; decizia 1): Vanzarile, Achizitiile si Productia misca stocul sincron, in tranzactia documentului lor.
/// Definit aici pentru ca acele module sunt sub Stocuri in dependente; implementat de Erp.Inventory. Fara Stocuri licentiat, implementarea
/// implicita accepta totul si nu scrie nimic (IsEnabled = false).
/// </summary>
public interface IStockLedger
{
    bool IsEnabled { get; }

    Task<StockPostingResult> PostAsync(StockPostingRequest request, CancellationToken ct);

    Task<StockPostingResult> ReverseAsync(StockReversalRequest request, CancellationToken ct);

    Task<IReadOnlyList<StockAvailabilityDto>> GetAvailabilityAsync(int companyId, int? depotId, int? warehouseId, IReadOnlyCollection<int> itemVariantIds,
        DateOnly onDate, CancellationToken ct);

    /// <summary>
    /// Costul adaugat sau diferenta de pret dupa receptie (02.05, S3 3.3 si 6): creeaza documentul CA (in ciorna sau validat) si intoarce Id-ul lui;
    /// 0 fara Stocuri licentiat.
    /// </summary>
    Task<int> AdjustInboundCostAsync(InboundCostAdjustmentRequest request, CancellationToken ct);
}

public enum InboundCostSourceKind : byte
{
    ServiceInvoice = 1,
    InvoicePriceDifference = 2,
    RebateAfterReceipt = 3,
}

public enum AllocationBasis : byte
{
    Value = 1,
    Quantity = 2,
    Weight = 3,
    Volume = 4,
    Manual = 5,
}

/// <summary>Cererea de cost adaugat: suma fara TVA in RON (negativa la reducere), baza de repartizare si liniile de receptie vizate.</summary>
public sealed record InboundCostAdjustmentRequest(int CompanyId, InboundCostSourceKind Kind, long SupplierInvoiceId, DateOnly AccountingDate, decimal AmountRon,
    AllocationBasis Basis, IReadOnlyList<InboundCostTarget> Targets, bool ValidateImmediately);

/// <summary>O linie de receptie vizata: modulul si linia sursa a miscarii de intrare; suma, doar la repartizarea manuala.</summary>
public sealed record InboundCostTarget(string SourceModule, long SourceLineId, decimal? ManualAmountRon);

/// <summary>
/// Un set de miscari dintr-un document sursa. IsExternalFact = fapt deja petrecut in afara ERP (bon POS): se accepta mereu, cu marcajul de
/// stoc negativ fortat.
/// </summary>
public sealed record StockPostingRequest(int CompanyId, string SourceModule, string SourceDocumentType, long SourceDocumentId, string SourceDocumentNumber,
    DateOnly MovementDate, DateOnly AccountingDate, int? PartnerId, bool IsExternalFact, IReadOnlyList<StockMovementRequest> Lines);

/// <summary>O miscare ceruta: cantitatea cu semn, in UM de baza; InboundUnitCost doar la intrarile cu cost din sursa (pretul de receptie).</summary>
public sealed record StockMovementRequest(long SourceLineId, byte SourceRole, string MovementTypeCode, int? DepotId, int WarehouseId, int ItemVariantId,
    decimal QtyBase, decimal? InboundUnitCost, int? LotId, string? NewLotCode, DateOnly? ExpiryDate, int? SerialId, int? CostCenterId, int? ProjectId,
    int? ReasonId);

/// <summary>Stornarea miscarilor unui document sursa (toate sau doar liniile date), la data ceruta.</summary>
public sealed record StockReversalRequest(int CompanyId, string SourceModule, long SourceDocumentId, IReadOnlyCollection<long>? SourceLineIds,
    DateOnly MovementDate, string ReversalDocumentType, long ReversalDocumentId, string ReversalDocumentNumber);

public sealed record PostedMovementDto(long MovementId, long SourceLineId, decimal QtyBase, decimal UnitCost, decimal CostAmount, bool IsForcedNegative);

public sealed record StockPostingResult(IReadOnlyList<PostedMovementDto> Movements);

public sealed record StockAvailabilityDto(int ItemVariantId, int WarehouseId, int? DepotId, decimal OnHand, decimal Reserved, decimal Available);

/// <summary>Portul fara Stocuri licentiat: nu scrie nimic si nu refuza nimic.</summary>
public sealed class NoStockLedger : IStockLedger
{
    public bool IsEnabled => false;

    public Task<StockPostingResult> PostAsync(StockPostingRequest request, CancellationToken ct) => Task.FromResult(new StockPostingResult([]));

    public Task<StockPostingResult> ReverseAsync(StockReversalRequest request, CancellationToken ct) => Task.FromResult(new StockPostingResult([]));

    public Task<IReadOnlyList<StockAvailabilityDto>> GetAvailabilityAsync(int companyId, int? depotId, int? warehouseId,
        IReadOnlyCollection<int> itemVariantIds, DateOnly onDate, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StockAvailabilityDto>>([]);

    public Task<int> AdjustInboundCostAsync(InboundCostAdjustmentRequest request, CancellationToken ct) => Task.FromResult(0);
}
