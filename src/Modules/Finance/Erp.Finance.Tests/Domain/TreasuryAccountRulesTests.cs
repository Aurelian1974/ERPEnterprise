using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Treasury;

namespace Erp.Finance.Tests.Domain;

/// <summary>Regulile contului de trezorerie (02.02, F1 3.1), fara baza.</summary>
public sealed class TreasuryAccountRulesTests
{
    [Theory]
    [Trait("Criteriu", "02.02-F1-3.1")]
    [InlineData(TreasuryAccountKind.Bank, "RON", "5121")]
    [InlineData(TreasuryAccountKind.Bank, "EUR", "5124")]
    [InlineData(TreasuryAccountKind.Cash, "RON", "5311")]
    [InlineData(TreasuryAccountKind.Cash, "EUR", "5314")]
    [InlineData(TreasuryAccountKind.CompanyCard, "RON", "5125")]
    [InlineData(TreasuryAccountKind.Transit, "RON", "581")]
    [InlineData(TreasuryAccountKind.Deposit, "RON", null)]
    public void The_proposed_gl_account_follows_the_kind_and_currency(TreasuryAccountKind kind, string currency, string? expected) =>
        Assert.Equal(expected, TreasuryAccountRules.DefaultGlAccount(kind, currency));

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.1")]
    public void Bank_and_card_need_a_bank_account_and_cash_needs_a_branch()
    {
        Assert.True(TreasuryAccountRules.RequiresBankAccount(TreasuryAccountKind.Bank));
        Assert.True(TreasuryAccountRules.RequiresBankAccount(TreasuryAccountKind.CompanyCard));
        Assert.False(TreasuryAccountRules.RequiresBankAccount(TreasuryAccountKind.Cash));
        Assert.True(TreasuryAccountRules.RequiresBranch(TreasuryAccountKind.Cash));
        Assert.False(TreasuryAccountRules.RequiresBranch(TreasuryAccountKind.Transit));
    }
}

/// <summary>Sumele ca text in API: punct zecimal, cel mult doua zecimale.</summary>
public sealed class MoneyTests
{
    [Theory]
    [InlineData("40000", true, "40000.00")]
    [InlineData(" 1.5 ", true, "1.50")]
    [InlineData("-3.25", true, "-3.25")]
    [InlineData("1.234", false, null)]
    [InlineData("1,5", false, null)]
    [InlineData("", false, null)]
    [InlineData("abc", false, null)]
    public void Amounts_are_parsed_with_a_decimal_point_and_at_most_two_decimals(string text, bool valid, string? formatted)
    {
        Assert.Equal(valid, Erp.Finance.Domain.Money.TryParse(text, out var amount));
        Assert.Equal(formatted, valid ? Erp.Finance.Domain.Money.Format(amount) : null);
    }
}
