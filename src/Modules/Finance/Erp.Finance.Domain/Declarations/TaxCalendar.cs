namespace Erp.Finance.Domain.Declarations;

/// <summary>Un termen din calendarul fiscal (C4 3.7): declaratia, perioada si scadenta (mutata pe ziua lucratoare).</summary>
public sealed record TaxDeadline(string FormCode, DateOnly PeriodFrom, DateOnly PeriodTo, DateOnly DueDate);

/// <summary>
/// Calendarul fiscal al unui an (C4 3.7), ca functie pura: D300 si D394 pe periodicitatea de TVA a firmei, D390 lunar, D100 trimestrial, pana pe
/// 25 a lunii urmatoare perioadei; D406 lunar, pana in ultima zi a lunii urmatoare; D101 pana pe 25 a lunii a treia dupa incheierea anului.
/// Termenele din zile nelucratoare (weekend si sarbatorile legale din Codul muncii, art. 139) se muta pe urmatoarea zi lucratoare.
/// </summary>
public static class TaxCalendar
{
    public static IReadOnlyList<TaxDeadline> ForYear(DateOnly yearStart, DateOnly yearEnd, int vatPeriodicity)
    {
        var list = new List<TaxDeadline>();
        void Add(string form, int months, Func<DateOnly, DateOnly> due)
        {
            for (var from = yearStart; from <= yearEnd; from = from.AddMonths(months))
            {
                var to = from.AddMonths(months).AddDays(-1);
                list.Add(new TaxDeadline(form, from, to, NextWorkingDay(due(to))));
            }
        }

        static DateOnly Day25(DateOnly periodEnd) => new DateOnly(periodEnd.Year, periodEnd.Month, 1).AddMonths(1).AddDays(24);
        Add("D300", vatPeriodicity, Day25);
        Add("D394", vatPeriodicity, Day25);
        Add("D390", 1, Day25);
        Add("D100", 3, Day25);
        Add("D406", 1, to => new DateOnly(to.Year, to.Month, 1).AddMonths(2).AddDays(-1));
        list.Add(new TaxDeadline("D101", yearStart, yearEnd, NextWorkingDay(new DateOnly(yearEnd.Year, yearEnd.Month, 1).AddMonths(3).AddDays(24))));
        return list;
    }

    public static DateOnly NextWorkingDay(DateOnly day)
    {
        while (!IsWorkingDay(day))
        {
            day = day.AddDays(1);
        }

        return day;
    }

    public static bool IsWorkingDay(DateOnly day) =>
        day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !Holidays(day.Year).Contains(day);

    /// <summary>Sarbatorile legale din Romania (Legea 53/2003, art. 139): fixe, plus Vinerea Mare, Pastele si Rusaliile ortodoxe.</summary>
    public static IReadOnlySet<DateOnly> Holidays(int year)
    {
        var easter = OrthodoxEaster(year);
        return new HashSet<DateOnly>
        {
            new(year, 1, 1), new(year, 1, 2), new(year, 1, 6), new(year, 1, 7), new(year, 1, 24), new(year, 5, 1), new(year, 6, 1), new(year, 8, 15),
            new(year, 11, 30), new(year, 12, 1), new(year, 12, 25), new(year, 12, 26),
            easter.AddDays(-2), easter, easter.AddDays(1), easter.AddDays(49), easter.AddDays(50),
        };
    }

    /// <summary>Pastele ortodox (algoritmul Meeus pentru calendarul iulian, mutat in calendarul gregorian).</summary>
    public static DateOnly OrthodoxEaster(int year)
    {
        var a = year % 4;
        var b = year % 7;
        var c = year % 19;
        var d = ((19 * c) + 15) % 30;
        var e = ((2 * a) + (4 * b) - d + 34) % 7;
        var month = (d + e + 114) / 31;
        var day = ((d + e + 114) % 31) + 1;
        return new DateOnly(year, month, day).AddDays(13);
    }
}
