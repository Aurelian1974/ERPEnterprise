using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Settlement;

/// <summary>Rolul partenerului in decontari (02.02, F3 3.1): soldurile se tin separat ca client si ca furnizor.</summary>
public enum PartnerRole : byte
{
    Customer = 1,
    Supplier = 2,
}

/// <summary>Starea e-Factura a unei partide (F3 3.1, 3.8).</summary>
public enum EInvoiceStatus : byte
{
    NotApplicable = 0,
    Sent = 1,
    Accepted = 2,
    Rejected = 3,
    ReceivedFromSpv = 4,
    NotReceivedFromSpv = 5,
}

/// <summary>O scadenta a facturii (F3 4.1); suma scadentelor = TotalAmount.</summary>
public sealed record InvoiceDueDate(DateOnly DueDate, decimal Amount);

/// <summary>
/// Factura emisa si validata in Vanzari (02.03), cu continutul minim cerut de F3 (4.1). Definit in contractele Finantelor, decizia lui Aurelian din
/// 23.11.2026: Vanzari depinde de Finante si publica acest tip; Finantele nu depind de Vanzari. Sumele au semnul facturii (o factura storno are
/// TotalAmount negativ). CompanyId vine din anvelopa evenimentului.
/// </summary>
public sealed record FacturaEmisa(long InvoiceId, int PartnerId, string Number, DateOnly DocumentDate, DateOnly AccountingDate, string CurrencyCode,
    decimal ExchangeRate, decimal TotalAmount, decimal TotalAmountRon, decimal VatAmount, decimal VatAmountRon, bool IsVatOnCollection, bool IsReversal,
    long? ReversedInvoiceId, bool IsAdvanceInvoice, IReadOnlyList<InvoiceDueDate> DueDates, int? CostCenterId, int? ProjectId, EInvoiceStatus EInvoiceStatus,
    string? EInvoiceIndexNo) : IntegrationEvent
{
    public const string DocumentType = "Sales.Invoice";
}

/// <summary>Factura de furnizor validata in Achizitii (02.04), acelasi continut minim ca FacturaEmisa (F3 4.1); definita aici din acelasi motiv.</summary>
public sealed record FacturaFurnizorValidata(long InvoiceId, int PartnerId, string Number, DateOnly DocumentDate, DateOnly AccountingDate, string CurrencyCode,
    decimal ExchangeRate, decimal TotalAmount, decimal TotalAmountRon, decimal VatAmount, decimal VatAmountRon, bool IsVatOnCollection, bool IsReversal,
    long? ReversedInvoiceId, bool IsAdvanceInvoice, IReadOnlyList<InvoiceDueDate> DueDates, int? CostCenterId, int? ProjectId, EInvoiceStatus EInvoiceStatus,
    string? EInvoiceIndexNo) : IntegrationEvent
{
    public const string DocumentType = "Purchasing.SupplierInvoice";
}

/// <summary>O parte a unei alocari, in evenimentul AlocareInregistrata (F3 5.1).</summary>
public sealed record AllocatedOpenItem(long Id, string SourceDocumentType, long SourceDocumentId, string CurrencyCode, decimal ExchangeRate);

/// <summary>Crearea (sau desfacerea, IsUndo) unei alocari (F3 5.1). Consumatori: partea contabila (reclasificarea avansurilor), F4.</summary>
public sealed record AlocareInregistrata(long AllocationId, int PartnerId, PartnerRole Role, AllocatedOpenItem DebitOpenItem, AllocatedOpenItem CreditOpenItem,
    decimal Amount, decimal AmountRonDebit, decimal AmountRonCredit, DateOnly AllocationDate, byte Source, bool IsUndo) : IntegrationEvent;

/// <summary>O factura a ajuns la rest zero (sau a iesit din zero la o desfacere, IsReopened) (F3 5.1). Consumatori: Vanzari, Achizitii, CRM.</summary>
public sealed record FacturaAchitata(PartnerRole Role, string SourceDocumentType, long SourceDocumentId, int PartnerId, DateOnly? ClosedAt, bool IsReopened)
    : IntegrationEvent;

/// <summary>Un rand de TVA la incasare devenit exigibil (sau inversat) (F3 3.7, 5.1). Side: 1 Colectat, 2 Deductibil.</summary>
public sealed record TvaLaIncasareExigibil(long OpenItemId, string SourceDocumentType, long SourceDocumentId, byte Side, DateOnly ExigibilityDate,
    decimal BaseAmountRon, decimal VatAmountRon, bool IsReversal) : IntegrationEvent;

/// <summary>O linie a unui PV de compensare validat (F3 5.1): partida, partenerul, rolul si suma stinsa (cu semnul partidei).</summary>
public sealed record CompensareValidataLinie(long OpenItemId, int PartnerId, PartnerRole Role, decimal Amount);

/// <summary>Validarea unui PV de compensare (F3 5.1). Consumator: partea contabila.</summary>
public sealed record CompensareValidata(int CompensationId, string Number, DateOnly DocumentDate, IReadOnlyList<CompensareValidataLinie> Lines) : IntegrationEvent;

/// <summary>Soldul unui partener pe rol si moneda la o data (F3 6). Sumele urmeaza conventia de semn: debit (+), credit (−).</summary>
public sealed record PartnerBalanceDto(int CompanyId, int PartnerId, PartnerRole Role, string CurrencyCode, decimal Balance, decimal BalanceRon,
    decimal OverdueRon, int MaxOverdueDays, decimal UnallocatedPaymentsRon, DateOnly AsOf);

/// <summary>O partida deschisa (F3 6).</summary>
public sealed record OpenItemDto(long OpenItemId, string SourceDocumentType, long SourceDocumentId, string DocumentNo, DateOnly DocumentDate, DateOnly DueDate,
    string CurrencyCode, decimal Amount, decimal OpenAmount, bool IsDisputed, bool IsBlockedForPayment, byte EInvoiceStatus);

/// <summary>Soldurile si partidele deschise, pentru Vanzari (limita de credit), CRM si Raportare (F3 6).</summary>
public interface IPartnerBalanceQueries
{
    Task<IReadOnlyList<PartnerBalanceDto>> GetBalanceAsync(int companyId, int partnerId, PartnerRole? role, DateOnly asOf, CancellationToken ct);

    Task<IReadOnlyList<OpenItemDto>> GetOpenItemsAsync(int companyId, int partnerId, PartnerRole role, CancellationToken ct);
}
