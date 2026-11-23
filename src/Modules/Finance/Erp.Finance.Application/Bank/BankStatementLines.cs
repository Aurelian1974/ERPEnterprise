using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Domain.Treasury;
using FluentValidation;

namespace Erp.Finance.Application.Bank;

/// <summary>O parte a explicatiei unei linii (F2 3.4): suma cu semn (intrare +, iesire −), ce stinge si cu ce pereche.</summary>
public sealed record BankLinePartInput(int OperationTypeId, string Amount, int? PartnerId, int? EmployeeId, string? SettlementRef, int? CashDocumentId,
    int? CounterTreasuryAccountId, string? BudgetObligationRef, int? CostCenterId, int? ProjectId, string? Note, int? PaymentOrderId = null,
    int? CardSettlementExpectedId = null);

public sealed record BankLinePartRecord(short PartNumber, int OperationTypeId, decimal Amount, int? PartnerId, int? EmployeeId, string? SettlementRef,
    int? CashDocumentId, int? CounterTreasuryAccountId, string? BudgetObligationRef, int? CostCenterId, int? ProjectId, string? Note)
{
    /// <summary>OP-ul pe care partea il confirma (F2 3.4); o astfel de parte nu mai publica o plata.</summary>
    public int? PaymentOrderId { get; init; }

    /// <summary>Decontarea de card asteptata pe care partea BANK-DECONTARE-CARD o explica (F2 3.7).</summary>
    public int? CardSettlementExpectedId { get; init; }
}

/// <summary>Un OP confirmat la validarea liniilor (F2 4.2 pasul 7): linia de confirmare si data ei.</summary>
public sealed record PaymentOrderConfirmed(int PaymentOrderId, long BankStatementLineId, decimal Amount, DateOnly BookingDate);

public sealed record BankLineVersion(long Id, byte[] RowVersion);

public sealed record BankLineSaved(long Id, byte[] RowVersion);

public sealed record BankLinePartDto(short PartNumber, int OperationTypeId, string OperationTypeCode, string OperationTypeName, string Amount, int? PartnerId,
    string? PartnerName, int? EmployeeId, string? SettlementRef, int? PaymentOrderId, int? CashDocumentId, int? CounterTreasuryAccountId,
    string? BudgetObligationRef, int? CostCenterId, int? ProjectId, string? Note);

/// <summary>O linie de extras cu explicatia ei (panoul de reconciliere din macheta f2-02).</summary>
public sealed record BankLineDetailDto(long Id, int BankStatementId, string? StatementNo, int LineNumber, DateOnly BookingDate, DateOnly? ValueDate,
    string Amount, string? CounterpartyName, string? CounterpartyIban, string? RemittanceInfo, string? EndToEndId, string BankReference,
    BankLineMatchStatus MatchStatus, int? PartnerId, string? PartnerName, int TreasuryAccountId, string CurrencyCode, DateTime? ValidatedAt,
    IReadOnlyList<BankLinePartDto> Parts, byte[] RowVersion)
{
    /// <summary>Propunerile motorului de potrivire pentru linie (F2 4.3 pasul 6), cu scor.</summary>
    public IReadOnlyList<BankSuggestionDto> Suggestions { get; init; } = [];
}

/// <summary>O propunere pentru o linie: Kind 1 = OP neconfirmat al partenerului identificat.</summary>
public sealed record BankSuggestionDto(byte Kind, int? PaymentOrderId, int? PartnerId, string Amount, byte Score, string Description);

public sealed record BankLineRow(long Id, int BankStatementId, string? StatementNo, int LineNumber, DateOnly BookingDate, DateOnly? ValueDate, decimal Amount,
    string? CounterpartyName, string? CounterpartyIban, string? RemittanceInfo, string? EndToEndId, string BankReference, BankLineMatchStatus MatchStatus,
    int? PartnerId, string? PartnerName, int TreasuryAccountId, Contracts.Treasury.TreasuryAccountKind AccountKind, string CurrencyCode,
    DateTime? ValidatedAt, byte[] RowVersion)
{
    /// <summary>Continuitatea extrasului liniei (F2 3.3): un gol neacceptat opreste validarea.</summary>
    public ContinuityStatus ContinuityStatus { get; init; } = ContinuityStatus.Ok;
}

