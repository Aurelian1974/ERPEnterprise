using System.Globalization;
using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;
using FluentValidation;

namespace Erp.Finance.Application.Settlement;

/// <summary>Starea unei confirmari de sold (F3 3.9): 0 generata (netrimisa), 1 trimisa, 2 confirmata, 3 contestata, 4 fara raspuns.</summary>
public enum ConfirmationStatus : byte
{
    Generated = 0,
    Sent = 1,
    Confirmed = 2,
    Contested = 3,
    NoAnswer = 4,
}

public sealed record BalanceConfirmationBatchDto(int Id, string Number, DateOnly AsOfDate, PartnerRole? Role, string MinBalance, DateTime? SentAt, int Total,
    int Confirmed, int Contested, int Unanswered);

public sealed record BalanceConfirmationDto(int Id, int BatchId, int PartnerId, string PartnerName, string? FiscalCode, PartnerRole Role, string Number,
    DateOnly AsOfDate, string OurBalance, int? GeneratedAttachmentId, DateTime? SentAt, string? SentTo, ConfirmationStatus Status, DateOnly? ResponseDate,
    string? PartnerBalance, string? Difference, string? ResponseNote, int? ResponseAttachmentId, bool IsResolved, string? Email, byte? EmailStatus,
    string? EmailError, byte[] RowVersion);

public sealed record BalanceConfirmationsDto(IReadOnlyList<BalanceConfirmationBatchDto> Batches, IReadOnlyList<BalanceConfirmationDto> Items);

/// <summary>O partida deschisa la data confirmarii (PDF-ul): suma si restul in moneda partidei, restul in lei.</summary>
public sealed record OpenItemAsOf(long OpenItemId, OpenItemKind Kind, string DocumentNo, DateOnly DocumentDate, DateOnly DueDate, string CurrencyCode, decimal Amount,
    decimal OpenAmount, decimal OpenRon);

/// <summary>Actualizarea unei confirmari (fin.usp_BalanceConfirmation_Update); campurile NULL raman cum sunt.</summary>
public sealed record ConfirmationUpdate(int Id)
{
    public int? GeneratedAttachmentId { get; init; }

    public string? SentTo { get; init; }

    public int? EmailMessageId { get; init; }

    public ConfirmationStatus? Status { get; init; }

    public DateOnly? ResponseDate { get; init; }

    public decimal? PartnerBalance { get; init; }

    public string? ResponseNote { get; init; }

    public int? ResponseAttachmentId { get; init; }

    public bool? IsResolved { get; init; }

    public byte[]? RowVersion { get; init; }
}

/// <summary>Accesul la date pentru confirmarile de sold (fin.usp_BalanceConfirmation_*, NET-06).</summary>
public interface IBalanceConfirmationStore
{
    Task<(int BatchId, IReadOnlyList<BalanceConfirmationDto> Items)> GenerateAsync(int companyId, string number, DateOnly asOf, PartnerRole? role,
        decimal minBalance, IReadOnlyList<int>? partnerIds, int userId, CancellationToken ct);

    Task<IReadOnlyList<OpenItemAsOf>> OpenItemsAsOfAsync(int companyId, int partnerId, PartnerRole role, DateOnly asOf, CancellationToken ct);

    Task<byte[]> UpdateAsync(int companyId, ConfirmationUpdate update, int userId, CancellationToken ct);

    Task MarkBatchSentAsync(int companyId, int batchId, CancellationToken ct);

    Task<BalanceConfirmationsDto> ListAsync(int companyId, int? batchId, CancellationToken ct);

    Task<IReadOnlyList<BalanceConfirmationDto>> GetAsync(int companyId, int? id, int? batchId, CancellationToken ct);
}

/// <summary>PDF-ul confirmarii (F3 4.6 pasul 1): partidele deschise la data, soldul si locul pentru raspunsul partenerului.</summary>
public interface IBalanceConfirmationPrinter
{
    byte[] Print(BalanceConfirmationDto confirmation, IReadOnlyList<OpenItemAsOf> items, CompanySnapshotDto company);
}

internal static class ConfirmationRules
{
    public const string DocumentType = "Finance.BalanceConfirmation";

    public static ErpException NotFound() => ErpException.NotFound("FIN_BALANCE_CONFIRMATION_NOT_FOUND", "Confirmarea de sold nu există.");

