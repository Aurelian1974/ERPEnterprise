using System.Globalization;
using System.Text;
using System.Xml;
using Erp.Core.Contracts;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Declarations;

namespace Erp.Finance.Infrastructure.Declarations;

/// <summary>
/// Fisierul XML al decontului de TVA (C4 3.4, 4.1 pasul 4): antetul firmei la sfarsitul perioadei, luna, anul, tipul decontului dupa
/// lungimea perioadei si randurile formularului (R{rand}_1 baza, R{rand}_2 TVA, in lei intregi). Validarea cu DUKIntegrator si PDF-ul cu XML-ul
/// atasat vin de la Integrari (02.14); pana atunci XML-ul se valideaza doar structural.
/// </summary>
public sealed class D300XmlGenerator(ICompanyDirectory companies) : ITaxReturnFileGenerator
{
    public const string Namespace = "mfp:anaf:dgti:d300:declaratie:v11";

    public string FormCode => "D300";

    public async Task<TaxFile> GenerateAsync(int companyId, TaxReturnDto taxReturn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taxReturn);
        var company = await companies.GetAtAsync(companyId, taxReturn.PeriodTo, ct).ConfigureAwait(false);
        var lines = taxReturn.Lines.Select(l => new TaxLine(l.RowCode, l.ColumnCode, l.Label, 0, Money.ParseOptional(l.ManualValue ?? l.CalculatedValue) ?? 0));
        var content = Write(company, taxReturn.PeriodFrom, taxReturn.PeriodTo, D300Layout.Aggregate(lines));
        return new TaxFile($"D300_{company.FiscalCode}_{taxReturn.PeriodFrom:yyyy-MM}.xml", content);
    }

    public static byte[] Write(CompanySnapshotDto company, DateOnly from, DateOnly to, IReadOnlyDictionary<int, (decimal Base, decimal Vat)> rows)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(rows);
        var months = (to.Year - from.Year) * 12 + to.Month - from.Month + 1;
        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("declaratie300", Namespace);
            Attr(xml, "luna", to.Month);
            Attr(xml, "an", to.Year);
            xml.WriteAttributeString("tip_decont", months switch { 1 => "L", 3 => "T", 6 => "S", _ => "A" });
            xml.WriteAttributeString("cui", company.FiscalCode);
            xml.WriteAttributeString("den", company.Name);
            xml.WriteAttributeString("adresa", $"{company.Address}, {company.City}");
            foreach (var (row, value) in rows)
            {
                if (value.Base != 0)
                {
                    Attr(xml, $"R{row}_1", Lei(value.Base));
                }

                if (value.Vat != 0 || row is D300Layout.DueRow or D300Layout.RefundRow)
                {
                    Attr(xml, $"R{row}_2", Lei(value.Vat));
                }
            }

            Attr(xml, "totalPlata_A", rows.Sum(r => Lei(r.Value.Base) + Lei(r.Value.Vat)));
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        return stream.ToArray();
    }

    private static long Lei(decimal value) => (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static void Attr(XmlWriter xml, string name, long value) => xml.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));
}
