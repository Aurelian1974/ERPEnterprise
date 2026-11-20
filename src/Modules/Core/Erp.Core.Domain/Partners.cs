using System.Text.RegularExpressions;

namespace Erp.Core.Domain;

/// <summary>Tipul partenerului (02.01, 3.2).</summary>
public enum PartnerType : byte
{
    LegalRomania = 1,
    LegalEu = 2,
    LegalNonEu = 3,
    Individual = 4,
}

/// <summary>
/// Codul numeric personal (CNP): 13 cifre, prima 1–9, luna 01–12, ziua 01–31, cifra de control cu cheia 279146358279
/// (restul 10 devine 1). Se verifica forma si cifra de control, nu existenta persoanei.
/// </summary>
public static partial class RomanianPersonalId
{
    private const string Key = "279146358279";

    public static string Normalize(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal);

    public static bool IsValid(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var cnp = Normalize(value);
        if (!Shape().IsMatch(cnp))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < Key.Length; i++)
        {
            sum += (cnp[i] - '0') * (Key[i] - '0');
        }

        var control = sum % 11;
        return (control == 10 ? 1 : control) == cnp[12] - '0';
    }

    [GeneratedRegex("^[1-9][0-9]{2}(0[1-9]|1[0-2])(0[1-9]|[12][0-9]|3[01])[0-9]{6}$")]
    private static partial Regex Shape();
}

/// <summary>Codul de TVA al unui partener din UE: prefixul tarii (2 litere, Grecia „EL”) urmat de 2–13 litere sau cifre. Doar forma; VIES verifica existenta.</summary>
public static partial class EuVatNumber
{
    public static string Normalize(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    public static bool IsValid(string? value) => value is not null && Shape().IsMatch(Normalize(value));

    [GeneratedRegex("^[A-Z]{2}[A-Z0-9+*]{2,13}$")]
    private static partial Regex Shape();
}
