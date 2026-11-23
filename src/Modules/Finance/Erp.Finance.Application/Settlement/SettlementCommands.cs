using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;
using FluentValidation;

namespace Erp.Finance.Application.Settlement;

/// <summary>
/// Factura primita prin eveniment devine partida deschisa (F3 4.1), idempotent pe document; apoi storno pe factura stornata si platile
/// nealocate pe factura (FIFO). Apelata de consumatorii FacturaEmisa si FacturaFurnizorValidata, in tranzactia lor.
/// </summary>
public sealed class InvoiceOpenItems(ISettlementStore store, SettlementService settlement, ICurrentContext context)
{
    public Task<long> RegisterAsync(FacturaEmisa evt, CancellationToken ct) =>
        RegisterAsync(CompanyOf(evt), PartnerRole.Customer, "Sales", FacturaEmisa.DocumentType, evt.InvoiceId, evt.PartnerId, evt.Number, evt.DocumentDate,
            evt.AccountingDate, evt.CurrencyCode, evt.ExchangeRate, evt.TotalAmount, evt.TotalAmountRon, evt.VatAmount, evt.VatAmountRon, evt.IsVatOnCollection,
            evt.IsReversal, evt.ReversedInvoiceId, evt.IsAdvanceInvoice, evt.DueDates, evt.CostCenterId, evt.ProjectId, evt.EInvoiceStatus, evt.EInvoiceIndexNo, ct);

    public Task<long> RegisterAsync(FacturaFurnizorValidata evt, CancellationToken ct) =>
        RegisterAsync(CompanyOf(evt), PartnerRole.Supplier, "Purchasing", FacturaFurnizorValidata.DocumentType, evt.InvoiceId, evt.PartnerId, evt.Number,
            evt.DocumentDate, evt.AccountingDate, evt.CurrencyCode, evt.ExchangeRate, evt.TotalAmount, evt.TotalAmountRon, evt.VatAmount, evt.VatAmountRon,
            evt.IsVatOnCollection, evt.IsReversal, evt.ReversedInvoiceId, evt.IsAdvanceInvoice, evt.DueDates, evt.CostCenterId, evt.ProjectId, evt.EInvoiceStatus,
            evt.EInvoiceIndexNo, ct);

    // Firma din anvelopa evenimentului; procesatorul outbox-ului o pune si in context (cand publicatorul nu a completat-o in eveniment).
    private int CompanyOf(IntegrationEvent evt) => evt.CompanyId != 0 ? evt.CompanyId : context.CompanyId;

    private async Task<long> RegisterAsync(int companyId, PartnerRole role, string module, string documentType, long invoiceId, int partnerId, string number,
        DateOnly documentDate, DateOnly accountingDate, string currencyCode, decimal exchangeRate, decimal total, decimal totalRon, decimal vat, decimal vatRon,
        bool vatOnCollection, bool isReversal, long? reversedInvoiceId, bool isAdvance, IReadOnlyList<InvoiceDueDate> dueDates, int? costCenterId, int? projectId,
        EInvoiceStatus eInvoiceStatus, string? eInvoiceIndexNo, CancellationToken ct)
    {
        var isCustomer = role == PartnerRole.Customer;
        var sign = SettlementRules.InvoiceSign(isCustomer);
        long? reversalOf = null;
        if (isReversal && reversedInvoiceId is { } reversed)
        {
            reversalOf = await store.FindBySourceAsync(documentType, reversed, role, ct).ConfigureAwait(false);
        }

        var schedules = dueDates.Count > 1 ? dueDates.Select(d => d with { Amount = sign * d.Amount }).ToArray() : [];
        var due = dueDates.Count == 0 ? documentDate : dueDates.Min(d => d.DueDate);
        var created = await store.CreateItemAsync(new OpenItemRecord(companyId, partnerId, role, SettlementRules.InvoiceKind(isReversal, isAdvance), module,
            documentType, invoiceId, "", number, documentDate, accountingDate, due, currencyCode, exchangeRate, sign * total, sign * totalRon, sign * vat,
            sign * vatRon, vatOnCollection, SettlementRules.InvoiceGlAccount(isCustomer), eInvoiceStatus, eInvoiceIndexNo, costCenterId, projectId, reversalOf,
            schedules), context.UserId, ct).ConfigureAwait(false);
        if (created.Created)
        {
            await settlement.AllocateNewInvoiceAsync(companyId, partnerId, role, created.Id, sign * total, currencyCode, reversalOf, created.Mode,
                created.IsPartnerPaymentBlocked, ct).ConfigureAwait(false);
        }

        return created.Id;
    }
}

