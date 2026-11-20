using Erp.Core.Domain;

namespace Erp.Core.Tests.Domain;

public sealed class PartnerRulesTests
{
    [Theory]
    [InlineData("1800101221144", true)]
    [InlineData("2801015400126", true)]
    [InlineData("280 101 540 0126", true)]   // spatiile se ignora
    [InlineData("1800101221145", false)]     // cifra de control gresita
    [InlineData("1801301221144", false)]     // luna 13
    [InlineData("0800101221144", false)]     // prima cifra 0
    [InlineData("180010122114", false)]
    [InlineData("18001012211AB", false)]
    public void Personal_id_checks_shape_and_control_digit(string value, bool valid) =>
        Assert.Equal(valid, RomanianPersonalId.IsValid(value));

    [Theory]
    [InlineData("DE298765432", true)]
    [InlineData("de 298765432", true)]
    [InlineData("EL123456789", true)]
    [InlineData("FR1A123456789", true)]
    [InlineData("DE", false)]
    [InlineData("298765432", false)]
    public void Eu_vat_number_has_country_prefix(string value, bool valid) => Assert.Equal(valid, EuVatNumber.IsValid(value));
}
