namespace Erp.Finance.Domain.Periods;

/// <summary>
/// Rezerva legala propusa la inchiderea anului (C3 3.6 pasul 2; Legea 31/1990, art. 183): 5% din profitul contabil brut, pana cand rezerva
/// legala ajunge la 20% din capitalul social. Fara profit nu se propune nimic.
/// </summary>
public static class LegalReserve
{
    public const decimal Rate = 0.05m;
    public const decimal Cap = 0.20m;

    public static decimal Propose(decimal grossProfit, decimal shareCapital, decimal existingReserve)
    {
        if (grossProfit <= 0)
        {
            return 0m;
        }

        var room = Math.Max(0m, Math.Round(shareCapital * Cap, 2, MidpointRounding.AwayFromZero) - existingReserve);
        return Math.Min(Math.Round(grossProfit * Rate, 2, MidpointRounding.AwayFromZero), room);
    }

    /// <summary>Suma aleasa respecta plafonul: rezerva existenta + suma nu trece de 20% din capital.</summary>
    public static bool WithinCap(decimal amount, decimal shareCapital, decimal existingReserve) =>
        amount >= 0 && existingReserve + amount <= Math.Round(shareCapital * Cap, 2, MidpointRounding.AwayFromZero);
}
