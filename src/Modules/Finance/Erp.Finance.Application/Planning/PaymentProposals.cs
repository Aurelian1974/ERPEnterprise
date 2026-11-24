using System.Text.Json;
using Erp.Core.Contracts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;
using FluentValidation;

namespace Erp.Finance.Application.Planning;

/// <summary>Starea propunerii de plata (F5 3.1).</summary>
public enum ProposalStatus : byte
{
    Draft = 1,
    InApproval = 2,
    Approved = 3,
    Generated = 4,
    Rejected = 5,
}

/// <summary>Starea liniei: 1 propusa, 2 respinsa, 3 generata, 4 sarita la generare.</summary>
public enum ProposalLineStatus : byte
{
    Proposed = 1,
    Rejected = 2,
    Generated = 3,
    Skipped = 4,
}

/// <summary>Decizia unui aprobator (F5 3.2).</summary>
public enum ApprovalDecision : byte
{
    Approve = 1,
    Return = 2,
    Reject = 3,
}

public sealed record PayableCandidate(long OpenItemId, int PartnerId, string PartnerName, string DocumentNo, DateOnly DueDate, string CurrencyCode, decimal OpenAmount,
    decimal ExchangeRate, byte PaymentPriority, int? PreferredTreasuryAccountId, bool AnafInactive, EInvoiceStatus EInvoiceStatus, bool SpvExempt,
    int? PartnerBankAccountId);

public sealed record AccountFunds(int TreasuryAccountId, string Code, string CurrencyCode, decimal BookBalance, decimal OverdraftLimit, decimal AlreadyProposed)
{
    public decimal Available => BookBalance + OverdraftLimit - AlreadyProposed;
}

public sealed record ProposalLineRecord(int LineNumber, long OpenItemId, int PartnerId, DateOnly DueDate, string CurrencyCode, decimal OpenAmount, decimal ProposedAmount,
    decimal ProposedAmountRon, int TreasuryAccountId, byte Priority, IReadOnlyList<string> Warnings, ProposalLineStatus LineStatus, string? RejectReason);

public sealed record ProposalLineDto(long Id, int LineNumber, long OpenItemId, string DocumentNo, int PartnerId, string PartnerName, DateOnly DueDate, string CurrencyCode,
    string OpenAmount, string ProposedAmount, string ProposedAmountRon, int TreasuryAccountId, string TreasuryAccountCode, string TreasuryCurrencyCode, byte Priority,
    IReadOnlyList<string> Warnings, ProposalLineStatus LineStatus, string? RejectReason, int? PaymentOrderId, string? PaymentOrderNumber);

public sealed record ProposalApprovalDto(long Id, int SubmissionNo, int Level, int UserId, string UserName, ApprovalDecision Decision, DateTime DecidedAt,
    string? Comment);

public sealed record ProposalDto(int Id, string Number, string? Name, DateOnly ProposalDate, DateOnly PlannedPaymentDate, DateOnly DueDateUntil,
    int DefaultTreasuryAccountId, string DefaultTreasuryAccountCode, string TotalAmountRon, string MaxLineRon, int? RequiredApprovalLevel, int CurrentApprovalLevel,
    int SubmissionNo, ProposalStatus Status, int? LastApproverId, string? ReturnReason, DateTime? SubmittedAt, DateTime? ApprovedAt, DateTime? GeneratedAt,
    int CreatedBy, string? CreatedByName, IReadOnlyList<ProposalLineDto> Lines, IReadOnlyList<ProposalApprovalDto> Approvals, byte[] RowVersion);

public sealed record ProposalItemDto(int Id, string Number, string? Name, DateOnly ProposalDate, DateOnly PlannedPaymentDate, string TotalAmountRon,
    ProposalStatus Status, int? RequiredApprovalLevel, int CurrentApprovalLevel, string? CreatedByName, int LineCount, int PartnerCount);

public sealed record ApprovalRuleDto(int Level, string ThresholdRon, byte Basis, int ApproverRoleId, string ApproverRoleCode, string ApproverRoleName);

public sealed record RoleOptionDto(int Id, string Code, string Name);

