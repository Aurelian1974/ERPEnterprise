using Erp.Inventory.Domain;

namespace Erp.Inventory.Tests.Domain;

/// <summary>Data unei miscari (02.05, S2 regula 8; decizia 6): limita firmei, dreptul de a trece de ea, fara date din viitor.</summary>
public sealed class MovementRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 28);

    [Theory]
    [Trait("Criteriu", "02.05-S2-2")]
    [InlineData(0, 5, false, true, true)]
    [InlineData(5, 5, false, true, true)]
    [InlineData(6, 5, false, true, false)]
    [InlineData(6, 5, true, true, true)]
    [InlineData(6, 5, true, false, false)]
    [InlineData(1, 0, false, true, false)]
    [InlineData(-1, 5, true, true, false)]
    public void The_movement_date_follows_the_company_limit_and_the_backdate_right(int daysAgo, int limit, bool right, bool withRight, bool allowed) =>
        Assert.Equal(allowed, MovementRules.IsDateAllowed(Today, Today.AddDays(-daysAgo), limit, right, withRight));
}
