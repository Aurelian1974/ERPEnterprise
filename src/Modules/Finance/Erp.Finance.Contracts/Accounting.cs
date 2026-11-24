using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Accounting;

/// <summary>Sursa unei note contabile (C2 3.5): eveniment, manuala, reclasificare, inchidere, deschidere, storno.</summary>
public enum EntrySourceKind : byte
{
    Event = 1,
    Manual = 2,
    Reclassification = 3,
    Closing = 4,
    Opening = 5,
    Reversal = 6,
}

/// <summary>Validarea oricarei note: automata, manuala, storno (C2 5.1). Consumatori: Raportare, C4 (SAF-T incremental), C1.</summary>
public sealed record NotaContabilaValidata(long EntryId, string JournalCode, int Number, DateOnly AccountingDate, EntrySourceKind SourceKind,
    string? SourceDocumentType, long? SourceDocumentId, decimal TotalRon) : IntegrationEvent;

/// <summary>
/// O componenta de suma a unui document (C2 3.1): ce suma e (Net, Vat, Total, Amount, FxDifference...), valorile in moneda documentului si in
/// lei si atributele din care se gaseste contul. PartnerId pe componenta (extensie a contractului v1): documentele cu mai multi parteneri
/// (compensarile) il dau pe fiecare suma; altfel se ia din antet.
/// </summary>
public sealed record AccountingComponent(string Component, decimal Amount, decimal AmountRon)
{
    public int? PartnerId { get; init; }
    public int? ItemId { get; init; }
    public int? ItemGroupId { get; init; }
    public string? ItemType { get; init; }
    public string? VatRateCode { get; init; }
    public string? VatCategory { get; init; }
    public int? TreasuryAccountId { get; init; }
    public string? OperationTypeCode { get; init; }
    public string? ExpenseTypeCode { get; init; }
    public int? WarehouseId { get; init; }
    public int? CostCenterId { get; init; }
    public int? ProjectId { get; init; }
    public int? SiteId { get; init; }
    public string? GlAccountOverride { get; init; }
    public string? SourceLineRef { get; init; }
}

/// <summary>
/// Contractul de contare v1 (C2 3.1): ce trimite un modul pentru a fi contat. Modulele nu stiu de conturi; trimit componente cu atribute.
/// Flags: caracteristicile documentului care aleg varianta modelului (VatOnCollection, ReverseCharge, IntraCommunity, Export, Customer,
/// Supplier, Gain, Loss...).
/// </summary>
public sealed record AccountingPayload(string DocumentType, string? DocumentNumber, DateOnly DocumentDate, DateOnly AccountingDate, string CurrencyCode,
    decimal ExchangeRate, IReadOnlyList<string> Flags, IReadOnlyList<AccountingComponent> Components)
{
    public string SourceModule { get; init; } = "Finance";
    public long DocumentId { get; init; }
    public int? PartnerId { get; init; }
    public int? EmployeeId { get; init; }
    public bool IsReversal { get; init; }
    public long? ReversedDocumentId { get; init; }
}

/// <summary>Un eveniment care poarta blocul de contare (modulele Vanzari, Achizitii, Stocuri, HR, Mijloace fixe).</summary>
public interface IAccountable
{
    AccountingPayload? Accounting { get; }
}
