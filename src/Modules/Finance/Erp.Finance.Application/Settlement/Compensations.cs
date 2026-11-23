using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;
using FluentValidation;

namespace Erp.Finance.Application.Settlement;

/// <summary>Felul compensarii (F3 3.4).</summary>
public enum CompensationKind : byte
{
    SamePartner = 1,
    Assignment = 2,
    ThreeParties = 3,
    ExternalSession = 4,
}

public enum CompensationStatus : byte
{
    Draft = 1,
    Validated = 2,
    Reversed = 3,
}

public sealed record CompensationLineInput(long OpenItemId, string Amount);

public sealed record CompensationLineRecord(short LineNumber, long OpenItemId, decimal Amount);

public sealed record CompensationLineDto(short LineNumber, long OpenItemId, int PartnerId, string PartnerName, PartnerRole Role, OpenItemKind Kind, string DocumentNo,
    DateOnly DocumentDate, DateOnly DueDate, string ItemAmount, string OpenAmount, string Amount, bool IsVatOnCollection);

public sealed record CompensationDetailDto(int Id, string? Number, DateOnly DocumentDate, CompensationKind Kind, string? ExternalRef, string CurrencyCode,
    string TotalAmount, CompensationStatus Status, int? SignedAttachmentId, string? Note, DateTime? ValidatedAt, DateTime? ReversedAt, DateTime CreatedAt,
    string? CreatedByName, IReadOnlyList<CompensationLineDto> Lines, byte[] RowVersion);

public sealed record CompensationItemDto(int Id, string? Number, DateOnly DocumentDate, CompensationKind Kind, string CurrencyCode, string TotalAmount,
    CompensationStatus Status, bool HasSignedScan, string? Partners);

public sealed record CompensationProposalDto(int PartnerId, string PartnerName, string CurrencyCode, string Amount);

public sealed record CompensationValidatedLine(short LineNumber, long OpenItemId, decimal Amount, long TechnicalOpenItemId, int PartnerId, PartnerRole Role);

/// <summary>Accesul la date pentru PV-urile de compensare (fin.usp_Compensation_*, NET-06).</summary>
public interface ICompensationStore
{
    Task<FinanceSaved> SaveAsync(int companyId, int? id, DateOnly documentDate, CompensationKind kind, string? externalRef, string currencyCode, string? note,
        IReadOnlyList<CompensationLineRecord> lines, byte[]? rowVersion, int userId, CancellationToken ct);

    Task DeleteAsync(int companyId, int id, byte[] rowVersion, CancellationToken ct);

    Task<CompensationDetailDto?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<CompensationItemDto>> ListAsync(int companyId, int? year, CompensationStatus? status, bool withoutSigned, string? search, CancellationToken ct);

    Task<IReadOnlyList<CompensationValidatedLine>> ValidateAsync(int companyId, int id, string number, byte[] rowVersion, int userId, CancellationToken ct);

    Task<(IReadOnlyList<long> Allocations, IReadOnlyList<(long Id, decimal Amount)> TechnicalItems)> ReverseAsync(int companyId, int id, byte[] rowVersion, int userId,
        CancellationToken ct);

    Task<byte[]> SetSignedAsync(int companyId, int id, int attachmentId, int userId, CancellationToken ct);

    Task<IReadOnlyList<CompensationProposalDto>> ProposalsAsync(int companyId, CancellationToken ct);
}

internal static class CompensationRules
{
    public const string DocumentType = "Finance.Compensation";

    public static ErpException NotFound() => ErpException.NotFound("FIN_COMPENSATION_NOT_FOUND", "Procesul-verbal de compensare nu există.");
}

// ---------- Lista, fisa, propunerile ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetCompensations(int? Year = null, CompensationStatus? Status = null, bool WithoutSigned = false, string? Search = null)
    : IQuery<IReadOnlyList<CompensationItemDto>>;

