namespace Erp.Core.Contracts;

/// <summary>
/// Alocarea numarului la validarea unui document (02.01, 4.2), in tranzactia documentului: fara goluri in seriile fiscale,
/// iar un rollback anuleaza si alocarea. Seria implicita a punctului de lucru are prioritate fata de cea a firmei.
/// </summary>
public interface INumberSeriesService
{
    /// <param name="choice">Seria aleasa de utilizator pe document sau datele pentru regulile de alegere (3.6 punctul 16); null = seria implicita.</param>
    Task<DocumentNumber> NextAsync(int companyId, string documentType, DateOnly documentDate, CancellationToken ct, int? branchId = null,
        NumberSeriesChoice? choice = null);

    /// <summary>Seriile din care se poate alege pe document si cea propusa de reguli (doar citire; numarul se aloca la validare).</summary>
    Task<NumberSeriesOptions> GetOptionsAsync(int companyId, string documentType, DateOnly documentDate, int? branchId, NumberSeriesChoice? choice,
        CancellationToken ct);
}

/// <summary>Tipul partenerului pentru regulile de alegere a seriei: Romania, UE, non-UE (de exemplu facturile de export).</summary>
public enum PartnerScope : byte
{
    Romania = 1,
    EuropeanUnion = 2,
    NonEu = 3,
}

/// <summary>
/// Alegerea seriei (02.01, 3.6 punctul 16): SeriesId = seria aleasa de utilizator (din GetOptionsAsync); fara ea, prima regula
/// potrivita dupa utilizator, locatie si tipul partenerului, apoi seria implicita.
/// </summary>
public sealed record NumberSeriesChoice(int? SeriesId = null, int? UserId = null, int? LocationId = null, PartnerScope? PartnerScope = null);

/// <summary>O serie deschisa pe care o poate folosi documentul; NextFormatted = numarul pe care l-ar primi acum.</summary>
public sealed record NumberSeriesOption(int SeriesId, string Prefix, int Year, string NextFormatted, bool IsFiscal, bool IsDefault, int? BranchId);

public sealed record NumberSeriesOptions(IReadOnlyList<NumberSeriesOption> Series, int? ProposedSeriesId);

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

/// <summary>
/// Datele firmei valabile la o data (02.01, 3.6 punctul 2 si 9), de copiat pe un document fiscal: denumirea, sediul, regimul TVA,
/// registrul si capitalul de atunci. ValidFrom = inceputul versiunii folosite.
/// </summary>
public sealed record CompanySnapshotDto(int CompanyId, string Code, string FiscalCode, string Name, bool VatPayer, bool VatOnCollection,
    string TradeRegisterNo, decimal? ShareCapital, string Address, string City, int? CitySirutaCode, string? CountyCode, string CountryCode,
    string? PostalCode, DateOnly ValidFrom);

/// <summary>Datele firmei pentru documente, la data lor (versiunile din core.CompanyVersion).</summary>
public interface ICompanyDirectory
{
    Task<CompanySnapshotDto> GetAtAsync(int companyId, DateOnly date, CancellationToken ct);
}
