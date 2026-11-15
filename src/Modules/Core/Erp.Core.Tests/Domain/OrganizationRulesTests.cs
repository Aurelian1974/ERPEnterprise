using Erp.Core.Domain;

namespace Erp.Core.Tests.Domain;

public sealed class OrganizationRulesTests
{
    [Theory]
    [InlineData("14080700", true)]       // ANAF
    [InlineData("RO14080700", true)]
    [InlineData("ro 2816464", true)]     // prefixul si spatiile se ignora
    [InlineData("14080701", false)]      // cifra de control gresita
    [InlineData("1", false)]
    [InlineData("12345678901", false)]
    [InlineData("1408070A", false)]
    public void Fiscal_code_control_digit_uses_the_anaf_key(string value, bool valid) =>
        Assert.Equal(valid, RomanianFiscalCode.IsValid(value));

    [Fact]
    public void Fiscal_code_is_stored_without_prefix() =>
        Assert.Equal("14080700", RomanianFiscalCode.Normalize(" RO 14080700 "));

    [Theory]
    [InlineData("RO49AAAA1B31007593840000", true)]
    [InlineData("ro49 aaaa 1b31 0075 9384 0000", true)]
    [InlineData("RO49AAAA1B31007593840001", false)]
    [InlineData("RO49AAAA1B3100759384000", false)]   // Romania: 24 de caractere
    [InlineData("DE89370400440532013000", true)]
    public void Iban_is_checked_with_mod_97(string value, bool valid) => Assert.Equal(valid, Iban.IsValid(value));

    [Theory]
    [InlineData("J40/1234/2015", true)]
    [InlineData("F12/56/2001", true)]
    [InlineData("J2024000123401", true)]
    [InlineData("J40-1234-2015", false)]
    [InlineData("J99/1/2015", false)]
    [InlineData("X2024000123401", false)]
    public void Trade_register_number_accepts_the_classic_and_the_unified_format(string value, bool valid) =>
        Assert.Equal(valid, TradeRegisterNumber.IsValid(value));

    [Fact]
    public void Document_number_is_prefix_year_and_padded_number() =>
        Assert.Equal("EXD-2026-001285", DocumentNumberFormat.Format("EXD", 2026, 1285, 6));
}
