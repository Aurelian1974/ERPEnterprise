using Erp.Core.Contracts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using FluentValidation;

namespace Erp.Finance.Application.Fx;

/// <summary>Starea unei rulari de reevaluare (F4 3.6).</summary>
public enum FxRunStatus : byte
{
    Draft = 1,
    Validated = 2,
    Reversed = 3,
}

public sealed record FxRateRecord(string CurrencyCode, decimal Rate, int Multiplier, DateOnly RateDate, string Source);

public sealed record FxRunItemDto(int Id, string Number, int PeriodYear, int PeriodMonth, DateOnly RevaluationDate, DateOnly RateDate, FxRunStatus Status,
    string TotalGainRon, string TotalLossRon, string NetRon, int LineCount, bool IsStale, DateTime GeneratedAt, DateTime? ValidatedAt, DateTime? ReversedAt);

public sealed record FxRunRateDto(string CurrencyCode, string Rate, int Multiplier, DateOnly RateDate, string Source);

public sealed record FxRunCurrencyDto(string CurrencyCode, int Lines, string ForeignAmount, string CarryingAmountRon, string RevaluedAmountRon, string GainRon,
    string LossRon);

public sealed record FxRunLineDto(long Id, byte ElementKind, long? OpenItemId, string? DocumentNo, int? PartnerId, string? PartnerName, PartnerRole? Role,
    string CurrencyCode, string ForeignAmount, string Rate, string CarryingAmountRon, string RevaluedAmountRon, string DifferenceRon, string GlAccountCode,
    string? CounterGlAccountCode, bool IsCorrection = false, int? CorrectedRunId = null);

public sealed record FxRunDto(int Id, string Number, int PeriodYear, int PeriodMonth, DateOnly RevaluationDate, DateOnly RateDate, FxRunStatus Status,
    bool IncludesAdvances, string TotalGainRon, string TotalLossRon, DateTime GeneratedAt, DateTime? ValidatedAt, string? ValidatedByName, DateTime? ReversedAt,
    int LineCount, IReadOnlyList<FxRunRateDto> Rates, IReadOnlyList<FxRunCurrencyDto> Currencies, IReadOnlyList<FxRunLineDto> Lines, byte[] RowVersion);

/// <summary>Rezultatul validarii sau al stornarii: rularea, totalurile pentru eveniment si corectiile diferentelor realizate.</summary>
public sealed record FxRunChanged(int Id, string Number, int PeriodYear, int PeriodMonth, DateOnly RevaluationDate, int LineCount, byte[] RowVersion,
    IReadOnlyList<ReevaluareTotal> Totals, IReadOnlyList<FxDifferenceMade> Corrections);

/// <summary>Accesul la date pentru reevaluarea valutara (fin.usp_FxRevaluation_*, NET-06).</summary>
public interface IFxRevaluationStore
{
    Task<(IReadOnlyList<string> Currencies, (int Year, int Month)? LastValidated)> InputsAsync(int companyId, DateOnly revaluationDate, bool includeAdvances,
        CancellationToken ct);

    Task<int> GenerateAsync(int companyId, int year, int month, DateOnly revaluationDate, DateOnly rateDate, bool includeAdvances,
        IReadOnlyList<FxRateRecord> rates, int userId, CancellationToken ct);

    Task<FxRunChanged> ValidateAsync(int companyId, int runId, byte[] rowVersion, int userId, CancellationToken ct);

    Task<FxRunChanged> ReverseAsync(int companyId, int runId, byte[] rowVersion, DateOnly today, int userId, CancellationToken ct);

    Task DeleteAsync(int companyId, int runId, byte[] rowVersion, CancellationToken ct);

    Task<IReadOnlyList<FxRunItemDto>> ListAsync(int companyId, int? year, CancellationToken ct);

    Task<FxRunDto?> GetAsync(int companyId, int runId, CancellationToken ct);
}

internal static class FxRevaluationRules
{
    public static ErpException NotFound() => ErpException.NotFound("FIN_FX_RUN_NOT_FOUND", "Rularea de reevaluare nu există.");