// ---------- Solduri si partide (F3 6; ecranele f3-01..f3-03) ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetPartnerBalance(int PartnerId, PartnerRole? Role = null, DateOnly? AsOf = null) : IQuery<IReadOnlyList<PartnerBalanceDto>>;

internal sealed class GetPartnerBalanceHandler(ISettlementStore store, ICurrentContext context, TimeProvider time)
    : IQueryHandler<GetPartnerBalance, IReadOnlyList<PartnerBalanceDto>>
{
    public Task<IReadOnlyList<PartnerBalanceDto>> Handle(GetPartnerBalance request, CancellationToken cancellationToken) =>
        store.BalanceAsync(context.CompanyId, request.PartnerId, request.Role, request.AsOf ?? BusinessDay.Today(time), cancellationToken);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetOpenItems(int PartnerId, PartnerRole? Role = null, bool OnlyOpen = true, DateOnly? From = null, DateOnly? To = null)
    : IQuery<IReadOnlyList<OpenItemListDto>>;

internal sealed class GetOpenItemsHandler(ISettlementStore store, ICurrentContext context) : IQueryHandler<GetOpenItems, IReadOnlyList<OpenItemListDto>>
{
    public Task<IReadOnlyList<OpenItemListDto>> Handle(GetOpenItems request, CancellationToken cancellationToken) =>
        store.ListOpenAsync(context.CompanyId, request.PartnerId, request.Role, cancellationToken, request.OnlyOpen, request.From, request.To);
}

/// <summary>Soldurile partenerilor la o data, cu restantele, vechimea si totalurile firmei (F3 10, macheta f3-01).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetPartnerBalances(DateOnly? AsOf = null, PartnerRole? Role = null, string? Search = null, bool OnlyOverdue = false,
    bool OnlyUnallocated = false, bool BothRoles = false) : IQuery<PartnerBalanceListDto>;

internal sealed class GetPartnerBalancesHandler(ISettlementStore store, ICurrentContext context, TimeProvider time)
    : IQueryHandler<GetPartnerBalances, PartnerBalanceListDto>
{
    public Task<PartnerBalanceListDto> Handle(GetPartnerBalances request, CancellationToken cancellationToken) =>
        store.ListBalancesAsync(context.CompanyId, new PartnerBalanceFilter(request.AsOf ?? BusinessDay.Today(time), request.Role,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(), request.OnlyOverdue, request.OnlyUnallocated, request.BothRoles),
            cancellationToken);
}

/// <summary>Alocarile unui partener, cele mai noi intai (F3 3.3; tab-ul Alocari din f3-02).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetPartnerAllocations(int PartnerId, PartnerRole? Role = null) : IQuery<IReadOnlyList<PartnerAllocationDto>>;

internal sealed class GetPartnerAllocationsHandler(ISettlementStore store, ICurrentContext context)
    : IQueryHandler<GetPartnerAllocations, IReadOnlyList<PartnerAllocationDto>>
{
    public Task<IReadOnlyList<PartnerAllocationDto>> Handle(GetPartnerAllocations request, CancellationToken cancellationToken) =>
        store.ListAllocationsAsync(context.CompanyId, request.PartnerId, request.Role, cancellationToken);
}

// ---------- Alocarea manuala si desfacerea (F3 3.3, 4.3) ----------

public sealed record AllocationPairInput(long DebitOpenItemId, long CreditOpenItemId, string Amount);

public sealed record AllocationsCreatedDto(IReadOnlyList<long> AllocationIds, IReadOnlyList<long> ClosedOpenItemIds);

/// <summary>Alocarea manuala (F3 4.3): perechi debit / credit ale aceluiasi partener si rol; litigiul si factura neprimita in SPV cer confirmare.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AllocationManage)]
[Audited("Fin.Allocation.Create")]
public sealed record AllocateManually(int PartnerId, PartnerRole Role, IReadOnlyList<AllocationPairInput> Pairs, bool ConfirmWarnings = false)
    : ICommand<AllocationsCreatedDto>;

