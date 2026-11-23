using System.Globalization;
using Erp.Core.Contracts;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>
/// PV-ul de compensare tiparit (02.02, F3 3.4, 4.4 pasul 2; Codul civil, art. 1616–1623): firma, data si numarul, partidele stinse pe fiecare
/// parte cu sumele, totalul si locurile pentru semnaturile partilor. QuestPDF, licenta din configurarea instalarii.
/// </summary>
public sealed class CompensationPrinter : ICompensationPrinter
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    public byte[] Print(CompensationDetailDto compensation, CompanySnapshotDto company)
    {
        ArgumentNullException.ThrowIfNull(compensation);
        ArgumentNullException.ThrowIfNull(company);
        static string Amount(string value) => decimal.Parse(value, CultureInfo.InvariantCulture).ToString("N2", Romanian);
        var partners = compensation.Lines.Select(l => l.PartnerName).Distinct(StringComparer.Ordinal).ToList();
        var receivables = compensation.Lines.Where(l => l.Role == PartnerRole.Customer).ToList();
        var payables = compensation.Lines.Where(l => l.Role == PartnerRole.Supplier).ToList();

        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10));
            page.Header().Column(c =>
            {
                c.Item().Text(company.Name).Bold().FontSize(12);
                c.Item().Text($"CUI {company.FiscalCode} · {company.TradeRegisterNo} · {company.Address}, {company.City}");
                c.Item().PaddingTop(16).AlignCenter().Text("PROCES-VERBAL DE COMPENSARE").Bold().FontSize(14);
                c.Item().AlignCenter().Text($"nr. {compensation.Number ?? "(ciornă)"} din {compensation.DocumentDate.ToString("dd.MM.yyyy", Romanian)}");
            });
            page.Content().PaddingTop(16).Column(c =>
            {
                c.Spacing(10);
                c.Item().Text($"Încheiat între {company.Name} și {string.Join(", ", partners)}, prin care părțile își sting reciproc creanțele și datoriile de mai jos, " +
                    $"în sumă de {Amount(compensation.TotalAmount)} {compensation.CurrencyCode}.");
                Section(c, "Creanțe stinse (ne datorează)", receivables);
                Section(c, "Datorii stinse (le datorăm)", payables);
                c.Item().Text($"Total compensat: {Amount(compensation.TotalAmount)} {compensation.CurrencyCode}").Bold();
                c.Item().PaddingTop(30).Row(r =>
                {
                    r.RelativeItem().Column(s => { s.Item().Text(company.Name).Bold(); s.Item().PaddingTop(30).Text("Semnătura și ștampila"); });
                    foreach (var partner in partners)
                    {
                        r.RelativeItem().Column(s => { s.Item().Text(partner).Bold(); s.Item().PaddingTop(30).Text("Semnătura și ștampila"); });
                    }
                });
            });
            page.Footer().AlignRight().Text(t => { t.Span("Pagina "); t.CurrentPageNumber(); t.Span(" din "); t.TotalPages(); });
        })).GeneratePdf();

        void Section(ColumnDescriptor column, string title, IReadOnlyList<CompensationLineDto> lines)
        {
            if (lines.Count == 0)
            {
                return;
            }

            column.Item().Text(title).Bold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(cols => { cols.RelativeColumn(3); cols.RelativeColumn(2); cols.RelativeColumn(2); cols.RelativeColumn(2); cols.RelativeColumn(2); });
                table.Header(h =>
                {
                    foreach (var head in new[] { "Partener", "Document", "Data", "Valoare", "Se stinge" })
                    {
                        h.Cell().BorderBottom(1).Padding(3).Text(head).Bold();
                    }
                });
                foreach (var l in lines)
                {
                    table.Cell().Padding(3).Text(l.PartnerName);
                    table.Cell().Padding(3).Text(l.DocumentNo);
                    table.Cell().Padding(3).Text(l.DocumentDate.ToString("dd.MM.yyyy", Romanian));
                    table.Cell().Padding(3).AlignRight().Text(Amount(l.ItemAmount.TrimStart('-')));
                    table.Cell().Padding(3).AlignRight().Text(Amount(l.Amount.TrimStart('-')));
                }
            });
        }
    }
}
