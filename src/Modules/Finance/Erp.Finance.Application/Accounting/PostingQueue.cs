using Erp.Core.Contracts;
using Erp.Finance.Contracts.Accounting;
using Erp.Finance.Domain.Accounting;
using FluentValidation;

namespace Erp.Finance.Application.Accounting;

// ---------- Coada „De contat” (C2 3.7, 4.4) ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.JournalEntryView)]
public sealed record GetPostingQueue(byte? Status = 1) : IQuery<IReadOnlyList<PostingQueueItemDto>>;

internal sealed class GetPostingQueueHandler(IPostingStore store, ICurrentContext context) : IQueryHandler<GetPostingQueue, IReadOnlyList<PostingQueueItemDto>>
{
    public Task<IReadOnlyList<PostingQueueItemDto>> Handle(GetPostingQueue request, CancellationToken cancellationToken) =>
        store.ListQueueAsync(context.CompanyId, request.Status, cancellationToken);
}

/// <summary>Rezultatul unei reincercari: cate randuri s-au contat si cate au ramas in coada.</summary>
public sealed record PostingRetryDto(int Posted, int StillQueued);

/// <summary>
/// Reincearca randurile in asteptare ale firmei (C2 4.4): unul (Id), cele cu un motiv, sau toate. Randurile care reusesc ies din coada;
/// celelalte raman, cu motivul actualizat.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PostingRetry)]
[Audited("Fin.Posting.Retry")]
public sealed record RetryPostingQueue(long? Id = null, PostingFailureReason? Reason = null) : ICommand<PostingRetryDto>;

internal sealed class RetryPostingQueueHandler(IPostingStore store, PostingService posting, ICurrentContext context)
    : ICommandHandler<RetryPostingQueue, PostingRetryDto>
{
    public async Task<PostingRetryDto> Handle(RetryPostingQueue request, CancellationToken cancellationToken)
    {
        var due = await store.DueAsync(new QueueFilter(context.CompanyId, request.Id, request.Reason), cancellationToken).ConfigureAwait(false);
        var posted = 0;
        foreach (var row in due)
        {
            if ((await posting.RetryAsync(row, cancellationToken).ConfigureAwait(false)).Posted)
            {
                posted++;
            }
        }

        return new PostingRetryDto(posted, due.Count - posted);
    }
}

/// <summary>Ignora un rand din coada, cu motiv (C2 3.7): documentul ramane necontat, iar inchiderea lunii nu il mai asteapta.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PostingIgnore)]
[Audited("Fin.Posting.Ignore")]
public sealed record IgnorePostingQueueItem(long Id, string Reason, byte[] RowVersion) : ICommand<bool>;

internal sealed class IgnorePostingQueueItemValidator : AbstractValidator<IgnorePostingQueueItem>
{
    public IgnorePostingQueueItemValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Scrieți motivul.").MaximumLength(500);
}

