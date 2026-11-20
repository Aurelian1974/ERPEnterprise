using Erp.Core.Application.Organization;

namespace Erp.Core.Application.Partners;

/// <summary>Un rand din lista partenerilor (macheta 04). PartnerType: 1 juridica RO, 2 juridica UE, 3 juridica non-UE, 4 persoana fizica.</summary>
public sealed record PartnerListItemDto(int Id, string Code, string Name, byte PartnerType, string? FiscalCode, bool IsCustomer, bool IsSupplier,
    bool VatPayer, bool VatOnCollection, bool AnafInactive, string CountryCode, string City, bool IsActive);

/// <summary>O pagina de parteneri: Total = toate rezultatele filtrului; ActiveCount = partenerii activi din nomenclator (antetul ecranului).</summary>
public sealed record PartnerPageDto(IReadOnlyList<PartnerListItemDto> Items, int Total, int ActiveCount);

/// <summary>
/// Valoarea unei proprietati configurabile; exact un camp completat, dupa tipul proprietatii. Numerele circula ca sir zecimal
/// invariant (UI-05). Listele multiple au cate o valoare pe optiune.
/// </summary>
public sealed record PartnerPropertyValue(int PropertyDefinitionId, string? ValueText = null, string? ValueNumber = null, DateOnly? ValueDate = null,
    bool? ValueBool = null, int? ValueOptionId = null, int? ValueUserId = null);

public sealed record PartnerAddressDto(int Id, int PartnerId, int AddressType, string? Name, string Address, string City, string? CountyCode,
    string CountryCode, string? PostalCode, string? Phone, string? Email, bool IsDefault, bool IsActive, byte[] RowVersion);

public sealed record PartnerBankAccountDto(int Id, int PartnerId, string Iban, string BankName, string Currency, bool IsDefault, bool IsActive, byte[] RowVersion);

public sealed record PartnerContactDto(int Id, int PartnerId, string Name, string? Role, string? Phone, string? Email, string? Notes, bool IsActive,
    byte[] RowVersion);

/// <summary>Fisa partenerului. CNP-ul nu e inclus: HasPersonalId spune doar ca exista; se citeste separat, cu permisiune (A13).</summary>
public sealed record PartnerDetailDto(int Id, string Code, string Name, byte PartnerType, string? FiscalCode, bool HasPersonalId, bool IsCustomer,
    bool IsSupplier, int? PartnerGroupId, bool VatPayer, bool VatOnCollection, bool AnafInactive, DateTime? AnafCheckedAt, string? TradeRegisterNo,
    string Address, string City, string? CountyCode, string CountryCode, string? PostalCode, string? Phone, string? Email, int? IntraGroupCompanyId,
    bool IsActive, DateTime CreatedAt, string ModifiedByName, DateTime LastChangedAt, byte[] RowVersion, IReadOnlyList<PartnerAddressDto> Addresses,
    IReadOnlyList<PartnerBankAccountDto> BankAccounts, IReadOnlyList<PartnerContactDto> Contacts, IReadOnlyList<PartnerPropertyValue> Properties);

public sealed record PartnerGroupDto(int Id, string Code, string Name, int SortOrder, bool IsActive, byte[] RowVersion);

/// <summary>DataType: 1 text, 2 numar, 3 data, 4 da/nu, 5 lista, 6 lista multipla, 7 utilizator. AppliesTo: 1 client, 2 furnizor, 3 ambele.</summary>
public sealed record PropertyDefinitionDto(int Id, string Code, string Name, byte DataType, byte AppliesTo, string Section, int SortOrder,
    bool IsRequired, bool IsFilterable);

public sealed record PropertyOptionDto(int Id, int PropertyDefinitionId, string Code, string Label, bool IsActive);

public sealed record UserOptionDto(int Id, string DisplayName);

/// <summary>Listele fisei: grupe, proprietati valabile pentru firma curenta cu optiunile lor, utilizatori, firmele din grup.</summary>
public sealed record PartnerLookupsDto(IReadOnlyList<PartnerGroupDto> Groups, IReadOnlyList<PropertyDefinitionDto> Properties,
    IReadOnlyList<PropertyOptionDto> Options, IReadOnlyList<UserOptionDto> Users, IReadOnlyList<CompanySummaryDto> Companies);

