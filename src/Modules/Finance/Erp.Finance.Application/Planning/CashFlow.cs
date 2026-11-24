using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Planning;
using FluentValidation;

namespace Erp.Finance.Application.Planning;

/// <summary>Sursa unei sume din prognoza (F5 3.6): 1 sold, 2 partida deschisa, 3 OP neconfirmat (ciorna), 4 propunere, 5 suma planificata.</summary>
public enum CashFlowSource : byte
{
    Balance = 1,
    OpenItem = 2,
    PaymentOrder = 3,
    Proposal = 4,
    PlanItem = 5,
}

public sealed record ForecastAccountRow(int TreasuryAccountId, string Code, string Name, byte Kind, string CurrencyCode, decimal Balance);

public sealed record ForecastOpenItemRow(long OpenItemId, int PartnerId, byte Role, byte Kind, string DocumentNo, DateOnly DueDate, string CurrencyCode,
    decimal OpenAmount);

public sealed record ForecastProposalRow(long LineId, string Number, DateOnly PlannedPaymentDate, int PartnerId, int TreasuryAccountId, string CurrencyCode,
    decimal ProposedAmount);

public sealed record ForecastDraftOrderRow(int PaymentOrderId, DateOnly Date, int? PartnerId, int TreasuryAccountId, string CurrencyCode, decimal Amount);

public sealed record ForecastPlanRow(int Id, int CategoryId, byte Direction, string Description, string CurrencyCode, decimal Amount, int? TreasuryAccountId,
    DateOnly FirstDate, Recurrence Recurrence, byte? RecurrenceDay, DateOnly? EndDate, byte Probability);

public sealed record ForecastCardRow(int Id, DateOnly Date, decimal GrossAmount);

/// <summary>O linie de istoric: PartnerId null = media firmei.</summary>
public sealed record ForecastBehaviorRow(int? PartnerId, int SampleInvoices, int? DaysFrom, int? DaysTo, bool IsUncollectible, decimal AmountShare,
    decimal AvgDaysLate);

public sealed record CashFlowInputs(IReadOnlyList<ForecastAccountRow> Accounts, IReadOnlyList<ForecastOpenItemRow> OpenItems,
    IReadOnlyList<ForecastProposalRow> Proposals, IReadOnlyList<ForecastDraftOrderRow> DraftOrders, IReadOnlyList<ForecastPlanRow> PlanItems,
    IReadOnlyList<ForecastCardRow> CardSettlements, IReadOnlyList<ForecastBehaviorRow> Behavior);

public sealed record CashFlowCategoryDto(int Id, string Code, string Name, byte Direction, short SortOrder, int? ParentId, bool IsSystem, bool IsActive);

/// <summary>Setarile Fin.Forecast.* (F5 3.4): CollectionMode 1 pe intervale (implicit), 2 la scadenta; MinSampleInvoices (5); HistoryMonths (12).</summary>
public sealed record ForecastSettings(byte CollectionMode, int MinSampleInvoices, int HistoryMonths);

public sealed record DelayBucketDto(string Code, string Name, int? DaysFrom, int? DaysTo, bool IsUncollectible);

public sealed record CashFlowPlanItemDto(int Id, int CategoryId, string CategoryCode, string CategoryName, byte Direction, string Description,
    string CurrencyCode, string Amount, int? TreasuryAccountId, string? TreasuryAccountCode, DateOnly FirstDate, Recurrence Recurrence, byte? RecurrenceDay,
    DateOnly? EndDate, bool IsActive, byte Probability, byte[] RowVersion);

public sealed record CashFlowPlanItemRecord(int? Id, int CategoryId, string Description, string CurrencyCode, decimal Amount, int? TreasuryAccountId,
    DateOnly FirstDate, Recurrence Recurrence, byte? RecurrenceDay, DateOnly? EndDate, bool IsActive, byte Probability, byte[]? RowVersion);

