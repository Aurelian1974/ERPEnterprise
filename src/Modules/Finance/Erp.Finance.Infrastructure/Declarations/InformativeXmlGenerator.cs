using System.Globalization;
using System.Text;
using System.Xml;
using Erp.Core.Contracts;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Declarations;

/// <summary>
/// Fisierele XML ale declaratiilor informative (C4 4.2): antetul firmei si cate un element pe linia de partener (sectiunea sau codul operatiunii,
/// codul fiscal, tara, denumirea, cota, numarul de documente, baza si TVA-ul in lei intregi). Structura exacta se confirma pe XSD-ul ANAF al
/// versiunii; validarea cu DUKIntegrator vine de la Integrari (02.14).
/// </summary>
public abstract class InformativeXmlGenerator(ICompanyDirectory companies, string formCode, string root, string ns) : ITaxReturnFileGenerator
{
    public string FormCode => formCode;

    public async Task<TaxFile> GenerateAsync(int companyId, TaxReturnDto taxReturn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taxReturn);
        var company = await companies.GetAtAsync(companyId, taxReturn.PeriodTo, ct).ConfigureAwait(false);
        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement(root, ns);
            xml.WriteAttributeString("luna", taxReturn.PeriodTo.Month.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("an", taxReturn.PeriodTo.Year.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("cui", company.FiscalCode);
            xml.WriteAttributeString("den", company.Name);
            xml.WriteAttributeString("adresa", $"{company.Address}, {company.City}");
            foreach (var line in taxReturn.PartnerLines)
            {
                xml.WriteStartElement("op", ns);
                xml.WriteAttributeString("tip", line.Section);
                xml.WriteAttributeString("tara", line.PartnerCountryCode ?? "RO");
                xml.WriteAttributeString("cod", line.PartnerFiscalCode ?? string.Empty);
                xml.WriteAttributeString("den", line.PartnerName ?? string.Empty);
                xml.WriteAttributeString("cota", line.VatRatePercent.ToString("0.##", CultureInfo.InvariantCulture));
                xml.WriteAttributeString("nrFact", line.Documents.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("baza", Lei(line.BaseRon));
                xml.WriteAttributeString("tva", Lei(line.VatRon));
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        return new TaxFile($"{formCode}_{company.FiscalCode}_{taxReturn.PeriodFrom:yyyy-MM}.xml", stream.ToArray());
    }

    private static string Lei(string amount) =>
        ((long)Math.Round(Money.ParseOptional(amount) ?? 0, 0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}

public sealed class D394XmlGenerator(ICompanyDirectory companies) : InformativeXmlGenerator(companies, "D394", "declaratie394", "mfp:anaf:dgti:d394:declaratie:v4");

public sealed class D390XmlGenerator(ICompanyDirectory companies) : InformativeXmlGenerator(companies, "D390", "declaratie390", "mfp:anaf:dgti:d390:declaratie:v3");
