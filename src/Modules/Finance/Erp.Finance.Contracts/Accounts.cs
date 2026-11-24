namespace Erp.Finance.Contracts.Accounts;

/// <summary>Un cont al planului romanesc, asa cum il vad celelalte module (C1 6). AccountType: 1 activ, 2 pasiv, 3 bifunctional.</summary>
public sealed record GlAccountDto(int Id, string Code, string Name, byte? AccountType, bool IsPostable, byte CurrencyMode, string? CurrencyCode,
    byte PartyRequirement, int? CompanyId);

/// <summary>Regula efectiva a unui tip de dimensiune pe cont: 0 interzis, 1 optional, 2 obligatoriu.</summary>
public sealed record GlAccountRuleDto(string DimensionType, byte Mode);

/// <summary>Rolurile de sistem ale conturilor (C1 3.2), folosite ca ultim nivel in cautarea contului la contare.</summary>
public static class GlAccountRoles
{
    public const string CustomerReceivable = "CustomerReceivable";
    public const string CustomerAdvance = "CustomerAdvance";
    public const string SupplierPayable = "SupplierPayable";
    public const string SupplierAdvance = "SupplierAdvance";
    public const string SupplierInvoicesToReceive = "SupplierInvoicesToReceive";
    public const string VatCollected = "VatCollected";
    public const string VatDeductible = "VatDeductible";
    public const string VatPending = "VatPending";
    public const string VatPayable = "VatPayable";
    public const string VatReceivable = "VatReceivable";
    public const string FxGain = "FxGain";
    public const string FxLoss = "FxLoss";
    public const string IndexGain = "IndexGain";
    public const string IndexLoss = "IndexLoss";
    public const string CashInTransit = "CashInTransit";
    public const string ProfitOrLoss = "ProfitOrLoss";
    public const string RetainedEarnings = "RetainedEarnings";
    public const string ProfitAppropriation = "ProfitAppropriation";
    public const string RoundingGain = "RoundingGain";
    public const string RoundingLoss = "RoundingLoss";

    public static IReadOnlyList<string> All { get; } =
    [
        CustomerReceivable, CustomerAdvance, SupplierPayable, SupplierAdvance, SupplierInvoicesToReceive, VatCollected, VatDeductible, VatPending,
        VatPayable, VatReceivable, FxGain, FxLoss, IndexGain, IndexLoss, CashInTransit, ProfitOrLoss, RetainedEarnings, ProfitAppropriation,
        RoundingGain, RoundingLoss,
    ];
}

/// <summary>Tipul partenerului in conditiile rolurilor (C1 3.2): 1 persoana juridica romana, 2 UE, 3 non-UE, 4 persoana fizica.</summary>
public enum PartnerAccountKind : byte
{
    DomesticCompany = 1,
    EuropeanUnion = 2,
    NonEuropeanUnion = 3,
    Individual = 4,
}

/// <summary>Planul de conturi pentru celelalte module (C1 6, NET-02): doar planul romanesc.</summary>
public interface IChartOfAccountsQueries
{
    /// <summary>Contul care poate primi note la data data, pe firma; null daca nu exista, nu e frunza, e inactiv sau nu e permis firmei.</summary>
    Task<GlAccountDto?> FindPostableAsync(int companyId, string code, DateOnly onDate, CancellationToken ct);

    Task<IReadOnlyList<GlAccountRuleDto>> GetDimensionRulesAsync(int accountId, CancellationToken ct);

    /// <summary>Contul pe rol (ultimul nivel din lantul de cautare din C2).</summary>
    Task<GlAccountDto?> FindByRoleAsync(int companyId, string role, PartnerAccountKind? partnerKind, string? currencyCode, DateOnly onDate,
        CancellationToken ct);
}

/// <summary>Rezultatul validarii unui cod de cont: null daca e valid, altfel motivul, de afisat utilizatorului.</summary>
public sealed record GlAccountCheck(bool IsValid, string? Reason)
{
    public static GlAccountCheck Valid { get; } = new(true, null);
}

/// <summary>Validarea unui GlAccountCode la salvare, folosita de F1–F5 si de celelalte module (C1 4.1 pasul 3).</summary>
public interface IGlAccountValidator
{
    Task<GlAccountCheck> ValidateAsync(int companyId, string code, DateOnly onDate, CancellationToken ct);
}