/// <summary>Rezultatul validarii unei linii dintr-un grup (F2 4.4 pasul 4): validata, sau motivul refuzului, fara sa le opreasca pe celelalte.</summary>
public sealed record BankLineValidationResult(long Id, bool Validated, string? ErrorCode, string? Message, byte[]? RowVersion);

public sealed record BankLinePartRow(long BankStatementLineId, short PartNumber, int OperationTypeId, string OperationTypeCode, string OperationTypeName,
    decimal Amount, int? PartnerId, string? PartnerName, int? EmployeeId, string? SettlementRef, int? PaymentOrderId, int? CashDocumentId,
    int? CounterTreasuryAccountId, string? BudgetObligationRef, int? CostCenterId, int? ProjectId, string? Note, bool RequiresPartner);

/// <summary>Accesul la date pentru liniile de extras (fin.usp_BankStatementLine_*, NET-06).</summary>
public interface IBankStatementLineStore
{
    /// <summary>Liniile firmei cerute, cu partile lor; cele inexistente sau ale altei firme lipsesc.</summary>
    Task<(IReadOnlyList<BankLineRow> Lines, IReadOnlyList<BankLinePartRow> Parts)> GetAsync(int companyId, IReadOnlyCollection<long> ids, CancellationToken ct);

    Task<IReadOnlyList<BankSuggestionDto>> GetSuggestionsAsync(int companyId, long lineId, CancellationToken ct);

    Task<byte[]> ExplainAsync(int companyId, long lineId, IReadOnlyList<BankLinePartRecord> parts, byte[] rowVersion, int userId, CancellationToken ct);

    Task<(IReadOnlyList<BankLineSaved> Saved, IReadOnlyList<PaymentOrderConfirmed> Confirmed, IReadOnlyList<(int PaymentOrderId, long BankStatementLineId)> Rejected)>
        ValidateAsync(int companyId, IReadOnlyList<BankLineVersion> lines, decimal cardFeeTolerancePercent, int userId, CancellationToken ct);

    Task<byte[]> UnvalidateAsync(int companyId, long lineId, byte[] rowVersion, int userId, CancellationToken ct);
}

/// <summary>Regulile liniilor de extras, fara acces la date.</summary>
public static class BankLineRules
{
    public const string DocumentType = "Finance.BankStatementLine";

    public static ErpException NotFound() => ErpException.NotFound("FIN_BANK_LINE_NOT_FOUND", "Linia de extras nu există.");

    /// <summary>Numarul liniei in evenimente (F2 5.1): numarul extrasului (sau Id-ul lui) / numarul liniei.</summary>
    public static string Number(BankLineRow line) => $"{line.StatementNo ?? line.BankStatementId.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{line.LineNumber}";

    public static BankLineDetailDto Detail(BankLineRow l, IEnumerable<BankLinePartRow> parts) => new(l.Id, l.BankStatementId, l.StatementNo, l.LineNumber,
        l.BookingDate, l.ValueDate, Money.Format(l.Amount), l.CounterpartyName, l.CounterpartyIban, l.RemittanceInfo, l.EndToEndId, l.BankReference,
        l.MatchStatus, l.PartnerId, l.PartnerName, l.TreasuryAccountId, l.CurrencyCode, l.ValidatedAt,
        parts.Select(p => new BankLinePartDto(p.PartNumber, p.OperationTypeId, p.OperationTypeCode, p.OperationTypeName, Money.Format(p.Amount), p.PartnerId,
            p.PartnerName, p.EmployeeId, p.SettlementRef, p.PaymentOrderId, p.CashDocumentId, p.CounterTreasuryAccountId, p.BudgetObligationRef,
            p.CostCenterId, p.ProjectId, p.Note)).ToList(), l.RowVersion);
}

/// <summary>Linia de extras cu partile ei (panoul de reconciliere, macheta f2-02).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetBankStatementLine(long Id) : IQuery<BankLineDetailDto>;

