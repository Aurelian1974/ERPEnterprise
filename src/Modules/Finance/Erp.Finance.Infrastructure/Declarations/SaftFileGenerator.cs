using System.Globalization;
using System.Text;
using System.Xml;
using Erp.Core.Contracts;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Contracts.Reporting;

namespace Erp.Finance.Infrastructure.Declarations;

/// <summary>
/// Fisierul D406 SAF-T lunar sau trimestrial (C4 3.5, 4.3): verificarile (conturile cu rulaj au cont SAF-T, 51951; partenerii au cod fiscal si,
/// in UE, cod de TVA, 51952), apoi antetul, nomenclatoarele (conturile cu soldurile din balanta, clientii, furnizorii, cotele), registrul pe
/// jurnale si facturile emise si primite din registrul de TVA. Se scrie in flux (XmlWriter intr-un fisier temporar, citire paginata, NET-12) si
/// se salveaza tot in flux; memoria nu depinde de numarul de linii. Validarea cu DUKIntegrator vine de la Integrari (02.14).
/// </summary>
public sealed class SaftFileGenerator(ISaftStore store, IBalanceQuery balances, ICompanyDirectory companies) : IStreamingTaxFileGenerator
{
    public const string Namespace = "mfp:anaf:dgti:d406:declaratie:v1";
    private const int PageSize = 5000;

    public string FormCode => "D406";

    public async Task GenerateAsync(int companyId, TaxReturnDto taxReturn, int userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taxReturn);
        var (from, to) = (taxReturn.PeriodFrom, taxReturn.PeriodTo);
        var issues = await store.CheckAsync(companyId, from, to, ct).ConfigureAwait(false);
        if (issues.FirstOrDefault(i => i.Category is 1 or 2) is { } issue)
        {
            throw new ErpException(ErpErrorKind.Validation, issue.Code, $"{issue.Message} ({issues.Count(i => i.Category is 1 or 2)} probleme de rezolvat înainte de D406)");
        }

        var company = await companies.GetAtAsync(companyId, to, ct).ConfigureAwait(false);
        var path = Path.GetTempFileName();
        try
        {
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await WriteAsync(file, companyId, company, from, to, ct).ConfigureAwait(false);
            }

