namespace Erp.Finance.Domain.Planning;

/// <summary>
/// Un interval de intarziere fata de scadenta cu ponderea lui in istoricul de incasari al clientului (F5 3.4): zilele (DaysTo null = fara
/// limita), ponderea sumei incasate (0–1), intarzierea medie ponderata si daca e „incert” (nu intra in sold).
/// </summary>
public sealed record DelayShare(int DaysFrom, int? DaysTo, decimal Share, int AvgDaysLate, bool IsUncollectible);

/// <summary>O transa de incasare in prognoza: data, suma si daca e incerta (intervalul marcat IsUncollectible).</summary>
public sealed record CollectionTranche(DateOnly Date, decimal Amount, bool IsUncertain);

/// <summary>Recurenta unei sume planificate (F5 3.5): 0 o data, 1 lunar in ziua X, 2 saptamanal, 3 trimestrial, 4 anual.</summary>
public enum Recurrence : byte
{
    Once = 0,
    Monthly = 1,
    Weekly = 2,
    Quarterly = 3,
    Yearly = 4,
}

/// <summary>Regulile prognozei de cash-flow (02.02, F5 3.4, 3.5, 4.4).</summary>
public static class CashFlowRules
{
    /// <summary>
    /// Imparte o factura de client in transe dupa istoricul de plata (F5 3.4, F5-A10): o transa pe interval, cu ponderea lui, la scadenta +
    /// intarzierea medie a intervalului (cel mai devreme in ziua de pornire). O factura restanta foloseste doar intervalele care se termina dupa
    /// intarzierea curenta, cu ponderile renormalizate; fara pondere ramasa, o singura transa in ziua de pornire + jumatatea intervalului
    /// curent. Fara istoric (distributie goala): la scadenta (restantele: ziua de pornire). Scenariul prudent muta fiecare pondere pe intervalul
    /// urmator, adauga zilele suplimentare si scoate restantele. Ultima transa ia restul sumei (P7).
    /// </summary>
    public static IReadOnlyList<CollectionTranche> Collections(decimal amount, DateOnly dueDate, DateOnly start, IReadOnlyList<DelayShare> distribution,
        bool prudent = false, int extraDays = 0)
    {
        var overdue = start.DayNumber - dueDate.DayNumber;
        if (prudent && overdue > 0)
        {
            return [];
        }

        var buckets = distribution.OrderBy(b => b.DaysFrom).ToList();
        if (buckets.Count == 0 || buckets.Sum(b => b.Share) <= 0)
        {
            return [new CollectionTranche(Max(dueDate.AddDays(extraDays), start), amount, false)];
        }

        if (prudent)
        {
            // Fiecare pondere trece pe intervalul urmator (ultimul ramane), cu intarzierea medie a acestuia; un interval fara istoric are
            // intarzierea de la mijlocul lui (cel fara sfarsit: inceputul).
            var original = buckets;
            buckets = original.Select((b, i) => b with
            {
                Share = i == 0 ? 0 : original[i - 1].Share + (i == original.Count - 1 ? b.Share : 0),
                AvgDaysLate = b.Share > 0 ? b.AvgDaysLate : Middle(b),
            }).ToList();
        }

        var remaining = overdue > 0 ? buckets.Where(b => b.DaysTo is null || b.DaysTo >= overdue).ToList() : buckets;
        var total = remaining.Sum(b => b.Share);
        if (total <= 0)
        {
            var current = buckets.LastOrDefault(b => b.DaysFrom <= overdue) ?? buckets[^1];
            var half = current.DaysTo is { } to ? (to - current.DaysFrom) / 2 : 30;
            return [new CollectionTranche(start.AddDays(half + extraDays), amount, current.IsUncollectible)];
        }

        var tranches = new List<CollectionTranche>();
        var left = amount;
        var shares = remaining.Where(b => b.Share > 0).ToList();
        for (var i = 0; i < shares.Count; i++)
        {
            var b = shares[i];
            var part = i == shares.Count - 1 ? left : Math.Round(amount * b.Share / total, 2, MidpointRounding.AwayFromZero);
            left -= part;
            tranches.Add(new CollectionTranche(Max(dueDate.AddDays(b.AvgDaysLate + extraDays), start), part, b.IsUncollectible));
        }

        return tranches;
    }