public sealed record ApprovalRulesDto(IReadOnlyList<ApprovalRuleDto> Rules, IReadOnlyList<RoleOptionDto> Roles);

/// <summary>Accesul la date pentru propunerile de plata (fin.usp_PaymentProposal_*, fin.usp_PaymentApprovalRule_*, NET-06).</summary>
public interface IPaymentProposalStore
{
    Task<IReadOnlyList<PayableCandidate>> CandidatesAsync(int companyId, DateOnly dueUntil, IReadOnlyList<int>? partnerIds, IReadOnlyList<string>? currencies,
        byte? maxPriority, int? excludeProposalId, CancellationToken ct);

    Task<IReadOnlyList<AccountFunds>> FundsAsync(int companyId, int? excludeProposalId, CancellationToken ct);

    Task<FinanceSaved> SaveAsync(int companyId, int? id, string? name, DateOnly proposalDate, DateOnly plannedPaymentDate, DateOnly dueDateUntil, int defaultAccountId,
        IReadOnlyList<ProposalLineRecord> lines, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<ProposalDto?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<ProposalItemDto>> ListAsync(int companyId, ProposalStatus? status, int? pendingForUser, CancellationToken ct);

    /// <summary>1 trimite, 2 retrage, 3 sterge; intoarce versiunea noua (null la stergere).</summary>
    Task<byte[]?> TransitionAsync(int companyId, int id, byte action, int? requiredLevel, string? reason, byte[] rowVersion, int userId, CancellationToken ct);

    Task<(ProposalStatus Status, byte[] RowVersion, int? LastApproverId)> DecideAsync(int companyId, int id, ApprovalDecision decision, string? comment,
        IReadOnlyList<long>? rejectedLineIds, byte[] rowVersion, int userId, CancellationToken ct);

    Task<byte[]?> MarkGeneratedAsync(int companyId, int id, IReadOnlyList<(long LineId, int? PaymentOrderId, string? Reason)> lines, int userId, CancellationToken ct);

    Task ApproveOrdersAsync(int companyId, IReadOnlyList<int> paymentOrderIds, int approverId, CancellationToken ct);

    Task<ApprovalRulesDto> RulesAsync(int companyId, CancellationToken ct);

    Task SaveRulesAsync(int companyId, IReadOnlyList<(int Level, decimal ThresholdRon, byte Basis, int ApproverRoleId)> rules, int userId, CancellationToken ct);
}

internal static class ProposalRules
{
    public static ErpException NotFound() => ErpException.NotFound("FIN_PROPOSAL_NOT_FOUND", "Propunerea de plată nu există.");

    /// <summary>
    /// Nivelul de aprobare cerut (F5 2 regula 4): regula cu pragul cel mai mare atins de totalul propunerii (Basis 1) sau de cea mai mare linie
    /// (Basis 2), in lei. Fara reguli active: 51710.
    /// </summary>
    public static int RequiredLevel(IReadOnlyList<ApprovalRuleDto> rules, decimal totalRon, decimal maxLineRon)
    {
        if (rules.Count == 0)
        {
            throw ErpException.Conflict("FIN_PROPOSAL_NO_APPROVAL_RULE", "Firma nu are reguli de aprobare a plăților; configurați-le în Configurare › Finanțe.");
        }

        var reached = rules.Where(r => (r.Basis == 2 ? maxLineRon : totalRon) >= decimal.Parse(r.ThresholdRon, System.Globalization.CultureInfo.InvariantCulture))
            .Select(r => r.Level).DefaultIfEmpty(1).Max();
        return Math.Max(1, reached);
    }

