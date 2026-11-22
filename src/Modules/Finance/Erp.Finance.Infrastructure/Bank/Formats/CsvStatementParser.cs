using System.Globalization;
using System.Text;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank.Formats;

/// <summary>Extrasul CSV dupa maparea profilului (F2 3.1). Un fisier = un extras pe intervalul liniilor.</summary>
public sealed class CsvStatementParser : IStatementParser
{
    static CsvStatementParser() => System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public StatementFormat Format => StatementFormat.Csv;

    public IReadOnlyList<ParsedStatement> Parse(byte[] content, string? mapping)
    {
        var map = CsvStatementMapping.Read(mapping);
        var columns = map.Columns!;
        Encoding encoding;
        try
        {
            encoding = System.Text.Encoding.GetEncoding(map.Encoding);
        }
        catch (ArgumentException)
        {
            throw new StatementFormatException($"Codarea „{map.Encoding}” nu este cunoscută.");
        }

        var rows = Rows(encoding.GetString(content).TrimStart('﻿'), map.Separator[0]);
        if (rows.Count < map.HeaderRow)
        {
            throw new StatementFormatException("Fișierul nu are rândul de antet.");
        }

        var header = map.HeaderRow > 0 ? rows[map.HeaderRow - 1].Fields : [];
        int? Index(string? column)
        {
            if (column is null)
            {
                return null;
            }

            if (int.TryParse(column, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0)
            {
                return number - 1;
            }

            var found = Array.FindIndex(header, h => string.Equals(h.Trim(), column, StringComparison.OrdinalIgnoreCase));
            return found >= 0 ? found : throw new StatementFormatException($"Coloana „{column}” nu există în antet.", map.HeaderRow);
        }

        var iBooking = Index(columns.BookingDate)!.Value;
        var iValue = Index(columns.ValueDate);
        var iAmount = Index(columns.Amount);
        var iDebit = Index(columns.Debit);
        var iCredit = Index(columns.Credit);
        var iName = Index(columns.CounterpartyName);
        var iIban = Index(columns.CounterpartyIban);
        var iRemit = Index(columns.RemittanceInfo);
        var iRef = Index(columns.BankReference);
        var iE2e = Index(columns.EndToEndId);
        var iBalance = Index(columns.Balance);

        var number = new NumberFormatInfo { NumberDecimalSeparator = map.DecimalSeparator, NumberGroupSeparator = map.ThousandsSeparator ?? "\u0000" };
        var lines = new List<ParsedStatementLine>();
        var balances = new List<decimal>();
        foreach (var row in rows.Skip(map.HeaderRow))
        {
            if (row.Fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string? Get(int? i) => i is { } x && x < row.Fields.Length && !string.IsNullOrWhiteSpace(row.Fields[x]) ? row.Fields[x].Trim() : null;
            decimal? Num(int? i)
            {
                var text = Get(i);
                if (text is null)
                {
                    return null;
                }

                return decimal.TryParse(text.Replace(" ", string.Empty, StringComparison.Ordinal), NumberStyles.Number | NumberStyles.AllowLeadingSign,
                    number, out var value) && Math.Round(value, 2) == value
                    ? value
                    : throw new StatementFormatException($"Suma „{text}” nu este validă.", row.Line);
            }

            DateOnly? Day(int? i)
            {
                var text = Get(i);
                if (text is null)
                {
                    return null;
                }

                return DateOnly.TryParseExact(text, map.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : throw new StatementFormatException($"Data „{text}” nu respectă formatul {map.DateFormat}.", row.Line);
            }

            var booking = Day(iBooking) ?? throw new StatementFormatException("Rând fără dată.", row.Line);
            var amount = iAmount is not null ? Num(iAmount) ?? 0 : (Num(iCredit) ?? 0) - Math.Abs(Num(iDebit) ?? 0);
            if (amount == 0)
            {
                throw new StatementFormatException("Rând fără sumă.", row.Line);
            }

            lines.Add(new ParsedStatementLine(booking, Day(iValue), amount, Cut(Get(iName), 200), BankStatementRules.NormalizeIban(Get(iIban)), null,
                Cut(Get(iRemit), 500), Cut(Get(iE2e), 35), Get(iRef), null, null, null));
            if (Num(iBalance) is { } balance)
            {
                balances.Add(balance);
            }
        }

        if (lines.Count == 0)
        {
            throw new StatementFormatException("Fișierul nu are nicio linie.");
        }

        // Cu sold pe rand (in ordinea fisierului): soldul initial = soldul primului rand − suma lui; soldul final = soldul ultimului rand.
        decimal? opening = null, closing = null;
        if (balances.Count == lines.Count)
        {
            opening = balances[0] - lines[0].Amount;
            closing = balances[^1];
        }

        var from = lines.Min(l => l.BookingDate);
        var to = lines.Max(l => l.BookingDate);
        return [new ParsedStatement(BankStatementRules.NormalizeIban(map.Iban), null, to, from, to, map.Currency, opening, closing, lines)];
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];

    /// <summary>Randurile CSV (RFC 4180): campuri intre ghilimele cu separatorul, ghilimele dublate sau rand nou in interior.</summary>
    internal static List<(string[] Fields, int Line)> Rows(string text, char separator)
    {
        var rows = new List<(string[], int)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var start = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(field.ToString());
                field.Clear();
                rows.Add((fields.ToArray(), start));
                fields.Clear();
                line++;
                start = line;
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add((fields.ToArray(), start));
        }

        return rows;
    }
}
