using Erp.Finance.Contracts.Accounting;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Contracts.Treasury;

namespace Erp.Finance.Application.Accounting;

/// <summary>
/// Adaptoarele evenimentelor Finantelor (F1–F4) la contractul de contare (C2 3.1, decizia din C2.2): F1–F4 nu poarta bloc de contare, iar
/// adaptorul traduce evenimentul in componente. Facturile modulelor externe pot trimite blocul (IAccountable); lipsa lui, se conteaza pe
/// totaluri. Intoarce null pentru evenimentele care nu produc nota (diferentele de curs fara partener, amanate in RAMAS-DE-FACUT).
/// </summary>
public static class PostingAdapters
{
    public static PostingRequest SalesInvoice(FacturaEmisa e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Request(e.EventId, nameof(FacturaEmisa), e.CompanyId, e.Accounting ?? Invoice(FacturaEmisa.DocumentType, e.InvoiceId, e.PartnerId, e.Number,
            e.DocumentDate, e.AccountingDate, e.CurrencyCode, e.ExchangeRate, e.TotalAmount, e.TotalAmountRon, e.VatAmount, e.VatAmountRon, e.IsVatOnCollection,
            e.IsReversal, e.ReversedInvoiceId, e.CostCenterId, e.ProjectId, "Sales"));
    }

    public static PostingRequest SupplierInvoice(FacturaFurnizorValidata e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Request(e.EventId, nameof(FacturaFurnizorValidata), e.CompanyId, e.Accounting ?? Invoice(FacturaFurnizorValidata.DocumentType, e.InvoiceId,
            e.PartnerId, e.Number, e.DocumentDate, e.AccountingDate, e.CurrencyCode, e.ExchangeRate, e.TotalAmount, e.TotalAmountRon, e.VatAmount, e.VatAmountRon,
            e.IsVatOnCollection, e.IsReversal, e.ReversedInvoiceId, e.CostCenterId, e.ProjectId, "Purchasing"));
    }

    public static PostingRequest Receipt(IncasareInregistrata e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Treasury(e.EventId, nameof(IncasareInregistrata), e.CompanyId, e.TreasuryAccountId, e.DocumentType, e.DocumentId, e.Number, e.DocumentDate,
            e.PartnerId, e.EmployeeId, e.CurrencyCode, e.ExchangeRate, e.Lines);
    }

    public static PostingRequest Payment(PlataInregistrata e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Treasury(e.EventId, nameof(PlataInregistrata), e.CompanyId, e.TreasuryAccountId, e.DocumentType, e.DocumentId, e.Number, e.DocumentDate,
            e.PartnerId, e.EmployeeId, e.CurrencyCode, e.ExchangeRate, e.Lines);
    }

    /// <summary>TVA la incasare devenita exigibila (F3 3.7): 4428 → 4427 la client, 4426 → 4428 la furnizor.</summary>
    public static PostingRequest VatOnCollection(TvaLaIncasareExigibil e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var payload = new AccountingPayload(e.SourceDocumentType, $"{e.SourceDocumentType} {e.SourceDocumentId}", e.ExigibilityDate, e.ExigibilityDate, "RON", 1m,
            [e.Side == 1 ? "Customer" : "Supplier"], [new AccountingComponent("Vat", e.VatAmountRon, e.VatAmountRon), new AccountingComponent("Base", e.BaseAmountRon, e.BaseAmountRon)])
        {
            DocumentId = e.SourceDocumentId, IsReversal = e.IsReversal,
        };
        return Request(e.EventId, nameof(TvaLaIncasareExigibil), e.CompanyId, payload);
    }

    /// <summary>
    /// Diferenta de curs la decontare (F4 4.3): castig sau pierdere dupa contul din eveniment (765 / 665), clientul sau furnizorul din rol.
    /// Stornarea vine cu suma negata si acelasi cont. Fara partener (schimb valutar, transfer intre conturi) nu produce nota inca.
    /// </summary>
    public static PostingRequest? FxDifference(DiferentaCursInregistrata e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.PartnerId is null || e.Role is null)
        {
            return null;
        }

