using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Erp.Finance.Domain.Bank;

/// <summary>Formatul extrasului dintr-un profil bancar (F2 3.1).</summary>
public enum StatementFormat : byte
{
    Camt053 = 1,
    Mt940 = 2,
    Csv = 3,
    None = 4,
}

/// <summary>Formatul fisierului de plati dintr-un profil bancar (F2 3.1).</summary>
public enum PaymentFileFormat : byte
{
    Pain001V03 = 1,
    Pain001V09 = 2,
    Csv = 3,
    None = 4,
}

/// <summary>Sursa extrasului (F2 3.3).</summary>
public enum StatementSource : byte
{
    Camt053 = 1,
    Mt940 = 2,
    Csv = 3,
    Manual = 4,
    OpeningBalance = 5,
}

public enum BankStatementStatus : byte
{
    Imported = 1,
    Reconciling = 2,
    Closed = 3,
}

/// <summary>Continuitatea cu extrasul anterior (F2 3.3): golul blocheaza validarea liniilor pana la import sau acceptare.</summary>
public enum ContinuityStatus : byte
{
    Ok = 1,
    Gap = 2,
    GapAccepted = 3,
}

public enum BankLineMatchStatus : byte
{
    Unmatched = 1,
    Proposed = 2,
    Explained = 3,
    Validated = 4,
}

/// <summary>O linie citita din fisier, inainte de salvare: suma cu semn (+ intrare, − iesire).</summary>
public sealed record ParsedStatementLine(
    DateOnly BookingDate, DateOnly? ValueDate, decimal Amount, string? CounterpartyName, string? CounterpartyIban, string? CounterpartyBic,
    string? RemittanceInfo, string? EndToEndId, string? BankReference, string? BankTransactionCode, decimal? ForeignAmount, string? ForeignCurrency);

/// <summary>
/// Un extras citit din fisier (un CAMT.053 sau MT940 poate avea mai multe). Soldurile lipsesc la CSV-urile fara sold: atunci soldul
/// initial e soldul final al extrasului anterior.
/// </summary>
public sealed record ParsedStatement(
    string? AccountIban, string? StatementNo, DateOnly StatementDate, DateOnly PeriodFrom, DateOnly PeriodTo, string? CurrencyCode,
    decimal? OpeningBalance, decimal? ClosingBalance, IReadOnlyList<ParsedStatementLine> Lines);

/// <summary>Fisier care nu poate fi citit cu formatul ales (FIN_BANK_FILE_INVALID), cu randul si motivul.</summary>
public sealed class StatementFormatException(string message, int? line = null) : Exception(line is null ? message : $"Rândul {line}: {message}")
{
    public int? Line { get; } = line;
}

/// <summary>Regulile extrasului bancar (F2 3.3, 3.4 si 4.1), fara acces la date.</summary>
public static class BankStatementRules
{
    /// <summary>Dimensiunea maxima a unui fisier de extras (SEC-05): 20 MB, cat are un CAMT.053 cu zeci de mii de linii.</summary>
    public const int MaxFileBytes = 20 * 1024 * 1024;

    /// <summary>IBAN normalizat (SQL-24): majuscule, fara spatii; null daca e gol.</summary>
    public static string? NormalizeIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return null;
        }

        var builder = new StringBuilder(iban.Length);
        foreach (var c in iban)
        {
            if (!char.IsWhiteSpace(c) && c != '-')
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.Length is > 0 and <= 34 ? builder.ToString() : null;
    }

    public static string Sha256Hex(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>
    /// Referinta calculata cand banca nu da una (F2 3.4): SHA-256 din cont, data, suma, contrapartida, detalii si numarul de ordine al
    /// aparitiei identice in aceeasi zi; acelasi fisier reimportat da aceleasi referinte, deci liniile se sar.
    /// </summary>
    public static string ComputedReference(int treasuryAccountId, ParsedStatementLine line, int occurrence)
    {
        var text = string.Join('|', treasuryAccountId.ToString(CultureInfo.InvariantCulture), line.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            line.Amount.ToString("0.00", CultureInfo.InvariantCulture), line.CounterpartyIban ?? string.Empty, line.CounterpartyName ?? string.Empty,
            line.RemittanceInfo ?? string.Empty, occurrence.ToString(CultureInfo.InvariantCulture));
        return "H:" + Sha256Hex(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Referintele finale ale liniilor: cele ale bancii (taiate la 100) sau calculate, numerotand aparitiile identice.</summary>
    public static IReadOnlyList<string> References(int treasuryAccountId, IReadOnlyList<ParsedStatementLine> lines)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>(lines.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var reference = string.IsNullOrWhiteSpace(line.BankReference) ? null : line.BankReference.Trim();
            if (reference is null || !seen.Add(reference))
            {
                // Fara referinta, sau o referinta repetata in fisier (unele banci dau aceeasi referinta pe linii diferite): hash cu aparitia.
                var key = ComputedReference(treasuryAccountId, line, 0);
                occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
                reference = ComputedReference(treasuryAccountId, line, occurrences[key]);
                seen.Add(reference);
            }

            result.Add(reference.Length <= 100 ? reference : reference[..100]);
        }

        return result;
    }

    /// <summary>Soldul final − soldul initial = suma liniilor (F2 3.3; 51208).</summary>
    public static bool BalancesMatch(decimal opening, decimal closing, IEnumerable<ParsedStatementLine> lines) =>
        closing - opening == lines.Sum(l => l.Amount);

    /// <summary>Formatul recunoscut dupa continut, cand contul nu are profil: XML = CAMT.053, „:20:” = MT940.</summary>
    public static StatementFormat Detect(ReadOnlySpan<byte> content)
    {
        var text = Encoding.UTF8.GetString(content[..Math.Min(content.Length, 512)]).TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (text.StartsWith('<'))
        {
            return StatementFormat.Camt053;
        }

        return text.StartsWith(":20:", StringComparison.Ordinal) || text.StartsWith("{1:", StringComparison.Ordinal) || text.Contains("\n:20:", StringComparison.Ordinal)
            ? StatementFormat.Mt940
            : StatementFormat.None;
    }

    public static StatementSource SourceOf(StatementFormat format) => format switch
    {
        StatementFormat.Camt053 => StatementSource.Camt053,
        StatementFormat.Mt940 => StatementSource.Mt940,
        StatementFormat.Csv => StatementSource.Csv,
        _ => StatementSource.Manual,
    };
}
