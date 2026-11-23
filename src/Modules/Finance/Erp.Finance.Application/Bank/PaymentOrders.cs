using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Bank;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Domain.Treasury;
using FluentValidation;

namespace Erp.Finance.Application.Bank;

public sealed record PaymentOrderLineInput(int OperationTypeId, string Amount, string? SettlementRef, int? CostCenterId, int? ProjectId);

public sealed record PaymentOrderLineRecord(short LineNumber, int OperationTypeId, string? SettlementRef, decimal Amount, int? CostCenterId, int? ProjectId);

public sealed record PaymentOrderDraft(int? Id, int TreasuryAccountId, DateOnly DocumentDate, DateOnly RequestedExecutionDate, bool IsUrgent,
    PaymentOrderOrigin Origin, PaymentPurpose Purpose, int? PartnerId, int? EmployeeId, int? PartnerBankAccountId, string? BeneficiaryName,
    string? BeneficiaryIban, string? BeneficiaryBic, string? BeneficiaryFiscalCode, bool AllowFreeIban, string? RemittanceInfo, string? BudgetObligationRef,
    int? SourceProposalId, IReadOnlyList<PaymentOrderLineRecord> Lines, byte[]? RowVersion);

public sealed record PaymentOrderVersion(int Id, byte[] RowVersion);

public sealed record PaymentOrderLineDto(long Id, short LineNumber, int OperationTypeId, string OperationTypeCode, string OperationTypeName, string? SettlementRef,
    string Amount, int? CostCenterId, int? ProjectId);

public sealed record PaymentOrderDetailDto(int Id, int TreasuryAccountId, string AccountCode, string? Number, DateOnly DocumentDate, DateOnly RequestedExecutionDate,
    bool IsUrgent, PaymentOrderOrigin Origin, PaymentPurpose Purpose, int? PartnerId, string? PartnerName, bool PartnerAnafInactive, int? EmployeeId,
    int? PartnerBankAccountId, string BeneficiaryName, string BeneficiaryIban, string? BeneficiaryBic, string? BeneficiaryFiscalCode, bool IsFreeIban,
    string CurrencyCode, string Amount, string? RemittanceInfo, string? EndToEndId, string? BudgetObligationRef, PaymentOrderStatus Status,
    PaymentConfirmationStatus? ConfirmationStatus, long? BankStatementLineId, int? PaymentBatchId, DateTime? ValidatedAt, string? ValidatedByName,
    DateTime? ApprovedAt, string? ApprovedByName, int? ReversalOfId, int? ReversedById, IReadOnlyList<PaymentOrderLineDto> Lines, byte[] RowVersion);

public sealed record PaymentOrderItemDto(int Id, int TreasuryAccountId, string AccountCode, string? Number, DateOnly DocumentDate, DateOnly RequestedExecutionDate,
    PaymentOrderOrigin Origin, PaymentPurpose Purpose, int? PartnerId, string BeneficiaryName, string BeneficiaryIban, string CurrencyCode, string Amount,
    string? RemittanceInfo, PaymentOrderStatus Status, PaymentConfirmationStatus? ConfirmationStatus, bool IsApproved, int? PaymentBatchId, int? ReversalOfId,
    byte[] RowVersion);

/// <summary>Un OP citit, cu ce trebuie handler-elor: filiala contului (seria), validatorul si partenerul inactiv la ANAF.</summary>
public sealed record PaymentOrderState(PaymentOrderDetailDto Order, int? BranchId, int? ValidatedBy, IReadOnlyList<PaymentOrderLineRecord> Lines);

/// <summary>Accesul la date pentru ordinele de plata (fin.usp_PaymentOrder_*, NET-06).</summary>
public interface IPaymentOrderStore
{
    Task<FinanceSaved> SaveAsync(int companyId, PaymentOrderDraft draft, int userId, CancellationToken ct);

    Task DeleteAsync(int companyId, int id, byte[] rowVersion, CancellationToken ct);

