using Erp.Core.Contracts;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Treasury;
using FluentValidation;

namespace Erp.Finance.Application.Treasury;

// ---------- DTO-uri si inregistrari ----------

/// <summary>O linie a documentului de casa (macheta f1-03). Sumele circula ca text.</summary>
public sealed record CashDocumentLineDto(long Id, short LineNumber, int OperationTypeId, string OperationTypeCode, string OperationTypeName,
    CashDirection Direction, string Amount, string AmountRon, int? CounterTreasuryAccountId, string? CounterTreasuryAccountCode, string? SettlementRef,
    int? CostCenterId, int? ProjectId, string? Note);

/// <summary>Documentul de casa (macheta f1-03), cu starea si legaturile de storno.</summary>
public sealed record CashDocumentDetailDto(int Id, int TreasuryAccountId, string TreasuryAccountCode, string TreasuryAccountName, CashDocumentKind Kind,
    string? Number, DateOnly DocumentDate, CashDocumentStatus Status, int? PartnerId, string? PartnerName, int? EmployeeId, string? EmployeeName,
    string? PersonName, bool HasPersonId, string CurrencyCode, string ExchangeRate, DateOnly RateDate, string TotalAmount, string TotalAmountRon,
    string? Description, int? ReversalOfId, string? ReversalOfNumber, int? ReversedById, string? ReversedByNumber, DateTime? ValidatedAt,
    string? ValidatedByName, DateTime CreatedAt, DateTime? ModifiedAt, string? ModifiedByName, IReadOnlyList<CashDocumentLineDto> Lines,
    byte[] RowVersion);

/// <summary>Un document in lista zilei (macheta f1-02).</summary>
public sealed record CashDayDocumentDto(int Id, CashDocumentKind Kind, string? Number, CashDocumentStatus Status, string? PersonName, string? OperationName,
    string TotalAmount, string? Description, int? ReversalOfId, int? ReversedById, DateTime? ValidatedAt);

/// <summary>Ziua casei (macheta f1-02): soldul de inceput, incasarile, platile, soldul curent si starea.</summary>
public sealed record CashDaySummaryDto(int TreasuryAccountId, string Code, string Name, string CurrencyCode, DateOnly Day, string OpeningBalance,
    string TotalIn, string TotalOut, string ClosingBalance, bool IsClosed, DateOnly? FirstOpenDay, DateOnly? LastClosedDay, string? MaxClosingBalance);

/// <summary>O casa pe care utilizatorul o vede (atribuita sau, cu Fin.Cash.ViewAll, orice casa activa a firmei).</summary>
public sealed record CashAccountOptionDto(int Id, string Code, string Name, string CurrencyCode, bool CanValidate);

/// <summary>Un tip de operatiune de casa pentru liniile documentului (F1 3.2).</summary>
public sealed record CashOperationTypeDto(int Id, string Code, string Name, CashDirection Direction, bool RequiresPartner, bool RequiresEmployee,
    bool RequiresCounterAccount, string? CashLimitCategory)
{
    /// <summary>Decontarea (F1 3.2, F3 4.2): 1 facturi client, 2 facturi furnizor, 5 / 6 avans; ecranul ofera facturile deschise la 1 si 2.</summary>
    public byte SettlementKind { get; init; }
}

/// <summary>Un angajat al firmei, pentru documentele de casa ale angajatilor (avansuri, salarii, restituiri).</summary>
public sealed record CashEmployeeOptionDto(int Id, string Name, string EmployeeNo);

/// <summary>Listele ecranului Casa: casele vizibile, tipurile de operatiuni, conturile corespondente, angajatii.</summary>
public sealed record CashDeskLookupsDto(IReadOnlyList<CashAccountOptionDto> Accounts, IReadOnlyList<CashOperationTypeDto> OperationTypes,
    IReadOnlyList<TreasuryOptionDto> CounterAccounts, IReadOnlyList<CashEmployeeOptionDto> Employees);

