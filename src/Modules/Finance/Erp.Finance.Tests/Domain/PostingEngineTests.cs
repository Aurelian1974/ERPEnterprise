using Erp.Finance.Contracts.Accounting;
using Erp.Finance.Domain.Accounting;

namespace Erp.Finance.Tests.Domain;

/// <summary>Motorul de contare (02.02, C2 3.2–3.4, 4.1): varianta, lantul cheilor, gruparea si rotunjirea.</summary>
public sealed class PostingEngineTests
{
    private static readonly DateOnly Day = new(2026, 11, 13);

    private static readonly PostingModelDef Sales = new("FacturaEmisa", 1,
    [
        new(10, 10, "TVA la încasare", ["VatOnCollection"], [],
            [new(1, "Total", 1, "PartnerReceivable", CopyDimensions: Dimensions.Partner), new(2, "Net", 2, "Revenue", "707", CopyDimensions: Dimensions.CostCenter),
                new(3, "Vat", 2, "VatPending", "4428", CopyDimensions: 0)], Day.AddYears(-5)),
        new(90, 90, "Standard", [], [],
            [new(1, "Total", 1, "PartnerReceivable", CopyDimensions: Dimensions.Partner), new(2, "Net", 2, "Revenue", "707", CopyDimensions: Dimensions.CostCenter),
                new(3, "Vat", 2, "VatCollected", "4427", CopyDimensions: 0)], Day.AddYears(-5)),
    ]);

    private static ResolutionContext Context(decimal maxRounding = 0.05m) => new(
        (role, kind, _) => (role, kind) switch
        {
            ("CustomerReceivable", 1) => "4111.01",
            ("CustomerReceivable", 2) => "4111.02",
            ("SupplierPayable", 1) => "401.01",
            ("SupplierPayable", 2) => "401.02",
            ("VatCollected", _) => "4427",
            ("VatPending", _) => "4428",
            ("RoundingGain", _) => "7588",
            ("RoundingLoss", _) => "6588",
            _ => null,
        },
        new Dictionary<int, string> { [5] = "5311" }, new Dictionary<string, string> { ["INC-CLIENT"] = "4111.01" }, new Dictionary<string, string>(),
        partner => partner == 1187 ? (byte)1 : partner == 2200 ? (byte)2 : null, _ => 0, maxRounding);

    private static AccountingPayload Invoice(params string[] flags) => new("SalesInvoice", "FV EXD 10241", Day, Day, "RON", 1m, flags,
    [
        new("Net", 12_400m, 12_400m) { CostCenterId = 3, VatRateCode = "S21", VatCategory = "S" },
        new("Vat", 2_604m, 2_604m) { VatRateCode = "S21", VatCategory = "S" },
        new("Total", 15_004m, 15_004m),
    ]) { PartnerId = 1187 };

    [Fact]
    [Trait("Criteriu", "02.02-C2-A1")]
    public void A_sales_invoice_posts_receivable_against_revenue_and_collected_vat_with_partner_and_cost_center()
    {
        var result = PostingEngine.Build(Sales, Invoice(), Context());
        Assert.True(result.Succeeded, result.Details);
        Assert.Equal(90, result.VariantId);
        Assert.Equal([("4111.01", (byte)1, 15_004m, (int?)1187, (int?)null), ("707", (byte)2, 12_400m, null, 3), ("4427", (byte)2, 2_604m, null, null)],
            result.Lines.Select(l => (l.AccountCode, l.Side, l.AmountRon, l.PartnerId, l.CostCenterId)));
        Assert.Equal([ResolvedFrom.Role, ResolvedFrom.Default, ResolvedFrom.Role], result.Lines.Select(l => l.ResolvedFrom));
    }

    [Fact]
    [Trait("Criteriu", "02.02-C2-A2")]
    public void Vat_on_collection_uses_the_pending_vat_account()
    {
        var result = PostingEngine.Build(Sales, Invoice("VatOnCollection"), Context());
        Assert.Equal(10, result.VariantId);
        Assert.Contains(result.Lines, l => l is { AccountCode: "4428", Side: 2, AmountRon: 2_604m });
    }

    [Fact]
    [Trait("Criteriu", "02.02-C2-A3")]
    public void The_partner_kind_picks_the_analytic_of_the_role_without_an_account_on_the_partner()
    {
        var purchase = new PostingModelDef("FacturaFurnizorValidata", 2,
        [
            new(1, 90, "Standard", [], [], [new(1, "Net", 1, "Expense", "628"), new(2, "Total", 2, "PartnerPayable")], Day.AddYears(-5)),
        ]);
        AccountingPayload Bill(int partner) => new("SupplierInvoice", "FF 1", Day, Day, "RON", 1m, [], [new("Net", 100m, 100m), new("Total", 100m, 100m)])
        {
            PartnerId = partner,
        };

        Assert.Equal("401.02", PostingEngine.Build(purchase, Bill(2200), Context()).Lines.Single(l => l.Side == 2).AccountCode);
        Assert.Equal("401.01", PostingEngine.Build(purchase, Bill(1187), Context()).Lines.Single(l => l.Side == 2).AccountCode);
    }

    [Fact]
    public void Rounding_under_the_threshold_goes_to_the_rounding_role_and_over_it_or_without_an_account_the_event_waits()
    {
        var uneven = Invoice() with
        {
            Components = [new("Net", 100m, 100m) { CostCenterId = 3 }, new("Vat", 21m, 21m), new("Total", 121.03m, 121.03m)],
        };
        Assert.Contains(PostingEngine.Build(Sales, uneven, Context()).Lines, l => l is { AccountCode: "7588", Side: 2, AmountRon: 0.03m });
        Assert.Equal(PostingFailureReason.Unbalanced, PostingEngine.Build(Sales, uneven, Context(0.01m)).Failure);

        var unknown = Invoice() with { PartnerId = 999 };
        var missing = PostingEngine.Build(Sales, unknown, Context());
        Assert.Equal(PostingFailureReason.AccountNotFound, missing.Failure);
        Assert.Contains("rolul CustomerReceivable", missing.Details, StringComparison.Ordinal);
        Assert.Equal(PostingFailureReason.ModelMissing, PostingEngine.Build(null, Invoice(), Context()).Failure);
    }
}