internal sealed class GetBankStatementLineHandler(IBankStatementLineStore store, ICurrentContext context) : IQueryHandler<GetBankStatementLine, BankLineDetailDto>
{
    public async Task<BankLineDetailDto> Handle(GetBankStatementLine request, CancellationToken cancellationToken)
    {
        var (lines, parts) = await store.GetAsync(context.CompanyId, [request.Id], cancellationToken).ConfigureAwait(false);
        var line = lines.SingleOrDefault() ?? throw BankLineRules.NotFound();
        return BankLineRules.Detail(line, parts) with
        {
            Suggestions = await store.GetSuggestionsAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false),
        };
    }
}

/// <summary>
/// Explica o linie de extras (F2 3.4 si 4.4): partile inlocuiesc explicatia anterioara si acopera exact suma liniei (51205); fara parti,
/// linia revine la Nepotrivita. Datele bancii raman neatinse; o linie validata nu se mai explica (51211).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementReconcile)]
[Audited("Fin.BankStatementLine.Explain")]
public sealed record ExplainBankStatementLine(long Id, IReadOnlyList<BankLinePartInput> Parts, byte[] RowVersion) : ICommand<BankLineSaved>;

internal sealed class ExplainBankStatementLineValidator : AbstractValidator<ExplainBankStatementLine>
{
    public ExplainBankStatementLineValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty().WithMessage("Lipsește versiunea înregistrării.");
        RuleFor(x => x.Parts).NotNull().Must(p => p.Count <= 50).WithMessage("O linie are cel mult 50 de părți.");
        RuleForEach(x => x.Parts).ChildRules(part =>
        {
            part.RuleFor(p => p.Amount).Must(a => Money.TryParse(a, out var v) && v != 0).WithMessage("Suma părții este un număr nenul cu cel mult 2 zecimale.");
            part.RuleFor(p => p.SettlementRef).MaximumLength(100);
            part.RuleFor(p => p.BudgetObligationRef).MaximumLength(50);
            part.RuleFor(p => p.Note).MaximumLength(500);
        });
    }
}

internal sealed class ExplainBankStatementLineHandler(IBankStatementLineStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ExplainBankStatementLine, BankLineSaved>
{
    public async Task<BankLineSaved> Handle(ExplainBankStatementLine request, CancellationToken cancellationToken)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var parts = request.Parts.Select((p, i) => new BankLinePartRecord((short)(i + 1), p.OperationTypeId, Money.ParseOptional(p.Amount) ?? 0, p.PartnerId,
            p.EmployeeId, Clean(p.SettlementRef), p.CashDocumentId, p.CounterTreasuryAccountId, Clean(p.BudgetObligationRef), p.CostCenterId, p.ProjectId,
            Clean(p.Note)) { PaymentOrderId = p.PaymentOrderId, CardSettlementExpectedId = p.CardSettlementExpectedId }).ToList();
        var version = await store.ExplainAsync(context.CompanyId, request.Id, parts, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.BankStatementLine", request.Id, new
        {
            Parts = parts.Select(p => new
            {
                p.OperationTypeId, p.Amount, p.PartnerId, p.EmployeeId, p.SettlementRef, p.PaymentOrderId, p.CashDocumentId, p.CounterTreasuryAccountId,
            }),
        });
        return new BankLineSaved(request.Id, version);
    }
}

/// <summary>
/// Valideaza linii explicate (F2 4.4; SP-08), fiecare cu rezultatul ei: liniile care nu pot trece (inexistente, modificate intre timp,
/// neexplicate, pe un extras cu gol, cu parti care nu acopera suma, plata catre partener fara OP, perioada inchisa) sunt raportate si scoase din
/// grup; celelalte se valideaza pe seturi, in aceeasi tranzactie cu evenimentele lor. O singura linie refuzata intoarce eroarea ei.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementValidate)]
[Audited("Fin.BankStatementLine.Validate")]
public sealed record ValidateBankStatementLines(IReadOnlyList<BankLineVersion> Lines) : ICommand<IReadOnlyList<BankLineValidationResult>>;

internal sealed class ValidateBankStatementLinesValidator : AbstractValidator<ValidateBankStatementLines>
{
    public ValidateBankStatementLinesValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Alegeți liniile de validat.")
            .Must(l => l.Count <= 500).WithMessage("Se validează cel mult 500 de linii odată.")
            .Must(l => l.Select(x => x.Id).Distinct().Count() == l.Count).WithMessage("O linie apare de două ori.");
    }
}

