using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank.Formats;

/// <summary>
/// Extrasul SWIFT MT940 (F2 3.4): :20: referinta, :25: contul, :28C: numarul extrasului, :60F:/:60M: soldul initial, :61: liniile,
/// :86: detaliile, :62F:/:62M: soldul final. Un fisier poate avea mai multe extrase. Detaliile :86: se citesc in trei forme folosite de
/// bancile din Romania: subcampuri „?20…?63”, cuvinte cheie „/NAME/…/IBAN/…/REMI/…” sau text liber (cu IBAN-ul gasit in text).
/// </summary>
public sealed partial class Mt940Parser : IStatementParser
{
    public StatementFormat Format => StatementFormat.Mt940;

    public IReadOnlyList<ParsedStatement> Parse(byte[] content, string? mapping)
    {
        var text = Decode(content);
        var fields = Fields(text);
        var statements = new List<ParsedStatement>();
        Builder? current = null;
        foreach (var (tag, value, line) in fields)
        {
            switch (tag)
            {
                case "20":
                    if (current is not null)
                    {
                        statements.Add(current.Build());
                    }

                    current = new Builder(line);
                    break;
                case "25":
                    Require(current, line).Account(value);
                    break;
                case "28C":
                case "28":
                    Require(current, line).StatementNo = Cut(value.Trim(), 35);
                    break;
                case "60F":
                case "60M":
                    var o = Require(current, line);
                    o.Opening ??= Balance(value, line, out _);
                    o.Currency ??= value.Trim().Length >= 10 ? value.Trim().Substring(7, 3) : null;
                    break;
                case "61":
                    Require(current, line).Lines.Add(Entry(value, line));
                    break;
                case "86":
                    var target = Require(current, line);
                    if (target.Lines.Count > 0 && target.Lines[^1].Info is null)
                    {
                        target.Lines[^1] = target.Lines[^1] with { Info = value };
                    }

                    break;
                case "62F":
                case "62M":
                    var b = Require(current, line);
                    b.Closing = Balance(value, line, out var date);
                    b.ClosingDate = date;
                    b.Currency ??= value.Trim().Length >= 10 ? value.Trim().Substring(7, 3) : null;
                    break;
            }
        }

        if (current is not null)
        {
            statements.Add(current.Build());
        }

        return statements.Count > 0 ? statements : throw new StatementFormatException("Fișierul nu conține niciun extras MT940 (:20:).");
    }

    private static string Decode(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    /// <summary>Campurile „:tag:valoare”, cu liniile de continuare lipite de campul lor; antetul SWIFT „{1:…}{4:” si sfarsitul „-}” se ignora.</summary>
    private static List<(string Tag, string Value, int Line)> Fields(string text)
    {
        var result = new List<(string, string, int)>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.StartsWith('{') ? HeaderPrefix().Replace(raw, string.Empty) : raw;
            if (line.Length == 0 || line == "-}" || line == "-")
            {
                continue;
            }

            var match = Tag().Match(line);
            if (match.Success)
            {
                result.Add((match.Groups[1].Value, line[match.Length..], i + 1));
            }
            else if (result.Count > 0)
            {
                var last = result[^1];
                result[^1] = (last.Item1, last.Item2 + "\n" + line, last.Item3);
            }
        }

        return result;
    }

    private static Builder Require(Builder? builder, int line) =>
        builder ?? throw new StatementFormatException("Câmp înainte de începutul extrasului (:20:).", line);

    /// <summary>:60F: / :62F: = D/C, AAMMZZ, moneda, suma cu virgula.</summary>
    private static decimal Balance(string value, int line, out DateOnly date)
    {
        var match = BalanceField().Match(value.Trim());
        if (!match.Success)
        {
            throw new StatementFormatException($"Sold invalid „{value.Trim()}”.", line);
        }

        date = Date(match.Groups[2].Value, line);
        var amount = Amount(match.Groups[4].Value, line);
        return match.Groups[1].Value == "D" ? -amount : amount;
    }

