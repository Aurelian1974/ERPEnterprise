using Erp.Core.Contracts;
using Erp.Finance.Domain;
using FluentValidation;

namespace Erp.Finance.Application.Reporting;

public sealed record GroupCompanyDto(int Id, string Code, string Name);

/// <summary>Un cont al balantei de grup (macheta c5-05): soldul net pe fiecare firma, eliminarile si consolidatul (debit − credit), ca text.</summary>
public sealed record GroupBalanceRowDto(string AccountCode, string AccountName, IReadOnlyDictionary<int, string> ByCompany, string Eliminations, string Consolidated);

public sealed record GroupBalanceDto(int RunId, int ChartId, string ChartCode, int Year, int Month, bool IsFinal, DateTime RunAt, IReadOnlyList<GroupCompanyDto> Companies,
    IReadOnlyList<GroupBalanceRowDto> Rows);

/// <summary>O diferenta intra-grup (C5 3.7): perechea de conturi, firmele, sumele de pe fiecare parte si diferenta.</summary>
public sealed record IntercompanyDifferenceDto(string PairName, int FirstCompanyId, string FirstCompanyCode, int SecondCompanyId, string SecondCompanyCode,
    string FirstAmount, string SecondAmount, string Difference);

public sealed record GroupLine(int CompanyId, byte Kind, string AccountCode, string AccountName, decimal DebitRon, decimal CreditRon);

/// <summary>Balanta de grup prin fin.usp_GroupBalance_*, fin.usp_Intercompany_Differences, fin.usp_IntercompanyPair_Save (NET-06).</summary>
public interface IGroupBalanceStore
{
    Task<int> RunAsync(int chartId, int year, int month, bool final, int userId, CancellationToken ct);

    Task<(int ChartId, string ChartCode, int Year, int Month, bool IsFinal, DateTime RunAt, IReadOnlyList<GroupCompanyDto> Companies, IReadOnlyList<GroupLine> Lines)?>
        GetAsync(int runId, CancellationToken ct);

    Task<IReadOnlyList<IntercompanyDifferenceDto>> DifferencesAsync(int chartId, int year, int month, CancellationToken ct);

    Task<int> SavePairAsync(int chartId, string name, int firstAccountId, int secondAccountId, int userId, CancellationToken ct);
}

/// <summary>Ruleaza balanta de grup (C5 4.3): previzualizare oricand, finala doar cu luna inchisa la toate firmele (51979) si maparea completa (51980).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportGroup)]
[Audited("Fin.GroupBalance.Run")]
public sealed record RunGroupBalance(int ChartId, int Year, int Month, bool Final) : ICommand<int>;

internal sealed class RunGroupBalanceValidator : AbstractValidator<RunGroupBalance>
{
    public RunGroupBalanceValidator() => RuleFor(x => x.Month).InclusiveBetween(1, 12);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportGroup)]
public sealed record GetGroupBalance(int RunId) : IQuery<GroupBalanceDto>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportGroup)]
public sealed record GetIntercompanyDifferences(int ChartId, int Year, int Month) : IQuery<IReadOnlyList<IntercompanyDifferenceDto>>;

/// <summary>O pereche de eliminare: primul cont cu sold debitor (creanta, cheltuiala), al doilea cu sold creditor (datoria, venitul).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportGroup)]
[Audited("Fin.GroupBalance.Pair")]
public sealed record SaveIntercompanyPair(int ChartId, string Name, int FirstAccountId, int SecondAccountId) : ICommand<int>;

internal sealed class GroupBalanceHandlers(IGroupBalanceStore store, ICurrentContext context, IAuditScope audit) :
    ICommandHandler<RunGroupBalance, int>, IQueryHandler<GetGroupBalance, GroupBalanceDto>,
    IQueryHandler<GetIntercompanyDifferences, IReadOnlyList<IntercompanyDifferenceDto>>, ICommandHandler<SaveIntercompanyPair, int>
{
    public async Task<int> Handle(RunGroupBalance request, CancellationToken cancellationToken)
    {
        var id = await store.RunAsync(request.ChartId, request.Year, request.Month, request.Final, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.GroupBalanceRun", id, request);
        return id;
    }

    public async Task<GroupBalanceDto> Handle(GetGroupBalance request, CancellationToken cancellationToken)
    {
        var r = await store.GetAsync(request.RunId, cancellationToken).ConfigureAwait(false)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_REPORT_NOT_FOUND", "Rularea nu există.");
        var rows = r.Lines.GroupBy(l => (l.AccountCode, l.AccountName)).OrderBy(g => g.Key.AccountCode, StringComparer.Ordinal).Select(g =>
        {
            var byCompany = g.Where(l => l.Kind == 1).GroupBy(l => l.CompanyId).ToDictionary(c => c.Key, c => Money.Format(c.Sum(l => l.DebitRon - l.CreditRon)));
            var eliminations = g.Where(l => l.Kind == 2).Sum(l => l.DebitRon - l.CreditRon);
            return new GroupBalanceRowDto(g.Key.AccountCode, g.Key.AccountName, byCompany, Money.Format(eliminations), Money.Format(g.Sum(l => l.DebitRon - l.CreditRon)));
        }).ToList();
        return new GroupBalanceDto(request.RunId, r.ChartId, r.ChartCode, r.Year, r.Month, r.IsFinal, r.RunAt, r.Companies, rows);
    }

    public Task<IReadOnlyList<IntercompanyDifferenceDto>> Handle(GetIntercompanyDifferences request, CancellationToken cancellationToken) =>
        store.DifferencesAsync(request.ChartId, request.Year, request.Month, cancellationToken);

    public async Task<int> Handle(SaveIntercompanyPair request, CancellationToken cancellationToken)
    {
        var id = await store.SavePairAsync(request.ChartId, request.Name.Trim(), request.FirstAccountId, request.SecondAccountId, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.IntercompanyPair", id, request);
        return id;
    }
}