internal sealed class IgnorePostingQueueItemHandler(IPostingStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<IgnorePostingQueueItem, bool>
{
    public async Task<bool> Handle(IgnorePostingQueueItem request, CancellationToken cancellationToken)
    {
        await store.IgnoreAsync(context.CompanyId, request.Id, request.Reason.Trim(), request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PostingQueue", request.Id, new { request.Reason });
        return true;
    }
}

// ---------- Recontarea (C2 4.6) ----------

/// <summary>O linie de nota in previzualizarea recontarii.</summary>
public sealed record RepostLineDto(string AccountCode, byte Side, string AmountRon, int? PartnerId, int? CostCenterId);

/// <summary>O nota care s-ar schimba la recontare: liniile de acum si cele noi, sau motivul pentru care nu se poate reconta.</summary>
public sealed record RepostChangeDto(long EntryId, string DisplayNumber, DateOnly AccountingDate, string EventType, IReadOnlyList<RepostLineDto> Current,
    IReadOnlyList<RepostLineDto> Proposed, string? Problem);

/// <summary>Previzualizarea recontarii (C2 4.6 pasul 2): notele automate din lunile neinchise ale intervalului care ar iesi altfel azi.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PostingRepost)]
public sealed record PreviewRepost(DateOnly From, DateOnly To, string? EventType = null) : IQuery<IReadOnlyList<RepostChangeDto>>;

/// <summary>
/// Recontarea notelor automate din lunile neinchise ale intervalului (C2 4.6, decizia 2): fiecare nota care iese altfel cu modelul si conturile
/// de azi isi rescrie liniile, pastreaza numarul si data, iar versiunea veche trece in istoric (NotaContabilaRecontata). Notele care nu se pot
/// reconstrui raman neschimbate.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PostingRepost)]
[Audited("Fin.Posting.Repost")]
public sealed record RepostEntries(DateOnly From, DateOnly To, string Reason, string? EventType = null) : ICommand<int>;

internal sealed class RepostEntriesValidator : AbstractValidator<RepostEntries>
{
    public RepostEntriesValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Scrieți motivul recontării.").MaximumLength(300);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
    }
}

internal sealed class RepostHandlers(IPostingStore store, PostingService posting, IOutbox outbox, ICurrentContext context, IAuditScope audit)
    : IQueryHandler<PreviewRepost, IReadOnlyList<RepostChangeDto>>, ICommandHandler<RepostEntries, int>
{
    public async Task<IReadOnlyList<RepostChangeDto>> Handle(PreviewRepost request, CancellationToken cancellationToken) =>
        (await Changes(request.From, request.To, request.EventType, cancellationToken).ConfigureAwait(false))
            .Select(c => new RepostChangeDto(c.Entry.Id, JournalEntryNumbers.Display(c.Entry.JournalCode, c.Entry.Number)!, c.Entry.AccountingDate, c.Entry.EventType,
                c.Entry.Lines.Select(l => new RepostLineDto(l.AccountCode, l.Side, Domain.Money.Format(l.AmountRon), l.PartnerId, l.CostCenterId)).ToList(),
                c.Result.Lines.Select(l => new RepostLineDto(l.AccountCode, l.Side, Domain.Money.Format(l.AmountRon), l.PartnerId, l.CostCenterId)).ToList(),
                c.Result.Succeeded ? null : c.Result.Details))
            .ToList();

    public async Task<int> Handle(RepostEntries request, CancellationToken cancellationToken)
    {
        var changed = 0;
        foreach (var (entry, result) in await Changes(request.From, request.To, request.EventType, cancellationToken).ConfigureAwait(false))
        {
            if (!result.Succeeded)
            {
                continue;
            }

            var (version, oldTotal, newTotal) = await store.RepostAsync(context.CompanyId, entry.Id, entry.RowVersion, result.VariantId, result.Lines,
                request.Reason.Trim(), context.UserId, cancellationToken).ConfigureAwait(false);
            await outbox.EnqueueAsync(new NotaContabilaRecontata(entry.Id, version, oldTotal, newTotal), cancellationToken).ConfigureAwait(false);
            changed++;
        }

        audit.Describe("Fin.JournalEntry", 0, new { request.From, request.To, request.EventType, request.Reason, Changed = changed });
        return changed;
    }

    private async Task<List<(RepostCandidate Entry, PostingResult Result)>> Changes(DateOnly from, DateOnly to, string? eventType, CancellationToken ct)
    {
        var list = new List<(RepostCandidate, PostingResult)>();
        foreach (var entry in await store.RepostCandidatesAsync(context.CompanyId, from, to, eventType, ct).ConfigureAwait(false))
        {
            var result = await posting.PreviewAsync(context.CompanyId, entry.EventType, entry.Payload, ct).ConfigureAwait(false);
            if (!result.Succeeded || !Same(entry.Lines, result.Lines))
            {
                list.Add((entry, result));
            }
        }

        return list;
    }

    /// <summary>Aceleasi linii, indiferent de ordine: cont, parte, suma si dimensiuni.</summary>
    internal static bool Same(IReadOnlyList<ExistingLine> current, IReadOnlyList<PostingLine> proposed)
    {
        static string Key(string code, byte side, decimal amount, int? p, int? e, int? c, int? pr, int? w, int? s) =>
            FormattableString.Invariant($"{code.ToUpperInvariant()}|{side}|{amount:0.00}|{p}|{e}|{c}|{pr}|{w}|{s}");
        var a = current.Select(l => Key(l.AccountCode, l.Side, l.AmountRon, l.PartnerId, l.EmployeeId, l.CostCenterId, l.ProjectId, l.WarehouseId, l.SiteId))
            .Order(StringComparer.Ordinal);
        var b = proposed.Select(l => Key(l.AccountCode, l.Side, l.AmountRon, l.PartnerId, l.EmployeeId, l.CostCenterId, l.ProjectId, l.WarehouseId, l.SiteId))
            .Order(StringComparer.Ordinal);
        return a.SequenceEqual(b, StringComparer.Ordinal);
    }
}
