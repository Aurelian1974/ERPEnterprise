using System.Text.Json;
using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Planning;
using FluentValidation;

namespace Erp.Finance.Application.Planning;

/// <summary>O linie de instantaneu: suma grupata pe perioada, categorie (null = sold de pornire), cont, moneda si sursa.</summary>
public sealed record SnapshotLineRecord(DateOnly PeriodStart, int? CategoryId, int? TreasuryAccountId, string CurrencyCode, decimal Amount, decimal AmountRon,
    CashFlowSource SourceKind);

public sealed record SnapshotHeader(int Id, string Name, DateTime TakenAt, DateOnly StartDate, DateOnly HorizonFrom, DateOnly HorizonTo, Granularity Granularity,
    int? TreasuryAccountId, string? CurrencyCode, bool IsAutomatic, decimal OpeningBalanceRon);

public sealed record CashFlowSnapshotItemDto(int Id, string Name, DateTime TakenAt, string? TakenByName, DateOnly StartDate, DateOnly HorizonFrom,
    DateOnly HorizonTo, Granularity Granularity, int? TreasuryAccountId, string? TreasuryAccountCode, string? CurrencyCode, bool IsAutomatic,
    string OpeningBalanceRon);

/// <summary>O suma reala (F5 4.5): AmountRon null = in moneda contului, de convertit.</summary>
public sealed record ActualRow(DateOnly Date, int? CategoryId, string CurrencyCode, decimal Amount, decimal? AmountRon);

public sealed record AlertRuleRow(int Id, int? TreasuryAccountId, string? TreasuryAccountCode, decimal MinBalanceRon, short HorizonDays, int? NotifyRoleId,
    string? NotifyRoleName, bool IsActive, DateOnly? LastAlertedOn);

public sealed record CashFlowAlertRuleDto(int Id, int? TreasuryAccountId, string? TreasuryAccountCode, string MinBalanceRon, short HorizonDays, int? NotifyRoleId,
    string? NotifyRoleName, bool IsActive, DateOnly? LastAlertedOn);

public sealed record CashFlowAlertRuleInput(int? Id, int? TreasuryAccountId, string MinBalanceRon, short HorizonDays, int? NotifyRoleId, bool IsActive);

/// <summary>Instantaneele, comparatia cu realizatul si alertele de cash-flow (NET-06).</summary>
public interface ICashFlowSnapshotStore
{
    Task<int> CreateAsync(int companyId, string name, int userId, DateOnly start, DateOnly from, DateOnly to, Granularity granularity, int? treasuryAccountId,
        string? currencyCode, bool isAutomatic, decimal openingBalanceRon, string parameters, IReadOnlyList<SnapshotLineRecord> lines, CancellationToken ct);

    Task<IReadOnlyList<CashFlowSnapshotItemDto>> ListAsync(int companyId, CancellationToken ct);

    Task<(SnapshotHeader Header, IReadOnlyList<(DateOnly PeriodStart, int? CategoryId, decimal AmountRon)> Lines)?> GetAsync(int companyId, int id,
        CancellationToken ct);

    Task<IReadOnlyList<ActualRow>> ActualsAsync(int companyId, DateOnly from, DateOnly to, int? treasuryAccountId, string? currencyCode, CancellationToken ct);

    Task<IReadOnlyList<AlertRuleRow>> AlertRulesAsync(int companyId, CancellationToken ct);

    Task SaveAlertRulesAsync(int companyId, IReadOnlyList<(int? Id, int? TreasuryAccountId, decimal MinBalanceRon, short HorizonDays, int? NotifyRoleId,
        bool IsActive)> rules, int userId, CancellationToken ct);

    Task<bool> MarkAlertedAsync(int companyId, int ruleId, DateOnly today, CancellationToken ct);
}

// ---------- Instantanee ----------

