using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Bank;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;
using FluentValidation;

namespace Erp.Finance.Application.Bank;

/// <summary>Sursa explicatiei unei linii (F2 3.4): ordin de plata, factura, tranzit, card, regula, manual.</summary>
public enum BankMatchSource : byte
{
    PaymentOrder = 1,
    Invoice = 2,
    Transit = 3,
    Card = 4,
    Rule = 5,
    Manual = 6,
}

public sealed record MatchedLine(long Id, BankMatchSource Source, bool AutoValidate, byte[] RowVersion, DateOnly BookingDate, ContinuityStatus ContinuityStatus);

public sealed record MatchingRun(IReadOnlyList<MatchedLine> Matched, int Considered, int Proposed);

/// <summary>Rezultatul potrivirii automate: liniile luate in calcul, explicate pe surse, propuse si validate automat.</summary>
public sealed record MatchingResultDto(int Considered, int Explained, int Proposed, int AutoValidated, IReadOnlyDictionary<BankMatchSource, int> BySource);

public sealed record MatchingRuleDto(int Id, int Priority, string Name, int? TreasuryAccountId, byte? Direction, string? CounterpartyIban,
    string? CounterpartyFiscalCode, string? RemittanceContains, string? BankTransactionCode, string? AmountFrom, string? AmountTo, int OperationTypeId,
    string OperationTypeCode, int? PartnerId, string? PartnerName, int? CostCenterId, int? ProjectId, string? NoteTemplate, bool AutoValidate,
    long? CreatedFromLineId, bool IsActive, byte[] RowVersion)
{
    /// <summary>Liniile de extras explicate de regula (macheta f2-04, coloana Folosiri).</summary>
    public int UsageCount { get; init; }
}

public sealed record MatchingRuleRecord(int? Id, int Priority, string Name, int? TreasuryAccountId, byte? Direction, string? CounterpartyIban,
    string? CounterpartyFiscalCode, string? RemittanceContains, string? BankTransactionCode, decimal? AmountFrom, decimal? AmountTo, int OperationTypeId,
    int? PartnerId, int? CostCenterId, int? ProjectId, string? NoteTemplate, bool AutoValidate, long? CreatedFromLineId, bool IsActive, byte[]? RowVersion);

public sealed record CardAcquirerDto(int Id, string Code, string Name, string? CounterpartyIban, bool IsActive, byte[] RowVersion);

public enum CardSettlementStatus : byte
{
    Pending = 1,
    Settled = 2,
    SettledWithDifference = 3,
}

public sealed record CardSettlementDto(int Id, string AcquirerCode, DateOnly SalesDate, string GrossAmount, DateOnly ExpectedSettlementDate, string SourceModule,
    string SourceRef, string? SettledAmount, string? FeeAmount, CardSettlementStatus Status, long? BankStatementLineId, string? Note, byte[] RowVersion);

public enum TransitKind : byte
{
    Cash = 1,
    InternalTransfer = 2,
    Card = 3,
}

public sealed record TransitItemDto(TransitKind Kind, int? TreasuryAccountId, int? SourceAccountId, DateOnly ItemDate, string Amount, string? Reference,
    long SourceId, int AgeDays);

/// <summary>Accesul la date pentru potrivire, reguli, carduri si tranzit (NET-06).</summary>
public interface IBankMatchingStore
{
    Task<MatchingRun> RunAsync(int companyId, int? bankStatementId, int? treasuryAccountId, decimal feeTolerancePercent, int userId, CancellationToken ct);

    Task<IReadOnlyList<MatchingRuleDto>> ListRulesAsync(int companyId, CancellationToken ct);

    Task<FinanceSaved> SaveRuleAsync(int companyId, MatchingRuleRecord rule, int userId, CancellationToken ct);

    Task<IReadOnlyList<CardAcquirerDto>> ListAcquirersAsync(int companyId, CancellationToken ct);

    Task<FinanceSaved> SaveAcquirerAsync(int companyId, int? id, string code, string name, string? iban, bool isActive, byte[]? rowVersion, int userId,
        CancellationToken ct);

    Task<int> RegisterCardSettlementAsync(int companyId, string acquirerCode, DateOnly salesDate, decimal grossAmount, DateOnly expectedSettlementDate,
        string sourceModule, string sourceRef, int userId, CancellationToken ct);

