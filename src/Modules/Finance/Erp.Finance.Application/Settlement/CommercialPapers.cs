using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;
using FluentValidation;

namespace Erp.Finance.Application.Settlement;

public enum PaperDirection : byte
{
    Received = 1,
    Issued = 2,
}

public enum PaperKind : byte
{
    Cheque = 1,
    PromissoryNote = 2,
}

public enum PaperStatus : byte
{
    InPortfolio = 1,
    Deposited = 2,
    Collected = 3,
    Refused = 4,
    Endorsed = 5,
    Reversed = 6,
}

public sealed record CommercialPaperItemDto(int Id, int PartnerId, string PartnerName, PaperDirection Direction, PaperKind Kind, string Series, string Number,
    string Amount, string CurrencyCode, DateOnly IssueDate, DateOnly MaturityDate, PaperStatus Status, string? TreasuryAccountCode);

public sealed record CommercialPaperTotalsDto(string InPortfolio, int InPortfolioCount, string Deposited, int DepositedCount, string IssuedToPay, int IssuedToPayCount,
    string Refused, int RefusedCount);

public sealed record CommercialPaperListDto(CommercialPaperTotalsDto Totals, IReadOnlyList<CommercialPaperItemDto> Items);

public sealed record CommercialPaperEventDto(PaperStatus Status, DateOnly EventDate, string? TreasuryAccountCode, long? BankStatementLineId, string? Note,
    DateTime CreatedAt, string? CreatedByName);

public sealed record CommercialPaperInvoiceDto(long OpenItemId, string DocumentNo, DateOnly DocumentDate, string Amount, bool IsActive);

public sealed record CommercialPaperDetailDto(int Id, int PartnerId, string PartnerName, PaperDirection Direction, PaperKind Kind, string Series, string Number,
    string? IssuerBank, string? IssuerAccount, string Amount, string CurrencyCode, DateOnly IssueDate, DateOnly MaturityDate, int? TreasuryAccountId,
    string? TreasuryAccountCode, PaperStatus Status, string? GlAccountCode, long? OpenItemId, int? EndorsedToPartnerId, string? EndorsedToPartnerName, string? Note,
    IReadOnlyList<CommercialPaperEventDto> Events, IReadOnlyList<CommercialPaperInvoiceDto> Invoices, byte[] RowVersion);

public sealed record CommercialPaperRecord(int PartnerId, PaperDirection Direction, PaperKind Kind, string Series, string Number, string? IssuerBank,
    string? IssuerAccount, decimal Amount, string CurrencyCode, decimal ExchangeRate, DateOnly IssueDate, DateOnly MaturityDate, string? Note);

public sealed record PaperStatusChange(int Id, PaperStatus Status, DateOnly EventDate, int? TreasuryAccountId, long? BankStatementLineId, int? EndorsedToPartnerId,
    string? Note, byte[] RowVersion);

/// <summary>Accesul la date pentru efectele comerciale (fin.usp_CommercialPaper_*, NET-06).</summary>
public interface ICommercialPaperStore
{
    Task<(int Id, long OpenItemId)> RegisterAsync(int companyId, CommercialPaperRecord paper, int userId, CancellationToken ct);

    Task<(PaperStatus Previous, PaperDirection Direction, long OpenItemId, byte[] RowVersion)> ChangeStatusAsync(int companyId, PaperStatusChange change, int userId,
        CancellationToken ct);

    Task<CommercialPaperDetailDto?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<CommercialPaperListDto> ListAsync(int companyId, PaperDirection? direction, bool activeOnly, DateOnly? maturityTo, int? year, CancellationToken ct);
}

internal static class CommercialPaperRules
{
    public const string DocumentType = "Finance.CommercialPaper";

    public static ErpException NotFound() => ErpException.NotFound("FIN_COMMERCIAL_PAPER_NOT_FOUND", "Efectul comercial nu există.");
}

// ---------- Portofoliul si fisa ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetCommercialPapers(PaperDirection? Direction = null, bool ActiveOnly = false, DateOnly? MaturityTo = null, int? Year = null)
    : IQuery<CommercialPaperListDto>;