/// <summary>
/// Salveaza un instantaneu al prognozei (F5 3.6; manual sau de jobul de luni): soldurile de pornire pe cont si sumele scenariului de baza,
/// grupate pe perioada, categorie, cont, moneda si sursa (incasarile incerte nu intra). Instantaneul nu se mai modifica.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowSnapshot)]
[Audited("Fin.CashFlowSnapshot.Take")]
public sealed record TakeCashFlowSnapshot(string? Name = null, DateOnly? From = null, DateOnly? To = null, Granularity Granularity = Granularity.Week,
    int? TreasuryAccountId = null, string? CurrencyCode = null, bool IsAutomatic = false) : ICommand<int>;

internal sealed class TakeCashFlowSnapshotValidator : AbstractValidator<TakeCashFlowSnapshot>
{
    public TakeCashFlowSnapshotValidator() => RuleFor(x => x.Name).MaximumLength(200);
}

internal sealed class TakeCashFlowSnapshotHandler(CashFlowForecaster forecaster, ICashFlowStore cashFlow, ICashFlowSnapshotStore store, ICurrentContext context,
    TimeProvider time, IAuditScope audit) : ICommandHandler<TakeCashFlowSnapshot, int>
{
    public async Task<int> Handle(TakeCashFlowSnapshot request, CancellationToken cancellationToken)
    {
        var start = BusinessDay.Today(time);
        var to = request.To ?? (request.From is { } f && f > start ? f : start).AddDays(7 * 13 - 1);
        var (from, last, _) = CashFlowHorizon.Resolve(start, request.From, to, request.Granularity);
        var currency = string.IsNullOrWhiteSpace(request.CurrencyCode) ? null : request.CurrencyCode.Trim().ToUpperInvariant();
        var result = await forecaster.ComputeAsync(context.CompanyId, start, last, request.TreasuryAccountId, currency, false, 0, cancellationToken)
            .ConfigureAwait(false);
        var settings = await cashFlow.SettingsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);

        var lines = result.Accounts.Select(a => new SnapshotLineRecord(from, null, a.Account.TreasuryAccountId, a.Account.CurrencyCode, a.Account.Balance,
            a.BalanceRon, CashFlowSource.Balance)).ToList();
        lines.AddRange(result.Entries.Where(e => !e.IsUncertain)
            .GroupBy(e => (Period: CashFlowPeriods.PeriodStart(e.Date < from ? from : e.Date, request.Granularity, from), e.CategoryId, e.TreasuryAccountId,
                e.CurrencyCode, e.Source))
            .Select(g => new SnapshotLineRecord(g.Key.Period, g.Key.CategoryId, g.Key.TreasuryAccountId, g.Key.CurrencyCode, g.Sum(e => e.Amount),
                g.Sum(e => e.AmountRon), g.Key.Source)));
        var name = string.IsNullOrWhiteSpace(request.Name)
            ? $"{(request.IsAutomatic ? "Automat" : "Instantaneu")} {start.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)}"
            : request.Name.Trim();
        var parameters = JsonSerializer.Serialize(new
        {
            collectionMode = settings.CollectionMode, minSampleInvoices = settings.MinSampleInvoices, historyMonths = settings.HistoryMonths,
            accounts = result.Accounts.Select(a => a.Account.Code).ToList(),
        });
        var id = await store.CreateAsync(context.CompanyId, name, context.UserId, start, from, last, request.Granularity, request.TreasuryAccountId, currency,
            request.IsAutomatic, result.Accounts.Sum(a => a.BalanceRon), parameters, lines, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CashFlowSnapshot", id, new { name, from, last, request.Granularity, request.TreasuryAccountId, currency, request.IsAutomatic });
        return id;
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowView)]
public sealed record GetCashFlowSnapshots : IQuery<IReadOnlyList<CashFlowSnapshotItemDto>>;

internal sealed class GetCashFlowSnapshotsHandler(ICashFlowSnapshotStore store, ICurrentContext context)
    : IQueryHandler<GetCashFlowSnapshots, IReadOnlyList<CashFlowSnapshotItemDto>>
{
    public Task<IReadOnlyList<CashFlowSnapshotItemDto>> Handle(GetCashFlowSnapshots request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, cancellationToken);
}

