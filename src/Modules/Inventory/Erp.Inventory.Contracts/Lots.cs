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

/// <summary>Validarea unei retrageri (S4 5.1): loturile, lotul furnizorului, motivul, numarul de clienti. Consumatori: Vanzari, POS, Raportare, Achizitii A7.</summary>
public sealed record RetragereValidata(int RecallId, string Number, IReadOnlyList<int> LotIds, string? SupplierLotCode, int? SupplierPartnerId, string Reason,
    int CustomerCount) : IntegrationEvent;

/// <summary>Rezultatul citirii unui cod GS1-128 sau GS1 DataMatrix (S4 3.8): GTIN, lot, fabricatie, expirare, serie, cantitate; erorile ca lista.</summary>
public sealed record Gs1ParseResult(string? Gtin, string? LotCode, DateOnly? ProductionDate, DateOnly? ExpiryDate, string? SerialNo, decimal? Quantity,
    IReadOnlyList<string> Errors);

/// <summary>Cititorul codurilor GS1 (S4 3.8), folosit de receptie, WMS si POS; nu arunca exceptii.</summary>
public interface IGs1BarcodeParser
{
    Gs1ParseResult Parse(string raw);
}
