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

/// <summary>Tipul articolului (02.01, 3.3).</summary>
public enum ItemType : byte
{
    Merchandise = 1,
    RawMaterial = 2,
    Material = 3,
    FinishedProduct = 4,
    SemiFinished = 5,
    Service = 6,
    Packaging = 7,
    FixedAsset = 8,
}

/// <summary>Tipul codului de bare: 1 EAN-13, 2 EAN-8, 3 GTIN-14 (cifra de control GS1), 4 intern (liber).</summary>
public static partial class Barcodes
{
    public static bool IsValid(byte type, string? value) => value is not null && type switch
    {
        1 => value.Length == 13 && Gs1CheckDigit(value),
        2 => value.Length == 8 && Gs1CheckDigit(value),
        3 => value.Length == 14 && Gs1CheckDigit(value),
        4 => Internal().IsMatch(value),
        _ => false,
    };

    /// <summary>Cifra de control GS1: ponderi 3 si 1 de la dreapta (fara cifra de control), suma completata la multiplu de 10.</summary>
    public static bool Gs1CheckDigit(string value)
    {
        if (!value.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = value.Length - 2; i >= 0; i--)
        {
            sum += (value[i] - '0') * ((value.Length - 2 - i) % 2 == 0 ? 3 : 1);
        }

        return (10 - sum % 10) % 10 == value[^1] - '0';
    }

    [GeneratedRegex("^[A-Za-z0-9._/-]{1,50}$")]
    private static partial Regex Internal();
}

/// <summary>Codul NC (Nomenclatura Combinata, Intrastat si e-Transport): 4–10 cifre, spatiile se ignora.</summary>
public static partial class CombinedNomenclature
{
    public static string Normalize(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal);

    public static bool IsValid(string? value) => value is not null && Shape().IsMatch(Normalize(value));

    [GeneratedRegex("^[0-9]{4,10}$")]
    private static partial Regex Shape();
}
