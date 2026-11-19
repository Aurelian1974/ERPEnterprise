using Erp.Core.Application.Licensing;
using Erp.Core.Contracts;
using Erp.Licensing;
using Microsoft.Extensions.Logging;

namespace Erp.Core.Infrastructure.Licensing;

/// <summary>Starea licentei dupa 02.01, 4.4: avertizare cu 30 de zile inainte, gratie 15 zile dupa expirare, apoi doar citire.</summary>
public static class LicenseStates
{
    public const int ExpiringSoonDays = 30;
    public const int GraceDays = 15;

    public static LicenseState For(DateOnly? validUntil, DateOnly referenceDate) => validUntil is not { } until ? LicenseState.Valid
        : referenceDate > until.AddDays(GraceDays) ? LicenseState.ReadOnly
        : referenceDate > until ? LicenseState.Grace
        : referenceDate > until.AddDays(-ExpiringSoonDays) ? LicenseState.ExpiringSoon
        : LicenseState.Valid;
}

/// <summary>
/// Optiunile licentei pentru Erp.Host. In Development, fara licenta valida, toate modulele sunt active.
/// LoadFromDatabase = false doar in testele fara baza (licenta ramane cea data de test).
/// </summary>
public sealed record LicenseOptions(bool Development, bool LoadFromDatabase = true);

/// <summary>Ce a gasit ultima reincarcare (08, 4.1): licenta autentica din baza, data de referinta, firmele nelicentiate.</summary>
public sealed record LicenseSnapshotData(License? License, byte KeyId, DateOnly HighestSeenDate, bool ClockBehind, IReadOnlySet<int> UnlicensedCompanies)
{
    public static LicenseSnapshotData Empty { get; } = new(null, 0, DateOnly.MinValue, false, new HashSet<int>());
}

/// <summary>
/// Licenta instalarii (08, 4.1–4.4), din baza, pastrata in memorie si inlocuita atomic de <see cref="LicenseRefresher"/>.
/// Fara licenta valida, instalarea ramane doar pentru consultare, cu modulul Core activ (ca administratorul sa poata incarca
/// o licenta); datele nu se blocheaza niciodata. In Development, fara licenta, toate modulele sunt active.
/// </summary>
public sealed class LicenseInfo(LicenseOptions options, TimeProvider time, ILogger<LicenseInfo> log) : ILicenseInfo, ILicenseSnapshot
{
    private LicenseSnapshotData data = LicenseSnapshotData.Empty;

    private LicenseSnapshotData Data => Volatile.Read(ref data);

    private bool DevelopmentWithoutLicense => options.Development && Data.License is null;

    public bool HasLicense => Data.License is not null;

    public License? Current => Data.License;

    public byte KeyId => Data.KeyId;

    public DateOnly ReferenceDate
    {
        get
        {
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var highest = Data.HighestSeenDate;
            return highest > today ? highest : today;
        }
    }

    public bool ClockBehind => Data.ClockBehind;

    public DateOnly? ValidUntil => Data.License?.ValidUntil;

    public DateOnly? UpdatesUntil => Data.License?.UpdatesUntil;

    public string? CustomerName => Data.License?.CustomerName;

    public LicenseState State => DevelopmentWithoutLicense ? LicenseState.Valid
        : Data.License is not { } license ? LicenseState.ReadOnly
        : LicenseStates.For(license.ValidUntil, ReferenceDate);

    public bool IsModuleActive(string moduleCode) => DevelopmentWithoutLicense
        || string.Equals(moduleCode, LicenseModules.Core, StringComparison.Ordinal)
        || (Data.License?.Modules.Contains(moduleCode, StringComparer.Ordinal) ?? false);

    public IReadOnlyCollection<string> ActiveModules => DevelopmentWithoutLicense ? [.. LicenseModules.Known]
        : Data.License is { } license ? [.. license.Modules.Where(LicenseModules.Known.Contains).Append(LicenseModules.Core).Distinct()]
        : [LicenseModules.Core];

    public bool IsFiscalCodeLicensed(string fiscalCode) => DevelopmentWithoutLicense || (Data.License?.ContainsFiscalCode(fiscalCode) ?? false);

    public bool IsCompanyLicensed(int companyId) => !Data.UnlicensedCompanies.Contains(companyId);

    public int? GetLimit(string limitCode) => Data.License is not { } license ? null : limitCode switch
    {
        "companies" => license.Companies,
        "users" => license.Limits.Users,
        "payrollEmployees" => license.Limits.PayrollEmployees,
        "posTerminals" => license.Limits.PosTerminals,
        _ => null,
    };

    /// <summary>Inlocuieste starea; jurnalizeaza doar schimbarile, fara continutul licentei in afara de numar si client.</summary>
    public void Apply(LicenseSnapshotData next)
    {
        var previous = Interlocked.Exchange(ref data, next);
        if (!string.Equals(previous.License?.LicenseId, next.License?.LicenseId, StringComparison.Ordinal))
        {
            if (next.License is { } license)
            {
                log.LogInformation("Licența {LicenseId} ({CustomerName}): {Companies} CUI-uri, module {Modules}, valabilă până la {ValidUntil}",
                    license.LicenseId, license.CustomerName, license.Companies, license.Modules, license.ValidUntil?.ToString() ?? "nelimitat");
            }
            else if (options.Development)
            {
                log.LogWarning("Mediu Development fără licență validă: toate modulele sunt active.");
            }
            else
            {
                log.LogCritical("Licența lipsește sau nu este validă: aplicația funcționează doar pentru consultare.");
            }
        }

        if (next.UnlicensedCompanies.Count > 0 && !next.UnlicensedCompanies.SetEquals(previous.UnlicensedCompanies))
        {
            log.LogWarning("Firme active al căror CUI nu e în licență (doar consultare): {CompanyIds}", next.UnlicensedCompanies.Order());
        }
    }
}
