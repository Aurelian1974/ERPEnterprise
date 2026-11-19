namespace Erp.Licensing;

/// <summary>
/// Codul unic de inregistrare (CUI/CIF) romanesc (08, 3.3): 2–10 cifre, ultima e cifra de control calculata cu cheia
/// 753217532 (ANAF). Singura implementare, folosita de generator, installer si Nucleu.
/// </summary>
public static class FiscalCode
{
    private const string Key = "753217532";

    /// <summary>Doar cifrele CUI-ului: se elimina prefixul „RO” (orice majuscule) si spatiile; null daca forma nu e valida.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        if (text.StartsWith("RO", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return text.Length is >= 2 and <= 10 && text.All(char.IsAsciiDigit) ? text : null;
    }

    /// <summary>Forma e valida si cifra de control corespunde.</summary>
    public static bool IsValid(string? value)
    {
        var digits = Normalize(value);
        if (digits is null)
        {
            return false;
        }

        var body = digits[..^1].PadLeft(Key.Length, '0');
        var sum = 0;
        for (var i = 0; i < Key.Length; i++)
        {
            sum += (body[i] - '0') * (Key[i] - '0');
        }

        var control = sum * 10 % 11 % 10;
        return digits[^1] - '0' == control;
    }
}