    /// <summary>Avertizarile unei linii (F5 4.1 pasul 5): furnizor inactiv ANAF, factura neprimita in SPV, moneda diferita de a contului, plata partiala.</summary>
    public static List<string> Warnings(PayableCandidate c, string accountCurrency, decimal proposed)
    {
        var warnings = new List<string>();
        if (c.AnafInactive)
        {
            warnings.Add("Furnizor inactiv fiscal la ANAF");
        }

        if (!c.SpvExempt && c.EInvoiceStatus == EInvoiceStatus.NotReceivedFromSpv)
        {
            warnings.Add("Factură neprimită în SPV");
        }

        if (c.CurrencyCode != accountCurrency)
        {
            warnings.Add($"Factură în {c.CurrencyCode}, plătită din cont în {accountCurrency}");
        }

        if (proposed < c.OpenAmount)
        {
            warnings.Add("Plată parțială");
        }

        if (c.PartnerBankAccountId is null)
        {
            warnings.Add($"Furnizorul nu are cont bancar în {c.CurrencyCode}");
        }

        return warnings;
    }
}

// ---------- Lista si fisa ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalView)]
public sealed record GetPaymentProposals(ProposalStatus? Status = null, bool PendingForMe = false) : IQuery<IReadOnlyList<ProposalItemDto>>;

internal sealed class GetPaymentProposalsHandler(IPaymentProposalStore store, ICurrentContext context) : IQueryHandler<GetPaymentProposals, IReadOnlyList<ProposalItemDto>>
{
    public Task<IReadOnlyList<ProposalItemDto>> Handle(GetPaymentProposals request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.Status, request.PendingForMe ? context.UserId : null, cancellationToken);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalView)]
public sealed record GetPaymentProposal(int Id) : IQuery<ProposalDto>;

internal sealed class GetPaymentProposalHandler(IPaymentProposalStore store, ICurrentContext context) : IQueryHandler<GetPaymentProposal, ProposalDto>
{
    public async Task<ProposalDto> Handle(GetPaymentProposal request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw ProposalRules.NotFound();
}

// ---------- Construire si modificare ----------

/// <summary>
/// Construieste o propunere ciorna (F5 4.1): partidele de furnizor scadente pana la data-limita, dupa filtre (furnizori, monede, prioritate,
/// valoare minima in lei), cu excluderile din F3 si din propunerile active; contul sursa: cel preferat al furnizorului sau cel implicit;
/// cu „Limiteaza la bani disponibili”, liniile se bifeaza in ordine pana la soldul scriptic + descoperit − sumele deja propuse, restul raman
/// respinse cu motivul „fonduri insuficiente”.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalCreate)]
[Audited("Fin.PaymentProposal.Build")]
public sealed record BuildPaymentProposal(string? Name, DateOnly PlannedPaymentDate, DateOnly DueDateUntil, int DefaultTreasuryAccountId,
    IReadOnlyList<int>? PartnerIds = null, IReadOnlyList<string>? Currencies = null, byte? MaxPriority = null, string? MinAmountRon = null,
    bool LimitToAvailableFunds = false) : ICommand<FinanceSaved>;

internal sealed class BuildPaymentProposalValidator : AbstractValidator<BuildPaymentProposal>
{
    public BuildPaymentProposalValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.MaxPriority).InclusiveBetween((byte)1, (byte)4).When(x => x.MaxPriority is not null);
        RuleFor(x => x.MinAmountRon).Must(v => Money.TryParse(v, out var m) && m >= 0).When(x => !string.IsNullOrWhiteSpace(x.MinAmountRon))
            .WithMessage("Valoarea minimă este o sumă pozitivă.");
    }
}

