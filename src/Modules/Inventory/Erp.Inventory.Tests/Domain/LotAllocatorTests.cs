using Erp.Inventory.Domain.Lots;

namespace Erp.Inventory.Tests.Domain;

/// <summary>Alegerea lotului la iesire (02.05, S4 3.3, 3.5): FEFO, durata minima a clientului, starea lotului, faptele POS.</summary>
public sealed class LotAllocatorTests
{
    private static readonly DateOnly Delivery = new(2026, 11, 26);

    private static LotCandidate Lot(int id, string code, DateOnly? expiry, decimal qty, byte status = 1, bool technical = false) =>
        new(1, null, 7, id, code, status, expiry, 1, new DateOnly(2026, 10, id), technical, qty);

    [Fact]
    [Trait("Criteriu", "02.05-S4-A2")]
    public void Fefo_skips_the_lot_below_the_customer_minimum_and_takes_the_next_ones()
    {
        var lots = new[]
        {
            Lot(1, "L2609-11", new DateOnly(2026, 12, 5), 480),
            Lot(2, "L2610-18", new DateOnly(2027, 1, 10), 2400),
            Lot(3, "L2611-07", new DateOnly(2027, 1, 14), 960),
        };
        var result = LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 3000, null, "ISSUE_SALE", 45)], lots, new Dictionary<int, int>(), Delivery, 1, false);
        Assert.Equal([(2, 2400m), (3, 600m)], result.Select(a => (a.LotId, a.Qty)));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A3")]
    public void A_lot_that_is_not_available_is_not_proposed_and_is_refused_for_a_sale()
    {
        var lots = new[]
        {
            Lot(1, "Q", new DateOnly(2027, 1, 1), 100, status: 2),
            Lot(2, "B", new DateOnly(2027, 1, 2), 100, status: 3),
            Lot(3, "OK", new DateOnly(2027, 2, 1), 100),
            Lot(4, "OLD", new DateOnly(2026, 11, 20), 100),
        };
        Assert.Equal([3], LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 50, null, "ISSUE_SALE")], lots, new Dictionary<int, int>(), Delivery, 1, false)
            .Select(a => a.LotId));
        Assert.Equal(LotAllocator.StatusForbidsIssue, Assert.Throws<LotAllocationException>(() =>
            LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, 1, "ISSUE_SALE")], lots, new Dictionary<int, int>(), Delivery, 1, false)).Code);
        Assert.Equal(LotAllocator.Expired, Assert.Throws<LotAllocationException>(() =>
            LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, 4, "ISSUE_SALE")], lots, new Dictionary<int, int>(), Delivery, 1, false)).Code);
        // Lotul in carantina poate fi returnat furnizorului.
        Assert.Single(LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, 1, "RETURN_TO_SUPPLIER")], lots, new Dictionary<int, int>(), Delivery, 1, false));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A11")]
    public void A_pos_fact_takes_what_the_lots_have_and_the_rest_goes_to_the_unidentified_lot()
    {
        var lots = new[] { Lot(1, "A", new DateOnly(2027, 1, 1), 3) };
        var result = LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, 999, "POS_SALE")], lots, new Dictionary<int, int> { [7] = 50 }, Delivery, 1, true);
        Assert.Equal([(1, 3m), (50, 2m)], result.Select(a => (a.LotId, a.Qty)));
        Assert.Equal(LotAllocator.InsufficientQty, Assert.Throws<LotAllocationException>(() =>
            LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, null, "UNLOAD")], lots, new Dictionary<int, int>(), Delivery, 1, false)).Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-3.5")]
    public void Two_lines_of_the_same_call_share_the_lot_quantities()
    {
        var lots = new[] { Lot(1, "A", new DateOnly(2027, 1, 1), 10), Lot(2, "B", new DateOnly(2027, 2, 1), 10) };
        var result = LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 6, null, "UNLOAD"), new LotDemand(2, 1, null, 7, 6, null, "UNLOAD")], lots,
            new Dictionary<int, int>(), Delivery, 1, false);
        Assert.Equal([(1, 1, 6m), (2, 1, 4m), (2, 2, 2m)], result.Select(a => (a.Row, a.LotId, a.Qty)));
    }
}

/// <summary>Drepturile si motivul la alegerea lotului (02.05, S4 3.2, 3.5, 8).</summary>
public sealed class LotRightsTests
{
    private static readonly DateOnly Delivery = new(2026, 11, 26);

    private static LotCandidate Lot(int id, DateOnly expiry, byte kind = 1) => new(1, null, 7, id, $"L{id}", 1, expiry, kind, new DateOnly(2026, 10, id), false, 100);

    [Fact]
    [Trait("Criteriu", "02.05-S4-3.5")]
    public void Choosing_another_lot_than_the_first_proposal_needs_a_reason_and_a_short_lot_needs_the_right()
    {
        var lots = new[] { Lot(1, new DateOnly(2027, 1, 1)), Lot(2, new DateOnly(2027, 2, 1)), Lot(3, new DateOnly(2026, 12, 1)) };
        Assert.Equal(LotAllocator.OverrideReasonRequired, Assert.Throws<LotAllocationException>(() =>
            LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, 2, "ISSUE_SALE", 30)], lots, new Dictionary<int, int>(), Delivery, 1, false)).Code);
        Assert.Single(LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, 2, "ISSUE_SALE", 30, 1)], lots, new Dictionary<int, int>(), Delivery, 1, false));
        Assert.Equal(LotAllocator.ShelfLifeTooShort, Assert.Throws<LotAllocationException>(() =>
            LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, 3, "ISSUE_SALE", 30, 1)], lots, new Dictionary<int, int>(), Delivery, 1, false)).Code);
        Assert.Single(LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, 3, "ISSUE_SALE", 30, 1)], lots, new Dictionary<int, int>(), Delivery, 1, false,
            new LotRights(false, true)));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-3.3")]
    public void A_best_before_lot_past_its_date_is_sold_only_with_the_right()
    {
        var lots = new[] { Lot(1, new DateOnly(2026, 11, 20), kind: 2) };
        Assert.Equal(LotAllocator.InsufficientQty, Assert.Throws<LotAllocationException>(() =>
            LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, null, "ISSUE_SALE")], lots, new Dictionary<int, int>(), Delivery, 1, false)).Code);
        Assert.Single(LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, null, "ISSUE_SALE")], lots, new Dictionary<int, int>(), Delivery, 1, false,
            new LotRights(true, false)));
        Assert.Single(LotAllocator.Allocate([new LotDemand(1, 1, null, 7, 5, null, "UNLOAD")], lots, new Dictionary<int, int>(), Delivery, 1, false));
    }
}
