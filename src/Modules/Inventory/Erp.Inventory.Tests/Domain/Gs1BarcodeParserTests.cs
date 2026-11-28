using Erp.Inventory.Domain.Lots;

namespace Erp.Inventory.Tests.Domain;

/// <summary>Cititorul GS1 (02.05, S4 3.8): forma cu paranteze si forma bruta, cu separatorul GS (ASCII 29) dupa campurile variabile.</summary>
public sealed class Gs1BarcodeParserTests
{
    private readonly Gs1BarcodeParser parser = new();

    [Fact]
    [Trait("Criteriu", "02.05-S4-A12")]
    public void The_human_readable_form_gives_gtin_expiry_and_lot()
    {
        var result = parser.Parse("(01)05941234000016(17)270222(10)L2611-15");

        Assert.Empty(result.Errors);
        Assert.Equal("05941234000016", result.Gtin);
        Assert.Equal(new DateOnly(2027, 2, 22), result.ExpiryDate);
        Assert.Equal("L2611-15", result.LotCode);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A12")]
    public void The_raw_form_splits_variable_fields_on_the_group_separator()
    {
        var result = parser.Parse("0105941234000016" + "10L2611-15\u001d" + "21SN000123\u001d" + "17270200");

        Assert.Empty(result.Errors);
        Assert.Equal("L2611-15", result.LotCode);
        Assert.Equal("SN000123", result.SerialNo);
        Assert.Equal(new DateOnly(2027, 2, 28), result.ExpiryDate);
    }

    [Fact]
    public void An_unknown_identifier_or_a_bad_date_is_reported_not_thrown()
    {
        Assert.NotEmpty(parser.Parse("(17)271340").Errors);
        Assert.NotEmpty(parser.Parse("(99)ABC").Errors);
        Assert.NotEmpty(parser.Parse("").Errors);
    }
}