/// <summary>Ecranul Casa (macheta f1-02): listele, ziua aleasa a casei alese si documentele ei.</summary>
public sealed record CashDeskDto(CashDeskLookupsDto Lookups, CashDaySummaryDto? Day, IReadOnlyList<CashDayDocumentDto> Documents);

/// <summary>Rezultatul validarii sau stornarii: documentul validat, numarul si soldul zilei lui.</summary>
public sealed record CashValidatedDto(int CashDocumentId, string? Number, string DayClosingBalance, byte[] RowVersion);

public sealed record CashLineInput(int OperationTypeId, string Amount, int? CounterTreasuryAccountId, string? SettlementRef, int? CostCenterId,
    int? ProjectId, string? Note);

public sealed record CashLineRecord(short LineNumber, int OperationTypeId, decimal Amount, decimal AmountRon, int? CounterTreasuryAccountId,
    string? SettlementRef, int? CostCenterId, int? ProjectId, string? Note);

public sealed record CashDocumentRecord(int? Id, int CompanyId, int TreasuryAccountId, CashDocumentKind Kind, DateOnly DocumentDate, int? PartnerId,
    int? EmployeeId, string? PersonName, byte[]? PersonIdEncrypted, bool KeepPersonId, decimal ExchangeRate, DateOnly RateDate, decimal TotalAmount,
    decimal TotalAmountRon, string? Description, IReadOnlyList<CashLineRecord> Lines, byte[]? RowVersion, int? CashAdvanceId = null,
    int? ExpenseReportId = null);

/// <summary>Datele interne ale documentului pentru validare (casa, seriile, tipul partenerului, liniile cu categoria de plafon).</summary>
public sealed record CashDocumentState(CashDocumentDetailDto Document, int? BranchId, int? ReceiptSeriesId, int? CashInSeriesId, int? CashOutSeriesId,
    byte? PartnerType, IReadOnlyList<CashLineState> Lines, int? CashAdvanceId = null, byte? AdvancePurpose = null, DateOnly? AdvanceDueDate = null,
    int? ExpenseReportId = null);

public sealed record CashLineState(long Id, string OperationTypeCode, string? CashLimitCategory, decimal Amount, decimal AmountRon, string? SettlementRef,
    int? CounterTreasuryAccountId);

public sealed record CashValidation(int CashDocumentId, string? Number, decimal DayClosingBalance, byte[] RowVersion);

/// <summary>Accesul la date pentru documentele de casa (fin.usp_CashDocument_*, fin.usp_CashDay_Get, fin.usp_CashLimit*, NET-06).</summary>
public interface ICashDocumentStore
{
    Task<CashDeskLookupsDto> GetDeskLookupsAsync(int companyId, int userId, bool viewAll, DateOnly day, CancellationToken ct);

    Task<(CashDaySummaryDto? Day, IReadOnlyList<CashDayDocumentDto> Documents)> GetDayAsync(int companyId, int treasuryAccountId, DateOnly day,
        CancellationToken ct);

    Task<CashDocumentState?> GetAsync(int companyId, int id, CancellationToken ct);

    /// <summary>Utilizatorul e atribuit casei in ziua data (si, daca se cere, are dreptul de validare).</summary>
    Task<bool> IsAssignedAsync(int treasuryAccountId, int userId, DateOnly day, bool forValidation, CancellationToken ct);

    Task<FinanceSaved> SaveAsync(CashDocumentRecord document, bool requireAssignment, int userId, CancellationToken ct);

    Task DeleteAsync(int companyId, int id, byte[] rowVersion, bool requireAssignment, int userId, CancellationToken ct);

    Task<IReadOnlyDictionary<string, decimal>> GetDayTotalsAsync(int companyId, DateOnly day, int? partnerId, int? employeeId, string? personName,
        int excludeId, CancellationToken ct);

    Task<IReadOnlyList<CashLimit>> GetLimitsAsync(int companyId, DateOnly day, CancellationToken ct);