    Task<IReadOnlyList<CardSettlementDto>> ListCardSettlementsAsync(int companyId, CardSettlementStatus? status, CancellationToken ct);

    Task<byte[]> ResolveCardSettlementAsync(int companyId, int id, string note, byte[] rowVersion, int userId, CancellationToken ct);

    Task<IReadOnlyList<TransitItemDto>> ListTransitAsync(int companyId, int? treasuryAccountId, DateOnly asOf, CancellationToken ct);
}

/// <summary>
/// Potrivirea automata (F2 4.3) si validarea automata dupa Fin.Bank.AutoValidateLevel: de la nivelul 1 se valideaza confirmarile de OP,
/// perechile interne, decontarile de card si liniile explicate de reguli cu validare automata; nu se valideaza automat liniile unui extras cu
/// gol neacceptat sau dintr-o perioada inchisa (raman Explicate). Platile catre parteneri fara OP nu se explica automat niciodata.
/// </summary>
public sealed class BankMatchingEngine(IBankMatchingStore store, BankLineValidation validation, IFinanceSettings settings, IPeriodLock periods,
    ICurrentContext context)
{
    public async Task<MatchingResultDto> RunAsync(int? bankStatementId, int? treasuryAccountId, CancellationToken ct)
    {
        var tolerance = await settings.CardFeeTolerancePercentAsync(context.CompanyId, ct).ConfigureAwait(false);
        var run = await store.RunAsync(context.CompanyId, bankStatementId, treasuryAccountId, tolerance, context.UserId, ct).ConfigureAwait(false);
        var level = await settings.AutoValidateLevelAsync(context.CompanyId, ct).ConfigureAwait(false);
        var eligible = new List<BankLineVersion>();
        if (level >= 1)
        {
            var closed = new Dictionary<DateOnly, bool>();
            foreach (var line in run.Matched.Where(m => m.ContinuityStatus != ContinuityStatus.Gap
                         && (m.Source is BankMatchSource.PaymentOrder or BankMatchSource.Transit or BankMatchSource.Card || m.AutoValidate)))
            {
                if (!closed.TryGetValue(line.BookingDate, out var isClosed))
                {
                    isClosed = await periods.IsClosedAsync(context.CompanyId, line.BookingDate, ct).ConfigureAwait(false);
                    closed[line.BookingDate] = isClosed;
                }

                if (!isClosed)
                {
                    eligible.Add(new BankLineVersion(line.Id, line.RowVersion));
                }
            }
        }

        if (eligible.Count > 0)
        {
            await validation.ValidateAsync(eligible, ct).ConfigureAwait(false);
        }

        var bySource = run.Matched.GroupBy(m => m.Source).ToDictionary(g => g.Key, g => g.Count());
        return new MatchingResultDto(run.Considered, run.Matched.Count, run.Proposed, eligible.Count, bySource);
    }
}

/// <summary>Ruleaza potrivirea automata pe un extras sau pe liniile nepotrivite ale unui cont (F2 4.3; la cerere si dupa import).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementReconcile)]
[Audited("Fin.BankStatement.AutoMatch")]
public sealed record RunAutoMatching(int? BankStatementId, int? TreasuryAccountId = null) : ICommand<MatchingResultDto>;

internal sealed class RunAutoMatchingValidator : AbstractValidator<RunAutoMatching>
{
    public RunAutoMatchingValidator() =>
        RuleFor(x => x).Must(x => x.BankStatementId is not null || x.TreasuryAccountId is not null).WithName("bankStatementId")
            .WithMessage("Alegeți extrasul sau contul.");
}

internal sealed class RunAutoMatchingHandler(BankMatchingEngine engine, IAuditScope audit) : ICommandHandler<RunAutoMatching, MatchingResultDto>
{
    public async Task<MatchingResultDto> Handle(RunAutoMatching request, CancellationToken cancellationToken)
    {
        var result = await engine.RunAsync(request.BankStatementId, request.TreasuryAccountId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.BankStatement", request.BankStatementId, new
        {
            request.TreasuryAccountId, result.Considered, result.Explained, result.Proposed, result.AutoValidated,
        });
        return result;
    }
}

// ---------- Reguli de potrivire ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetMatchingRules : IQuery<IReadOnlyList<MatchingRuleDto>>;

