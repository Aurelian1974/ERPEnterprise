using Erp.Core.Contracts;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Treasury;
using FluentValidation;

namespace Erp.Finance.Application.Treasury;

/// <summary>O zi a registrului de casa (macheta f1-04): soldurile, starea, paginile, inchiderea si redeschiderea.</summary>
public sealed record CashRegisterDayDto(DateOnly Day, string OpeningBalance, string TotalIn, string TotalOut, string ClosingBalance, bool IsClosed,
    int? FirstPageNo, int? LastPageNo, DateTime? ClosedAt, string? ClosedByName, DateTime? ReopenedAt, string? ReopenedByName, string? ReopenReason,
    int DraftCount);

/// <summary>Un rand al registrului: documentul validat, in ordinea validarii, cu soldul dupa el.</summary>
public sealed record CashRegisterEntryDto(int Id, DateOnly Day, CashDocumentKind Kind, string? Number, string? PersonName, string? Description,
    string? CashIn, string? CashOut, string Balance, CashDocumentStatus Status, bool IsReversal, DateTime? ValidatedAt);

public sealed record CashRegisterDto(int TreasuryAccountId, DateOnly From, DateOnly To, IReadOnlyList<CashRegisterDayDto> Days,
    IReadOnlyList<CashRegisterEntryDto> Entries);

/// <summary>Rezultatul inchiderii: ziua inchisa, paginile si avertizarea de sold (plafonul legal SoldCasa sau plafonul propriu).</summary>
public sealed record CashDayClosedDto(DateOnly Day, string ClosingBalance, int? FirstPageNo, int? LastPageNo, string? BalanceWarning);

public sealed record CashDayClosing(int CashRegisterDayId, DateOnly Day, decimal OpeningBalance, decimal TotalIn, decimal TotalOut,
    decimal ClosingBalance, int? FirstPageNo, int? LastPageNo, decimal? MaxClosingBalance, decimal? LegalLimit, string CurrencyCode);

public sealed record CashRegisterDayRow(DateOnly Day, decimal OpeningBalance, decimal TotalIn, decimal TotalOut, decimal ClosingBalance, bool IsClosed,
    int? FirstPageNo, int? LastPageNo, DateTime? ClosedAt, string? ClosedByName, DateTime? ReopenedAt, string? ReopenedByName, string? ReopenReason,
    int DraftCount);

public sealed record CashRegisterDocumentRow(int Id, DateOnly Day, CashDocumentKind Kind, string? Number, string? PersonName, string? Description,
    decimal TotalAmount, CashDocumentStatus Status, bool IsReversal, DateTime? ValidatedAt);

/// <summary>Accesul la date pentru registrul de casa (fin.usp_CashRegisterDay_*, fin.usp_CashRegister_Get, NET-06).</summary>
public interface ICashRegisterStore
{
    Task<CashDayClosing> CloseAsync(int companyId, int treasuryAccountId, DateOnly day, int userId, CancellationToken ct);

    Task<IReadOnlyList<DateOnly>> ReopenAsync(int companyId, int treasuryAccountId, DateOnly day, string reason, int userId, CancellationToken ct);