internal sealed class ValidateBankStatementLinesHandler(BankLineValidation validation, IAuditScope audit)
    : ICommandHandler<ValidateBankStatementLines, IReadOnlyList<BankLineValidationResult>>
{
    public async Task<IReadOnlyList<BankLineValidationResult>> Handle(ValidateBankStatementLines request, CancellationToken cancellationToken)
    {
        var results = await validation.ValidateEachAsync(request.Lines, cancellationToken).ConfigureAwait(false);
        if (results is [{ Validated: false } single])
        {
            throw new ErpException(BankLineValidation.KindOf(single.ErrorCode!), single.ErrorCode!, single.Message!);
        }

        audit.Describe("Fin.BankStatementLine", results.Count == 1 ? results[0].Id : null, new
        {
            Validated = results.Where(r => r.Validated).Select(r => r.Id),
            Refused = results.Where(r => !r.Validated).Select(r => new { r.Id, r.ErrorCode }),
        });
        return results;
    }
}

/// <summary>
/// Validarea liniilor si evenimentele ei, comune validarii manuale si celei automate dupa potrivire (F2 4.3 si 4.4): perioadele, validarea pe
/// seturi, IncasareInregistrata / PlataInregistrata (partile pe OP nu republica plata), OrdinPlataConfirmat si OrdinPlataRespins.
/// </summary>
public sealed class BankLineValidation(IBankStatementLineStore store, IPeriodLock periods, IExchangeRateProvider rates, IPrecisionService precision,
    IFinanceSettings settings, IOutbox outbox, ICurrentContext context, Settlement.TreasurySettlement settlement)
{
    /// <summary>
    /// Valideaza fiecare linie care poate trece si raporteaza motivul pentru celelalte (F2 4.4 pasul 4); procedura primeste doar liniile bune,
    /// deci o eroare a ei e o concurenta si anuleaza grupul.
    /// </summary>
    public async Task<IReadOnlyList<BankLineValidationResult>> ValidateEachAsync(IReadOnlyList<BankLineVersion> versions, CancellationToken cancellationToken)
    {
        var (lines, parts) = await store.GetAsync(context.CompanyId, versions.Select(l => l.Id).ToList(), cancellationToken).ConfigureAwait(false);
        var byId = lines.ToDictionary(l => l.Id);
        var byLine = parts.ToLookup(p => p.BankStatementLineId);
        var closed = new Dictionary<DateOnly, bool>();
        var results = new Dictionary<long, BankLineValidationResult>();
        var passing = new List<BankLineVersion>();
        foreach (var version in versions)
        {
            if (!byId.TryGetValue(version.Id, out var line))
            {
                results[version.Id] = Refused(version.Id, "FIN_BANK_LINE_NOT_FOUND", "Linia de extras nu există.");
                continue;
            }

            if (!closed.TryGetValue(line.BookingDate, out var isClosed))
            {
                isClosed = await periods.IsClosedAsync(context.CompanyId, line.BookingDate, cancellationToken).ConfigureAwait(false);
                closed[line.BookingDate] = isClosed;
            }

            var lineParts = byLine[line.Id].ToList();
            var refusal = !line.RowVersion.AsSpan().SequenceEqual(version.RowVersion)
                ? ("FIN_CONCURRENCY_CONFLICT", "Linia de extras a fost modificată de alt utilizator; reîncărcați datele.")
                : line.MatchStatus != BankLineMatchStatus.Explained
                    ? ("FIN_BANK_LINE_NOT_EXPLAINED", "Doar liniile explicate pot fi validate.")
                    : line.ContinuityStatus == ContinuityStatus.Gap
                        ? ("FIN_BANK_STATEMENT_GAP", "Extrasul are un gol față de extrasul anterior; importați extrasul lipsă sau acceptați golul.")
                        : lineParts.Sum(p => p.Amount) != line.Amount
                            ? ("FIN_BANK_LINE_PARTS_MISMATCH", "Suma părților nu este egală cu suma liniei.")
                            : lineParts.Any(p => p.Amount < 0 && p.RequiresPartner && p.PaymentOrderId is null)
                                ? ("FIN_BANK_PARTNER_PAYMENT_WITHOUT_OP", "O plată către partener se validează prin ordin de plată („OP nou” din linia de extras).")
                                : isClosed
                                    ? (CoreErrorCodes.PeriodClosed, "Perioada contabilă a liniei este închisă.")
                                    : ((string, string)?)null;
            if (refusal is { } r)
            {
                results[line.Id] = Refused(line.Id, r.Item1, r.Item2);
            }
            else
            {
                passing.Add(version);
            }
        }

        if (passing.Count > 0)
        {
            var (_, saved) = await ValidateAsync(passing, cancellationToken).ConfigureAwait(false);
            foreach (var s in saved)
            {
                results[s.Id] = new BankLineValidationResult(s.Id, true, null, null, s.RowVersion);
            }
        }

        return versions.Select(v => results[v.Id]).ToList();
    }

    /// <summary>Felul erorii API pentru un cod de refuz al validarii unei linii.</summary>
    public static ErpErrorKind KindOf(string code) => code switch
    {
        "FIN_BANK_LINE_NOT_FOUND" => ErpErrorKind.NotFound,
        "FIN_BANK_LINE_PARTS_MISMATCH" or "FIN_BANK_PARTNER_PAYMENT_WITHOUT_OP" => ErpErrorKind.Validation,
        _ => ErpErrorKind.Conflict,
    };

    private static BankLineValidationResult Refused(long id, string code, string message) => new(id, false, code, message, null);

    public async Task<(IReadOnlyList<BankLineRow> Lines, IReadOnlyList<BankLineSaved> Saved)> ValidateAsync(IReadOnlyList<BankLineVersion> versions,
        CancellationToken cancellationToken)
    {
        var (lines, parts) = await store.GetAsync(context.CompanyId, versions.Select(l => l.Id).ToList(), cancellationToken).ConfigureAwait(false);
        if (lines.Count != versions.Count)
        {
            throw BankLineRules.NotFound();
        }

        foreach (var day in lines.Select(l => l.BookingDate).Distinct())
        {
            await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, day, cancellationToken).ConfigureAwait(false);
        }

        var tolerance = await settings.CardFeeTolerancePercentAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var (saved, confirmed, rejected) = await store.ValidateAsync(context.CompanyId, versions, tolerance, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var c in confirmed)
        {
            await outbox.EnqueueAsync(new Contracts.Bank.OrdinPlataConfirmat(c.PaymentOrderId, c.BankStatementLineId, c.BookingDate, c.Amount)
            {
                CompanyId = context.CompanyId,
            }, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (orderId, lineId) in rejected)
        {
            await outbox.EnqueueAsync(new Contracts.Bank.OrdinPlataRespins(orderId, lineId, "Suma a fost returnată de bancă.") { CompanyId = context.CompanyId },
                cancellationToken).ConfigureAwait(false);
        }

        var policy = await precision.ResolveAsync(context.CompanyId, null, null, cancellationToken).ConfigureAwait(false);
        var rateCache = new Dictionary<(string, DateOnly), decimal>();
        var byLine = parts.ToLookup(p => p.BankStatementLineId);
        foreach (var line in lines)
        {
            var included = byLine[line.Id].Where(p => p.PaymentOrderId is null).ToList();
            if (included.Count == 0)
            {
                continue;
            }

            if (!rateCache.TryGetValue((line.CurrencyCode, line.BookingDate), out var perUnit))
            {
                perUnit = (await rates.GetRateAsync(line.CurrencyCode, line.BookingDate, cancellationToken).ConfigureAwait(false)).PerUnit;
                rateCache[(line.CurrencyCode, line.BookingDate)] = perUnit;
            }

            // Sumele evenimentului sunt in sensul lui: pozitive la incasare si la plata; o parte de sens opus (comisionul retinut dintr-o
            // incasare) ramane negativa.
            var incoming = line.Amount > 0;
            var amounts = included.Select(p => incoming ? p.Amount : -p.Amount).ToList();
            var (ron, totalRon) = CashDocumentRules.ToRon(amounts, perUnit, v => precision.RoundAmount(v, policy));
            var movement = included.Select((p, i) => new TreasuryMovementLine(p.PartNumber, p.OperationTypeCode, amounts[i], ron[i], p.SettlementRef,
                p.CounterTreasuryAccountId)).ToList();
            var employee = included.Select(p => p.EmployeeId).Distinct().Count() == 1 ? included[0].EmployeeId : null;
            var lineId = checked((int)line.Id);
            IntegrationEvent evt = incoming
                ? new IncasareInregistrata(line.TreasuryAccountId, line.AccountKind, BankLineRules.DocumentType, lineId, BankLineRules.Number(line),
                    line.BookingDate, line.PartnerId, employee, line.CurrencyCode, perUnit, amounts.Sum(), totalRon, movement)
                : new PlataInregistrata(line.TreasuryAccountId, line.AccountKind, BankLineRules.DocumentType, lineId, BankLineRules.Number(line),
                    line.BookingDate, line.PartnerId, employee, line.CurrencyCode, perUnit, amounts.Sum(), totalRon, movement);
            await outbox.EnqueueAsync(evt with { CompanyId = context.CompanyId }, cancellationToken).ConfigureAwait(false);

            // Partile pe partener sting facturile legate prin F3 (F2 4.4, F3 4.2); facturile au fost alese explicit la explicarea liniei, deci
            // avertizarile (litigiu) sunt considerate confirmate. Platile catre furnizori trec prin OP (51223), cu regula SPV la validarea OP-ului.
            await settlement.RegisterAsync(new Settlement.TreasurySettlementDocument(context.CompanyId, BankLineRules.DocumentType, line.Id,
                BankLineRules.Number(line), line.BookingDate, line.CurrencyCode, perUnit,
                included.Select((p, i) => new Settlement.TreasurySettlementLine(p.PartNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    p.OperationTypeCode, p.PartnerId ?? line.PartnerId, p.Amount, ron[i], p.SettlementRef) { Incoming = p.Amount > 0 }).ToList(), true),
                cancellationToken).ConfigureAwait(false);
        }

        return (lines, saved);
    }
}

