using Erp.Core.Contracts;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Declarations;

namespace Erp.Finance.Application.Declarations;

/// <summary>O problema gasita inaintea fisierului D406 (macheta c4-04): categoria (1 conturi, 2 parteneri, 3 note), codul, mesajul si referinta.</summary>
public sealed record SaftIssueDto(byte Category, string Code, string Message, string Reference);

public sealed record SaftSummary(int Entries, int Lines, decimal TotalDebit, decimal TotalCredit, int Partners, int Invoices, int Hash);

public sealed record SaftAccount(string Code, string Name, string? SaftAccountCode);

public sealed record SaftPartner(int Id, string Code, string Name, string? FiscalCode, byte PartnerType, string CountryCode, string? City, string? Address,
    bool IsCustomer, bool IsSupplier);

public sealed record SaftVatRate(string Code, decimal Percent, string Category, DateTime ValidFrom, DateTime? ValidTo);

public sealed record SaftJournal(int Id, string Code, string Name);

public sealed record SaftMasterData(IReadOnlyList<SaftAccount> Accounts, IReadOnlyList<SaftPartner> Partners, IReadOnlyList<SaftVatRate> VatRates,
    IReadOnlyList<SaftJournal> Journals);

public sealed record SaftLedgerLine(long EntryId, int? Number, DateTime AccountingDate, DateTime EntryDate, byte SourceKind, string? EntryDescription,
    string? SourceDocumentNumber, int CreatedBy, int LineNumber, string AccountCode, byte Side, decimal AmountRon, string? CurrencyCode, decimal? AmountCurrency,
    int? PartnerId, string? Description);

public sealed record SaftInvoiceRow(long Id, string SourceDocumentType, long SourceDocumentId, string DocumentNumber, DateTime DocumentDate, int? PartnerId,
    string OperationTypeCode, string VatRateCode, decimal VatRatePercent, decimal BaseRon, decimal VatRon, bool IsReversal);

/// <summary>Citirile D406 prin fin.usp_Saft_* (NET-06, NET-12: paginate, ca fisierul sa se scrie in flux).</summary>
public interface ISaftStore
{
    Task<SaftSummary> SummaryAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<SaftIssueDto>> CheckAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<SaftMasterData> MasterDataAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<SaftLedgerLine>> LedgerPageAsync(int companyId, int journalId, DateOnly from, DateOnly to, long afterEntryId, int afterLine, int take,
        CancellationToken ct);

    Task<IReadOnlyList<SaftInvoiceRow>> InvoicePageAsync(int companyId, byte direction, DateOnly from, DateOnly to, long afterId, int take, CancellationToken ct);

    /// <summary>Salveaza fisierul generat din flux (fara sa-l incarce in memorie).</summary>
    Task SaveFileAsync(int companyId, int returnId, string fileName, Stream content, int userId, CancellationToken ct);
}

/// <summary>Un generator care scrie si salveaza singur fisierul, in flux (D406, C4 3.5).</summary>
public interface IStreamingTaxFileGenerator
{
    string FormCode { get; }

    Task GenerateAsync(int companyId, TaxReturnDto taxReturn, int userId, CancellationToken ct);
}

/// <summary>Rezumatul D406 ca randuri ale declaratiei (C4 3.5): notele, liniile, rulajele, partenerii si facturile perioadei.</summary>
public static class SaftComputation
{
    public static TaxComputation Lines(SaftSummary s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new TaxComputation(
        [
            new TaxLine("ENTRIES", "VALUE", "Note contabile", 1, s.Entries), new TaxLine("LINES", "VALUE", "Linii de note", 2, s.Lines),
            new TaxLine("DEBIT", "VALUE", "Total debit", 3, s.TotalDebit), new TaxLine("CREDIT", "VALUE", "Total credit", 4, s.TotalCredit),
            new TaxLine("PARTNERS", "VALUE", "Parteneri", 5, s.Partners), new TaxLine("INVOICES", "VALUE", "Facturi", 6, s.Invoices),
        ], 0, 0);
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TaxView)]
public sealed record GetSaftChecks(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<SaftIssueDto>>;

internal sealed class GetSaftChecksHandler(ISaftStore store, ICurrentContext context) : IQueryHandler<GetSaftChecks, IReadOnlyList<SaftIssueDto>>
{
    public Task<IReadOnlyList<SaftIssueDto>> Handle(GetSaftChecks request, CancellationToken cancellationToken) =>
        store.CheckAsync(context.CompanyId, request.From, request.To, cancellationToken);
}