internal sealed class GetMatchingRulesHandler(IBankMatchingStore store, ICurrentContext context) : IQueryHandler<GetMatchingRules, IReadOnlyList<MatchingRuleDto>>
{
    public Task<IReadOnlyList<MatchingRuleDto>> Handle(GetMatchingRules request, CancellationToken cancellationToken) =>
        store.ListRulesAsync(context.CompanyId, cancellationToken);
}

/// <summary>Salveaza o regula de potrivire (F2 3.5): cel putin o conditie; validarea automata doar pentru tipurile fara partener.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankRuleManage)]
[Audited("Fin.MatchingRule.Save")]
public sealed record SaveMatchingRule(int? Id, int Priority, string Name, int? TreasuryAccountId, byte? Direction, string? CounterpartyIban,
    string? CounterpartyFiscalCode, string? RemittanceContains, string? BankTransactionCode, string? AmountFrom, string? AmountTo, int OperationTypeId,
    int? PartnerId, int? CostCenterId, int? ProjectId, string? NoteTemplate, bool AutoValidate, bool IsActive, byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SaveMatchingRuleValidator : AbstractValidator<SaveMatchingRule>
{
    public SaveMatchingRuleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200);
        RuleFor(x => x.Priority).InclusiveBetween(1, 100_000);
        RuleFor(x => x.Direction).Must(d => d is null or 1 or 2).WithMessage("Sensul este intrare, ieșire sau oricare.");
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.CounterpartyIban) || !string.IsNullOrWhiteSpace(x.CounterpartyFiscalCode)
                || !string.IsNullOrWhiteSpace(x.RemittanceContains) || !string.IsNullOrWhiteSpace(x.BankTransactionCode))
            .WithName("counterpartyIban").WithMessage("Regula are cel puțin o condiție: IBAN, CUI, text din detalii sau codul tranzacției.");
        RuleFor(x => x.AmountFrom).Must(a => a is null || Money.TryParse(a, out var v) && v >= 0).WithMessage("Suma minimă nu este validă.");
        RuleFor(x => x.AmountTo).Must(a => a is null || Money.TryParse(a, out var v) && v >= 0).WithMessage("Suma maximă nu este validă.");
        RuleFor(x => x.RemittanceContains).MaximumLength(200);
        RuleFor(x => x.NoteTemplate).MaximumLength(500);
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveMatchingRuleHandler(IBankMatchingStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveMatchingRule, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveMatchingRule request, CancellationToken cancellationToken)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var rule = new MatchingRuleRecord(request.Id, request.Priority, request.Name.Trim(), request.TreasuryAccountId, request.Direction,
            BankStatementRules.NormalizeIban(request.CounterpartyIban), Clean(request.CounterpartyFiscalCode)?.ToUpperInvariant().Replace("RO", "", StringComparison.Ordinal),
            Clean(request.RemittanceContains), Clean(request.BankTransactionCode), Money.ParseOptional(request.AmountFrom), Money.ParseOptional(request.AmountTo),
            request.OperationTypeId, request.PartnerId, request.CostCenterId, request.ProjectId, Clean(request.NoteTemplate), request.AutoValidate, null,
            request.IsActive, request.RowVersion);
        var saved = await store.SaveRuleAsync(context.CompanyId, rule, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.MatchingRule", saved.Id, new { rule.Name, rule.Priority, rule.OperationTypeId, rule.AutoValidate, rule.IsActive });
        return saved;
    }
}

/// <summary>
/// „Tine minte pentru data viitoare” (F2 4.4 pasul 3): o regula din explicatia unei linii cu o singura parte: conditia e IBAN-ul
/// contrapartidei (sau CUI-ul din detalii, sau textul dat), actiunea e tipul si partenerul partii.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementReconcile)]
[Audited("Fin.MatchingRule.Remember")]
public sealed record RememberMatchingRule(long BankStatementLineId, string? RemittanceContains = null, bool AutoValidate = false) : ICommand<FinanceSaved>;

