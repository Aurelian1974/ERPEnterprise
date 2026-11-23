using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Tests.Domain;

/// <summary>Regulile de alocare ale F3 (4.2): impartirea pe partide in ordine, semnul platilor si al facturilor.</summary>
public sealed class SettlementRulesTests
{
    [Fact]
    public void Distribute_fills_items_in_order_and_leaves_the_surplus_unallocated()
    {
        var plan = SettlementRules.Distribute(1200m, [new AllocationCandidate(1, 500m), new AllocationCandidate(2, 0m), new AllocationCandidate(3, 1000m)]);
        Assert.Equal([(1L, 500m), (3L, 700m)], plan);
        Assert.Equal([(1L, 500m)], SettlementRules.Distribute(800m, [new AllocationCandidate(1, 500m)]));
        Assert.Empty(SettlementRules.Distribute(0m, [new AllocationCandidate(1, 500m)]));
    }

    [Fact]
    public void Signs_follow_the_debit_credit_convention_per_role()
    {
        Assert.Equal((-1m, 1m, 1m, -1m), (SettlementRules.PaymentSign(true, false), SettlementRules.PaymentSign(true, true),
            SettlementRules.PaymentSign(false, false), SettlementRules.PaymentSign(false, true)));
        Assert.Equal((1m, -1m), (SettlementRules.InvoiceSign(true), SettlementRules.InvoiceSign(false)));
        Assert.Equal(OpenItemKind.CreditNote, SettlementRules.InvoiceKind(true, true));
        Assert.Equal(OpenItemKind.AdvanceInvoice, SettlementRules.InvoiceKind(false, true));
    }
}
