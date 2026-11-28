using Erp.Core.Contracts;
using Erp.Finance.Contracts.Periods;
using FluentValidation;

namespace Erp.Finance.Application.Periods;

public sealed record FiscalYearDto(int Id, int Year, DateOnly StartDate, DateOnly EndDate, byte Status, byte VatPeriodicity, DateTime? ClosedAt, DateTime? LockedAt,
    string? LockReason, byte[] RowVersion, IReadOnlyList<AccountingPeriodDto> Periods);

public sealed record AccountingPeriodDto(int Id, int PeriodNo, DateOnly StartDate, DateOnly EndDate, PeriodState Status, bool IsImported, DateTime? SoftClosedAt,
    string? SoftClosedByName, DateTime? ClosedAt, string? ClosedByName, byte[] RowVersion);

public sealed record PeriodHistoryDto(PeriodState FromStatus, PeriodState ToStatus, string? Reason, DateTime ChangedAt, string? ChangedByName);

public sealed record PeriodChange(int Id, int Year, int PeriodNo, DateOnly StartDate, DateOnly EndDate, PeriodState FromStatus, PeriodState Status, byte[] RowVersion);

public sealed record DueFiscalYear(int CompanyId, int Year, DateOnly StartDate, DateOnly EndDate, byte VatPeriodicity);

/// <summary>Anii financiari si lunile contabile prin fin.usp_FiscalYear_*, fin.usp_AccountingPeriod_* (NET-06).</summary>
public interface IAccountingPeriodStore
{
    Task<int> CreateYearAsync(int companyId, int year, DateOnly start, DateOnly end, byte vatPeriodicity, int userId, CancellationToken ct);

    Task<IReadOnlyList<FiscalYearDto>> ListAsync(int companyId, CancellationToken ct);

    Task<PeriodChange> SetStatusAsync(int companyId, int id, PeriodState status, string? reason, byte[] rowVersion, int userId, CancellationToken ct);

    Task<PeriodState?> StateAsync(int companyId, DateOnly day, CancellationToken ct);

    Task<IReadOnlyList<PeriodHistoryDto>> HistoryAsync(int companyId, int id, CancellationToken ct);
}

/// <summary>
/// Starea perioadelor pentru module si pentru Nucleu (C3 6): inlocuieste IPeriodLock-ul implicit al Nucleului, deci PeriodLockBehavior si
/// verificarile din F1–F4 refuza documentele datate intr-o luna inchisa operativ, inchisa sau blocata.
/// </summary>
public sealed class AccountingPeriodGuard(IAccountingPeriodStore store) : IAccountingPeriodGuard, IPeriodLock
{
    public Task<PeriodState?> GetStateAsync(int companyId, DateOnly accountingDate, CancellationToken ct) => store.StateAsync(companyId, accountingDate, ct);

    public async Task<bool> CanWriteAsync(int companyId, DateOnly accountingDate, PeriodWriteKind kind, CancellationToken ct)
    {
        var state = await store.StateAsync(companyId, accountingDate, ct).ConfigureAwait(false);
        return state is null || (kind == PeriodWriteKind.SourceDocument ? state == PeriodState.Open : state <= PeriodState.SoftClosed);
    }

    public async Task<bool> IsClosedAsync(int companyId, DateOnly date, CancellationToken ct) =>
        !await CanWriteAsync(companyId, date, PeriodWriteKind.SourceDocument, ct).ConfigureAwait(false);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodView)]
public sealed record GetAccountingPeriods : IQuery<IReadOnlyList<FiscalYearDto>>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodView)]
public sealed record GetPeriodHistory(int Id) : IQuery<IReadOnlyList<PeriodHistoryDto>>;

internal sealed class AccountingPeriodQueries(IAccountingPeriodStore store, ICurrentContext context) :
    IQueryHandler<GetAccountingPeriods, IReadOnlyList<FiscalYearDto>>, IQueryHandler<GetPeriodHistory, IReadOnlyList<PeriodHistoryDto>>
{
    public Task<IReadOnlyList<FiscalYearDto>> Handle(GetAccountingPeriods request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, cancellationToken);

    public Task<IReadOnlyList<PeriodHistoryDto>> Handle(GetPeriodHistory request, CancellationToken cancellationToken) =>
        store.HistoryAsync(context.CompanyId, request.Id, cancellationToken);
}

/// <summary>
/// Creeaza un an financiar cu lunile lui (C3 3.1): implicit anul calendaristic; primul exercitiu al unei firme poate incepe in alta zi.
/// Periodicitatea TVA: 1 lunar, 3 trimestrial, 6 semestrial, 12 anual. Anii urmatori ii creeaza jobul, cu 60 de zile inainte.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FiscalYearClose)]
[Audited("Fin.FiscalYear.Create")]
public sealed record CreateFiscalYear(int Year, DateOnly? StartDate = null, DateOnly? EndDate = null, byte VatPeriodicity = 1) : ICommand<int>;