// ---------- Prognoza vs realizat ----------

public sealed record VsActualPeriodDto(DateOnly Start, DateOnly End, bool HasActuals);

/// <summary>Pe categorie (CategoryId null = necategorizat): prognozat, realizat (null pentru perioadele viitoare) si abaterea (realizat - prognozat).</summary>
public sealed record VsActualRowDto(int? CategoryId, string Code, string Name, IReadOnlyList<string> ForecastRon, IReadOnlyList<string?> ActualRon,
    IReadOnlyList<string?> DeviationRon, string TotalForecastRon, string TotalActualRon, string TotalDeviationRon);

public sealed record CashFlowVsActualDto(int SnapshotId, string Name, DateTime TakenAt, Granularity Granularity, IReadOnlyList<VsActualPeriodDto> Periods,
    IReadOnlyList<VsActualRowDto> Rows, IReadOnlyList<string> NetForecastRon, IReadOnlyList<string?> NetActualRon);

/// <summary>
/// Prognoza dintr-un instantaneu fata de incasarile si platile reale (F5 4.5): pe saptamani (instantaneele zilnice se aduna pe saptamani,
/// cele lunare raman pe luni) si categorii, cu realizatul pana azi. Banca in valuta se converteste la cursul zilei instantaneului.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowView)]
public sealed record GetCashFlowForecastVsActual(int SnapshotId) : IQuery<CashFlowVsActualDto>;

internal sealed class GetCashFlowForecastVsActualHandler(ICashFlowSnapshotStore store, ICashFlowStore cashFlow, IExchangeRateProvider rates,
    ICurrentContext context, TimeProvider time) : IQueryHandler<GetCashFlowForecastVsActual, CashFlowVsActualDto>
{
    public async Task<CashFlowVsActualDto> Handle(GetCashFlowForecastVsActual request, CancellationToken cancellationToken)
    {
        var found = await store.GetAsync(context.CompanyId, request.SnapshotId, cancellationToken).ConfigureAwait(false)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_CASHFLOW_SNAPSHOT_NOT_FOUND", "Instantaneul nu există.");
        var (header, lines) = found;
        var today = BusinessDay.Today(time);
        var granularity = header.Granularity == Granularity.Day ? Granularity.Week : header.Granularity;
        var periods = CashFlowPeriods.Periods(header.HorizonFrom, header.HorizonTo, granularity);
        var index = periods.Select((p, i) => (p.Start, i)).ToDictionary(x => x.Start, x => x.i);
        int Period(DateOnly day) => index[CashFlowPeriods.PeriodStart(day, granularity, header.HorizonFrom)];

        var actualTo = today < header.HorizonTo ? today : header.HorizonTo;
        var actuals = actualTo < header.HorizonFrom ? []
            : await store.ActualsAsync(context.CompanyId, header.HorizonFrom, actualTo, header.TreasuryAccountId, header.CurrencyCode, cancellationToken)
                .ConfigureAwait(false);
        var perUnit = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["RON"] = 1m };
        var converted = new List<(DateOnly Date, int? CategoryId, decimal AmountRon)>();
        foreach (var a in actuals)
        {
            if (a.AmountRon is { } ron)
            {
                converted.Add((a.Date, a.CategoryId, ron));
                continue;
            }

            if (!perUnit.TryGetValue(a.CurrencyCode, out var rate))
            {
                rate = (await rates.GetRateAsync(a.CurrencyCode, header.StartDate, cancellationToken).ConfigureAwait(false)).PerUnit;
                perUnit[a.CurrencyCode] = rate;
            }

            converted.Add((a.Date, a.CategoryId, Math.Round(a.Amount * rate, 2, MidpointRounding.AwayFromZero)));
        }

        var categories = await cashFlow.CategoriesAsync(cancellationToken).ConfigureAwait(false);
        var used = lines.Select(l => l.CategoryId).Concat(converted.Select(a => a.CategoryId)).Distinct().ToList();
        var hasActuals = periods.Select(p => p.Start <= today).ToList();
        var netForecast = new decimal[periods.Count];
        var netActual = new decimal[periods.Count];
        var rows = new List<(short Order, VsActualRowDto Row)>();
        foreach (var categoryId in used)
        {
            var category = categories.FirstOrDefault(c => c.Id == categoryId);
            var forecast = new decimal[periods.Count];
            var actual = new decimal[periods.Count];
            foreach (var l in lines.Where(l => l.CategoryId == categoryId))
            {
                forecast[Period(l.PeriodStart)] += l.AmountRon;
            }

            foreach (var a in converted.Where(a => a.CategoryId == categoryId))
            {
                actual[Period(a.Date)] += a.AmountRon;
            }

            for (var i = 0; i < periods.Count; i++)
            {
                netForecast[i] += forecast[i];
                netActual[i] += actual[i];
            }

            var pastForecast = forecast.Where((_, i) => hasActuals[i]).Sum();
            rows.Add((category?.SortOrder ?? short.MaxValue, new VsActualRowDto(categoryId, category?.Code ?? "NECATEGORIZAT", category?.Name ?? "Necategorizat",
                forecast.Select(Money.Format).ToList(),
                actual.Select((v, i) => hasActuals[i] ? Money.Format(v) : null).ToList(),
                actual.Select((v, i) => hasActuals[i] ? Money.Format(v - forecast[i]) : null).ToList(),
                Money.Format(forecast.Sum()), Money.Format(actual.Sum()), Money.Format(actual.Sum() - pastForecast))));
        }

        return new CashFlowVsActualDto(header.Id, header.Name, header.TakenAt, granularity,
            periods.Select((p, i) => new VsActualPeriodDto(p.Start, p.End, hasActuals[i])).ToList(),
            rows.OrderBy(r => r.Order).Select(r => r.Row).ToList(), netForecast.Select(Money.Format).ToList(),
            netActual.Select((v, i) => hasActuals[i] ? Money.Format(v) : null).ToList());
    }
}

