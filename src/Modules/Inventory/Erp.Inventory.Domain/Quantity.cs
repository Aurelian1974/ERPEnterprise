using System.Globalization;

namespace Erp.Inventory.Domain;

/// <summary>
/// Cantitatile in API circula ca text (Bune practici, UI: fara calcule cu number in browser), cu punct zecimal si cel mult sase zecimale
/// (decimal(19,6), SQL-07).
/// </summary>
public static class Quantity
{
    public static bool TryParse(string? value, out decimal quantity)
    {
        quantity = 0;
        var text = value?.Trim();
        return !string.IsNullOrEmpty(text)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out quantity)
            && decimal.Round(quantity, 6) == quantity
            && Math.Abs(quantity) < 10_000_000_000_000m;
    }

    /// <summary>Gol = fara valoare; altfel o cantitate valida, nenegativa.</summary>
    public static bool IsValidOptional(string? value) => string.IsNullOrWhiteSpace(value) || (TryParse(value, out var q) && q >= 0);

    public static decimal? ParseOptional(string? value) => TryParse(value, out var quantity) ? quantity : null;

    public static string Format(decimal quantity) => quantity.ToString("0.######", CultureInfo.InvariantCulture);

    public static string? Format(decimal? quantity) => quantity is { } q ? Format(q) : null;
}