    /// <summary>
    /// :61: = data valutei AAMMZZ, [data contabila LLZZ], C/D/RC/RD, [cod fonduri], suma, tip (N/F/S + 3), referinta clientului,
    /// [//referinta bancii], [linia 2: detalii suplimentare].
    /// </summary>
    private static Line Entry(string value, int line)
    {
        var first = value.Split('\n')[0].Trim();
        var match = EntryField().Match(first);
        if (!match.Success)
        {
            throw new StatementFormatException($"Linie :61: invalidă „{first}”.", line);
        }

        var valueDate = Date(match.Groups["value"].Value, line);
        var booking = valueDate;
        if (match.Groups["entry"].Success)
        {
            var month = int.Parse(match.Groups["entry"].Value[..2], CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups["entry"].Value[2..], CultureInfo.InvariantCulture);
            var year = valueDate.Year;
            if (month == 12 && valueDate.Month == 1)
            {
                year--;
            }
            else if (month == 1 && valueDate.Month == 12)
            {
                year++;
            }

            booking = DateOnly.TryParseExact($"{year:D4}{month:D2}{day:D2}", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b)
                ? b
                : throw new StatementFormatException($"Data contabilă invalidă în :61: „{first}”.", line);
        }

        var mark = match.Groups["mark"].Value;
        var amount = Amount(match.Groups["amount"].Value, line);
        if (amount == 0)
        {
            throw new StatementFormatException("Linie cu suma zero.", line);
        }

        // C = credit (intrare), D = debit (iesire); RC/RD = stornarea unui credit/debit, deci sensul invers.
        var signed = mark is "C" or "RD" ? amount : -amount;
        var bankRef = match.Groups["bank"].Success ? match.Groups["bank"].Value.Trim() : null;
        var customerRef = match.Groups["customer"].Value.Trim();
        return new Line(booking, valueDate, signed, match.Groups["type"].Value, string.IsNullOrEmpty(bankRef) ? null : bankRef,
            customerRef is "" or "NONREF" ? null : customerRef, null);
    }

