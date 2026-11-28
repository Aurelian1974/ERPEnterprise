using Erp.Core.Contracts;

namespace Erp.Inventory.Contracts;

/// <summary>
/// Schimbarea starii unui lot (02.05, S4 5.1): carantina, eliberarea, blocarea, expirarea, retragerea. Consumatori: Vanzari (blocheaza ciornele),
/// WMS, POS, Notificari, Achizitii A7 (calitatea furnizorului, prin SupplierPartnerId).
/// </summary>
public sealed record LotStareSchimbata(int LotId, int ItemVariantId, string LotCode, LotStatus FromStatus, LotStatus ToStatus, string? Reason,
    string? SourceDocument, int? SupplierPartnerId) : IntegrationEvent;

/// <summary>Un lot cu stoc care expira curand, intr-o gestiune.</summary>
public sealed record LotExpiryLine(int LotId, int ItemVariantId, DateOnly ExpiryDate, decimal QtyOnHand);

/// <summary>Loturile care expira in urmatoarele ExpiryWarningDays zile, pe gestiune (S4 5.1, jobul zilnic). Consumatori: Notificari, Vanzari.</summary>
public sealed record LoturiAproapeDeExpirare(int WarehouseId, IReadOnlyList<LotExpiryLine> Lots) : IntegrationEvent;
