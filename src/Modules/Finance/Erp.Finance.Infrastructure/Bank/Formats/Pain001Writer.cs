using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank.Formats;

/// <summary>Optiunile bancii pentru pain.001 din PaymentMapping (F2 3.1): lot sau linie cu linie, ChrgBr.</summary>
public sealed record Pain001Options(bool BatchBooking = true, string? ChargeBearer = null);

/// <summary>
/// Fisierul de plati ISO 20022 pain.001.001.03 sau pain.001.001.09 (F2 3.6, 4.2 pasul 4; F2-A7): un PmtInf pe data executiei si urgenta,
/// o tranzactie pe OP cu EndToEndId-ul lui (acelasi la reexport, ca banca sa refuze dublura). Elementele urmeaza ordinea din schemele
/// ISO; textele se taie la lungimile lor (nume 140, detalii la lungimea maxima a bancii, cel mult 140).
/// </summary>
public sealed class Pain001Writer : IPaymentFileWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool Supports(PaymentFileFormat format) => format is PaymentFileFormat.Pain001V03 or PaymentFileFormat.Pain001V09;

    public PaymentFile Write(PaymentBatchData batch, PaymentFileFormat format, DateTime createdAt)
    {
        var v09 = format == PaymentFileFormat.Pain001V09;
        var options = Options(batch.PaymentMapping);
        var ns = v09 ? "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09" : "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";
        var remittanceLength = Math.Min((int)batch.MaxRemittanceLength, 140);
        var today = DateOnly.FromDateTime(createdAt);
        var groups = batch.Orders.GroupBy(o => (Date: o.RequestedExecutionDate < today ? today : o.RequestedExecutionDate, o.IsUrgent))
            .OrderBy(g => g.Key.Date).ThenByDescending(g => g.Key.IsUrgent).ToList();

        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("Document", ns);
            xml.WriteStartElement("CstmrCdtTrfInitn", ns);

            xml.WriteStartElement("GrpHdr", ns);
            Text(xml, ns, "MsgId", $"{batch.Number}-{batch.ExportCount + 1}", 35);
            xml.WriteElementString("CreDtTm", ns, createdAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            xml.WriteElementString("NbOfTxs", ns, batch.Orders.Count.ToString(CultureInfo.InvariantCulture));
            xml.WriteElementString("CtrlSum", ns, Amount(batch.Orders.Sum(o => o.Amount)));
            xml.WriteStartElement("InitgPty", ns);
            Text(xml, ns, "Nm", batch.CompanyName, 140);
            xml.WriteEndElement();
            xml.WriteEndElement();

            var index = 0;
            foreach (var group in groups)
            {
                var orders = group.ToList();
                xml.WriteStartElement("PmtInf", ns);
                Text(xml, ns, "PmtInfId", $"{batch.Number}-{++index}", 35);
                xml.WriteElementString("PmtMtd", ns, "TRF");
                xml.WriteElementString("BtchBookg", ns, options.BatchBooking ? "true" : "false");
                xml.WriteElementString("NbOfTxs", ns, orders.Count.ToString(CultureInfo.InvariantCulture));
                xml.WriteElementString("CtrlSum", ns, Amount(orders.Sum(o => o.Amount)));
                xml.WriteStartElement("PmtTpInf", ns);
                xml.WriteElementString("InstrPrty", ns, group.Key.IsUrgent ? "HIGH" : "NORM");
                if (batch.CurrencyCode == "EUR")
                {
                    xml.WriteStartElement("SvcLvl", ns);
                    xml.WriteElementString("Cd", ns, "SEPA");
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
                var date = group.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (v09)
                {
                    xml.WriteStartElement("ReqdExctnDt", ns);
                    xml.WriteElementString("Dt", ns, date);
                    xml.WriteEndElement();
                }
                else
                {
                    xml.WriteElementString("ReqdExctnDt", ns, date);
                }

                xml.WriteStartElement("Dbtr", ns);
                Text(xml, ns, "Nm", batch.CompanyName, 140);
                xml.WriteStartElement("Id", ns);
                xml.WriteStartElement("OrgId", ns);
                xml.WriteStartElement("Othr", ns);
                Text(xml, ns, "Id", batch.CompanyFiscalCode, 35);
                xml.WriteEndElement();
                xml.WriteEndElement();
                xml.WriteEndElement();
                xml.WriteEndElement();
                Account(xml, ns, batch.AccountIban!, batch.CurrencyCode);
                Agent(xml, ns, "DbtrAgt", batch.BankBic, v09, required: true);
                if (!string.IsNullOrWhiteSpace(options.ChargeBearer))
                {
                    xml.WriteElementString("ChrgBr", ns, options.ChargeBearer);
                }

                foreach (var o in orders)
                {
                    xml.WriteStartElement("CdtTrfTxInf", ns);
                    xml.WriteStartElement("PmtId", ns);
                    Text(xml, ns, "InstrId", o.Number ?? o.Id.ToString(CultureInfo.InvariantCulture), 35);
                    Text(xml, ns, "EndToEndId", o.EndToEndId ?? "NOTPROVIDED", 35);
                    xml.WriteEndElement();
                    xml.WriteStartElement("Amt", ns);
                    xml.WriteStartElement("InstdAmt", ns);
                    xml.WriteAttributeString("Ccy", batch.CurrencyCode);
                    xml.WriteString(Amount(o.Amount));
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    Agent(xml, ns, "CdtrAgt", o.BeneficiaryBic, v09, required: false);
                    xml.WriteStartElement("Cdtr", ns);
                    Text(xml, ns, "Nm", o.BeneficiaryName, 140);
                    xml.WriteEndElement();
                    Account(xml, ns, o.BeneficiaryIban, null);
                    if (!string.IsNullOrWhiteSpace(o.RemittanceInfo))
                    {
                        xml.WriteStartElement("RmtInf", ns);
                        Text(xml, ns, "Ustrd", o.RemittanceInfo, remittanceLength);
                        xml.WriteEndElement();
                    }

                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        return new PaymentFile($"{batch.Number}.xml", "application/xml", stream.ToArray());
    }

    private static Pain001Options Options(string? mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping))
        {
            return new Pain001Options();
        }

        try
        {
            return JsonSerializer.Deserialize<Pain001Options>(mapping, Json) ?? new Pain001Options();
        }
        catch (JsonException)
        {
            return new Pain001Options();
        }
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static void Text(XmlWriter xml, string ns, string name, string value, int max) =>
        xml.WriteElementString(name, ns, value.Length <= max ? value : value[..max]);

    private static void Account(XmlWriter xml, string ns, string iban, string? currency)
    {
        xml.WriteStartElement(currency is null ? "CdtrAcct" : "DbtrAcct", ns);
        xml.WriteStartElement("Id", ns);
        xml.WriteElementString("IBAN", ns, iban);
        xml.WriteEndElement();
        if (currency is not null)
        {
            xml.WriteElementString("Ccy", ns, currency);
        }

        xml.WriteEndElement();
    }

    /// <summary>Banca dupa BIC (BIC in .03, BICFI in .09); fara BIC, agentul platitorului e „NOTPROVIDED”, iar al beneficiarului lipseste.</summary>
    private static void Agent(XmlWriter xml, string ns, string name, string? bic, bool v09, bool required)
    {
        if (string.IsNullOrWhiteSpace(bic) && !required)
        {
            return;
        }

        xml.WriteStartElement(name, ns);
        xml.WriteStartElement("FinInstnId", ns);
        if (string.IsNullOrWhiteSpace(bic))
        {
            xml.WriteStartElement("Othr", ns);
            xml.WriteElementString("Id", ns, "NOTPROVIDED");
            xml.WriteEndElement();
        }
        else
        {
            xml.WriteElementString(v09 ? "BICFI" : "BIC", ns, bic.Trim().ToUpperInvariant());
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
    }
}