    public static async Task<DownloadedFile> DownloadAsync(IAttachmentStore attachments, int? id, string missing, CancellationToken ct)
    {
        var attachmentId = id ?? throw ErpException.NotFound("CORE_ATTACHMENT_NOT_FOUND", missing);
        var info = await attachments.GetInfoAsync(attachmentId, ct).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ATTACHMENT_NOT_FOUND", "Atașamentul nu există.");
        await using var stream = await attachments.OpenAsync(attachmentId, ct).ConfigureAwait(false);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct).ConfigureAwait(false);
        return new DownloadedFile(info.FileName, info.ContentType, copy.ToArray());
    }
}

// ---------- Lista ----------

/// <summary>Loturile de confirmari si confirmarile unui lot (implicit ultimul), pentru macheta f3-07.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetBalanceConfirmations(int? BatchId = null) : IQuery<BalanceConfirmationsDto>;

internal sealed class GetBalanceConfirmationsHandler(IBalanceConfirmationStore store, ICurrentContext context)
    : IQueryHandler<GetBalanceConfirmations, BalanceConfirmationsDto>
{
    public Task<BalanceConfirmationsDto> Handle(GetBalanceConfirmations request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.BatchId, cancellationToken);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetBalanceConfirmationPdf(int Id) : IQuery<DownloadedFile>;

internal sealed class GetBalanceConfirmationPdfHandler(IBalanceConfirmationStore store, IAttachmentStore attachments, ICurrentContext context)
    : IQueryHandler<GetBalanceConfirmationPdf, DownloadedFile>
{
    public async Task<DownloadedFile> Handle(GetBalanceConfirmationPdf request, CancellationToken cancellationToken)
    {
        var confirmation = (await store.GetAsync(context.CompanyId, request.Id, null, cancellationToken).ConfigureAwait(false)).SingleOrDefault()
            ?? throw ConfirmationRules.NotFound();
        return await ConfirmationRules.DownloadAsync(attachments, confirmation.GeneratedAttachmentId, "Confirmarea nu are PDF generat.", cancellationToken)
            .ConfigureAwait(false);
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetBalanceConfirmationResponseFile(int Id) : IQuery<DownloadedFile>;

internal sealed class GetBalanceConfirmationResponseFileHandler(IBalanceConfirmationStore store, IAttachmentStore attachments, ICurrentContext context)
    : IQueryHandler<GetBalanceConfirmationResponseFile, DownloadedFile>
{
    public async Task<DownloadedFile> Handle(GetBalanceConfirmationResponseFile request, CancellationToken cancellationToken)
    {
        var confirmation = (await store.GetAsync(context.CompanyId, request.Id, null, cancellationToken).ConfigureAwait(false)).SingleOrDefault()
            ?? throw ConfirmationRules.NotFound();
        return await ConfirmationRules.DownloadAsync(attachments, confirmation.ResponseAttachmentId, "Răspunsul partenerului nu a fost atașat.", cancellationToken)
            .ConfigureAwait(false);
    }
}

// ---------- Generare ----------

/// <summary>
/// Genereaza un lot de confirmari (F3 4.6 pasul 1): numarul lotului din seria Finance.BalanceConfirmation, o confirmare pe fiecare partener si rol
/// cu soldul in lei la data, in valoare absoluta, de cel putin prag (optional doar partenerii alesi), si PDF-ul ei cu partidele deschise la data,
/// atasat confirmarii. Intoarce lotul.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BalanceConfirmationManage)]
[Audited("Fin.BalanceConfirmation.Generate")]
public sealed record GenerateBalanceConfirmations(DateOnly AsOf, PartnerRole? Role, string MinBalance, IReadOnlyList<int>? PartnerIds) : ICommand<int>;

internal sealed class GenerateBalanceConfirmationsValidator : AbstractValidator<GenerateBalanceConfirmations>
{
    public GenerateBalanceConfirmationsValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.MinBalance).Must(v => Money.TryParse(v, out var m) && m >= 0).WithMessage("Pragul de sold este o sumă pozitivă.");
        RuleFor(x => x.PartnerIds).Must(p => p is null || p.Count <= 1000).WithMessage("Cel mult 1000 de parteneri într-un lot.");
    }
}

internal sealed class GenerateBalanceConfirmationsHandler(IBalanceConfirmationStore store, IBalanceConfirmationPrinter printer, IAttachmentStore attachments,
    INumberSeriesService numbers, ICompanyDirectory companies, ICurrentContext context, TimeProvider time, IAuditScope audit)
    : ICommandHandler<GenerateBalanceConfirmations, int>
{
    public async Task<int> Handle(GenerateBalanceConfirmations request, CancellationToken cancellationToken)
    {
        if (request.AsOf > BusinessDay.Today(time))
        {
            throw new ErpValidationException(new Dictionary<string, string[]> { ["asOf"] = ["Data soldului nu poate fi în viitor."] });
        }

        var number = (await numbers.NextAsync(context.CompanyId, ConfirmationRules.DocumentType, request.AsOf, cancellationToken).ConfigureAwait(false)).Formatted;
        var (batchId, items) = await store.GenerateAsync(context.CompanyId, number, request.AsOf, request.Role, Money.ParseOptional(request.MinBalance) ?? 0,
            request.PartnerIds is { Count: > 0 } ? request.PartnerIds : null, context.UserId, cancellationToken).ConfigureAwait(false);
        var company = await PrintCompensationHandler.CompanyAtAsync(companies, context.CompanyId, request.AsOf, cancellationToken).ConfigureAwait(false);
        foreach (var confirmation in items)
        {
            var lines = await store.OpenItemsAsOfAsync(context.CompanyId, confirmation.PartnerId, confirmation.Role, request.AsOf, cancellationToken)
                .ConfigureAwait(false);
            using var pdf = new MemoryStream(printer.Print(confirmation, lines, company), writable: false);
            var attachment = await attachments.SaveAsync(ConfirmationRules.DocumentType, confirmation.Id,
                $"Confirmare sold {confirmation.Number.Replace('/', '-')}.pdf", "application/pdf", pdf, cancellationToken).ConfigureAwait(false);
            await store.UpdateAsync(context.CompanyId, new ConfirmationUpdate(confirmation.Id) { GeneratedAttachmentId = attachment }, context.UserId,
                cancellationToken).ConfigureAwait(false);
        }

        audit.Describe("Fin.BalanceConfirmationBatch", batchId, new { Number = number, request.AsOf, request.Role, request.MinBalance, Count = items.Count });
        return batchId;
    }
}

// ---------- Trimitere ----------

/// <summary>Rezultatul trimiterii: cate confirmari au plecat in outbox si care nu au adresa de email (raman de trimis).</summary>
public sealed record BalanceConfirmationsSent(int Sent, IReadOnlyList<string> WithoutEmail);

/// <summary>
/// Trimite pe email confirmarile unui lot (F3 4.6 pasul 2), prin outbox (02.01, 4.7), cu PDF-ul atasat: toate cele fara raspuns sau, cu
/// OnlyUnanswered, doar cele deja trimise fara raspuns („Retrimite celor fara raspuns”). Confirmarile cu raspuns nu se retrimit.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BalanceConfirmationManage)]
[Audited("Fin.BalanceConfirmation.Send")]
public sealed record SendBalanceConfirmations(int BatchId, bool OnlyUnanswered, IReadOnlyList<int>? Ids = null) : ICommand<BalanceConfirmationsSent>;

internal sealed class SendBalanceConfirmationsHandler(IBalanceConfirmationStore store, IEmailOutbox email, ICompanyDirectory companies, ICurrentContext context,
    IAuditScope audit) : ICommandHandler<SendBalanceConfirmations, BalanceConfirmationsSent>
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    public async Task<BalanceConfirmationsSent> Handle(SendBalanceConfirmations request, CancellationToken cancellationToken)
    {
        var items = await store.GetAsync(context.CompanyId, null, request.BatchId, cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            throw ConfirmationRules.NotFound();
        }

        var targets = items.Where(c => c.Status is ConfirmationStatus.Generated or ConfirmationStatus.Sent or ConfirmationStatus.NoAnswer
                && (!request.OnlyUnanswered || c.Status != ConfirmationStatus.Generated) && (request.Ids is null || request.Ids.Contains(c.Id)))
            .ToList();
        var company = await PrintCompensationHandler.CompanyAtAsync(companies, context.CompanyId, items[0].AsOfDate, cancellationToken).ConfigureAwait(false);
        var withoutEmail = new List<string>();
        var sent = 0;
        foreach (var c in targets)
        {
            if (string.IsNullOrWhiteSpace(c.Email) || c.GeneratedAttachmentId is null)
            {
                withoutEmail.Add(c.PartnerName);
                continue;
            }

            var date = c.AsOfDate.ToString("dd.MM.yyyy", Romanian);
            var body = $"""
                Stimați parteneri,

                Vă transmitem atașat confirmarea de sold nr. {c.Number}, cu partidele deschise din evidențele {company.Name} la data de {date}.
                Vă rugăm să ne răspundeți, confirmând soldul sau indicând soldul din evidențele dumneavoastră și documentele care explică diferența.

                Cu stimă,
                {company.Name}
                """;
            var messageId = await email.EnqueueAsync(new EmailRequest(c.Email.Trim(), null, $"Confirmare de sold {c.Number} la {date} – {company.Name}", body, false,
                [c.GeneratedAttachmentId.Value], ConfirmationRules.DocumentType, c.Id), cancellationToken).ConfigureAwait(false);
            await store.UpdateAsync(context.CompanyId, new ConfirmationUpdate(c.Id) { SentTo = c.Email.Trim(), EmailMessageId = messageId }, context.UserId,
                cancellationToken).ConfigureAwait(false);
            sent++;
        }

        if (sent > 0)
        {
            await store.MarkBatchSentAsync(context.CompanyId, request.BatchId, cancellationToken).ConfigureAwait(false);
        }

        audit.Describe("Fin.BalanceConfirmationBatch", request.BatchId, new { request.OnlyUnanswered, Sent = sent, WithoutEmail = withoutEmail.Count });
        return new BalanceConfirmationsSent(sent, withoutEmail);
    }
}

