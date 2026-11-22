using Erp.Core.Contracts;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Treasury;
using FluentValidation;

namespace Erp.Finance.Application.Treasury;

// ---------- DTO-uri ----------

/// <summary>Un avans in lista (macheta f1-05): acordat, decontat, restituit, soldul, termenul si intarzierea.</summary>
public sealed record CashAdvanceItemDto(int Id, int EmployeeId, string EmployeeName, AdvancePurpose Purpose, string CurrencyCode, string Amount,
    string SettledAmount, string ReturnedAmount, string PaidDifference, string Balance, DateOnly SettlementDueDate, DateOnly? TripStartDate,
    DateOnly? TripEndDate, AdvanceStatus Status, string? Description, string? GrantedByNumber, int? ExpenseReportId, string? ExpenseReportNumber,
    int DaysOverdue);

/// <summary>Un tip de cheltuiala in vigoare (cu diurna pe zi si plafonul neimpozabil, la diurna).</summary>
public sealed record ExpenseTypeOptionDto(int Id, string Code, string Name, bool IsPerDiem, string? DailyAmount, string? NonTaxableDailyLimit);

/// <summary>Listele avansurilor si deconturilor: angajatii, casele, tipurile de cheltuieli.</summary>
public sealed record AdvanceLookupsDto(IReadOnlyList<CashEmployeeOptionDto> Employees, IReadOnlyList<CashAccountOptionDto> CashAccounts,
    IReadOnlyList<ExpenseTypeOptionDto> ExpenseTypes);

public sealed record CashAdvancePageDto(IReadOnlyList<CashAdvanceItemDto> Advances, AdvanceLookupsDto Lookups);

public sealed record CashAdvanceCreatedDto(int CashAdvanceId, int CashDocumentId, DateOnly SettlementDueDate);

/// <summary>Un decont in lista.</summary>
public sealed record ExpenseReportItemDto(int Id, string? Number, DateOnly DocumentDate, string EmployeeName, int? CashAdvanceId, string TotalJustified,
    string Difference, ExpenseReportStatus Status);

public sealed record ExpenseReportPageDto(IReadOnlyList<ExpenseReportItemDto> Reports, IReadOnlyList<CashAdvanceItemDto> OpenAdvances, AdvanceLookupsDto Lookups);

/// <summary>O linie a decontului (macheta f1-06).</summary>
public sealed record ExpenseReportLineDto(long Id, short LineNumber, int ExpenseTypeId, string ExpenseTypeCode, string ExpenseTypeName, bool IsPerDiem,
    JustificationKind JustificationKind, string? DocumentNo, DateOnly? DocumentDate, string? SupplierFiscalCode, int? SupplierInvoiceId, string Amount,
    string VatAmount, int? CostCenterId, int? ProjectId, string? Days, string? DailyAmount, string? NonTaxableAmount, string? TaxableAmount, string? Note);

/// <summary>Decontul de cheltuieli (macheta f1-06): avansul, totalul justificat, diferenta, aprobarea si dispozitia diferentei.</summary>
public sealed record ExpenseReportDetailDto(int Id, int EmployeeId, string EmployeeName, int? CashAdvanceId, string? AdvanceGranted, string? Number,
    DateOnly DocumentDate, string CurrencyCode, string TotalJustified, string AdvanceAmount, string Difference, string? Description, ExpenseReportStatus Status,
    int CreatedBy, string? CreatedByName, DateTime CreatedAt, DateTime? SubmittedAt, string? SubmittedByName, DateTime? ApprovedAt, string? ApprovedByName,
    DateTime? ValidatedAt, int? SettlementCashDocumentId, string? SettlementNumber, CashDocumentStatus? SettlementStatus, IReadOnlyList<ExpenseReportLineDto> Lines,
    byte[] RowVersion);

public sealed record ExpenseReportLineInput(int ExpenseTypeId, JustificationKind JustificationKind, string? DocumentNo, DateOnly? DocumentDate,
    string? SupplierFiscalCode, int? SupplierInvoiceId, string? Amount, string? VatAmount, int? CostCenterId, int? ProjectId, string? Days, string? Note);

