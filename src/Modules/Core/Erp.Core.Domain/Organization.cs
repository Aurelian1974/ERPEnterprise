using System.Globalization;
using System.Text.RegularExpressions;

namespace Erp.Core.Domain;

/// <summary>
/// Codul unic de inregistrare (CUI/CIF) romanesc. Algoritmul (cheia 753217532) e unul singur, in Erp.Licensing.FiscalCode
/// (08, 3.3), folosit si de licenta; aici ramane numele din domeniu.
/// </summary>
public static class RomanianFiscalCode
{
    /// <summary>Doar cifrele CUI-ului, fara „RO” si spatii; null daca forma nu e valida.</summary>
    public static string? Normalize(string? value) => Licensing.FiscalCode.Normalize(value);

    public static bool IsValid(string? value) => Licensing.FiscalCode.IsValid(value);
}

/// <summary>IBAN dupa ISO 13616: tara, cifre de control, cont; verificarea mod 97 = 1. Romania: 24 de caractere.</summary>
public static partial class Iban
{
    /// <summary>Majuscule, fara spatii.</summary>
    public static string Normalize(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    public static bool IsValid(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var iban = Normalize(value);
        if (!Shape().IsMatch(iban) || (iban.StartsWith("RO", StringComparison.Ordinal) && iban.Length != 24))
        {
            return false;
        }

        var remainder = 0;
        foreach (var c in iban[4..] + iban[..4])
        {
            var digits = char.IsAsciiDigit(c) ? (c - '0').ToString(CultureInfo.InvariantCulture) : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture);
            foreach (var d in digits)
            {
                remainder = (remainder * 10 + (d - '0')) % 97;
            }
        }

        return remainder == 1;
    }

    [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$")]
    private static partial Regex Shape();
}

/// <summary>
/// Numarul de ordine din registrul comertului (02.01, 3.1): formatul clasic (J40/1234/2015, F, C) si formatul unic ONRC
/// (Legea 265/2022, din 2024: litera, anul, 6 cifre numarul, 2 cifre judetul, cifra de control, de exemplu J2024000123401).
/// Se verifica doar forma; cifra de control a formatului nou nu se recalculeaza.
/// </summary>
public static partial class TradeRegisterNumber
{
    public static bool IsValid(string? value) =>
        value is not null && (Classic().IsMatch(value) || Unified().IsMatch(value));

    [GeneratedRegex("^[JFC](0?[1-9]|[1-4][0-9]|5[0-2])/[0-9]{1,8}/(19|20)[0-9]{2}$")]
    private static partial Regex Classic();

    [GeneratedRegex("^[JFC](19|20)[0-9]{2}[0-9]{6}[0-9]{2}[0-9]$")]
    private static partial Regex Unified();
}

/// <summary>Numarul de document formatat ca pe documente (macheta 07c): {Prefix}-{An}-{Numar}, cu zerouri la stanga.</summary>
public static class DocumentNumberFormat
{
    public static string Format(string prefix, int year, long number, int padding) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}-{year}-{number.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0')}");
}