    Task<PaymentOrderState?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<PaymentOrderItemDto>> ListAsync(int companyId, int? treasuryAccountId, PaymentOrderStatus? status, bool unconfirmed, DateOnly? from,
        DateOnly? to, CancellationToken ct);

    Task<byte[]> ValidateAsync(int companyId, int id, string number, bool anafInactiveConfirmed, byte[] rowVersion, int userId, CancellationToken ct);

    Task<IReadOnlyList<PaymentOrderVersion>> ApproveAsync(int companyId, IReadOnlyList<PaymentOrderVersion> orders, bool approve, bool requireApproval,
        int userId, CancellationToken ct);

    Task<FinanceSaved> ReverseAsync(int companyId, int id, string number, DateOnly reversalDate, byte[] rowVersion, int userId, CancellationToken ct);
}

/// <summary>Setarile Finantelor pe firma (F2: patru ochi, alerte, validare automata), cu valorile implicite din specificatie.</summary>
public interface IFinanceSettings
{
    /// <summary>Fin.PaymentOrder.RequireApproval: aprobarea cu patru ochi, pornita implicit (decizia din 15.11.2026).</summary>
    Task<bool> RequireApprovalAsync(int companyId, CancellationToken ct);

    /// <summary>
    /// Fin.Bank.AutoValidateLevel: 0 niciodata, 1 confirmarile de OP, perechile interne, cardurile si regulile cu validare automata, 2 si
    /// incasarile potrivite exact pe factura (implicit 2, decizia din 15.11.2026).
    /// </summary>
    Task<int> AutoValidateLevelAsync(int companyId, CancellationToken ct);

    /// <summary>Fin.Card.FeeTolerancePercent: comisionul maxim acceptat la potrivirea automata a unei decontari de card (procent din brut).</summary>
    Task<decimal> CardFeeTolerancePercentAsync(int companyId, CancellationToken ct);

    /// <summary>Fin.Card.SettlementDays: zilele lucratoare pana la decontarea asteptata (implicit 2).</summary>
    Task<int> CardSettlementDaysAsync(int companyId, CancellationToken ct);

    /// <summary>
    /// Fin.Payment.RequireSpvInvoice (F3 3.8): plata unei facturi de furnizor neprimite in SPV: 0 fara verificare, 1 blocare, 2 avertizare
    /// care se confirma (implicit, decizia din 15.11.2026).
    /// </summary>
    Task<int> RequireSpvInvoiceAsync(int companyId, CancellationToken ct);

    /// <summary>Salveaza o setare a firmei (codul e unul din cele de mai sus, valoarea invarianta).</summary>
    Task SaveAsync(int companyId, string code, string value, int userId, CancellationToken ct);
}

internal static class PaymentOrderErrors
{
    public static ErpException NotFound() => ErpException.NotFound("FIN_PAYMENT_ORDER_NOT_FOUND", "Ordinul de plată nu există.");

    public static PaymentOrderLineRecord[] Lines(IEnumerable<PaymentOrderLineInput> lines) =>
        lines.Select((l, i) => new PaymentOrderLineRecord((short)(i + 1), l.OperationTypeId, string.IsNullOrWhiteSpace(l.SettlementRef) ? null : l.SettlementRef.Trim(),
            Money.ParseOptional(l.Amount) ?? 0, l.CostCenterId, l.ProjectId)).ToArray();
}

internal sealed class PaymentOrderLineInputValidator : AbstractValidator<PaymentOrderLineInput>
{
    public PaymentOrderLineInputValidator()
    {
        RuleFor(l => l.Amount).Must(a => Money.TryParse(a, out var v) && v > 0).WithMessage("Suma liniei este pozitivă, cu cel mult 2 zecimale.");
        RuleFor(l => l.SettlementRef).MaximumLength(100);
    }
}