    /// <summary>
    /// Datele unei sume planificate intre from si to (F5 3.5, F5-A11): lunar si trimestrial in ziua aleasa (in lunile mai scurte, ultima zi),
    /// saptamanal la 7 zile de la prima data, anual la aceeasi zi, pana la data de sfarsit.
    /// </summary>
    public static IReadOnlyList<DateOnly> Occurrences(DateOnly firstDate, Recurrence recurrence, int? recurrenceDay, DateOnly? endDate, DateOnly from, DateOnly to)
    {
        var last = endDate is { } e && e < to ? e : to;
        var result = new List<DateOnly>();
        if (recurrence == Recurrence.Once)
        {
            if (firstDate >= from && firstDate <= last)
            {
                result.Add(firstDate);
            }

            return result;
        }

        if (recurrence == Recurrence.Weekly)
        {
            for (var d = firstDate; d <= last; d = d.AddDays(7))
            {
                if (d >= from)
                {
                    result.Add(d);
                }
            }

            return result;
        }

        var step = recurrence switch { Recurrence.Quarterly => 3, Recurrence.Yearly => 12, _ => 1 };
        var day = recurrenceDay ?? firstDate.Day;
        for (var month = new DateOnly(firstDate.Year, firstDate.Month, 1); month <= last; month = month.AddMonths(step))
        {
            var date = new DateOnly(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
            if (date >= firstDate && date >= from && date <= last)
            {
                result.Add(date);
            }
        }

        return result;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static int Middle(DelayShare bucket) => bucket.DaysTo is not { } to ? Math.Max(bucket.DaysFrom, 0)
        : bucket.DaysFrom == int.MinValue ? Math.Min(to, 0) : (bucket.DaysFrom + to) / 2;
}

/// <summary>Granularitatea prognozei (F5 4.4): zi, saptamana (de luni), luna.</summary>
public enum Granularity : byte
{
    Day = 1,
    Week = 2,
    Month = 3,
}

/// <summary>Un interval de intarziere configurat (F5 3.4); DaysFrom null = fara limita inferioara, DaysTo null = fara limita superioara.</summary>
public sealed record DelayBucketDefinition(string Code, string Name, int? DaysFrom, int? DaysTo, bool IsUncollectible);

/// <summary>Perioadele, intervalele si alegerea istoricului in prognoza de cash-flow (F5 3.4, 4.4).</summary>
public static class CashFlowPeriods
{
    /// <summary>Intervalele livrate (F5 3.4): B0 la termen, B1–B4, B5 peste 180 de zile (incert).</summary>
    public static IReadOnlyList<DelayBucketDefinition> DefaultBuckets { get; } =
    [
        new("B0", "La termen sau înainte", null, 0, false),
        new("B1", "1–30 zile", 1, 30, false),
        new("B2", "31–60 zile", 31, 60, false),
        new("B3", "61–90 zile", 61, 90, false),
        new("B4", "91–180 zile", 91, 180, false),
        new("B5", "Peste 180 de zile", 181, null, true),
    ];

    /// <summary>Numarul maxim de perioade pe granularitate (51711): zi 92, saptamana 104, luna 36.</summary>
    public static int MaxPeriods(Granularity granularity) => granularity switch { Granularity.Day => 92, Granularity.Week => 104, _ => 36 };

    /// <summary>Inceputul perioadei care contine ziua (saptamana incepe lunea); prima perioada incepe la from.</summary>
    public static DateOnly PeriodStart(DateOnly day, Granularity granularity, DateOnly from)
    {
        var start = granularity switch
        {
            Granularity.Day => day,
            Granularity.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            _ => new DateOnly(day.Year, day.Month, 1),
        };
        return start < from ? from : start;
    }

    /// <summary>Perioadele intre from si to, fiecare cu prima si ultima zi.</summary>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> Periods(DateOnly from, DateOnly to, Granularity granularity)
    {
        var result = new List<(DateOnly, DateOnly)>();
        var start = from;
        while (start <= to)
        {
            var next = granularity switch
            {
                Granularity.Day => start.AddDays(1),
                Granularity.Week => PeriodStart(start, Granularity.Week, DateOnly.MinValue).AddDays(7),
                _ => new DateOnly(start.Year, start.Month, 1).AddMonths(1),
            };
            var end = next.AddDays(-1);
            result.Add((start, end > to ? to : end));
            start = next;
        }

        return result;
    }

    /// <summary>
    /// Distributia folosita pentru un client (F5 3.4, F5-A15): a lui, de la minSample facturi incasate; altfel a firmei, daca firma are
    /// istoric suficient; altfel niciuna (incasarea la scadenta).
    /// </summary>
    public static IReadOnlyList<DelayShare> Choose(int partnerSample, IReadOnlyList<DelayShare>? partner, int companySample, IReadOnlyList<DelayShare>? company,
        int minSample) =>
        partner is { Count: > 0 } && partnerSample >= minSample ? partner
        : company is { Count: > 0 } && companySample >= minSample ? company
        : [];

    /// <summary>Intervalele acopera toata axa, in ordine, fara suprapuneri si fara goluri (F5 3.4): primul fara inceput, ultimul fara sfarsit.</summary>
    public static bool CoverTheAxis(IReadOnlyList<DelayBucketDefinition> buckets)
    {
        if (buckets.Count == 0 || buckets[0].DaysFrom is not null || buckets[^1].DaysTo is not null)
        {
            return false;
        }

        for (var i = 0; i < buckets.Count - 1; i++)
        {
            if (buckets[i].DaysTo is not { } to || buckets[i + 1].DaysFrom != to + 1 || (buckets[i].DaysFrom is { } f && f > to))
            {
                return false;
            }
        }

        return true;
    }
}
