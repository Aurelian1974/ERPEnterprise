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
    int? CounterTreasuryAccountId, string? BudgetObligationRef, int? CostCenterId, int? ProjectId, string? Note);

public sealed record BankLinePartRecord(short PartNumber, int OperationTypeId, decimal Amount, int? PartnerId, int? EmployeeId, string? SettlementRef,
    int? CashDocumentId, int? CounterTreasuryAccountId, string? BudgetObligationRef, int? CostCenterId, int? ProjectId, string? Note);

public sealed record BankLineVersion(long Id, byte[] RowVersion);

public sealed record BankLineSaved(long Id, byte[] RowVersion);

public sealed record BankLinePartDto(short PartNumber, int OperationTypeId, string OperationTypeCode, string OperationTypeName, string Amount, int? PartnerId,
    string? PartnerName, int? EmployeeId, string? SettlementRef, int? PaymentOrderId, int? CashDocumentId, int? CounterTreasuryAccountId,
    string? BudgetObligationRef, int? CostCenterId, int? ProjectId, string? Note);

/// <summary>O linie de extras cu explicatia ei (panoul de reconciliere din macheta f2-02).</summary>
public sealed record BankLineDetailDto(long Id, int BankStatementId, string? StatementNo, int LineNumber, DateOnly BookingDate, DateOnly? ValueDate,
    string Amount, string? CounterpartyName, string? CounterpartyIban, string? RemittanceInfo, string? EndToEndId, string BankReference,
    BankLineMatchStatus MatchStatus, int? PartnerId, string? PartnerName, int TreasuryAccountId, string CurrencyCode, DateTime? ValidatedAt,
    IReadOnlyList<BankLinePartDto> Parts, byte[] RowVersion);

public sealed record BankLineRow(long Id, int BankStatementId, string? StatementNo, int LineNumber, DateOnly BookingDate, DateOnly? ValueDate, decimal Amount,
    string? CounterpartyName, string? CounterpartyIban, string? RemittanceInfo, string? EndToEndId, string BankReference, BankLineMatchStatus MatchStatus,
    int? PartnerId, string? PartnerName, int TreasuryAccountId, Contracts.Treasury.TreasuryAccountKind AccountKind, string CurrencyCode,
    DateTime? ValidatedAt, byte[] RowVersion);

public sealed record BankLinePartRow(long BankStatementLineId, short PartNumber, int OperationTypeId, string OperationTypeCode, string OperationTypeName,
    decimal Amount, int? PartnerId, string? PartnerName, int? EmployeeId, string? SettlementRef, int? PaymentOrderId, int? CashDocumentId,
    int? CounterTreasuryAccountId, string? BudgetObligationRef, int? CostCenterId, int? ProjectId, string? Note);

/// <summary>Accesul la date pentru liniile de extras (fin.usp_BankStatementLine_*, NET-06).</summary>
public interface IBankStatementLineStore
{
    /// <summary>Liniile firmei cerute, cu partile lor; cele inexistente sau ale altei firme lipsesc.</summary>
    Task<(IReadOnlyList<BankLineRow> Lines, IReadOnlyList<BankLinePartRow> Parts)> GetAsync(int companyId, IReadOnlyCollection<long> ids, CancellationToken ct);

    Task<byte[]> ExplainAsync(int companyId, long lineId, IReadOnlyList<BankLinePartRecord> parts, byte[] rowVersion, int userId, CancellationToken ct);

    Task<IReadOnlyList<BankLineSaved>> ValidateAsync(int companyId, IReadOnlyList<BankLineVersion> lines, int userId, CancellationToken ct);

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
        return BankLineRules.Detail(line, parts);
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
            Clean(p.Note))).ToList();
        var version = await store.ExplainAsync(context.CompanyId, request.Id, parts, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.BankStatementLine", request.Id, new
        {
            Parts = parts.Select(p => new { p.OperationTypeId, p.Amount, p.PartnerId, p.EmployeeId, p.SettlementRef, p.CashDocumentId, p.CounterTreasuryAccountId }),
        });
        return new BankLineSaved(request.Id, version);
    }
}

/// <summary>
/// Valideaza un grup de linii explicate (F2 4.4; SP-08): perioada fiecarei zile contabile deschisa (CORE_PERIOD_CLOSED), apoi validarea pe
/// seturi (51201–51205, 51223) si, in aceeasi tranzactie, cate un eveniment pe linie: IncasareInregistrata pentru intrari,
/// PlataInregistrata pentru iesirile fara ordin de plata (partile care confirma un OP nu republica plata).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementValidate)]
[Audited("Fin.BankStatementLine.Validate")]
public sealed record ValidateBankStatementLines(IReadOnlyList<BankLineVersion> Lines) : ICommand<IReadOnlyList<BankLineSaved>>;

internal sealed class ValidateBankStatementLinesValidator : AbstractValidator<ValidateBankStatementLines>
{
    public ValidateBankStatementLinesValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Alegeți liniile de validat.")
            .Must(l => l.Count <= 2000).WithMessage("Se validează cel mult 2.000 de linii odată.")
            .Must(l => l.Select(x => x.Id).Distinct().Count() == l.Count).WithMessage("O linie apare de două ori.");
    }
}

internal sealed class ValidateBankStatementLinesHandler(IBankStatementLineStore store, IPeriodLock periods, IExchangeRateProvider rates,
    IPrecisionService precision, IOutbox outbox, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ValidateBankStatementLines, IReadOnlyList<BankLineSaved>>
{
    public async Task<IReadOnlyList<BankLineSaved>> Handle(ValidateBankStatementLines request, CancellationToken cancellationToken)
    {
        var (lines, parts) = await store.GetAsync(context.CompanyId, request.Lines.Select(l => l.Id).ToList(), cancellationToken).ConfigureAwait(false);
        if (lines.Count != request.Lines.Count)
        {
            throw BankLineRules.NotFound();
        }

        foreach (var day in lines.Select(l => l.BookingDate).Distinct())
        {
            await CashAccess.EnsureOpenPeriodAsync(periods, context.CompanyId, day, cancellationToken).ConfigureAwait(false);
        }

        var saved = await store.ValidateAsync(context.CompanyId, request.Lines, context.UserId, cancellationToken).ConfigureAwait(false);

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
        }

        audit.Describe("Fin.BankStatementLine", lines.Count == 1 ? lines[0].Id : null, new { Lines = lines.Select(l => l.Id), Count = lines.Count });
        return saved;
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
    IAuditScope audit) : ICommandHandler<UnvalidateBankStatementLine, BankLineSaved>
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
        audit.Describe("Fin.BankStatementLine", request.Id, new { Reason = request.Reason.Trim(), line.BookingDate, line.Amount });
        return new BankLineSaved(request.Id, version);
    }
}