/// <summary>O intrare din tab-ul Istoric; Changes = JSON-ul din jurnalul de audit, fara date personale.</summary>
public sealed record PartnerHistoryEntryDto(long Id, DateTime OccurredAt, string Action, string EntityType, long? EntityId, string UserName, string? Changes);

public sealed record PartnerPersonalIdDto(string? PersonalId);

public sealed record PartnerSearchCriteria(string? Search, string? FiscalSearch, byte? Role, byte? PartnerType, bool? IsActive, bool? AnafInactive,
    int? PropertyDefinitionId, int? PropertyOptionId, bool? PropertyBool, int Offset, int PageSize);

/// <summary>Accesul la date pentru parteneri (implementat in Infrastructure, NET-06).</summary>
public interface IPartnerStore
{
    Task<PartnerPageDto> SearchAsync(PartnerSearchCriteria criteria, CancellationToken ct);

    Task<PartnerDetailDto?> GetAsync(int id, CancellationToken ct);

    Task<PartnerLookupsDto> GetLookupsAsync(int companyId, CancellationToken ct);

    Task<IReadOnlyList<PartnerHistoryEntryDto>> GetHistoryAsync(int id, CancellationToken ct);

    /// <summary>null = partenerul nu are CNP; arunca 50027 daca partenerul nu exista.</summary>
    Task<byte[]?> GetPersonalIdAsync(int id, CancellationToken ct);

    Task<SavedEntity> SaveAsync(PartnerRecord partner, int userId, CancellationToken ct);

    /// <summary>Creeaza seria implicita nefiscala a codurilor de partener daca firma nu are una pentru anul dat; true = creata acum.</summary>
    Task<bool> EnsureCodeSeriesAsync(int companyId, short year, int userId, CancellationToken ct);

    Task<SavedEntity> SaveAddressAsync(SavePartnerAddress command, int userId, CancellationToken ct);

    Task<SavedEntity> SaveBankAccountAsync(SavePartnerBankAccount command, int userId, CancellationToken ct);

    Task<SavedEntity> SaveContactAsync(SavePartnerContact command, int userId, CancellationToken ct);

    Task<SavedEntity> SaveGroupAsync(SavePartnerGroup command, int userId, CancellationToken ct);
}

/// <summary>
/// Partenerul pregatit pentru salvare: codul alocat, CUI-ul normalizat, CNP-ul deja criptat. PersonalIdChange = false pastreaza
/// CNP-ul existent; Properties = null pastreaza valorile proprietatilor; AnafCheckedAt = null pastreaza data ultimei verificari ANAF.
/// </summary>
public sealed record PartnerRecord(int? Id, string Code, string Name, byte PartnerType, string? FiscalCode, bool PersonalIdChange,
    byte[]? PersonalIdEncrypted, bool IsCustomer, bool IsSupplier, int? PartnerGroupId, bool VatPayer, bool VatOnCollection, bool AnafInactive,
    string? TradeRegisterNo, string Address, string City, string? CountyCode, string CountryCode, string? PostalCode, string? Phone, string? Email,
    int? IntraGroupCompanyId, bool IsActive, IReadOnlyList<PartnerPropertyValue>? Properties, byte[]? RowVersion, DateTime? AnafCheckedAt = null);

/// <summary>Criptarea datelor personale ale partenerilor (CNP, GDPR; 02.01, 9), cu cheile Data Protection ale instalarii.</summary>
public interface IPersonalDataProtector
{
    byte[] Protect(string value);

    string Unprotect(byte[] value);
}

/// <summary>
/// Seria codurilor de partener: tipul de document „Core.Partner” (nefiscal). Fara serie configurata, primul partener fara cod
/// creeaza seria implicita P-{An}-{Numar din 6 cifre}, cu trecere automata la anul urmator; se poate schimba in Configurare › Serii.
/// </summary>
public static class PartnerCodeSeries
{
    public const string DocumentType = "Core.Partner";
    public const string DefaultPrefix = "P";
    public const byte DefaultPadding = 6;
}