/// <summary>O suma din prognoza, cu data deja adusa in orizont (cel mai devreme ziua de pornire). CategoryId null = sold de pornire.</summary>
public sealed record CashFlowEntry(DateOnly Date, int? CategoryId, int? TreasuryAccountId, string CurrencyCode, decimal Amount, decimal AmountRon,
    CashFlowSource Source, bool IsUncertain);

/// <summary>Accesul la datele prognozei de cash-flow (fin.usp_CashFlow_ForecastInputs, NET-06) si la nomenclatoarele ei.</summary>
public interface ICashFlowStore
{
    Task<CashFlowInputs> InputsAsync(int companyId, DateOnly start, DateOnly to, int? treasuryAccountId, string? currencyCode, CancellationToken ct);

    Task<IReadOnlyList<CashFlowCategoryDto>> CategoriesAsync(CancellationToken ct);

    Task<ForecastSettings> SettingsAsync(int companyId, CancellationToken ct);

    Task<int> RecalculateBehaviorAsync(int companyId, DateOnly asOf, int historyMonths, int userId, CancellationToken ct);

    Task<IReadOnlyList<DelayBucketDto>> BucketsAsync(int companyId, CancellationToken ct);

    Task SaveBucketsAsync(int companyId, IReadOnlyList<DelayBucketDefinition> buckets, int userId, CancellationToken ct);

    Task<IReadOnlyList<CashFlowPlanItemDto>> PlanItemsAsync(int companyId, bool includeInactive, CancellationToken ct);

    Task<int> SavePlanItemAsync(int companyId, CashFlowPlanItemRecord item, int userId, CancellationToken ct);

    Task DeletePlanItemAsync(int companyId, int id, CancellationToken ct);
}

/// <summary>Rezultatul calculului: soldurile de pornire pe cont, sumele scenariului de baza si, la cerere, ale celui prudent.</summary>
public sealed record CashFlowComputation(IReadOnlyList<(ForecastAccountRow Account, decimal BalanceRon)> Accounts, IReadOnlyList<CashFlowEntry> Entries,
    IReadOnlyList<CashFlowEntry>? PrudentEntries);

/// <summary>
/// Calculul prognozei de cash-flow (F5 4.4): soldul scriptic al conturilor incluse, incasarile asteptate in transe dupa istoricul clientului
/// (CashFlowRules), platile asteptate la scadenta, propunerile la data planificata, OP-urile in ciorna, sumele planificate desfasurate pe
/// recurenta si inmultite cu probabilitatea si decontarile de card. Valuta se converteste la cursul BNR al zilei de pornire.
/// </summary>
public sealed class CashFlowForecaster(ICashFlowStore store, IExchangeRateProvider rates)
{
    public const string CustomersCategory = "INC-CLIENTI";
    public const string OtherInflowsCategory = "INC-ALTE";
    public const string SuppliersCategory = "PLATI-FURNIZORI";