    Task<(IReadOnlyList<CashRegisterDayRow> Days, IReadOnlyList<CashRegisterDocumentRow> Documents)?> GetAsync(int companyId, int treasuryAccountId,
        DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>Regulile registrului de casa (F1 3.4 si 4.2), fara acces la date.</summary>
public static class CashRegisterRules
{
    /// <summary>Avertizarea la inchidere (F1 4.2 pasul 2): soldul peste plafonul legal SoldCasa sau peste plafonul propriu al casei.</summary>
    public static string? BalanceWarning(CashDayClosing day)
    {
        var limit = new[] { day.LegalLimit, day.MaxClosingBalance }.Where(l => l is not null).Select(l => l!.Value).DefaultIfEmpty().Min();
        return limit > 0 && day.ClosingBalance > limit
            ? string.Create(System.Globalization.CultureInfo.GetCultureInfo("ro-RO"),
                $"Soldul de {day.ClosingBalance:N2} lei depășește plafonul de {limit:N2} lei; depuneți numerarul în plus la bancă (Numerar depus la bancă).")
            : null;
    }

    /// <summary>Randurile registrului: in fiecare zi, soldul dupa fiecare document pornind de la soldul de inceput al zilei.</summary>
    public static IReadOnlyList<CashRegisterEntryDto> Entries(IReadOnlyList<CashRegisterDayRow> days, IReadOnlyList<CashRegisterDocumentRow> documents)
    {
        var entries = new List<CashRegisterEntryDto>();
        foreach (var group in documents.GroupBy(d => d.Day))
        {
            var balance = days.FirstOrDefault(d => d.Day == group.Key)?.OpeningBalance ?? 0;
            foreach (var d in group)
            {
                var direction = CashDocumentRules.DirectionOf(d.Kind);
                balance += direction == CashDirection.In ? d.TotalAmount : -d.TotalAmount;
                entries.Add(new CashRegisterEntryDto(d.Id, d.Day, d.Kind, d.Number, d.PersonName, d.Description,
                    direction == CashDirection.In ? Money.Format(d.TotalAmount) : null, direction == CashDirection.Out ? Money.Format(d.TotalAmount) : null,
                    Money.Format(balance), d.Status, d.IsReversal, d.ValidatedAt));
            }
        }

        return entries;
    }
}

/// <summary>Registrul de casa (macheta f1-04) pe un interval de cel mult 62 de zile; implicit luna curenta.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashView)]
public sealed record GetCashRegister(int TreasuryAccountId, DateOnly? From = null, DateOnly? To = null) : IQuery<CashRegisterDto>;

internal sealed class GetCashRegisterHandler(ICashRegisterStore store, ICashDocumentStore documents, ICurrentContext context, TimeProvider time)
    : IQueryHandler<GetCashRegister, CashRegisterDto>
{
    public async Task<CashRegisterDto> Handle(GetCashRegister request, CancellationToken cancellationToken)
    {
        var today = BusinessDay.Today(time);
        var from = request.From ?? new DateOnly(today.Year, today.Month, 1);
        var to = request.To ?? from.AddMonths(1).AddDays(-1);
        if (to < from || to.DayNumber - from.DayNumber > 62)
        {
            throw new ErpValidationException(new Dictionary<string, string[]> { ["to"] = ["Intervalul registrului are cel mult 62 de zile."] });
        }

        if (!CashAccess.ViewAll(context)
            && !await documents.IsAssignedAsync(request.TreasuryAccountId, context.UserId, today, false, cancellationToken).ConfigureAwait(false))
        {
            throw CashAccess.NotAssigned();
        }

        var data = await store.GetAsync(context.CompanyId, request.TreasuryAccountId, from, to, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound(FinanceErrorCodes.TreasuryAccountNotFound, "Casa nu există în firma curentă.");
        var days = data.Days.Select(d => new CashRegisterDayDto(d.Day, Money.Format(d.OpeningBalance), Money.Format(d.TotalIn), Money.Format(d.TotalOut),
            Money.Format(d.ClosingBalance), d.IsClosed, d.FirstPageNo, d.LastPageNo, d.ClosedAt, d.ClosedByName, d.ReopenedAt, d.ReopenedByName,
            d.ReopenReason, d.DraftCount)).ToList();
        return new CashRegisterDto(request.TreasuryAccountId, from, to, days, CashRegisterRules.Entries(data.Days, data.Documents));
    }
}

/// <summary>
/// Inchide ziua de casa la cerere (F1 4.2): ziua anterioara cu activitate inchisa, fara ciorne; paginile registrului; RegistruCasaInchis in
/// outbox. Soldul peste plafon (SoldCasa sau plafonul propriu) nu blocheaza: intoarce avertizarea si propunerea de depunere la banca.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashCloseDay)]
[Audited("Fin.Cash.CloseDay")]
public sealed record CloseCashDay(int TreasuryAccountId, DateOnly Day) : ICommand<CashDayClosedDto>;

internal sealed class CloseCashDayHandler(ICashRegisterStore store, ICashDocumentStore documents, IOutbox outbox, ICurrentContext context,
    IAuditScope audit, TimeProvider time) : ICommandHandler<CloseCashDay, CashDayClosedDto>
{
    public async Task<CashDayClosedDto> Handle(CloseCashDay request, CancellationToken cancellationToken)
    {
        if (request.Day > BusinessDay.Today(time))
        {
            throw ErpException.Conflict("FIN_CASH_DAY_IN_FUTURE", "O zi viitoare nu se poate închide.");
        }

        if (!CashAccess.ViewAll(context)
            && !await documents.IsAssignedAsync(request.TreasuryAccountId, context.UserId, request.Day, false, cancellationToken).ConfigureAwait(false))
        {
            throw CashAccess.NotAssigned();
        }

        var closed = await store.CloseAsync(context.CompanyId, request.TreasuryAccountId, request.Day, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        await outbox.EnqueueAsync(CashRegisterEvents.Closed(request.TreasuryAccountId, closed) with { CompanyId = context.CompanyId }, cancellationToken)
            .ConfigureAwait(false);
        var warning = CashRegisterRules.BalanceWarning(closed);
        audit.Describe("Fin.CashRegisterDay", closed.CashRegisterDayId, new
        {
            request.TreasuryAccountId, request.Day, closed.ClosingBalance, closed.FirstPageNo, closed.LastPageNo, Warning = warning,
        });
        return new CashDayClosedDto(closed.Day, Money.Format(closed.ClosingBalance), closed.FirstPageNo, closed.LastPageNo, warning);
    }
}

/// <summary>Evenimentul inchiderii zilei, construit la fel la cerere si din jobul de la miezul noptii.</summary>
public static class CashRegisterEvents
{
    public static RegistruCasaInchis Closed(int treasuryAccountId, CashDayClosing day) =>
        new(treasuryAccountId, day.Day, day.OpeningBalance, day.TotalIn, day.TotalOut, day.ClosingBalance);
}

/// <summary>
/// Redeschide o zi de casa inchisa si zilele ulterioare inchise (F1 4.2 pasul 5): cere Fin.Cash.ReopenDay, un motiv scris si perioada
/// contabila deschisa; se auditeaza.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashReopenDay)]
[Audited("Fin.Cash.ReopenDay")]
public sealed record ReopenCashDay(int TreasuryAccountId, DateOnly Day, string Reason) : ICommand<IReadOnlyList<DateOnly>>, IAccountingDated
{
    public DateOnly AccountingDate => Day;
}

internal sealed class ReopenCashDayValidator : AbstractValidator<ReopenCashDay>
{
    public ReopenCashDayValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Redeschiderea cere un motiv scris.").MaximumLength(500);
    }
}

internal sealed class ReopenCashDayHandler(ICashRegisterStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ReopenCashDay, IReadOnlyList<DateOnly>>
{
    public async Task<IReadOnlyList<DateOnly>> Handle(ReopenCashDay request, CancellationToken cancellationToken)
    {
        var reopened = await store.ReopenAsync(context.CompanyId, request.TreasuryAccountId, request.Day, request.Reason.Trim(), context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CashRegisterDay", null, new { request.TreasuryAccountId, Days = reopened, Reason = request.Reason.Trim() });
        return reopened;
    }
}
