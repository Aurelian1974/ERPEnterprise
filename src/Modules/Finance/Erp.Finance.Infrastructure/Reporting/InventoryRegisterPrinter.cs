using System.Globalization;
using Erp.Core.Contracts;
using Erp.Finance.Application.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Erp.Finance.Infrastructure.Reporting;

/// <summary>
/// Registrul inventar tiparit (02.02, C5 3.4; OMFP 2861/2009): firma, anul, mentiunea „provizoriu” pe anul neinchis, apoi pe cont elementele
/// (partenerul), partea soldului, valoarea contabila, valoarea de inventar si diferenta, cu totalul contului. QuestPDF, ca PV-ul de compensare.
/// </summary>
public sealed class InventoryRegisterPrinter : IInventoryRegisterPrinter
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    public byte[] Print(CompanySnapshotDto company, int year, bool isFinal, IReadOnlyList<InventoryLine> lines)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(lines);
        static string Amount(decimal v) => v.ToString("N2", Romanian);
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(30);
            page.DefaultTextStyle(t => t.FontSize(9));
            page.Header().Column(c =>
            {
                c.Item().Text(company.Name).Bold().FontSize(12);
                c.Item().Text($"CUI {company.FiscalCode} · {company.TradeRegisterNo} · {company.Address}, {company.City}");
                c.Item().PaddingTop(10).AlignCenter().Text($"REGISTRUL INVENTAR · {year}{(isFinal ? string.Empty : " · provizoriu (anul nu e închis)")}").Bold().FontSize(13);
            });
            page.Content().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1.2f); cols.RelativeColumn(3); cols.RelativeColumn(3); cols.RelativeColumn(0.7f); cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.5f); cols.RelativeColumn(1.3f);
                });
                table.Header(h =>
                {
                    foreach (var head in new[] { "Cont", "Denumire", "Element (partener)", "Sold", "Valoare contabilă", "Valoare de inventar", "Diferență" })
                    {
                        h.Cell().BorderBottom(1).Padding(2).Text(head).Bold();
                    }
                });
                foreach (var account in lines.GroupBy(l => (l.AccountCode, l.AccountName)))
                {
                    foreach (var l in account)
                    {
                        table.Cell().Padding(2).Text(l.AccountCode);
                        table.Cell().Padding(2).Text(l.AccountName);
                        table.Cell().Padding(2).Text(l.PartnerName ?? "—");
                        table.Cell().Padding(2).Text(l.Side == 1 ? "D" : "C");
                        table.Cell().Padding(2).AlignRight().Text(Amount(l.BookValue));
                        table.Cell().Padding(2).AlignRight().Text(Amount(l.InventoryValue));
                        table.Cell().Padding(2).AlignRight().Text(Amount(l.InventoryValue - l.BookValue));
                    }
                }
            });
            page.Footer().Row(r =>
            {
                r.RelativeItem().Text("Întocmit: ____________    Verificat: ____________");
                r.RelativeItem().AlignRight().Text(t => { t.Span("Pagina "); t.CurrentPageNumber(); t.Span(" din "); t.TotalPages(); });
            });
        })).GeneratePdf();
    }
}