    public static ErpException RateMissing(string currency, DateOnly date) =>
        ErpException.Conflict("FIN_FX_RATE_MISSING", $"Lipsește cursul BNR pentru {currency} în ultima zi bancară a lunii ({date:dd.MM.yyyy}).");

    /// <summary>Cursul de reevaluare e cel din ultima zi bancara a lunii (OMFP 1802/2014): cel mai recent curs pana la sfarsitul lunii, din ultima saptamana.</summary>
    public const int MaxRateAgeDays = 7;
}

// ---------- Lista si fisa ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxView)]
public sealed record GetFxRevaluations(int? Year = null) : IQuery<IReadOnlyList<FxRunItemDto>>;

internal sealed class GetFxRevaluationsHandler(IFxRevaluationStore store, ICurrentContext context) : IQueryHandler<GetFxRevaluations, IReadOnlyList<FxRunItemDto>>
{
    public Task<IReadOnlyList<FxRunItemDto>> Handle(GetFxRevaluations request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.Year, cancellationToken);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxView)]
public sealed record GetFxRevaluation(int Id) : IQuery<FxRunDto>;

internal sealed class GetFxRevaluationHandler(IFxRevaluationStore store, ICurrentContext context) : IQueryHandler<GetFxRevaluation, FxRunDto>
{
    public async Task<FxRunDto> Handle(GetFxRevaluation request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw FxRevaluationRules.NotFound();
}

// ---------- Generare ----------

/// <summary>
/// Genereaza (sau regenereaza) ciorna reevaluarii unei luni incheiate (F4 4.4 pasii 1–2): cursurile BNR din ultima zi bancara a lunii, copiate
/// in rulare, si liniile pe partidele in valuta cu rest la sfarsitul lunii (avansurile doar cu Fin.Fx.RevalueAdvances). Numarul: „RV an-luna/n”.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxRevalue)]
[Audited("Fin.Fx.Generate")]
public sealed record GenerateFxRevaluation(int Year, int Month) : ICommand<int>;

internal sealed class GenerateFxRevaluationValidator : AbstractValidator<GenerateFxRevaluation>
{
    public GenerateFxRevaluationValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12).WithMessage("Luna este între 1 și 12.");
    }
}

