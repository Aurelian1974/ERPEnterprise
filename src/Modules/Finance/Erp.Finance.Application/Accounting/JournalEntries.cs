using Erp.Core.Contracts;
using Erp.Finance.Contracts.Accounting;
using Erp.Finance.Domain;
using FluentValidation;

namespace Erp.Finance.Application.Accounting;

/// <summary>Rezultatul salvarii unei note: Id-ul (bigint) si versiunea.</summary>
public sealed record EntrySaved(long Id, byte[] RowVersion);

public sealed record JournalDto(int Id, int? CompanyId, string Code, string Name, bool IsSystem, bool IsActive);

public sealed record JournalLineInput(int AccountId, byte Side, string AmountRon, string? CurrencyCode = null, string? AmountCurrency = null, int? PartnerId = null,
    int? EmployeeId = null, int? CostCenterId = null, int? ProjectId = null, int? WarehouseId = null, int? SiteId = null, string? Description = null);

public sealed record JournalLineDto(long Id, int LineNo, int AccountId, string AccountCode, string AccountName, byte Side, string AmountRon, string? CurrencyCode,
    string? AmountCurrency, int? PartnerId, string? PartnerName, int? EmployeeId, int? CostCenterId, string? CostCenterCode, int? ProjectId, string? ProjectCode,
    int? WarehouseId, string? WarehouseCode, int? SiteId, string? SiteCode, string? Description, byte? ResolvedFrom);

/// <summary>O nota contabila (macheta c2-02). Status: 1 ciorna, 2 validata, 3 stornata.</summary>
public sealed record JournalEntryDto(long Id, int JournalId, string JournalCode, int? Number, string? DisplayNumber, DateOnly EntryDate, DateOnly AccountingDate,
    EntrySourceKind SourceKind, string? SourceModule, string? SourceDocumentType, long? SourceDocumentId, string? SourceDocumentNumber, byte Status,
    long? ReversalOfId, long? ReversedById, string? Description, string TotalDebit, string TotalCredit, DateTime? ValidatedAt, string? ValidatedByName,
    string? CreatedByName, byte[] RowVersion, IReadOnlyList<JournalLineDto> Lines);

public sealed record JournalEntryItemDto(long Id, string JournalCode, int? Number, string? DisplayNumber, DateOnly AccountingDate, EntrySourceKind SourceKind,
    string? SourceDocumentType, string? SourceDocumentNumber, byte Status, string? Description, string TotalRon, int LineCount, string? DebitAccounts,
    string? CreditAccounts);

public sealed record JournalEntryPageDto(int Total, IReadOnlyList<JournalEntryItemDto> Items);

public sealed record JournalEntryHeader(long Id, int CompanyId, string JournalCode, int Number, DateOnly AccountingDate, EntrySourceKind SourceKind,
    string? SourceDocumentType, long? SourceDocumentId, decimal TotalDebit, byte[] RowVersion);

public sealed record JournalLineRecord(int AccountId, byte Side, decimal AmountRon, string? CurrencyCode, decimal? AmountCurrency, int? PartnerId, int? EmployeeId,
    int? CostCenterId, int? ProjectId, int? WarehouseId, int? SiteId, string? Description);

public sealed record JournalSearch(DateOnly From, DateOnly To, int? JournalId, EntrySourceKind? SourceKind, byte? Status, string? AccountCode, string? Search,
    int Skip, int Take);

/// <summary>Notele contabile prin fin.usp_JournalEntry_* (NET-06).</summary>
public interface IJournalEntryStore
{
    Task<IReadOnlyList<JournalDto>> JournalsAsync(int companyId, CancellationToken ct);

    Task<EntrySaved> SaveDraftAsync(long? id, int companyId, int journalId, DateOnly entryDate, DateOnly accountingDate, EntrySourceKind kind, string? description,
        string? documentRef, IReadOnlyList<JournalLineRecord> lines, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<JournalEntryHeader> ValidateAsync(int companyId, long id, byte[] rowVersion, bool fourEyes, int userId, CancellationToken ct);

    Task<JournalEntryHeader> ReverseAsync(int companyId, long id, DateOnly accountingDate, string description, bool black, bool allowAutomatic, int userId,
        CancellationToken ct);

    Task DeleteDraftAsync(int companyId, long id, CancellationToken ct);

    Task<JournalEntryDto?> GetAsync(int companyId, long id, CancellationToken ct);

    Task<JournalEntryPageDto> SearchAsync(int companyId, JournalSearch search, CancellationToken ct);

    Task<bool> TouchesControlledAccountsAsync(long id, CancellationToken ct);

    Task<string?> SettingAsync(int companyId, string code, CancellationToken ct);
}

public static class JournalEntryNumbers
{
    /// <summary>Numarul afisat al notei: codul jurnalului si numarul pe 5 cifre (IN 00021).</summary>
    public static string? Display(string journalCode, int? number) => number is { } n ? $"{journalCode} {n:D5}" : null;