    private static DateOnly Date(string yymmdd, int line) =>
        DateOnly.TryParseExact(yymmdd, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new StatementFormatException($"Data „{yymmdd}” nu este validă.", line);

    private static decimal Amount(string value, int line)
    {
        var normalized = value.Replace(',', '.');
        if (normalized.EndsWith('.'))
        {
            normalized = normalized[..^1];
        }

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
               && Math.Round(amount, 2) == amount
            ? amount
            : throw new StatementFormatException($"Suma „{value}” nu este validă.", line);
    }

    private static string? Cut(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];

    /// <summary>Detaliile :86: impartite in contrapartida, IBAN, BIC, detalii si EndToEndId.</summary>
    internal static (string? Name, string? Iban, string? Bic, string? Remittance, string? EndToEnd) Details(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
        {
            return (null, null, null, null, null);
        }

        var flat = info.Replace("\n", string.Empty, StringComparison.Ordinal);
        string? name = null, iban = null, bic = null, remittance = null, endToEnd = null;
        if (SubField().IsMatch(flat))
        {
            // Subcampuri ?NN: 20–29 si 60–63 detalii, 30 BIC, 31 IBAN, 32–33 nume.
            var parts = SubField().Split(flat);
            var codes = SubField().Matches(flat).Select(m => m.Value[1..]).ToList();
            var remit = new StringBuilder();
            var names = new StringBuilder();
            for (var i = 0; i < codes.Count; i++)
            {
                var part = parts[i + 1];
                var code = int.Parse(codes[i], CultureInfo.InvariantCulture);
                if (code is >= 20 and <= 29 or >= 60 and <= 63)
                {
                    if (part.StartsWith("EREF+", StringComparison.Ordinal))
                    {
                        endToEnd = part[5..];
                        continue;
                    }

                    remit.Append(part);
                }
                else if (code == 30)
                {
                    bic = part;
                }
                else if (code == 31)
                {
                    iban = part;
                }
                else if (code is 32 or 33)
                {
                    names.Append(part);
                }
            }

            name = names.ToString();
            remittance = remit.ToString();
        }
        else if (flat.StartsWith('/') && KeywordField().IsMatch(flat))
        {
            foreach (Match m in KeywordField().Matches(flat))
            {
                var value = m.Groups[2].Value.Trim();
                switch (m.Groups[1].Value)
                {
                    case "NAME":
                    case "BENM":
                    case "ORDP":
                        name ??= value.StartsWith("/NAME/", StringComparison.Ordinal) ? value[6..] : value;
                        break;
                    case "IBAN":
                    case "ACCW":
                        iban ??= value;
                        break;
                    case "BIC":
                        bic ??= value;
                        break;
                    case "REMI":
                    case "DETL":
                        remittance = remittance is null ? value : remittance + " " + value;
                        break;
                    case "EREF":
                        endToEnd = value;
                        break;
                }
            }
        }
        else
        {
            remittance = flat.Trim();
            iban = RomanianIban().Match(remittance) is { Success: true } m ? m.Value : null;
        }

        return (Cut(name?.Trim(), 200), BankStatementRules.NormalizeIban(iban), Cut(bic?.Trim(), 11), Cut(remittance?.Trim(), 500),
            endToEnd is null or "NOTPROVIDED" ? null : Cut(endToEnd.Trim(), 35));
    }

    [GeneratedRegex(@"^(\{[0-9]:[^}]*\})*\{4:")]
    private static partial Regex HeaderPrefix();

    [GeneratedRegex(@"^:([0-9]{2}[A-Z]?):")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^([CD])([0-9]{6})([A-Z]{3})([0-9]+,[0-9]{0,2})$")]
    private static partial Regex BalanceField();

    [GeneratedRegex(@"^(?<value>[0-9]{6})(?<entry>[0-9]{4})?(?<mark>RC|RD|C|D)[A-Z]?(?<amount>[0-9]+,[0-9]{0,2})(?<type>[NFS][A-Z0-9]{3})(?<customer>.{0,16}?)(//(?<bank>.{0,16}))?$")]
    private static partial Regex EntryField();

    [GeneratedRegex(@"\?[0-9]{2}")]
    private static partial Regex SubField();

    [GeneratedRegex(@"/(NAME|BENM|ORDP|IBAN|ACCW|BIC|REMI|DETL|EREF)/(.*?)(?=/(?:NAME|BENM|ORDP|IBAN|ACCW|BIC|REMI|DETL|EREF)/|$)")]
    private static partial Regex KeywordField();

    [GeneratedRegex(@"\bRO[0-9]{2}[A-Z]{4}[0-9A-Z]{16}\b")]
    private static partial Regex RomanianIban();

    private sealed record Line(DateOnly BookingDate, DateOnly ValueDate, decimal Amount, string Type, string? BankReference, string? CustomerReference,
        string? Info);

    private sealed class Builder(int line)
    {
        public string? Iban { get; private set; }

        public string? Currency { get; set; }

        public string? StatementNo { get; set; }

        public decimal? Opening { get; set; }

        public decimal? Closing { get; set; }

        public DateOnly? ClosingDate { get; set; }

        public List<Line> Lines { get; } = [];

        /// <summary>:25: = „BIC/IBAN”, „IBAN” sau „IBAN MONEDA”.</summary>
        public void Account(string value)
        {
            var account = value.Trim().Split('/').Last().Split(' ')[0];
            Iban = BankStatementRules.NormalizeIban(account);
            var parts = value.Trim().Split(' ');
            if (parts.Length > 1 && parts[^1].Length == 3)
            {
                Currency = parts[^1];
            }
        }

        public ParsedStatement Build()
        {
            if (Opening is null || Closing is null || ClosingDate is null)
            {
                throw new StatementFormatException("Extrasul nu are soldul inițial (:60F:) și soldul final (:62F:).", line);
            }

            var lines = Lines.Select(l =>
            {
                var (name, iban, bic, remittance, endToEnd) = Details(l.Info);
                return new ParsedStatementLine(l.BookingDate, l.ValueDate, l.Amount, name, iban, bic, remittance, endToEnd ?? l.CustomerReference,
                    l.BankReference, l.Type, null, null);
            }).ToList();
            var from = lines.Count == 0 ? ClosingDate.Value : lines.Min(l => l.BookingDate);
            var to = lines.Count == 0 ? ClosingDate.Value : new[] { lines.Max(l => l.BookingDate), ClosingDate.Value }.Max();
            return new ParsedStatement(Iban, StatementNo, ClosingDate.Value, from, to, Currency, Opening, Closing, lines);
        }
    }
}
