using Erp.Core.Contracts;

namespace Erp.Inventory.Contracts;

/// <summary>O miscare inregistrata, in evenimentul MiscareStocValidata (S2 5.1).</summary>
public sealed record StockMovementEventLine(long MovementId, int WarehouseId, int? DepotId, int ItemVariantId, decimal QtyBase, decimal UnitCost,
    decimal CostAmount, string MovementTypeCode, string? ReasonCode, bool IsForcedNegative);

/// <summary>
/// Validarea sau stornarea oricarui document de stoc si miscarile venite prin port (S2 5.1). Consumatori: Finante (C2, contarea; blocul
/// Accounting se adauga cu modelele de contare ale Stocurilor, S3), S7, WMS, Raportare.
/// </summary>
public sealed record MiscareStocValidata(string SourceModule, string DocumentType, long DocumentId, string? Number, DateOnly MovementDate,
    DateOnly AccountingDate, bool IsReversal, IReadOnlyList<StockMovementEventLine> Movements) : IntegrationEvent;

/// <summary>O miscare a lasat stocul pe minus (politica permisiva sau fapt din afara ERP, S1 3.6). Consumatori: notificarile gestionarului.</summary>
public sealed record StocNegativFortat(int WarehouseId, int ItemVariantId, long MovementId, string SourceModule, long SourceDocumentId) : IntegrationEvent;

/// <summary>Validarea unei note de predare in doua etape (S2 5.1). Consumatori: WMS, TMS, notificarea gestionarului care primeste.</summary>
public sealed record TransferExpediat(int TransitShipmentId, int NpDocumentId, int FromWarehouseId, int ToWarehouseId,
    IReadOnlyList<StockMovementEventLine> Lines) : IntegrationEvent;

/// <summary>Validarea unei note de primire (S2 5.1): cantitatile primite si diferentele, cu rezolvarea lor. Consumatori: WMS, Raportare.</summary>
public sealed record TransferPrimit(int TransitShipmentId, int NrDocumentId, byte ShipmentStatus, IReadOnlyList<TransferReceivedLine> Lines) : IntegrationEvent;

public sealed record TransferReceivedLine(int ItemVariantId, decimal SentQtyBase, decimal ReceivedQtyBase, decimal DifferenceQtyBase, byte? Resolution);

/// <summary>Un bon POS sincronizat: linia cu cantitatea vanduta (pozitiva) sau returnata (negativa), in UM de baza.</summary>
public sealed record PosReceiptLine(long ExternalLineId, int ItemVariantId, decimal QtyBase);

/// <summary>Un bon fiscal deja emis la casa (fapt petrecut), cu data si ora reale.</summary>
public sealed record PosReceipt(long ExternalId, string Number, DateOnly SaleDate, int WarehouseId, int? DepotId, IReadOnlyList<PosReceiptLine> Lines);

/// <summary>
/// Bonurile unei case sincronizate dupa lucrul offline (S2 4.7, 5.2). Definit in contractele Stocurilor, ca POS (deasupra Stocurilor in
/// dependente) sa-l publice; Stocurile il consuma idempotent, fara verificarea politicii de stoc negativ.
/// </summary>
public sealed record BonuriPOSSincronizate(int CashRegisterId, IReadOnlyList<PosReceipt> Receipts) : IntegrationEvent;

/// <summary>O miscare din registru, cu soldul cheii dupa ea (S2 10; macheta s2-05).</summary>
public sealed record StockMovementDto(long Id, long PostingSeq, DateOnly MovementDate, int WarehouseId, string WarehouseCode, int? DepotId, int ItemVariantId,
    string VariantCode, string MovementTypeCode, string QtyBase, string UnitCost, string CostAmount, string? BalanceQty, string SourceModule,
    string SourceDocumentType, long SourceDocumentId, string? SourceDocumentNumber, string? ReasonCode, bool IsForcedNegative, long? ReversalOfId);

/// <summary>O linie a unei ajustari de cost: cheia, miscarea afectata (iesirea, pentru marja din Vanzari V7), suma si tinta (Stock sau Expense).</summary>
public sealed record CostAdjustmentEventLine(int WarehouseId, int ItemVariantId, long? SourceMovementId, decimal Amount, string TargetKind);

/// <summary>
/// Ajustari de valoare fara cantitate (S3 5.1): recalcularea, regularizarea negativului, costurile adaugate; grupate pe document sursa si luna.
/// Consumatori: C2 (blocul Accounting vine cu modelele de contare ale Stocurilor), Raportare, Vanzari V7.
/// </summary>
public sealed record CostStocAjustat(byte AdjustmentKind, string SourceModule, string SourceDocumentType, long SourceDocumentId, DateOnly AccountingDate,
    IReadOnlyList<CostAdjustmentEventLine> Lines) : IntegrationEvent;
