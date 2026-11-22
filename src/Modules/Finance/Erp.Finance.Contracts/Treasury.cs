using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Treasury;

/// <summary>Tipul contului de trezorerie (02.02, F1 3.1).</summary>
public enum TreasuryAccountKind : byte
{
    Bank = 1,
    Cash = 2,
    CompanyCard = 3,
    Transit = 4,
    Deposit = 5,
}

/// <summary>Un cont de trezorerie al firmei, pentru celelalte module (F1 6).</summary>
public sealed record TreasuryAccountDto(
    int Id, int CompanyId, int? BranchId, string Code, string Name,
    TreasuryAccountKind Kind, string CurrencyCode, int? CompanyBankAccountId,
    string? GlAccountCode, bool IsActive);

/// <summary>Soldul unui cont de trezorerie la sfarsitul unei zile (ultima zi cu activitate pana la ea), in moneda contului si in lei.</summary>
public sealed record TreasuryBalanceDto(int TreasuryAccountId, DateOnly Day, string CurrencyCode, decimal Balance, decimal BalanceRon, bool IsDayClosed);

/// <summary>Conturile de trezorerie ale unei firme, citite de celelalte module (F1 6).</summary>
public interface ITreasuryAccountQueries
{
    Task<IReadOnlyList<TreasuryAccountDto>> GetByCompanyAsync(int companyId, TreasuryAccountKind? kind, CancellationToken ct);

    Task<TreasuryBalanceDto> GetBalanceAsync(int treasuryAccountId, DateOnly day, CancellationToken ct);
}

/// <summary>O linie a unei incasari sau plati de trezorerie (F1 5.1).</summary>
public sealed record TreasuryMovementLine(long LineId, string OperationTypeCode, decimal Amount, decimal AmountRon, string? SettlementRef,
    int? CounterTreasuryAccountId);

/// <summary>
/// Validarea unei chitante sau dispozitii de incasare (F1 5.1; in F2 si a unei incasari bancare). Consumatori: Vanzari, CRM, partea
/// contabila. DocumentType = codul tipului de document (Finance.Receipt, Finance.CashIn).
/// </summary>
public sealed record IncasareInregistrata(int TreasuryAccountId, TreasuryAccountKind TreasuryKind, string DocumentType, int DocumentId, string? Number,
    DateOnly DocumentDate, int? PartnerId, int? EmployeeId, string CurrencyCode, decimal ExchangeRate, decimal TotalAmount, decimal TotalAmountRon,
    IReadOnlyList<TreasuryMovementLine> Lines) : IntegrationEvent;

/// <summary>Validarea unei dispozitii de plata (F1 5.1; in F2 si a unei plati bancare). Consumatori: F3, Achizitii, partea contabila.</summary>
public sealed record PlataInregistrata(int TreasuryAccountId, TreasuryAccountKind TreasuryKind, string DocumentType, int DocumentId, string? Number,
    DateOnly DocumentDate, int? PartnerId, int? EmployeeId, string CurrencyCode, decimal ExchangeRate, decimal TotalAmount, decimal TotalAmountRon,
    IReadOnlyList<TreasuryMovementLine> Lines) : IntegrationEvent;

/// <summary>
/// Validarea unei stornari de document de trezorerie (F1 5.1; in F2 si desfacerea validarii unei linii de extras). Consumatori: F3, partea
/// contabila. DocumentType lipseste la documentele de casa; la banca e Finance.BankStatementLine (original = storno = linia).
/// </summary>
public sealed record DocumentTrezorerieStornat(int OriginalDocumentId, int ReversalDocumentId, DateOnly DocumentDate) : IntegrationEvent
{
    public string? DocumentType { get; init; }
}

/// <summary>Inchiderea unei zile de casa (F1 5.1). Consumatori: Raportare, partea contabila.</summary>
public sealed record RegistruCasaInchis(int TreasuryAccountId, DateOnly Day, decimal OpeningBalance, decimal TotalIn, decimal TotalOut,
    decimal ClosingBalance) : IntegrationEvent;

/// <summary>Plata unui avans spre decontare a fost validata (F1 5.1). Consumatori: HR (evidenta), Raportare.</summary>
public sealed record AvansDecontareAcordat(int CashAdvanceId, int EmployeeId, decimal Amount, string CurrencyCode, DateOnly SettlementDueDate)
    : IntegrationEvent;

/// <summary>Linia unui decont validat (F1 5.1).</summary>
public sealed record DecontLinie(string ExpenseTypeCode, decimal Amount, decimal VatAmount, int? SupplierInvoiceId);

/// <summary>Validarea unui decont de cheltuieli (F1 5.1). Consumatori: HR (diurna impozabila), F3, partea contabila.</summary>
public sealed record DecontCheltuieliValidat(int ExpenseReportId, int EmployeeId, int? CashAdvanceId, decimal TotalJustified, decimal Difference,
    decimal TaxablePerDiemAmount, IReadOnlyList<DecontLinie> Lines) : IntegrationEvent;