// ---------- Alerte ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowView)]
public sealed record GetCashFlowAlertRules : IQuery<IReadOnlyList<CashFlowAlertRuleDto>>;

internal sealed class GetCashFlowAlertRulesHandler(ICashFlowSnapshotStore store, ICurrentContext context)
    : IQueryHandler<GetCashFlowAlertRules, IReadOnlyList<CashFlowAlertRuleDto>>
{
    public async Task<IReadOnlyList<CashFlowAlertRuleDto>> Handle(GetCashFlowAlertRules request, CancellationToken cancellationToken) =>
        (await store.AlertRulesAsync(context.CompanyId, cancellationToken).ConfigureAwait(false))
        .Select(r => new CashFlowAlertRuleDto(r.Id, r.TreasuryAccountId, r.TreasuryAccountCode, Money.Format(r.MinBalanceRon), r.HorizonDays, r.NotifyRoleId,
            r.NotifyRoleName, r.IsActive, r.LastAlertedOn)).ToList();
}

/// <summary>Salveaza regulile de alerta (F5 3.7) in bloc: pragul in lei, orizontul (1–92 de zile, implicit 14), rolul notificat.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowPlan)]
[Audited("Fin.CashFlowAlertRule.Save")]
public sealed record SaveCashFlowAlertRules(IReadOnlyList<CashFlowAlertRuleInput> Rules) : ICommand<IReadOnlyList<CashFlowAlertRuleDto>>;

internal sealed class SaveCashFlowAlertRulesValidator : AbstractValidator<SaveCashFlowAlertRules>
{
    public SaveCashFlowAlertRulesValidator()
    {
        RuleFor(x => x.Rules).NotNull().Must(r => r.Count <= 50);
        RuleForEach(x => x.Rules).ChildRules(r =>
        {
            r.RuleFor(x => x.MinBalanceRon).Must(v => Money.ParseOptional(v) is not null).WithMessage("Pragul trebuie să fie o sumă.");
            r.RuleFor(x => x.HorizonDays).InclusiveBetween((short)1, (short)92);
        });
    }
}

