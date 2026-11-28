namespace Erp.Core.Contracts;

/// <summary>
/// Cursul unei monede (02.01, 4.3 si 6): <see cref="Rate"/> lei pentru <see cref="Multiplier"/> unitati (BNR publica HUF, JPY, KRW
/// la 100). RateDate = ziua cursului folosit (vezi <see cref="IExchangeRateProvider"/>). Source: BNR sau MANUAL.
/// </summary>
public sealed record ExchangeRateDto(string Currency, DateOnly RateDate, decimal Rate, int Multiplier, string Source)
{
    /// <summary>Lei pentru o unitate din moneda.</summary>
    public decimal PerUnit => Rate / Multiplier;
}

/// <summary>
/// Cursul BNR (02.01, 4.3), cu data lui. RON intoarce 1. Fara curs: ErpException CORE_EXCHANGE_RATE_NOT_FOUND. Documentele memoreaza
/// cursul folosit; o corectare ulterioara nu le schimba.
/// </summary>
public interface IExchangeRateProvider
{
    /// <summary>
    /// Cursul pentru operatiunile din ziua <paramref name="date"/> (documente, casa, banca, plati): ultimul curs BNR cu data strict
    /// anterioara. Cursul publicat vineri dupa-amiaza se foloseste sambata, duminica si luni (Codul fiscal, art. 290).
    /// </summary>
    Task<ExchangeRateDto> GetRateAsync(string currency, DateOnly date, CancellationToken ct);

    /// <summary>Cursul de inchidere la <paramref name="date"/> (reevaluare, solduri la o data): ultimul curs BNR pana la data, inclusiv.</summary>
    Task<ExchangeRateDto> GetClosingRateAsync(string currency, DateOnly date, CancellationToken ct);
}

/// <summary>
/// Salvarea cursurilor BNR (Integrari si preluarea din Nucleu), in tranzactia comenzii: se adauga doar ce lipseste, iar pentru
/// fiecare zi cu cursuri noi se publica CursValutarActualizat. Intoarce cursurile adaugate.
/// </summary>
public interface IExchangeRateWriter
{
    Task<IReadOnlyList<ExchangeRateDto>> SaveBnrRatesAsync(IReadOnlyList<ExchangeRateDto> rates, CancellationToken ct);
}

/// <summary>Cursurile unei zile au fost salvate (02.01, 5): Finante (reevaluare), Raportare.</summary>
public sealed record CursValutarActualizat(DateOnly RateDate, string Source, IReadOnlyList<CursValutar> Rates) : IntegrationEvent;

public sealed record CursValutar(string CurrencyCode, decimal Rate, int Multiplier);
