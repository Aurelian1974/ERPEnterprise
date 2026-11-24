using Erp.Core.Contracts;
using Erp.Finance.Contracts.Reporting;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Reporting;
using FluentValidation;

namespace Erp.Finance.Application.Reporting;

public sealed record ReportRowDto(string RowCode, string Label, string? Formula, bool IsTotal, byte Indent);

/// <summary>Un raport definit (C5 3.5): livrat (Kind 1) sau al clientului (Kind 2), cu randurile lui.</summary>
public sealed record ReportDefinitionDto(int Id, string Code, string Name, byte Kind, int? ChartId, short Version, DateOnly? ValidFrom, DateOnly? ValidTo,
    byte[] RowVersion, IReadOnlyList<ReportRowDto> Rows);

public sealed record ReportDefinitionItemDto(int Id, string Code, string Name, byte Kind, short Version, DateOnly? ValidFrom, DateOnly? ValidTo, int Rows);

public sealed record ReportDimension(int Id, string DimensionType, string Code);

public sealed record ReportDefinitionData(ReportDefinitionDto Definition, IReadOnlyList<string> AccountCodes, IReadOnlyList<ReportDimension> Dimensions);

/// <summary>O celula calculata: valoarea pe perioada aleasa si pe perioada de comparatie (anul precedent), ca text.</summary>
public sealed record ReportResultRowDto(string RowCode, string Label, bool IsTotal, byte Indent, string? Current, string? Previous);

public sealed record ReportResultDto(int DefinitionId, string Code, string Name, DateOnly From, DateOnly To, DateOnly? PreviousFrom, DateOnly? PreviousTo,
    IReadOnlyList<ReportResultRowDto> Rows);

/// <summary>Rapoartele definite prin fin.usp_ReportDefinition_* (NET-06).</summary>
public interface IReportDefinitionStore
{
    Task<IReadOnlyList<ReportDefinitionItemDto>> ListAsync(int companyId, CancellationToken ct);

    Task<ReportDefinitionData?> GetAsync(int companyId, int? id, string? code, DateOnly? onDate, CancellationToken ct);

    Task<IReadOnlyList<string>> AccountCodesAsync(int companyId, int? chartId, CancellationToken ct);

    Task<int> SaveAsync(int companyId, int? id, byte[]? rowVersion, string code, string name, int? chartId, IReadOnlyList<ReportRowDto> rows, int userId,
        CancellationToken ct);
}

/// <summary>
/// Motorul rapoartelor definite (C5 3.5, decizia 4): formulele se parseaza si se valideaza (51974), apoi se calculeaza peste balanta perioadei
/// (o citire cu notele de inchidere pentru solduri, una fara pentru rulaje; cu dimensiuni doar daca o formula are filtru), prin IBalanceQuery.
/// </summary>
public sealed class ReportEngine(IBalanceQuery balances)
{
    public static IReadOnlyDictionary<string, FormulaNode> Validate(IReadOnlyList<ReportRowDto> rows, IReadOnlyCollection<string> accountCodes)
    {
        ArgumentNullException.ThrowIfNull(rows);
        try
        {
            return ReportFormula.Validate(rows.Where(r => !string.IsNullOrWhiteSpace(r.Formula)).ToDictionary(r => r.RowCode, r => r.Formula!, StringComparer.Ordinal),
                accountCodes);
        }
        catch (ReportFormulaException e)
        {
            throw new ErpException(ErpErrorKind.Validation, "FIN_REPORT_FORMULA_INVALID", e.Message);
        }
    }

    public async Task<IReadOnlyDictionary<string, decimal>> RunAsync(int companyId, ReportDefinitionData data, DateOnly from, DateOnly to, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        var formulas = Validate(data.Definition.Rows, data.AccountCodes);
        var withDims = formulas.Values.Any(f => HasFilter(f));
        async Task<IReadOnlyList<BalanceRow>> Read(bool closing, bool dims) =>
            await balances.GetBalanceAsync(new BalanceRequest(companyId, data.Definition.ChartId, from, to, 3, closing, null, false, dims, dims), ct).ConfigureAwait(false);
        var closingRows = await Read(true, false).ConfigureAwait(false);
        var turnoverRows = await Read(false, false).ConfigureAwait(false);
        var closingDims = withDims ? await Read(true, true).ConfigureAwait(false) : [];
        var turnoverDims = withDims ? await Read(false, true).ConfigureAwait(false) : [];
        var dimensions = data.Dimensions.ToDictionary(d => (d.DimensionType, d.Code.ToUpperInvariant()), d => d.Id);

        decimal Measure(AccountNode a)
        {
            var turnover = a.Measure is AccountMeasure.TurnoverDebit or AccountMeasure.TurnoverCredit;
            IEnumerable<BalanceRow> rows = a.DimensionKind is null ? (turnover ? turnoverRows : closingRows) : (turnover ? turnoverDims : closingDims);
            if (a.DimensionKind is { } kind)
            {
                var type = kind == "CC" ? "COST_CENTER" : "PROJECT";
                var id = dimensions.TryGetValue((type, a.DimensionCode!.ToUpperInvariant()), out var v) ? v : -1;
                rows = rows.Where(r => (kind == "CC" ? r.CostCenterId : r.ProjectId) == id);
            }

            rows = rows.Where(r => a.Accounts.Any(x => x.Matches(r.AccountCode)));
            return a.Measure switch
            {
                AccountMeasure.ClosingDebit => rows.Sum(r => r.ClosingDebit),
                AccountMeasure.ClosingCredit => rows.Sum(r => r.ClosingCredit),
                AccountMeasure.ClosingNet => rows.Sum(r => r.ClosingDebit - r.ClosingCredit),
                AccountMeasure.TurnoverDebit => rows.Sum(r => r.PeriodDebit),
                _ => rows.Sum(r => r.PeriodCredit),
            };
        }

        return ReportFormula.Evaluate(formulas, Measure);
    }

