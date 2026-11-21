using Dapper;
using Erp.Core.Application;
using Erp.Core.Application.ExchangeRates;
using Erp.Core.Contracts;
using Microsoft.Data.SqlClient;

namespace Erp.Core.Tests.Sql;

/// <summary>Sursa BNR din teste: cursurile date de test, fara retea.</summary>
public sealed class FakeBnrSource : IBnrRatesSource
{
    public List<ExchangeRateDto> Rates { get; } = [];

    public Task<IReadOnlyList<ExchangeRateDto>> GetRecentAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ExchangeRateDto>>([.. Rates]);
}

/// <summary>Valute si cursuri pe SQL Server real (02.01, 3.5 si 4.3; A11): preluarea BNR, cursul unei zile fara curs, cursurile manuale.</summary>
public sealed partial class UserIntegrationTests
{
    private static ExchangeRateDto Bnr(string currency, int day, decimal rate, int multiplier = 1) =>
        new(currency, new DateOnly(2027, 3, day), rate, multiplier, "BNR");

    [Fact]
    [Trait("Criteriu", "02.01-A11")]
    public async Task Bnr_rates_are_added_once_and_a_day_without_rate_uses_the_last_previous_one()
    {
        bnr.Rates.AddRange([Bnr("EUR", 11, 5.0809m), Bnr("EUR", 12, 5.0812m), Bnr("HUF", 12, 1.3125m, 100), Bnr("XYZ", 12, 2.5m)]);
        var first = await Send(new ImportBnrRates());
        Assert.Equal((2, 4, (DateOnly?)new DateOnly(2027, 3, 12)), (first.Days, first.Added, first.LastRateDate));
        Assert.Equal(0, (await Send(new ImportBnrRates())).Added);

        // Sambata 13.03.2027 nu are curs: se foloseste cel de vineri, cu data lui.
        var saturday = await Send(new GetExchangeRate("eur", new DateOnly(2027, 3, 13)));
        Assert.Equal((new DateOnly(2027, 3, 12), 5.0812m, "BNR"), (saturday.RateDate, saturday.Rate, saturday.Source));
        var huf = await Send(new GetExchangeRate("HUF", new DateOnly(2027, 3, 12)));
        Assert.Equal((100, 0.013125m), (huf.Multiplier, huf.PerUnit));
        Assert.Equal(1m, (await Send(new GetExchangeRate("RON", new DateOnly(2027, 3, 13)))).Rate);
        var missing = await Assert.ThrowsAsync<ErpException>(() => Send(new GetExchangeRate("EUR", new DateOnly(2000, 1, 3))));
        Assert.Equal("CORE_EXCHANGE_RATE_NOT_FOUND", missing.Code);

        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.False(await connection.ExecuteScalarAsync<bool>("SELECT IsActive FROM core.Currency WHERE Code = 'XYZ'"));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM core.Outbox WHERE EventType = @Type AND Payload LIKE '%2027-03-1%'", new { Type = typeof(CursValutarActualizat).FullName }));

        var page = await Send(new GetExchangeRates(new DateOnly(2027, 3, 12), new DateOnly(2027, 3, 14)));
        Assert.Contains(page.BnrRates, r => r.CurrencyCode == "EUR" && r.Rate == "5.0812");
        Assert.Contains(page.PreviousRates, r => r.CurrencyCode == "EUR" && r.RateDate == new DateOnly(2027, 3, 11));
        Assert.Equal(new DateOnly(2027, 3, 12), page.LastRateDate);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.5")]
    public async Task Manual_rates_need_a_reason_never_change_a_bnr_rate_and_do_not_replace_it_for_documents()
    {
        bnr.Rates.Add(Bnr("USD", 19, 4.3794m));
        await Send(new ImportBnrRates());

        var saved = await Send(new SaveManualExchangeRate(null, "USD", new DateOnly(2027, 3, 19), "4,41", 1, "Curs fix din contract", null));
        var duplicate = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveManualExchangeRate(null, "USD", new DateOnly(2027, 3, 19), "4.42", 1, "Alt motiv", null)));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);
        var noReason = await Assert.ThrowsAsync<ErpValidationException>(() =>
            Send(new SaveManualExchangeRate(null, "USD", new DateOnly(2027, 3, 20), "4.42", 1, " ", null)));
        Assert.Contains("Note", noReason.Errors.Keys, StringComparer.OrdinalIgnoreCase);

        await using var connection = new SqlConnection(database.ConnectionString);
        var bnrRate = await connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(
            "SELECT Id, RowVersion FROM core.ExchangeRate WHERE CurrencyCode = 'USD' AND RateDate = '2027-03-19' AND Source = 'BNR'");
        var readOnly = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveManualExchangeRate(bnrRate.Id, "USD", new DateOnly(2027, 3, 19), "4.5", 1, "Corectie", bnrRate.RowVersion)));
        Assert.Equal("CORE_BNR_RATE_READONLY", readOnly.Code);

        Assert.Equal(4.3794m, (await Send(new GetExchangeRate("USD", new DateOnly(2027, 3, 19)))).Rate);
        var manual = Assert.Single((await Send(new GetExchangeRates(new DateOnly(2027, 3, 19), new DateOnly(2027, 3, 19)))).ManualRates, m => m.Id == saved.Id);
        Assert.Equal(("4.41", "Curs fix din contract"), (manual.Rate, manual.Note));
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.5")]
    public async Task Leu_stays_active_and_other_currencies_are_activated_when_needed()
    {
        var currencies = (await Send(new GetExchangeRates())).Currencies;
        var ron = currencies.Single(c => c.Code == "RON");
        var pln = currencies.Single(c => c.Code == "PLN");
        Assert.True(ron.IsActive);
        Assert.False(pln.IsActive);

        var leu = await Assert.ThrowsAsync<ErpException>(() => Send(new SetCurrencyActive("RON", false, ron.RowVersion)));
        Assert.Equal("CORE_INVALID_REFERENCE", leu.Code);
        await Send(new SetCurrencyActive("pln", true, pln.RowVersion));
        Assert.True((await Send(new GetExchangeRates())).Currencies.Single(c => c.Code == "PLN").IsActive);

        var inactive = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveManualExchangeRate(null, "SEK", new DateOnly(2027, 3, 22), "0.44", 1, "Raport intern", null)));
        Assert.Equal("CORE_INVALID_REFERENCE", inactive.Code);
    }
}
