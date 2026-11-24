using Erp.Finance.Domain.Periods;

namespace Erp.Finance.Tests.Domain;

/// <summary>Rezerva legala (02.02, C3 3.6): 5% din profitul brut, pana la plafonul de 20% din capitalul social.</summary>
public sealed class LegalReserveTests
{
    [Fact]
    [Trait("Criteriu", "02.02-C3-A10")]
    public void The_proposed_reserve_respects_the_20_percent_cap()
    {
        Assert.Equal(5_000m, LegalReserve.Propose(100_000m, 200_000m, 0m));
        Assert.Equal(1_000m, LegalReserve.Propose(100_000m, 200_000m, 39_000m));
        Assert.Equal(0m, LegalReserve.Propose(100_000m, 200_000m, 40_000m));
        Assert.Equal(0m, LegalReserve.Propose(-10_000m, 200_000m, 0m));
        Assert.Equal(12.35m, LegalReserve.Propose(247m, 200_000m, 0m));
        Assert.True(LegalReserve.WithinCap(1_000m, 200_000m, 39_000m));
        Assert.False(LegalReserve.WithinCap(1_000.01m, 200_000m, 39_000m));
    }
}