    private static bool HasFilter(FormulaNode node) => node switch
    {
        AccountNode a => a.DimensionKind is not null,
        NegateNode n => HasFilter(n.Operand),
        BinaryNode b => HasFilter(b.Left) || HasFilter(b.Right),
        FunctionNode f => f.Arguments.Any(HasFilter),
        _ => false,
    };
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportTrialBalance)]
public sealed record GetReportDefinitions : IQuery<IReadOnlyList<ReportDefinitionItemDto>>;

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportTrialBalance)]
public sealed record GetReportDefinition(int Id) : IQuery<ReportDefinitionDto>;

/// <summary>Salveaza un raport al clientului (decizia 4, drept separat); formulele se valideaza (51974), cele livrate nu se modifica (51975).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportDefine)]
[Audited("Fin.Report.Define")]
public sealed record SaveReportDefinition(int? Id, byte[]? RowVersion, string Code, string Name, int? ChartId, IReadOnlyList<ReportRowDto> Rows) : ICommand<int>;

internal sealed class SaveReportDefinitionValidator : AbstractValidator<SaveReportDefinition>
{
    public SaveReportDefinitionValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Rows).NotEmpty().Must(r => r.Select(x => x.RowCode).Distinct(StringComparer.Ordinal).Count() == r.Count)
            .WithMessage("Codurile rândurilor sunt unice.");
        RuleForEach(x => x.Rows).Must(r => !string.IsNullOrWhiteSpace(r.RowCode) && r.RowCode.Length <= 30 && !string.IsNullOrWhiteSpace(r.Label))
            .WithMessage("Fiecare rând are cod și denumire.");
    }
}

/// <summary>Ruleaza un raport definit pe o perioada (intr-un an financiar), cu perioada de comparatie optionala (anul precedent).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportTrialBalance)]
public sealed record RunReport(int DefinitionId, DateOnly From, DateOnly To, DateOnly? PreviousFrom = null, DateOnly? PreviousTo = null) : IQuery<ReportResultDto>;

internal sealed class ReportDefinitionHandlers(IReportDefinitionStore store, ReportEngine engine, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetReportDefinitions, IReadOnlyList<ReportDefinitionItemDto>>, IQueryHandler<GetReportDefinition, ReportDefinitionDto>,
    ICommandHandler<SaveReportDefinition, int>, IQueryHandler<RunReport, ReportResultDto>
{
    public Task<IReadOnlyList<ReportDefinitionItemDto>> Handle(GetReportDefinitions request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, cancellationToken);

    public async Task<ReportDefinitionDto> Handle(GetReportDefinition request, CancellationToken cancellationToken) =>
        (await Load(request.Id, cancellationToken).ConfigureAwait(false)).Definition;

    public async Task<int> Handle(SaveReportDefinition request, CancellationToken cancellationToken)
    {
        ReportEngine.Validate(request.Rows, await store.AccountCodesAsync(context.CompanyId, request.ChartId, cancellationToken).ConfigureAwait(false));
        var id = await store.SaveAsync(context.CompanyId, request.Id, request.RowVersion, request.Code.Trim(), request.Name.Trim(), request.ChartId, request.Rows,
            context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.ReportDefinition", id, new { request.Code, Rows = request.Rows.Count });
        return id;
    }

    public async Task<ReportResultDto> Handle(RunReport request, CancellationToken cancellationToken)
    {
        var data = await Load(request.DefinitionId, cancellationToken).ConfigureAwait(false);
        var current = await engine.RunAsync(context.CompanyId, data, request.From, request.To, cancellationToken).ConfigureAwait(false);
        var previous = request.PreviousFrom is { } pf && request.PreviousTo is { } pt
            ? await engine.RunAsync(context.CompanyId, data, pf, pt, cancellationToken).ConfigureAwait(false)
            : null;
        var d = data.Definition;
        return new ReportResultDto(d.Id, d.Code, d.Name, request.From, request.To, request.PreviousFrom, request.PreviousTo, d.Rows.Select(r => new ReportResultRowDto(
            r.RowCode, r.Label, r.IsTotal, r.Indent, current.TryGetValue(r.RowCode, out var c) ? Money.Format(c) : null,
            previous is not null && previous.TryGetValue(r.RowCode, out var p) ? Money.Format(p) : null)).ToList());
    }

    private async Task<ReportDefinitionData> Load(int id, CancellationToken ct) =>
        await store.GetAsync(context.CompanyId, id, null, null, ct).ConfigureAwait(false)
        ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_REPORT_NOT_FOUND", "Raportul nu există.");
}