internal sealed class AllocateManuallyValidator : AbstractValidator<AllocateManually>
{
    public AllocateManuallyValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.Pairs).NotEmpty().WithMessage("Alegeți cel puțin o pereche de partide.").Must(p => p.Count <= 500).WithMessage("Cel mult 500 de perechi.");
        RuleForEach(x => x.Pairs).ChildRules(p =>
        {
            p.RuleFor(x => x.Amount).Must(a => Money.TryParse(a, out var v) && v > 0).WithMessage("Suma alocată trebuie să fie pozitivă.");
            p.RuleFor(x => x).Must(x => x.DebitOpenItemId != x.CreditOpenItemId).WithMessage("O partidă nu se alocă pe ea însăși.");
        });
    }
}

internal sealed class AllocateManuallyHandler(SettlementService settlement, ICurrentContext context, IPeriodLock periods, TimeProvider time, IAuditScope audit)
    : ICommandHandler<AllocateManually, AllocationsCreatedDto>
{
    public async Task<AllocationsCreatedDto> Handle(AllocateManually request, CancellationToken cancellationToken)
    {
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, BusinessDay.Today(time), cancellationToken).ConfigureAwait(false);
        var pairs = request.Pairs.Select(p => new AllocationRequest(p.DebitOpenItemId, p.CreditOpenItemId, Money.ParseOptional(p.Amount) ?? 0, null,
            AllocationSource.Manual)).ToList();
        var outcome = await settlement.AllocateManuallyAsync(context.CompanyId, request.PartnerId, request.Role, pairs, request.ConfirmWarnings, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.Allocation", null, new { request.PartnerId, request.Role, Allocations = outcome.Allocations.Select(a => a.AllocationId) });
        return new AllocationsCreatedDto(outcome.Allocations.Select(a => a.AllocationId).ToList(), outcome.ChangedItems.Select(i => i.Id).ToList());
    }
}

/// <summary>Desfacerea unei alocari (F3 3.3): refuzata intr-o perioada inchisa si pentru alocarile din documentele F3 (se storneaza documentul).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AllocationUndo)]
[Audited("Fin.Allocation.Undo")]
public sealed record UndoAllocation(long AllocationId, string Reason) : ICommand<bool>;

internal sealed class UndoAllocationValidator : AbstractValidator<UndoAllocation>
{
    public UndoAllocationValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Motivul desfacerii este obligatoriu.").MaximumLength(500);
}