    public static NotaContabilaValidata Event(JournalEntryHeader h) =>
        new(h.Id, h.JournalCode, h.Number, h.AccountingDate, h.SourceKind, h.SourceDocumentType, h.SourceDocumentId, h.TotalDebit);

    public static ErpException NotFound() => new(ErpErrorKind.NotFound, "FIN_ENTRY_NOT_FOUND", "Nota nu există.");
}

// ---------- Interogari ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.JournalEntryView)]
public sealed record GetJournals : IQuery<IReadOnlyList<JournalDto>>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.JournalEntryView)]
public sealed record GetJournalEntry(long Id) : IQuery<JournalEntryDto>;

/// <summary>Notele firmei pe interval (macheta c2-01), cu filtre si paginare.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.JournalEntryView)]
public sealed record SearchJournalEntries(DateOnly From, DateOnly To, int? JournalId = null, EntrySourceKind? SourceKind = null, byte? Status = null,
    string? AccountCode = null, string? Search = null, int Skip = 0, int Take = 100) : IQuery<JournalEntryPageDto>;

internal sealed class JournalEntryQueries(IJournalEntryStore store, ICurrentContext context) :
    IQueryHandler<GetJournals, IReadOnlyList<JournalDto>>, IQueryHandler<GetJournalEntry, JournalEntryDto>, IQueryHandler<SearchJournalEntries, JournalEntryPageDto>
{
    public Task<IReadOnlyList<JournalDto>> Handle(GetJournals request, CancellationToken cancellationToken) => store.JournalsAsync(context.CompanyId, cancellationToken);

    public async Task<JournalEntryDto> Handle(GetJournalEntry request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw JournalEntryNumbers.NotFound();

    public Task<JournalEntryPageDto> Handle(SearchJournalEntries request, CancellationToken cancellationToken) =>
        store.SearchAsync(context.CompanyId, new JournalSearch(request.From, request.To, request.JournalId, request.SourceKind, request.Status,
            string.IsNullOrWhiteSpace(request.AccountCode) ? null : request.AccountCode.Trim(), string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            Math.Max(request.Skip, 0), Math.Clamp(request.Take, 1, 500)), cancellationToken);
}

// ---------- Nota manuala ----------

/// <summary>
/// Salveaza ciorna unei note manuale sau de reclasificare (C2 3.8, 4.3): jurnalul (implicit DV), data contabila, justificativul, liniile.
/// Verificarile (echilibru, conturi, dimensiuni, partener, moneda, perioada) se fac la validare.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ManualEntryCreate)]
[Audited("Fin.ManualEntry.Save")]
public sealed record SaveManualEntry(long? Id, int JournalId, DateOnly AccountingDate, string? Description, string? DocumentRef,
    IReadOnlyList<JournalLineInput> Lines, bool IsReclassification = false, byte[]? RowVersion = null) : ICommand<EntrySaved>;

internal sealed class SaveManualEntryValidator : AbstractValidator<SaveManualEntry>
{
    public SaveManualEntryValidator()
    {
        RuleFor(x => x.Description).MaximumLength(400);
        RuleFor(x => x.DocumentRef).MaximumLength(50);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Adăugați cel puțin o linie.").Must(l => l.Count <= 2000);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Side).InclusiveBetween((byte)1, (byte)2);
            l.RuleFor(x => x.AmountRon).Must(a => Money.ParseOptional(a) is { } v && v != 0).WithMessage("Suma liniei e diferită de zero.");
            l.RuleFor(x => x.CurrencyCode).Length(3).When(x => !string.IsNullOrWhiteSpace(x.CurrencyCode));
            l.RuleFor(x => x.AmountCurrency).Must(a => Money.ParseOptional(a) is not null).When(x => !string.IsNullOrWhiteSpace(x.CurrencyCode))
                .WithMessage("Linia în valută are și suma în valută.");
            l.RuleFor(x => x.Description).MaximumLength(400);
        });
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null);
    }
}