        var gain = e.GlAccountCode.StartsWith('7');
        var amount = Math.Abs(e.DifferenceRon) * (e.IsReversal ? -1 : 1);
        var payload = new AccountingPayload("Finance.FxDifference", $"DC {e.FxDifferenceId}", e.DifferenceDate, e.DifferenceDate, "RON", 1m,
            [e.Role == PartnerRole.Customer ? "Customer" : "Supplier", gain ? "Gain" : "Loss"],
            [new AccountingComponent("Difference", amount, amount) { GlAccountOverride = e.GlAccountCode }])
        {
            DocumentId = e.FxDifferenceId, PartnerId = e.PartnerId, IsReversal = e.IsReversal,
        };
        return Request(e.EventId, nameof(DiferentaCursInregistrata), e.CompanyId, payload);
    }

    /// <summary>Compensarea (F3 3.5): datoria stinsa (401 debit) si creanta stinsa (4111 credit), fiecare pe partenerul ei.</summary>
    public static PostingRequest Compensation(CompensareValidata e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var components = e.Lines.Select(l => new AccountingComponent(l.Role == PartnerRole.Supplier ? "Payable" : "Receivable", Math.Abs(l.Amount), Math.Abs(l.Amount))
        {
            PartnerId = l.PartnerId, SourceLineRef = l.OpenItemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).ToList();
        var payload = new AccountingPayload("Finance.Compensation", e.Number, e.DocumentDate, e.DocumentDate, "RON", 1m, [], components) { DocumentId = e.CompensationId };
        return Request(e.EventId, nameof(CompensareValidata), e.CompanyId, payload);
    }

    /// <summary>
    /// Decontul de cheltuieli (F1 3.6): cheltuiala pe tipul ei (net), TVA deductibila, totalul pe 542 al angajatului. Liniile platite pe o factura
    /// de furnizor (contata la Achizitii) nu intra aici; stingerea datoriei fata de furnizor e in RAMAS-DE-FACUT.
    /// </summary>
    public static PostingRequest ExpenseReport(DecontCheltuieliValidat e, DateOnly accountingDate)
    {
        ArgumentNullException.ThrowIfNull(e);
        var lines = e.Lines.Where(l => l.SupplierInvoiceId is null).ToList();
        var components = new List<AccountingComponent>();
        foreach (var l in lines)
        {
            components.Add(new AccountingComponent("Net", l.Amount - l.VatAmount, l.Amount - l.VatAmount) { ExpenseTypeCode = l.ExpenseTypeCode });
            if (l.VatAmount != 0)
            {
                components.Add(new AccountingComponent("Vat", l.VatAmount, l.VatAmount));
            }
        }

        var total = lines.Sum(l => l.Amount);
        components.Add(new AccountingComponent("Total", total, total));
        var payload = new AccountingPayload("Finance.ExpenseReport", $"DC {e.ExpenseReportId}", accountingDate, accountingDate, "RON", 1m, [], components)
        {
            DocumentId = e.ExpenseReportId, EmployeeId = e.EmployeeId,
        };
        return Request(e.EventId, nameof(DecontCheltuieliValidat), e.CompanyId, payload);
    }

    private static AccountingPayload Invoice(string documentType, long id, int partnerId, string number, DateOnly documentDate, DateOnly accountingDate,
        string currency, decimal rate, decimal total, decimal totalRon, decimal vat, decimal vatRon, bool vatOnCollection, bool isReversal, long? reversedId,
        int? costCenterId, int? projectId, string module)
    {
        var components = new List<AccountingComponent>
        {
            new("Total", total, totalRon),
            new("Net", total - vat, totalRon - vatRon) { CostCenterId = costCenterId, ProjectId = projectId },
        };
        if (vat != 0 || vatRon != 0)
        {
            components.Add(new AccountingComponent("Vat", vat, vatRon));
        }

        return new AccountingPayload(documentType, number, documentDate, accountingDate, currency, rate, vatOnCollection ? ["VatOnCollection"] : [], components)
        {
            SourceModule = module, DocumentId = id, PartnerId = partnerId, IsReversal = isReversal, ReversedDocumentId = reversedId,
        };
    }

    private static PostingRequest Treasury(Guid eventId, string eventType, int companyId, int treasuryAccountId, string documentType, int documentId, string? number,
        DateOnly date, int? partnerId, int? employeeId, string currency, decimal rate, IReadOnlyList<TreasuryMovementLine> lines)
    {
        var components = lines.Select(l => new AccountingComponent("Amount", l.Amount, l.AmountRon)
        {
            TreasuryAccountId = treasuryAccountId, OperationTypeCode = l.OperationTypeCode,
            SourceLineRef = l.LineId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).ToList();
        var payload = new AccountingPayload(documentType, number, date, date, currency, rate, [], components)
        {
            DocumentId = documentId, PartnerId = partnerId, EmployeeId = employeeId,
        };
        return Request(eventId, eventType, companyId, payload);
    }

    private static PostingRequest Request(Guid eventId, string eventType, int companyId, AccountingPayload payload) =>
        new(eventId, eventType, companyId, payload);
}