internal sealed class BuildPaymentProposalHandler(IPaymentProposalStore store, IExchangeRateProvider rates, ICurrentContext context, TimeProvider time,
    IAuditScope audit) : ICommandHandler<BuildPaymentProposal, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(BuildPaymentProposal request, CancellationToken cancellationToken)
    {
        var today = BusinessDay.Today(time);
        var funds = (await store.FundsAsync(context.CompanyId, null, cancellationToken).ConfigureAwait(false)).ToDictionary(f => f.TreasuryAccountId);
        if (!funds.ContainsKey(request.DefaultTreasuryAccountId))
        {
            throw SettlementErrors.Invalid("defaultTreasuryAccountId", "Contul sursă nu este un cont bancar activ al firmei.");
        }

        var candidates = await store.CandidatesAsync(context.CompanyId, request.DueDateUntil, request.PartnerIds is { Count: > 0 } ? request.PartnerIds : null,
            request.Currencies is { Count: > 0 } ? request.Currencies : null, request.MaxPriority, null, cancellationToken).ConfigureAwait(false);
        var minRon = Money.ParseOptional(request.MinAmountRon) ?? 0;
        var perUnit = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["RON"] = 1m };
        async Task<decimal> Rate(string currency)
        {
            if (!perUnit.TryGetValue(currency, out var value))
            {
                value = (await rates.GetRateAsync(currency, today, cancellationToken).ConfigureAwait(false)).PerUnit;
                perUnit[currency] = value;
            }

            return value;
        }

        var available = funds.ToDictionary(f => f.Key, f => f.Value.Available);
        var lines = new List<ProposalLineRecord>();
        foreach (var c in candidates)
        {
            var ron = Math.Round(c.OpenAmount * await Rate(c.CurrencyCode).ConfigureAwait(false), 2, MidpointRounding.AwayFromZero);
            if (ron < minRon)
            {
                continue;
            }

            var accountId = c.PreferredTreasuryAccountId is { } preferred && funds.ContainsKey(preferred) ? preferred : request.DefaultTreasuryAccountId;
            var account = funds[accountId];
            var status = ProposalLineStatus.Proposed;
            string? reason = null;
            if (request.LimitToAvailableFunds)
            {
                var inAccount = account.CurrencyCode == c.CurrencyCode ? c.OpenAmount
                    : Math.Round(ron / await Rate(account.CurrencyCode).ConfigureAwait(false), 2, MidpointRounding.AwayFromZero);
                if (inAccount <= available[accountId])
                {
                    available[accountId] -= inAccount;
                }
                else
                {
                    (status, reason) = (ProposalLineStatus.Rejected, "Fonduri insuficiente");
                }
            }

            lines.Add(new ProposalLineRecord(lines.Count + 1, c.OpenItemId, c.PartnerId, c.DueDate, c.CurrencyCode, c.OpenAmount, c.OpenAmount, ron, accountId,
                c.PaymentPriority, ProposalRules.Warnings(c, account.CurrencyCode, c.OpenAmount), status, reason));
        }

        var saved = await store.SaveAsync(context.CompanyId, null, string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(), today, request.PlannedPaymentDate,
            request.DueDateUntil, request.DefaultTreasuryAccountId, lines, null, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PaymentProposal", saved.Id, new { request.DueDateUntil, request.PlannedPaymentDate, Lines = lines.Count, request.LimitToAvailableFunds });
        return saved;
    }
}

/// <summary>O linie modificata a unei ciorne: suma propusa (plata partiala), contul sursa si daca ramane in propunere.</summary>
public sealed record ProposalLineInput(long OpenItemId, string ProposedAmount, int TreasuryAccountId, bool Included);

/// <summary>Modifica o propunere ciorna (F5 4.1 pasul 6): numele, datele, contul implicit si liniile existente (suma, contul, bifa).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalCreate)]
[Audited("Fin.PaymentProposal.Save")]
public sealed record SavePaymentProposal(int Id, string? Name, DateOnly PlannedPaymentDate, DateOnly DueDateUntil, int DefaultTreasuryAccountId,
    IReadOnlyList<ProposalLineInput> Lines, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class SavePaymentProposalValidator : AbstractValidator<SavePaymentProposal>
{
    public SavePaymentProposalValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleForEach(x => x.Lines).ChildRules(l => l.RuleFor(x => x.ProposedAmount).Must(a => Money.TryParse(a, out var v) && v > 0)
            .WithMessage("Suma propusă este pozitivă."));
    }
}