internal sealed class GetCompensationsHandler(ICompensationStore store, ICurrentContext context) : IQueryHandler<GetCompensations, IReadOnlyList<CompensationItemDto>>
{
    public Task<IReadOnlyList<CompensationItemDto>> Handle(GetCompensations request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.Year, request.Status, request.WithoutSigned, string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            cancellationToken);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetCompensation(int Id) : IQuery<CompensationDetailDto>;

internal sealed class GetCompensationHandler(ICompensationStore store, ICurrentContext context) : IQueryHandler<GetCompensation, CompensationDetailDto>
{
    public async Task<CompensationDetailDto> Handle(GetCompensation request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CompensationRules.NotFound();
}

/// <summary>Propunerile automate (F3 4.4 pasul 1): partenerii client si furnizor cu suma compensabila (minimul dintre creante si datorii).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetCompensationProposals : IQuery<IReadOnlyList<CompensationProposalDto>>;

internal sealed class GetCompensationProposalsHandler(ICompensationStore store, ICurrentContext context)
    : IQueryHandler<GetCompensationProposals, IReadOnlyList<CompensationProposalDto>>
{
    public Task<IReadOnlyList<CompensationProposalDto>> Handle(GetCompensationProposals request, CancellationToken cancellationToken) =>
        store.ProposalsAsync(context.CompanyId, cancellationToken);
}

/// <summary>
/// Liniile propuse pentru compensarea bilaterala la un partener (F3 4.4 pasul 1): suma minima dintre creante si datorii, stinsa pe fiecare parte
/// in ordinea scadentelor (restantele intai), fara partidele in litigiu.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record ProposeCompensation(int PartnerId, string CurrencyCode) : IQuery<IReadOnlyList<CompensationLineInput>>;

internal sealed class ProposeCompensationHandler(ISettlementStore store, ICurrentContext context) : IQueryHandler<ProposeCompensation, IReadOnlyList<CompensationLineInput>>
{
    public async Task<IReadOnlyList<CompensationLineInput>> Handle(ProposeCompensation request, CancellationToken cancellationToken)
    {
        var items = (await store.ListOpenAsync(context.CompanyId, request.PartnerId, null, cancellationToken).ConfigureAwait(false))
            .Where(i => i.CurrencyCode == request.CurrencyCode && !i.IsDisputed && i.Kind is OpenItemKind.Invoice or OpenItemKind.AdvanceInvoice
                or OpenItemKind.DebitNote or OpenItemKind.OpeningBalance)
            .ToList();
        var receivables = items.Where(i => i.Role == PartnerRole.Customer && Money.ParseOptional(i.OpenAmount) > 0).ToList();
        var payables = items.Where(i => i.Role == PartnerRole.Supplier && Money.ParseOptional(i.OpenAmount) < 0).ToList();
        var amount = Math.Min(receivables.Sum(i => Money.ParseOptional(i.OpenAmount) ?? 0), -payables.Sum(i => Money.ParseOptional(i.OpenAmount) ?? 0));
        if (amount <= 0)
        {
            return [];
        }

        static IEnumerable<CompensationLineInput> Take(IEnumerable<OpenItemListDto> side, decimal amount, decimal sign) =>
            SettlementRules.Distribute(amount, side.Select(i => new AllocationCandidate(i.OpenItemId, Math.Abs(Money.ParseOptional(i.OpenAmount) ?? 0))))
                .Select(p => new CompensationLineInput(p.Id, Money.Format(sign * p.Amount)));

        return [.. Take(receivables, amount, 1m), .. Take(payables, amount, -1m)];
    }
}

// ---------- Ciorna ----------

/// <summary>Salveaza un PV de compensare ciorna (F3 3.4, 4.4 pasul 1); echilibrul se cere la validare.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CompensationCreate)]
[Audited("Fin.Compensation.Save")]
public sealed record SaveCompensation(int? Id, DateOnly DocumentDate, CompensationKind Kind, string? ExternalRef, string CurrencyCode, string? Note,
    IReadOnlyList<CompensationLineInput> Lines, byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SaveCompensationValidator : AbstractValidator<SaveCompensation>
{
    public SaveCompensationValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExternalRef).MaximumLength(100);
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Procesul-verbal are cel puțin o linie.")
            .Must(l => l.Count <= 500 && l.Select(x => x.OpenItemId).Distinct().Count() == l.Count).WithMessage("Fiecare partidă apare o singură dată.");
        RuleForEach(x => x.Lines).ChildRules(l =>
            l.RuleFor(x => x.Amount).Must(a => Money.TryParse(a, out var v) && v != 0).WithMessage("Suma compensată este diferită de zero."));
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveCompensationHandler(ICompensationStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveCompensation, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveCompensation request, CancellationToken cancellationToken)
    {
        var lines = request.Lines.Select((l, i) => new CompensationLineRecord((short)(i + 1), l.OpenItemId, Money.ParseOptional(l.Amount) ?? 0)).ToList();
        var saved = await store.SaveAsync(context.CompanyId, request.Id, request.DocumentDate, request.Kind,
            string.IsNullOrWhiteSpace(request.ExternalRef) ? null : request.ExternalRef.Trim(), request.CurrencyCode.ToUpperInvariant(),
            string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), lines, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.Compensation", saved.Id, new { request.Kind, request.DocumentDate, Lines = lines.Select(l => new { l.OpenItemId, l.Amount }) });
        return saved;
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CompensationCreate)]
[Audited("Fin.Compensation.Delete")]
public sealed record DeleteCompensation(int Id, byte[] RowVersion) : ICommand<bool>;

internal sealed class DeleteCompensationHandler(ICompensationStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<DeleteCompensation, bool>
{
    public async Task<bool> Handle(DeleteCompensation request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(context.CompanyId, request.Id, request.RowVersion, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.Compensation", request.Id, null);
        return true;
    }
}

// ---------- Validare si storno ----------

/// <summary>
/// Valideaza PV-ul (F3 4.4 pasul 3): numarul din seria Finance.Compensation, partidele tehnice pe partener si rol si alocarile (Source = 3),
/// cu TVA-ul la incasare (compensarea conteaza ca plata) si evenimentele; apoi CompensareValidata. Perioada datei PV-ului trebuie sa fie deschisa.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CompensationValidate)]
[Audited("Fin.Compensation.Validate")]
public sealed record ValidateCompensation(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class ValidateCompensationHandler(ICompensationStore store, SettlementService settlement, INumberSeriesService numbers, IPeriodLock periods,
    IOutbox outbox, ICurrentContext context, IAuditScope audit) : ICommandHandler<ValidateCompensation, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(ValidateCompensation request, CancellationToken cancellationToken)
    {
        var current = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CompensationRules.NotFound();
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, current.DocumentDate, cancellationToken).ConfigureAwait(false);
        var number = (await numbers.NextAsync(context.CompanyId, CompensationRules.DocumentType, current.DocumentDate, cancellationToken).ConfigureAwait(false))
            .Formatted;
        var lines = await store.ValidateAsync(context.CompanyId, request.Id, number, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        var pairs = lines.Select(l => l.Amount > 0
            ? new AllocationRequest(l.OpenItemId, l.TechnicalOpenItemId, l.Amount, current.DocumentDate, AllocationSource.Compensation) { CompensationId = request.Id }
            : new AllocationRequest(l.TechnicalOpenItemId, l.OpenItemId, -l.Amount, current.DocumentDate, AllocationSource.Compensation) { CompensationId = request.Id })
            .ToList();
        await settlement.AllocateAsync(context.CompanyId, pairs, cancellationToken).ConfigureAwait(false);
        await outbox.EnqueueAsync(new CompensareValidata(request.Id, number, current.DocumentDate,
            lines.Select(l => new CompensareValidataLinie(l.OpenItemId, l.PartnerId, l.Role, l.Amount)).ToList()) { CompanyId = context.CompanyId }, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.Compensation", request.Id, new { Number = number, current.DocumentDate, Total = current.TotalAmount });
        var after = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CompensationRules.NotFound();
        return new FinanceSaved(request.Id, after.RowVersion);
    }
}

/// <summary>Storneaza un PV validat (F3 4.4 pasul 4): alocarile lui se desfac (TVA-ul invers), iar partidele tehnice se sting intre ele.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CompensationValidate)]
[Audited("Fin.Compensation.Reverse")]
public sealed record ReverseCompensation(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class ReverseCompensationHandler(ICompensationStore store, ISettlementStore settlementStore, SettlementService settlement, IPeriodLock periods,
    IOutbox outbox, ICurrentContext context, TimeProvider time, IAuditScope audit) : ICommandHandler<ReverseCompensation, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(ReverseCompensation request, CancellationToken cancellationToken)
    {
        var current = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CompensationRules.NotFound();
        var today = BusinessDay.Today(time);
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, current.DocumentDate, cancellationToken).ConfigureAwait(false);
        var (allocations, technical) = await store.ReverseAsync(context.CompanyId, request.Id, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (allocations.Count > 0)
        {
            var undone = await settlementStore.UndoAsync(context.CompanyId, allocations, "Stornarea procesului-verbal de compensare", true, today, context.UserId,
                cancellationToken).ConfigureAwait(false);
            await SettlementEvents.PublishAsync(outbox, undone, true, cancellationToken).ConfigureAwait(false);
        }

        // Partidele tehnice (suma lor e zero) se sting intre ele, ca soldurile partenerilor sa revina la cele dinaintea PV-ului.
        var debits = technical.Where(t => t.Amount > 0).Select(t => new { t.Id, Left = t.Amount }).ToList();
        var credits = technical.Where(t => t.Amount < 0).Select(t => new { t.Id, Left = -t.Amount }).ToList();
        var pairs = new List<AllocationRequest>();
        var dLeft = debits.Select(d => d.Left).ToArray();
        var cLeft = credits.Select(c => c.Left).ToArray();
        for (int i = 0, j = 0; i < debits.Count && j < credits.Count;)
        {
            var amount = Math.Min(dLeft[i], cLeft[j]);
            pairs.Add(new AllocationRequest(debits[i].Id, credits[j].Id, amount, today, AllocationSource.Compensation) { CompensationId = request.Id });
            dLeft[i] -= amount;
            cLeft[j] -= amount;
            if (dLeft[i] == 0)
            {
                i++;
            }

            if (cLeft[j] == 0)
            {
                j++;
            }
        }

        await settlement.AllocateAsync(context.CompanyId, pairs, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.Compensation", request.Id, new { current.Number, Reversed = today });
        var after = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CompensationRules.NotFound();
        return new FinanceSaved(request.Id, after.RowVersion);
    }
}

// ---------- PV-ul semnat (atasament) ----------

/// <summary>Ataseaza PV-ul semnat de parti, scanat (F3 4.4 pasul 2), la un PV ciorna sau validat; fisierul trece prin regulile atasamentelor (SEC-05).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CompensationCreate)]
[Audited("Fin.Compensation.AttachSigned")]
public sealed record AttachCompensationScan(int Id, string FileName, byte[] Content) : ICommand<FinanceSaved>;

internal sealed class AttachCompensationScanHandler(ICompensationStore store, IAttachmentStore attachments, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<AttachCompensationScan, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(AttachCompensationScan request, CancellationToken cancellationToken)
    {
        _ = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CompensationRules.NotFound();
        using var content = new MemoryStream(request.Content, writable: false);
        var attachment = await attachments.SaveAsync(CompensationRules.DocumentType, request.Id, request.FileName, "application/octet-stream", content,
            cancellationToken).ConfigureAwait(false);
        var version = await store.SetSignedAsync(context.CompanyId, request.Id, attachment, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.Compensation", request.Id, new { AttachmentId = attachment, Size = request.Content.Length });
        return new FinanceSaved(request.Id, version);
    }
}

public sealed record DownloadedFile(string FileName, string ContentType, byte[] Content);

/// <summary>Descarca PV-ul semnat atasat.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetCompensationScan(int Id) : IQuery<DownloadedFile>;

internal sealed class GetCompensationScanHandler(ICompensationStore store, IAttachmentStore attachments, ICurrentContext context)
    : IQueryHandler<GetCompensationScan, DownloadedFile>
{
    public async Task<DownloadedFile> Handle(GetCompensationScan request, CancellationToken cancellationToken)
    {
        var compensation = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CompensationRules.NotFound();
        var id = compensation.SignedAttachmentId ?? throw ErpException.NotFound("CORE_ATTACHMENT_NOT_FOUND", "Procesul-verbal semnat nu a fost atașat.");
        var info = await attachments.GetInfoAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ATTACHMENT_NOT_FOUND", "Atașamentul nu există.");
        await using var stream = await attachments.OpenAsync(id, cancellationToken).ConfigureAwait(false);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        return new DownloadedFile(info.FileName, info.ContentType, copy.ToArray());
    }
}

/// <summary>Tiparirea PV-ului (F3 4.4 pasul 2), PDF generat la cerere de gazda (QuestPDF).</summary>
public interface ICompensationPrinter
{
    byte[] Print(CompensationDetailDto compensation, CompanySnapshotDto company);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record PrintCompensation(int Id) : IQuery<DownloadedFile>;

internal sealed class PrintCompensationHandler(ICompensationStore store, ICompensationPrinter printer, ICompanyDirectory companies, ICurrentContext context)
    : IQueryHandler<PrintCompensation, DownloadedFile>
{
    public async Task<DownloadedFile> Handle(PrintCompensation request, CancellationToken cancellationToken)
    {
        var compensation = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CompensationRules.NotFound();
        var company = await CompanyAtAsync(companies, context.CompanyId, compensation.DocumentDate, cancellationToken).ConfigureAwait(false);
        return new DownloadedFile($"PV compensare {compensation.Number ?? $"ciorna {compensation.Id}"}.pdf", "application/pdf", printer.Print(compensation, company));
    }

    /// <summary>Datele firmei la data documentului; pentru un document dinaintea primei versiuni a firmei in sistem, datele ei actuale.</summary>
    internal static async Task<CompanySnapshotDto> CompanyAtAsync(ICompanyDirectory companies, int companyId, DateOnly date, CancellationToken ct)
    {
        try
        {
            return await companies.GetAtAsync(companyId, date, ct).ConfigureAwait(false);
        }
        catch (ErpException e) when (e.Kind == ErpErrorKind.NotFound)
        {
            return await companies.GetAtAsync(companyId, DateOnly.FromDateTime(DateTime.UtcNow), ct).ConfigureAwait(false);
        }
    }
}
