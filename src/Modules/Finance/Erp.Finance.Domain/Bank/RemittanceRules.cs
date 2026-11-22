using System.Text.RegularExpressions;

namespace Erp.Finance.Domain.Bank;

/// <summary>
/// Ce se citeste din detaliile unei plati (F2 3.4 si 4.3): CUI-ul (cu cifra de control verificata, ca in Nucleu) si numarul documentului
/// platit, folosite la identificarea partenerului si la potrivirea pe factura.
/// </summary>
public static partial class RemittanceRules
{
    /// <summary>CUI valid: 2–10 cifre, cifra de control dupa cheia 753217532.</summary>
    public static bool IsValidFiscalCode(string? code)
    {
        if (code is null || code.Length is < 2 or > 10 || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        const string key = "753217532";
        var body = code[..^1].PadLeft(9, '0');
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * (key[i] - '0');
        }

        var control = sum * 10 % 11;
        return (control == 10 ? 0 : control) == code[^1] - '0';
    }

    /// <summary>Primul CUI valid din detalii („CUI 14399840”, „CIF: RO14399840”, „RO14399840”), fara prefixul RO; null daca lipseste.</summary>
    public static string? ExtractFiscalCode(string? remittance)
    {
        if (string.IsNullOrWhiteSpace(remittance))
        {
            return null;
        }

        foreach (Match match in FiscalCode().Matches(remittance))
        {
            var code = match.Groups["code"].Value;
            if (IsValidFiscalCode(code))
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>Numarul documentului platit din detalii („fact. FA-123”, „FV 9981”, „factura nr. 77”); null daca lipseste.</summary>
    public static string? ExtractDocumentNo(string? remittance)
    {
        if (string.IsNullOrWhiteSpace(remittance))
        {
            return null;
        }

        var match = DocumentNo().Match(remittance);
        if (!match.Success)
        {
            return null;
        }

        var number = match.Groups["no"].Value.Trim().ToUpperInvariant();
        return number.Length <= 50 ? number : number[..50];
    }

    [GeneratedRegex(@"(?:\b(?:CUI|CIF|C\.U\.I\.|CF|COD FISCAL)\s*[:.]?\s*(?:RO)?\s*|\bRO)(?<code>\d{2,10})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FiscalCode();

    [GeneratedRegex(@"\b(?:FACTURA|FACTURII|FACT|FCT|FV|FA|FF)\.?\s*(?:NR\.?|NO\.?)?\s*(?<no>(?:[A-Z]{1,6}[-/ ]?)?\d{1,10})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentNo();
}