            await using var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            await store.SaveFileAsync(companyId, taxReturn.Id, $"D406_{company.FiscalCode}_{from:yyyy-MM}.xml", read, userId, ct).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    public async Task WriteAsync(Stream output, int companyId, CompanySnapshotDto company, DateOnly from, DateOnly to, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(company);
        var master = await store.MasterDataAsync(companyId, from, to, ct).ConfigureAwait(false);
        var summary = await store.SummaryAsync(companyId, from, to, ct).ConfigureAwait(false);
        var balance = (await balances.GetBalanceAsync(new BalanceRequest(companyId, null, from, to, 3, true, null, false, false, false), ct).ConfigureAwait(false))
            .ToDictionary(r => r.AccountCode, StringComparer.Ordinal);
        var partners = master.Partners.ToDictionary(p => p.Id);
        using var xml = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true });
        xml.WriteStartDocument();
        xml.WriteStartElement("AuditFile", Namespace);
        WriteHeader(xml, company, from, to);

        xml.WriteStartElement("MasterFiles", Namespace);
        xml.WriteStartElement("GeneralLedgerAccounts", Namespace);
        foreach (var a in master.Accounts.Where(a => balance.ContainsKey(a.Code)).OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            var b = balance[a.Code];
            var opening = b.OpeningDebit + b.PriorDebit - b.OpeningCredit - b.PriorCredit;
            var closing = b.ClosingDebit - b.ClosingCredit;
            xml.WriteStartElement("Account", Namespace);
            Element(xml, "AccountID", a.Code);
            Element(xml, "AccountDescription", a.Name);
            Element(xml, "StandardAccountID", a.SaftAccountCode ?? a.Code);
            Element(xml, "AccountType", "Bifunctional");
            Element(xml, opening >= 0 ? "OpeningDebitBalance" : "OpeningCreditBalance", Amount(Math.Abs(opening)));
            Element(xml, closing >= 0 ? "ClosingDebitBalance" : "ClosingCreditBalance", Amount(Math.Abs(closing)));
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        WriteParties(xml, "Customers", "Customer", "CustomerID", master.Partners.Where(p => p.IsCustomer || !p.IsSupplier));
        WriteParties(xml, "Suppliers", "Supplier", "SupplierID", master.Partners.Where(p => p.IsSupplier));
        xml.WriteStartElement("TaxTable", Namespace);
        xml.WriteStartElement("TaxTableEntry", Namespace);
        Element(xml, "TaxType", "300");
        Element(xml, "Description", "Taxa pe valoarea adaugata");
        foreach (var r in master.VatRates)
        {
            xml.WriteStartElement("TaxCodeDetails", Namespace);
            Element(xml, "TaxCode", r.Code);
            Element(xml, "EffectiveDate", r.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Element(xml, "Description", $"{r.Category} {r.Percent.ToString("0.##", CultureInfo.InvariantCulture)}%");
            Element(xml, "TaxPercentage", r.Percent.ToString("0.00", CultureInfo.InvariantCulture));
            Element(xml, "Country", "RO");
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();

        xml.WriteStartElement("GeneralLedgerEntries", Namespace);
        Element(xml, "NumberOfEntries", summary.Entries.ToString(CultureInfo.InvariantCulture));
        Element(xml, "TotalDebit", Amount(summary.TotalDebit));
        Element(xml, "TotalCredit", Amount(summary.TotalCredit));
        foreach (var journal in master.Journals)
        {
            xml.WriteStartElement("Journal", Namespace);
            Element(xml, "JournalID", journal.Code);
            Element(xml, "Description", journal.Name);
            Element(xml, "Type", journal.Code);
            await WriteJournalAsync(xml, companyId, journal.Id, from, to, partners, ct).ConfigureAwait(false);
            xml.WriteEndElement();
            xml.Flush();
        }

        xml.WriteEndElement();

        xml.WriteStartElement("SourceDocuments", Namespace);
        await WriteInvoicesAsync(xml, companyId, 1, from, to, partners, ct).ConfigureAwait(false);
        await WriteInvoicesAsync(xml, companyId, 2, from, to, partners, ct).ConfigureAwait(false);
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndDocument();
        xml.Flush();
    }

    private static void WriteHeader(XmlWriter xml, CompanySnapshotDto company, DateOnly from, DateOnly to)
    {
        var months = (to.Year - from.Year) * 12 + to.Month - from.Month + 1;
        xml.WriteStartElement("Header", Namespace);
        Element(xml, "AuditFileVersion", "2.4.8");
        Element(xml, "AuditFileCountry", "RO");
        Element(xml, "AuditFileDateCreated", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Element(xml, "SoftwareCompanyName", "ERPEnterprise");
        Element(xml, "SoftwareID", "ERPEnterprise");
        Element(xml, "SoftwareVersion", "1.0");
        xml.WriteStartElement("Company", Namespace);
        Element(xml, "RegistrationNumber", company.FiscalCode);
        Element(xml, "Name", company.Name);
        WriteAddress(xml, company.Address, company.City, company.CountryCode);
        xml.WriteEndElement();
        Element(xml, "DefaultCurrencyCode", "RON");
        xml.WriteStartElement("SelectionCriteria", Namespace);
        Element(xml, "PeriodStart", from.Month.ToString(CultureInfo.InvariantCulture));
        Element(xml, "PeriodStartYear", from.Year.ToString(CultureInfo.InvariantCulture));
        Element(xml, "PeriodEnd", to.Month.ToString(CultureInfo.InvariantCulture));
        Element(xml, "PeriodEndYear", to.Year.ToString(CultureInfo.InvariantCulture));
        xml.WriteEndElement();
        Element(xml, "HeaderComment", months == 1 ? "L" : "T");
        Element(xml, "SegmentIndex", "1");
        Element(xml, "TotalSegmentsInsequence", "1");
        Element(xml, "TaxAccountingBasis", "A");
        xml.WriteEndElement();
    }

    private static void WriteParties(XmlWriter xml, string list, string item, string idElement, IEnumerable<SaftPartner> partners)
    {
        xml.WriteStartElement(list, Namespace);
        foreach (var p in partners)
        {
            xml.WriteStartElement(item, Namespace);
            xml.WriteStartElement("CompanyStructure", Namespace);
            Element(xml, "RegistrationNumber", PartyCode(p));
            Element(xml, "Name", p.Name);
            WriteAddress(xml, p.Address, p.City, p.CountryCode);
            xml.WriteEndElement();
            Element(xml, idElement, p.Code);
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    private async Task WriteJournalAsync(XmlWriter xml, int companyId, int journalId, DateOnly from, DateOnly to, Dictionary<int, SaftPartner> partners,
        CancellationToken ct)
    {
        long afterEntry = 0;
        var afterLine = 0;
        long open = -1;
        while (true)
        {
            var page = await store.LedgerPageAsync(companyId, journalId, from, to, afterEntry, afterLine, PageSize, ct).ConfigureAwait(false);
            foreach (var l in page)
            {
                if (l.EntryId != open)
                {
                    if (open != -1)
                    {
                        xml.WriteEndElement();
                    }

                    open = l.EntryId;
                    xml.WriteStartElement("Transaction", Namespace);
                    Element(xml, "TransactionID", l.Number?.ToString(CultureInfo.InvariantCulture) ?? l.EntryId.ToString(CultureInfo.InvariantCulture));
                    Element(xml, "Period", l.AccountingDate.Month.ToString(CultureInfo.InvariantCulture));
                    Element(xml, "PeriodYear", l.AccountingDate.Year.ToString(CultureInfo.InvariantCulture));
                    Element(xml, "TransactionDate", l.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    Element(xml, "SourceID", l.CreatedBy.ToString(CultureInfo.InvariantCulture));
                    Element(xml, "TransactionType", l.SourceKind.ToString(CultureInfo.InvariantCulture));
                    Element(xml, "Description", l.EntryDescription ?? l.SourceDocumentNumber ?? "-");
                    Element(xml, "SystemEntryDate", l.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    Element(xml, "GLPostingDate", l.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }

                xml.WriteStartElement("TransactionLine", Namespace);
                Element(xml, "RecordID", l.LineNumber.ToString(CultureInfo.InvariantCulture));
                Element(xml, "AccountID", l.AccountCode);
                if (l.PartnerId is { } partnerId && partners.TryGetValue(partnerId, out var partner))
                {
                    Element(xml, partner.IsSupplier && !partner.IsCustomer ? "SupplierID" : "CustomerID", partner.Code);
                }

                Element(xml, "Description", l.Description ?? l.EntryDescription ?? "-");
                xml.WriteStartElement(l.Side == 1 ? "DebitAmount" : "CreditAmount", Namespace);
                Element(xml, "Amount", Amount(l.AmountRon));
                if (l.CurrencyCode is { } currency && l.AmountCurrency is { } amountCurrency)
                {
                    Element(xml, "CurrencyCode", currency);
                    Element(xml, "CurrencyAmount", Amount(amountCurrency));
                }

                xml.WriteEndElement();
                xml.WriteEndElement();
            }

            if (page.Count < PageSize)
            {
                break;
            }

            (afterEntry, afterLine) = (page[^1].EntryId, page[^1].LineNumber);
            xml.Flush();
        }

        if (open != -1)
        {
            xml.WriteEndElement();
        }
    }

    private async Task WriteInvoicesAsync(XmlWriter xml, int companyId, byte direction, DateOnly from, DateOnly to, Dictionary<int, SaftPartner> partners,
        CancellationToken ct)
    {
        xml.WriteStartElement(direction == 1 ? "SalesInvoices" : "PurchaseInvoices", Namespace);
        long after = 0;
        string? open = null;
        var lineNo = 0;
        while (true)
        {
            var page = await store.InvoicePageAsync(companyId, direction, from, to, after, PageSize, ct).ConfigureAwait(false);
            foreach (var r in page)
            {
                var key = $"{r.SourceDocumentType}|{r.SourceDocumentId}";
                if (key != open)
                {
                    if (open is not null)
                    {
                        xml.WriteEndElement();
                    }

                    open = key;
                    lineNo = 0;
                    xml.WriteStartElement("Invoice", Namespace);
                    Element(xml, "InvoiceNo", r.DocumentNumber);
                    if (r.PartnerId is { } partnerId && partners.TryGetValue(partnerId, out var partner))
                    {
                        xml.WriteStartElement(direction == 1 ? "CustomerInfo" : "SupplierInfo", Namespace);
                        Element(xml, direction == 1 ? "CustomerID" : "SupplierID", partner.Code);
                        xml.WriteEndElement();
                    }

                    Element(xml, "InvoiceDate", r.DocumentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    Element(xml, "InvoiceType", r.IsReversal ? "381" : "380");
                }

                xml.WriteStartElement("Line", Namespace);
                Element(xml, "LineNumber", (++lineNo).ToString(CultureInfo.InvariantCulture));
                Element(xml, "Description", r.OperationTypeCode);
                xml.WriteStartElement("InvoiceLineAmount", Namespace);
                Element(xml, "Amount", Amount(r.BaseRon));
                xml.WriteEndElement();
                Element(xml, "DebitCreditIndicator", direction == 1 ? "C" : "D");
                xml.WriteStartElement("TaxInformation", Namespace);
                Element(xml, "TaxType", "300");
                Element(xml, "TaxCode", r.VatRateCode);
                Element(xml, "TaxPercentage", r.VatRatePercent.ToString("0.00", CultureInfo.InvariantCulture));
                Element(xml, "TaxBase", Amount(r.BaseRon));
                xml.WriteStartElement("TaxAmount", Namespace);
                Element(xml, "Amount", Amount(r.VatRon));
                xml.WriteEndElement();
                xml.WriteEndElement();
                xml.WriteEndElement();
            }

            if (page.Count < PageSize)
            {
                break;
            }

            after = page[^1].Id;
            xml.Flush();
        }

        if (open is not null)
        {
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    private static string PartyCode(SaftPartner p) =>
        p.PartnerType == 4 && string.IsNullOrWhiteSpace(p.FiscalCode) ? "0000000000000" : p.FiscalCode ?? string.Empty;

    private static void WriteAddress(XmlWriter xml, string? street, string? city, string country)
    {
        xml.WriteStartElement("Address", Namespace);
        Element(xml, "StreetName", street ?? "-");
        Element(xml, "City", city ?? "-");
        Element(xml, "Country", country);
        xml.WriteEndElement();
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static void Element(XmlWriter xml, string name, string value) => xml.WriteElementString(name, Namespace, value);
}
