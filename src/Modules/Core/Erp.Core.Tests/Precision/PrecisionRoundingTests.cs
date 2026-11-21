using Erp.Core.Application.Precision;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Precision;

/// <summary>A10: rotunjirea respecta politica — pret cu pana la 6 zecimale, valoare si TVA la 2, inclusiv energie (macheta 07d).</summary>
public sealed class PrecisionRoundingTests
{
    private static PrecisionInfo P(byte price, byte quantity, MidpointRounding rounding = MidpointRounding.AwayFromZero) => new(price, quantity, 2, rounding);

    [Theory]
    [Trait("Criteriu", "02.01-A10")]
    [InlineData(2, "40.14")]
    [InlineData(4, "40.12")]
    [InlineData(6, "40.12")]
    public void Line_amount_rounds_price_and_quantity_first_then_the_value_to_two_decimals(byte priceDecimals, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PrecisionRounding.LineAmount(3.25m, 12.345678m, P(priceDecimals, 3)));
    }

    [Fact]
    [Trait("Criteriu", "02.01-A10")]
    public void Energy_prices_keep_six_decimals_and_vat_is_rounded_half_away_from_zero()
    {
        var energy = P(6, 3);
        Assert.Equal(0.812345m, PrecisionRounding.Price(0.8123454m, energy));
        Assert.Equal(1234.568m, PrecisionRounding.Quantity(1234.5675m, energy));
        var value = PrecisionRounding.LineAmount(1234.568m, 0.812345m, energy);
        Assert.Equal(1002.90m, value); // 1234,568 × 0,812345 = 1002,895141
        Assert.Equal(210.61m, PrecisionRounding.Amount(value * 0.21m, energy));
        Assert.Equal(0.13m, PrecisionRounding.Amount(0.125m, energy));
        Assert.Equal(0.12m, PrecisionRounding.Amount(0.125m, P(6, 3, MidpointRounding.ToEven)));
        Assert.Equal(-0.13m, PrecisionRounding.Amount(-0.125m, energy));
    }

    [Fact]
    public void Only_dotnet_midpoint_modes_are_valid()
    {
        Assert.True(RoundingModes.IsValid(1));
        Assert.False(RoundingModes.IsValid(9));
    }
}