    Task<CashValidation> ValidateAsync(int companyId, int id, string? number, byte[] rowVersion, int userId, CancellationToken ct);

    Task<CashValidation> ReverseAsync(int companyId, int id, byte[] rowVersion, DateOnly day, string? number, int userId, CancellationToken ct);
}

/// <summary>
/// Jurnalul verificarilor de plafon (F1 3.6 regula 4): refuzurile se scriu pe o conexiune proprie, in afara tranzactiei comenzii, care se
/// anuleaza la refuz; confirmarile se scriu in tranzactie, odata cu validarea.
/// </summary>
public interface ICashLimitLog
{
    Task LogAsync(int companyId, int? cashDocumentId, int userId, CashDocumentDetailDto document, CashLimitBreach breach, bool confirmed,
        bool outsideTransaction, CancellationToken ct);
}

/// <summary>Actul de identitate al persoanelor fara fisa de partener, criptat (SP-20).</summary>
public interface IPersonIdProtector
{
    byte[] Protect(string value);
}

/// <summary>Ziua de lucru a Romaniei (Europe/Bucharest), din ceasul serverului.</summary>
public static class BusinessDay
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).DateTime);
}

internal static class CashAccess
{
    public static bool ViewAll(ICurrentContext context) => context.HasPermission(FinancePermissions.CashViewAll);

    public static ErpException NotAssigned() =>
        new(ErpErrorKind.Forbidden, "FIN_CASHIER_NOT_ASSIGNED", "Nu sunteți atribuit acestei case sau nu aveți dreptul de validare pe ea.");

    public static ErpException NotFound() => ErpException.NotFound("FIN_CASH_DOCUMENT_NOT_FOUND", "Documentul de casă nu există.");

    public static async Task EnsureOpenPeriodAsync(IPeriodLock periods, int companyId, DateOnly day, CancellationToken ct)
    {
        if (await periods.IsClosedAsync(companyId, day, ct).ConfigureAwait(false))
        {
            throw ErpException.Conflict(CoreErrorCodes.PeriodClosed, "Perioada contabilă a documentului este închisă.");
        }
    }
}

// ---------- Interogari ----------

/// <summary>Ecranul Casa (macheta f1-02): casele vizibile, ziua aleasa (implicit azi) a casei alese (implicit prima) si documentele ei.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashView)]
public sealed record GetCashDesk(int? TreasuryAccountId = null, DateOnly? Day = null) : IQuery<CashDeskDto>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashView)]
public sealed record GetCashDocument(int Id) : IQuery<CashDocumentDetailDto>;

internal sealed class GetCashDeskHandler(ICashDocumentStore store, ICurrentContext context, TimeProvider time) : IQueryHandler<GetCashDesk, CashDeskDto>
{
    public async Task<CashDeskDto> Handle(GetCashDesk request, CancellationToken cancellationToken)
    {
        var day = request.Day ?? BusinessDay.Today(time);
        var lookups = await store.GetDeskLookupsAsync(context.CompanyId, context.UserId, CashAccess.ViewAll(context), day, cancellationToken)
            .ConfigureAwait(false);
        var accountId = request.TreasuryAccountId ?? lookups.Accounts.FirstOrDefault()?.Id;
        if (accountId is null)
        {
            return new CashDeskDto(lookups, null, []);
        }

        if (lookups.Accounts.All(a => a.Id != accountId))
        {
            throw CashAccess.NotAssigned();
        }

        var (summary, documents) = await store.GetDayAsync(context.CompanyId, accountId.Value, day, cancellationToken).ConfigureAwait(false);
        return new CashDeskDto(lookups, summary, documents);
    }
}