internal sealed class UndoAllocationHandler(ISettlementStore store, ICurrentContext context, IPeriodLock periods, IOutbox outbox, TimeProvider time,
    IAuditScope audit) : ICommandHandler<UndoAllocation, bool>
{
    public async Task<bool> Handle(UndoAllocation request, CancellationToken cancellationToken)
    {
        var date = await store.AllocationDateAsync(context.CompanyId, request.AllocationId, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.Conflict("FIN_ALLOCATION_UNDO_NOT_ALLOWED", "Alocarea nu există.");
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, date, cancellationToken).ConfigureAwait(false);
        var outcome = await store.UndoAsync(context.CompanyId, [request.AllocationId], request.Reason.Trim(), false, BusinessDay.Today(time), context.UserId,
            cancellationToken).ConfigureAwait(false);
        await SettlementEvents.PublishAsync(outbox, outcome, true, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.Allocation", request.AllocationId, new { Reason = request.Reason.Trim() });
        return true;
    }
}

// ---------- Litigiu, blocare la plata, profil (F3 2 regula 7, 3.1, 3.2) ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpenItemSetDispute)]
[Audited("Fin.OpenItem.SetDispute")]
public sealed record SetOpenItemDispute(long OpenItemId, bool IsDisputed, string? Note, byte[] RowVersion) : ICommand<byte[]>;

internal sealed class SetOpenItemDisputeValidator : AbstractValidator<SetOpenItemDispute>
{
    public SetOpenItemDisputeValidator()
    {
        RuleFor(x => x.Note).NotEmpty().When(x => x.IsDisputed).WithMessage("Descrieți litigiul.").MaximumLength(500);
        RuleFor(x => x.RowVersion).NotNull().WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SetOpenItemDisputeHandler(ISettlementStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SetOpenItemDispute, byte[]>
{
    public async Task<byte[]> Handle(SetOpenItemDispute request, CancellationToken cancellationToken)
    {
        var version = await store.SetFlagsAsync(context.CompanyId, request.OpenItemId, request.IsDisputed, request.Note?.Trim(), null, request.RowVersion,
            context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.OpenItem", request.OpenItemId, new { request.IsDisputed });
        return version;
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpenItemBlockPayment)]
[Audited("Fin.OpenItem.BlockPayment")]
public sealed record SetOpenItemPaymentBlock(long OpenItemId, bool IsBlocked, byte[] RowVersion) : ICommand<byte[]>;

internal sealed class SetOpenItemPaymentBlockHandler(ISettlementStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SetOpenItemPaymentBlock, byte[]>
{
    public async Task<byte[]> Handle(SetOpenItemPaymentBlock request, CancellationToken cancellationToken)
    {
        var version = await store.SetFlagsAsync(context.CompanyId, request.OpenItemId, null, null, request.IsBlocked, request.RowVersion, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.OpenItem", request.OpenItemId, new { request.IsBlocked });
        return version;
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerBalanceView)]
public sealed record GetPartnerFinanceProfile(int PartnerId, PartnerRole Role) : IQuery<PartnerFinanceProfileDto>;

internal sealed class GetPartnerFinanceProfileHandler(ISettlementStore store, ICurrentContext context) : IQueryHandler<GetPartnerFinanceProfile, PartnerFinanceProfileDto>
{
    public Task<PartnerFinanceProfileDto> Handle(GetPartnerFinanceProfile request, CancellationToken cancellationToken) =>
        store.GetProfileAsync(context.CompanyId, request.PartnerId, request.Role, cancellationToken);
}

/// <summary>Profilul financiar (F3 3.2): modul de alocare, penalitatile din contract, blocarea furnizorului la plata (cere Fin.OpenItem.BlockPayment).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PartnerProfileManage)]
[Audited("Fin.PartnerProfile.Save")]
public sealed record SavePartnerFinanceProfile(int PartnerId, PartnerRole Role, AutoAllocationMode AutoAllocationMode, string PenaltyRatePerDay, int PenaltyGraceDays,
    bool PenaltyCapAtPrincipal, bool ApplyFixedRecoveryCost, bool IsPaymentBlocked, string? BlockReason, string? BalanceConfirmationEmail, byte[]? RowVersion)
    : ICommand<byte[]>;

internal sealed class SavePartnerFinanceProfileValidator : AbstractValidator<SavePartnerFinanceProfile>
{
    public SavePartnerFinanceProfileValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.AutoAllocationMode).IsInEnum();
        RuleFor(x => x.PenaltyRatePerDay).Must(r => decimal.TryParse(r, System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 10).WithMessage("Rata pe zi este un procent între 0 și 10.");
        RuleFor(x => x.PenaltyGraceDays).InclusiveBetween(0, 365);
        RuleFor(x => x.BlockReason).NotEmpty().When(x => x.IsPaymentBlocked).WithMessage("Motivul blocării este obligatoriu.").MaximumLength(500);
        RuleFor(x => x.BalanceConfirmationEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.BalanceConfirmationEmail)).MaximumLength(200);
    }
}

internal sealed class SavePartnerFinanceProfileHandler(ISettlementStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SavePartnerFinanceProfile, byte[]>
{
    public async Task<byte[]> Handle(SavePartnerFinanceProfile request, CancellationToken cancellationToken)
    {
        var current = await store.GetProfileAsync(context.CompanyId, request.PartnerId, request.Role, cancellationToken).ConfigureAwait(false);
        if (current.IsPaymentBlocked != request.IsPaymentBlocked && !context.HasPermission(FinancePermissions.OpenItemBlockPayment))
        {
            throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, "Blocarea la plată cere permisiunea Fin.OpenItem.BlockPayment.");
        }

        var version = await store.SaveProfileAsync(context.CompanyId, new PartnerFinanceProfileRecord(request.PartnerId, request.Role, request.AutoAllocationMode,
            decimal.Parse(request.PenaltyRatePerDay, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture),
            request.PenaltyGraceDays, request.PenaltyCapAtPrincipal, request.ApplyFixedRecoveryCost, request.IsPaymentBlocked, request.BlockReason?.Trim(),
            string.IsNullOrWhiteSpace(request.BalanceConfirmationEmail) ? null : request.BalanceConfirmationEmail.Trim(), request.RowVersion), context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PartnerProfile", request.PartnerId, new { request.Role, request.AutoAllocationMode, request.IsPaymentBlocked });
        return version;
    }
}
