using System.Globalization;
using System.Text;
using System.Text.Json;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank.Formats;

/// <summary>
/// Maparea CSV pentru plati din PaymentMapping (F2 3.1): separatorul, antetul, formatul datei si al sumei si coloanele, in ordine, din
/// BeneficiaryName, BeneficiaryIban, BeneficiaryBic, Amount, Currency, RemittanceInfo, EndToEndId, RequestedExecutionDate, Number.
/// </summary>
public sealed record CsvPaymentMapping(string Separator = ";", bool Header = true, string DateFormat = "dd.MM.yyyy", string DecimalSeparator = ",",
    IReadOnlyList<string>? Columns = null);

/// <summary>Fisierul de plati CSV dupa profilul bancii (F2 3.1, 4.2 pasul 4): un rand pe OP, campurile cu ghilimele cand e nevoie (RFC 4180).</summary>
public sealed class CsvPaymentWriter : IPaymentFileWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] DefaultColumns =
        ["BeneficiaryName", "BeneficiaryIban", "Amount", "Currency", "RemittanceInfo", "EndToEndId", "RequestedExecutionDate"];

    public bool Supports(PaymentFileFormat format) => format == PaymentFileFormat.Csv;

    public PaymentFile Write(PaymentBatchData batch, PaymentFileFormat format, DateTime createdAt)
    {
        CsvPaymentMapping mapping;
        try
        {
            mapping = string.IsNullOrWhiteSpace(batch.PaymentMapping) ? new CsvPaymentMapping() : JsonSerializer.Deserialize<CsvPaymentMapping>(batch.PaymentMapping, Json)
                ?? new CsvPaymentMapping();
        }
        catch (JsonException)
        {
            mapping = new CsvPaymentMapping();
        }

        var columns = mapping.Columns is { Count: > 0 } c ? c : DefaultColumns;
        var number = new NumberFormatInfo { NumberDecimalSeparator = mapping.DecimalSeparator };
        var text = new StringBuilder();
        if (mapping.Header)
        {
            text.Append(string.Join(mapping.Separator, columns.Select(col => Quote(col, mapping.Separator)))).Append("\r\n");
        }

        foreach (var o in batch.Orders)
        {
            var values = columns.Select(col => col switch
            {
                "BeneficiaryName" => o.BeneficiaryName,
                "BeneficiaryIban" => o.BeneficiaryIban,
                "BeneficiaryBic" => o.BeneficiaryBic ?? string.Empty,
                "Amount" => o.Amount.ToString("0.00", number),
                "Currency" => batch.CurrencyCode,
                "RemittanceInfo" => o.RemittanceInfo is { } r && r.Length > batch.MaxRemittanceLength ? r[..batch.MaxRemittanceLength] : o.RemittanceInfo ?? string.Empty,
                "EndToEndId" => o.EndToEndId ?? string.Empty,
                "RequestedExecutionDate" => o.RequestedExecutionDate.ToString(mapping.DateFormat, CultureInfo.InvariantCulture),
                "Number" => o.Number ?? string.Empty,
                _ => string.Empty,
            });
            text.Append(string.Join(mapping.Separator, values.Select(v => Quote(v, mapping.Separator)))).Append("\r\n");
        }

        return new PaymentFile($"{batch.Number}.csv", "text/csv", new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray());
    }

    private static string Quote(string value, string separator) =>
        value.Contains(separator, StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