internal sealed class GetCashDocumentHandler(ICashDocumentStore store, ICurrentContext context) : IQueryHandler<GetCashDocument, CashDocumentDetailDto>
{
    public async Task<CashDocumentDetailDto> Handle(GetCashDocument request, CancellationToken cancellationToken)
    {
        var state = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CashAccess.NotFound();
        if (!CashAccess.ViewAll(context)
            && !await store.IsAssignedAsync(state.Document.TreasuryAccountId, context.UserId, state.Document.DocumentDate, false, cancellationToken)
                .ConfigureAwait(false))
        {
            throw CashAccess.NotAssigned();
        }

        return state.Document;
    }
}

// ---------- Ciorna ----------

/// <summary>
/// Creeaza sau modifica o ciorna de casa (F1 3.3, fluxul 4.1 pasii 2–3): serverul calculeaza totalul si echivalentul in lei cu cursul BNR
/// al zilei (sau ultimul anterior) si rotunjirea P7. Soldul initial (Kind 4) nu are linii; suma lui e in <see cref="OpeningAmount"/>.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashCreate)]
[Audited("Fin.Cash.Save")]
public sealed record SaveCashDocument(int? Id, int TreasuryAccountId, CashDocumentKind Kind, DateOnly DocumentDate, int? PartnerId, int? EmployeeId,
    string? PersonName, string? PersonIdDocument, string? Description, string? OpeningAmount, IReadOnlyList<CashLineInput> Lines, byte[]? RowVersion)
    : ICommand<FinanceSaved>, IAccountingDated
{
    public DateOnly AccountingDate => DocumentDate;
}

