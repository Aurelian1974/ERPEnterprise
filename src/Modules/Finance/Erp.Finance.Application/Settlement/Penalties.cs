using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;
using FluentValidation;

namespace Erp.Finance.Application.Settlement;

/// <summary>Starea unui calcul de penalitati (F3 3.10).</summary>
public enum PenaltyStatus : byte
{
    Draft = 1,
    Approved = 2,
    Invoiced = 3,
    Cancelled = 4,
}

/// <summary>Felul liniei: 1 interval penalizat, 2 suma fixa de recuperare (Legea 72/2013).</summary>
public enum PenaltyLineKind : byte
{
    Interval = 1,
    FixedRecoveryCost = 2,
}

public sealed record PenaltySourceItem(long OpenItemId, int PartnerId, string PartnerName, string DocumentNo, DateOnly DueDate, decimal Amount, string CurrencyCode,
    decimal RatePerDay, int GraceDays, bool CapAtPrincipal, bool ApplyFixedRecoveryCost, decimal AlreadyCharged, DateOnly? ChargedThrough, bool HasFixedCost);

public sealed record PenaltySources(IReadOnlyList<PenaltySourceItem> Items, ILookup<long, (DateOnly Date, decimal Amount)> Payments, int DisputedCount,
    IReadOnlyList<ReferenceRate> Rates);

public sealed record PenaltyLineRecord(int LineNumber, PenaltyLineKind Kind, int PartnerId, long OpenItemId, DateOnly DueDate, DateOnly? FromDate, DateOnly? ToDate,
    int Days, decimal BaseAmount, string CurrencyCode, decimal? RatePerDay, decimal? AnnualRate, decimal PenaltyAmount);

public sealed record CurrencyTotalDto(string CurrencyCode, string Total);

public sealed record PenaltyLineDto(long Id, int LineNumber, PenaltyLineKind Kind, int PartnerId, string PartnerName, long OpenItemId, string DocumentNo, DateOnly DueDate,
    DateOnly? FromDate, DateOnly? ToDate, int Days, string BaseAmount, string CurrencyCode, string? RatePerDay, string? AnnualRate, string PenaltyAmount);

public sealed record PenaltyCalculationDto(int Id, string Number, DateOnly CalculationDate, DateOnly PeriodFrom, DateOnly PeriodTo, int? PartnerId, string? PartnerName,
    PenaltyStatus Status, int DisputedCount, DateTime? ApprovedAt, string? ApprovedByName, DateTime CreatedAt, string? CreatedByName,
    IReadOnlyList<CurrencyTotalDto> Totals, IReadOnlyList<PenaltyLineDto> Lines, byte[] RowVersion);

public sealed record PenaltyCalculationItemDto(int Id, string Number, DateOnly CalculationDate, DateOnly PeriodFrom, DateOnly PeriodTo, int? PartnerId,
    string? PartnerName, PenaltyStatus Status, int PartnerCount, IReadOnlyList<CurrencyTotalDto> Totals);

public sealed record ReferenceRateDto(DateOnly ValidFrom, string Rate);

/// <summary>Accesul la date pentru penalitati (fin.usp_Penalty*, fin.usp_ReferenceRate_*, NET-06).</summary>
public interface IPenaltyStore
{
    Task<PenaltySources> SourcesAsync(int companyId, int? partnerId, DateOnly from, DateOnly to, int? excludeCalculationId, CancellationToken ct);

    Task<FinanceSaved> SaveAsync(int companyId, int? id, string? number, DateOnly calculationDate, DateOnly from, DateOnly to, int? partnerId, int disputedCount,
        IReadOnlyList<PenaltyLineRecord> lines, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<byte[]> ChangeAsync(int companyId, int id, PenaltyStatus? status, IReadOnlyList<long>? removeLines, byte[] rowVersion, int userId, CancellationToken ct);

    Task<PenaltyCalculationDto?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<PenaltyCalculationItemDto>> ListAsync(int companyId, int? year, PenaltyStatus? status, CancellationToken ct);

    Task<IReadOnlyList<ReferenceRateDto>> ReferenceRatesAsync(CancellationToken ct);

    Task SaveReferenceRateAsync(DateOnly validFrom, decimal rate, bool delete, int userId, CancellationToken ct);
}

internal static class PenaltyCalculator
{
    public const string DocumentType = "Finance.PenaltyCalculation";

