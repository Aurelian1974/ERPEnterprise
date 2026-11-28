using System.Data;
using Dapper;
using Erp.Bnr;
using Erp.Core.Application.Abstractions;
using Erp.Core.Application.ExchangeRates;
using Erp.Core.Application.Items;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.ExchangeRates;

/// <summary>Valutele si cursurile prin procedurile core.usp_ExchangeRate* si core.usp_Currency_SetActive (NET-06).</summary>
public sealed class ExchangeRateStore(IDbSession session) : IExchangeRateStore
{
    public async Task<ExchangeRatesDto> ListAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_ExchangeRate_List",
            new { From = Date(from), To = Date(to) }, ct)).ConfigureAwait(false);
        var currencies = (await grid.ReadAsync<CurrencyDto>().ConfigureAwait(false)).ToList();
        var bnr = (await grid.ReadAsync<RateRow>().ConfigureAwait(false)).Select(Bnr).ToList();
        var previous = (await grid.ReadAsync<RateRow>().ConfigureAwait(false)).Select(Bnr).ToList();
        var manual = (await grid.ReadAsync<ManualRow>().ConfigureAwait(false)).Select(m => new ManualRateDto(m.Id, m.CurrencyCode,
            DateOnly.FromDateTime(m.RateDate), Decimals.Trim(m.Rate), m.Multiplier, m.Note, m.ChangedByName, Utc(m.ChangedAt), m.RowVersion)).ToList();
        var last = await grid.ReadSingleAsync<LastRow>().ConfigureAwait(false);
        return new ExchangeRatesDto(from, to, currencies, bnr, previous, manual, last.LastImportAt is { } at ? Utc(at) : null,
            last.LastRateDate is { } date ? DateOnly.FromDateTime(date) : null);
    }

    public async Task<IReadOnlyList<CurrencyDto>> ListCurrenciesAsync(CancellationToken ct) =>
        [.. await session.Connection.QueryAsync<CurrencyDto>(Command("core.usp_Currency_List", new { }, ct)).ConfigureAwait(false)];

    public Task<SavedEntity> SaveManualAsync(ManualRateRecord rate, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_ExchangeRate_SaveManual", new
        {
            rate.Id, CurrencyCode = Code(rate.CurrencyCode), RateDate = Date(rate.RateDate), rate.Rate, rate.Multiplier, rate.Note, rate.RowVersion,
            UserId = userId,
        }, ct));

    public Task<byte[]> SetCurrencyActiveAsync(string code, bool isActive, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]>(Command("core.usp_Currency_SetActive", new
        {
            Code = Code(code), IsActive = isActive, RowVersion = rowVersion, UserId = userId,
        }, ct));

    internal static DbString Code(string code) => new() { Value = code, Length = 3, IsAnsi = true, IsFixedLength = true };

    private static BnrRateDto Bnr(RateRow r) =>
        new(r.CurrencyCode, DateOnly.FromDateTime(r.RateDate), Decimals.Trim(r.Rate), r.Multiplier, Utc(r.CreatedAt));

    private static DateTime Date(DateOnly value) => value.ToDateTime(TimeOnly.MinValue);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record RateRow(string CurrencyCode, DateTime RateDate, decimal Rate, int Multiplier, DateTime CreatedAt);

    private sealed record ManualRow(int Id, string CurrencyCode, DateTime RateDate, decimal Rate, int Multiplier, string Note, string ChangedByName,
        DateTime ChangedAt, byte[] RowVersion);

    private sealed record LastRow(DateTime? LastImportAt, DateTime? LastRateDate);
}

/// <summary>Cursul pentru documente (core.usp_ExchangeRate_Get), cu codul THROW tradus si din afara comenzilor (NET-09).</summary>
public sealed class ExchangeRateProvider(IDbSession session, ISqlErrorTranslator errors) : IExchangeRateProvider
{
    public Task<ExchangeRateDto> GetRateAsync(string currency, DateOnly date, CancellationToken ct) => GetAsync(currency, date, false, ct);

