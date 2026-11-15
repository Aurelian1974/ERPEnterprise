using Erp.Core.Contracts;
using Erp.Licensing;
using Microsoft.Extensions.Logging;

namespace Erp.Core.Infrastructure.Licensing;

/// <summary>Starea licentei dupa 02.01, 4.4: avertizare cu 30 de zile inainte, gratie 15 zile dupa expirare, apoi doar citire.</summary>
public static class LicenseStates
{
    public const int ExpiringSoonDays = 30;
    public const int GraceDays = 15;

    public static LicenseState For(DateOnly validUntil, DateOnly today) =>
        today > validUntil.AddDays(GraceDays) ? LicenseState.ReadOnly
        : today > validUntil ? LicenseState.Grace
        : today > validUntil.AddDays(-ExpiringSoonDays) ? LicenseState.ExpiringSoon
        : LicenseState.Valid;
}

/// <summary>Optiunile licentei pentru Erp.Host si Erp.Worker.</summary>
public sealed record LicenseOptions(string? LicensePath, bool Development);

/// <summary>
/// ILicenseInfo din fisierul de licenta (4.4). Fara licenta valida, instalarea ramane doar pentru consultare, cu modulul Core
/// activ (ca administratorul sa poata incarca o licenta noua); datele nu se blocheaza niciodata.
/// In mediul Development, fara licenta valida, toate modulele sunt active (licenta de dezvoltare, jurnalizata).
/// Reverificarea zilnica si refuzul la pornire (A7) se adauga in etapa Licenta.
/// </summary>
public sealed class LicenseInfo : ILicenseInfo
{
    private readonly HashSet<string> modules;
    private readonly HashSet<string> fiscalCodes = new(StringComparer.Ordinal);
    private readonly LicenseLimits? limits;
    private readonly TimeProvider time;
    private readonly bool development;

    public LicenseInfo(LicenseOptions options, LicenseVerifier verifier, TimeProvider time, ILogger<LicenseInfo> log)
    {
        this.time = time;
        var result = options.LicensePath is { Length: > 0 } path && File.Exists(path) ? verifier.Verify(File.ReadAllText(path)) : null;
        var payload = result is { Status: LicenseStatus.Valid or LicenseStatus.Expired } ? result.Payload : null;

        if (payload is not null)
        {
            modules = new HashSet<string>(payload.Modules, StringComparer.Ordinal);
            fiscalCodes = new HashSet<string>(payload.FiscalCodes.Select(Domain.RomanianFiscalCode.Normalize).OfType<string>(), StringComparer.Ordinal);
            limits = payload.Limits;
            ValidUntil = payload.ValidUntil;
            CustomerName = payload.CustomerName;
            log.LogInformation("Licența {LicenseId}: module {Modules}, valabilă până la {ValidUntil}", payload.LicenseId, payload.Modules, payload.ValidUntil);
            return;
        }

        development = options.Development;
        modules = new HashSet<string>(StringComparer.Ordinal) { "Core" };
        ValidUntil = DateOnly.MinValue;
        if (development)
        {
            log.LogWarning("Mediu Development fără licență validă ({Reason}): toate modulele sunt active.", result?.Message ?? "fișier lipsă");
        }
        else
        {
            log.LogCritical("Licența lipsește sau nu este validă ({Reason}): aplicația funcționează doar pentru consultare.", result?.Message ?? "fișier lipsă");
        }
    }

    public DateOnly ValidUntil { get; }

    public string? CustomerName { get; }

    public LicenseState State => development ? LicenseState.Valid
        : ValidUntil == DateOnly.MinValue ? LicenseState.ReadOnly
        : LicenseStates.For(ValidUntil, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));

    public bool IsModuleActive(string moduleCode) => development || modules.Contains(moduleCode);

    public IReadOnlyCollection<string> ActiveModules => development ? [.. LicenseModules.Known] : modules;

    public bool IsFiscalCodeLicensed(string fiscalCode) =>
        development || (Domain.RomanianFiscalCode.Normalize(fiscalCode) is { } digits && fiscalCodes.Contains(digits));

    public int? GetLimit(string limitCode) => limits is null ? null : limitCode switch
    {
        "companies" => limits.Companies,
        "users" => limits.Users,
        "payrollEmployees" => limits.PayrollEmployees,
        "posTerminals" => limits.PosTerminals,
        _ => null,
    };
}