internal sealed class SavePaymentProposalHandler(IPaymentProposalStore store, IExchangeRateProvider rates, ICurrentContext context, TimeProvider time,
    IAuditScope audit) : ICommandHandler<SavePaymentProposal, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SavePaymentProposal request, CancellationToken cancellationToken)
    {
        var current = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw ProposalRules.NotFound();
        var funds = (await store.FundsAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false)).ToDictionary(f => f.TreasuryAccountId);
        var inputs = request.Lines.ToDictionary(l => l.OpenItemId);
        var today = BusinessDay.Today(time);
        var lines = new List<ProposalLineRecord>();
        foreach (var l in current.Lines)
        {
            var input = inputs.GetValueOrDefault(l.OpenItemId);
            var open = Money.ParseOptional(l.OpenAmount) ?? 0;
            var amount = input is null ? Money.ParseOptional(l.ProposedAmount) ?? 0 : Money.ParseOptional(input.ProposedAmount) ?? 0;
            var accountId = input?.TreasuryAccountId ?? l.TreasuryAccountId;
            if (!funds.TryGetValue(accountId, out var account))
            {
                throw SettlementErrors.Invalid("lines", "Contul sursă nu este un cont bancar activ al firmei.");
            }

            var perUnit = l.CurrencyCode == "RON" ? 1m : (await rates.GetRateAsync(l.CurrencyCode, today, cancellationToken).ConfigureAwait(false)).PerUnit;
            var status = input is null ? l.LineStatus : input.Included ? ProposalLineStatus.Proposed : ProposalLineStatus.Rejected;
            var candidate = new PayableCandidate(l.OpenItemId, l.PartnerId, l.PartnerName, l.DocumentNo, l.DueDate, l.CurrencyCode, open, perUnit, l.Priority, null,
                l.Warnings.Contains("Furnizor inactiv fiscal la ANAF"), l.Warnings.Contains("Factură neprimită în SPV") ? EInvoiceStatus.NotReceivedFromSpv
                    : EInvoiceStatus.NotApplicable, false, l.Warnings.Any(w => w.StartsWith("Furnizorul nu are cont", StringComparison.Ordinal)) ? null : 0);
            lines.Add(new ProposalLineRecord(lines.Count + 1, l.OpenItemId, l.PartnerId, l.DueDate, l.CurrencyCode, open, amount,
                Math.Round(amount * perUnit, 2, MidpointRounding.AwayFromZero), accountId, l.Priority, ProposalRules.Warnings(candidate, account.CurrencyCode, amount),
                status, status == ProposalLineStatus.Rejected ? (l.RejectReason ?? "Scoasă din propunere") : null));
        }

        var saved = await store.SaveAsync(context.CompanyId, request.Id, string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(), current.ProposalDate,
            request.PlannedPaymentDate, request.DueDateUntil, request.DefaultTreasuryAccountId, lines, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.PaymentProposal", request.Id, new { Lines = lines.Select(l => new { l.OpenItemId, l.ProposedAmount, l.TreasuryAccountId, l.LineStatus }) });
        return saved;
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalCreate)]
[Audited("Fin.PaymentProposal.Delete")]
public sealed record DeletePaymentProposal(int Id, byte[] RowVersion) : ICommand<bool>;

