using System.Text.Json;
using Erp.Core.Contracts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Reporting;
using Erp.Finance.Domain;
using FluentValidation;

namespace Erp.Finance.Application.Reporting;

public sealed record StatementLineDto(string FormCode, string RowCode, string Label, bool IsTotal, string? CurrentYear, string? PriorYear);

/// <summary>
/// Situatiile financiare ale unui an (macheta c5-04): tipul (1 anuale, 2 semestriale), categoria entitatii (1 micro, 2 mica, 3 mijlocie,
/// 4 mare), starea, echilibrul bilantului, recipisa si liniile F10, F20 cu anul curent si cel precedent.
/// </summary>
public sealed record FinancialStatementDto(int Id, int Year, byte Kind, byte EntityCategory, byte Status, bool IsBalanced, DateTime CalculatedAt, DateTime? GeneratedAt,
    DateTime? SubmittedAt, string? RegistrationNumber, string? FileName, byte[] RowVersion, IReadOnlyList<StatementLineDto> Lines);

public sealed record StatementLine(string FormCode, string RowCode, string Label, int SortOrder, bool IsTotal, decimal? CurrentYear, decimal? PriorYear);

/// <summary>Situatiile financiare prin fin.usp_FinancialStatement_* (NET-06).</summary>
public interface IFinancialStatementStore
{
    Task<int> SaveAsync(int companyId, int year, byte kind, byte category, bool isBalanced, IReadOnlyList<StatementLine> lines, int userId, CancellationToken ct);

    Task<FinancialStatementDto?> GetAsync(int companyId, int year, byte kind, CancellationToken ct);

    Task<byte[]> TransitionAsync(int companyId, int year, byte kind, byte[] rowVersion, string action, string? fileName, byte[]? content, string? registrationNumber,
        int userId, CancellationToken ct);

    Task<(string FileName, byte[] Content)?> GetFileAsync(int companyId, int year, byte kind, CancellationToken ct);
}

/// <summary>
/// Categoria entitatii (C5 3.6) propusa din criteriile OMFP 1802/2014 pe anul curent (totalul activelor si cifra de afaceri, in lei; numarul
/// mediu de salariati vine cu HR): micro pana la 1,5 milioane / 3 milioane, mica pana la 20 / 40 de milioane, mijlocie pana la 86 / 172 de
/// milioane (doua criterii din trei; aici cele doua disponibile).
/// </summary>
public static class EntityCategories
{
    public static byte Propose(decimal totalAssets, decimal turnover) =>
        totalAssets <= 1_500_000m && turnover <= 3_000_000m ? (byte)1
        : totalAssets <= 20_000_000m && turnover <= 40_000_000m ? (byte)2
        : totalAssets <= 86_000_000m && turnover <= 172_000_000m ? (byte)3
        : (byte)4;
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportStatements)]
public sealed record GetFinancialStatements(int Year, byte Kind = 1) : IQuery<FinancialStatementDto?>;

/// <summary>Calculeaza situatiile (C5 4.2): F10 si F20 din definitiile livrate in vigoare, pe anul curent si pe cel precedent; ciorna oricand.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportStatements)]
[Audited("Fin.Statements.Calculate")]
public sealed record CalculateFinancialStatements(int Year, byte Kind = 1) : ICommand<int>;

/// <summary>Genereaza fisierul (decizia 5): doar pe anul inchis (51976) si cu bilantul echilibrat (51977).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportStatements)]
[Audited("Fin.Statements.Generate")]
public sealed record GenerateFinancialStatements(int Year, byte Kind, byte[] RowVersion) : ICommand<byte[]>;

/// <summary>Inregistreaza depunerea cu numarul din recipisa; publica SituatiiFinanciareDepuse (C3 propune blocarea anului).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportStatementsSubmit)]
[Audited("Fin.Statements.Submit")]
public sealed record SubmitFinancialStatements(int Year, byte Kind, byte[] RowVersion, string RegistrationNumber) : ICommand<byte[]>;

internal sealed class SubmitFinancialStatementsValidator : AbstractValidator<SubmitFinancialStatements>
{
    public SubmitFinancialStatementsValidator() => RuleFor(x => x.RegistrationNumber).NotEmpty().MaximumLength(50);
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ReportStatements)]
public sealed record DownloadFinancialStatements(int Year, byte Kind) : IQuery<Settlement.DownloadedFile>;

