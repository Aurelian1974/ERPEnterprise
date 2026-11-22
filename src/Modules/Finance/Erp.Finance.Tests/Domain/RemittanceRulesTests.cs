using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Tests.Domain;

/// <summary>Ce se citeste din detaliile platii (02.02, F2 3.4 si 4.3): CUI-ul cu cifra de control si numarul facturii.</summary>
public sealed class RemittanceRulesTests
{
    [Theory]
    [InlineData("14399840", true)]
    [InlineData("18547290", true)]
    [InlineData("14399841", false)]
    [InlineData("1", false)]
    [InlineData("12345678901", false)]
    public void Fiscal_codes_are_checked_with_the_control_digit(string code, bool valid) => Assert.Equal(valid, RemittanceRules.IsValidFiscalCode(code));

    [Theory]
    [InlineData("Plata factura FV 1234 CUI 14399840", "14399840")]
    [InlineData("c/v marfa, CIF: RO18547290, fact 77", "18547290")]
    [InlineData("RO14399841 apoi RO18547290", "18547290")]
    [InlineData("Comision administrare cont 0045", null)]
    public void The_first_valid_fiscal_code_is_found_in_the_details(string text, string? expected) =>
        Assert.Equal(expected, RemittanceRules.ExtractFiscalCode(text));

    [Theory]
    [InlineData("Plata fact. FA-123, FA-130", "FA-123")]
    [InlineData("contravaloare factura nr. 77 din 01.11", "77")]
    [InlineData("FV 9981 si FV 9982", "9981")]
    [InlineData("Dobanda octombrie", null)]
    public void The_paid_document_number_is_found_in_the_details(string text, string? expected) =>
        Assert.Equal(expected, RemittanceRules.ExtractDocumentNo(text));
}
