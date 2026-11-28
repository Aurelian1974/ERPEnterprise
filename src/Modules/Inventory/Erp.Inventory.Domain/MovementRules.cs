namespace Erp.Inventory.Domain;

/// <summary>Regulile datei unei miscari (02.05, S2 regula 8; decizia 6), fara acces la date.</summary>
public static class MovementRules
{
    /// <summary>
    /// O miscare poate fi datata cu cel mult BackdatingDays zile in urma (0 = doar azi); dreptul Inv.Movement.Backdate trece de limita doar
    /// cand BackdatingWithRight = 1. Data din viitor nu e permisa. Lunile inchise le refuza separat blocarea perioadelor.
    /// </summary>
    public static bool IsDateAllowed(DateOnly today, DateOnly movementDate, int backdatingDays, bool hasBackdateRight, bool backdatingWithRight)
    {
        if (movementDate > today)
        {
            return false;
        }

        return today.DayNumber - movementDate.DayNumber <= backdatingDays || (hasBackdateRight && backdatingWithRight);
    }
}

/// <summary>Ziua de lucru a Romaniei (Europe/Bucharest), din ceasul serverului.</summary>
public static class BusinessDay
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).DateTime);
}
