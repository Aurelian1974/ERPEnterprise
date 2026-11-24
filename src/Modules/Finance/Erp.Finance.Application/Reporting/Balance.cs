using Erp.Core.Contracts;
using Erp.Finance.Contracts.Reporting;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Reporting;
using FluentValidation;

namespace Erp.Finance.Application.Reporting;

/// <summary>Rulajele brute ale balantei si conturile planului, cum vin din fin.usp_Balance_Get.</summary>
public sealed record BalanceData(DateOnly YearStart, DateOnly YearEnd, bool IsAlternative, IReadOnlyList<BalanceInput> Inputs, IReadOnlyDictionary<string, BalanceAccount> Accounts);

public sealed record LedgerLineDto(long LineId, long EntryId, string? DisplayNumber, DateOnly AccountingDate, string AccountCode, byte SourceKind,
    string? SourceDocumentType, string? SourceDocumentNumber, string? Description, string? PartnerName, string? CostCenterCode, string? ProjectCode, string Debit,
    string Credit, string Balance);

/// <summary>Fisa contului (C5 3.3, macheta c5-02): soldul initial al perioadei, rulajele, soldul final si liniile cu soldul curent (debit − credit).</summary>
public sealed record AccountLedgerDto(string AccountCode, string? AccountName, byte? AccountType, string Opening, string PeriodDebit, string PeriodCredit,
    string Closing, int LineCount, IReadOnlyList<LedgerLineDto> Lines);

public sealed record LedgerHeader(decimal Opening, decimal PeriodDebit, decimal PeriodCredit, int LineCount, string? AccountName, byte? AccountType);

public sealed record LedgerLineRecord(long LineId, long EntryId, string JournalCode, int? Number, DateOnly AccountingDate, string AccountCode, byte SourceKind,
    string? SourceDocumentType, string? SourceDocumentNumber, string? Description, string? PartnerName, string? CostCenterCode, string? ProjectCode, decimal Debit,
    decimal Credit, decimal Balance);

public sealed record LedgerFilter(string AccountCode, DateOnly From, DateOnly To, int? PartnerId, int? CostCenterId, int? ProjectId, bool IncludeClosing, int Skip,
    int Take);

public sealed record TrialBalanceRowDto(string AccountCode, string AccountName, int? PartnerId, int? CostCenterId, int? ProjectId, string OpeningDebit,
    string OpeningCredit, string PriorDebit, string PriorCredit, string PeriodDebit, string PeriodCredit, string TotalDebit, string TotalCredit,
    string ClosingDebit, string ClosingCredit);

public sealed record BalanceCheckDto(string Name, bool Passed, string Difference);

/// <summary>Balanta de verificare (macheta c5-01): randurile, totalul si verificarile egalitatilor (C5 regula 2).</summary>
public sealed record TrialBalanceDto(DateOnly YearStart, DateOnly From, DateOnly To, IReadOnlyList<TrialBalanceRowDto> Rows, TrialBalanceRowDto Total,
    IReadOnlyList<BalanceCheckDto> Checks);

/// <summary>Un rand al cartii mari: contul sintetic, contul corespondent („%” pentru mai multe) si rulajele.</summary>
public sealed record GeneralLedgerMoveDto(string CounterAccount, string Debit, string Credit);

public sealed record GeneralLedgerAccountDto(string AccountCode, string AccountName, string OpeningDebit, string OpeningCredit,
    IReadOnlyList<GeneralLedgerMoveDto> Moves, string PeriodDebit, string PeriodCredit, string ClosingDebit, string ClosingCredit);

/// <summary>Soldurile si mișcarile prin fin.usp_Balance_Get, fin.usp_AccountLedger_Get, fin.usp_GeneralLedger_Get (NET-06).</summary>
public interface IBalanceStore
{
    Task<BalanceData> BalanceAsync(BalanceRequest request, CancellationToken ct);

    Task<(LedgerHeader Header, IReadOnlyList<LedgerLineRecord> Lines)> LedgerAsync(int companyId, LedgerFilter filter, CancellationToken ct);

    Task<IReadOnlyList<(string AccountCode, string CounterAccount, decimal Debit, decimal Credit)>> GeneralLedgerAsync(int companyId, DateOnly from, DateOnly to,
        CancellationToken ct);
}

