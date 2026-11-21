using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Bnr;
using Erp.Core.Contracts;
using Microsoft.Data.SqlClient;

namespace Erp.Worker.Jobs;

/// <summary>
/// Preluarea zilnica a cursurilor BNR (02.01, 4.3): in zilele lucratoare, dupa ora publicarii (putin dupa 13:00, ora Romaniei),
/// citeste fisierul zilnic si salveaza cursurile lipsa (core.usp_ExchangeRate_SaveBatch), cu evenimentul CursValutarActualizat in
/// outbox, in aceeasi tranzactie. Ruleaza de cateva ori pana la 16:45 si se opreste cand cursul zilei exista deja, ca sa nu incarce
/// serverul BNR. Utilizatorul tehnic „system” (Id 1) apare ca autor.
/// </summary>
public sealed class BnrRatesJob(Func<SqlConnection> connect, BnrRatesClient client, BnrOptions options, TimeProvider clock, ILogger<BnrRatesJob> log)
{
    public const string Id = "core-bnr-rates";

    /// <summary>La :15 si :45, intre 13 si 16, de luni pana vineri, ora Romaniei.</summary>
    public const string Schedule = "15,45 13-16 * * 1-5";

    private const int SystemUserId = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TimeZoneInfo Romania => TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");

    public async Task RunAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Romania).DateTime);
        await using var connection = connect();
        await connection.OpenAsync(ct);
        var hasToday = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT TOP (1) 1 FROM core.ExchangeRate WHERE Source = 'BNR' AND RateDate = @Today",
            new { Today = today.ToDateTime(TimeOnly.MinValue) }, cancellationToken: ct)) is not null;
        if (hasToday)
        {
            log.LogDebug("Cursurile BNR din {Date} sunt deja salvate.", today);
            return;
        }

        var rates = await client.GetAsync(options.DailyUrl, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var added = await SaveAsync(connection, (SqlTransaction)transaction, rates, ct);
        foreach (var day in added.GroupBy(r => r.Date))
        {
            await EnqueueAsync(connection, (SqlTransaction)transaction,
                new CursValutarActualizat(day.Key, "BNR", [.. day.Select(r => new CursValutar(r.Currency, r.Rate, r.Multiplier))]), ct);
        }

        await transaction.CommitAsync(ct);
        log.LogInformation("Cursuri BNR: fișierul are ziua {Date}, {Added} cursuri noi.", rates.Count == 0 ? null : rates.Max(r => r.Date), added.Count);
    }

    private static async Task<IReadOnlyList<BnrRate>> SaveAsync(SqlConnection connection, SqlTransaction transaction, IReadOnlyList<BnrRate> rates,
        CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("CurrencyCode", typeof(string));
        table.Columns.Add("RateDate", typeof(DateTime));
        table.Columns.Add("Rate", typeof(decimal));
        table.Columns.Add("Multiplier", typeof(int));
        foreach (var rate in rates.DistinctBy(r => (r.Currency, r.Date)))
        {
            table.Rows.Add(rate.Currency, rate.Date.ToDateTime(TimeOnly.MinValue), rate.Rate, rate.Multiplier);
        }

        var rows = await connection.QueryAsync<(string CurrencyCode, DateTime RateDate, decimal Rate, int Multiplier)>(new CommandDefinition(
            "core.usp_ExchangeRate_SaveBatch", new { Rates = table.AsTableValuedParameter("core.ExchangeRateList"), UserId = SystemUserId },
            transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return [.. rows.Select(r => new BnrRate(r.CurrencyCode.Trim(), DateOnly.FromDateTime(r.RateDate), r.Rate, r.Multiplier))];
    }

    // Aceeasi anvelopa ca IOutbox din Nucleu (Erp.Core.Infrastructure.Data.Outbox): tipul complet, versiunea, JSON web.
    private async Task EnqueueAsync(SqlConnection connection, SqlTransaction transaction, CursValutarActualizat evt, CancellationToken ct)
    {
        var envelope = evt with { OccurredAtUtc = clock.GetUtcNow().UtcDateTime, CorrelationId = Guid.NewGuid() };
        await connection.ExecuteAsync(new CommandDefinition("core.usp_Outbox_Enqueue", new
        {
            envelope.EventId,
            EventType = new DbString { Value = typeof(CursValutarActualizat).FullName, Length = 200 },
            EventVersion = envelope.Version,
            CompanyId = (int?)null,
            envelope.CorrelationId,
            Payload = JsonSerializer.Serialize(envelope, Json),
            OccurredAt = envelope.OccurredAtUtc,
        }, transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
}