internal sealed class DeletePaymentProposalHandler(IPaymentProposalStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<DeletePaymentProposal, bool>
{
    public async Task<bool> Handle(DeletePaymentProposal request, CancellationToken cancellationToken)
    {
        await store.TransitionAsync(context.CompanyId, request.Id, 3, null, null, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PaymentProposal", request.Id, null);
        return true;
    }
}

// ---------- Aprobare ----------

/// <summary>Trimite propunerea la aprobare (F5 4.2 pasul 1): nivelul cerut din regulile firmei; aprobarile anterioare nu mai conteaza.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalCreate)]
[Audited("Fin.PaymentProposal.Submit")]
public sealed record SubmitPaymentProposal(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class SubmitPaymentProposalHandler(IPaymentProposalStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SubmitPaymentProposal, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SubmitPaymentProposal request, CancellationToken cancellationToken)
    {
        var current = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw ProposalRules.NotFound();
        var rules = (await store.RulesAsync(context.CompanyId, cancellationToken).ConfigureAwait(false)).Rules;
        var level = ProposalRules.RequiredLevel(rules, Money.ParseOptional(current.TotalAmountRon) ?? 0, Money.ParseOptional(current.MaxLineRon) ?? 0);
        var version = await store.TransitionAsync(context.CompanyId, request.Id, 1, level, null, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.PaymentProposal", request.Id, new { current.Number, RequiredLevel = level, current.TotalAmountRon });
        return new FinanceSaved(request.Id, version!);
    }
}

/// <summary>Retrage propunerea la ciorna (F5 3.1): autorul, din aprobare, sau oricine, dintr-o propunere aprobata inca negenerata; cu motiv.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalCreate)]
[Audited("Fin.PaymentProposal.Withdraw")]
public sealed record WithdrawPaymentProposal(int Id, string Reason, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class WithdrawPaymentProposalValidator : AbstractValidator<WithdrawPaymentProposal>
{
    public WithdrawPaymentProposalValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500).WithMessage("Motivul retragerii este obligatoriu.");
}

internal sealed class WithdrawPaymentProposalHandler(IPaymentProposalStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<WithdrawPaymentProposal, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(WithdrawPaymentProposal request, CancellationToken cancellationToken)
    {
        var version = await store.TransitionAsync(context.CompanyId, request.Id, 2, null, request.Reason.Trim(), request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.PaymentProposal", request.Id, new { Reason = request.Reason.Trim() });
        return new FinanceSaved(request.Id, version!);
    }
}

/// <summary>
/// Decizia unui aprobator (F5 4.2 pasul 2): aprobare (optional cu liniile respinse), returnare la ciorna sau respingere, cu motiv. Dupa ultimul
/// nivel cerut, evenimentul PropunerePlataAprobata.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalApprove)]
[Audited("Fin.PaymentProposal.Decide")]
public sealed record DecidePaymentProposal(int Id, ApprovalDecision Decision, string? Comment, IReadOnlyList<long>? RejectedLineIds, byte[] RowVersion)
    : ICommand<FinanceSaved>;

internal sealed class DecidePaymentProposalValidator : AbstractValidator<DecidePaymentProposal>
{
    public DecidePaymentProposalValidator()
    {
        RuleFor(x => x.Decision).IsInEnum();
        RuleFor(x => x.Comment).NotEmpty().When(x => x.Decision != ApprovalDecision.Approve || x.RejectedLineIds is { Count: > 0 })
            .WithMessage("Motivul este obligatoriu la returnare, respingere sau linii respinse.");
        RuleFor(x => x.Comment).MaximumLength(500);
    }
}

internal sealed class DecidePaymentProposalHandler(IPaymentProposalStore store, IOutbox outbox, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<DecidePaymentProposal, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(DecidePaymentProposal request, CancellationToken cancellationToken)
    {
        var (status, version, _) = await store.DecideAsync(context.CompanyId, request.Id, request.Decision, string.IsNullOrWhiteSpace(request.Comment) ? null
            : request.Comment.Trim(), request.RejectedLineIds, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        if (status == ProposalStatus.Approved)
        {
            var p = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw ProposalRules.NotFound();
            await outbox.EnqueueAsync(new Contracts.Planning.PropunerePlataAprobata(p.Id, p.Number, p.PlannedPaymentDate, Money.ParseOptional(p.TotalAmountRon) ?? 0,
                p.Lines.Count(l => l.LineStatus == ProposalLineStatus.Proposed), context.UserId) { CompanyId = context.CompanyId }, cancellationToken)
                .ConfigureAwait(false);
        }

        audit.Describe("Fin.PaymentProposal", request.Id, new { request.Decision, request.Comment, request.RejectedLineIds, Status = status });
        return new FinanceSaved(request.Id, version);
    }
}

// ---------- Generarea OP-urilor ----------

/// <summary>Rezultatul generarii: OP-urile create si liniile sarite (factura schimbata intre aprobare si generare), cu motivul.</summary>
public sealed record GeneratedOrdersDto(IReadOnlyList<int> PaymentOrderIds, IReadOnlyList<string> Skipped, bool ApprovalSatisfied);

/// <summary>
/// Genereaza OP-urile din propunerea aprobata (F5 4.3): cate un OP pe furnizor, cont sursa si cont al furnizorului, legat de facturi si validat
/// (F3 le stinge in aceeasi tranzactie, cu avertizarile confirmate); liniile ale caror facturi s-au schimbat se sar, cu motiv. OP-urile sunt
/// aprobate pentru patru ochi de ultimul aprobator al propunerii, daca nu el le genereaza (F5 2 regula 5).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalGenerateOrders)]
[Audited("Fin.PaymentProposal.Generate")]
public sealed record GeneratePaymentOrders(int Id) : ICommand<GeneratedOrdersDto>;

internal sealed class GeneratePaymentOrdersHandler(IPaymentProposalStore store, IPaymentOrderStore orders, PaymentOrderWorkflow workflow, ISettlementStore settlement,
    IExchangeRateProvider rates, ICurrentContext context, TimeProvider time, IAuditScope audit) : ICommandHandler<GeneratePaymentOrders, GeneratedOrdersDto>
{
    public async Task<GeneratedOrdersDto> Handle(GeneratePaymentOrders request, CancellationToken cancellationToken)
    {
        var p = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw ProposalRules.NotFound();
        if (p.Status != ProposalStatus.Approved)
        {
            throw ErpException.Conflict("FIN_PROPOSAL_NOT_APPROVED", "Ordinele de plată se generează doar dintr-o propunere aprobată.");
        }

        var today = BusinessDay.Today(time);
        var proposed = p.Lines.Where(l => l.LineStatus == ProposalLineStatus.Proposed).ToList();
        var invoices = (await settlement.InvoicesToPayAsync(context.CompanyId, proposed.Select(l => l.OpenItemId).ToList(), cancellationToken).ConfigureAwait(false))
            .ToDictionary(i => i.OpenItemId);
        var marks = new List<(long LineId, int? PaymentOrderId, string? Reason)>();
        var skipped = new List<string>();
        var ready = new List<(ProposalLineDto Line, InvoiceToPay Invoice)>();
        foreach (var l in proposed)
        {
            var amount = Money.ParseOptional(l.ProposedAmount) ?? 0;
            var reason = !invoices.TryGetValue(l.OpenItemId, out var i) ? "factura nu mai există"
                : i.OpenAmount < amount ? "factura a fost plătită sau stornată între timp"
                : i.IsDisputed ? "factura e în litigiu"
                : i.IsBlockedForPayment || i.PartnerBlocked ? "plata e blocată"
                : i.PartnerBankAccountId is null ? $"furnizorul nu are cont bancar în {i.CurrencyCode}" : null;
            if (reason is null)
            {
                ready.Add((l, i!));
            }
            else
            {
                marks.Add((l.Id, null, reason));
                skipped.Add($"{l.DocumentNo} ({l.PartnerName}): {reason}");
            }
        }

        var type = await orders.OperationTypeIdAsync("PLATA-FURNIZOR", cancellationToken).ConfigureAwait(false);
        var created = new List<int>();
        foreach (var group in ready.GroupBy(r => (r.Line.TreasuryAccountId, r.Line.PartnerId, r.Invoice.PartnerBankAccountId)))
        {
            var lines = new List<PaymentOrderLineRecord>();
            foreach (var (l, _) in group)
            {
                // Suma OP-ului e in moneda contului: aceeasi suma la aceeasi moneda, altfel echivalentul la cursul BNR al zilei (F4 3.3 stinge factura).
                var amount = Money.ParseOptional(l.ProposedAmount) ?? 0;
                if (l.TreasuryCurrencyCode != l.CurrencyCode)
                {
                    var invoiceRate = l.CurrencyCode == "RON" ? 1m : (await rates.GetRateAsync(l.CurrencyCode, today, cancellationToken).ConfigureAwait(false)).PerUnit;
                    var accountRate = l.TreasuryCurrencyCode == "RON" ? 1m
                        : (await rates.GetRateAsync(l.TreasuryCurrencyCode, today, cancellationToken).ConfigureAwait(false)).PerUnit;
                    amount = Math.Round(amount * invoiceRate / accountRate, 2, MidpointRounding.AwayFromZero);
                }

                lines.Add(new PaymentOrderLineRecord((short)(lines.Count + 1), type, TreasurySettlement.Reference([l.OpenItemId]), amount, null, null));
            }

            var draft = new PaymentOrderDraft(null, group.Key.TreasuryAccountId, today, p.PlannedPaymentDate < today ? today : p.PlannedPaymentDate, false,
                PaymentOrderOrigin.Proposal, PaymentPurpose.Suppliers, group.Key.PartnerId, null, group.Key.PartnerBankAccountId, null, null, null, null, false,
                PaymentOrderRules.Remittance(group.Select(r => r.Line.DocumentNo), 140), null, p.Id, lines, null);
            var saved = await orders.SaveAsync(context.CompanyId, draft, context.UserId, cancellationToken).ConfigureAwait(false);
            await workflow.ValidateAsync(saved.Id, saved.RowVersion, confirmAnafInactive: true, cancellationToken, confirmWarnings: true).ConfigureAwait(false);
            created.Add(saved.Id);
            marks.AddRange(group.Select(r => (r.Line.Id, (int?)saved.Id, (string?)null)));
        }

        var approvalSatisfied = p.LastApproverId is { } approver && approver != context.UserId;
        if (approvalSatisfied && created.Count > 0)
        {
            await store.ApproveOrdersAsync(context.CompanyId, created, p.LastApproverId!.Value, cancellationToken).ConfigureAwait(false);
        }

        await store.MarkGeneratedAsync(context.CompanyId, request.Id, marks, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PaymentProposal", request.Id, new { p.Number, Orders = created, Skipped = skipped, ApprovalSatisfied = approvalSatisfied });
        return new GeneratedOrdersDto(created, skipped, approvalSatisfied);
    }
}

// ---------- Regulile de aprobare ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentProposalView)]
public sealed record GetPaymentApprovalRules : IQuery<ApprovalRulesDto>;

internal sealed class GetPaymentApprovalRulesHandler(IPaymentProposalStore store, ICurrentContext context) : IQueryHandler<GetPaymentApprovalRules, ApprovalRulesDto>
{
    public Task<ApprovalRulesDto> Handle(GetPaymentApprovalRules request, CancellationToken cancellationToken) => store.RulesAsync(context.CompanyId, cancellationToken);
}

public sealed record ApprovalRuleInput(int Level, string ThresholdRon, byte Basis, int ApproverRoleId);

/// <summary>Inlocuieste regulile de aprobare ale firmei (F5 3.2; macheta cf-09): niveluri 1..3 consecutive, praguri crescatoare, rolul aprobator.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentApprovalRuleManage)]
[Audited("Fin.PaymentApprovalRule.Save")]
public sealed record SavePaymentApprovalRules(IReadOnlyList<ApprovalRuleInput> Rules) : ICommand<ApprovalRulesDto>;

internal sealed class SavePaymentApprovalRulesValidator : AbstractValidator<SavePaymentApprovalRules>
{
    public SavePaymentApprovalRulesValidator()
    {
        RuleFor(x => x.Rules).NotEmpty().Must(r => r.Count <= 3).WithMessage("Între unul și trei niveluri de aprobare.");
        RuleForEach(x => x.Rules).ChildRules(r =>
        {
            r.RuleFor(x => x.ThresholdRon).Must(v => Money.TryParse(v, out var m) && m >= 0).WithMessage("Pragul este o sumă în lei, pozitivă.");
            r.RuleFor(x => x.Basis).InclusiveBetween((byte)1, (byte)2);
        });
    }
}

internal sealed class SavePaymentApprovalRulesHandler(IPaymentProposalStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SavePaymentApprovalRules, ApprovalRulesDto>
{
    public async Task<ApprovalRulesDto> Handle(SavePaymentApprovalRules request, CancellationToken cancellationToken)
    {
        var rules = request.Rules.Select(r => (r.Level, Money.ParseOptional(r.ThresholdRon) ?? 0, r.Basis, r.ApproverRoleId)).ToList();
        await store.SaveRulesAsync(context.CompanyId, rules, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PaymentApprovalRule", context.CompanyId, request.Rules);
        return await store.RulesAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
    }
}
