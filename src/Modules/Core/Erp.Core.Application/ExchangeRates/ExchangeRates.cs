using Erp.Core.Application.Items;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.ExchangeRates;

/// <summary>O moneda din nomenclator (ISO 4217); Decimals = zecimalele sumelor in moneda; RON e baza, mereu activa.</summary>
public sealed record CurrencyDto(string Code, string Name, byte Decimals, bool IsActive, byte[] RowVersion);

/// <summary>Un curs BNR (doar citire): Rate = sir zecimal invariant, lei pentru Multiplier unitati; ImportedAt = momentul preluarii.</summary>
public sealed record BnrRateDto(string CurrencyCode, DateOnly RateDate, string Rate, int Multiplier, DateTime ImportedAt);

/// <summary>Un curs manual, cu motivul si cine l-a introdus sau modificat ultimul.</summary>
public sealed record ManualRateDto(int Id, string CurrencyCode, DateOnly RateDate, string Rate, int Multiplier, string Note, string ChangedByName,
    DateTime ChangedAt, byte[] RowVersion);

/// <summary>
/// Configurare › Cursuri valutare (macheta 07b): valutele, cursurile BNR din interval, ultimul curs BNR de dinaintea intervalului pe
/// fiecare moneda (zilele fara curs de la inceputul intervalului), cursurile manuale si ultima preluare BNR.
/// </summary>
public sealed record ExchangeRatesDto(DateOnly From, DateOnly To, IReadOnlyList<CurrencyDto> Currencies, IReadOnlyList<BnrRateDto> BnrRates,
    IReadOnlyList<BnrRateDto> PreviousRates, IReadOnlyList<ManualRateDto> ManualRates, DateTime? LastImportAt, DateOnly? LastRateDate);

/// <summary>Rezultatul preluarii de la BNR: zilele din fisier, cursurile noi si ultima zi cu curs.</summary>
public sealed record BnrImportResultDto(int Days, int Added, DateOnly? LastRateDate);

public sealed record ManualRateRecord(int? Id, string CurrencyCode, DateOnly RateDate, decimal Rate, int Multiplier, string Note, byte[]? RowVersion);

/// <summary>Accesul la date pentru valute si cursuri (core.usp_ExchangeRate*, core.usp_Currency*; NET-06).</summary>
public interface IExchangeRateStore
{
    Task<ExchangeRatesDto> ListAsync(DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<CurrencyDto>> ListCurrenciesAsync(CancellationToken ct);

    Task<SavedEntity> SaveManualAsync(ManualRateRecord rate, int userId, CancellationToken ct);

    Task<byte[]> SetCurrencyActiveAsync(string code, bool isActive, byte[] rowVersion, int userId, CancellationToken ct);
}

/// <summary>Fisierele BNR (zilnic, ultimele 10 zile), citite de Infrastructure cu Erp.Bnr.</summary>
public interface IBnrRatesSource
{
    /// <summary>Cursurile ultimelor zile bancare (fisierul pe 10 zile; daca nu e disponibil, cel zilnic).</summary>
    Task<IReadOnlyList<ExchangeRateDto>> GetRecentAsync(CancellationToken ct);
}

/// <summary>Lista ecranului Cursuri valutare; implicit ultimele 7 zile, cel mult 93 de zile o data.</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record GetExchangeRates(DateOnly? From = null, DateOnly? To = null) : IQuery<ExchangeRatesDto>;

/// <summary>Valutele (RON intai, apoi cele active), pentru listele de alegere: moneda conturilor bancare si a documentelor.</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record GetCurrencies : IQuery<IReadOnlyList<CurrencyDto>>;

/// <summary>Cursul folosit pe documente pentru o moneda si o zi (ultimul curs anterior daca ziua nu are curs).</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record GetExchangeRate(string Currency, DateOnly Date) : IQuery<ExchangeRateDto>;

/// <summary>Curs manual (macheta 07b): contracte cu curs fix sau rapoarte interne; motiv obligatoriu; nu inlocuieste cursul BNR.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ExchangeRateManageManual)]
[Audited("Core.ExchangeRate.SaveManual")]
public sealed record SaveManualExchangeRate(int? Id, string CurrencyCode, DateOnly RateDate, string Rate, int Multiplier, string Note, byte[]? RowVersion)
    : ICommand<SavedEntity>;

/// <summary>„Preia acum de la BNR”: cursurile ultimelor zile bancare; ce exista deja nu se schimba.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ExchangeRateManageManual)]
[Audited("Core.ExchangeRate.ImportBnr")]
public sealed record ImportBnrRates : ICommand<BnrImportResultDto>;

/// <summary>Activarea sau dezactivarea unei monede („+ monedă” din macheta 07b); RON ramane activ.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.NomenclatureManage)]
[Audited("Core.Currency.SetActive")]
public sealed record SetCurrencyActive(string Code, bool IsActive, byte[] RowVersion) : ICommand<byte[]>;

public sealed record SetCurrencyActiveBody(bool IsActive, byte[] RowVersion);

internal static class ExchangeRateRules
{
    public const int DefaultDays = 7;
    public const int MaxDays = 93;