internal sealed class SaveCashFlowAlertRulesHandler(ICashFlowSnapshotStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveCashFlowAlertRules, IReadOnlyList<CashFlowAlertRuleDto>>
{
    public async Task<IReadOnlyList<CashFlowAlertRuleDto>> Handle(SaveCashFlowAlertRules request, CancellationToken cancellationToken)
    {
        await store.SaveAlertRulesAsync(context.CompanyId, request.Rules.Select(r => (r.Id, r.TreasuryAccountId, Money.ParseOptional(r.MinBalanceRon)!.Value,
            r.HorizonDays, r.NotifyRoleId, r.IsActive)).ToList(), context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CashFlowAlertRule", context.CompanyId, request.Rules);
        return await new GetCashFlowAlertRulesHandler(store, context).Handle(new GetCashFlowAlertRules(), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Verificarea zilnica a alertelor (F5 3.7, F5-A12): pentru fiecare regula activa, prognoza zilnica pe orizontul ei (contul regulii sau toata
/// firma); la prima zi cu soldul sub prag, evenimentul AlertaCashFlow cu data si categoria cu cea mai mare iesire, o singura data pe zi si
/// regula. Intoarce numarul de alerte trimise.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashFlowPlan)]
public sealed record CheckCashFlowAlerts : ICommand<int>;

internal sealed class CheckCashFlowAlertsHandler(ICashFlowSnapshotStore store, ICashFlowStore cashFlow, CashFlowForecaster forecaster, IOutbox outbox,
    ICurrentContext context, TimeProvider time) : ICommandHandler<CheckCashFlowAlerts, int>
{
    public async Task<int> Handle(CheckCashFlowAlerts request, CancellationToken cancellationToken)
    {
        var today = BusinessDay.Today(time);
        var categories = (await cashFlow.CategoriesAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Code);
        var sent = 0;
        foreach (var rule in (await store.AlertRulesAsync(context.CompanyId, cancellationToken).ConfigureAwait(false)).Where(r => r.IsActive))
        {
            if (rule.LastAlertedOn == today)
            {
                continue;
            }

            var last = today.AddDays(rule.HorizonDays - 1);
            var result = await forecaster.ComputeAsync(context.CompanyId, today, last, rule.TreasuryAccountId, null, false, 0, cancellationToken)
                .ConfigureAwait(false);
            var low = CashFlowAlerts.FirstDayBelow(result.Accounts.Sum(a => a.BalanceRon), result.Entries, today, last, rule.MinBalanceRon);
            if (low is not { } breach || !await store.MarkAlertedAsync(context.CompanyId, rule.Id, today, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await outbox.EnqueueAsync(new Contracts.Planning.AlertaCashFlow(rule.Id, rule.TreasuryAccountId, breach.Date, breach.BalanceRon, rule.MinBalanceRon,
                breach.CauseCategoryId is { } c && categories.TryGetValue(c, out var code) ? code : null, rule.NotifyRoleId), cancellationToken).ConfigureAwait(false);
            sent++;
        }

        return sent;
    }
}

/// <summary>Prima zi in care soldul prognozat scade sub prag (F5 3.7), cu categoria celei mai mari iesiri din acea zi.</summary>
public static class CashFlowAlerts
{
    public static (DateOnly Date, decimal BalanceRon, int? CauseCategoryId)? FirstDayBelow(decimal openingRon, IReadOnlyList<CashFlowEntry> entries, DateOnly from,
        DateOnly to, decimal minBalanceRon)
    {
        var counted = entries.Where(e => !e.IsUncertain).ToLookup(e => e.Date);
        var balance = openingRon;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var mine = counted[day].ToList();
            balance += mine.Sum(e => e.AmountRon);
            if (balance < minBalanceRon)
            {
                var cause = mine.Where(e => e.AmountRon < 0).GroupBy(e => e.CategoryId).OrderBy(g => g.Sum(e => e.AmountRon)).FirstOrDefault()?.Key;
                return (day, balance, cause);
            }
        }

        return null;
    }
}