/// <summary>Balanta (C5 regula 1): un singur calcul de sold, folosit de ecrane, de cartea mare si de Raportare (IBalanceQuery).</summary>
public sealed class BalanceService(IBalanceStore store) : IBalanceQuery
{
    public const string UnmappedName = "Nemapat";

    public async Task<IReadOnlyList<BalanceRow>> GetBalanceAsync(BalanceRequest request, CancellationToken ct) =>
        (await BuildAsync(request, ct).ConfigureAwait(false)).Rows.Select(r => new BalanceRow(r.AccountCode, r.AccountName, r.PartnerId, r.CostCenterId,
            r.ProjectId, r.OpeningDebit, r.OpeningCredit, r.PriorDebit, r.PriorCredit, r.PeriodDebit, r.PeriodCredit, r.ClosingDebit, r.ClosingCredit)).ToList();

    public async Task<(DateOnly YearStart, IReadOnlyList<TrialBalanceRow> Rows)> BuildAsync(BalanceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var data = await store.BalanceAsync(request, ct).ConfigureAwait(false);
        var level = request.Level is >= 1 and <= 3 ? (BalanceLevel)request.Level : BalanceLevel.Analytic;
        return (data.YearStart, TrialBalance.Build(data.Inputs, data.Accounts, level, data.IsAlternative, UnmappedName));
    }
}

/// <summary>Balanta de verificare cu 10 coloane (C5 3.2, decizia 1), pe nivel, plan, dimensiune, cu verificarile egalitatilor.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportTrialBalance)]
public sealed record RunTrialBalance(DateOnly From, DateOnly To, byte Level = 3, int? ChartId = null, bool IncludeClosingEntries = true, string? AccountFilter = null,
    bool ByPartner = false, bool ByCostCenter = false, bool ByProject = false) : IQuery<TrialBalanceDto>;

internal sealed class RunTrialBalanceValidator : AbstractValidator<RunTrialBalance>
{
    public RunTrialBalanceValidator()
    {
        RuleFor(x => x.Level).InclusiveBetween((byte)1, (byte)3);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
        RuleFor(x => x.AccountFilter).MaximumLength(20);
    }
}

/// <summary>Fisa contului (C5 3.3): contul si descendentii lui, in perioada, cu filtre pe partener si dimensiuni, paginata.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportTrialBalance)]
public sealed record GetAccountLedger(string AccountCode, DateOnly From, DateOnly To, int? PartnerId = null, int? CostCenterId = null, int? ProjectId = null,
    bool IncludeClosingEntries = true, int Skip = 0, int Take = 500) : IQuery<AccountLedgerDto>;