    /// <summary>Cursul: cel mult 6 zecimale (decimal(19,6)), pozitiv; virgula romaneasca se accepta.</summary>
    public static bool TryRate(string? value, out decimal rate) => Decimals.TryParse(value?.Trim().Replace(',', '.'), 6, out rate) && rate > 0;
}

internal sealed class SaveManualExchangeRateValidator : AbstractValidator<SaveManualExchangeRate>
{
    public SaveManualExchangeRateValidator()
    {
        RuleFor(x => x.CurrencyCode).Matches("^[A-Z]{3}$").WithMessage("Alegeți moneda.")
            .NotEqual("RON").WithMessage("Leul e moneda de bază; nu are curs.");
        RuleFor(x => x.Rate).Must(r => ExchangeRateRules.TryRate(r, out _)).WithMessage("Cursul este un număr pozitiv, cu cel mult 6 zecimale.");
        RuleFor(x => x.Multiplier).Must(m => m is 1 or 10 or 100 or 1000).WithMessage("Multiplicatorul este 1, 10, 100 sau 1000.");
        RuleFor(x => x.Note).NotEmpty().WithMessage("Motivul este obligatoriu pentru un curs manual.").MaximumLength(500)
            .WithMessage("Motivul are cel mult 500 de caractere.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class GetExchangeRatesHandler(IExchangeRateStore store, TimeProvider clock) : IQueryHandler<GetExchangeRates, ExchangeRatesDto>
{
    public Task<ExchangeRatesDto> Handle(GetExchangeRates request, CancellationToken cancellationToken)
    {
        var to = request.To ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var from = request.From ?? to.AddDays(-(ExchangeRateRules.DefaultDays - 1));
        if (from > to || to.DayNumber - from.DayNumber >= ExchangeRateRules.MaxDays)
        {
            throw new ErpValidationException(new Dictionary<string, string[]>
            {
                ["from"] = [$"Intervalul are cel mult {ExchangeRateRules.MaxDays} de zile, iar începutul e înaintea sfârșitului."],
            });
        }

        return store.ListAsync(from, to, cancellationToken);
    }
}

internal sealed class GetCurrenciesHandler(IExchangeRateStore store) : IQueryHandler<GetCurrencies, IReadOnlyList<CurrencyDto>>
{
    public Task<IReadOnlyList<CurrencyDto>> Handle(GetCurrencies request, CancellationToken cancellationToken) => store.ListCurrenciesAsync(cancellationToken);
}

internal sealed class GetExchangeRateHandler(IExchangeRateProvider provider) : IQueryHandler<GetExchangeRate, ExchangeRateDto>
{
    public Task<ExchangeRateDto> Handle(GetExchangeRate request, CancellationToken cancellationToken) =>
        provider.GetRateAsync(request.Currency.Trim().ToUpperInvariant(), request.Date, cancellationToken);
}

internal sealed class SaveManualExchangeRateHandler(IExchangeRateStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveManualExchangeRate, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveManualExchangeRate request, CancellationToken cancellationToken)
    {
        ExchangeRateRules.TryRate(request.Rate, out var rate);
        var record = new ManualRateRecord(request.Id, request.CurrencyCode, request.RateDate, rate, request.Multiplier, request.Note.Trim(),
            request.RowVersion);
        var saved = await store.SaveManualAsync(record, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ExchangeRate", saved.Id, new { record.CurrencyCode, record.RateDate, record.Rate, record.Multiplier, record.Note });
        return saved;
    }
}

internal sealed class ImportBnrRatesHandler(IBnrRatesSource source, IExchangeRateWriter writer, IAuditScope audit)
    : ICommandHandler<ImportBnrRates, BnrImportResultDto>
{
    public async Task<BnrImportResultDto> Handle(ImportBnrRates request, CancellationToken cancellationToken)
    {
        var rates = await source.GetRecentAsync(cancellationToken).ConfigureAwait(false);
        var added = await writer.SaveBnrRatesAsync(rates, cancellationToken).ConfigureAwait(false);
        var result = new BnrImportResultDto(rates.Select(r => r.RateDate).Distinct().Count(), added.Count,
            rates.Count == 0 ? null : rates.Max(r => r.RateDate));
        audit.Describe("Core.ExchangeRate", null, new { Source = "BNR", result.Days, result.Added, result.LastRateDate });
        return result;
    }
}

internal sealed class SetCurrencyActiveHandler(IExchangeRateStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SetCurrencyActive, byte[]>
{
    public async Task<byte[]> Handle(SetCurrencyActive request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var version = await store.SetCurrencyActiveAsync(code, request.IsActive, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Core.Currency", null, new { Code = code, request.IsActive });
        return version;
    }
}
