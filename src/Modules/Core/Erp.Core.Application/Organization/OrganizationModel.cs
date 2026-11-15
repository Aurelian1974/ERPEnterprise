using Erp.Core.Contracts;

namespace Erp.Core.Application.Organization;

/// <summary>Rezultatul unei salvari: cheia si RowVersion-ul nou (concurenta optimista, SQL-18).</summary>
public sealed record SavedEntity(int Id, byte[] RowVersion);

public sealed record CompanyGroupNode(int Id, string Code, string Name, int? ParentGroupId, int? ParentCompanyId, bool IsActive, byte[] RowVersion);

public sealed record CompanyNode(int Id, int? GroupId, string Code, string Name, string FiscalCode, bool VatPayer, bool IsActive);

public sealed record BranchNode(int Id, int CompanyId, string Code, string Name, string Address, string City, string? CountyCode,
    string CountryCode, string? PostalCode, bool IsActive, byte[] RowVersion);

/// <summary>LocationType: 1 birou, 2 depozit, 3 magazin, 4 fabrica, 5 santier, 6 punct de livrare.</summary>
public sealed record LocationNode(int Id, int CompanyId, int? BranchId, string Code, string Name, byte LocationType, string? Address,
    string? City, string? CountyCode, string? CountryCode, string? PostalCode, bool IsActive, byte[] RowVersion);

/// <summary>Arborele structurii organizatorice (macheta 03), filtrat dupa drepturi.</summary>
public sealed record OrgTreeDto(IReadOnlyList<CompanyGroupNode> Groups, IReadOnlyList<CompanyNode> Companies,
    IReadOnlyList<BranchNode> Branches, IReadOnlyList<LocationNode> Locations);

/// <summary>Capitalul social e sir zecimal invariant (de exemplu „200.00”), ca toate valorile banesti din API (UI-05).</summary>
public sealed record CompanyDetailDto(int Id, int? GroupId, string Code, string Name, string FiscalCode, bool VatPayer, bool VatOnCollection,
    string TradeRegisterNo, string? ShareCapital, string Address, string City, string? CountyCode, string CountryCode, string? PostalCode,
    string FunctionalCurrency, int DefaultPrecisionPolicyId, bool IncludeInConsolidation, bool IsActive, string ModifiedByName,
    DateTime LastChangedAt, byte[] RowVersion, IReadOnlyList<CompanyBankAccountDto> BankAccounts);

public sealed record CompanyBankAccountDto(int Id, int CompanyId, string Iban, string BankName, string Currency, bool IsDefault, bool IsActive, byte[] RowVersion);

public sealed record CountryDto(string Code, string Name);

public sealed record CountyDto(string Code, string CountryCode, string Name);

public sealed record PrecisionPolicyDto(int Id, string Code, string Name);

public sealed record DocumentTypeDto(string Code, string ModuleCode, string Name, bool IsFiscalDefault);

public sealed record OrgLookupsDto(IReadOnlyList<CountryDto> Countries, IReadOnlyList<CountyDto> Counties,
    IReadOnlyList<PrecisionPolicyDto> PrecisionPolicies, IReadOnlyList<DocumentTypeDto> DocumentTypes);

/// <summary>O firma din selectorul de firma (antet).</summary>
public sealed record CompanySummaryDto(int Id, string Code, string Name);

public sealed record NumberSeriesDto(int Id, int CompanyId, int? BranchId, string? BranchCode, string? BranchName, string DocumentTypeCode,
    string DocumentTypeName, string Prefix, short Year, long NextNumber, long FirstNumber, long UsedCount, byte Padding, bool IsFiscal,
    bool IsDefault, bool AutoRollover, DateTime? LastUsedAt, DateTime? ClosedAt, byte[] RowVersion);

/// <summary>Accesul la date pentru structura organizatorica si serii (implementat in Infrastructure, NET-06).</summary>
public interface IOrganizationStore
{
    Task<OrgTreeDto> GetTreeAsync(int userId, CancellationToken ct);

    Task<CompanyDetailDto?> GetCompanyAsync(int companyId, CancellationToken ct);

    Task<OrgLookupsDto> GetLookupsAsync(CancellationToken ct);

    Task<IReadOnlyList<CompanySummaryDto>> GetUserCompaniesAsync(int userId, CancellationToken ct);

    Task<SavedEntity> SaveGroupAsync(SaveCompanyGroup command, int userId, CancellationToken ct);

    Task<SavedEntity> SaveCompanyAsync(SaveCompany command, int userId, CancellationToken ct);

    Task<SavedEntity> SaveBranchAsync(SaveBranch command, int userId, CancellationToken ct);

    Task<SavedEntity> SaveLocationAsync(SaveLocation command, int userId, CancellationToken ct);

    Task<SavedEntity> SaveBankAccountAsync(SaveCompanyBankAccount command, int userId, CancellationToken ct);

    Task<IReadOnlyList<NumberSeriesDto>> ListNumberSeriesAsync(int companyId, short? year, CancellationToken ct);

    Task<SavedEntity> SaveNumberSeriesAsync(NumberSeries.SaveNumberSeries command, int userId, CancellationToken ct);

    Task<SavedEntity> CloseNumberSeriesAsync(NumberSeries.CloseNumberSeries command, int userId, CancellationToken ct);
}

/// <summary>
/// Firma asupra careia se lucreaza trebuie sa fie firma contextului (antetul X-Company-Id; permisiunile sunt pe ea) sau
/// contextul instalarii (X-Company-Id: 0, doar rolurile la nivel de instalare). Grupurile si firmele noi cer instalarea.
/// </summary>
internal static class CompanyScope
{
    public static void Ensure(ICurrentContext context, int companyId)
    {
        if (context.CompanyId != 0 && context.CompanyId != companyId)
        {
            throw Forbidden("Operațiunea privește altă firmă decât firma curentă.");
        }
    }

    public static void EnsureInstallation(ICurrentContext context)
    {
        if (context.CompanyId != 0)
        {
            throw Forbidden("Grupurile și firmele noi se administrează la nivelul instalării, de către administrator.");
        }
    }

    private static ErpException Forbidden(string message) => new(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, message);
}
