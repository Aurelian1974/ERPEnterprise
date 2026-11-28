using Erp.Core.Contracts;
using Erp.Finance.Contracts.Accounts;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Periods;
using FluentValidation;

namespace Erp.Finance.Application.Periods;

/// <summary>Un sold reportat (C3 3.6): contul, partenerul, angajatul, moneda, soldul in lei (D − C) si in valuta.</summary>
public sealed record CarriedBalance(int AccountId, string AccountCode, int? PartnerId, int? EmployeeId, string? CurrencyCode, decimal Net, decimal NetCurrency);

/// <summary>Datele inchiderii anului, cum vin din fin.usp_FiscalYear_CloseData.</summary>
public sealed record YearCloseData(int YearId, DateOnly StartDate, DateOnly EndDate, int OpenMonths, int? NextYearId, DateOnly? NextStart, byte Status,
    decimal GrossProfit, decimal ShareCapital, decimal ExistingReserve, IReadOnlyDictionary<byte, long> Entries, IReadOnlyList<CarriedBalance> Carried);

/// <summary>O linie a unei note de sistem (inchideri): contul, partea, suma si analitica.</summary>
public sealed record SystemLine(int AccountId, byte Side, decimal AmountRon, int? PartnerId = null, int? EmployeeId = null, string? CurrencyCode = null,
    decimal? AmountCurrency = null);

/// <summary>Starea inchiderii anului (macheta c3-05): lunile deschise, cifrele rezervei legale, propunerea si notele scrise.</summary>
public sealed record YearCloseDto(int Year, byte Status, int OpenMonths, bool NextYearExists, string GrossProfit, string ShareCapital, string ExistingReserve,
    string ProposedReserve, IReadOnlyList<long> EntryIds);

/// <summary>Inchiderea anului prin fin.usp_FiscalYear_CloseData, fin.usp_SystemEntry_Write, fin.usp_FiscalYearCloseEntry_Save, fin.usp_FiscalYear_SetStatus.</summary>
public interface IFiscalYearCloseStore
{
    Task<YearCloseData> DataAsync(int companyId, int year, CancellationToken ct);

    Task<ClosingWrite> WriteAsync(int companyId, long? entryId, DateOnly date, byte sourceKind, int yearId, string description, IReadOnlyList<SystemLine> lines,
        string reason, int userId, CancellationToken ct);

    Task LinkAsync(int companyId, int yearId, byte kind, long entryId, CancellationToken ct);

    Task SetStatusAsync(int companyId, int yearId, byte status, string? reason, int userId, CancellationToken ct);
}

/// <summary>
/// Inchiderea anului (C3 3.6, 4.3): rezerva legala propusa si contata in decembrie (129 = 1061), apoi, cu toate lunile inchise, soldurile
/// reportate ale conturilor de bilant (o nota de deschidere in prima zi a anului urmator, pe cont, partener, angajat si moneda) si preluarea
/// rezultatului (121 = 129 rezerva, 121 = 117 restul profitului sau 117 = 121 pierderea). La reinchidere notele se rescriu (C3-A9).
/// </summary>
public sealed class FiscalYearClosing(IFiscalYearCloseStore store, IChartOfAccountsQueries accounts, IPeriodCloseStore periods, IOutbox outbox)
{
    public const byte CarriedKind = 1;
    public const byte TransferKind = 2;
    public const byte ReserveKind = 3;

    public async Task<YearCloseDto> StateAsync(int companyId, int year, CancellationToken ct)
    {
        var d = await store.DataAsync(companyId, year, ct).ConfigureAwait(false);
        return new YearCloseDto(year, d.Status, d.OpenMonths, d.NextYearId is not null, Money.Format(d.GrossProfit), Money.Format(d.ShareCapital),
            Money.Format(d.ExistingReserve), Money.Format(LegalReserve.Propose(d.GrossProfit, d.ShareCapital, d.ExistingReserve)), d.Entries.Values.ToList());
    }

