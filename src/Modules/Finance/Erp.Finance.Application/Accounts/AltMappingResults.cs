using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Finance.Application.Accounts;

/// <summary>O luna a unei firme in planul alternativ: liniile de nota si inghetarea (C1 4.4 pasul 1).</summary>
public sealed record AltMonthDto(int CompanyId, int Year, int Month, int LineCount, bool IsFrozen);

/// <summary>Rezultatul recalcularii (C1 4.4): lunile recalculate (cu liniile si nemapatele) si lunile sarite fiindca sunt inghetate.</summary>
public sealed record AltRecalculationDto(IReadOnlyList<AltMonthResultDto> Recalculated, IReadOnlyList<AltMonthDto> SkippedFrozen);

public sealed record AltMonthResultDto(int CompanyId, int Year, int Month, int LineCount, int UnmappedCount);

/// <summary>Un cont romanesc cu linii nemapate intr-un plan alternativ (C1 4.5).</summary>
public sealed record UnmappedAccountDto(int AccountId, string Code, string Name, string Debit, string Credit, int LineCount);

/// <summary>Rezultatele maparii prin fin.usp_AltMapping_Months, _Recalculate, _Unmapped, fin.usp_AltChartPeriod_SetFrozen (NET-06).</summary>
public interface IAltMappingResultStore
{
    Task<IReadOnlyList<AltMonthDto>> MonthsAsync(int chartId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<(bool Skipped, int LineCount, int UnmappedCount)> RecalculateAsync(int chartId, int companyId, int year, int month, int userId, CancellationToken ct);

    Task SetFrozenAsync(int chartId, int companyId, int year, int month, bool frozen, int userId, CancellationToken ct);

    Task<IReadOnlyList<UnmappedAccountDto>> UnmappedAsync(int chartId, int companyId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>Previzualizarea recalcularii (C1 4.4 pasul 1): lunile cu note ale firmelor planului in interval, cu liniile si inghetarea.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.MappingManage)]
public sealed record PreviewAltRecalculation(int ChartId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<AltMonthDto>>;

/// <summary>
/// Recalculeaza maparea unui plan alternativ pe interval (C1 4.4): fiecare luna a fiecarei firme a planului se mapeaza din nou cu regulile de azi,
/// intr-o tranzactie pe luna, cu un rand in fin.AltMappingRun; lunile inghetate se sar si se listeaza (C1-A8). Planul romanesc nu se atinge.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.MappingManage)]
[Audited("Fin.Mapping.Recalculate")]
public sealed record RecalculateAltMapping(int ChartId, DateOnly From, DateOnly To) : ICommand<AltRecalculationDto>;

internal sealed class RecalculateAltMappingValidator : AbstractValidator<RecalculateAltMapping>
{
    public RecalculateAltMappingValidator() => RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).Must((x, to) => to.DayNumber - x.From.DayNumber <= 3700)
        .WithMessage("Intervalul are cel mult 10 ani.");
}

/// <summary>Nemapatele unui plan alternativ pentru firma curenta (C1 4.5).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountView)]
public sealed record GetUnmappedAccounts(int ChartId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<UnmappedAccountDto>>;

/// <summary>Ingheata (Fin.Mapping.Freeze) sau dezgheata (Fin.Mapping.Unfreeze, cu audit) o luna a firmei curente intr-un plan alternativ (C1 3.6).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.MappingFreeze)]
[Audited("Fin.Mapping.Freeze")]
public sealed record SetAltPeriodFrozen(int ChartId, int Year, int Month, bool Frozen) : ICommand<bool>;

internal sealed class SetAltPeriodFrozenValidator : AbstractValidator<SetAltPeriodFrozen>
{
    public SetAltPeriodFrozenValidator()
    {
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
    }
}

internal sealed class AltMappingResultHandlers(IAltMappingResultStore store, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<PreviewAltRecalculation, IReadOnlyList<AltMonthDto>>, ICommandHandler<RecalculateAltMapping, AltRecalculationDto>,
    IQueryHandler<GetUnmappedAccounts, IReadOnlyList<UnmappedAccountDto>>, ICommandHandler<SetAltPeriodFrozen, bool>
{
    public Task<IReadOnlyList<AltMonthDto>> Handle(PreviewAltRecalculation request, CancellationToken cancellationToken) =>
        store.MonthsAsync(request.ChartId, request.From, request.To, cancellationToken);

    public async Task<AltRecalculationDto> Handle(RecalculateAltMapping request, CancellationToken cancellationToken)
    {
        var done = new List<AltMonthResultDto>();
        var skipped = new List<AltMonthDto>();
        foreach (var m in await store.MonthsAsync(request.ChartId, request.From, request.To, cancellationToken).ConfigureAwait(false))
        {
            var (isSkipped, lines, unmapped) = await store.RecalculateAsync(request.ChartId, m.CompanyId, m.Year, m.Month, context.UserId, cancellationToken)
                .ConfigureAwait(false);
            if (isSkipped)
            {
                skipped.Add(m);
            }
            else
            {
                done.Add(new AltMonthResultDto(m.CompanyId, m.Year, m.Month, lines, unmapped));
            }
        }

        audit.Describe("Fin.ChartOfAccounts", request.ChartId, new { request.From, request.To, Recalculated = done.Count, Skipped = skipped.Count });
        return new AltRecalculationDto(done, skipped);
    }

    public Task<IReadOnlyList<UnmappedAccountDto>> Handle(GetUnmappedAccounts request, CancellationToken cancellationToken) =>
        store.UnmappedAsync(request.ChartId, context.CompanyId, request.From, request.To, cancellationToken);

    public async Task<bool> Handle(SetAltPeriodFrozen request, CancellationToken cancellationToken)
    {
        if (!request.Frozen && !context.HasPermission(FinancePermissions.MappingUnfreeze))
        {
            throw new ErpException(ErpErrorKind.Forbidden, "FIN_MAPPING_UNFREEZE_FORBIDDEN", "Dezghețarea unei luni cere dreptul Fin.Mapping.Unfreeze.");
        }

        await store.SetFrozenAsync(request.ChartId, context.CompanyId, request.Year, request.Month, request.Frozen, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.ChartOfAccounts", request.ChartId, new { request.Year, request.Month, request.Frozen });
        return true;
    }
}
