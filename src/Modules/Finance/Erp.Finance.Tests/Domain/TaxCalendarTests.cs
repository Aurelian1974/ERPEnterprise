using Erp.Finance.Domain.Declarations;

namespace Erp.Finance.Tests.Domain;

/// <summary>Calendarul fiscal (02.02, C4 3.7): termenele pe periodicitate, mutate pe ziua lucratoare, cu sarbatorile legale.</summary>
public sealed class TaxCalendarTests
{
    [Fact]
    [Trait("Criteriu", "02.02-C4-A10")]
    public void Deadlines_follow_the_vat_periodicity_and_move_to_the_next_working_day()
    {
        Assert.Equal(new DateOnly(2026, 4, 12), TaxCalendar.OrthodoxEaster(2026));
        Assert.Equal(new DateOnly(2025, 4, 20), TaxCalendar.OrthodoxEaster(2025));
        var monthly = TaxCalendar.ForYear(new(2026, 1, 1), new(2026, 12, 31), 1);
        Assert.Equal(12, monthly.Count(d => d.FormCode == "D300"));
        // Octombrie 2026: 25 noiembrie e miercuri; septembrie: 25 octombrie e duminica, deci luni 26.
        Assert.Equal(new DateOnly(2026, 11, 25), monthly.Single(d => d.FormCode == "D300" && d.PeriodFrom.Month == 10).DueDate);
        Assert.Equal(new DateOnly(2026, 10, 26), monthly.Single(d => d.FormCode == "D300" && d.PeriodFrom.Month == 9).DueDate);
        // D406 pe octombrie: 30 noiembrie (Sf. Andrei) si 1 decembrie sunt sarbatori, deci 2 decembrie.
        Assert.Equal(new DateOnly(2026, 12, 2), monthly.Single(d => d.FormCode == "D406" && d.PeriodFrom.Month == 10).DueDate);
        var quarterly = TaxCalendar.ForYear(new(2026, 1, 1), new(2026, 12, 31), 3);
        Assert.Equal(4, quarterly.Count(d => d.FormCode == "D300"));
        Assert.Equal(new DateOnly(2027, 3, 25), quarterly.Single(d => d.FormCode == "D101").DueDate);
        Assert.False(TaxCalendar.IsWorkingDay(new DateOnly(2026, 12, 1)));
    }
}
