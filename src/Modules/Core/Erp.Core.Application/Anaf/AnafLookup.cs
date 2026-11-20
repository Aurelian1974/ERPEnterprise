using Erp.Core.Contracts;
using Erp.Core.Domain;

namespace Erp.Core.Application.Anaf;

/// <summary>
/// Datele unui contribuabil din registrele ANAF (serviciul web de verificare a contribuabililor), in forma fisei de partener.
/// CountyCode e codul auto al judetului (ca in core.County); EInvoiceRegistered, SplitVat si RegistrationState sunt informative,
/// fara camp pe partener.
/// </summary>
public sealed record AnafCompanyDto(string FiscalCode, string Name, string? TradeRegisterNo, bool VatPayer, bool VatOnCollection, bool AnafInactive,
    bool SplitVat, bool EInvoiceRegistered, string? RegistrationState, string? Address, string? City, string? CountyCode, string? PostalCode,
    string? Phone, DateTime CheckedAt);

/// <summary>Clientul serviciului web ANAF (Infrastructure); adresa vine din setarea Anaf.VatRegistryUrl.</summary>
public interface IAnafVatRegistry
{
    /// <summary>null = CUI-ul nu figureaza in registre. ErpException Unavailable daca serviciul nu raspunde corect.</summary>
    Task<AnafCompanyDto?> LookupAsync(string fiscalCode, DateOnly date, CancellationToken ct);
}

public static class AnafErrorCodes
{
    public const string Unavailable = "CORE_ANAF_UNAVAILABLE";
    public const string NotFound = "CORE_ANAF_NOT_FOUND";
}

/// <summary>
/// „Preia de la ANAF” pe fisa partenerului (02.01, 11): datele contribuabilului la data de azi. E interogare, nu comanda: nu scrie
/// nimic si ruleaza in afara oricarei tranzactii (fara apeluri externe in tranzactie); fisa se salveaza apoi de utilizator.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerManage)]
public sealed record LookupAnafCompany(string FiscalCode) : IQuery<AnafCompanyDto>;

internal sealed class LookupAnafCompanyHandler(IAnafVatRegistry registry, TimeProvider clock) : IQueryHandler<LookupAnafCompany, AnafCompanyDto>
{
    public async Task<AnafCompanyDto> Handle(LookupAnafCompany request, CancellationToken cancellationToken)
    {
        // Pipeline-ul valideaza doar comenzile; interogarea isi verifica singura CUI-ul, ca eroare pe camp.
        if (!RomanianFiscalCode.IsValid(request.FiscalCode))
        {
            throw new ErpValidationException(new Dictionary<string, string[]> { ["fiscalCode"] = ["CUI-ul nu este valid (cifra de control greșită)."] });
        }

        var fiscalCode = RomanianFiscalCode.Normalize(request.FiscalCode)!;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return await registry.LookupAsync(fiscalCode, today, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound(AnafErrorCodes.NotFound, $"CUI-ul {fiscalCode} nu figurează în registrele ANAF.");
    }
}
