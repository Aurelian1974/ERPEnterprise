using System.Text.RegularExpressions;

namespace Erp.Licensing;

/// <summary>Limitele din licenta (08, 3.3). Numarul de firme nu e o limita separata: e numarul de CUI-uri.</summary>
public sealed record LicenseLimits(int Users, int PayrollEmployees, int PosTerminals);

/// <summary>Continutul decriptat al licentei (08, 3.3). Exista doar in memorie (08, 4.1).</summary>
public sealed partial record License
{
    public const string ProductCode = "ERP";

    public required string LicenseId { get; init; }

    public required string Product { get; init; }

    public required string CustomerName { get; init; }

    /// <summary>CUI-urile, doar cifre, separate prin „:” (decizia D4).</summary>
    public required string FiscalCodes { get; init; }

    public required IReadOnlyList<string> Modules { get; init; }

    public required LicenseLimits Limits { get; init; }

    public required DateOnly IssuedAt { get; init; }

    /// <summary>null = „Nelimitat” (08, 4.4).</summary>
    public DateOnly? ValidUntil { get; init; }

    /// <summary>null = „Fara limita” (08, 4.5).</summary>
    public DateOnly? UpdatesUntil { get; init; }

    public IReadOnlyList<string> FiscalCodeList => FiscalCodes.Split(':');

    public int Companies => FiscalCodeList.Count;

    /// <summary>CUI-ul (cu sau fara „RO”) figureaza in licenta; comparatie exacta pe cifre (08, 4.3).</summary>
    public bool ContainsFiscalCode(string fiscalCode) =>
        FiscalCode.Normalize(fiscalCode) is { } digits && FiscalCodeList.Contains(digits, StringComparer.Ordinal);

    /// <summary>Regulile de continut (08, 3.3); null daca licenta e corecta, altfel motivul (pentru jurnal, nu pentru utilizator).</summary>
    public string? Validate()
    {
        if (!string.Equals(Product, ProductCode, StringComparison.Ordinal))
        {
            return "produsul nu este ERP";
        }

        if (!LicenseIdPattern().IsMatch(LicenseId))
        {
            return "numărul licenței nu are forma LIC-AAAA-NNNNNN";
        }

        if (CustomerName.Trim().Length is < 2 or > 200)
        {
            return "denumirea clientului trebuie să aibă 2–200 de caractere";
        }

        if (!FiscalCodesPattern().IsMatch(FiscalCodes))
        {
            return "CUI-urile nu respectă forma cifre separate prin „:”";
        }

        var codes = FiscalCodeList;
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Count || codes.Any(c => !FiscalCode.IsValid(c)))
        {
            return "CUI-uri duplicate sau cu cifra de control greșită";
        }

        if (Modules.Count == 0 || !Modules.Contains(LicenseModules.Core, StringComparer.Ordinal))
        {
            return "modulul Core lipsește";
        }

        if (Limits.Users < 0 || Limits.PayrollEmployees < 0 || Limits.PosTerminals < 0)
        {
            return "limite negative";
        }

        if (ValidUntil is { } until && until < IssuedAt)
        {
            return "valabilitatea se termină înaintea emiterii";
        }

        return null;
    }

    [GeneratedRegex(@"^LIC-\d{4}-\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex LicenseIdPattern();

    [GeneratedRegex(@"^[1-9][0-9]{1,9}(:[1-9][0-9]{1,9})*$", RegexOptions.CultureInvariant)]
    private static partial Regex FiscalCodesPattern();
}

/// <summary>Codurile de modul cunoscute de aceasta versiune (comparatie exacta, NET-21).</summary>
public static class LicenseModules
{
    public const string Core = "Core";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        Core, "Finance", "Sales", "Purchasing", "Inventory", "Integrations", "Wms", "Tms",
        "Manufacturing", "Hr", "Pos", "Construction", "Crm", "Reporting",
    };
}