internal sealed class SaveManualEntryHandler(IJournalEntryStore store, ICurrentContext context, TimeProvider time, IAuditScope audit)
    : ICommandHandler<SaveManualEntry, EntrySaved>
{
    public async Task<EntrySaved> Handle(SaveManualEntry request, CancellationToken cancellationToken)
    {
        var lines = request.Lines.Select(l =>
        {
            var currency = string.IsNullOrWhiteSpace(l.CurrencyCode) ? null : l.CurrencyCode.Trim().ToUpperInvariant();
            return new JournalLineRecord(l.AccountId, l.Side, Money.ParseOptional(l.AmountRon)!.Value, currency, currency is null ? null : Money.ParseOptional(l.AmountCurrency),
                l.PartnerId, l.EmployeeId, l.CostCenterId, l.ProjectId, l.WarehouseId, l.SiteId, string.IsNullOrWhiteSpace(l.Description) ? null : l.Description.Trim());
        }).ToList();
        var saved = await store.SaveDraftAsync(request.Id, context.CompanyId, request.JournalId, Treasury.BusinessDay.Today(time), request.AccountingDate,
            request.IsReclassification ? EntrySourceKind.Reclassification : EntrySourceKind.Manual, request.Description?.Trim(), request.DocumentRef?.Trim(), lines,
            request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.JournalEntry", saved.Id, request with { RowVersion = null });
        return saved;
    }
}

/// <summary>
/// Valideaza o nota manuala (C2 4.3): verificarile pe set, perioada (deschisa sau inchisa operativ), conturile tinute de module doar cu dreptul
/// Fin.ManualEntry.ControlledAccounts (51851), patru ochi cu Fin.ManualEntry.FourEyes (implicit oprit), numarul din jurnal si evenimentul
/// NotaContabilaValidata.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ManualEntryValidate)]
[Audited("Fin.ManualEntry.Validate")]
public sealed record ValidateManualEntry(long Id, byte[] RowVersion) : ICommand<EntrySaved>;

internal sealed class ValidateManualEntryHandler(IJournalEntryStore store, IOutbox outbox, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ValidateManualEntry, EntrySaved>
{
    public const string FourEyesSetting = "Fin.ManualEntry.FourEyes";

    public async Task<EntrySaved> Handle(ValidateManualEntry request, CancellationToken cancellationToken)
    {
        if (!context.HasPermission(FinancePermissions.ManualEntryControlledAccounts)
            && await store.TouchesControlledAccountsAsync(request.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new ErpException(ErpErrorKind.Forbidden, "FIN_ENTRY_CONTROLLED_ACCOUNT",
                "Nota folosește un cont ținut de module (clienți, furnizori, stocuri, trezorerie, TVA); cere dreptul Fin.ManualEntry.ControlledAccounts.");
        }

        var fourEyes = string.Equals(await store.SettingAsync(context.CompanyId, FourEyesSetting, cancellationToken).ConfigureAwait(false), "true",
            StringComparison.OrdinalIgnoreCase);
        var header = await store.ValidateAsync(context.CompanyId, request.Id, request.RowVersion, fourEyes, context.UserId, cancellationToken).ConfigureAwait(false);
        await outbox.EnqueueAsync(JournalEntryNumbers.Event(header), cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.JournalEntry", request.Id, new { Number = JournalEntryNumbers.Display(header.JournalCode, header.Number) });
        return new EntrySaved(header.Id, header.RowVersion);
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ManualEntryCreate)]
[Audited("Fin.ManualEntry.Delete")]
public sealed record DeleteManualEntry(long Id) : ICommand<bool>;

internal sealed class DeleteManualEntryHandler(IJournalEntryStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<DeleteManualEntry, bool>
{
    public async Task<bool> Handle(DeleteManualEntry request, CancellationToken cancellationToken)
    {
        await store.DeleteDraftAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.JournalEntry", request.Id, null);
        return true;
    }
}

/// <summary>
/// Storneaza o nota validata (C2 4.5): implicit „in rosu” (setarea Fin.Posting.ReversalStyle = Black pentru „in negru”), la data aleasa,
/// cu motiv. Notele automate doar cu Fin.JournalEntry.ReverseAutomatic (51852): altfel se storneaza documentul sursa.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.JournalEntryReverse)]
[Audited("Fin.JournalEntry.Reverse")]
public sealed record ReverseJournalEntry(long Id, DateOnly AccountingDate, string Reason) : ICommand<EntrySaved>;

internal sealed class ReverseJournalEntryValidator : AbstractValidator<ReverseJournalEntry>
{
    public ReverseJournalEntryValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Scrieți motivul stornării.").MaximumLength(300);
}

internal sealed class ReverseJournalEntryHandler(IJournalEntryStore store, IOutbox outbox, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ReverseJournalEntry, EntrySaved>
{
    public const string ReversalStyleSetting = "Fin.Posting.ReversalStyle";

    public async Task<EntrySaved> Handle(ReverseJournalEntry request, CancellationToken cancellationToken)
    {
        var black = string.Equals(await store.SettingAsync(context.CompanyId, ReversalStyleSetting, cancellationToken).ConfigureAwait(false), "Black",
            StringComparison.OrdinalIgnoreCase);
        var header = await store.ReverseAsync(context.CompanyId, request.Id, request.AccountingDate, $"Storno: {request.Reason.Trim()}", black,
            context.HasPermission(FinancePermissions.JournalEntryReverseAutomatic), context.UserId, cancellationToken).ConfigureAwait(false);
        await outbox.EnqueueAsync(JournalEntryNumbers.Event(header), cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.JournalEntry", request.Id, new { ReversalId = header.Id, request.Reason });
        return new EntrySaved(header.Id, header.RowVersion);
    }
}