internal sealed class SaveCashDocumentValidator : AbstractValidator<SaveCashDocument>
{
    public SaveCashDocumentValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Alegeți felul documentului.");
        RuleFor(x => x.TreasuryAccountId).GreaterThan(0).WithMessage("Alegeți casa.");
        RuleFor(x => x).Must(x => (x.PartnerId is null ? 0 : 1) + (x.EmployeeId is null ? 0 : 1) + (string.IsNullOrWhiteSpace(x.PersonName) ? 0 : 1) <= 1)
            .WithName("partnerId").WithMessage("Documentul are cel mult un partener, un angajat sau o persoană fără fișă.");
        RuleFor(x => x.PartnerId).NotNull().When(x => x.Kind == CashDocumentKind.Receipt && string.IsNullOrWhiteSpace(x.PersonName))
            .WithMessage("Chitanța are obligatoriu un plătitor: partener sau nume și act de identitate.");
        RuleFor(x => x.PersonIdDocument).NotEmpty().When(x => x.Kind == CashDocumentKind.Receipt && !string.IsNullOrWhiteSpace(x.PersonName) && x.Id is null)
            .WithMessage("Pentru o persoană fără fișă, chitanța cere actul de identitate.");
        RuleFor(x => x.PersonName).MaximumLength(200);
        RuleFor(x => x.PersonIdDocument).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.OpeningAmount).Must(v => Money.TryParse(v, out var a) && a >= 0).When(x => x.Kind == CashDocumentKind.OpeningBalance)
            .WithMessage("Soldul inițial este o sumă pozitivă sau zero, cu cel mult două zecimale.");
        RuleFor(x => x.Lines).Empty().When(x => x.Kind == CashDocumentKind.OpeningBalance).WithMessage("Soldul inițial nu are linii.");
        RuleFor(x => x.Lines).NotEmpty().When(x => x.Kind != CashDocumentKind.OpeningBalance).WithMessage("Documentul are cel puțin o linie.")
            .Must(l => l.Count <= 200).WithMessage("Documentul are cel mult 200 de linii.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.OperationTypeId).GreaterThan(0).WithMessage("Alegeți tipul de operațiune.");
            line.RuleFor(l => l.Amount).Must(v => Money.TryParse(v, out var a) && a > 0)
                .WithMessage("Suma este pozitivă, cu cel mult două zecimale.");
            line.RuleFor(l => l.SettlementRef).MaximumLength(100);
            line.RuleFor(l => l.Note).MaximumLength(500);
        });
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveCashDocumentHandler(ICashDocumentStore store, ITreasuryAccountStore accounts, IExchangeRateProvider rates,
    IPrecisionService precision, IPersonIdProtector personIds, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveCashDocument, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveCashDocument request, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(context.CompanyId, request.TreasuryAccountId, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound(FinanceErrorCodes.TreasuryAccountNotFound, "Casa nu există în firma curentă.");
        var rate = await rates.GetRateAsync(account.CurrencyCode, request.DocumentDate, cancellationToken).ConfigureAwait(false);
        var policy = await precision.ResolveAsync(context.CompanyId, null, null, cancellationToken).ConfigureAwait(false);
        var ron = (Func<decimal, decimal>)(v => precision.RoundAmount(v, policy));

        IReadOnlyList<CashLineRecord> lines;
        decimal total, totalRon;
        if (request.Kind == CashDocumentKind.OpeningBalance)
        {
            total = Money.ParseOptional(request.OpeningAmount) ?? 0;
            totalRon = ron(total * rate.PerUnit);
            lines = [];
        }
        else
        {
            var amounts = request.Lines.Select(l => Money.ParseOptional(l.Amount) ?? 0).ToList();
            var (lineRon, documentRon) = CashDocumentRules.ToRon(amounts, rate.PerUnit, ron);
            lines = request.Lines.Select((l, i) => new CashLineRecord((short)(i + 1), l.OperationTypeId, amounts[i], lineRon[i], l.CounterTreasuryAccountId,
                Clean(l.SettlementRef), l.CostCenterId, l.ProjectId, Clean(l.Note))).ToList();
            total = amounts.Sum();
            totalRon = documentRon;
        }

        var personName = Clean(request.PersonName);
        var personId = Clean(request.PersonIdDocument);
        var record = new CashDocumentRecord(request.Id, context.CompanyId, request.TreasuryAccountId, request.Kind, request.DocumentDate, request.PartnerId,
            request.EmployeeId, personName, personName is null || personId is null ? null : personIds.Protect(personId),
            KeepPersonId: personId is null, rate.PerUnit, rate.RateDate, total, totalRon, Clean(request.Description), lines, request.RowVersion);
        var saved = await store.SaveAsync(record, !CashAccess.ViewAll(context), context.UserId, cancellationToken).ConfigureAwait(false);
        // Actul de identitate nu intra in audit (NET-18); doar faptul ca exista.
        audit.Describe("Fin.CashDocument", saved.Id, new
        {
            record.TreasuryAccountId, record.Kind, record.DocumentDate, record.PartnerId, record.EmployeeId, HasPersonName = personName is not null,
            record.TotalAmount, record.TotalAmountRon, Lines = lines.Select(l => new { l.OperationTypeId, l.Amount, l.CounterTreasuryAccountId, l.SettlementRef }),
        });
        return saved;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Sterge o ciorna de casa (fara numar, nu a miscat soldul). Un document validat se storneaza.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashCreate)]
[Audited("Fin.Cash.Delete")]
public sealed record DeleteCashDocument(int Id, byte[] RowVersion) : ICommand<int>;

