using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank.Formats;

/// <summary>
/// Extrasul ISO 20022 camt.053 (versiunile 001.02–001.13; F2 3.4). Fisierul e nesigur (SEC-01, SEC-05): fara DTD, fara entitati
/// externe, fara resolver; un document cu DTD e refuzat inainte de a fi procesat. Elementele se cauta dupa numele local, ca sa
/// accepte orice versiune a spatiului de nume.
/// </summary>
public sealed class Camt053Parser : IStatementParser
{
    public StatementFormat Format => StatementFormat.Camt053;

    internal static XDocument LoadSafe(byte[] content)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = BankStatementRules.MaxFileBytes * 2L,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            throw new StatementFormatException(ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase)
                ? "Fișierul conține DTD sau entități externe și nu este acceptat."
                : "Fișierul nu este un XML valid.", ex.LineNumber == 0 ? null : ex.LineNumber);
        }
    }

    public IReadOnlyList<ParsedStatement> Parse(byte[] content, string? mapping)
    {
        var document = LoadSafe(content);
        var root = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "BkToCstmrStmt")
            ?? throw new StatementFormatException("Fișierul nu este un extras camt.053 (lipsește BkToCstmrStmt).");
        var statements = root.Elements().Where(e => e.Name.LocalName == "Stmt").Select(ParseStatement).ToList();
        return statements.Count > 0 ? statements : throw new StatementFormatException("Fișierul nu conține niciun extras (Stmt).");
    }

    private static ParsedStatement ParseStatement(XElement stmt)
    {
        var account = stmt.Child("Acct");
        var iban = BankStatementRules.NormalizeIban(account?.Child("Id")?.Child("IBAN")?.Value);
        var currency = account?.Child("Ccy")?.Value.Trim();
        var statementNo = (stmt.Child("ElctrncSeqNb") ?? stmt.Child("LglSeqNb"))?.Value.Trim() ?? stmt.Child("Id")?.Value.Trim();
        if (statementNo is { Length: > 35 })
        {
            statementNo = statementNo[..35];
        }

        decimal? opening = null, closing = null;
        DateOnly? closingDate = null, openingDate = null;
        foreach (var balance in stmt.Elements().Where(e => e.Name.LocalName == "Bal"))
        {
            var code = balance.Child("Tp")?.Child("CdOrPrtry")?.Child("Cd")?.Value.Trim();
            var amount = SignedAmount(balance);
            var date = DateOf(balance.Child("Dt"));
            if (code is "OPBD" or "PRCD" && opening is null)
            {
                opening = amount;
                openingDate = date;
            }
            else if (code == "CLBD")
            {
                closing = amount;
                closingDate = date;
            }

            currency ??= balance.Child("Amt")?.Attribute("Ccy")?.Value;
        }

        if (opening is null || closing is null)
        {
            throw new StatementFormatException("Extrasul nu are soldul inițial (OPBD/PRCD) și soldul final (CLBD).", Line(stmt));
        }

        var lines = stmt.Elements().Where(e => e.Name.LocalName == "Ntry").Where(IsBooked).Select(ParseEntry).ToList();
        var range = stmt.Child("FrToDt");
        var from = DateOf(range?.Child("FrDtTm")) ?? lines.Select(l => (DateOnly?)l.BookingDate).Min() ?? closingDate ?? openingDate;
        var to = DateOf(range?.Child("ToDtTm")) ?? lines.Select(l => (DateOnly?)l.BookingDate).Max() ?? closingDate ?? from;
        if (from is null || to is null)
        {
            throw new StatementFormatException("Extrasul nu are intervalul (FrToDt) și nici date pe linii sau solduri.", Line(stmt));
        }

        if (to < from)
        {
            (from, to) = (to, from);
        }

        return new ParsedStatement(iban, statementNo, closingDate ?? to.Value, from.Value, to.Value, currency, opening, closing, lines);
    }

    /// <summary>Doar inregistrarile contabilizate (Sts = BOOK); cele in asteptare (PDNG) sau informative nu intra in sold.</summary>
    private static bool IsBooked(XElement entry)
    {
        var status = entry.Child("Sts");
        var code = (status?.Child("Cd") ?? status)?.Value.Trim();
        return code is null or "" or "BOOK";
    }

    private static ParsedStatementLine ParseEntry(XElement entry)
    {
        var amount = SignedAmount(entry);
        var reversal = string.Equals(entry.Child("RvslInd")?.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        if (amount == 0)
        {
            throw new StatementFormatException("Înregistrare cu suma zero.", Line(entry));
        }

        var booking = DateOf(entry.Child("BookgDt")) ?? DateOf(entry.Child("ValDt"))
            ?? throw new StatementFormatException("Înregistrare fără data contabilă (BookgDt).", Line(entry));
        var details = entry.Child("NtryDtls")?.Elements().Where(e => e.Name.LocalName == "TxDtls").ToList() ?? [];
        var tx = details.Count == 1 ? details[0] : null;
        var incoming = amount > 0;

        // Contrapartida: platitorul la o intrare, beneficiarul la o iesire (o stornare are sensul inversat fata de partile tranzactiei).
        var parties = tx?.Child("RltdPties");
        var agents = tx?.Child("RltdAgts");
        var counterSide = incoming != reversal ? "Dbtr" : "Cdtr";
        var party = parties?.Child(counterSide);
        var name = (party?.Child("Pty") ?? party)?.Child("Nm")?.Value.Trim();
        var iban = BankStatementRules.NormalizeIban(parties?.Child(counterSide + "Acct")?.Child("Id")?.Child("IBAN")?.Value);
        var agent = agents?.Child(counterSide + "Agt")?.Child("FinInstnId");
        var bic = (agent?.Child("BICFI") ?? agent?.Child("BIC"))?.Value.Trim();

        var refs = tx?.Child("Refs");
        var endToEnd = refs?.Child("EndToEndId")?.Value.Trim();
        if (string.IsNullOrEmpty(endToEnd) || endToEnd == "NOTPROVIDED")
        {
            endToEnd = null;
        }

        var reference = entry.Child("AcctSvcrRef")?.Value.Trim() ?? refs?.Child("AcctSvcrRef")?.Value.Trim();
        var remittance = Remittance(tx?.Child("RmtInf")) ?? entry.Child("AddtlNtryInf")?.Value.Trim();
        var code = BankTransactionCode(entry.Child("BkTxCd") ?? tx?.Child("BkTxCd"));
        var instructed = tx?.Child("AmtDtls")?.Child("InstdAmt")?.Child("Amt");
        var foreignCurrency = instructed?.Attribute("Ccy")?.Value;
        var entryCurrency = entry.Child("Amt")?.Attribute("Ccy")?.Value;
        decimal? foreign = instructed is not null && foreignCurrency != entryCurrency ? Amount(instructed) : null;

        return new ParsedStatementLine(booking, DateOf(entry.Child("ValDt")), amount, Cut(name, 200), iban, Cut(bic, 11), Cut(remittance, 500),
            Cut(endToEnd, 35), string.IsNullOrEmpty(reference) ? null : reference, Cut(code, 35), foreign, foreign is null ? null : foreignCurrency);
    }

    private static string? Remittance(XElement? info)
    {
        if (info is null)
        {
            return null;
        }

        var parts = info.Elements().Where(e => e.Name.LocalName == "Ustrd").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
        parts.AddRange(info.Descendants().Where(e => e.Name.LocalName == "Ref" && e.Parent?.Name.LocalName == "CdtrRefInf").Select(e => e.Value.Trim()));
        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static string? BankTransactionCode(XElement? code)
    {
        var domain = code?.Child("Domn");
        if (domain is not null)
        {
            var family = domain.Child("Fmly");
            return string.Join('/', new[] { domain.Child("Cd")?.Value, family?.Child("Cd")?.Value, family?.Child("SubFmlyCd")?.Value }
                .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));
        }

        return code?.Child("Prtry")?.Child("Cd")?.Value.Trim();
    }

    private static decimal SignedAmount(XElement element)
    {
        var amount = Amount(element.Child("Amt") ?? throw new StatementFormatException("Lipsește suma (Amt).", Line(element)));
        var indicator = element.Child("CdtDbtInd")?.Value.Trim();
        return indicator switch
        {
            "CRDT" => amount,
            "DBIT" => -amount,
            _ => throw new StatementFormatException("Indicatorul credit/debit (CdtDbtInd) lipsește sau nu e CRDT/DBIT.", Line(element)),
        };
    }

    private static decimal Amount(XElement amount) =>
        decimal.TryParse(amount.Value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? Math.Round(value, 2, MidpointRounding.AwayFromZero) == value ? value : throw new StatementFormatException("Suma are mai mult de 2 zecimale.", Line(amount))
            : throw new StatementFormatException($"Suma „{amount.Value}” nu este validă.", Line(amount));

    /// <summary>Data din Dt sau DtTm (cu sau fara fus orar): ziua calendaristica din fisier.</summary>
    private static DateOnly? DateOf(XElement? element)
    {
        var value = (element?.Child("Dt") ?? element?.Child("DtTm") ?? element)?.Value.Trim();
        if (string.IsNullOrEmpty(value) || element is null || element.HasElements && element.Child("Dt") is null && element.Child("DtTm") is null)
        {
            return null;
        }

        return value.Length >= 10 && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new StatementFormatException($"Data „{value}” nu este validă.", Line(element));
    }

    private static string? Cut(string? value, int max) => string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

    private static int? Line(XElement element) => element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : null;
}

internal static class XmlExtensions
{
    public static XElement? Child(this XElement element, string localName) => element.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
}
