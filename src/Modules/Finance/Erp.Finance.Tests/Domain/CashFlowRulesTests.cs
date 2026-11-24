using Erp.Finance.Domain.Planning;

namespace Erp.Finance.Tests.Domain;

/// <summary>Prognoza de cash-flow (02.02, F5 3.4, 3.5): transele incasarilor pe intervale de intarziere si desfasurarea sumelor planificate.</summary>
public sealed class CashFlowRulesTests
{
    private static readonly DelayShare[] History =
    [
        new(int.MinValue, 0, 0.6m, -2, false),
        new(1, 30, 0.3m, 18, false),
        new(31, 60, 0.1m, 45, false),
        new(61, 90, 0m, 0, false),
        new(91, 180, 0m, 0, false),
        new(181, null, 0m, 0, true),
    ];

    [Fact]
    [Trait("Criteriu", "02.02-F5-A10")]
    public void An_invoice_is_split_on_the_customer_delay_history_and_an_overdue_one_uses_only_the_later_intervals()
    {
        var due = new DateOnly(2026, 12, 1);
        var tranches = CashFlowRules.Collections(10_000m, due, new DateOnly(2026, 11, 24), History);
        Assert.Equal([(new DateOnly(2026, 11, 29), 6000m), (new DateOnly(2026, 12, 19), 3000m), (new DateOnly(2027, 1, 15), 1000m)],
            tranches.Select(t => (t.Date, t.Amount)));

        // Restanta de 40 de zile: doar intervalul 31–60 (in medie 45 de zile), cu toata suma.
        var overdue = CashFlowRules.Collections(10_000m, new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 24), History);
        Assert.Equal([(new DateOnly(2026, 11, 29), 10_000m)], overdue.Select(t => (t.Date, t.Amount)));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F5-A15")]
    public void Without_history_the_collection_is_at_the_due_date_and_beyond_the_history_it_is_half_an_interval_later()
    {
        var due = new DateOnly(2026, 12, 1);
        Assert.Equal([(due, 500m)], CashFlowRules.Collections(500m, due, new DateOnly(2026, 11, 24), []).Select(t => (t.Date, t.Amount)));
        // Restanta de 70 de zile, istoric doar pana la 60: ziua de pornire + jumatatea intervalului 61–90.
        var late = CashFlowRules.Collections(500m, new DateOnly(2026, 9, 15), new DateOnly(2026, 11, 24), History);
        Assert.Equal([(new DateOnly(2026, 12, 8), 500m)], late.Select(t => (t.Date, t.Amount)));

        // Sub 5 facturi incasate: distributia firmei; fara istoric suficient nici la firma: niciuna (la scadenta).
        DelayShare[] company = [new(int.MinValue, 0, 1m, 0, false)];
        Assert.Same(company, CashFlowPeriods.Choose(4, History, 30, company, 5));
        Assert.Same(History, CashFlowPeriods.Choose(5, History, 30, company, 5));
        Assert.Empty(CashFlowPeriods.Choose(4, History, 3, company, 5));
    }

    [Fact]
    public void Periods_start_on_monday_or_the_first_of_the_month_and_buckets_must_cover_the_axis()
    {
        var weeks = CashFlowPeriods.Periods(new DateOnly(2026, 11, 26), new DateOnly(2026, 12, 9), Granularity.Week);
        Assert.Equal([(new DateOnly(2026, 11, 26), new DateOnly(2026, 11, 29)), (new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 6)),
            (new DateOnly(2026, 12, 7), new DateOnly(2026, 12, 9))], weeks);
        Assert.Equal(new DateOnly(2026, 12, 1), CashFlowPeriods.PeriodStart(new DateOnly(2026, 12, 20), Granularity.Month, new DateOnly(2026, 11, 26)));
        Assert.True(CashFlowPeriods.CoverTheAxis(CashFlowPeriods.DefaultBuckets));
        Assert.False(CashFlowPeriods.CoverTheAxis([new("A", "a", null, 10, false), new("B", "b", 12, null, false)]));
    }

    [Fact]
    public void The_prudent_scenario_moves_each_share_one_interval_later_and_drops_overdue_invoices()
    {
        var tranches = CashFlowRules.Collections(10_000m, new DateOnly(2026, 12, 1), new DateOnly(2026, 11, 24), History, prudent: true);
        Assert.Equal([(new DateOnly(2026, 12, 19), 6000m), (new DateOnly(2027, 1, 15), 3000m), (new DateOnly(2027, 2, 14), 1000m)],
            tranches.Select(t => (t.Date, t.Amount)));
        Assert.Empty(CashFlowRules.Collections(10_000m, new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 24), History, prudent: true));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F5-A11")]
    public void A_monthly_amount_on_the_31st_falls_on_the_last_day_of_shorter_months_for_twelve_months()
    {
        var dates = CashFlowRules.Occurrences(new DateOnly(2026, 1, 31), Recurrence.Monthly, 31, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Assert.Equal(12, dates.Count);
        Assert.Equal([new DateOnly(2026, 2, 28), new DateOnly(2026, 4, 30), new DateOnly(2026, 6, 30)], [dates[1], dates[3], dates[5]]);
        Assert.Equal(4, CashFlowRules.Occurrences(new DateOnly(2026, 1, 10), Recurrence.Quarterly, 10, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Count);
        Assert.Equal(3, CashFlowRules.Occurrences(new DateOnly(2026, 1, 5), Recurrence.Weekly, null, new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 1)).Count);
        Assert.Single(CashFlowRules.Occurrences(new DateOnly(2026, 3, 1), Recurrence.Once, null, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
    }
}