internal sealed class DeleteCashDocumentHandler(ICashDocumentStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<DeleteCashDocument, int>
{
    public async Task<int> Handle(DeleteCashDocument request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(context.CompanyId, request.Id, request.RowVersion, !CashAccess.ViewAll(context), context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.CashDocument", request.Id);
        return request.Id;
    }
}

// ---------- Validare si storno ----------

/// <summary>
/// Valideaza un document de casa (F1 4.1 pasul 4): casierul atribuit cu drept de validare, perioada deschisa, plafoanele de numerar (Blocare
/// fara exceptie prin drepturi; Avertizare doar cu confirmare, auditata), numarul din seria casei, registrul zilei si soldul nenegativ,
/// evenimentul IncasareInregistrata / PlataInregistrata in outbox, in aceeasi tranzactie.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashValidate)]
[Audited("Fin.Cash.Validate")]
public sealed record ValidateCashDocument(int Id, byte[] RowVersion, bool ConfirmWarnings = false) : ICommand<CashValidatedDto>;

internal sealed class ValidateCashDocumentHandler(ICashDocumentStore store, ICashLimitLog limitLog, INumberSeriesService numbers, IPeriodLock periods,
    IOutbox outbox, ICurrentContext context, IAuditScope audit, Settlement.TreasurySettlement settlement) : ICommandHandler<ValidateCashDocument, CashValidatedDto>
{
    private static readonly System.Globalization.CultureInfo Romanian = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");

    public async Task<CashValidatedDto> Handle(ValidateCashDocument request, CancellationToken cancellationToken)
    {
        var state = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CashAccess.NotFound();
        var doc = state.Document;
        if (doc.Status != CashDocumentStatus.Draft)
        {
            throw ErpException.Conflict("FIN_CASH_DOCUMENT_NOT_DRAFT", "Doar o ciornă poate fi validată.");
        }

        // Validarea cere atribuirea cu drept de validare chiar si cu Fin.Cash.ViewAll (F1 8: Validate doar pe casele cu CanValidate).
        if (!await store.IsAssignedAsync(doc.TreasuryAccountId, context.UserId, doc.DocumentDate, true, cancellationToken).ConfigureAwait(false))
        {
            throw CashAccess.NotAssigned();
        }

        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, doc.DocumentDate, cancellationToken).ConfigureAwait(false);

        var confirmed = await CheckLimitsAsync(state, request.ConfirmWarnings, cancellationToken).ConfigureAwait(false);

        var documentType = CashDocumentRules.DocumentTypeOf(doc.Kind);
        string? number = null;
        if (documentType is not null)
        {
            var seriesId = doc.Kind switch
            {
                CashDocumentKind.Receipt => state.ReceiptSeriesId,
                CashDocumentKind.CashIn => state.CashInSeriesId,
                _ => state.CashOutSeriesId,
            };
            number = (await numbers.NextAsync(context.CompanyId, documentType, doc.DocumentDate, cancellationToken, state.BranchId,
                seriesId is null ? null : new NumberSeriesChoice(SeriesId: seriesId)).ConfigureAwait(false)).Formatted;
        }

        var result = await store.ValidateAsync(context.CompanyId, doc.Id, number, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        foreach (var breach in confirmed)
        {
            await limitLog.LogAsync(context.CompanyId, doc.Id, context.UserId, doc, breach, confirmed: true, outsideTransaction: false, cancellationToken)
                .ConfigureAwait(false);
        }

        await outbox.EnqueueAsync(Event(state, documentType ?? "Finance.OpeningBalance", number), cancellationToken).ConfigureAwait(false);
        // Incasarea sau plata catre partener stinge facturile indicate, apoi FIFO dupa profil (F1 4.1 pasul 5, F3 4.2); avertizarile F3
        // (factura neprimita in SPV, litigiu) se confirma cu aceeasi confirmare ca plafoanele.
        if (doc.Kind != CashDocumentKind.OpeningBalance && doc.PartnerId is { } partnerId)
        {
            await settlement.RegisterAsync(new Settlement.TreasurySettlementDocument(context.CompanyId, CashDocumentRules.SettlementSource, doc.Id, number ?? "",
                doc.DocumentDate, doc.CurrencyCode, decimal.Parse(doc.ExchangeRate, System.Globalization.CultureInfo.InvariantCulture),
                state.Lines.Select(l => new Settlement.TreasurySettlementLine(l.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), l.OperationTypeCode,
                    partnerId, l.Amount, l.AmountRon, l.SettlementRef)).ToList(), request.ConfirmWarnings), cancellationToken).ConfigureAwait(false);
        }

        // Plata avansului spre decontare il acorda (F1 4.4 pasul 2).
        if (state.CashAdvanceId is { } advanceId && state.AdvanceDueDate is { } due && state.Lines.Any(l => l.OperationTypeCode == "PLATA-AVANS-DECONTARE"))
        {
            await outbox.EnqueueAsync(new AvansDecontareAcordat(advanceId, doc.EmployeeId ?? 0, Money.ParseOptional(doc.TotalAmount) ?? 0, doc.CurrencyCode, due)
                { CompanyId = context.CompanyId }, cancellationToken).ConfigureAwait(false);
        }
        audit.Describe("Fin.CashDocument", doc.Id, new
        {
            Number = number, doc.Kind, doc.TreasuryAccountId, doc.DocumentDate, doc.TotalAmount,
            ConfirmedWarnings = confirmed.Select(b => new { b.Category, b.DayTotal, b.Limit }),
        });
        return new CashValidatedDto(result.CashDocumentId, result.Number, Money.Format(result.DayClosingBalance), result.RowVersion);
    }

    /// <summary>Plafoanele (F1 3.6): Blocare = refuz jurnalizat; Avertizare = refuz fara confirmare, altfel lista avertizarilor confirmate.</summary>
    private async Task<IReadOnlyList<CashLimitBreach>> CheckLimitsAsync(CashDocumentState state, bool confirm, CancellationToken ct)
    {
        var doc = state.Document;
        if (doc.Kind == CashDocumentKind.OpeningBalance || (doc.PartnerId is null && doc.EmployeeId is null && doc.PersonName is null))
        {
            return [];
        }

        var legal = state.PartnerType is >= 1 and <= 3;
        // Plafonul avansurilor pe zi si persoana nu se aplica deplasarilor (F1 3.6: „fara deplasari”).
        var trip = state.AdvancePurpose is 2 or 3;
        var documentTotals = state.Lines
            .Select(l => (Category: CashDocumentRules.LimitCategory(l.CashLimitCategory, legal), l.AmountRon))
            .Where(l => l.Category is not null && !(trip && l.Category == CashDocumentRules.AdvanceCategory))
            .GroupBy(l => l.Category!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.AmountRon), StringComparer.Ordinal);
        if (documentTotals.Count == 0)
        {
            return [];
        }

        var dayTotals = await store.GetDayTotalsAsync(context.CompanyId, doc.DocumentDate, doc.PartnerId, doc.EmployeeId, doc.PersonName, doc.Id, ct)
            .ConfigureAwait(false);
        var limits = await store.GetLimitsAsync(context.CompanyId, doc.DocumentDate, ct).ConfigureAwait(false);
        var breaches = CashDocumentRules.Breaches(documentTotals, dayTotals, limits);

        var blocked = breaches.FirstOrDefault(b => b.Action == CashLimitAction.Block);
        if (blocked is not null)
        {
            await limitLog.LogAsync(context.CompanyId, doc.Id, context.UserId, doc, blocked, confirmed: false, outsideTransaction: true, ct).ConfigureAwait(false);
            throw ErpException.Conflict("FIN_CASH_LIMIT_EXCEEDED", string.Create(Romanian,
                $"Plafonul de numerar {blocked.Category} de {blocked.Limit:N2} lei pe zi ar fi depășit: total {blocked.DayTotal:N2} lei."));
        }

        var warnings = breaches.Where(b => b.Action == CashLimitAction.Warn).ToList();
        if (warnings.Count > 0 && !confirm)
        {
            var first = warnings[0];
            throw ErpException.Conflict("FIN_CASH_LIMIT_WARNING_UNCONFIRMED", string.Create(Romanian,
                $"Plafonul de avertizare {first.Category} de {first.Limit:N2} lei este depășit (total {first.DayTotal:N2} lei). Confirmați pentru a valida."));
        }

        return warnings;
    }

    private IntegrationEvent Event(CashDocumentState state, string documentType, string? number)
    {
        var doc = state.Document;
        var lines = state.Lines.Select(l => new TreasuryMovementLine(l.Id, l.OperationTypeCode, l.Amount, l.AmountRon, l.SettlementRef,
            l.CounterTreasuryAccountId)).ToList();
        var total = Money.ParseOptional(doc.TotalAmount) ?? 0;
        var totalRon = Money.ParseOptional(doc.TotalAmountRon) ?? 0;
        var rate = decimal.Parse(doc.ExchangeRate, System.Globalization.CultureInfo.InvariantCulture);
        return CashDocumentRules.DirectionOf(doc.Kind) == CashDirection.In
            ? new IncasareInregistrata(doc.TreasuryAccountId, TreasuryAccountKind.Cash, documentType, doc.Id, number, doc.DocumentDate, doc.PartnerId,
                doc.EmployeeId, doc.CurrencyCode, rate, total, totalRon, lines) { CompanyId = context.CompanyId }
            : new PlataInregistrata(doc.TreasuryAccountId, TreasuryAccountKind.Cash, documentType, doc.Id, number, doc.DocumentDate, doc.PartnerId,
                doc.EmployeeId, doc.CurrencyCode, rate, total, totalRon, lines) { CompanyId = context.CompanyId };
    }
}

