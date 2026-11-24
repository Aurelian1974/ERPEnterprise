using Erp.Core.Contracts;
using Erp.Finance.Contracts.Accounting;

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
    string? EInvoiceIndexNo) : IntegrationEvent, IAccountable
{
    public const string DocumentType = "Sales.Invoice";

    /// <summary>Blocul de contare (C2 3.1), trimis de modulul emitent; lipsa: contarea pe totaluri (Net = Total − TVA, fara articole).</summary>
    public AccountingPayload? Accounting { get; init; }

    /// <summary>Factura in lei decontata la cursul unei valute (F4 4.2): valuta de referinta si suma in ea; ambele lipsa pentru o factura obisnuita.</summary>
    public string? IndexCurrencyCode { get; init; }

    public decimal? IndexedAmount { get; init; }
}

/// <summary>Factura de furnizor validata in Achizitii (02.04), acelasi continut minim ca FacturaEmisa (F3 4.1); definita aici din acelasi motiv.</summary>
public sealed record FacturaFurnizorValidata(long InvoiceId, int PartnerId, string Number, DateOnly DocumentDate, DateOnly AccountingDate, string CurrencyCode,
    decimal ExchangeRate, decimal TotalAmount, decimal TotalAmountRon, decimal VatAmount, decimal VatAmountRon, bool IsVatOnCollection, bool IsReversal,
    long? ReversedInvoiceId, bool IsAdvanceInvoice, IReadOnlyList<InvoiceDueDate> DueDates, int? CostCenterId, int? ProjectId, EInvoiceStatus EInvoiceStatus,
    string? EInvoiceIndexNo) : IntegrationEvent, IAccountable
{
    public const string DocumentType = "Purchasing.SupplierInvoice";

    /// <summary>Blocul de contare (C2 3.1), trimis de modulul emitent; lipsa: contarea pe totaluri (Net = Total − TVA, fara articole).</summary>
    public AccountingPayload? Accounting { get; init; }

    /// <summary>Factura in lei decontata la cursul unei valute (F4 4.2), ca la FacturaEmisa.</summary>
    public string? IndexCurrencyCode { get; init; }

    public decimal? IndexedAmount { get; init; }
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

/// <summary>
/// Orice schimbare de stare a unui efect comercial (F3 5.1). Direction: 1 primit, 2 emis; Kind: 1 CEC, 2 bilet la ordin; Status: 1 in portofoliu,
/// 2 depus, 3 incasat / platit, 4 refuzat, 5 girat, 6 stornat. Consumatori: partea contabila, Raportare.
/// </summary>
public sealed record EfectComercialModificat(int CommercialPaperId, byte Direction, byte Kind, byte Status, decimal Amount, int PartnerId, DateOnly EventDate)
    : IntegrationEvent;

/// <summary>O linie a penalitatilor unui partener (F3 5.1): factura, intervalul si zilele, baza, rata (pe zi sau anuala) si penalitatea; Kind 2 e
/// suma fixa de recuperare (Legea 72/2013), fara interval.</summary>
public sealed record PenalitateLinie(byte Kind, long OpenItemId, string InvoiceNo, DateOnly DueDate, DateOnly? FromDate, DateOnly? ToDate, int Days,
    decimal BaseAmount, decimal? RatePerDay, decimal? AnnualRate, decimal PenaltyAmount);

/// <summary>Penalitatile unui partener intr-o moneda; Vanzari emite din ele o factura de penalitati ciorna.</summary>
public sealed record PenalitatiPartener(int PartnerId, string CurrencyCode, decimal Total, IReadOnlyList<PenalitateLinie> Lines);

/// <summary>Aprobarea unui calcul de penalitati (F3 4.7 pasul 3, 5.1). Consumator: Vanzari (factura de penalitati ca ciorna, una pe partener).</summary>
public sealed record PenalitatiCalculate(int PenaltyCalculationId, string Number, DateOnly CalculationDate, DateOnly PeriodFrom, DateOnly PeriodTo,
    IReadOnlyList<PenalitatiPartener> Partners) : IntegrationEvent;

/// <summary>
/// O diferenta de curs realizata sau inversata (F4 3.2, 5.1): Source 1 alocare, 2 schimb valutar, 3 iesire din cont in valuta, 4 indexare;
/// DifferenceRon pozitiv = castig (765 / 768), negativ = pierdere (665 / 668). Consumatori: partea contabila, Raportare.
/// </summary>
public sealed record DiferentaCursInregistrata(long FxDifferenceId, byte Source, DateOnly DifferenceDate, int? PartnerId, PartnerRole? Role, long? OpenItemId,
    long? AllocationId, string CurrencyCode, decimal ForeignAmount, decimal DifferenceRon, bool IsIndexed, string GlAccountCode, bool IsReversal) : IntegrationEvent;

/// <summary>Totalul unei reevaluari pe cont de diferenta (765 / 665), contul elementului reevaluat (4111, 401...) si moneda (F4 5.1).</summary>
public sealed record ReevaluareTotal(string GlAccountCode, string? CounterGlAccountCode, string CurrencyCode, decimal DifferenceRon);

/// <summary>Validarea reevaluarii valutare a unei luni (F4 4.4, 5.1). Consumatori: partea contabila (nota), Raportare.</summary>
public sealed record ReevaluareValutaraValidata(int RunId, int PeriodYear, int PeriodMonth, DateOnly RevaluationDate, IReadOnlyList<ReevaluareTotal> Totals,
    int LineCount) : IntegrationEvent;

/// <summary>Stornarea unei reevaluari validate (F4 4.5, 5.1). Consumator: partea contabila (nota inversa).</summary>
public sealed record ReevaluareValutaraStornata(int RunId, int PeriodYear, int PeriodMonth, DateOnly ReversalDate) : IntegrationEvent;

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
