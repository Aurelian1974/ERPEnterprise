using System.Globalization;
using Erp.Core.Contracts;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>
/// Confirmarea de sold tiparita (02.02, F3 3.9, 4.6 pasul 1): firma, partenerul, data soldului, partidele deschise la acea data (in moneda lor si
/// in lei), soldul si partea de raspuns a partenerului (confirmam / nu confirmam, soldul din evidentele lui, semnatura). QuestPDF.
/// </summary>
public sealed class BalanceConfirmationPrinter : IBalanceConfirmationPrinter
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    public byte[] Print(BalanceConfirmationDto confirmation, IReadOnlyList<OpenItemAsOf> items, CompanySnapshotDto company)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(company);
        static string Amount(decimal value) => value.ToString("N2", Romanian);
        var date = confirmation.AsOfDate.ToString("dd.MM.yyyy", Romanian);
        var balance = decimal.Parse(confirmation.OurBalance, CultureInfo.InvariantCulture);

        // Conventia de semn: la client debitul e creanta noastra, la furnizor creditul e datoria noastra.
        var direction = (confirmation.Role, balance > 0) switch
        {
            (PartnerRole.Customer, true) => "sumă datorată de dumneavoastră către noi",
            (PartnerRole.Customer, false) => "sumă datorată de noi către dumneavoastră (avans sau plată în plus)",
            (_, false) => "sumă datorată de noi către dumneavoastră",
            _ => "sumă datorată de dumneavoastră către noi (avans sau plată în plus)",
        };

        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10));
            page.Header().Column(c =>
            {
                c.Item().Text(company.Name).Bold().FontSize(12);
                c.Item().Text($"CUI {company.FiscalCode} · {company.TradeRegisterNo} · {company.Address}, {company.City}");
                c.Item().PaddingTop(10).Text($"Către: {confirmation.PartnerName}{(confirmation.FiscalCode is null ? string.Empty : $", CUI {confirmation.FiscalCode}")}");
                c.Item().PaddingTop(12).AlignCenter().Text("CONFIRMARE DE SOLD").Bold().FontSize(14);
                c.Item().AlignCenter().Text($"nr. {confirmation.Number} · sold la {date} · {(confirmation.Role == PartnerRole.Customer ? "client" : "furnizor")}");
            });
            page.Content().PaddingTop(14).Column(c =>
            {
                c.Spacing(10);
                c.Item().Text($"Vă rugăm să confirmați că, la data de {date}, în evidențele noastre figurează un sold de {Amount(Math.Abs(balance))} lei, {direction}, " +
                    "compus din documentele de mai jos.");
                c.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        foreach (var head in new[] { "Document", "Data", "Scadența", "Moneda", "Valoare", "Rest de plată", "Rest în lei" })
                        {
                            h.Cell().BorderBottom(1).Padding(3).Text(head).Bold();
                        }
                    });
                    foreach (var i in items)
                    {
                        table.Cell().Padding(3).Text(i.DocumentNo);
                        table.Cell().Padding(3).Text(i.DocumentDate.ToString("dd.MM.yyyy", Romanian));
                        table.Cell().Padding(3).Text(i.DueDate.ToString("dd.MM.yyyy", Romanian));
                        table.Cell().Padding(3).Text(i.CurrencyCode);
                        table.Cell().Padding(3).AlignRight().Text(Amount(i.Amount));
                        table.Cell().Padding(3).AlignRight().Text(Amount(i.OpenAmount));
                        table.Cell().Padding(3).AlignRight().Text(Amount(i.OpenRon));
                    }

                    table.Cell().ColumnSpan(6).BorderTop(1).Padding(3).Text("Sold").Bold();
                    table.Cell().BorderTop(1).Padding(3).AlignRight().Text(Amount(balance)).Bold();
                });
                c.Item().Text("Sumele pozitive sunt în debitul contului dumneavoastră din evidențele noastre, cele negative în credit.").FontSize(8);
                c.Item().PaddingTop(16).Text("Răspunsul partenerului").Bold();
                c.Item().Text("[  ] Confirmăm soldul de mai sus.");
                c.Item().Text("[  ] Nu confirmăm; soldul din evidențele noastre este de ____________________ lei. Anexăm documentele care explică diferența.");
                c.Item().PaddingTop(24).Row(r =>
                {
                    r.RelativeItem().Column(s => { s.Item().Text(company.Name).Bold(); s.Item().PaddingTop(30).Text("Semnătura și ștampila"); });
                    r.RelativeItem().Column(s =>
                    {
                        s.Item().Text(confirmation.PartnerName).Bold();
                        s.Item().PaddingTop(30).Text("Data, semnătura și ștampila");
                    });
                });
            });
            page.Footer().AlignRight().Text(t => { t.Span("Pagina "); t.CurrentPageNumber(); t.Span(" din "); t.TotalPages(); });
        })).GeneratePdf();
    }
}
