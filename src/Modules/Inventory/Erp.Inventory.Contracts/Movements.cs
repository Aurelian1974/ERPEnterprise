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