    public async Task PostReserveAsync(int companyId, int year, decimal amount, string? reason, int userId, CancellationToken ct)
    {
        var d = await store.DataAsync(companyId, year, ct).ConfigureAwait(false);
        if (!LegalReserve.WithinCap(amount, d.ShareCapital, d.ExistingReserve) && string.IsNullOrWhiteSpace(reason))
        {
            throw new ErpException(ErpErrorKind.Validation, "FIN_LEGAL_RESERVE_INVALID", "Rezerva legală depășește plafonul de 20% din capital; scrieți motivul.");
        }

        var appropriation = await Role(companyId, GlAccountRoles.ProfitAppropriation, d.EndDate, ct).ConfigureAwait(false);
        var reserve = await accounts.FindPostableAsync(companyId, "1061", d.EndDate, ct).ConfigureAwait(false)
            ?? throw new ErpException(ErpErrorKind.Validation, "FIN_ENTRY_ACCOUNT_INVALID", "Contul 1061 nu poate primi note.");
        if (amount <= 0)
        {
            return;
        }

        await Write(companyId, d, ReserveKind, d.EndDate, 4, $"Rezerva legală {year}", [new(appropriation, 1, amount), new(reserve.Id, 2, amount)],
            reason ?? "Rezerva legală recalculată", userId, ct).ConfigureAwait(false);
    }

    public async Task CloseAsync(int companyId, int year, int userId, CancellationToken ct)
    {
        var d = await store.DataAsync(companyId, year, ct).ConfigureAwait(false);
        if (d.OpenMonths > 0)
        {
            throw new ErpException(ErpErrorKind.Conflict, "FIN_FISCAL_YEAR_NOT_CLOSABLE", $"Anul are {d.OpenMonths} luni neînchise.");
        }

        if (d.NextStart is not { } next)
        {
            throw new ErpException(ErpErrorKind.Conflict, "FIN_FISCAL_YEAR_NOT_CLOSABLE", "Creați întâi anul financiar următor (soldurile se reportează în el).");
        }

        var carried = d.Carried.Select(c => new SystemLine(c.AccountId, c.Net > 0 ? (byte)1 : (byte)2, Math.Abs(c.Net), c.PartnerId, c.EmployeeId, c.CurrencyCode,
            c.CurrencyCode is null ? null : Math.Abs(c.NetCurrency))).ToList();
        if (carried.Count > 0)
        {
            await Write(companyId, d, CarriedKind, next, 5, $"Solduri reportate din {year}", carried, "Reînchiderea anului", userId, ct).ConfigureAwait(false);
        }

        var result = await Role(companyId, GlAccountRoles.ProfitOrLoss, d.EndDate, ct).ConfigureAwait(false);
        var profit = -d.Carried.Where(c => c.AccountId == result).Sum(c => c.Net);
        var appropriation = await Role(companyId, GlAccountRoles.ProfitAppropriation, d.EndDate, ct).ConfigureAwait(false);
        var reserve = d.Carried.Where(c => c.AccountId == appropriation).Sum(c => c.Net);
        var retained = await Role(companyId, GlAccountRoles.RetainedEarnings, d.EndDate, ct).ConfigureAwait(false);
        var transfer = new List<SystemLine>();
        if (reserve > 0)
        {
            transfer.Add(new(result, 1, reserve));
            transfer.Add(new(appropriation, 2, reserve));
        }

        var rest = profit - Math.Max(reserve, 0);
        if (rest != 0)
        {
            transfer.Add(new(rest > 0 ? result : retained, 1, Math.Abs(rest)));
            transfer.Add(new(rest > 0 ? retained : result, 2, Math.Abs(rest)));
        }

        if (transfer.Count > 0)
        {
            await Write(companyId, d, TransferKind, next, 5, $"Preluarea rezultatului {year}", transfer, "Reînchiderea anului", userId, ct).ConfigureAwait(false);
        }

        if (d.Status == 1)
        {
            await store.SetStatusAsync(companyId, d.YearId, 2, null, userId, ct).ConfigureAwait(false);
        }
    }

    private async Task<int> Role(int companyId, string role, DateOnly onDate, CancellationToken ct) =>
        (await accounts.FindByRoleAsync(companyId, role, null, null, onDate, ct).ConfigureAwait(false))?.Id
        ?? throw new ErpException(ErpErrorKind.Validation, "FIN_POSTING_ACCOUNT_NOT_RESOLVED", $"Rolul {role} nu are cont.");