/// <summary>Salvarea si validarea unui OP, comune ecranului Ordine de plata si „OP nou” din extras.</summary>
public sealed class PaymentOrderWorkflow(IPaymentOrderStore store, INumberSeriesService numbers, IPeriodLock periods, IExchangeRateProvider rates,
    IPrecisionService precision, IOutbox outbox, ICurrentContext context)
{
    /// <summary>
    /// Valideaza un OP ciorna (F2 3.6 regula 2): partenerul inactiv la ANAF cere confirmare (regula 4), perioada documentului deschisa,
    /// numarul din seria OP, apoi PlataInregistrata (cate o intrare pe linie) in aceeasi tranzactie.
    /// </summary>
    public async Task<(PaymentOrderState Order, string Number, byte[] RowVersion)> ValidateAsync(int id, byte[] rowVersion, bool confirmAnafInactive,
        CancellationToken ct)
    {
        var state = await store.GetAsync(context.CompanyId, id, ct).ConfigureAwait(false) ?? throw PaymentOrderErrors.NotFound();
        var order = state.Order;
        if (order.PartnerAnafInactive && !confirmAnafInactive && order.Status == PaymentOrderStatus.Draft)
        {
            throw ErpException.Conflict("FIN_PAYMENT_ORDER_ANAF_INACTIVE_UNCONFIRMED",
                "Partenerul este inactiv fiscal la ANAF; confirmați explicit plata către el.");
        }

        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, order.DocumentDate, ct).ConfigureAwait(false);
        var number = (await numbers.NextAsync(context.CompanyId, PaymentOrderRules.DocumentType, order.DocumentDate, ct, state.BranchId).ConfigureAwait(false))
            .Formatted;
        var version = await store.ValidateAsync(context.CompanyId, id, number, order.PartnerAnafInactive && confirmAnafInactive, rowVersion, context.UserId, ct)
            .ConfigureAwait(false);

        var rate = await rates.GetRateAsync(order.CurrencyCode, order.DocumentDate, ct).ConfigureAwait(false);
        var policy = await precision.ResolveAsync(context.CompanyId, null, null, ct).ConfigureAwait(false);
        var amounts = state.Lines.Select(l => l.Amount).ToList();
        var (ron, totalRon) = CashDocumentRules.ToRon(amounts, rate.PerUnit, v => precision.RoundAmount(v, policy));
        var codes = order.Lines.ToDictionary(l => l.LineNumber, l => l.OperationTypeCode);
        var lines = state.Lines.Select((l, i) => new TreasuryMovementLine(l.LineNumber, codes[l.LineNumber], l.Amount, ron[i], l.SettlementRef, null)).ToList();
        await outbox.EnqueueAsync(new PlataInregistrata(order.TreasuryAccountId, TreasuryAccountKind.Bank, PaymentOrderRules.DocumentType, order.Id, number,
            order.DocumentDate, order.PartnerId, order.EmployeeId, order.CurrencyCode, rate.PerUnit, amounts.Sum(), totalRon, lines)
        {
            CompanyId = context.CompanyId,
        }, ct).ConfigureAwait(false);
        return (state, number, version);
    }
}

/// <summary>Un ordin de plata cu liniile lui (macheta f2-03).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetPaymentOrder(int Id) : IQuery<PaymentOrderDetailDto>;

internal sealed class GetPaymentOrderHandler(IPaymentOrderStore store, ICurrentContext context) : IQueryHandler<GetPaymentOrder, PaymentOrderDetailDto>
{
    public async Task<PaymentOrderDetailDto> Handle(GetPaymentOrder request, CancellationToken cancellationToken) =>
        (await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw PaymentOrderErrors.NotFound()).Order;
}

/// <summary>Lista ordinelor de plata (macheta f2-03): pe cont, stare, neconfirmate, interval.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetPaymentOrders(int? TreasuryAccountId = null, PaymentOrderStatus? Status = null, bool Unconfirmed = false, DateOnly? From = null,
    DateOnly? To = null) : IQuery<IReadOnlyList<PaymentOrderItemDto>>;

internal sealed class GetPaymentOrdersHandler(IPaymentOrderStore store, ICurrentContext context) : IQueryHandler<GetPaymentOrders, IReadOnlyList<PaymentOrderItemDto>>
{
    public Task<IReadOnlyList<PaymentOrderItemDto>> Handle(GetPaymentOrders request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.TreasuryAccountId, request.Status, request.Unconfirmed, request.From, request.To, cancellationToken);
}