// ---------- Raspuns ----------

/// <summary>
/// Inregistreaza raspunsul partenerului (F3 4.6 pasul 2): confirmat (soldul lui e al nostru), contestat (soldul declarat si explicatia diferentei,
/// obligatorii) sau fara raspuns, cu fisierul primit (optional, regulile atasamentelor SEC-05) si marcajul de diferenta lamurita.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BalanceConfirmationManage)]
[Audited("Fin.BalanceConfirmation.Response")]
public sealed record RecordBalanceConfirmationResponse(int Id, ConfirmationStatus Status, DateOnly? ResponseDate, string? PartnerBalance, string? Note,
    bool IsResolved, string? FileName, byte[]? Content, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class RecordBalanceConfirmationResponseValidator : AbstractValidator<RecordBalanceConfirmationResponse>
{
    public RecordBalanceConfirmationResponseValidator()
    {
        RuleFor(x => x.Status).Must(s => s is ConfirmationStatus.Confirmed or ConfirmationStatus.Contested or ConfirmationStatus.NoAnswer)
            .WithMessage("Răspunsul este Confirmat, Contestat sau Fără răspuns.");
        RuleFor(x => x.ResponseDate).NotNull().When(x => x.Status != ConfirmationStatus.NoAnswer).WithMessage("Data răspunsului este obligatorie.");
        RuleFor(x => x.PartnerBalance).Must(v => Money.TryParse(v, out _)).When(x => x.Status == ConfirmationStatus.Contested)
            .WithMessage("Soldul declarat de partener este obligatoriu la contestare.");
        RuleFor(x => x.Note).NotEmpty().When(x => x.Status == ConfirmationStatus.Contested)
            .WithMessage("Explicați diferența (partidele lipsă la una din părți).");
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.FileName).NotEmpty().When(x => x.Content is { Length: > 0 }).WithMessage("Lipsește numele fișierului.");
        RuleFor(x => x.RowVersion).NotEmpty().WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class RecordBalanceConfirmationResponseHandler(IBalanceConfirmationStore store, IAttachmentStore attachments, ICurrentContext context,
    IAuditScope audit) : ICommandHandler<RecordBalanceConfirmationResponse, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(RecordBalanceConfirmationResponse request, CancellationToken cancellationToken)
    {
        var current = (await store.GetAsync(context.CompanyId, request.Id, null, cancellationToken).ConfigureAwait(false)).SingleOrDefault()
            ?? throw ConfirmationRules.NotFound();
        int? attachment = null;
        if (request.Content is { Length: > 0 })
        {
            using var content = new MemoryStream(request.Content, writable: false);
            attachment = await attachments.SaveAsync(ConfirmationRules.DocumentType, request.Id, request.FileName!, "application/octet-stream", content,
                cancellationToken).ConfigureAwait(false);
        }

        var partnerBalance = request.Status switch
        {
            ConfirmationStatus.Confirmed => Money.ParseOptional(current.OurBalance),
            ConfirmationStatus.Contested => Money.ParseOptional(request.PartnerBalance),
            _ => null,
        };
        var version = await store.UpdateAsync(context.CompanyId, new ConfirmationUpdate(request.Id)
        {
            Status = request.Status, ResponseDate = request.Status == ConfirmationStatus.NoAnswer ? null : request.ResponseDate, PartnerBalance = partnerBalance,
            ResponseNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), ResponseAttachmentId = attachment,
            IsResolved = request.Status == ConfirmationStatus.Confirmed || request.IsResolved, RowVersion = request.RowVersion,
        }, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.BalanceConfirmation", request.Id, new { request.Status, PartnerBalance = partnerBalance, AttachmentId = attachment, request.IsResolved });
        return new FinanceSaved(request.Id, version);
    }
}