    public async Task<CashFlowComputation> ComputeAsync(int companyId, DateOnly start, DateOnly to, int? treasuryAccountId, string? currencyCode,
        bool prudent, int extraDays, CancellationToken ct)
    {
        var inputs = await store.InputsAsync(companyId, start, to, treasuryAccountId, currencyCode, ct).ConfigureAwait(false);
        var categories = (await store.CategoriesAsync(ct).ConfigureAwait(false)).ToDictionary(c => c.Code, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var settings = await store.SettingsAsync(companyId, ct).ConfigureAwait(false);
        var perUnit = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["RON"] = 1m };
        async Task<decimal> Ron(string currency, decimal amount)
        {
            if (!perUnit.TryGetValue(currency, out var rate))
            {
                rate = (await rates.GetRateAsync(currency, start, ct).ConfigureAwait(false)).PerUnit;
                perUnit[currency] = rate;
            }

            return Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
        }

        var accounts = new List<(ForecastAccountRow, decimal)>();
        foreach (var account in inputs.Accounts)
        {
            accounts.Add((account, await Ron(account.CurrencyCode, account.Balance).ConfigureAwait(false)));
        }

        DateOnly Clamp(DateOnly date) => date < start ? start : date;
        var common = new List<CashFlowEntry>();
        var customers = inputs.OpenItems.Where(i => i.Role == 1 && i.OpenAmount > 0 && i.Kind != 8).ToList();
        foreach (var item in inputs.OpenItems.Except(customers))
        {
            var category = item.Role == 1 ? CustomersCategory : SuppliersCategory;
            common.Add(new CashFlowEntry(Clamp(item.DueDate), categories[category], null, item.CurrencyCode, item.OpenAmount,
                await Ron(item.CurrencyCode, item.OpenAmount).ConfigureAwait(false), CashFlowSource.OpenItem, false));
        }

        foreach (var line in inputs.Proposals)
        {
            common.Add(new CashFlowEntry(Clamp(line.PlannedPaymentDate), categories[SuppliersCategory], line.TreasuryAccountId, line.CurrencyCode,
                -line.ProposedAmount, await Ron(line.CurrencyCode, -line.ProposedAmount).ConfigureAwait(false), CashFlowSource.Proposal, false));
        }

        foreach (var order in inputs.DraftOrders)
        {
            common.Add(new CashFlowEntry(Clamp(order.Date), categories[SuppliersCategory], order.TreasuryAccountId, order.CurrencyCode, -order.Amount,
                await Ron(order.CurrencyCode, -order.Amount).ConfigureAwait(false), CashFlowSource.PaymentOrder, false));
        }

        foreach (var plan in inputs.PlanItems)
        {
            var signed = plan.Direction switch { 1 => Math.Abs(plan.Amount), 2 => -Math.Abs(plan.Amount), _ => plan.Amount };
            var amount = Math.Round(signed * plan.Probability / 100m, 2, MidpointRounding.AwayFromZero);
            foreach (var date in CashFlowRules.Occurrences(plan.FirstDate, plan.Recurrence, plan.RecurrenceDay, plan.EndDate, start, to))
            {
                common.Add(new CashFlowEntry(date, plan.CategoryId, plan.TreasuryAccountId, plan.CurrencyCode, amount,
                    await Ron(plan.CurrencyCode, amount).ConfigureAwait(false), CashFlowSource.PlanItem, false));
            }
        }

        foreach (var card in inputs.CardSettlements)
        {
            common.Add(new CashFlowEntry(Clamp(card.Date), categories[OtherInflowsCategory], null, "RON", card.GrossAmount, card.GrossAmount,
                CashFlowSource.OpenItem, false));
        }

        var distributions = inputs.Behavior.GroupBy(b => b.PartnerId).ToDictionary(g => g.Key ?? 0, g => (Sample: g.First().SampleInvoices,
            Shares: (IReadOnlyList<DelayShare>)g.Select(b => new DelayShare(b.DaysFrom ?? int.MinValue, b.DaysTo, b.AmountShare,
                (int)Math.Round(b.AvgDaysLate, MidpointRounding.AwayFromZero), b.IsUncollectible)).ToList()));
        distributions.TryGetValue(0, out var company);

        async Task<List<CashFlowEntry>> Collections(bool isPrudent)
        {
            var result = new List<CashFlowEntry>();
            foreach (var item in customers)
            {
                IReadOnlyList<DelayShare> shares = [];
                if (settings.CollectionMode != 2)
                {
                    var found = distributions.TryGetValue(item.PartnerId, out var partner);
                    shares = CashFlowPeriods.Choose(found ? partner.Sample : 0, found ? partner.Shares : null, company.Sample, company.Shares,
                        settings.MinSampleInvoices);
                }

                foreach (var tranche in CashFlowRules.Collections(item.OpenAmount, item.DueDate, start, shares, isPrudent, isPrudent ? extraDays : 0))
                {
                    result.Add(new CashFlowEntry(tranche.Date, categories[CustomersCategory], null, item.CurrencyCode, tranche.Amount,
                        await Ron(item.CurrencyCode, tranche.Amount).ConfigureAwait(false), CashFlowSource.OpenItem, tranche.IsUncertain));
                }
            }

            return result;
        }

        var entries = common.Concat(await Collections(false).ConfigureAwait(false)).Where(e => e.Date <= to).ToList();
        var prudentEntries = prudent ? common.Concat(await Collections(true).ConfigureAwait(false)).Where(e => e.Date <= to).ToList() : null;
        return new CashFlowComputation(accounts, entries, prudentEntries);
    }
}

