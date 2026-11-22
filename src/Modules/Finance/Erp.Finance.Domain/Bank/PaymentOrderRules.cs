using System.Globalization;
using System.Numerics;
using System.Text;

namespace Erp.Finance.Domain.Bank;

public enum PaymentOrderStatus : byte
{
    Draft = 1,
    Validated = 2,
    Reversed = 3,
}

public enum PaymentConfirmationStatus : byte
{
    Unconfirmed = 1,
    Confirmed = 2,
    Rejected = 3,
}

/// <summary>De unde vine OP-ul (F2 3.6): ecranul propriu, ecranul Extras de cont (plata constatata), propunere F5 sau alt modul.</summary>
public enum PaymentOrderOrigin : byte
{
    PaymentOrders = 1,
    BankStatement = 2,
    Proposal = 3,
    OtherModule = 4,
}

public enum PaymentPurpose : byte
{
    Suppliers = 1,
    Salaries = 2,
    StateBudget = 3,
    Other = 4,
}

/// <summary>Regulile ordinului de plata (F2 3.6), fara acces la date.</summary>
public static class PaymentOrderRules
{
    public const string DocumentType = "Finance.PaymentOrder";

    /// <summary>IBAN valid (ISO 13616): 15–34 de caractere, tara + cifre de control, mod 97 = 1.</summary>
    public static bool IsValidIban(string? iban)
    {
        var value = BankStatementRules.NormalizeIban(iban);
        if (value is null || value.Length < 15 || !char.IsAsciiLetterUpper(value[0]) || !char.IsAsciiLetterUpper(value[1])
            || !char.IsAsciiDigit(value[2]) || !char.IsAsciiDigit(value[3]) || !value.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        var digits = new StringBuilder();
        foreach (var c in value[4..] + value[..4])
        {
            digits.Append(char.IsAsciiLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString());
        }

        return BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture) % 97 == 1;
    }

    /// <summary>Detaliile platii generate din facturi (F2 3.6): „Plata fact. FA-123, FA-130”, taiate la lungimea maxima a bancii.</summary>
    public static string? Remittance(IEnumerable<string?> settlementRefs, int maxLength)
    {
        var refs = settlementRefs.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (refs.Count == 0)
        {
            return null;
        }

        var text = "Plata fact. " + string.Join(", ", refs);
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
