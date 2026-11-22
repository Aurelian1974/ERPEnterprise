using System.Globalization;

namespace Erp.Finance.Domain;

/// <summary>
/// Sumele in API circula ca text (Bune practici, UI: fara calcule monetare cu number in browser), cu punct zecimal si cel mult doua
/// zecimale (decimal(19,2), SQL-07).
/// </summary>
public static class Money
{
    public static bool TryParse(string? value, out decimal amount)
    {
        amount = 0;
        var text = value?.Trim();
        return !string.IsNullOrEmpty(text)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount)
            && decimal.Round(amount, 2) == amount
            && Math.Abs(amount) < 100_000_000_000_000_000m;
    }

    public static decimal? ParseOptional(string? value) => TryParse(value, out var amount) ? amount : null;

    public static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string? Format(decimal? amount) => amount is { } a ? Format(a) : null;
}