public sealed record CashFlowPeriodDto(DateOnly Start, DateOnly End, string OpeningRon, string InflowsRon, string OutflowsRon, string ClosingRon);

public sealed record CashFlowRowDto(int CategoryId, string Code, string Name, byte Direction, IReadOnlyList<string> AmountsRon, string TotalRon);

/// <summary>Un scenariu: perioadele cu soldurile, randurile pe categorii si randul separat al incasarilor incerte (nu intra in sold).</summary>
public sealed record CashFlowScenarioDto(IReadOnlyList<CashFlowPeriodDto> Periods, IReadOnlyList<CashFlowRowDto> Rows, IReadOnlyList<string> UncertainRon,
    string MinimumBalanceRon, DateOnly? MinimumBalanceDate);

public sealed record CashFlowAccountDto(int TreasuryAccountId, string Code, string Name, string CurrencyCode, string Balance, string BalanceRon);

public sealed record CashFlowForecastDto(DateOnly Start, DateOnly From, DateOnly To, Granularity Granularity, string OpeningBalanceRon,
    IReadOnlyList<CashFlowAccountDto> Accounts, CashFlowScenarioDto Base, CashFlowScenarioDto? Prudent, IReadOnlyList<string> EmptyCategories);

/// <summary>
/// Prognoza de cash-flow (F5 4.4; macheta f5-03) pe interval si granularitate, pe toata firma sau pe un cont ori o moneda; optional si
/// scenariul prudent (incasarile cu un interval mai tarziu, plus zile suplimentare, fara restante). Orizontul are limita granularitatii (51711).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowView)]
public sealed record GetCashFlowForecast(DateOnly? From = null, DateOnly? To = null, Granularity Granularity = Granularity.Week, int? TreasuryAccountId = null,
    string? CurrencyCode = null, bool Prudent = false, int ExtraDays = 0) : IQuery<CashFlowForecastDto>;

internal sealed class GetCashFlowForecastHandler(CashFlowForecaster forecaster, ICashFlowStore store, ICurrentContext context, TimeProvider time)
    : IQueryHandler<GetCashFlowForecast, CashFlowForecastDto>
{
    private static readonly string[] Expected = ["SALARII", "TAXE"];

    public async Task<CashFlowForecastDto> Handle(GetCashFlowForecast request, CancellationToken cancellationToken)
    {
        var start = BusinessDay.Today(time);
        var (from, to, periods) = CashFlowHorizon.Resolve(start, request.From, request.To, request.Granularity, request.ExtraDays);
        var currency = string.IsNullOrWhiteSpace(request.CurrencyCode) ? null : request.CurrencyCode.Trim().ToUpperInvariant();
        var result = await forecaster.ComputeAsync(context.CompanyId, start, to, request.TreasuryAccountId, currency, request.Prudent, request.ExtraDays,
            cancellationToken).ConfigureAwait(false);
        var categories = await store.CategoriesAsync(cancellationToken).ConfigureAwait(false);
        var opening = result.Accounts.Sum(a => a.BalanceRon);
        var baseScenario = CashFlowReport.Scenario(opening, from, periods, request.Granularity, result.Entries, categories);
        var empty = categories.Where(c => Expected.Contains(c.Code, StringComparer.OrdinalIgnoreCase) && result.Entries.All(e => e.CategoryId != c.Id))
            .Select(c => c.Name).ToList();
        return new CashFlowForecastDto(start, from, to, request.Granularity, Money.Format(opening),
            result.Accounts.Select(a => new CashFlowAccountDto(a.Account.TreasuryAccountId, a.Account.Code, a.Account.Name, a.Account.CurrencyCode,
                Money.Format(a.Account.Balance), Money.Format(a.BalanceRon))).ToList(),
            baseScenario,
            result.PrudentEntries is { } prudent ? CashFlowReport.Scenario(opening, from, periods, request.Granularity, prudent, categories) : null,
            empty);
    }
}

