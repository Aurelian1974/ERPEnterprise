using Erp.Inventory.Domain.Costing;

namespace Erp.Inventory.Tests.Domain;

/// <summary>Repartizarea costurilor adaugate (02.05, S3 3.3): proportional cu baza, restul de rotunjire pe linia cea mai mare, manual exact.</summary>
public sealed class LandedCostAllocatorTests
{
    private static AllocationTarget Target(decimal value, decimal qty, decimal? weight = null, decimal? volume = null, decimal? manual = null) =>
        new(value, qty, weight, volume, manual);

    [Fact]
    [Trait("Criteriu", "02.05-S3-3.3")]
    public void Value_allocation_sums_to_the_amount_and_puts_the_rounding_on_the_largest_line()
    {
        var result = LandedCostAllocator.Allocate(100m, AllocationBasisKind.Value, [Target(10, 1), Target(10, 1), Target(20, 1)]);
        Assert.Equal([25m, 25m, 50m], result);
        var thirds = LandedCostAllocator.Allocate(100m, AllocationBasisKind.Quantity, [Target(1, 1), Target(1, 1), Target(1, 1)]);
        Assert.Equal(100m, thirds.Sum());
        Assert.Equal([33.34m, 33.33m, 33.33m], thirds);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-3.3")]
    public void Weight_allocation_needs_the_weight_of_every_item()
    {
        Assert.Equal([30m, 70m], LandedCostAllocator.Allocate(100m, AllocationBasisKind.Weight, [Target(1, 1, 3), Target(1, 1, 7)]));
        var refused = Assert.Throws<AllocationException>(() => LandedCostAllocator.Allocate(100m, AllocationBasisKind.Weight, [Target(1, 1, 3), Target(1, 1)]));
        Assert.Equal(LandedCostAllocator.BasisMissing, refused.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-3.3")]
    public void Manual_amounts_must_add_up_to_the_document()
    {
        Assert.Equal([-40m, -60m], LandedCostAllocator.Allocate(-100m, AllocationBasisKind.Manual, [Target(1, 1, manual: -40), Target(1, 1, manual: -60)]));
        var refused = Assert.Throws<AllocationException>(() =>
            LandedCostAllocator.Allocate(100m, AllocationBasisKind.Manual, [Target(1, 1, manual: 40), Target(1, 1, manual: 50)]));
        Assert.Equal(LandedCostAllocator.ManualMismatch, refused.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A6")]
    public void An_added_amount_on_a_partly_sold_receipt_splits_between_stock_and_the_issues()
    {
        // Refacerea cheii cu valoarea receptiei marita: FIFO si CMP dau aceeasi impartire, 60% stoc, 40% iesire.
        foreach (var method in new[] { CostMethod.Fifo, CostMethod.Average })
        {
            var key = new CostKeyState { Method = method };
            CostEngine.Apply(key, new CostLine(1, 100, new DateOnly(2026, 11, 2), 1, null, InboundAmount: 5000m + 1840m));
            var issue = CostEngine.Apply(key, new CostLine(2, -40, new DateOnly(2026, 11, 3), 2, null));
            Assert.Equal(-2736m, issue.Amount);
            Assert.Equal(4104m, key.Value);
        }
    }
}
