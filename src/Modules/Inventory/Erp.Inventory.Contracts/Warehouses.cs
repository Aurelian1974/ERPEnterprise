using Erp.Core.Contracts;

namespace Erp.Inventory.Contracts;

/// <summary>Tipul gestiunii (02.05, S1 3.1).</summary>
public enum WarehouseKind : byte
{
    Depot = 1,
    Store = 2,
    Transit = 3,
    Custody = 4,
    AtThirdParty = 5,
}

/// <summary>Baza de evaluare a gestiunii: la cost sau la pret de vanzare cu amanuntul (doar magazinele).</summary>
public enum ValuationBasis : byte
{
    Cost = 1,
    RetailPrice = 2,
}

/// <summary>Metoda de evaluare a modelului, pe firma (S1 3.4).</summary>
public enum ValuationMethod : byte
{
    Fifo = 1,
    WeightedAverage = 2,
    Lifo = 3,
    SpecificIdentification = 4,
}

/// <summary>Nivelul de evidenta a stocului (S1 3.8): pe gestiune sau pe depozit.</summary>
public enum StockKeepingLevel : byte
{
    Warehouse = 1,
    Depot = 2,
}

/// <summary>Urmarirea pe lot sau serie (S1 3.4; valoarea 4 adaugata de S4).</summary>
public enum TrackingMode : byte
{
    None = 0,
    Lot = 1,
    LotAndExpiry = 2,
    Serial = 3,
    SerialAndLot = 4,
}

/// <summary>Politica de stoc negativ a gestiunii (S1 3.6).</summary>
public enum NegativeStockPolicy : byte
{
    Blocked = 1,
    AllowedWithWarning = 2,
}

/// <summary>Metoda de reaprovizionare (S1 3.4, Achizitii A2).</summary>
public enum ReplenishmentMethod : byte
{
    None = 0,
    MinMax = 1,
    Mrp = 2,
}

/// <summary>Destinatia gestiunii (S1 3.1, precizare din Vanzari V6).</summary>
public enum WarehousePurpose : byte
{
    Regular = 1,
    CustomerReturns = 2,
    DepositPackaging = 3,
}

/// <summary>Gestiunea, pentru celelalte module (S1 6).</summary>
public sealed record WarehouseDto(int Id, int CompanyId, string Code, string Name, WarehouseKind Kind, ValuationBasis ValuationBasis, int? LocationId,
    int? ThirdPartyId, int DimensionValueId, NegativeStockPolicy NegativeStockPolicy, bool IsBlocked, bool IsActive);

/// <summary>
/// Creare, modificare, blocare, deblocare, dezactivare a unei gestiuni (S1 5.1). Consumatori: POS, WMS, Vanzari, Achizitii, Raportare.
/// </summary>
public sealed record GestiuneModificata(int WarehouseId, string Code, string Name, WarehouseKind Kind, ValuationBasis ValuationBasis, int? LocationId,
    bool IsBlocked, bool IsActive) : IntegrationEvent;

/// <summary>Finalizarea unei predari de gestiune (S1 5.1). Consumatori: HR (raspunderea materiala), Raportare.</summary>
public sealed record GestionarSchimbat(int WarehouseId, int? FromEmployeeId, int ToEmployeeId, DateOnly HandoverDate, int? InventoryCountId)
    : IntegrationEvent;

/// <summary>Politica efectiva pentru o varianta intr-o gestiune, cu precedenta din S1 3.4 si 3.6 deja aplicata.</summary>
public sealed record ItemStockPolicyDto(int ItemId, int? ItemVariantId, int WarehouseId, ValuationMethod Method, TrackingMode Tracking,
    NegativeStockPolicy NegativeStock, bool IsBlockedForIssue, bool IsBlockedForReceipt, decimal? MinQty, decimal? MaxQty, decimal? ReorderQty,
    ReplenishmentMethod Replenishment);

/// <summary>Politica de stoc efectiva (S1 6), citita de S2 la validarea documentelor, pe toate liniile o data.</summary>
public interface IStockPolicyProvider
{
    Task<ItemStockPolicyDto> GetAsync(int warehouseId, int itemVariantId, CancellationToken ct);

    Task<IReadOnlyDictionary<int, ItemStockPolicyDto>> GetManyAsync(int warehouseId, IReadOnlyCollection<int> itemVariantIds, CancellationToken ct);
}

/// <summary>Depozitul (S1 3.8), cu gestiunile active din el.</summary>
public sealed record DepotDto(int Id, int CompanyId, string Code, string Name, int LocationId, int? DefaultWarehouseId, IReadOnlyList<int> WarehouseIds,
    bool IsBlocked, bool IsActive);

/// <summary>Gestiunile pentru celelalte module (S1 6): Vanzari, Achizitii, Productie, POS, WMS.</summary>
public interface IWarehouseLookup
{
    Task<WarehouseDto?> GetAsync(int warehouseId, CancellationToken ct);

    /// <summary>Gestiunile active ale firmei pe care le vede utilizatorul curent (DimensionScope).</summary>
    Task<IReadOnlyList<WarehouseDto>> ListForUserAsync(int companyId, CancellationToken ct);

    Task<int?> GetKeeperEmployeeIdAsync(int warehouseId, DateOnly onDate, CancellationToken ct);

    /// <summary>Nivelul de evidenta efectiv al locatiei (S1 3.8): locatia, punctul de lucru, firma.</summary>
    Task<StockKeepingLevel> GetLevelAsync(int locationId, CancellationToken ct);

    Task<DepotDto?> GetDepotAsync(int depotId, CancellationToken ct);

    /// <summary>Gestiunea propusa pe linie la nivelul „depozit”: regula pe articol, pe grupa, apoi gestiunea implicita a depozitului.</summary>
    Task<int?> ProposeWarehouseAsync(int depotId, int itemVariantId, CancellationToken ct);
}

/// <summary>Validarea schimbarii metodei de evaluare (S1 5.1, 4.5). Consumatori: S3 (intern), Finante (notele explicative).</summary>
public sealed record MetodaEvaluareSchimbata(int DocumentId, string Number, IReadOnlyList<int> ItemIds, IReadOnlyList<ValuationMethod> OldMethods,
    ValuationMethod NewMethod, DateOnly EffectiveDate, string Reason) : IntegrationEvent;