/// <summary>Orizontul prognozei (F5 4.4): de la azi (sau mai tarziu), implicit 90 de zile, in limita granularitatii (51711).</summary>
public static class CashFlowHorizon
{
    public static (DateOnly From, DateOnly To, IReadOnlyList<(DateOnly Start, DateOnly End)> Periods) Resolve(DateOnly start, DateOnly? from, DateOnly? to,
        Granularity granularity, int extraDays = 0)
    {
        var first = from is { } f && f > start ? f : start;
        var last = to ?? first.AddDays(90);
        if (last < first || !Enum.IsDefined(granularity) || extraDays is < 0 or > 365)
        {
            throw new ErpException(ErpErrorKind.Validation, CoreErrorCodes.Validation, "Intervalul, granularitatea sau zilele suplimentare nu sunt valide.");
        }

        var periods = CashFlowPeriods.Periods(first, last, granularity);
        if (periods.Count > CashFlowPeriods.MaxPeriods(granularity))
        {
            throw new ErpException(ErpErrorKind.Validation, "FIN_CASHFLOW_HORIZON_TOO_LONG",
                "Orizontul depășește limita granularității: 92 de zile, 104 săptămâni sau 36 de luni.");
        }

        return (first, last, periods);
    }
}

/// <summary>Gruparea sumelor prognozei pe perioade si categorii (F5 4.4: sold initial, intrari si iesiri pe categorii, sold final).</summary>
public static class CashFlowReport
{
    public static CashFlowScenarioDto Scenario(decimal openingRon, DateOnly from, IReadOnlyList<(DateOnly Start, DateOnly End)> periods, Granularity granularity,
        IReadOnlyList<CashFlowEntry> entries, IReadOnlyList<CashFlowCategoryDto> categories)
    {
        var index = periods.Select((p, i) => (p.Start, i)).ToDictionary(x => x.Start, x => x.i);
        var counted = entries.Where(e => !e.IsUncertain).ToList();
        var opening = openingRon + counted.Where(e => e.Date < from).Sum(e => e.AmountRon);
        var inPeriods = counted.Where(e => e.Date >= from).ToList();
        int Period(CashFlowEntry e) => index[CashFlowPeriods.PeriodStart(e.Date, granularity, from)];

        var rows = categories.Where(c => inPeriods.Any(e => e.CategoryId == c.Id)).OrderBy(c => c.SortOrder).Select(c =>
        {
            var amounts = new decimal[periods.Count];
            foreach (var e in inPeriods.Where(e => e.CategoryId == c.Id))
            {
                amounts[Period(e)] += e.AmountRon;
            }

            return new CashFlowRowDto(c.Id, c.Code, c.Name, c.Direction, amounts.Select(Money.Format).ToList(), Money.Format(amounts.Sum()));
        }).ToList();

        var uncertain = new decimal[periods.Count];
        foreach (var e in entries.Where(e => e.IsUncertain))
        {
            uncertain[e.Date < from ? 0 : Period(e)] += e.AmountRon;
        }

        var result = new List<CashFlowPeriodDto>();
        var balance = opening;
        var minimum = opening;
        DateOnly? minimumDate = null;
        for (var i = 0; i < periods.Count; i++)
        {
            var mine = inPeriods.Where(e => Period(e) == i).ToList();
            var inflows = mine.Where(e => e.AmountRon > 0).Sum(e => e.AmountRon);
            var outflows = -mine.Where(e => e.AmountRon < 0).Sum(e => e.AmountRon);
            var closing = balance + inflows - outflows;
            result.Add(new CashFlowPeriodDto(periods[i].Start, periods[i].End, Money.Format(balance), Money.Format(inflows), Money.Format(outflows),
                Money.Format(closing)));
            if (closing < minimum)
            {
                minimum = closing;
                minimumDate = periods[i].End;
            }

            balance = closing;
        }

        return new CashFlowScenarioDto(result, rows, uncertain.Select(Money.Format).ToList(), Money.Format(minimum), minimumDate);
    }
}