    public static ErpException NotFound() => ErpException.NotFound("FIN_PENALTY_CALCULATION_NOT_FOUND", "Calculul de penalități nu există.");

    public static decimal? Rate(string? value) => value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Liniile calculului (F3 3.10): intervalele fiecarei partide, in ordinea partenerilor si a scadentelor, apoi suma fixa de 40 EUR pe factura
    /// penalizata, o singura data, cand profilul o cere.
    /// </summary>
    public static IReadOnlyList<PenaltyLineRecord> Build(PenaltySources sources, DateOnly from, DateOnly to)
    {
        var lines = new List<PenaltyLineRecord>();
        foreach (var i in sources.Items)
        {
            IReadOnlyList<PenaltyInterval> intervals;
            try
            {
                intervals = PenaltyRules.Calculate(new PenaltyItem(i.OpenItemId, i.DueDate, i.Amount, sources.Payments[i.OpenItemId].ToList(), i.GraceDays, i.RatePerDay,
                    i.CapAtPrincipal, i.AlreadyCharged, i.ChargedThrough), from, to, sources.Rates);
            }
            catch (ReferenceRateMissingException e)
            {
                throw new ErpException(ErpErrorKind.Validation, "FIN_REFERENCE_RATE_MISSING",
                    $"{e.Message} Adăugați rata de referință BNR (dobânda legală se aplică partenerilor fără penalitate în contract).");
            }

            lines.AddRange(intervals.Select(p => new PenaltyLineRecord(0, PenaltyLineKind.Interval, i.PartnerId, i.OpenItemId, i.DueDate, p.From, p.To, p.Days,
                p.BaseAmount, i.CurrencyCode, p.RatePerDay, p.AnnualRate, p.PenaltyAmount)));
            if (intervals.Count > 0 && i.ApplyFixedRecoveryCost && !i.HasFixedCost)
            {
                lines.Add(new PenaltyLineRecord(0, PenaltyLineKind.FixedRecoveryCost, i.PartnerId, i.OpenItemId, i.DueDate, null, null, 0, i.Amount, "EUR", null, null,
                    PenaltyRules.FixedRecoveryCostEur));
            }
        }

        return lines.Select((l, n) => l with { LineNumber = n + 1 }).ToList();
    }
}

// ---------- Lista, fisa, ratele ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetPenaltyCalculations(int? Year = null, PenaltyStatus? Status = null) : IQuery<IReadOnlyList<PenaltyCalculationItemDto>>;

internal sealed class GetPenaltyCalculationsHandler(IPenaltyStore store, ICurrentContext context)
    : IQueryHandler<GetPenaltyCalculations, IReadOnlyList<PenaltyCalculationItemDto>>
{
    public Task<IReadOnlyList<PenaltyCalculationItemDto>> Handle(GetPenaltyCalculations request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.Year, request.Status, cancellationToken);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetPenaltyCalculation(int Id) : IQuery<PenaltyCalculationDto>;

internal sealed class GetPenaltyCalculationHandler(IPenaltyStore store, ICurrentContext context) : IQueryHandler<GetPenaltyCalculation, PenaltyCalculationDto>
{
    public async Task<PenaltyCalculationDto> Handle(GetPenaltyCalculation request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw PenaltyCalculator.NotFound();
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetReferenceRates : IQuery<IReadOnlyList<ReferenceRateDto>>;

internal sealed class GetReferenceRatesHandler(IPenaltyStore store) : IQueryHandler<GetReferenceRates, IReadOnlyList<ReferenceRateDto>>
{
    public Task<IReadOnlyList<ReferenceRateDto>> Handle(GetReferenceRates request, CancellationToken cancellationToken) => store.ReferenceRatesAsync(cancellationToken);
}

/// <summary>Adauga, modifica sau sterge rata de referinta BNR valabila de la o data (F3 3.10 regula 2); comuna tuturor firmelor instalarii.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.TreasuryConfigure)]
[Audited("Fin.ReferenceRate.Save")]
public sealed record SaveReferenceRate(DateOnly ValidFrom, string Rate, bool Delete = false) : ICommand<bool>;

internal sealed class SaveReferenceRateValidator : AbstractValidator<SaveReferenceRate>
{
    public SaveReferenceRateValidator()
    {
        RuleFor(x => x.Rate).Must(r => decimal.TryParse(r, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v)
            && v >= 0 && v < 100 && decimal.Round(v, 4) == v).When(x => !x.Delete).WithMessage("Rata este un procent anual între 0 și 100, cu cel mult 4 zecimale.");
    }
}

internal sealed class SaveReferenceRateHandler(IPenaltyStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveReferenceRate, bool>
{
    public async Task<bool> Handle(SaveReferenceRate request, CancellationToken cancellationToken)
    {
        var rate = request.Delete ? 0m : decimal.Parse(request.Rate, System.Globalization.CultureInfo.InvariantCulture);
        await store.SaveReferenceRateAsync(request.ValidFrom, rate, request.Delete, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.ReferenceRate", request.ValidFrom.DayNumber, new { request.ValidFrom, Rate = rate, request.Delete });
        return true;
    }
}

// ---------- Calcul ----------

/// <summary>
/// Calculeaza penalitatile pe un interval, optional pentru un singur partener (F3 4.7 pasul 1): un calcul ciorna numerotat din seria
/// Finance.PenaltyCalculation, cu liniile din regulile 3.10; partidele in litigiu se exclud si se numara.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PenaltyCalculate)]
[Audited("Fin.Penalty.Calculate")]
public sealed record CalculatePenalties(DateOnly PeriodFrom, DateOnly PeriodTo, int? PartnerId) : ICommand<FinanceSaved>;

internal sealed class CalculatePenaltiesValidator : AbstractValidator<CalculatePenalties>
{
    public CalculatePenaltiesValidator()
    {
        RuleFor(x => x.PeriodTo).GreaterThanOrEqualTo(x => x.PeriodFrom).WithMessage("Sfârșitul perioadei este după început.");
        RuleFor(x => x.PeriodTo).Must((x, to) => to.DayNumber - x.PeriodFrom.DayNumber <= 366).WithMessage("Perioada are cel mult un an.");
    }
}

internal sealed class CalculatePenaltiesHandler(IPenaltyStore store, INumberSeriesService numbers, ICurrentContext context, TimeProvider time, IAuditScope audit)
    : ICommandHandler<CalculatePenalties, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(CalculatePenalties request, CancellationToken cancellationToken)
    {
        var today = BusinessDay.Today(time);
        if (request.PeriodTo > today)
        {
            throw new ErpValidationException(new Dictionary<string, string[]> { ["periodTo"] = ["Perioada nu poate trece de ziua de azi."] });
        }

        var sources = await store.SourcesAsync(context.CompanyId, request.PartnerId, request.PeriodFrom, request.PeriodTo, null, cancellationToken).ConfigureAwait(false);
        var lines = PenaltyCalculator.Build(sources, request.PeriodFrom, request.PeriodTo);
        var number = (await numbers.NextAsync(context.CompanyId, PenaltyCalculator.DocumentType, today, cancellationToken).ConfigureAwait(false)).Formatted;
        var saved = await store.SaveAsync(context.CompanyId, null, number, today, request.PeriodFrom, request.PeriodTo, request.PartnerId, sources.DisputedCount, lines,
            null, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PenaltyCalculation", saved.Id, new { Number = number, request.PeriodFrom, request.PeriodTo, request.PartnerId, Lines = lines.Count });
        return saved;
    }
}

/// <summary>Recalculeaza un calcul ciorna (macheta f3-08, „Recalculează”): liniile se refac din datele de azi, cu aceeasi perioada si partener.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PenaltyCalculate)]
[Audited("Fin.Penalty.Recalculate")]
public sealed record RecalculatePenalties(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class RecalculatePenaltiesHandler(IPenaltyStore store, ICurrentContext context, TimeProvider time, IAuditScope audit)
    : ICommandHandler<RecalculatePenalties, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(RecalculatePenalties request, CancellationToken cancellationToken)
    {
        var current = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw PenaltyCalculator.NotFound();
        var sources = await store.SourcesAsync(context.CompanyId, current.PartnerId, current.PeriodFrom, current.PeriodTo, request.Id, cancellationToken)
            .ConfigureAwait(false);
        var lines = PenaltyCalculator.Build(sources, current.PeriodFrom, current.PeriodTo);
        var saved = await store.SaveAsync(context.CompanyId, request.Id, null, BusinessDay.Today(time), current.PeriodFrom, current.PeriodTo, current.PartnerId,
            sources.DisputedCount, lines, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PenaltyCalculation", request.Id, new { current.Number, Lines = lines.Count });
        return saved;
    }
}

/// <summary>Scoate din ciorna liniile care nu se factureaza (F3 4.7 pasul 2).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PenaltyCalculate)]
[Audited("Fin.Penalty.RemoveLines")]
public sealed record RemovePenaltyLines(int Id, IReadOnlyList<long> LineIds, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class RemovePenaltyLinesValidator : AbstractValidator<RemovePenaltyLines>
{
    public RemovePenaltyLinesValidator() => RuleFor(x => x.LineIds).NotEmpty().WithMessage("Alegeți liniile de scos.");
}

internal sealed class RemovePenaltyLinesHandler(IPenaltyStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<RemovePenaltyLines, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(RemovePenaltyLines request, CancellationToken cancellationToken)
    {
        var version = await store.ChangeAsync(context.CompanyId, request.Id, null, request.LineIds, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.PenaltyCalculation", request.Id, new { Removed = request.LineIds });
        return new FinanceSaved(request.Id, version);
    }
}

/// <summary>Anuleaza un calcul ciorna.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PenaltyCalculate)]
[Audited("Fin.Penalty.Cancel")]
public sealed record CancelPenaltyCalculation(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class CancelPenaltyCalculationHandler(IPenaltyStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<CancelPenaltyCalculation, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(CancelPenaltyCalculation request, CancellationToken cancellationToken)
    {
        var version = await store.ChangeAsync(context.CompanyId, request.Id, PenaltyStatus.Cancelled, null, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.PenaltyCalculation", request.Id, new { Status = PenaltyStatus.Cancelled });
        return new FinanceSaved(request.Id, version);
    }
}

/// <summary>
/// Aproba calculul (F3 4.7 pasul 3): starea Aprobat si evenimentul PenalitatiCalculate, cu penalitatile pe partener si moneda, din care Vanzari
/// emite facturile de penalitati ca ciorne. Un calcul fara linii nu se aproba.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PenaltyApprove)]
[Audited("Fin.Penalty.Approve")]
public sealed record ApprovePenaltyCalculation(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class ApprovePenaltyCalculationHandler(IPenaltyStore store, IOutbox outbox, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ApprovePenaltyCalculation, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(ApprovePenaltyCalculation request, CancellationToken cancellationToken)
    {
        var current = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw PenaltyCalculator.NotFound();
        if (current.Lines.Count == 0)
        {
            throw ErpException.Conflict("FIN_PENALTY_EMPTY", "Calculul nu are nicio penalitate de aprobat.");
        }

        var version = await store.ChangeAsync(context.CompanyId, request.Id, PenaltyStatus.Approved, null, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        var partners = current.Lines.GroupBy(l => (l.PartnerId, l.CurrencyCode)).Select(g =>
        {
            var lines = g.Select(l => new PenalitateLinie((byte)l.Kind, l.OpenItemId, l.DocumentNo, l.DueDate, l.FromDate, l.ToDate, l.Days, Money.ParseOptional(l.BaseAmount) ?? 0,
                PenaltyCalculator.Rate(l.RatePerDay), PenaltyCalculator.Rate(l.AnnualRate),
                Money.ParseOptional(l.PenaltyAmount) ?? 0)).ToList();
            return new PenalitatiPartener(g.Key.PartnerId, g.Key.CurrencyCode, lines.Sum(l => l.PenaltyAmount), lines);
        }).ToList();
        await outbox.EnqueueAsync(new PenalitatiCalculate(request.Id, current.Number, current.CalculationDate, current.PeriodFrom, current.PeriodTo, partners)
        {
            CompanyId = context.CompanyId,
        }, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PenaltyCalculation", request.Id, new { current.Number, Status = PenaltyStatus.Approved, Totals = current.Totals });
        return new FinanceSaved(request.Id, version);
    }
}
