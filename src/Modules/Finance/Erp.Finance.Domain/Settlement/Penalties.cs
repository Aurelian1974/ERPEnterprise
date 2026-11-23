namespace Erp.Finance.Domain.Settlement;

/// <summary>Rata de referinta BNR valabila de la o data (F3 3.10, OG 13/2011), in procente pe an.</summary>
public sealed record ReferenceRate(DateOnly ValidFrom, decimal Rate);

/// <summary>
/// O partida de client pentru calculul penalitatilor (F3 3.10): suma si scadenta, alocarile pe ea (data si suma, in moneda partidei), conditiile
/// din profil (rata pe zi in procente, 0 = dobanda legala; zilele de gratie; plafonul la debit) si ce s-a calculat deja pe ea (suma si ultima zi).
/// </summary>
public sealed record PenaltyItem(long OpenItemId, DateOnly DueDate, decimal Amount, IReadOnlyList<(DateOnly Date, decimal Amount)> Payments, int GraceDays,
    decimal RatePerDay, bool CapAtPrincipal, decimal AlreadyCharged = 0, DateOnly? ChargedThrough = null);

/// <summary>Un interval cu rest de plata constant si aceeasi rata; zilele se numara inclusiv capetele.</summary>
public sealed record PenaltyInterval(long OpenItemId, DateOnly From, DateOnly To, int Days, decimal BaseAmount, decimal? RatePerDay, decimal? AnnualRate,
    decimal PenaltyAmount);

/// <summary>Lipseste rata de referinta pentru o zi la care se aplica dobanda legala.</summary>
public sealed class ReferenceRateMissingException(DateOnly day) : Exception($"Lipsește rata de referință BNR valabilă la {day:dd.MM.yyyy}.")
{
    public DateOnly Day { get; } = day;
}

/// <summary>Regulile penalitatilor de intarziere (02.02, F3 3.10; OG 13/2011, Legea 72/2013).</summary>
public static class PenaltyRules
{
    /// <summary>Dobanda legala penalizatoare intre profesionisti: rata de referinta + 8 puncte procentuale pe an (OG 13/2011, art. 3).</summary>
    public const decimal LegalSpread = 8m;

    /// <summary>Suma fixa de recuperare pe factura intarziata, intre profesionisti (Legea 72/2013), in euro.</summary>
    public const decimal FixedRecoveryCostEur = 40m;

    /// <summary>
    /// Intervalele penalizate ale unei partide in [from, to] (F3 3.10 regulile 1–3): de la scadenta + zilele de gratie + 1 (sau de dupa ultima
    /// zi deja calculata) pana la fiecare alocare, inclusiv ziua ei; de a doua zi baza scade cu suma alocata. Cu dobanda legala, intervalele se
    /// rup si la schimbarea ratei de referinta (semestrul). Penalitatea: baza × rata pe zi × zile, sau baza × rata anuala × zile / 365,
    /// rotunjita la 2 zecimale; cu plafon, totalul (cu cel deja calculat) nu depaseste debitul.
    /// </summary>
    public static IReadOnlyList<PenaltyInterval> Calculate(PenaltyItem item, DateOnly from, DateOnly to, IReadOnlyList<ReferenceRate> referenceRates)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(referenceRates);
        var start = Max(Max(item.DueDate.AddDays(item.GraceDays + 1), from), item.ChargedThrough?.AddDays(1) ?? DateOnly.MinValue);
        var legal = item.RatePerDay == 0;
        var rates = referenceRates.OrderBy(r => r.ValidFrom).ToList();
        var result = new List<PenaltyInterval>();
        var room = item.CapAtPrincipal ? item.Amount - item.AlreadyCharged : decimal.MaxValue;
        for (var s = start; s <= to && room > 0;)
        {
            var paid = item.Payments.Where(p => p.Date < s).Sum(p => p.Amount);
            var baseAmount = item.Amount - paid;
            if (baseAmount <= 0)
            {
                break;
            }

            var end = to;
            var nextPayment = item.Payments.Where(p => p.Date >= s && p.Date < end).Select(p => (DateOnly?)p.Date).Min();
            if (nextPayment is { } payment)
            {
                end = payment;
            }

            decimal? annual = null;
            if (legal)
            {
                var rate = rates.LastOrDefault(r => r.ValidFrom <= s) ?? throw new ReferenceRateMissingException(s);
                annual = rate.Rate + LegalSpread;
                var change = rates.FirstOrDefault(r => r.ValidFrom > s && r.ValidFrom <= end);
                if (change is not null)
                {
                    end = change.ValidFrom.AddDays(-1);
                }
            }

            var days = end.DayNumber - s.DayNumber + 1;
            var penalty = Math.Round(legal ? baseAmount * annual!.Value / 100m * days / 365m : baseAmount * item.RatePerDay / 100m * days, 2,
                MidpointRounding.AwayFromZero);
            penalty = Math.Min(penalty, room);
            if (penalty > 0)
            {
                result.Add(new PenaltyInterval(item.OpenItemId, s, end, days, baseAmount, legal ? null : item.RatePerDay, annual, penalty));
                room -= penalty;
            }

            s = end.AddDays(1);
        }

        return result;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
