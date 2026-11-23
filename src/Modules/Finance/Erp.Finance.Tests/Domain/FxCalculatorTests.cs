using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Tests.Domain;

/// <summary>Diferentele de curs la alocare (02.02, F4 2 regulile 2–4, 3.3, 4.1).</summary>
public sealed class FxCalculatorTests
{
    private static Dictionary<long, FxItem> Items(params FxItem[] items) => items.ToDictionary(i => i.Id);

    [Fact]
    [Trait("Criteriu", "02.02-F4-A1")]
    public void A_customer_invoice_revalued_at_4_97_and_collected_at_4_99_realizes_a_gain_of_20_lei()
    {
        // Factura 1.000 EUR la 4,95, reevaluata la 4,97 (valoare contabila 4.970); incasarea 1.000 EUR la 4,99 (4.990).
        var invoice = new FxItem(1, PartnerRole.Customer, OpenItemKind.Invoice, "EUR", 4.95m, 1000m, 4970m);
        var payment = new FxItem(2, PartnerRole.Customer, OpenItemKind.Payment, "EUR", 4.99m, -1000m, -4990m);

        var row = Assert.Single(FxCalculator.Plan(Items(invoice, payment), [new FxRequest(1, 2, 1000m)]));

        Assert.Equal((4970m, 4990m, 20m, 1L), (row.AmountRonDebit, row.AmountRonCredit, row.DifferenceRon, row.FxOpenItemId!.Value));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F4-A2")]
    public void The_same_case_at_a_supplier_is_a_loss_of_20_lei()
    {
        // Datoria 1.000 EUR reevaluata la 4,97 (−4.970); plata 1.000 EUR la 4,99 costa 4.990: pierdere 20.
        var invoice = new FxItem(1, PartnerRole.Supplier, OpenItemKind.Invoice, "EUR", 4.95m, -1000m, -4970m);
        var payment = new FxItem(2, PartnerRole.Supplier, OpenItemKind.Payment, "EUR", 4.99m, 1000m, 4990m);

        var row = Assert.Single(FxCalculator.Plan(Items(invoice, payment), [new FxRequest(2, 1, 1000m)]));

        Assert.Equal((-20m, 1L), (row.DifferenceRon, row.FxOpenItemId!.Value));
    }

    [Fact]
    public void Partial_payments_share_the_carrying_value_and_the_closing_one_takes_the_rest()
    {
        var invoice = new FxItem(1, PartnerRole.Customer, OpenItemKind.Invoice, "EUR", 4.9537m, 1000m, 4953.70m);
        var first = new FxItem(2, PartnerRole.Customer, OpenItemKind.Payment, "EUR", 5m, -333.33m, -1666.65m);
        var second = new FxItem(3, PartnerRole.Customer, OpenItemKind.Payment, "EUR", 5m, -666.67m, -3333.35m);

        var rows = FxCalculator.Plan(Items(invoice, first, second), [new FxRequest(1, 2, 333.33m), new FxRequest(1, 3, 666.67m)]);

        Assert.Equal(1651.22m, rows[0].AmountRonDebit);
        Assert.Equal(4953.70m - 1651.22m, rows[1].AmountRonDebit);
        Assert.Equal(5000m - 4953.70m, rows.Sum(r => r.DifferenceRon));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F4-A4")]
    public void An_eur_invoice_paid_in_lei_at_bnr_plus_1_percent_settles_the_eur_amount_and_records_the_difference()
    {
        // BNR 5,00 in ziua platii, +1% = 5,05; 5.050 RON stinge 1.000 EUR (valoare contabila 4.950): castig 100.
        var rate = FxCalculator.CrossRate(CrossRateRule.BnrOnPaymentDatePlusMarkup, 5m, 1m, 1m, 4.95m, 1m);
        var invoice = new FxItem(1, PartnerRole.Customer, OpenItemKind.Invoice, "EUR", 4.95m, 1000m, 4950m);
        var payment = new FxItem(2, PartnerRole.Customer, OpenItemKind.Payment, "RON", 1m, -6000m, -6000m);

        var row = Assert.Single(FxCalculator.Plan(Items(invoice, payment), [new FxRequest(1, 2, 6000m, rate, CrossRateRule.BnrOnPaymentDatePlusMarkup)]));

        Assert.Equal(5.05m, rate);
        // Plata e mai mare decat factura: se stinge restul facturii, iar surplusul in lei ramane plata nealocata.
        Assert.Equal((1000m, 5050m, 4950m, 5050m, 100m), (row.DebitAmount, row.CreditAmount, row.AmountRonDebit, row.AmountRonCredit, row.DifferenceRon));
    }

    [Fact]
    public void Lei_on_both_sides_never_make_a_difference_and_the_invoice_rate_rule_uses_the_invoice_rate()
    {
        var a = new FxItem(1, PartnerRole.Customer, OpenItemKind.Invoice, "RON", 1m, 100m, 100m);
        var b = new FxItem(2, PartnerRole.Customer, OpenItemKind.Payment, "RON", 1m, -100m, -100m);
        Assert.Equal(0m, Assert.Single(FxCalculator.Plan(Items(a, b), [new FxRequest(1, 2, 40m)])).DifferenceRon);
        Assert.Equal(4.95m, FxCalculator.CrossRate(CrossRateRule.InvoiceRate, 5m, 1m, 0m, 4.95m, 1m));
        // EUR platit in USD: cursul din cursurile in lei ale ambelor monede.
        Assert.Equal(1.086957m, FxCalculator.CrossRate(CrossRateRule.BnrOnPaymentDate, 5m, 4.6m, 0m, 4.95m, 4.6m));
    }
}
