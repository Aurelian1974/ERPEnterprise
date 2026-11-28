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

    /// <summary>Propunerea FEFO de loturi pentru o iesire (02.05, S4 3.5, 6): loturile disponibile, cu durata minima a clientului.</summary>
    Task<IReadOnlyList<LotAllocationDto>> ProposeLotsAsync(LotProposalRequest request, CancellationToken ct);

    /// <summary>Lotul unei variante dupa cod si, optional, termen (S4 6).</summary>
    Task<LotInfoDto?> FindLotAsync(int companyId, int itemVariantId, string lotCode, DateOnly? expiryDate, CancellationToken ct);

    /// <summary>Lotul dupa codul sau id-ul CKU de pe eticheta interna (S4 6).</summary>
    Task<LotInfoDto?> FindCkuAsync(int companyId, string ckuCodeOrUid, CancellationToken ct);
}

/// <summary>Cererea de propunere a loturilor: cheia, cantitatea, clientul (durata minima) si data livrarii.</summary>
public sealed record LotProposalRequest(int CompanyId, int WarehouseId, int? DepotId, int ItemVariantId, decimal QtyBase, int? CustomerPartnerId,
    DateOnly DeliveryDate, long? ExcludeReservationsOfSourceLineId);

/// <summary>O parte a propunerii: lotul, cantitatea propusa, zilele ramase si daca e prima alegere FEFO.</summary>
public sealed record LotAllocationDto(int LotId, string CkuCode, string LotCode, DateOnly? ExpiryDate, decimal QtyBase, int DaysLeft, bool IsFirstChoice);

/// <summary>Un lot (CKU) gasit dupa cod sau eticheta.</summary>
public sealed record LotInfoDto(int LotId, Guid CkuUid, string CkuCode, string LotCode, string? SupplierLotCode, DateOnly? ExpiryDate, LotStatus Status);

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
    int? ReasonId)
{
    /// <summary>Lotul dat prin eticheta interna (CkuUid), alternativa la LotId (S4 6).</summary>
    public Guid? CkuUid { get; init; }

    /// <summary>La intrarea unui lot nou: codul de pe ambalajul furnizorului, data fabricatiei, tara de origine (ISO 3166-1).</summary>
    public string? SupplierLotCode { get; init; }

    public DateOnly? ProductionDate { get; init; }

    public string? CountryOfOriginCode { get; init; }

    /// <summary>Confirmarea unui lot nou cu un cod existent si alt termen (S4 3.2, 54452).</summary>
    public bool ConfirmNewExpiry { get; init; }

    /// <summary>Motivul alegerii altui lot decat primul propus FEFO (S4 3.5): 1 cererea clientului, 2 lot deja pregatit, 3 altul.</summary>
    public byte? LotOverrideReason { get; init; }
}

/// <summary>Starea unui lot (02.05, S4 3.3).</summary>
public enum LotStatus : byte
{
    Available = 1,
    Quarantine = 2,
    Blocked = 3,
    Expired = 4,
    Recalled = 5,
}

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

    public Task<IReadOnlyList<LotAllocationDto>> ProposeLotsAsync(LotProposalRequest request, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LotAllocationDto>>([]);

    public Task<LotInfoDto?> FindLotAsync(int companyId, int itemVariantId, string lotCode, DateOnly? expiryDate, CancellationToken ct) =>
        Task.FromResult<LotInfoDto?>(null);

    public Task<LotInfoDto?> FindCkuAsync(int companyId, string ckuCodeOrUid, CancellationToken ct) => Task.FromResult<LotInfoDto?>(null);
}