// ---------- Istoricul de plata si intervalele ----------

/// <summary>Intervalele de intarziere ale firmei (F5 3.4); o firma fara intervale le vede pe cele livrate.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowView)]
public sealed record GetPaymentDelayBuckets : IQuery<IReadOnlyList<DelayBucketDto>>;

internal sealed class GetPaymentDelayBucketsHandler(ICashFlowStore store, ICurrentContext context) : IQueryHandler<GetPaymentDelayBuckets, IReadOnlyList<DelayBucketDto>>
{
    public async Task<IReadOnlyList<DelayBucketDto>> Handle(GetPaymentDelayBuckets request, CancellationToken cancellationToken)
    {
        var buckets = await store.BucketsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        return buckets.Count > 0 ? buckets
            : CashFlowPeriods.DefaultBuckets.Select(b => new DelayBucketDto(b.Code, b.Name, b.DaysFrom, b.DaysTo, b.IsUncollectible)).ToList();
    }
}

/// <summary>Salveaza intervalele (toata axa, fara suprapuneri) si recalculeaza imediat istoricul firmei (F5 4.6).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowPlan)]
[Audited("Fin.PaymentDelayBucket.Save")]
public sealed record SavePaymentDelayBuckets(IReadOnlyList<DelayBucketDto> Buckets) : ICommand<IReadOnlyList<DelayBucketDto>>;

internal sealed class SavePaymentDelayBucketsValidator : AbstractValidator<SavePaymentDelayBuckets>
{
    public SavePaymentDelayBucketsValidator()
    {
        RuleFor(x => x.Buckets).NotEmpty().Must(b => b.Count <= 20)
            .Must(b => CashFlowPeriods.CoverTheAxis(b.Select(x => new DelayBucketDefinition(x.Code, x.Name, x.DaysFrom, x.DaysTo, x.IsUncollectible)).ToList()))
            .WithMessage("Intervalele trebuie să acopere toate zilele, în ordine, fără suprapuneri și fără goluri.")
            .Must(b => b.Select(x => x.Code.Trim().ToUpperInvariant()).Distinct().Count() == b.Count).WithMessage("Codurile intervalelor trebuie să fie unice.");
        RuleForEach(x => x.Buckets).ChildRules(b =>
        {
            b.RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
            b.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        });
    }
}

internal sealed class SavePaymentDelayBucketsHandler(ICashFlowStore store, ICurrentContext context, TimeProvider time, IAuditScope audit)
    : ICommandHandler<SavePaymentDelayBuckets, IReadOnlyList<DelayBucketDto>>
{
    public async Task<IReadOnlyList<DelayBucketDto>> Handle(SavePaymentDelayBuckets request, CancellationToken cancellationToken)
    {
        await store.SaveBucketsAsync(context.CompanyId, request.Buckets.Select(b => new DelayBucketDefinition(b.Code.Trim(), b.Name.Trim(), b.DaysFrom, b.DaysTo,
            b.IsUncollectible)).ToList(), context.UserId, cancellationToken).ConfigureAwait(false);
        var settings = await store.SettingsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        await store.RecalculateBehaviorAsync(context.CompanyId, BusinessDay.Today(time), settings.HistoryMonths, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.PaymentDelayBucket", context.CompanyId, request.Buckets);
        return await store.BucketsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Recalculeaza la cerere istoricul de plata al clientilor (F5 3.4; altfel jobul lunar). Intoarce numarul de clienti cu istoric.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowPlan)]
[Audited("Fin.PaymentBehavior.Recalculate")]
public sealed record RecalculatePaymentBehavior : ICommand<int>;

internal sealed class RecalculatePaymentBehaviorHandler(ICashFlowStore store, ICurrentContext context, TimeProvider time) : ICommandHandler<RecalculatePaymentBehavior, int>
{
    public async Task<int> Handle(RecalculatePaymentBehavior request, CancellationToken cancellationToken)
    {
        var settings = await store.SettingsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        return await store.RecalculateBehaviorAsync(context.CompanyId, BusinessDay.Today(time), settings.HistoryMonths, context.UserId, cancellationToken)
            .ConfigureAwait(false);
    }
}

// ---------- Categorii si sume planificate ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowView)]
public sealed record GetCashFlowCategories : IQuery<IReadOnlyList<CashFlowCategoryDto>>;

internal sealed class GetCashFlowCategoriesHandler(ICashFlowStore store) : IQueryHandler<GetCashFlowCategories, IReadOnlyList<CashFlowCategoryDto>>
{
    public Task<IReadOnlyList<CashFlowCategoryDto>> Handle(GetCashFlowCategories request, CancellationToken cancellationToken) =>
        store.CategoriesAsync(cancellationToken);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowView)]
