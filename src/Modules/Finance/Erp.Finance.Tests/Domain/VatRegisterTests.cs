using Erp.Finance.Contracts.Accounting;
using Erp.Finance.Domain.Declarations;

namespace Erp.Finance.Tests.Domain;

/// <summary>Registrul de TVA (02.02, C4 3.1, 3.2): cota si tipul de operatiune pentru livrari si achizitii.</summary>
public sealed class VatRegisterTests
{
    private static readonly VatRateInfo[] Rates =
    [
        new("S21", 21, "S", new(2025, 8, 1), null), new("R11", 11, "S", new(2025, 8, 1), null), new("S19", 19, "S", new(2017, 1, 1), new(2025, 7, 31)),
        new("Z0", 0, "Z", new(2017, 1, 1), null), new("E0", 0, "E", new(2017, 1, 1), null), new("AE", 0, "AE", new(2017, 1, 1), null),
        new("K0", 0, "K", new(2017, 1, 1), null), new("G0", 0, "G", new(2017, 1, 1), null),
    ];

    private static readonly DateOnly Day = new(2026, 10, 15);

    private static AccountingComponent C(string component, decimal amount, string? rate = null, string? itemType = null) =>
        new(component, amount, amount) { VatRateCode = rate, ItemType = itemType };

    [Fact]
    public void A_totals_invoice_gets_the_nearest_standard_rate_and_lines_with_rates_are_grouped()
    {
        Assert.Equal([new VatRow("L-S21", "S21", 21, 1000, 210)], VatRegister.Build(1, [], 1, [C("Total", 1210), C("Net", 1000), C("Vat", 210)], Day, Rates));
        Assert.Equal([new VatRow("A-S19", "S19", 19, 100, 19)], VatRegister.Build(2, [], 1, [C("Net", 100), C("Vat", 19)], new DateOnly(2025, 3, 1), Rates));
        var lines = VatRegister.Build(1, [], 1, [C("Net", 1000, "S21"), C("Vat", 210, "S21"), C("Net", 500, "R11"), C("Vat", 55, "R11"), C("Net", 300, "S21"), C("Vat", 63, "S21")], Day, Rates);
        Assert.Equal([new VatRow("L-S21", "S21", 21, 1300, 273), new VatRow("L-S11", "R11", 11, 500, 55)], lines);
    }

    [Fact]
    [Trait("Criteriu", "02.02-C4-A4")]
    public void Intra_community_export_and_reverse_charge_operations_are_recognised()
    {
        Assert.Equal("L-IC", VatRegister.Build(1, [], 2, [C("Net", 1000)], Day, Rates).Single().OperationTypeCode);
        Assert.Equal("L-EXP", VatRegister.Build(1, [], 3, [C("Net", 1000)], Day, Rates).Single().OperationTypeCode);
        Assert.Equal("A-IC-S", VatRegister.Build(2, ["ReverseCharge"], 2, [C("Net", 1000, itemType: "Service"), C("Vat", 210)], Day, Rates).Single().OperationTypeCode);
        Assert.Equal("A-IC-B", VatRegister.Build(2, ["ReverseCharge"], 2, [C("Net", 1000), C("Vat", 210)], Day, Rates).Single().OperationTypeCode);
        Assert.Equal("A-AE", VatRegister.Build(2, ["ReverseCharge"], 1, [C("Net", 1000), C("Vat", 210)], Day, Rates).Single().OperationTypeCode);
        Assert.Equal("A-IMP", VatRegister.Build(2, [], 3, [C("Net", 1000), C("Vat", 210)], Day, Rates).Single().OperationTypeCode);
        Assert.Equal("A-NOD", VatRegister.Build(2, [], 1, [C("Net", 50)], Day, Rates).Single().OperationTypeCode);
    }
}