public sealed record ExpenseReportLineRecord(short LineNumber, int ExpenseTypeId, JustificationKind JustificationKind, string? DocumentNo,
    DateOnly? DocumentDate, string? SupplierFiscalCode, int? SupplierInvoiceId, decimal Amount, decimal VatAmount, int? CostCenterId, int? ProjectId,
    decimal? Days, decimal? DailyAmount, decimal? NonTaxableAmount, decimal? TaxableAmount, string? Note);

public sealed record ExpenseReportRecord(int? Id, int CompanyId, int EmployeeId, int? CashAdvanceId, DateOnly DocumentDate, string? Description,
    decimal TotalJustified, IReadOnlyList<ExpenseReportLineRecord> Lines, byte[]? RowVersion);

public sealed record ExpenseReportStep(int Id, string? Number, ExpenseReportStatus Status, decimal Difference, int? CashAdvanceId, byte[] RowVersion);

public sealed record CashAdvanceRecord(int CompanyId, int EmployeeId, AdvancePurpose Purpose, string CurrencyCode, decimal Amount, string? Description,
    DateOnly? TripStartDate, DateOnly? TripEndDate, DateOnly SettlementDueDate, bool OverdueConfirmed);

/// <summary>Un tip de cheltuiala cu sumele in vigoare (pentru calculul diurnei).</summary>
public sealed record ExpenseTypeRates(int Id, string Code, string Name, bool IsPerDiem, decimal? DailyAmount, decimal? NonTaxableDailyLimit);

/// <summary>Accesul la date pentru avansuri si deconturi (fin.usp_CashAdvance_*, fin.usp_ExpenseReport_*, NET-06).</summary>
public interface IAdvanceStore
{
    Task<IReadOnlyList<CashAdvanceItemDto>> ListAdvancesAsync(int companyId, byte filter, DateOnly today, CancellationToken ct);

    Task<IReadOnlyList<ExpenseTypeRates>> GetExpenseTypesAsync(DateOnly day, CancellationToken ct);

    Task<IReadOnlyList<ExpenseReportItemDto>> ListReportsAsync(int companyId, CancellationToken ct);

    Task<bool> HasOverdueAsync(int companyId, int employeeId, DateOnly today, CancellationToken ct);

    Task<int> CreateAdvanceAsync(CashAdvanceRecord advance, int userId, CancellationToken ct);

    Task<int> GetOperationTypeIdAsync(string code, CancellationToken ct);

    Task<FinanceSaved> SaveReportAsync(ExpenseReportRecord report, int userId, CancellationToken ct);

    Task<ExpenseReportDetailDto?> GetReportAsync(int companyId, int id, CancellationToken ct);

    Task<ExpenseReportStep> TransitionAsync(int companyId, int id, byte[] rowVersion, string action, string? number, bool separateDuties, int userId,
        CancellationToken ct);

    Task SetSettlementAsync(int reportId, int cashDocumentId, CancellationToken ct);
}

