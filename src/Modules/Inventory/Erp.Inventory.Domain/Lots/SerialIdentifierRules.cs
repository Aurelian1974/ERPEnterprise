using System.Text.RegularExpressions;

namespace Erp.Inventory.Domain.Lots;

/// <summary>
/// Validarea identificatorilor bucatii (02.05, S4 3.4): masca (expresie regulata) a tipului si verificarea speciala: cifra de control Luhn la IMEI,
/// 17 caractere fara I, O, Q la VIN. Valoarea invalida e refuzata (54471).
/// </summary>
public static class SerialIdentifierRules
{
    public const string Invalid = "INV_SERIAL_IDENTIFIER_INVALID";

    public static bool IsValid(string value, string? mask, byte checkKind)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = value.Trim();
        if (text.Length == 0 || (mask is { Length: > 0 } && !Regex.IsMatch(text, mask, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200))))
        {
            return false;
        }

        return checkKind switch
        {
            1 => Luhn(text),
            2 => text.Length == 17 && text.All(c => char.IsAsciiLetterOrDigit(c) && c is not ('I' or 'O' or 'Q')),
            _ => true,
        };
    }

    /// <summary>Cifra de control Luhn (ISO/IEC 7812), folosita de IMEI.</summary>
    public static bool Luhn(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
        }

        return sum % 10 == 0;
    }
}