    private async Task Write(int companyId, YearCloseData d, byte kind, DateOnly date, byte sourceKind, string description, IReadOnlyList<SystemLine> lines,
        string reason, int userId, CancellationToken ct)
    {
        var existing = d.Entries.TryGetValue(kind, out var id) ? id : (long?)null;
        var written = await store.WriteAsync(companyId, existing, date, sourceKind, d.YearId, description, lines, reason, userId, ct).ConfigureAwait(false);
        await store.LinkAsync(companyId, d.YearId, kind, written.EntryId!.Value, ct).ConfigureAwait(false);
        var header = await periods.HeaderAsync(written.EntryId.Value, ct).ConfigureAwait(false);
        IntegrationEvent evt = written.IsNew
            ? Accounting.JournalEntryNumbers.Event(header) with { CompanyId = companyId }
            : new Contracts.Accounting.NotaContabilaRecontata(written.EntryId.Value, written.OldVersion, written.OldTotal, header.TotalDebit) { CompanyId = companyId };
        await outbox.EnqueueAsync(evt, ct).ConfigureAwait(false);
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodView)]
public sealed record GetYearClose(int Year) : IQuery<YearCloseDto>;

/// <summary>Conteaza rezerva legala a anului (C3 3.6 pasul 2), in decembrie: peste plafonul de 20% doar cu motiv (51913).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FiscalYearClose)]
[Audited("Fin.FiscalYear.LegalReserve")]
public sealed record PostLegalReserve(int Year, string Amount, string? Reason = null) : ICommand<bool>;

internal sealed class PostLegalReserveValidator : AbstractValidator<PostLegalReserve>
{
    public PostLegalReserveValidator() => RuleFor(x => x.Amount).Must(a => Money.ParseOptional(a) is >= 0).WithMessage("Suma rezervei e un număr pozitiv.");
}

/// <summary>Inchide anul (C3 3.6 pasii 4–5): soldurile reportate si preluarea rezultatului in anul urmator; la reinchidere le rescrie.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FiscalYearClose)]
[Audited("Fin.FiscalYear.Close")]
public sealed record CloseFiscalYear(int Year) : ICommand<bool>;

/// <summary>Blocheaza un an inchis dupa depunerea situatiilor financiare (C3 3.6 pasul 7, decizia 6).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FiscalYearLock)]
[Audited("Fin.FiscalYear.Lock")]
public sealed record LockFiscalYear(int Year) : ICommand<bool>;

/// <summary>Deblocheaza un an blocat, cu motiv (decizia 6): doar cu Fin.FiscalYear.Unlock, din rolul „Deblocare an”.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FiscalYearUnlock)]
[Audited("Fin.FiscalYear.Unlock")]
public sealed record UnlockFiscalYear(int Year, string Reason) : ICommand<bool>;

internal sealed class UnlockFiscalYearValidator : AbstractValidator<UnlockFiscalYear>
{
    public UnlockFiscalYearValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Scrieți motivul deblocării.").MaximumLength(500);
}

internal sealed class FiscalYearCloseHandlers(FiscalYearClosing closing, IFiscalYearCloseStore store, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetYearClose, YearCloseDto>, ICommandHandler<PostLegalReserve, bool>, ICommandHandler<CloseFiscalYear, bool>,
    ICommandHandler<LockFiscalYear, bool>, ICommandHandler<UnlockFiscalYear, bool>
{
    public Task<YearCloseDto> Handle(GetYearClose request, CancellationToken cancellationToken) => closing.StateAsync(context.CompanyId, request.Year, cancellationToken);

    public async Task<bool> Handle(PostLegalReserve request, CancellationToken cancellationToken)
    {
        await closing.PostReserveAsync(context.CompanyId, request.Year, Money.ParseOptional(request.Amount) ?? 0, request.Reason?.Trim(), context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FiscalYear", request.Year, request);
        return true;
    }

    public async Task<bool> Handle(CloseFiscalYear request, CancellationToken cancellationToken)
    {
        await closing.CloseAsync(context.CompanyId, request.Year, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FiscalYear", request.Year, null);
        return true;
    }

    public async Task<bool> Handle(LockFiscalYear request, CancellationToken cancellationToken)
    {
        var d = await store.DataAsync(context.CompanyId, request.Year, cancellationToken).ConfigureAwait(false);
        await store.SetStatusAsync(context.CompanyId, d.YearId, 3, null, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FiscalYear", request.Year, null);
        return true;
    }

    public async Task<bool> Handle(UnlockFiscalYear request, CancellationToken cancellationToken)
    {
        var d = await store.DataAsync(context.CompanyId, request.Year, cancellationToken).ConfigureAwait(false);
        await store.SetStatusAsync(context.CompanyId, d.YearId, 2, request.Reason.Trim(), context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FiscalYear", request.Year, new { request.Reason });
        return true;
    }
}