internal sealed class GetAccountLedgerValidator : AbstractValidator<GetAccountLedger>
{
    public GetAccountLedgerValidator()
    {
        RuleFor(x => x.AccountCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
    }
}

/// <summary>Cartea mare pe o luna (C5 3.3): pe fiecare cont sintetic, soldul initial, rulajele pe conturi corespondente si soldul final.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportTrialBalance)]
public sealed record GetGeneralLedger(int Year, int Month) : IQuery<IReadOnlyList<GeneralLedgerAccountDto>>;

internal sealed class GetGeneralLedgerValidator : AbstractValidator<GetGeneralLedger>
{
    public GetGeneralLedgerValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}

internal sealed class ReportingHandlers(BalanceService balance, IBalanceStore store, ICurrentContext context) :
    IQueryHandler<RunTrialBalance, TrialBalanceDto>, IQueryHandler<GetAccountLedger, AccountLedgerDto>,
    IQueryHandler<GetGeneralLedger, IReadOnlyList<GeneralLedgerAccountDto>>
{
    public async Task<TrialBalanceDto> Handle(RunTrialBalance request, CancellationToken cancellationToken)
    {
        var (yearStart, rows) = await balance.BuildAsync(new BalanceRequest(context.CompanyId, request.ChartId, request.From, request.To, request.Level,
            request.IncludeClosingEntries, string.IsNullOrWhiteSpace(request.AccountFilter) ? null : request.AccountFilter.Trim(), request.ByPartner,
            request.ByCostCenter, request.ByProject), cancellationToken).ConfigureAwait(false);
        var total = new TrialBalanceRow("", "Total", null, null, null, rows.Sum(r => r.OpeningDebit), rows.Sum(r => r.OpeningCredit), rows.Sum(r => r.PriorDebit),
            rows.Sum(r => r.PriorCredit), rows.Sum(r => r.PeriodDebit), rows.Sum(r => r.PeriodCredit), rows.Sum(r => r.TotalDebit), rows.Sum(r => r.TotalCredit),
            rows.Sum(r => r.ClosingDebit), rows.Sum(r => r.ClosingCredit));
        return new TrialBalanceDto(yearStart, request.From, request.To, rows.Select(Dto).ToList(), Dto(total),
            TrialBalance.Checks(rows).Select(c => new BalanceCheckDto(c.Name, c.Passed, Money.Format(c.Difference))).ToList());
    }

    public async Task<AccountLedgerDto> Handle(GetAccountLedger request, CancellationToken cancellationToken)
    {
        var code = request.AccountCode.Trim();
        var (h, lines) = await store.LedgerAsync(context.CompanyId, new LedgerFilter(code, request.From, request.To, request.PartnerId, request.CostCenterId,
            request.ProjectId, request.IncludeClosingEntries, Math.Max(request.Skip, 0), Math.Clamp(request.Take, 1, 2000)), cancellationToken).ConfigureAwait(false);
        return new AccountLedgerDto(code, h.AccountName, h.AccountType, Money.Format(h.Opening), Money.Format(h.PeriodDebit), Money.Format(h.PeriodCredit),
            Money.Format(h.Opening + h.PeriodDebit - h.PeriodCredit), h.LineCount, lines.Select(l => new LedgerLineDto(l.LineId, l.EntryId,
                Accounting.JournalEntryNumbers.Display(l.JournalCode, l.Number), l.AccountingDate, l.AccountCode, l.SourceKind, l.SourceDocumentType,
                l.SourceDocumentNumber, l.Description, l.PartnerName, l.CostCenterCode, l.ProjectCode, Money.Format(l.Debit), Money.Format(l.Credit),
                Money.Format(l.Balance))).ToList());
    }

    public async Task<IReadOnlyList<GeneralLedgerAccountDto>> Handle(GetGeneralLedger request, CancellationToken cancellationToken)
    {
        var from = new DateOnly(request.Year, request.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var (_, rows) = await balance.BuildAsync(new BalanceRequest(context.CompanyId, null, from, to, (byte)BalanceLevel.Synthetic2, true, null, false, false, false),
            cancellationToken).ConfigureAwait(false);
        var moves = (await store.GeneralLedgerAsync(context.CompanyId, from, to, cancellationToken).ConfigureAwait(false)).ToLookup(m => m.AccountCode);
        return rows.Select(r =>
        {
            var (openingDebit, openingCredit) = TrialBalance.Closing(r.OpeningDebit + r.PriorDebit, r.OpeningCredit + r.PriorCredit, null);
            return new GeneralLedgerAccountDto(r.AccountCode, r.AccountName, Money.Format(openingDebit), Money.Format(openingCredit),
                moves[r.AccountCode].Select(m => new GeneralLedgerMoveDto(m.CounterAccount, Money.Format(m.Debit), Money.Format(m.Credit))).ToList(),
                Money.Format(r.PeriodDebit), Money.Format(r.PeriodCredit), Money.Format(r.ClosingDebit), Money.Format(r.ClosingCredit));
        }).ToList();
    }

    private static TrialBalanceRowDto Dto(TrialBalanceRow r) => new(r.AccountCode, r.AccountName, r.PartnerId, r.CostCenterId, r.ProjectId, Money.Format(r.OpeningDebit),
        Money.Format(r.OpeningCredit), Money.Format(r.PriorDebit), Money.Format(r.PriorCredit), Money.Format(r.PeriodDebit), Money.Format(r.PeriodCredit),
        Money.Format(r.TotalDebit), Money.Format(r.TotalCredit), Money.Format(r.ClosingDebit), Money.Format(r.ClosingCredit));
}
