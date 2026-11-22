using Erp.Finance.Contracts.Treasury;

namespace Erp.Finance.Domain.Treasury;

/// <summary>Cum se inchide ziua unei case (F1 3.1, decizia din 15.11.2026).</summary>
public enum CashCloseMode : byte
{
    OnDemand = 1,
    AutomaticAtMidnight = 2,
}

/// <summary>Regulile contului de trezorerie (02.02, F1 2 si 3.1), fara acces la date.</summary>
public static class TreasuryAccountRules
{
    public const string Ron = "RON";

    /// <summary>
    /// Simbolul contului contabil propus pentru un cont nou (F1 3.1): 5121 / 5124 banca, 5311 / 5314 casa, 5125 card, 581 tranzit.
    /// Depozitul nu are simbol propus; utilizatorul il alege.
    /// </summary>
    public static string? DefaultGlAccount(TreasuryAccountKind kind, string currencyCode)
    {
        var ron = string.Equals(currencyCode, Ron, StringComparison.Ordinal);
        return kind switch
        {
            TreasuryAccountKind.Bank => ron ? "5121" : "5124",
            TreasuryAccountKind.Cash => ron ? "5311" : "5314",
            TreasuryAccountKind.CompanyCard => "5125",
            TreasuryAccountKind.Transit => "581",
            _ => null,
        };
    }

    /// <summary>Banca si cardul cer contul bancar al firmei (IBAN-ul vine din Nucleu, nu se copiaza).</summary>
    public static bool RequiresBankAccount(TreasuryAccountKind kind) => kind is TreasuryAccountKind.Bank or TreasuryAccountKind.CompanyCard;

    /// <summary>Casa cere punctul de lucru.</summary>
    public static bool RequiresBranch(TreasuryAccountKind kind) => kind == TreasuryAccountKind.Cash;

    /// <summary>Doar casele au casieri, gestionar, serii de documente, plafon propriu si mod de inchidere a zilei.</summary>
    public static bool IsCash(TreasuryAccountKind kind) => kind == TreasuryAccountKind.Cash;
}