public sealed record GetCashFlowPlanItems(bool IncludeInactive = false) : IQuery<IReadOnlyList<CashFlowPlanItemDto>>;

internal sealed class GetCashFlowPlanItemsHandler(ICashFlowStore store, ICurrentContext context) : IQueryHandler<GetCashFlowPlanItems, IReadOnlyList<CashFlowPlanItemDto>>
{
    public Task<IReadOnlyList<CashFlowPlanItemDto>> Handle(GetCashFlowPlanItems request, CancellationToken cancellationToken) =>
        store.PlanItemsAsync(context.CompanyId, request.IncludeInactive, cancellationToken);
}

/// <summary>Adauga sau modifica o suma planificata (F5 3.5): suma pozitiva (directia vine din categorie; la transferuri, cu semn).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowPlan)]
[Audited("Fin.CashFlowPlanItem.Save")]
public sealed record SaveCashFlowPlanItem(int? Id, int CategoryId, string Description, string CurrencyCode, string Amount, int? TreasuryAccountId,
    DateOnly FirstDate, Recurrence Recurrence, byte? RecurrenceDay, DateOnly? EndDate, bool IsActive = true, byte Probability = 100, byte[]? RowVersion = null)
    : ICommand<int>;

internal sealed class SaveCashFlowPlanItemValidator : AbstractValidator<SaveCashFlowPlanItem>
{
    public SaveCashFlowPlanItemValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.Amount).Must(a => Money.ParseOptional(a) is { } v && v != 0).WithMessage("Suma trebuie să fie diferită de zero.");
        RuleFor(x => x.Recurrence).IsInEnum();
        RuleFor(x => x.RecurrenceDay).InclusiveBetween((byte)1, (byte)31).When(x => x.RecurrenceDay is not null);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.FirstDate).When(x => x.EndDate is not null);
        RuleFor(x => x.Probability).InclusiveBetween((byte)0, (byte)100);
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null);
    }
}

internal sealed class SaveCashFlowPlanItemHandler(ICashFlowStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveCashFlowPlanItem, int>
{
    public async Task<int> Handle(SaveCashFlowPlanItem request, CancellationToken cancellationToken)
    {
        var monthly = request.Recurrence is Recurrence.Monthly or Recurrence.Quarterly;
        var id = await store.SavePlanItemAsync(context.CompanyId, new CashFlowPlanItemRecord(request.Id, request.CategoryId, request.Description.Trim(),
            request.CurrencyCode.Trim().ToUpperInvariant(), Money.ParseOptional(request.Amount)!.Value, request.TreasuryAccountId, request.FirstDate,
            request.Recurrence, monthly ? request.RecurrenceDay ?? (byte)request.FirstDate.Day : null, request.EndDate, request.IsActive, request.Probability,
            request.RowVersion), context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CashFlowPlanItem", id, request with { RowVersion = null });
        return id;
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowPlan)]
[Audited("Fin.CashFlowPlanItem.Delete")]
public sealed record DeleteCashFlowPlanItem(int Id) : ICommand<bool>;

internal sealed class DeleteCashFlowPlanItemHandler(ICashFlowStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<DeleteCashFlowPlanItem, bool>
{
    public async Task<bool> Handle(DeleteCashFlowPlanItem request, CancellationToken cancellationToken)
    {
        await store.DeletePlanItemAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CashFlowPlanItem", request.Id, null);
        return true;
    }
}