internal sealed class GetCommercialPapersHandler(ICommercialPaperStore store, ICurrentContext context) : IQueryHandler<GetCommercialPapers, CommercialPaperListDto>
{
    public Task<CommercialPaperListDto> Handle(GetCommercialPapers request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.Direction, request.ActiveOnly, request.MaturityTo, request.Year, cancellationToken);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetCommercialPaper(int Id) : IQuery<CommercialPaperDetailDto>;

internal sealed class GetCommercialPaperHandler(ICommercialPaperStore store, ICurrentContext context) : IQueryHandler<GetCommercialPaper, CommercialPaperDetailDto>
{
    public async Task<CommercialPaperDetailDto> Handle(GetCommercialPaper request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CommercialPaperRules.NotFound();
}

// ---------- Inregistrarea ----------

/// <summary>
/// Inregistreaza un CEC sau un bilet la ordin primit de la un client sau emis catre un furnizor (F3 3.5, 4.5 pasul 1): partida efectului si
/// alocarile pe facturile alese (Source = 5), in ordinea data, pana la suma efectului; ce ramane, ramane pe efect. EfectComercialModificat.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CommercialPaperManage)]
[Audited("Fin.CommercialPaper.Register")]
public sealed record RegisterCommercialPaper(int PartnerId, PaperDirection Direction, PaperKind Kind, string Series, string Number, string? IssuerBank,
    string? IssuerAccount, string Amount, string CurrencyCode, DateOnly IssueDate, DateOnly MaturityDate, string? Note, IReadOnlyList<long> InvoiceIds)
    : ICommand<FinanceSaved>;

internal sealed class RegisterCommercialPaperValidator : AbstractValidator<RegisterCommercialPaper>
{
    public RegisterCommercialPaperValidator()
    {
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Series).NotEmpty().WithMessage("Seria instrumentului este obligatorie.").MaximumLength(20);
        RuleFor(x => x.Number).NotEmpty().WithMessage("Numărul instrumentului este obligatoriu.").MaximumLength(30);
        RuleFor(x => x.IssuerBank).MaximumLength(100);
        RuleFor(x => x.IssuerAccount).MaximumLength(34);
        RuleFor(x => x.Amount).Must(a => Money.TryParse(a, out var v) && v > 0).WithMessage("Suma efectului trebuie să fie pozitivă.");
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.MaturityDate).GreaterThanOrEqualTo(x => x.IssueDate).WithMessage("Scadența nu poate fi înaintea emiterii.");
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.InvoiceIds).Must(i => i.Count <= 100 && i.Distinct().Count() == i.Count).WithMessage("Fiecare factură apare o singură dată.");
    }
}

