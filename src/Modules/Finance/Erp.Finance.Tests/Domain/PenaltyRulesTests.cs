using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Tests.Domain;

/// <summary>Penalitatile de intarziere pe intervale (02.02, F3 3.10), cu exemplele din macheta f3-08.</summary>
public sealed class PenaltyRulesTests
{
    private static readonly ReferenceRate[] Rates = [new(new DateOnly(2026, 1, 1), 6.75m), new(new DateOnly(2026, 7, 1), 6.50m)];

    private static DateOnly D(int month, int day) => new(2026, month, day);

    [Fact]
    [Trait("Criteriu", "02.02-F3-A11")]
    public void An_invoice_paid_in_two_tranches_is_charged_on_two_intervals_with_different_bases()
    {
        // Scadenta 30.09, rata din contract 0,10%/zi; 4.000 platiti pe 10.10, restul pe 20.10.
        var item = new PenaltyItem(1, D(9, 30), 9800m, [(D(10, 10), 4000m), (D(10, 20), 5800m)], 0, 0.10m, false);

        var lines = PenaltyRules.Calculate(item, D(10, 1), D(10, 31), Rates);

        Assert.Equal([(D(10, 1), D(10, 10), 10, 9800m, 98.00m), (D(10, 11), D(10, 20), 10, 5800m, 58.00m)],
            lines.Select(l => (l.From, l.To, l.Days, l.BaseAmount, l.PenaltyAmount)));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F3-A11")]
    public void The_legal_interest_uses_the_reference_rate_of_each_semester()
    {
        // Fara rata in contract: dobanda legala = rata de referinta + 8 puncte; 6,75% in semestrul I, 6,50% in semestrul II.
        var item = new PenaltyItem(2, D(6, 15), 4100m, [], 0, 0m, false);

        var lines = PenaltyRules.Calculate(item, D(6, 1), D(7, 31), Rates);

        Assert.Equal([(D(6, 16), D(6, 30), 15, 14.75m, 24.85m), (D(7, 1), D(7, 31), 31, 14.50m, 50.49m)],
            lines.Select(l => (l.From, l.To, l.Days, l.AnnualRate!.Value, l.PenaltyAmount)));
    }

    [Fact]
    public void Grace_days_disputed_bases_already_charged_days_and_the_cap_at_principal_limit_the_penalty()
    {
        // Gratie 3 zile: scadenta 25.10 → de la 29.10 (macheta: 4.600 × 0,05% × 3 = 6,90).
        var grace = PenaltyRules.Calculate(new PenaltyItem(3, D(10, 25), 4600m, [], 3, 0.05m, false), D(10, 1), D(10, 31), Rates);
        Assert.Equal((D(10, 29), 3, 6.90m), (grace.Single().From, grace.Single().Days, grace.Single().PenaltyAmount));

        // Zilele deja calculate nu se mai penalizeaza.
        var again = PenaltyRules.Calculate(new PenaltyItem(4, D(9, 20), 21880m, [], 0, 0.10m, false, 678.28m, D(10, 31)), D(10, 1), D(11, 30), Rates);
        Assert.Equal((D(11, 1), 30), (again.Single().From, again.Single().Days));

        // Plafonul la debit: 100 × 1%/zi × 300 de zile ar fi 300; ramane 100 minus cei 20 deja calculati.
        var capped = PenaltyRules.Calculate(new PenaltyItem(5, D(1, 1), 100m, [], 0, 1m, true, 20m), D(1, 2), D(10, 29), Rates);
        Assert.Equal(80m, capped.Sum(l => l.PenaltyAmount));

        // Platita integral inainte de perioada: nimic.
        Assert.Empty(PenaltyRules.Calculate(new PenaltyItem(6, D(8, 1), 500m, [(D(9, 1), 500m)], 0, 0.1m, false), D(10, 1), D(10, 31), Rates));
        Assert.Throws<ReferenceRateMissingException>(() => PenaltyRules.Calculate(new PenaltyItem(7, D(1, 1), 10m, [], 0, 0m, false), D(1, 1), D(1, 31), []));
    }
}