    public Task<ExchangeRateDto> GetClosingRateAsync(string currency, DateOnly date, CancellationToken ct) => GetAsync(currency, date, true, ct);

    private async Task<ExchangeRateDto> GetAsync(string currency, DateOnly date, bool closing, CancellationToken ct)
    {
        try
        {
            var row = await session.Connection.QuerySingleAsync<Row>(new CommandDefinition("core.usp_ExchangeRate_Get", new
            {
                CurrencyCode = ExchangeRateStore.Code(currency), Date = date.ToDateTime(TimeOnly.MinValue), Closing = closing,
            }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
            return new ExchangeRateDto(row.Currency.Trim(), DateOnly.FromDateTime(row.RateDate), row.Rate, row.Multiplier, row.Source);
        }
        catch (Exception exception) when (errors.Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    private sealed record Row(string Currency, DateTime RateDate, decimal Rate, int Multiplier, string Source);
}

/// <summary>Salvarea cursurilor BNR (core.usp_ExchangeRate_SaveBatch) si evenimentul CursValutarActualizat pe fiecare zi cu cursuri noi.</summary>
public sealed class ExchangeRateWriter(IDbSession session, IOutbox outbox, ICurrentContext context) : IExchangeRateWriter
{
    public async Task<IReadOnlyList<ExchangeRateDto>> SaveBnrRatesAsync(IReadOnlyList<ExchangeRateDto> rates, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("CurrencyCode", typeof(string));
        table.Columns.Add("RateDate", typeof(DateTime));
        table.Columns.Add("Rate", typeof(decimal));
        table.Columns.Add("Multiplier", typeof(int));
        foreach (var rate in rates.DistinctBy(r => (r.Currency, r.RateDate)))
        {
            table.Rows.Add(rate.Currency, rate.RateDate.ToDateTime(TimeOnly.MinValue), rate.Rate, rate.Multiplier);
        }

        var added = (await session.Connection.QueryAsync<Row>(new CommandDefinition("core.usp_ExchangeRate_SaveBatch", new
        {
            Rates = table.AsTableValuedParameter("core.ExchangeRateList"), UserId = context.UserId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false))
            .Select(r => new ExchangeRateDto(r.CurrencyCode.Trim(), DateOnly.FromDateTime(r.RateDate), r.Rate, r.Multiplier, "BNR")).ToList();

        foreach (var day in added.GroupBy(r => r.RateDate))
        {
            await outbox.EnqueueAsync(new CursValutarActualizat(day.Key, "BNR",
                [.. day.Select(r => new CursValutar(r.Currency, r.Rate, r.Multiplier))]), ct).ConfigureAwait(false);
        }

        return added;
    }

    private sealed record Row(string CurrencyCode, DateTime RateDate, decimal Rate, int Multiplier);
}

/// <summary>Fisierele BNR prin Erp.Bnr: intai cel pe 10 zile; daca lipseste sau nu are formatul asteptat, cel zilnic.</summary>
public sealed class BnrRatesSource(BnrRatesClient client, BnrOptions options) : IBnrRatesSource
{
    public async Task<IReadOnlyList<ExchangeRateDto>> GetRecentAsync(CancellationToken ct)
    {
        IReadOnlyList<BnrRate> rates;
        try
        {
            rates = await client.GetAsync(options.TenDaysUrl, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or BnrFormatException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            rates = await Daily(ct).ConfigureAwait(false);
        }

        return [.. rates.Select(r => new ExchangeRateDto(r.Currency, r.Date, r.Rate, r.Multiplier, "BNR"))];
    }

    private async Task<IReadOnlyList<BnrRate>> Daily(CancellationToken ct)
    {
        try
        {
            return await client.GetAsync(options.DailyUrl, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or BnrFormatException or TaskCanceledException)
        {
            // BNR nu raspunde sau trimite altceva decat cursurile (de exemplu o pagina de eroare): 503, se poate reincerca.
            throw new ErpException(ErpErrorKind.Unavailable, "CORE_BNR_UNAVAILABLE",
                $"Cursurile BNR nu au putut fi preluate: {ex.Message} Reîncercați mai târziu.", ex);
        }
    }
}