internal sealed class CreateFiscalYearValidator : AbstractValidator<CreateFiscalYear>
{
    public CreateFiscalYearValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.VatPeriodicity).Must(v => v is 1 or 3 or 6 or 12).WithMessage("Periodicitatea TVA e 1, 3, 6 sau 12 luni.");
        RuleFor(x => x).Must(x => (x.EndDate ?? new DateOnly(x.Year, 12, 31)) > (x.StartDate ?? new DateOnly(x.Year, 1, 1)))
            .WithMessage("Sfârșitul anului e după început.");
    }
}

internal sealed class CreateFiscalYearHandler(IAccountingPeriodStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<CreateFiscalYear, int>
{
    public async Task<int> Handle(CreateFiscalYear request, CancellationToken cancellationToken)
    {
        var id = await store.CreateYearAsync(context.CompanyId, request.Year, request.StartDate ?? new DateOnly(request.Year, 1, 1),
            request.EndDate ?? new DateOnly(request.Year, 12, 31), request.VatPeriodicity, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FiscalYear", id, request);
        return id;
    }
}

/// <summary>Inchide operativ o luna (C3 3.2): modulele nu mai valideaza documente datate in ea; contabilitatea mai poate scrie note.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodSoftClose)]
[Audited("Fin.Period.SoftClose")]
public sealed record SoftClosePeriod(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

/// <summary>
/// Inchide o luna (C3 4.1 pasul 5): ruleaza lista de verificari (o rulare noua, cu trecerile peste pasi ale celei anterioare) si refuza inchiderea
/// cu codul primului pas blocant esuat; scrie notele de inchidere (regularizarea TVA, inchiderea in 121) si trece luna in Inchisa; coada „De
/// contat” si evenimentele in tranzit se re-verifica in tranzactia inchiderii.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodClose)]
[Audited("Fin.Period.Close")]
public sealed record ClosePeriod(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

/// <summary>Redeschide o luna (C3 4.2), cu motiv: in Deschisa sau Inchisa operativ; lunile de dupa ea trebuie sa fie mai deschise.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodReopen)]
[Audited("Fin.Period.Reopen")]
public sealed record ReopenPeriod(int Id, string Reason, bool ToSoftClosed, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class ReopenPeriodValidator : AbstractValidator<ReopenPeriod>
{
    public ReopenPeriodValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Scrieți motivul redeschiderii.").MaximumLength(500);
}

internal sealed class PeriodStatusHandlers(IAccountingPeriodStore store, PeriodCloseService checklist, ClosingEntries closing, IOutbox outbox, ICurrentContext context, IAuditScope audit) :
    ICommandHandler<SoftClosePeriod, FinanceSaved>, ICommandHandler<ClosePeriod, FinanceSaved>, ICommandHandler<ReopenPeriod, FinanceSaved>
{
    public Task<FinanceSaved> Handle(SoftClosePeriod request, CancellationToken cancellationToken) =>
        Change(request.Id, PeriodState.SoftClosed, null, request.RowVersion, cancellationToken);

    public async Task<FinanceSaved> Handle(ClosePeriod request, CancellationToken cancellationToken)
    {
        var run = await checklist.RunAsync(context.CompanyId, request.Id, context.UserId, cancellationToken).ConfigureAwait(false);
        if (!run.CanClose)
        {
            throw PeriodCloseService.Refusal(run);
        }

        await closing.WriteAsync(context.CompanyId, request.Id, context.UserId, cancellationToken).ConfigureAwait(false);
        return await Change(request.Id, PeriodState.Closed, null, request.RowVersion, cancellationToken).ConfigureAwait(false);
    }

    public Task<FinanceSaved> Handle(ReopenPeriod request, CancellationToken cancellationToken) =>
        Change(request.Id, request.ToSoftClosed ? PeriodState.SoftClosed : PeriodState.Open, request.Reason.Trim(), request.RowVersion, cancellationToken);

    private async Task<FinanceSaved> Change(int id, PeriodState target, string? reason, byte[] rowVersion, CancellationToken ct)
    {
        var p = await store.SetStatusAsync(context.CompanyId, id, target, reason, rowVersion, context.UserId, ct).ConfigureAwait(false);
        if (p.Status != p.FromStatus)
        {
            IntegrationEvent evt = p.Status < p.FromStatus
                ? new PerioadaContabilaRedeschisa(p.Year, p.PeriodNo, p.StartDate, p.EndDate, p.Status, reason ?? "")
                : p.Status == PeriodState.SoftClosed ? new PerioadaContabilaInchisaOperativ(p.Year, p.PeriodNo, p.StartDate, p.EndDate)
                : new PerioadaContabilaInchisa(p.Year, p.PeriodNo, p.StartDate, p.EndDate);
            await outbox.EnqueueAsync(evt, ct).ConfigureAwait(false);
        }

        audit.Describe("Fin.AccountingPeriod", id, new { From = p.FromStatus, To = p.Status, Reason = reason });
        return new FinanceSaved(p.Id, p.RowVersion);
    }
}