/// <summary>
/// Desface validarea unei linii (F2 3.4): perioada zilei contabile deschisa (CORE_PERIOD_CLOSED); linia revine la Explicata cu datele bancii
/// neschimbate, iar DocumentTrezorerieStornat anunta F3 si contabilitatea sa desfaca efectele. Auditata, cu motiv.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementUnvalidate)]
[Audited("Fin.BankStatementLine.Unvalidate")]
public sealed record UnvalidateBankStatementLine(long Id, string Reason, byte[] RowVersion) : ICommand<BankLineSaved>;

internal sealed class UnvalidateBankStatementLineValidator : AbstractValidator<UnvalidateBankStatementLine>
{
    public UnvalidateBankStatementLineValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Desfacerea validării cere un motiv scris.").MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty().WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class UnvalidateBankStatementLineHandler(IBankStatementLineStore store, IPeriodLock periods, IOutbox outbox, ICurrentContext context,
    IAuditScope audit, Settlement.ISettlementService settlement) : ICommandHandler<UnvalidateBankStatementLine, BankLineSaved>
{
    public async Task<BankLineSaved> Handle(UnvalidateBankStatementLine request, CancellationToken cancellationToken)
    {
        var (lines, _) = await store.GetAsync(context.CompanyId, [request.Id], cancellationToken).ConfigureAwait(false);
        var line = lines.SingleOrDefault() ?? throw BankLineRules.NotFound();
        await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, line.BookingDate, cancellationToken).ConfigureAwait(false);
        var version = await store.UnvalidateAsync(context.CompanyId, request.Id, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        var lineId = checked((int)line.Id);
        await outbox.EnqueueAsync(new DocumentTrezorerieStornat(lineId, lineId, line.BookingDate)
        {
            CompanyId = context.CompanyId, DocumentType = BankLineRules.DocumentType,
        }, cancellationToken).ConfigureAwait(false);
        // Facturile stinse de linie redevin deschise (F3 4.2 pasul 6).
        await settlement.ReversePaymentAsync(context.CompanyId, BankLineRules.DocumentType, line.Id, null, line.BookingDate, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.BankStatementLine", request.Id, new { Reason = request.Reason.Trim(), line.BookingDate, line.Amount });
        return new BankLineSaved(request.Id, version);
    }
}
