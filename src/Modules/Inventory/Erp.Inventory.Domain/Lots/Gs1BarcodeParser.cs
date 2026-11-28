using System.Globalization;
using System.Text.RegularExpressions;
using Erp.Inventory.Contracts;

namespace Erp.Inventory.Domain.Lots;

/// <summary>
/// Citirea codurilor GS1 (02.05, S4 3.8): forma lizibila „(01)…(17)…(10)…” sau forma bruta, cu separatorul GS (ASCII 29) dupa campurile de lungime
/// variabila si prefixul de simbologie (]C1, ]d2, ]Q3) eliminat. AI cunoscute: 00, 01, 02 (fixe), 11, 13, 15, 17 (date AAMMZZ; ZZ = 00 inseamna
/// ultima zi a lunii), 10, 21, 30, 37 (variabile). Un cod necunoscut sau ilizibil nu arunca: erorile se intorc in lista.
/// </summary>
public sealed partial class Gs1BarcodeParser : IGs1BarcodeParser
{
    private const char GroupSeparator = (char)29;

    private static readonly Dictionary<string, int> Fixed = new(StringComparer.Ordinal)
    {
        ["00"] = 18, ["01"] = 14, ["02"] = 14, ["11"] = 6, ["13"] = 6, ["15"] = 6, ["17"] = 6,
    };

    private static readonly HashSet<string> Variable = new(StringComparer.Ordinal) { "10", "21", "30", "37" };

    [GeneratedRegex(@"\((\d{2,4})\)([^(]*)", RegexOptions.CultureInvariant)]
    private static partial Regex HumanReadable();

    public Gs1ParseResult Parse(string raw)
    {
        var errors = new List<string>();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = (raw ?? string.Empty).Trim();
        if (text.Length >= 3 && text[0] == ']')
        {
            text = text[3..];
        }

        if (text.Contains('(', StringComparison.Ordinal))
        {
            foreach (Match m in HumanReadable().Matches(text))
            {
                var ai = m.Groups[1].Value;
                var value = m.Groups[2].Value.Trim(GroupSeparator).Trim();
                if (!Fixed.TryGetValue(ai, out var length) && !Variable.Contains(ai))
                {
                    errors.Add($"AI {ai} necunoscut.");
                    continue;
                }

                if (length > 0 && value.Length != length)
                {
                    errors.Add($"AI {ai}: lungime greșită.");
                    continue;
                }

                fields[ai] = value;
            }
        }
        else
        {
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] == GroupSeparator)
                {
                    i++;
                    continue;
                }

                if (i + 2 > text.Length)
                {
                    errors.Add("Cod GS1 incomplet.");
                    break;
                }

                var ai = text.Substring(i, 2);
                i += 2;
                if (Fixed.TryGetValue(ai, out var length))
                {
                    if (i + length > text.Length)
                    {
                        errors.Add($"AI {ai}: lungime greșită.");
                        break;
                    }

                    fields[ai] = text.Substring(i, length);
                    i += length;
                }
                else if (Variable.Contains(ai))
                {
                    var end = text.IndexOf(GroupSeparator, i);
                    end = end < 0 ? text.Length : end;
                    fields[ai] = text[i..end];
                    i = end;
                }
                else
                {
                    errors.Add($"AI {ai} necunoscut.");
                    break;
                }
            }
        }

        if (fields.Count == 0 && errors.Count == 0)
        {
            errors.Add("Codul nu conține identificatori GS1.");
        }

        decimal? quantity = null;
        if (fields.TryGetValue("37", out var q) || fields.TryGetValue("30", out q))
        {
            if (decimal.TryParse(q, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                quantity = parsed;
            }
            else
            {
                errors.Add("Cantitatea (AI 37) nu e un număr.");
            }
        }

        return new Gs1ParseResult(fields.GetValueOrDefault("01") ?? fields.GetValueOrDefault("02"), fields.GetValueOrDefault("10"), Date("11"), Date("17"),
            fields.GetValueOrDefault("21"), quantity, errors);

        DateOnly? Date(string ai)
        {
            if (!fields.TryGetValue(ai, out var value))
            {
                return null;
            }

            if (value.Length != 6 || !int.TryParse(value.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var yy)
                || !int.TryParse(value.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var mm)
                || !int.TryParse(value.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var dd) || mm is < 1 or > 12)
            {
                errors.Add($"AI {ai}: dată invalidă.");
                return null;
            }

            var year = 2000 + yy;
            var last = DateTime.DaysInMonth(year, mm);
            if (dd > last)
            {
                errors.Add($"AI {ai}: dată invalidă.");
                return null;
            }

            return new DateOnly(year, mm, dd == 0 ? last : dd);
        }
    }
}
