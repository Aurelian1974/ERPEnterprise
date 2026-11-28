using Erp.Inventory.Domain.Lots;

namespace Erp.Inventory.Tests.Domain;

/// <summary>Identificatorii bucatii (02.05, S4 3.4): IMEI cu cifra de control Luhn, VIN fara I, O, Q, MAC dupa masca.</summary>
public sealed class SerialIdentifierRulesTests
{
    private const string ImeiMask = "^[0-9]{15}$";

    [Theory]
    [Trait("Criteriu", "02.05-S4-A14")]
    [InlineData("490154203237518", true)]
    [InlineData("490154203237519", false)]
    [InlineData("49015420323751", false)]
    public void Imei_needs_fifteen_digits_and_a_valid_luhn_check_digit(string imei, bool valid) =>
        Assert.Equal(valid, SerialIdentifierRules.IsValid(imei, ImeiMask, 1));

    [Fact]
    [Trait("Criteriu", "02.05-S4-3.4")]
    public void Vin_and_mac_follow_their_masks()
    {
        Assert.True(SerialIdentifierRules.IsValid("1HGCM82633A004352", "^[A-HJ-NPR-Z0-9]{17}$", 2));
        Assert.False(SerialIdentifierRules.IsValid("1HGCM82633A00435I", "^[A-HJ-NPR-Z0-9]{17}$", 2));
        Assert.True(SerialIdentifierRules.IsValid("00:1A:2b:3C:4d:5E", "^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$", 0));
        Assert.False(SerialIdentifierRules.IsValid("00:1A:2b:3C:4d", "^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$", 0));
    }
}
