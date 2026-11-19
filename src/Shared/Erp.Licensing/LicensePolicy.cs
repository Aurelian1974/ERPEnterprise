using System.Globalization;

namespace Erp.Licensing;

/// <summary>Regulile dintre licente si dintre licenta si kit (08, 4.2 verificarea 6; 4.5).</summary>
public static class LicensePolicy
{
    /// <summary>
    /// Verificarea 6 (08, 4.2, decizia D20): o licenta noua inlocuieste licenta activa doar daca nu e emisa inaintea ei.
    /// Aceeasi licenta incarcata din nou e acceptata (fara efect). Null = acceptata, altfel mesajul pentru utilizator.
    /// </summary>
    public static string? CheckReplacement(License? active, License candidate) =>
        active is null || string.Equals(active.LicenseId, candidate.LicenseId, StringComparison.Ordinal) || candidate.IssuedAt >= active.IssuedAt
            ? null
            : $"Licența încărcată este mai veche decât cea activă (emisă la {Format(active.IssuedAt)}).";

    /// <summary>
    /// Dreptul la actualizari (08, 4.5, decizia D19): un kit lansat dupa updatesUntil nu se instaleaza si nu se foloseste la
    /// actualizare (cod 41). Null = permis, altfel mesajul.
    /// </summary>
    public static string? CheckKitRelease(License license, DateOnly releaseDate, string kitVersion) =>
        license.UpdatesUntil is { } until && releaseDate > until
            ? $"Licența include actualizări lansate până la {Format(until)}; kitul {kitVersion} a fost lansat la {Format(releaseDate)}. Folosiți un kit lansat până la acea dată sau cereți furnizorului prelungirea."
            : null;

    private static string Format(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