/// <summary>
/// Salveaza un OP ciorna din ecranul Ordine de plata (F2 3.6 si 4.2): beneficiarul din fisa partenerului (contul lui bancar) sau, cu
/// Fin.PaymentOrder.FreeIban, un IBAN tastat (verificat si jurnalizat); detaliile platii generate din facturi daca lipsesc.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderCreate)]
[Audited("Fin.PaymentOrder.Save")]
public sealed record SavePaymentOrder(int? Id, int TreasuryAccountId, DateOnly DocumentDate, DateOnly RequestedExecutionDate, bool IsUrgent, PaymentPurpose Purpose,
    int? PartnerId, int? EmployeeId, int? PartnerBankAccountId, string? BeneficiaryName, string? BeneficiaryIban, string? BeneficiaryBic,
    string? BeneficiaryFiscalCode, string? RemittanceInfo, string? BudgetObligationRef, IReadOnlyList<PaymentOrderLineInput> Lines, byte[]? RowVersion)
    : ICommand<FinanceSaved>;

internal sealed class SavePaymentOrderValidator : AbstractValidator<SavePaymentOrder>
{
    public SavePaymentOrderValidator()
    {
        RuleFor(x => x.Purpose).IsInEnum().WithMessage("Alegeți scopul plății.");
        RuleFor(x => x.RequestedExecutionDate).GreaterThanOrEqualTo(x => x.DocumentDate).WithMessage("Data execuției nu e înaintea datei OP-ului.");
        RuleFor(x => x).Must(x => x.PartnerId is null || x.EmployeeId is null).WithName("partnerId").WithMessage("Beneficiarul e un partener sau un angajat.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Adăugați cel puțin o linie.").Must(l => l.Count <= 200).WithMessage("Un OP are cel mult 200 de linii.");
        RuleForEach(x => x.Lines).SetValidator(new PaymentOrderLineInputValidator());
        RuleFor(x => x.BeneficiaryIban).Must(PaymentOrderRules.IsValidIban).When(x => x.PartnerBankAccountId is null)
            .WithMessage("IBAN-ul beneficiarului nu este valid.");
        RuleFor(x => x.BeneficiaryName).NotEmpty().When(x => x.PartnerBankAccountId is null).WithMessage("Numele beneficiarului este obligatoriu.")
            .MaximumLength(140);
        RuleFor(x => x.RemittanceInfo).MaximumLength(500);
        RuleFor(x => x.BudgetObligationRef).MaximumLength(50);
        RuleFor(x => x.BeneficiaryFiscalCode).MaximumLength(20);
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SavePaymentOrderHandler(IPaymentOrderStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SavePaymentOrder, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SavePaymentOrder request, CancellationToken cancellationToken)
    {
        var lines = PaymentOrderErrors.Lines(request.Lines);
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var freeIban = request.PartnerBankAccountId is null;
        var draft = new PaymentOrderDraft(request.Id, request.TreasuryAccountId, request.DocumentDate, request.RequestedExecutionDate, request.IsUrgent,
            PaymentOrderOrigin.PaymentOrders, request.Purpose, request.PartnerId, request.EmployeeId, request.PartnerBankAccountId, Clean(request.BeneficiaryName),
            freeIban ? BankStatementRules.NormalizeIban(request.BeneficiaryIban) : null, Clean(request.BeneficiaryBic)?.ToUpperInvariant(),
            Clean(request.BeneficiaryFiscalCode), context.HasPermission(FinancePermissions.PaymentOrderFreeIban),
            Clean(request.RemittanceInfo) ?? PaymentOrderRules.Remittance(lines.Select(l => l.SettlementRef), 140), Clean(request.BudgetObligationRef), null, lines,
            request.RowVersion);
        var saved = await store.SaveAsync(context.CompanyId, draft, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PaymentOrder", saved.Id, new
        {
            request.TreasuryAccountId, request.DocumentDate, request.Purpose, request.PartnerId, request.EmployeeId, FreeIban = freeIban,
            Amount = lines.Sum(l => l.Amount), Lines = lines.Select(l => new { l.OperationTypeId, l.SettlementRef, l.Amount }),
        });
        return saved;
    }
}

/// <summary>Sterge un OP ciorna (F2 3.6).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderCreate)]
[Audited("Fin.PaymentOrder.Delete")]
public sealed record DeletePaymentOrder(int Id, byte[] RowVersion) : ICommand<bool>;

internal sealed class DeletePaymentOrderHandler(IPaymentOrderStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<DeletePaymentOrder, bool>
{
    public async Task<bool> Handle(DeletePaymentOrder request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(context.CompanyId, request.Id, request.RowVersion, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PaymentOrder", request.Id, null);
        return true;
    }
}

/// <summary>
/// Valideaza un OP la legarea de obiectul platii (F2 3.6 regula 2, comanda LinkPaymentOrderSettlement din F2 6): liniile acopera suma si au
/// factura (SettlementRef, pana la lista facturilor deschise din F3), partenerul, angajatul sau obligatia (51221, 51216); primeste numar,
/// publica PlataInregistrata si devine Neconfirmat. Partenerul inactiv la ANAF cere ConfirmAnafInactive (auditat).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderValidate)]
[Audited("Fin.PaymentOrder.Validate")]
public sealed record ValidatePaymentOrder(int Id, byte[] RowVersion, bool ConfirmAnafInactive = false) : ICommand<FinanceSaved>;

internal sealed class ValidatePaymentOrderHandler(PaymentOrderWorkflow workflow, IAuditScope audit) : ICommandHandler<ValidatePaymentOrder, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(ValidatePaymentOrder request, CancellationToken cancellationToken)
    {
        var (state, number, version) = await workflow.ValidateAsync(request.Id, request.RowVersion, request.ConfirmAnafInactive, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.PaymentOrder", request.Id, new
        {
            Number = number, state.Order.Amount, state.Order.PartnerId, AnafInactiveConfirmed = state.Order.PartnerAnafInactive && request.ConfirmAnafInactive,
        });
        return new FinanceSaved(request.Id, version);
    }
}

/// <summary>
/// Aproba OP-uri validate (F2 3.6 regula 3): cu patru ochi activ (implicit), cine a validat nu aproba (51213). Aprobarea controleaza ce pleaca
/// la banca, nu efectul platii.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderApprove)]
[Audited("Fin.PaymentOrder.Approve")]
public sealed record ApprovePaymentOrders(IReadOnlyList<PaymentOrderVersion> Orders) : ICommand<IReadOnlyList<PaymentOrderVersion>>;

/// <summary>Retrage aprobarea unor OP-uri (inainte de export).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderApprove)]
[Audited("Fin.PaymentOrder.Unapprove")]
public sealed record UnapprovePaymentOrders(IReadOnlyList<PaymentOrderVersion> Orders) : ICommand<IReadOnlyList<PaymentOrderVersion>>;

internal sealed class PaymentOrdersApprovalValidator : AbstractValidator<ApprovePaymentOrders>
{
    public PaymentOrdersApprovalValidator() =>
        RuleFor(x => x.Orders).NotEmpty().WithMessage("Alegeți ordinele de plată.").Must(o => o.Count <= 1000 && o.Select(x => x.Id).Distinct().Count() == o.Count)
            .WithMessage("Cel mult 1.000 de ordine distincte.");
}

internal sealed class PaymentOrdersUnapprovalValidator : AbstractValidator<UnapprovePaymentOrders>
{
    public PaymentOrdersUnapprovalValidator() =>
        RuleFor(x => x.Orders).NotEmpty().WithMessage("Alegeți ordinele de plată.").Must(o => o.Count <= 1000 && o.Select(x => x.Id).Distinct().Count() == o.Count)
            .WithMessage("Cel mult 1.000 de ordine distincte.");
}

internal sealed class ApprovePaymentOrdersHandler(IPaymentOrderStore store, IFinanceSettings settings, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ApprovePaymentOrders, IReadOnlyList<PaymentOrderVersion>>, ICommandHandler<UnapprovePaymentOrders, IReadOnlyList<PaymentOrderVersion>>
{
    public Task<IReadOnlyList<PaymentOrderVersion>> Handle(ApprovePaymentOrders request, CancellationToken cancellationToken) =>
        RunAsync(request.Orders, true, cancellationToken);

    public Task<IReadOnlyList<PaymentOrderVersion>> Handle(UnapprovePaymentOrders request, CancellationToken cancellationToken) =>
        RunAsync(request.Orders, false, cancellationToken);

    private async Task<IReadOnlyList<PaymentOrderVersion>> RunAsync(IReadOnlyList<PaymentOrderVersion> orders, bool approve, CancellationToken ct)
    {
        var requireApproval = await settings.RequireApprovalAsync(context.CompanyId, ct).ConfigureAwait(false);
        var result = await store.ApproveAsync(context.CompanyId, orders, approve, requireApproval, context.UserId, ct).ConfigureAwait(false);
        audit.Describe("Fin.PaymentOrder", orders.Count == 1 ? orders[0].Id : null, new { Orders = orders.Select(o => o.Id), Approve = approve });
        return result;
    }
}

/// <summary>
/// Storneaza un OP validat (F2 3.6): OP de stornare cu sume negative, la data OP-ului sau azi daca perioada lui e inchisa; originalul devine
/// Stornat, factura redevine deschisa (F3 consuma DocumentTrezorerieStornat). Un OP confirmat pe extras se desface intai acolo (51217).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderReverse)]
[Audited("Fin.PaymentOrder.Reverse")]
public sealed record ReversePaymentOrder(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class ReversePaymentOrderHandler(IPaymentOrderStore store, INumberSeriesService numbers, IPeriodLock periods, IOutbox outbox,
    ICurrentContext context, IAuditScope audit, TimeProvider time) : ICommandHandler<ReversePaymentOrder, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(ReversePaymentOrder request, CancellationToken cancellationToken)
    {
        var state = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw PaymentOrderErrors.NotFound();
        var date = await periods.IsClosedAsync(context.CompanyId, state.Order.DocumentDate, cancellationToken).ConfigureAwait(false)
            ? BusinessDay.Today(time)
            : state.Order.DocumentDate;
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, date, cancellationToken).ConfigureAwait(false);
        var number = (await numbers.NextAsync(context.CompanyId, PaymentOrderRules.DocumentType, date, cancellationToken, state.BranchId).ConfigureAwait(false))
            .Formatted;
        var reversal = await store.ReverseAsync(context.CompanyId, request.Id, number, date, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        await outbox.EnqueueAsync(new DocumentTrezorerieStornat(request.Id, reversal.Id, date)
        {
            CompanyId = context.CompanyId, DocumentType = PaymentOrderRules.DocumentType,
        }, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PaymentOrder", request.Id, new { ReversalId = reversal.Id, Number = number, Date = date, state.Order.Amount });
        return reversal;
    }
}

/// <summary>
/// „OP nou” din linia de extras (F2 4.2a, plata constatata): OP-ul precompletat din linie (contul, data contabila, suma, beneficiarul),
/// legat de obiectul platii, se valideaza si se confirma pe linie in acelasi pas, fara aprobare (plata e deja executata); validarea liniei
/// nu mai publica o plata, ci OrdinPlataConfirmat.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PaymentOrderValidate)]
[Audited("Fin.PaymentOrder.CreateFromStatementLine")]
public sealed record CreatePaymentOrderFromStatementLine(long BankStatementLineId, byte[] LineRowVersion, PaymentPurpose Purpose, int? PartnerId,
    int? EmployeeId, int? PartnerBankAccountId, string? BeneficiaryFiscalCode, string? BudgetObligationRef, IReadOnlyList<PaymentOrderLineInput> Lines,
    bool ConfirmAnafInactive = false) : ICommand<FinanceSaved>;

internal sealed class CreatePaymentOrderFromStatementLineValidator : AbstractValidator<CreatePaymentOrderFromStatementLine>
{
    public CreatePaymentOrderFromStatementLineValidator()
    {
        RuleFor(x => x.Purpose).IsInEnum().WithMessage("Alegeți scopul plății.");
        RuleFor(x => x.LineRowVersion).NotEmpty().WithMessage("Lipsește versiunea liniei.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Adăugați cel puțin o linie.").Must(l => l.Count <= 200).WithMessage("Un OP are cel mult 200 de linii.");
        RuleForEach(x => x.Lines).SetValidator(new PaymentOrderLineInputValidator());
    }
}

internal sealed class CreatePaymentOrderFromStatementLineHandler(IPaymentOrderStore store, IBankStatementLineStore lines, PaymentOrderWorkflow workflow,
    BankLineValidation validation, ICurrentContext context, IAuditScope audit) : ICommandHandler<CreatePaymentOrderFromStatementLine, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(CreatePaymentOrderFromStatementLine request, CancellationToken cancellationToken)
    {
        if (!context.HasPermission(FinancePermissions.PaymentOrderCreate) || !context.HasPermission(FinancePermissions.BankStatementValidate))
        {
            throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, "„OP nou” din extras cere crearea OP-ului și validarea liniilor de extras.");
        }

        var (found, _) = await lines.GetAsync(context.CompanyId, [request.BankStatementLineId], cancellationToken).ConfigureAwait(false);
        var line = found.SingleOrDefault() ?? throw BankLineRules.NotFound();
        if (line.Amount >= 0 || line.MatchStatus == BankLineMatchStatus.Validated)
        {
            throw ErpException.Conflict("FIN_BANK_LINE_NOT_A_PAYMENT", "„OP nou” se face doar dintr-o linie de ieșire nevalidată.");
        }

        var orderLines = PaymentOrderErrors.Lines(request.Lines);
        if (orderLines.Sum(l => l.Amount) != -line.Amount)
        {
            throw new ErpException(ErpErrorKind.Validation, "FIN_BANK_LINE_PARTS_MISMATCH", "Suma liniilor OP-ului trebuie să fie egală cu suma liniei de extras.");
        }

        // Beneficiarul: contul bancar al partenerului sau, altfel, numele si IBAN-ul din extras (plata e deja facuta, nu e IBAN tastat).
        var draft = new PaymentOrderDraft(null, line.TreasuryAccountId, line.BookingDate, line.BookingDate, false, PaymentOrderOrigin.BankStatement,
            request.Purpose, request.PartnerId, request.EmployeeId, request.PartnerBankAccountId, line.CounterpartyName ?? line.PartnerName ?? "Beneficiar",
            line.CounterpartyIban ?? "-", null, request.BeneficiaryFiscalCode?.Trim(), true,
            PaymentOrderRules.Remittance(orderLines.Select(l => l.SettlementRef), 500) ?? line.RemittanceInfo, request.BudgetObligationRef?.Trim(), null,
            orderLines, null);
        var saved = await store.SaveAsync(context.CompanyId, draft, context.UserId, cancellationToken).ConfigureAwait(false);
        var (state, number, version) = await workflow.ValidateAsync(saved.Id, saved.RowVersion, request.ConfirmAnafInactive, cancellationToken).ConfigureAwait(false);

        var codes = state.Order.Lines.ToDictionary(l => l.LineNumber, l => l.OperationTypeId);
        var parts = orderLines.Select(l => new BankLinePartRecord(l.LineNumber, codes[l.LineNumber], -l.Amount, request.PartnerId, request.EmployeeId,
            l.SettlementRef, null, null, request.BudgetObligationRef, l.CostCenterId, l.ProjectId, null) { PaymentOrderId = saved.Id }).ToList();
        var lineVersion = await lines.ExplainAsync(context.CompanyId, line.Id, parts, request.LineRowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        await validation.ValidateAsync([new BankLineVersion(line.Id, lineVersion)], cancellationToken).ConfigureAwait(false);

        audit.Describe("Fin.PaymentOrder", saved.Id, new { Number = number, line.Id, Amount = -line.Amount, request.PartnerId });
        return new FinanceSaved(saved.Id, version);
    }
}