internal sealed class RegisterCommercialPaperHandler(ICommercialPaperStore store, ISettlementStore settlementStore, SettlementService settlement,
    IExchangeRateProvider rates, IPeriodLock periods, IOutbox outbox, ICurrentContext context, IAuditScope audit) : ICommandHandler<RegisterCommercialPaper, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(RegisterCommercialPaper request, CancellationToken cancellationToken)
    {
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, request.IssueDate, cancellationToken).ConfigureAwait(false);
        var currency = request.CurrencyCode.ToUpperInvariant();
        var rate = currency == "RON" ? 1m : (await rates.GetRateAsync(currency, request.IssueDate, cancellationToken).ConfigureAwait(false)).PerUnit;
        var amount = Money.ParseOptional(request.Amount) ?? 0;
        var (id, itemId) = await store.RegisterAsync(context.CompanyId, new CommercialPaperRecord(request.PartnerId, request.Direction, request.Kind,
            request.Series.Trim(), request.Number.Trim(), Clean(request.IssuerBank), Clean(request.IssuerAccount)?.Replace(" ", "", StringComparison.Ordinal), amount,
            currency, rate, request.IssueDate, request.MaturityDate, Clean(request.Note)), context.UserId, cancellationToken).ConfigureAwait(false);

        if (request.InvoiceIds.Count > 0)
        {
            var role = request.Direction == PaperDirection.Received ? PartnerRole.Customer : PartnerRole.Supplier;
            var invoices = await settlementStore.CandidatesAsync(context.CompanyId, request.PartnerId, role, currency, 1, request.InvoiceIds, cancellationToken)
                .ConfigureAwait(false);
            var ordered = request.InvoiceIds.Select(i => invoices.SingleOrDefault(c => c.Id == i)).ToList();
            var wrongSide = role == PartnerRole.Customer ? (Func<OpenItemCandidate, bool>)(c => c.Amount <= 0) : c => c.Amount >= 0;
            if (ordered.Any(c => c is null || c.OpenAmount == 0 || c.CurrencyCode != currency || wrongSide(c)))
            {
                throw SettlementErrors.Invalid("invoiceIds", "O factură aleasă nu este o factură deschisă a partenerului, în moneda efectului.");
            }

            var plan = SettlementRules.Distribute(amount, ordered.Select(c => new AllocationCandidate(c!.Id, Math.Abs(c.OpenAmount))));
            await settlement.AllocateAsync(context.CompanyId, plan.Select(p => role == PartnerRole.Customer
                ? new AllocationRequest(p.Id, itemId, p.Amount, request.IssueDate, AllocationSource.CommercialPaper) { CommercialPaperId = id }
                : new AllocationRequest(itemId, p.Id, p.Amount, request.IssueDate, AllocationSource.CommercialPaper) { CommercialPaperId = id }).ToList(),
                cancellationToken).ConfigureAwait(false);
        }

        await outbox.EnqueueAsync(new EfectComercialModificat(id, (byte)request.Direction, (byte)request.Kind, (byte)PaperStatus.InPortfolio, amount, request.PartnerId,
            request.IssueDate) { CompanyId = context.CompanyId }, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CommercialPaper", id, new { request.Direction, request.Kind, request.Series, request.Number, Amount = amount, request.InvoiceIds });
        var saved = await store.GetAsync(context.CompanyId, id, cancellationToken).ConfigureAwait(false) ?? throw CommercialPaperRules.NotFound();
        return new FinanceSaved(id, saved.RowVersion);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// ---------- Schimbarea starii ----------

/// <summary>
/// Schimba starea efectului (F3 4.5): depus la banca (cu contul), incasat / platit, refuzat (alocarile lui se desfac, facturile redevin
/// deschise si restante, partida efectului se anuleaza), girat catre un furnizor (plata furnizorului, pe facturile lui alese), stornat (ca
/// refuzul). Perioada datei trebuie sa fie deschisa; EfectComercialModificat.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CommercialPaperManage)]
[Audited("Fin.CommercialPaper.ChangeStatus")]
public sealed record ChangeCommercialPaperStatus(int Id, PaperStatus Status, DateOnly? EventDate, int? TreasuryAccountId, long? BankStatementLineId,
    int? EndorsedToPartnerId, IReadOnlyList<long>? EndorsedInvoiceIds, string? Note, byte[] RowVersion, bool ConfirmWarnings = false) : ICommand<FinanceSaved>;

internal sealed class ChangeCommercialPaperStatusValidator : AbstractValidator<ChangeCommercialPaperStatus>
{
    public ChangeCommercialPaperStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum().Must(s => s != PaperStatus.InPortfolio).WithMessage("Efectul nu se readuce în portofoliu.");
        RuleFor(x => x.Note).NotEmpty().When(x => x.Status is PaperStatus.Refused or PaperStatus.Reversed).WithMessage("Motivul este obligatoriu.").MaximumLength(500);
        RuleFor(x => x.RowVersion).NotNull().WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class ChangeCommercialPaperStatusHandler(ICommercialPaperStore store, SettlementService settlement, IPeriodLock periods, IOutbox outbox,
    ICurrentContext context, TimeProvider time, IAuditScope audit) : ICommandHandler<ChangeCommercialPaperStatus, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(ChangeCommercialPaperStatus request, CancellationToken cancellationToken)
    {
        var paper = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CommercialPaperRules.NotFound();
        var date = request.EventDate ?? BusinessDay.Today(time);
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, date, cancellationToken).ConfigureAwait(false);
        var changed = await store.ChangeStatusAsync(context.CompanyId, new PaperStatusChange(request.Id, request.Status, date, request.TreasuryAccountId,
            request.BankStatementLineId, request.EndorsedToPartnerId, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), request.RowVersion),
            context.UserId, cancellationToken).ConfigureAwait(false);

        if (request.Status is PaperStatus.Refused or PaperStatus.Reversed)
        {
            // Facturile stinse la primire redevin deschise, iar partida efectului se anuleaza (F3 4.5 pasul 4, F3-A10).
            await settlement.ReverseDocumentAsync(context.CompanyId, CommercialPaperRules.DocumentType, request.Id, "", date, cancellationToken).ConfigureAwait(false);
        }
        else if (request.Status == PaperStatus.Endorsed && request.EndorsedToPartnerId is { } supplier)
        {
            // Girul plateste furnizorul cu efectul primit: plata lui, pe facturile alese (apoi FIFO dupa profil).
            var amount = Money.ParseOptional(paper.Amount) ?? 0;
            await settlement.RegisterPaymentAsync(new PaymentSettlementRequest(context.CompanyId, supplier, PartnerRole.Supplier, CommercialPaperRules.DocumentType,
                request.Id, "GIR", $"Gir {paper.Series} {paper.Number}", date, paper.CurrencyCode, 1m, amount, amount, false,
                (request.EndorsedInvoiceIds ?? []).Select(i => new SettlementTarget(i, null)).ToList(), request.ConfirmWarnings), cancellationToken).ConfigureAwait(false);
        }

        await outbox.EnqueueAsync(new EfectComercialModificat(request.Id, (byte)paper.Direction, (byte)paper.Kind, (byte)request.Status,
            Money.ParseOptional(paper.Amount) ?? 0, paper.PartnerId, date) { CompanyId = context.CompanyId }, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CommercialPaper", request.Id, new { From = changed.Previous, To = request.Status, Date = date, request.TreasuryAccountId, request.Note });
        var after = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CommercialPaperRules.NotFound();
        return new FinanceSaved(request.Id, after.RowVersion);
    }
}
