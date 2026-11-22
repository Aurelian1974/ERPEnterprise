using System.Globalization;
using System.Numerics;
using System.Text;

namespace Erp.Finance.Tests.Bank;

/// <summary>O tranzactie dintr-un extras de test: suma cu semn (+ intrare, − iesire).</summary>
public sealed record TestEntry(DateOnly Day, decimal Amount, string? Reference, string? Name = null, string? Iban = null, string? Remittance = null,
    string? EndToEndId = null);

/// <summary>
/// Extrase construite dupa specificatiile publice ISO 20022 camt.053 si SWIFT MT940 (F2-A1: fisierele reale anonimizate de la banci se
/// procura ulterior, decizia din 15.11.2026). IBAN-urile de test au cifrele de control corecte (ISO 13616).
/// </summary>
public static class StatementFiles
{
    private static string Amount(decimal value) => Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);

    public static byte[] Camt053(string iban, string currency, decimal opening, IReadOnlyList<TestEntry> entries, string statementNo = "1",
        string ns = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.02", DateOnly? day = null)
    {
        var closing = opening + entries.Sum(e => e.Amount);
        var date = day ?? entries.Select(e => (DateOnly?)e.Day).Max() ?? new DateOnly(2026, 11, 2);
        var from = entries.Select(e => (DateOnly?)e.Day).Min() ?? date;
        var v8 = !ns.EndsWith(".02", StringComparison.Ordinal);
        var xml = new StringBuilder();
        xml.Append($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="{ns}">
              <BkToCstmrStmt>
                <GrpHdr><MsgId>MSG-{statementNo}</MsgId><CreDtTm>{date:yyyy-MM-dd}T23:00:00</CreDtTm></GrpHdr>
                <Stmt>
                  <Id>STMT-{statementNo}</Id>
                  <ElctrncSeqNb>{statementNo}</ElctrncSeqNb>
                  <CreDtTm>{date:yyyy-MM-dd}T23:00:00</CreDtTm>
                  <FrToDt><FrDtTm>{from:yyyy-MM-dd}T00:00:00</FrDtTm><ToDtTm>{date:yyyy-MM-dd}T23:59:59</ToDtTm></FrToDt>
                  <Acct><Id><IBAN>{iban}</IBAN></Id><Ccy>{currency}</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp><Amt Ccy="{currency}">{Amount(opening)}</Amt><CdtDbtInd>{(opening >= 0 ? "CRDT" : "DBIT")}</CdtDbtInd><Dt><Dt>{from.AddDays(-1):yyyy-MM-dd}</Dt></Dt></Bal>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="{currency}">{Amount(closing)}</Amt><CdtDbtInd>{(closing >= 0 ? "CRDT" : "DBIT")}</CdtDbtInd><Dt><Dt>{date:yyyy-MM-dd}</Dt></Dt></Bal>

            """);
        foreach (var e in entries)
        {
            var side = e.Amount > 0 ? "Dbtr" : "Cdtr";
            var party = e.Name is null ? string.Empty : v8 ? $"<{side}><Pty><Nm>{e.Name}</Nm></Pty></{side}>" : $"<{side}><Nm>{e.Name}</Nm></{side}>";
            var account = e.Iban is null ? string.Empty : $"<{side}Acct><Id><IBAN>{e.Iban}</IBAN></Id></{side}Acct>";
            xml.Append($"""
                      <Ntry>
                        {(e.Reference is null ? string.Empty : $"<NtryRef>{e.Reference}</NtryRef>")}
                        <Amt Ccy="{currency}">{Amount(e.Amount)}</Amt>
                        <CdtDbtInd>{(e.Amount > 0 ? "CRDT" : "DBIT")}</CdtDbtInd>
                        <Sts>{(v8 ? "<Cd>BOOK</Cd>" : "BOOK")}</Sts>
                        <BookgDt><Dt>{e.Day:yyyy-MM-dd}</Dt></BookgDt>
                        <ValDt><Dt>{e.Day:yyyy-MM-dd}</Dt></ValDt>
                        {(e.Reference is null ? string.Empty : $"<AcctSvcrRef>{e.Reference}</AcctSvcrRef>")}
                        <BkTxCd><Domn><Cd>PMNT</Cd><Fmly><Cd>{(e.Amount > 0 ? "RCDT" : "ICDT")}</Cd><SubFmlyCd>ESCT</SubFmlyCd></Fmly></Domn></BkTxCd>
                        <NtryDtls><TxDtls>
                          <Refs><EndToEndId>{e.EndToEndId ?? "NOTPROVIDED"}</EndToEndId></Refs>
                          <RltdPties>{party}{account}</RltdPties>
                          {(e.Remittance is null ? string.Empty : $"<RmtInf><Ustrd>{e.Remittance}</Ustrd></RmtInf>")}
                        </TxDtls></NtryDtls>
                      </Ntry>

                """);
        }

        xml.Append("""
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """);
        return Encoding.UTF8.GetBytes(xml.ToString());
    }

    public static byte[] Mt940(string iban, string currency, decimal opening, IReadOnlyList<TestEntry> entries, string statementNo = "1",
        DateOnly? day = null)
    {
        static string Swift(decimal value) => Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
        var closing = opening + entries.Sum(e => e.Amount);
        var date = day ?? entries.Select(e => (DateOnly?)e.Day).Max() ?? new DateOnly(2026, 11, 2);
        var text = new StringBuilder();
        text.Append("{1:F01RNCBROBUAXXX0000000000}{2:O940}{4:\r\n");
        text.Append($":20:STMT{statementNo}\r\n:25:RNCBROBU/{iban}\r\n:28C:{statementNo}/1\r\n");
        text.Append($":60F:{(opening >= 0 ? 'C' : 'D')}{date.AddDays(-1):yyMMdd}{currency}{Swift(opening)}\r\n");
        foreach (var e in entries)
        {
            text.Append($":61:{e.Day:yyMMdd}{e.Day:MMdd}{(e.Amount > 0 ? 'C' : 'D')}{Swift(e.Amount)}NTRF{e.EndToEndId ?? "NONREF"}");
            text.Append(e.Reference is null ? "\r\n" : $"//{e.Reference}\r\n");
            text.Append($":86:/NAME/{e.Name}/IBAN/{e.Iban}/REMI/{e.Remittance}\r\n");
        }

        text.Append($":62F:{(closing >= 0 ? 'C' : 'D')}{date:yyMMdd}{currency}{Swift(closing)}\r\n-}}");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>Un IBAN romanesc valid (cifre de control ISO 7064 mod 97-10), din codul bancii si 16 caractere de cont.</summary>
    public static string RomanianIban(string bank, string account)
    {
        var bban = bank + account;
        var digits = new StringBuilder();
        foreach (var c in bban + "RO00")
        {
            digits.Append(char.IsLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString());
        }

        var check = 98 - (int)(BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture) % 97);
        return $"RO{check:D2}{bban}";
    }
}
