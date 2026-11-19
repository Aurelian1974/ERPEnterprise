using System.Text.RegularExpressions;
using Erp.Licensing;

namespace Erp.LicenseGen.Core;

/// <summary>Un CUI scris de operator, dupa normalizare (08, 3.2).</summary>
public sealed record FiscalCodeEntry(string Input, string? Digits, bool IsValid, bool IsDuplicate)
{
    public bool IsAccepted => IsValid && !IsDuplicate;

    public string? Problem => Digits is null ? "nu are forma unui CUI"
        : !IsValid ? "cifra de control este greșită"
        : IsDuplicate ? "apare de mai multe ori"
        : null;
}

/// <summary>
/// Campul „CUI-uri” al generatorului (08, 3.2): CUI-uri separate prin rand nou, virgula, punct si virgula, spatiu sau „:”,
/// cu sau fara prefixul RO (inclusiv „RO 12345674”, cu spatiu). Rezultatul e lista normalizata „cifre:cifre”.
/// </summary>
public static partial class FiscalCodeInput
{
    public static IReadOnlyList<FiscalCodeEntry> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var joined = RoPrefix().Replace(text, "RO");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<FiscalCodeEntry>();
        foreach (var token in Separators().Split(joined).Where(t => t.Length > 0))
        {
            var digits = FiscalCode.Normalize(token);
            var valid = digits is not null && FiscalCode.IsValid(digits);
            entries.Add(new FiscalCodeEntry(token, digits, valid, digits is not null && !seen.Add(digits)));
        }

        return entries;
    }

    /// <summary>Lista pentru licenta, doar daca toate CUI-urile sunt valide si unice; altfel null.</summary>
    public static string? Normalized(IReadOnlyList<FiscalCodeEntry> entries) =>
        entries.Count > 0 && entries.All(e => e.IsAccepted) ? string.Join(':', entries.Select(e => e.Digits)) : null;

    [GeneratedRegex(@"\bRO\s+(?=\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoPrefix();

    [GeneratedRegex(@"[\s,;:]+", RegexOptions.CultureInvariant)]
    private static partial Regex Separators();
}