/// <summary>Ciorna unei dispozitii de casa generate (avans, diferenta de decont): cursul zilei, echivalentul in lei, legaturile.</summary>
public sealed class CashDraftFactory(ICashDocumentStore documents, ITreasuryAccountStore accounts, IExchangeRateProvider rates, IPrecisionService precision,
    ICurrentContext context)
{
    public async Task<(FinanceSaved Saved, string CurrencyCode)> CreateAsync(int treasuryAccountId, CashDocumentKind kind, DateOnly day, int employeeId,
        int operationTypeId, decimal amount, string description, int? cashAdvanceId, int? expenseReportId, CancellationToken ct)
    {
        var account = await accounts.GetAsync(context.CompanyId, treasuryAccountId, ct).ConfigureAwait(false);
        if (account is null || account.Kind != TreasuryAccountKind.Cash)
        {
            throw ErpException.NotFound(FinanceErrorCodes.TreasuryAccountNotFound, "Casa nu există în firma curentă.");
        }

        var rate = await rates.GetRateAsync(account.CurrencyCode, day, ct).ConfigureAwait(false);
        var policy = await precision.ResolveAsync(context.CompanyId, null, null, ct).ConfigureAwait(false);
        var ron = precision.RoundAmount(amount * rate.PerUnit, policy);
        var record = new CashDocumentRecord(null, context.CompanyId, treasuryAccountId, kind, day, null, employeeId, null, null, false, rate.PerUnit, rate.RateDate,
            amount, ron, description, [new CashLineRecord(1, operationTypeId, amount, ron, null, null, null, null, null)], null, cashAdvanceId, expenseReportId);
        // Ciorna o genereaza contabilul (avans, decont), cu dreptul comenzii lui; atribuirea pe casa o cere validarea casierului.
        var saved = await documents.SaveAsync(record, requireAssignment: false, context.UserId, ct).ConfigureAwait(false);
        return (saved, account.CurrencyCode);
    }
}

// ---------- Avansuri ----------

/// <summary>Lista avansurilor spre decontare (macheta f1-05). Filtru: 0 toate, 1 deschise, 2 depasite, 3 decontate.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AdvanceManage)]
public sealed record GetCashAdvances(byte Filter = 1) : IQuery<CashAdvancePageDto>;

internal sealed class GetCashAdvancesHandler(IAdvanceStore store, ICashDocumentStore documents, ICurrentContext context, TimeProvider time)
    : IQueryHandler<GetCashAdvances, CashAdvancePageDto>
{
    public async Task<CashAdvancePageDto> Handle(GetCashAdvances request, CancellationToken cancellationToken)
    {
        var today = BusinessDay.Today(time);
        var advances = await store.ListAdvancesAsync(context.CompanyId, request.Filter, today, cancellationToken).ConfigureAwait(false);
        return new CashAdvancePageDto(advances, await AdvanceLookups.LoadAsync(store, documents, context, today, cancellationToken).ConfigureAwait(false));
    }
}

internal static class AdvanceLookups
{
    public static async Task<AdvanceLookupsDto> LoadAsync(IAdvanceStore store, ICashDocumentStore documents, ICurrentContext context, DateOnly today,
        CancellationToken ct)
    {
        var desk = await documents.GetDeskLookupsAsync(context.CompanyId, context.UserId, true, today, ct).ConfigureAwait(false);
        var types = await store.GetExpenseTypesAsync(today, ct).ConfigureAwait(false);
        return new AdvanceLookupsDto(desk.Employees, desk.Accounts, types.Select(t => new ExpenseTypeOptionDto(t.Id, t.Code, t.Name, t.IsPerDiem,
            Money.Format(t.DailyAmount), Money.Format(t.NonTaxableDailyLimit))).ToList());
    }
}

/// <summary>
/// Acorda un avans spre decontare (F1 4.4 pasii 1–2): avansul (De platit) si dispozitia de plata „Avans spre decontare acordat” ca ciorna,
/// pe casa aleasa; validarea ei de casier il acorda. Angajatul cu un avans depasit primeste unul nou doar cu avertizarea confirmata
/// (51018, decizia din 15.11.2026), care intra in audit. Plafonul pe zi si persoana se verifica la validarea dispozitiei.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AdvanceManage)]
[Audited("Fin.Advance.Create")]
public sealed record CreateCashAdvance(int EmployeeId, AdvancePurpose Purpose, string Amount, int TreasuryAccountId, DateOnly DocumentDate,
    DateOnly? TripStartDate, DateOnly? TripEndDate, string? Description, bool ConfirmOverdue = false) : ICommand<CashAdvanceCreatedDto>, IAccountingDated
{
    public DateOnly AccountingDate => DocumentDate;
}