/// <summary>
/// Storneaza un document de casa validat (F1 3.3; doar storno, fara anulare): document nou cu sume negative, in ziua curenta, validat imediat,
/// cu numar nou din aceeasi serie; ziua initiala nu se modifica (F1-A9). Publica DocumentTrezorerieStornat.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.CashReverse)]
[Audited("Fin.Cash.Reverse")]
public sealed record ReverseCashDocument(int Id, byte[] RowVersion) : ICommand<CashValidatedDto>;

internal sealed class ReverseCashDocumentHandler(ICashDocumentStore store, INumberSeriesService numbers, IPeriodLock periods, IOutbox outbox,
    ICurrentContext context, IAuditScope audit, TimeProvider time, Settlement.ISettlementService settlement) : ICommandHandler<ReverseCashDocument, CashValidatedDto>
{
    public async Task<CashValidatedDto> Handle(ReverseCashDocument request, CancellationToken cancellationToken)
    {
        var state = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw CashAccess.NotFound();
        var doc = state.Document;
        var today = BusinessDay.Today(time);
        if (!CashAccess.ViewAll(context) && !await store.IsAssignedAsync(doc.TreasuryAccountId, context.UserId, today, true, cancellationToken)
                .ConfigureAwait(false))
        {
            throw CashAccess.NotAssigned();
        }

        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, today, cancellationToken).ConfigureAwait(false);
        var documentType = CashDocumentRules.DocumentTypeOf(doc.Kind);
        string? number = null;
        if (documentType is not null && doc.Status == CashDocumentStatus.Validated && doc.ReversedById is null && doc.ReversalOfId is null)
        {
            var seriesId = doc.Kind switch
            {
                CashDocumentKind.Receipt => state.ReceiptSeriesId,
                CashDocumentKind.CashIn => state.CashInSeriesId,
                _ => state.CashOutSeriesId,
            };
            number = (await numbers.NextAsync(context.CompanyId, documentType, today, cancellationToken, state.BranchId,
                seriesId is null ? null : new NumberSeriesChoice(SeriesId: seriesId)).ConfigureAwait(false)).Formatted;
        }

        var result = await store.ReverseAsync(context.CompanyId, doc.Id, request.RowVersion, today, number, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        await outbox.EnqueueAsync(new DocumentTrezorerieStornat(doc.Id, result.CashDocumentId, today) { CompanyId = context.CompanyId }, cancellationToken)
            .ConfigureAwait(false);
        // Facturile stinse de document redevin deschise (F3 4.2 pasul 6).
        await settlement.ReversePaymentAsync(context.CompanyId, CashDocumentRules.SettlementSource, doc.Id, null, today, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CashDocument", doc.Id, new { ReversalId = result.CashDocumentId, ReversalNumber = result.Number, Day = today });
        return new CashValidatedDto(result.CashDocumentId, result.Number, Money.Format(result.DayClosingBalance), result.RowVersion);
    }
}