internal sealed class GenerateFxRevaluationHandler(IFxRevaluationStore store, IExchangeRateProvider rates, IFinanceSettings settings, ICurrentContext context,
    TimeProvider time, IAuditScope audit) : ICommandHandler<GenerateFxRevaluation, int>
{
    public async Task<int> Handle(GenerateFxRevaluation request, CancellationToken cancellationToken)
    {
        var revaluationDate = new DateOnly(request.Year, request.Month, DateTime.DaysInMonth(request.Year, request.Month));
        if (revaluationDate >= BusinessDay.Today(time))
        {
            throw new ErpValidationException(new Dictionary<string, string[]> { ["month"] = ["Luna nu s-a încheiat încă."] });
        }

        var includeAdvances = await settings.RevalueAdvancesAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var (currencies, _) = await store.InputsAsync(context.CompanyId, revaluationDate, includeAdvances, cancellationToken).ConfigureAwait(false);
        var used = new List<FxRateRecord>();
        foreach (var currency in currencies)
        {
            ExchangeRateDto rate;
            try
            {
                rate = await rates.GetRateAsync(currency, revaluationDate, cancellationToken).ConfigureAwait(false);
            }
            catch (ErpException e) when (e.Kind == ErpErrorKind.NotFound)
            {
                throw FxRevaluationRules.RateMissing(currency, revaluationDate);
            }

            if (rate.RateDate < revaluationDate.AddDays(-FxRevaluationRules.MaxRateAgeDays))
            {
                throw FxRevaluationRules.RateMissing(currency, revaluationDate);
            }

            used.Add(new FxRateRecord(currency, rate.Rate, rate.Multiplier, rate.RateDate, rate.Source));
        }

        var rateDate = used.Count == 0 ? revaluationDate : used.Max(r => r.RateDate);
        var id = await store.GenerateAsync(context.CompanyId, request.Year, request.Month, revaluationDate, rateDate, includeAdvances, used, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FxRevaluationRun", id, new { request.Year, request.Month, Rates = used.Select(r => new { r.CurrencyCode, r.Rate, r.RateDate }) });
        return id;
    }
}

/// <summary>Sterge o rulare ciorna.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxRevalue)]
[Audited("Fin.Fx.DeleteDraft")]
public sealed record DeleteFxRevaluationDraft(int Id, byte[] RowVersion) : ICommand<bool>;

internal sealed class DeleteFxRevaluationDraftHandler(IFxRevaluationStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<DeleteFxRevaluationDraft, bool>
{
    public async Task<bool> Handle(DeleteFxRevaluationDraft request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(context.CompanyId, request.Id, request.RowVersion, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FxRevaluationRun", request.Id, null);
        return true;
    }
}

// ---------- Validare si storno ----------

/// <summary>
/// Valideaza rularea (F4 4.4 pasul 3): perioada lunii deschisa, valorile contabile si cursurile partidelor actualizate, corectiile platilor cu data
/// dupa sfarsitul lunii; evenimentele ReevaluareValutaraValidata (totalurile pe cont, contrapartida si moneda) si DiferentaCursInregistrata.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxRevalue)]
[Audited("Fin.Fx.Validate")]
public sealed record ValidateFxRevaluation(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class ValidateFxRevaluationHandler(IFxRevaluationStore store, IPeriodLock periods, IOutbox outbox, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ValidateFxRevaluation, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(ValidateFxRevaluation request, CancellationToken cancellationToken)
    {
        var run = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw FxRevaluationRules.NotFound();
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, run.RevaluationDate, cancellationToken).ConfigureAwait(false);
        var changed = await store.ValidateAsync(context.CompanyId, request.Id, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        await outbox.EnqueueAsync(new ReevaluareValutaraValidata(changed.Id, changed.PeriodYear, changed.PeriodMonth, changed.RevaluationDate, changed.Totals,
            changed.LineCount) { CompanyId = context.CompanyId }, cancellationToken).ConfigureAwait(false);
        await SettlementEvents.PublishAsync(outbox, new AllocationOutcome([], [], [], changed.Corrections), false, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FxRevaluationRun", request.Id, new { changed.Number, Totals = changed.Totals, Corrections = changed.Corrections.Count });
        return new FinanceSaved(request.Id, changed.RowVersion);
    }
}

/// <summary>
/// Storneaza o rulare validata (F4 4.5): perioada lunii deschisa, nicio rulare validata ulterioara; valorile contabile revin, corectiile se
/// inverseaza; evenimentele ReevaluareValutaraStornata si DiferentaCursInregistrata (inverse).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxReverseRevaluation)]
[Audited("Fin.Fx.Reverse")]
public sealed record ReverseFxRevaluation(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class ReverseFxRevaluationHandler(IFxRevaluationStore store, IPeriodLock periods, IOutbox outbox, ICurrentContext context, TimeProvider time,
    IAuditScope audit) : ICommandHandler<ReverseFxRevaluation, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(ReverseFxRevaluation request, CancellationToken cancellationToken)
    {
        var run = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw FxRevaluationRules.NotFound();
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, run.RevaluationDate, cancellationToken).ConfigureAwait(false);
        var today = BusinessDay.Today(time);
        var changed = await store.ReverseAsync(context.CompanyId, request.Id, request.RowVersion, today, context.UserId, cancellationToken).ConfigureAwait(false);
        await outbox.EnqueueAsync(new ReevaluareValutaraStornata(changed.Id, changed.PeriodYear, changed.PeriodMonth, today) { CompanyId = context.CompanyId },
            cancellationToken).ConfigureAwait(false);
        await SettlementEvents.PublishAsync(outbox, new AllocationOutcome([], [], [], changed.Corrections), true, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FxRevaluationRun", request.Id, new { changed.Number, Reversed = today });
        return new FinanceSaved(request.Id, changed.RowVersion);
    }
}
