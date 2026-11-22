using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Infrastructure.Bank.Formats;

namespace Erp.Finance.Tests.Bank;

/// <summary>
/// Fisierul de plati (02.02, F2 3.6 si 4.2; criteriul F2-A7): pain.001.001.03 si .09 valide dupa schemele ISO 20022 publice, cu
/// EndToEndId-urile unice ale OP-urilor, un PmtInf pe data executiei si urgenta; CSV-ul dupa maparea profilului.
/// </summary>
public sealed class PaymentFileWriterTests
{
    private static readonly DateTime Created = new(2026, 11, 22, 10, 15, 30);

    private static PaymentBatchData Batch(string? mapping = null, string currency = "RON") => new(7, 3, "BCR-LEI", currency,
        StatementFiles.RomanianIban("RNCB", "0082004530120001"), "RNCBROBU", PaymentFileFormat.Pain001V03, mapping, 140, "Exemplu Distribuție & Servicii SRL",
        "12345674", "LOT-7", null, 3, 3_750.75m, PaymentBatchStatus.Open, 0, null, null, null, null, null, null, null,
        [
            Order(1, 1_250.50m, new DateOnly(2026, 11, 23), false, "Plata fact. FA-123, FA-130"),
            Order(2, 2_000m, new DateOnly(2026, 11, 23), true, "Plată factura „FV 9” <urgent>"),
            Order(3, 500.25m, new DateOnly(2026, 11, 20), false, new string('X', 200)),
        ], [1, 2, 3, 4, 5, 6, 7, 8]);

    private static PaymentBatchOrder Order(int id, decimal amount, DateOnly date, bool urgent, string remittance) => new(id, $"OP{id:D6}",
        PaymentOrderStatus.Validated, null, PaymentConfirmationStatus.Unconfirmed, Created, PaymentOrderOrigin.PaymentOrders, 10 + id, $"Furnizor {id} SRL",
        StatementFiles.RomanianIban("BTRL", $"013012024567890{id}"), id == 1 ? "BTRLRO22" : null, amount, remittance, $"C1-OP{id}", date, urgent, [1]);

    private static XDocument Validated(byte[] content, string schema)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, Path.Combine(AppContext.BaseDirectory, "Bank", "Schemas", schema));
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas, DtdProcessing = DtdProcessing.Prohibit };
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Severity}: {e.Message} (rândul {e.Exception?.LineNumber})");
        using (var reader = XmlReader.Create(new MemoryStream(content), settings))
        {
            while (reader.Read())
            {
            }
        }

        Assert.Empty(errors);
        return XDocument.Load(new MemoryStream(content));
    }

    [Theory]
    [Trait("Criteriu", "02.02-F2-A7")]
    [InlineData(PaymentFileFormat.Pain001V03, "pain.001.001.03.xsd")]
    [InlineData(PaymentFileFormat.Pain001V09, "pain.001.001.09.xsd")]
    public void The_pain001_file_is_valid_against_the_iso20022_schema_with_unique_end_to_end_ids(PaymentFileFormat format, string schema)
    {
        var file = new Pain001Writer().Write(Batch("""{"batchBooking":false,"chargeBearer":"SLEV"}"""), format, Created);
        var document = Validated(file.Content, schema);

        Assert.Equal(("LOT-7.xml", "application/xml"), (file.FileName, file.ContentType));
        XNamespace ns = document.Root!.Name.Namespace;
        Assert.Equal("3", document.Descendants(ns + "GrpHdr").Single().Element(ns + "NbOfTxs")!.Value);
        Assert.Equal("3750.75", document.Descendants(ns + "GrpHdr").Single().Element(ns + "CtrlSum")!.Value);
        var ids = document.Descendants(ns + "EndToEndId").Select(e => e.Value).ToList();
        Assert.Equal(["C1-OP1", "C1-OP2", "C1-OP3"], ids.Order());
        Assert.Equal(ids.Count, ids.Distinct().Count());
        // OP-ul cu data trecuta se executa azi (22): trei grupuri, 22 normal, 23 normal si 23 urgent.
        var groups = document.Descendants(ns + "PmtInf").ToList();
        Assert.Equal(3, groups.Count);
        Assert.Equal(["HIGH", "NORM", "NORM"], groups.Select(g => g.Descendants(ns + "InstrPrty").Single().Value).OrderBy(v => v));
        Assert.All(document.Descendants(ns + "Ustrd"), u => Assert.True(u.Value.Length <= 140));
        Assert.Contains(document.Descendants(ns + "Ustrd"), u => u.Value == "Plată factura „FV 9” <urgent>");
        Assert.Equal(format == PaymentFileFormat.Pain001V09 ? "BICFI" : "BIC", document.Descendants(ns + "DbtrAgt").First().Descendants().Last().Name.LocalName);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A7")]
    public void A_bank_without_bic_and_a_euro_batch_are_still_valid()
    {
        var batch = Batch(currency: "EUR") with { BankBic = null };
        Validated(new Pain001Writer().Write(batch, PaymentFileFormat.Pain001V03, Created).Content, "pain.001.001.03.xsd");
        Validated(new Pain001Writer().Write(batch, PaymentFileFormat.Pain001V09, Created).Content, "pain.001.001.09.xsd");
    }

    [Fact]
    public void The_csv_file_follows_the_profile_columns_and_quotes_fields()
    {
        var file = new CsvPaymentWriter().Write(Batch("""{"separator":";","columns":["Number","BeneficiaryName","BeneficiaryIban","Amount","RemittanceInfo"]}"""),
            PaymentFileFormat.Csv, Created);
        var lines = Encoding.UTF8.GetString(file.Content).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("LOT-7.csv", file.FileName);
        Assert.Equal("Number;BeneficiaryName;BeneficiaryIban;Amount;RemittanceInfo", lines[0]);
        Assert.StartsWith("OP000001;Furnizor 1 SRL;", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(";1250,50;Plata fact. FA-123, FA-130", lines[1], StringComparison.Ordinal);
        Assert.Equal(4, lines.Length);
    }
}
