namespace Erp.Core.Contracts;

/// <summary>
/// Alocarea numarului la validarea unui document (02.01, 4.2), in tranzactia documentului: fara goluri in seriile fiscale,
/// iar un rollback anuleaza si alocarea. Seria implicita a punctului de lucru are prioritate fata de cea a firmei.
/// </summary>
public interface INumberSeriesService
{
    Task<DocumentNumber> NextAsync(int companyId, string documentType, DateOnly documentDate, CancellationToken ct, int? branchId = null);
}

/// <summary>Numarul alocat; Formatted = {Prefix}-{An}-{Numar} (macheta 07c).</summary>
public sealed record DocumentNumber(string Prefix, int Year, long Number, string Formatted);

/// <summary>Modificarea datelor unei firme (02.01, 5): Integrari (emitentul e-Factura), Finante. CompanyId e in anvelopa.</summary>
public sealed record FirmaModificata(string FiscalCode, string Name, bool VatPayer, bool VatOnCollection) : IntegrationEvent;

/// <summary>
/// Crearea sau modificarea unui partener (02.01, 5): CRM, Vanzari, Achizitii (cache-uri locale), POS. Partenerii sunt la
/// nivelul instalarii; CompanyId din anvelopa e firma din care s-a facut modificarea (0 = nivelul instalarii).
/// </summary>
public sealed record PartenerModificat(int PartnerId, string Code, string Name, string? FiscalCode, bool VatPayer, bool VatOnCollection,
    bool AnafInactive, bool IsActive) : IntegrationEvent;

/// <summary>Crearea sau modificarea unui articol (02.01, 5): POS (sincronizare), Stocuri, Productie.</summary>
public sealed record ArticolModificat(int ItemId, string Code, string Name, byte ItemType, string BaseUomCode, string VatRateCode, int? DepositItemId,
    bool IsActive) : IntegrationEvent;

/// <summary>Adaugarea sau modificarea unei cote TVA ori a unei UM (02.01, 5). Kind: VatRate sau Uom; ValidFrom doar la cote.</summary>
public sealed record NomenclatorFiscalModificat(string Kind, string Code, DateOnly? ValidFrom) : IntegrationEvent;