internal sealed class CreateCashAdvanceValidator : AbstractValidator<CreateCashAdvance>
{
    public CreateCashAdvanceValidator()
    {
        RuleFor(x => x.EmployeeId).GreaterThan(0).WithMessage("Alegeți angajatul.");
        RuleFor(x => x.Purpose).IsInEnum().WithMessage("Alegeți scopul avansului.");
        RuleFor(x => x.Amount).Must(v => Money.TryParse(v, out var a) && a > 0).WithMessage("Suma este pozitivă, cu cel mult două zecimale.");
        RuleFor(x => x.TreasuryAccountId).GreaterThan(0).WithMessage("Alegeți casa din care se plătește avansul.");
        RuleFor(x => x.TripEndDate).NotNull().When(x => x.Purpose is AdvancePurpose.DomesticTrip or AdvancePurpose.ForeignTrip)
            .WithMessage("Deplasarea are data întoarcerii.");
        RuleFor(x => x.TripEndDate).GreaterThanOrEqualTo(x => x.TripStartDate).When(x => x.TripStartDate is not null && x.TripEndDate is not null)
            .WithMessage("Întoarcerea nu poate fi înaintea plecării.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

internal sealed class CreateCashAdvanceHandler(IAdvanceStore store, CashDraftFactory drafts, ITreasuryAccountStore accounts, ICurrentContext context,
    IAuditScope audit, TimeProvider time) : ICommandHandler<CreateCashAdvance, CashAdvanceCreatedDto>
{
    public async Task<CashAdvanceCreatedDto> Handle(CreateCashAdvance request, CancellationToken cancellationToken)
    {
        var overdue = await store.HasOverdueAsync(context.CompanyId, request.EmployeeId, BusinessDay.Today(time), cancellationToken).ConfigureAwait(false);
        if (overdue && !request.ConfirmOverdue)
        {
            throw ErpException.Conflict("FIN_ADVANCE_OVERDUE_UNCONFIRMED",
                "Angajatul are un avans nedecontat după termen. Confirmați pentru a acorda totuși un avans nou.");
        }

        var account = await accounts.GetAsync(context.CompanyId, request.TreasuryAccountId, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound(FinanceErrorCodes.TreasuryAccountNotFound, "Casa nu există în firma curentă.");
        var amount = Money.ParseOptional(request.Amount) ?? 0;
        var start = request.Purpose is AdvancePurpose.DomesticTrip or AdvancePurpose.ForeignTrip ? request.TripEndDate ?? request.DocumentDate
            : request.DocumentDate;
        var due = AdvanceRules.SettlementDueDate(start, AdvanceRules.DefaultSettlementWorkingDays);
        var advanceId = await store.CreateAdvanceAsync(new CashAdvanceRecord(context.CompanyId, request.EmployeeId, request.Purpose, account.CurrencyCode, amount,
            request.Description?.Trim(), request.TripStartDate, request.TripEndDate, due, overdue), context.UserId, cancellationToken).ConfigureAwait(false);
        var operation = await store.GetOperationTypeIdAsync("PLATA-AVANS-DECONTARE", cancellationToken).ConfigureAwait(false);
        var (draft, _) = await drafts.CreateAsync(request.TreasuryAccountId, CashDocumentKind.CashOut, request.DocumentDate, request.EmployeeId, operation, amount,
            string.IsNullOrWhiteSpace(request.Description) ? "Avans spre decontare" : request.Description.Trim(), advanceId, null, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.CashAdvance", advanceId, new
        {
            request.EmployeeId, request.Purpose, amount, account.CurrencyCode, due, CashDocumentId = draft.Id, OverdueConfirmed = overdue,
        });
        return new CashAdvanceCreatedDto(advanceId, draft.Id, due);
    }
}

// ---------- Deconturi ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ExpenseReportCreate)]
public sealed record GetExpenseReports : IQuery<ExpenseReportPageDto>;

internal sealed class GetExpenseReportsHandler(IAdvanceStore store, ICashDocumentStore documents, ICurrentContext context, TimeProvider time)
    : IQueryHandler<GetExpenseReports, ExpenseReportPageDto>
{
    public async Task<ExpenseReportPageDto> Handle(GetExpenseReports request, CancellationToken cancellationToken)
    {
        var today = BusinessDay.Today(time);
        var reports = await store.ListReportsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var open = await store.ListAdvancesAsync(context.CompanyId, 1, today, cancellationToken).ConfigureAwait(false);
        return new ExpenseReportPageDto(reports, open.Where(a => a.Status != AdvanceStatus.ToPay && a.ExpenseReportId is null).ToList(),
            await AdvanceLookups.LoadAsync(store, documents, context, today, cancellationToken).ConfigureAwait(false));
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ExpenseReportCreate)]
public sealed record GetExpenseReport(int Id) : IQuery<ExpenseReportDetailDto>;

internal sealed class GetExpenseReportHandler(IAdvanceStore store, ICurrentContext context) : IQueryHandler<GetExpenseReport, ExpenseReportDetailDto>
{
    public async Task<ExpenseReportDetailDto> Handle(GetExpenseReport request, CancellationToken cancellationToken) =>
        await store.GetReportAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw ExpenseReports.NotFound();
}

internal static class ExpenseReports
{
    public static ErpException NotFound() => ErpException.NotFound("FIN_EXPENSE_REPORT_NOT_FOUND", "Decontul nu există.");
}

/// <summary>
/// Ciorna decontului de cheltuieli (F1 3.7; o introduce contabilul): justificativele, diurna calculata pe server din tipul de cheltuiala in
/// vigoare la data decontului (zile × suma pe zi, partea neimpozabila si impozabila), totalul si diferenta fata de avans.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ExpenseReportCreate)]
[Audited("Fin.ExpenseReport.Save")]
public sealed record SaveExpenseReport(int? Id, int EmployeeId, int? CashAdvanceId, DateOnly DocumentDate, string? Description,
    IReadOnlyList<ExpenseReportLineInput> Lines, byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SaveExpenseReportValidator : AbstractValidator<SaveExpenseReport>
{
    public SaveExpenseReportValidator()
    {
        RuleFor(x => x.EmployeeId).GreaterThan(0).WithMessage("Alegeți angajatul.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Decontul are cel puțin o cheltuială.").Must(l => l.Count <= 200);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ExpenseTypeId).GreaterThan(0).WithMessage("Alegeți tipul de cheltuială.");
            line.RuleFor(l => l.JustificationKind).IsInEnum();
            line.RuleFor(l => l.Amount).Must(v => Money.TryParse(v, out var a) && a >= 0).When(l => l.JustificationKind != JustificationKind.PerDiem)
                .WithMessage("Suma este pozitivă, cu cel mult două zecimale.");
            line.RuleFor(l => l.Days).Must(v => decimal.TryParse(v, System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0 && d <= 366)
                .When(l => l.JustificationKind == JustificationKind.PerDiem).WithMessage("Diurna cere numărul de zile.");
            line.RuleFor(l => l.VatAmount).Must(v => string.IsNullOrWhiteSpace(v) || (Money.TryParse(v, out var a) && a >= 0))
                .WithMessage("TVA-ul este pozitiv, cu cel mult două zecimale.");
            line.RuleFor(l => l.DocumentNo).MaximumLength(50);
            line.RuleFor(l => l.SupplierFiscalCode).MaximumLength(20);
            line.RuleFor(l => l.Note).MaximumLength(500);
        });
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveExpenseReportHandler(IAdvanceStore store, IPrecisionService precision, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveExpenseReport, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveExpenseReport request, CancellationToken cancellationToken)
    {
        var types = (await store.GetExpenseTypesAsync(request.DocumentDate, cancellationToken).ConfigureAwait(false)).ToDictionary(t => t.Id);
        var policy = await precision.ResolveAsync(context.CompanyId, null, null, cancellationToken).ConfigureAwait(false);
        var lines = new List<ExpenseReportLineRecord>();
        foreach (var (line, index) in request.Lines.Select((l, i) => (l, i)))
        {
            if (!types.TryGetValue(line.ExpenseTypeId, out var type))
            {
                throw new ErpValidationException(new Dictionary<string, string[]> { [$"lines[{index}].expenseTypeId"] = ["Tipul de cheltuială nu este în vigoare."] });
            }

            if (line.JustificationKind == JustificationKind.PerDiem != type.IsPerDiem)
            {
                throw new ErpValidationException(new Dictionary<string, string[]> { [$"lines[{index}].justificationKind"] = ["Diurna se justifică doar cu tipul de cheltuială „diurnă”."] });
            }

            var vat = Money.ParseOptional(line.VatAmount) ?? 0;
            if (type.IsPerDiem)
            {
                var days = decimal.Parse(line.Days!, System.Globalization.CultureInfo.InvariantCulture);
                var perDiem = AdvanceRules.PerDiem(days, type.DailyAmount ?? 0, type.NonTaxableDailyLimit ?? 0, v => precision.RoundAmount(v, policy));
                lines.Add(new ExpenseReportLineRecord((short)(index + 1), type.Id, line.JustificationKind, Clean(line.DocumentNo), line.DocumentDate, null, null,
                    perDiem.Amount, 0, line.CostCenterId, line.ProjectId, days, type.DailyAmount, perDiem.NonTaxable, perDiem.Taxable, Clean(line.Note)));
            }
            else
            {
                lines.Add(new ExpenseReportLineRecord((short)(index + 1), type.Id, line.JustificationKind, Clean(line.DocumentNo), line.DocumentDate,
                    Clean(line.SupplierFiscalCode), line.SupplierInvoiceId, Money.ParseOptional(line.Amount) ?? 0, vat, line.CostCenterId, line.ProjectId, null,
                    null, null, null, Clean(line.Note)));
            }
        }

        var record = new ExpenseReportRecord(request.Id, context.CompanyId, request.EmployeeId, request.CashAdvanceId, request.DocumentDate,
            Clean(request.Description), lines.Sum(l => l.Amount), lines, request.RowVersion);
        var saved = await store.SaveReportAsync(record, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.ExpenseReport", saved.Id, new { record.EmployeeId, record.CashAdvanceId, record.DocumentDate, record.TotalJustified, Lines = lines.Count });
        return saved;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Trimite decontul spre aprobare (Ciorna → Trimis).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ExpenseReportCreate)]
[Audited("Fin.ExpenseReport.Submit")]
public sealed record SubmitExpenseReport(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

/// <summary>Aproba decontul (Trimis → Aprobat); cine l-a intocmit nu il aproba (separarea sarcinilor, 51019).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ExpenseReportApprove)]
[Audited("Fin.ExpenseReport.Approve")]
public sealed record ApproveExpenseReport(int Id, byte[] RowVersion) : ICommand<FinanceSaved>;

internal sealed class ExpenseReportStepHandlers(IAdvanceStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SubmitExpenseReport, FinanceSaved>, ICommandHandler<ApproveExpenseReport, FinanceSaved>
{
    public Task<FinanceSaved> Handle(SubmitExpenseReport request, CancellationToken cancellationToken) =>
        StepAsync(request.Id, request.RowVersion, "Submit", cancellationToken);

    public Task<FinanceSaved> Handle(ApproveExpenseReport request, CancellationToken cancellationToken) =>
        StepAsync(request.Id, request.RowVersion, "Approve", cancellationToken);

    private async Task<FinanceSaved> StepAsync(int id, byte[] rowVersion, string action, CancellationToken ct)
    {
        var step = await store.TransitionAsync(context.CompanyId, id, rowVersion, action, null, separateDuties: true, context.UserId, ct).ConfigureAwait(false);
        audit.Describe("Fin.ExpenseReport", id, new { Status = step.Status });
        return new FinanceSaved(step.Id, step.RowVersion);
    }
}

/// <summary>Rezultatul validarii decontului: numarul, diferenta si dispozitia (ciorna) care o inchide.</summary>
public sealed record ExpenseReportValidatedDto(int Id, string? Number, string Difference, int? SettlementCashDocumentId, byte[] RowVersion);

/// <summary>
/// Valideaza decontul aprobat (F1 4.4 pasii 4–5): numarul din serie, suma justificata trecuta pe avans, DecontCheltuieliValidat in outbox
/// (cu diurna impozabila pentru HR) si, pentru diferenta, ciorna pe casa aleasa: dispozitie de incasare „Restituire avans” cand angajatul
/// restituie, dispozitie de plata „Diferenta de platit din decont” cand firma ii datoreaza. Validarea ei de casier inchide avansul.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ExpenseReportValidate)]
[Audited("Fin.ExpenseReport.Validate")]
public sealed record ValidateExpenseReport(int Id, byte[] RowVersion, int? SettlementTreasuryAccountId) : ICommand<ExpenseReportValidatedDto>;

internal sealed class ValidateExpenseReportHandler(IAdvanceStore store, CashDraftFactory drafts, INumberSeriesService numbers, IPeriodLock periods,
    IOutbox outbox, ICurrentContext context, IAuditScope audit) : ICommandHandler<ValidateExpenseReport, ExpenseReportValidatedDto>
{
    public async Task<ExpenseReportValidatedDto> Handle(ValidateExpenseReport request, CancellationToken cancellationToken)
    {
        var report = await store.GetReportAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw ExpenseReports.NotFound();
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, report.DocumentDate, cancellationToken).ConfigureAwait(false);
        var difference = Money.ParseOptional(report.Difference) ?? 0;
        if (difference != 0 && request.SettlementTreasuryAccountId is null)
        {
            throw new ErpValidationException(new Dictionary<string, string[]>
            {
                ["settlementTreasuryAccountId"] = ["Alegeți casa în care se închide diferența decontului."],
            });
        }

        var number = (await numbers.NextAsync(context.CompanyId, "Finance.ExpenseReport", report.DocumentDate, cancellationToken).ConfigureAwait(false)).Formatted;
        var step = await store.TransitionAsync(context.CompanyId, report.Id, request.RowVersion, "Validate", number, true, context.UserId, cancellationToken)
            .ConfigureAwait(false);

        int? settlementId = null;
        if (step.Difference != 0)
        {
            var refund = step.Difference < 0;
            var operation = await store.GetOperationTypeIdAsync(refund ? "INC-RESTITUIRE-AVANS" : "PLATA-DIFERENTA-DECONT", cancellationToken).ConfigureAwait(false);
            var (draft, _) = await drafts.CreateAsync(request.SettlementTreasuryAccountId!.Value, refund ? CashDocumentKind.CashIn : CashDocumentKind.CashOut,
                report.DocumentDate, report.EmployeeId, operation, Math.Abs(step.Difference), $"Decont {number}", report.CashAdvanceId, report.Id,
                cancellationToken).ConfigureAwait(false);
            await store.SetSettlementAsync(report.Id, draft.Id, cancellationToken).ConfigureAwait(false);
            settlementId = draft.Id;
        }

        var taxable = report.Lines.Sum(l => Money.ParseOptional(l.TaxableAmount) ?? 0);
        await outbox.EnqueueAsync(new DecontCheltuieliValidat(report.Id, report.EmployeeId, report.CashAdvanceId, Money.ParseOptional(report.TotalJustified) ?? 0,
            step.Difference, taxable, report.Lines.Select(l => new DecontLinie(l.ExpenseTypeCode, Money.ParseOptional(l.Amount) ?? 0,
                Money.ParseOptional(l.VatAmount) ?? 0, l.SupplierInvoiceId)).ToList()) { CompanyId = context.CompanyId }, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.ExpenseReport", report.Id, new { Number = number, step.Difference, SettlementCashDocumentId = settlementId });
        return new ExpenseReportValidatedDto(report.Id, number, Money.Format(step.Difference), settlementId, step.RowVersion);
    }
}
