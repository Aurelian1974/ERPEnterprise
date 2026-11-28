using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;

namespace Erp.Inventory.Tests.Domain;

/// <summary>Regulile pure ale S1: precedenta politicii de stoc (S1 3.4, 3.6) si suprapunerea titularilor (S1 3.2).</summary>
public sealed class StockPolicyRulesTests
{
    private static readonly WarehousePolicyLayer Depot = new(10, WarehouseKind.Depot, NegativeStockPolicy.AllowedWithWarning, ValuationMethod.WeightedAverage);

    [Fact]
    [Trait("Criteriu", "02.05-S1-A6")]
    public void Two_holders_with_overlapping_periods_are_refused_and_substitutes_or_continuous_periods_are_allowed()
    {
        var existing = new[] { new KeeperPeriod(1, 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)), new KeeperPeriod(2, 2, new DateOnly(2026, 1, 1), null) };

        Assert.True(KeeperRules.HolderOverlaps(new KeeperPeriod(null, 1, new DateOnly(2026, 6, 30), null), existing));
        Assert.True(KeeperRules.HolderOverlaps(new KeeperPeriod(null, 1, new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 1)), existing));
        Assert.False(KeeperRules.HolderOverlaps(new KeeperPeriod(null, 1, new DateOnly(2026, 7, 1), null), existing));
        Assert.False(KeeperRules.HolderOverlaps(new KeeperPeriod(null, 2, new DateOnly(2026, 3, 1), null), existing));
        // Modificarea aceluiasi titular nu se compara cu el insusi.
        Assert.False(KeeperRules.HolderOverlaps(new KeeperPeriod(1, 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 31)), existing));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A8")]
    public void The_effective_policy_follows_variant_then_item_in_warehouse_then_company_for_random_layers()
    {
        // Test de proprietate: straturi generate aleator (seed fix, reproductibil).
        var random = new Random(20261128);
        for (var i = 0; i < 2000; i++)
        {
            var company = random.Next(4) == 0 ? null : new CompanyItemLayer((ValuationMethod)random.Next(1, 5), (TrackingMode)random.Next(0, 5),
                Qty(random), Qty(random), (ReplenishmentMethod)random.Next(0, 3), random.Next(5) == 0);
            var item = random.Next(2) == 0 ? null : Layer(random);
            var variant = random.Next(2) == 0 ? null : Layer(random);
            var warehouse = Depot with { Kind = (WarehouseKind)random.Next(1, 6) };

            var policy = StockPolicyRules.Resolve(warehouse, 5, 7, company, item, variant);

            var expected = variant ?? item;
            Assert.Equal(expected is null ? company?.MinQty : expected.MinQty, policy.MinQty);
            Assert.Equal(expected is null ? company?.MaxQty : expected.MaxQty, policy.MaxQty);
            Assert.Equal(expected?.ReorderQty, policy.ReorderQty);
            Assert.Equal(expected?.IsBlockedForReceipt ?? false, policy.IsBlockedForReceipt);
            Assert.Equal((expected?.IsBlockedForIssue ?? false) || (company?.IsBlockedForIssue ?? false), policy.IsBlockedForIssue);
            Assert.Equal(expected?.Replenishment ?? company?.Replenishment ?? ReplenishmentMethod.None, policy.Replenishment);
            Assert.Equal(company?.Method ?? ValuationMethod.WeightedAverage, policy.Method);
            // Loturile si seriile, tranzitul si custodia nu pot avea niciodata stoc negativ.
            if (policy.Tracking != TrackingMode.None || warehouse.Kind is WarehouseKind.Transit or WarehouseKind.Custody)
            {
                Assert.Equal(NegativeStockPolicy.Blocked, policy.NegativeStock);
            }
            else
            {
                Assert.Equal(warehouse.NegativeStock, policy.NegativeStock);
            }
        }
    }

    private static decimal? Qty(Random random) => random.Next(3) == 0 ? null : random.Next(0, 1000);

    private static WarehouseItemLayer Layer(Random random) => new(Qty(random), Qty(random), random.Next(2) == 0 ? null : random.Next(1, 50),
        random.Next(3) == 0 ? null : (ReplenishmentMethod)random.Next(0, 3), random.Next(4) == 0, random.Next(4) == 0);
}
