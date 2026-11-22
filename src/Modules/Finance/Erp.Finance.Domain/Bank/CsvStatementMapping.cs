using System.Text.Json;

namespace Erp.Finance.Domain.Bank;

/// <summary>
/// Maparea CSV a unui profil bancar (F2 3.1, SQL-12): separatorul, codarea, randul de antet, formatul datei si al numerelor si coloana
/// fiecarui camp (numele din antet sau numarul coloanei, de la 1). Suma vine dintr-o coloana cu semn sau din doua coloane debit / credit;
/// soldul, daca exista, e soldul dupa fiecare rand.
/// </summary>
public sealed record CsvStatementMapping(
    string Separator = ";", string Encoding = "utf-8", int HeaderRow = 1, string DateFormat = "dd.MM.yyyy", string DecimalSeparator = ",",
    string? ThousandsSeparator = null, string? Iban = null, string? Currency = null, CsvColumns? Columns = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CsvStatementMapping Read(string? mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping))
        {
            throw new StatementFormatException("Profilul CSV nu are mapare.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<CsvStatementMapping>(mapping, Json) ?? throw new StatementFormatException("Maparea CSV este goală.");
            var c = result.Columns;
            if (c?.BookingDate is null || c.Amount is null && (c.Debit is null || c.Credit is null) || result.Separator.Length != 1)
            {
                throw new StatementFormatException("Maparea CSV cere separatorul (un caracter), data și suma (sau debit și credit).");
            }

            return result;
        }
        catch (JsonException)
        {
            throw new StatementFormatException("Maparea CSV nu este un JSON valid.");
        }
    }
}

public sealed record CsvColumns(
    string? BookingDate = null, string? ValueDate = null, string? Amount = null, string? Debit = null, string? Credit = null,
    string? CounterpartyName = null, string? CounterpartyIban = null, string? RemittanceInfo = null, string? BankReference = null,
    string? EndToEndId = null, string? Balance = null);