internal sealed class RememberMatchingRuleHandler(IBankStatementLineStore lines, IBankMatchingStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<RememberMatchingRule, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(RememberMatchingRule request, CancellationToken cancellationToken)
    {
        var (found, parts) = await lines.GetAsync(context.CompanyId, [request.BankStatementLineId], cancellationToken).ConfigureAwait(false);
        var line = found.SingleOrDefault() ?? throw BankLineRules.NotFound();
        if (parts.Count != 1)
        {
            throw ErpException.Conflict("FIN_BANK_RULE_NEEDS_ONE_PART", "Se ține minte doar explicația unei linii cu o singură parte.");
        }

        var part = parts[0];
        var text = string.IsNullOrWhiteSpace(request.RemittanceContains) ? null : request.RemittanceContains.Trim();
        var fiscalCode = line.CounterpartyIban is null && text is null ? RemittanceRules.ExtractFiscalCode(line.RemittanceInfo) : null;
        if (line.CounterpartyIban is null && text is null && fiscalCode is null)
        {
            throw ErpException.Conflict("FIN_BANK_RULE_NEEDS_CONDITION", "Linia nu are IBAN sau CUI; dați textul din detalii după care se recunoaște.");
        }

        var name = $"{part.OperationTypeName} – {line.CounterpartyName ?? line.PartnerName ?? text ?? fiscalCode}";
        var rule = new MatchingRuleRecord(null, 1000, name.Length <= 200 ? name : name[..200], line.TreasuryAccountId, line.Amount > 0 ? (byte)1 : (byte)2,
            line.CounterpartyIban, fiscalCode, text, null, null, null, part.OperationTypeId, part.PartnerId, part.CostCenterId, part.ProjectId, part.Note,
            request.AutoValidate, line.Id, true, null);
        var saved = await store.SaveRuleAsync(context.CompanyId, rule, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.MatchingRule", saved.Id, new { line.Id, rule.OperationTypeId, rule.AutoValidate });
        return saved;
    }
}

// ---------- Carduri ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetCardAcquirers : IQuery<IReadOnlyList<CardAcquirerDto>>;

internal sealed class GetCardAcquirersHandler(IBankMatchingStore store, ICurrentContext context) : IQueryHandler<GetCardAcquirers, IReadOnlyList<CardAcquirerDto>>
{
    public Task<IReadOnlyList<CardAcquirerDto>> Handle(GetCardAcquirers request, CancellationToken cancellationToken) =>
        store.ListAcquirersAsync(context.CompanyId, cancellationToken);
}

/// <summary>Procesatorul de carduri al firmei (F2 3.7): codul folosit de magazine si IBAN-ul de pe care vin decontarile.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankFormatManage)]
[Audited("Fin.CardAcquirer.Save")]
public sealed record SaveCardAcquirer(int? Id, string Code, string Name, string? CounterpartyIban, bool IsActive, byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SaveCardAcquirerValidator : AbstractValidator<SaveCardAcquirer>
{
    public SaveCardAcquirerValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.").Matches(SettingsRules.CodePattern)
            .WithMessage("Codul are 1–30 de caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200);
        RuleFor(x => x.CounterpartyIban).Must(PaymentOrderRules.IsValidIban).When(x => !string.IsNullOrWhiteSpace(x.CounterpartyIban))
            .WithMessage("IBAN-ul nu este valid.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveCardAcquirerHandler(IBankMatchingStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveCardAcquirer, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveCardAcquirer request, CancellationToken cancellationToken)
    {
        var saved = await store.SaveAcquirerAsync(context.CompanyId, request.Id, request.Code.Trim(), request.Name.Trim(),
            BankStatementRules.NormalizeIban(request.CounterpartyIban), request.IsActive, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CardAcquirer", saved.Id, new { Code = request.Code.Trim(), request.IsActive });
        return saved;
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetCardSettlements(CardSettlementStatus? Status = null) : IQuery<IReadOnlyList<CardSettlementDto>>;

internal sealed class GetCardSettlementsHandler(IBankMatchingStore store, ICurrentContext context) : IQueryHandler<GetCardSettlements, IReadOnlyList<CardSettlementDto>>
{
    public Task<IReadOnlyList<CardSettlementDto>> Handle(GetCardSettlements request, CancellationToken cancellationToken) =>
        store.ListCardSettlementsAsync(context.CompanyId, request.Status, cancellationToken);
}

/// <summary>
/// Decontarea de card asteptata pentru vanzarile online prin procesator (F2 3.7, introdusa manual; cele din POS vin prin ICardSettlementWriter),
/// cu data asteptata dupa Fin.Card.SettlementDays zile lucratoare.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementReconcile)]
[Audited("Fin.CardSettlement.Register")]
public sealed record RegisterCardSettlement(string AcquirerCode, DateOnly SalesDate, string GrossAmount, string SourceRef) : ICommand<int>;

internal sealed class RegisterCardSettlementValidator : AbstractValidator<RegisterCardSettlement>
{
    public RegisterCardSettlementValidator()
    {
        RuleFor(x => x.AcquirerCode).NotEmpty().MaximumLength(30);
        RuleFor(x => x.GrossAmount).Must(a => Money.TryParse(a, out var v) && v > 0).WithMessage("Suma brută este pozitivă.");
        RuleFor(x => x.SourceRef).NotEmpty().WithMessage("Referința vânzărilor este obligatorie.").MaximumLength(100);
    }
}

internal sealed class RegisterCardSettlementHandler(ICardSettlementWriter writer, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<RegisterCardSettlement, int>
{
    public async Task<int> Handle(RegisterCardSettlement request, CancellationToken cancellationToken)
    {
        var id = await writer.RegisterExpectedAsync(new CardSettlementRequest(context.CompanyId, request.AcquirerCode.Trim(), request.SalesDate,
            Money.ParseOptional(request.GrossAmount) ?? 0, "Manual", request.SourceRef.Trim()), cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CardSettlement", id, new { request.AcquirerCode, request.SalesDate, request.GrossAmount });
        return id;
    }
}

/// <summary>Explica o decontare cu diferenta peste toleranta (F2 4.6 pasul 3): retur pe card, tranzactie contestata.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementReconcile)]
[Audited("Fin.CardSettlement.Resolve")]
public sealed record ResolveCardSettlement(int Id, string Note, byte[] RowVersion) : ICommand<byte[]>;

internal sealed class ResolveCardSettlementValidator : AbstractValidator<ResolveCardSettlement>
{
    public ResolveCardSettlementValidator() => RuleFor(x => x.Note).NotEmpty().WithMessage("Explicați diferența.").MaximumLength(500);
}

internal sealed class ResolveCardSettlementHandler(IBankMatchingStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<ResolveCardSettlement, byte[]>
{
    public async Task<byte[]> Handle(ResolveCardSettlement request, CancellationToken cancellationToken)
    {
        var version = await store.ResolveCardSettlementAsync(context.CompanyId, request.Id, request.Note.Trim(), request.RowVersion, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.CardSettlement", request.Id, new { Note = request.Note.Trim() });
        return version;
    }
}

/// <summary>Decontarile asteptate pentru alte module (F2 6, ICardSettlementWriter): POS-ul (02.10) la inchiderea zilei.</summary>
public sealed class CardSettlementWriter(IBankMatchingStore store, IFinanceSettings settings, ICurrentContext context) : ICardSettlementWriter
{
    public async Task<int> RegisterExpectedAsync(CardSettlementRequest request, CancellationToken ct)
    {
        var days = await settings.CardSettlementDaysAsync(request.CompanyId, ct).ConfigureAwait(false);
        var expected = Domain.Treasury.AdvanceRules.SettlementDueDate(request.SalesDate, days);
        return await store.RegisterCardSettlementAsync(request.CompanyId, request.AcquirerCode, request.SalesDate, request.GrossAmount, expected,
            request.SourceModule, request.SourceRef, context.UserId, ct).ConfigureAwait(false);
    }
}

// ---------- Tranzit ----------

/// <summary>Sumele in tranzit (F2 3.7): numerar depus / ridicat fara pereche, viramente interne cu o singura parte, carduri in asteptare.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetTransitItems(int? TreasuryAccountId = null) : IQuery<IReadOnlyList<TransitItemDto>>;

internal sealed class GetTransitItemsHandler(IBankMatchingStore store, ICurrentContext context, TimeProvider time)
    : IQueryHandler<GetTransitItems, IReadOnlyList<TransitItemDto>>
{
    public Task<IReadOnlyList<TransitItemDto>> Handle(GetTransitItems request, CancellationToken cancellationToken) =>
        store.ListTransitAsync(context.CompanyId, request.TreasuryAccountId, BusinessDay.Today(time), cancellationToken);
}