internal sealed class FinancialStatementHandlers(IFinancialStatementStore store, IReportDefinitionStore definitions, ReportEngine engine,
    IAccountingPeriodStore periods, IOutbox outbox, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetFinancialStatements, FinancialStatementDto?>, ICommandHandler<CalculateFinancialStatements, int>,
    ICommandHandler<GenerateFinancialStatements, byte[]>, ICommandHandler<SubmitFinancialStatements, byte[]>,
    IQueryHandler<DownloadFinancialStatements, Settlement.DownloadedFile>
{
    public static readonly string[] Forms = ["F10", "F20"];

    public Task<FinancialStatementDto?> Handle(GetFinancialStatements request, CancellationToken cancellationToken) =>
        store.GetAsync(context.CompanyId, request.Year, request.Kind, cancellationToken);

    public async Task<int> Handle(CalculateFinancialStatements request, CancellationToken cancellationToken)
    {
        var years = await periods.ListAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var year = years.SingleOrDefault(y => y.Year == request.Year)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_PERIOD_NOT_FOUND", "Anul financiar nu există.");
        var to = request.Kind == 2 ? year.StartDate.AddMonths(6).AddDays(-1) : year.EndDate;
        var prior = years.SingleOrDefault(y => y.Year == request.Year - 1);
        var lines = new List<StatementLine>();
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var form in Forms)
        {
            var data = await definitions.GetAsync(context.CompanyId, null, form, to, cancellationToken).ConfigureAwait(false)
                ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_REPORT_NOT_FOUND", $"Formularul {form} nu e livrat.");
            var current = await engine.RunAsync(context.CompanyId, data, year.StartDate, to, cancellationToken).ConfigureAwait(false);
            var previous = prior is null ? null : await engine.RunAsync(context.CompanyId, data, prior.StartDate,
                request.Kind == 2 ? prior.StartDate.AddMonths(6).AddDays(-1) : prior.EndDate, cancellationToken).ConfigureAwait(false);
            var order = 0;
            foreach (var r in data.Definition.Rows)
            {
                decimal? c = current.TryGetValue(r.RowCode, out var cv) ? cv : null;
                decimal? p = previous is not null && previous.TryGetValue(r.RowCode, out var pv) ? pv : null;
                lines.Add(new StatementLine(form, r.RowCode, r.Label, ++order, r.IsTotal, c, p));
                values[r.RowCode] = c ?? 0;
            }
        }

        var balanced = values.GetValueOrDefault("F10.90") == values.GetValueOrDefault("F10.91");
        var category = EntityCategories.Propose(values.GetValueOrDefault("F10.90"), values.GetValueOrDefault("F20.01"));
        var id = await store.SaveAsync(context.CompanyId, request.Year, request.Kind, category, balanced, lines, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FinancialStatement", id, new { request.Year, request.Kind, Balanced = balanced, Category = category });
        return id;
    }

    public async Task<byte[]> Handle(GenerateFinancialStatements request, CancellationToken cancellationToken)
    {
        var s = await store.GetAsync(context.CompanyId, request.Year, request.Kind, cancellationToken).ConfigureAwait(false)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_REPORT_NOT_FOUND", "Situațiile financiare nu sunt calculate.");
        var content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            s.Year, s.Kind, s.EntityCategory,
            Forms = s.Lines.GroupBy(l => l.FormCode).Select(g => new { Form = g.Key, Rows = g.Select(l => new { l.RowCode, l.CurrentYear, l.PriorYear }) }),
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        var version = await store.TransitionAsync(context.CompanyId, request.Year, request.Kind, request.RowVersion, "Generate", $"SF_{request.Year}_{request.Kind}.json",
            content, null, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FinancialStatement", s.Id, new { request.Year, request.Kind });
        return version;
    }

    public async Task<byte[]> Handle(SubmitFinancialStatements request, CancellationToken cancellationToken)
    {
        var version = await store.TransitionAsync(context.CompanyId, request.Year, request.Kind, request.RowVersion, "Submit", null, null,
            request.RegistrationNumber.Trim(), context.UserId, cancellationToken).ConfigureAwait(false);
        await outbox.EnqueueAsync(new SituatiiFinanciareDepuse(request.Year, request.Kind, request.RegistrationNumber.Trim()) { CompanyId = context.CompanyId },
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.FinancialStatement", request.Year, new { request.Kind, request.RegistrationNumber });
        return version;
    }

    public async Task<Settlement.DownloadedFile> Handle(DownloadFinancialStatements request, CancellationToken cancellationToken)
    {
        var f = await store.GetFileAsync(context.CompanyId, request.Year, request.Kind, cancellationToken).ConfigureAwait(false)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_REPORT_NOT_FOUND", "Fișierul nu a fost generat.");
        return new Settlement.DownloadedFile(f.FileName, "application/json", f.Content);
    }
}
