using System.Text.RegularExpressions;
using Erp.Core.Contracts;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Accounts;
using FluentValidation;

namespace Erp.Finance.Application.Accounts;

public sealed record AltChartDto(int Id, string Code, string Name, byte Kind, byte? Purpose, string LanguageCode, bool RequireCompleteMapping, bool IsActive,
    byte[] RowVersion, int AccountCount, int RuleCount, IReadOnlyList<AltChartCompanyDto> Companies);

public sealed record AltChartCompanyDto(int CompanyId, string CompanyName);

public sealed record MappingSourceDto(int AccountId, string Code);

public sealed record MappingTargetDto(int AccountId, string Code, string Name, string Percent);

/// <summary>O regula de mapare (macheta c1-03). SourceKind: 1 un cont, 2 interval, 3 lista; DimensionCondition: 0 fara, 1 valoare, 2 orice, 3 fara valoare.</summary>
public sealed record MappingRuleDto(int Id, int Priority, byte SourceKind, int? SourceAccountId, string? SourceAccountCode, string? SourceFrom, string? SourceTo,
    IReadOnlyList<MappingSourceDto> SourceAccounts, int? CompanyId, byte Side, byte? PartnerKind, int? PartnerGroupId, int? PartnerId, string? PartnerName,
    string? DimensionType, byte DimensionCondition, int? DimensionValueId, string? DimensionValueCode, string? DimensionValueName, int? ItemGroupId,
    string? CurrencyCode, byte BalanceSign, DateOnly ValidFrom, DateOnly? ValidTo, bool IsActive, string? Note, byte[] RowVersion,
    IReadOnlyList<MappingTargetDto> Targets);

public sealed record MappingTargetInput(int AccountId, string Percent);

public sealed record MappingRuleRecord(int? Id, int ChartId, int Priority, byte SourceKind, int? SourceAccountId, string? SourceFrom, string? SourceTo,
    IReadOnlyList<int> SourceAccounts, int? CompanyId, byte Side, byte? PartnerKind, int? PartnerGroupId, int? PartnerId, string? DimensionType,
    byte DimensionCondition, int? DimensionValueId, int? ItemGroupId, string? CurrencyCode, byte BalanceSign, DateOnly ValidFrom, DateOnly? ValidTo,
    bool IsActive, string? Note, IReadOnlyList<(int AccountId, decimal Percent)> Targets, byte[]? RowVersion);

public sealed record SimulationDto(int? RuleId, int? Priority, string? Note, IReadOnlyList<MappingTargetDto> Targets);

public sealed record MappingOverlapDto(int WinnerId, int WinnerPriority, int LoserId, int LoserPriority, bool IsDead, IReadOnlyList<string> CommonAccounts);

public sealed record AltAccountRefDto(int Id, string Code, string Name);

/// <summary>Verificarea unui plan alternativ fara liniile de nota (C1 4.5): suprapunerile, regulile moarte si conturile fara sursa.</summary>
public sealed record MappingCheckDto(int ChartId, IReadOnlyList<MappingOverlapDto> Overlaps, IReadOnlyList<AltAccountRefDto> AccountsWithoutSource);

/// <summary>Planurile alternative si regulile de mapare prin procedurile fin.usp_AltChart*, fin.usp_AccountMappingRule*, fin.usp_AltMapping* (NET-06).</summary>
public interface IAltMappingStore
{
    Task<AltChartDto?> GetChartAsync(int id, CancellationToken ct);

    Task<FinanceSaved> SaveChartAsync(int? id, string code, string name, byte purpose, string languageCode, bool requireCompleteMapping, bool isActive,
        IReadOnlyList<int> companies, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<FinanceSaved> SaveAccountAsync(int? id, int chartId, int? parentId, string code, string name, bool isActive, byte[]? rowVersion, int userId,
        CancellationToken ct);

    Task<IReadOnlyList<MappingRuleDto>> RulesAsync(int chartId, CancellationToken ct);

    Task<FinanceSaved> SaveRuleAsync(MappingRuleRecord rule, int userId, CancellationToken ct);

    Task ReorderAsync(int chartId, IReadOnlyList<(int Id, int Priority)> order, int userId, CancellationToken ct);

    Task<SimulationDto> SimulateAsync(int chartId, int companyId, string accountCode, byte side, int? partnerId, int? costCenterId, int? projectId,
        int? warehouseId, int? siteId, int? itemGroupId, string? currencyCode, DateOnly onDate, CancellationToken ct);

    Task<IReadOnlyList<AltAccountRefDto>> AccountsWithoutSourceAsync(int chartId, CancellationToken ct);
}

internal static class AltMappingRules
{
    public static ErpException NotFound() => new(ErpErrorKind.NotFound, "FIN_ACCOUNT_NOT_FOUND", "Planul nu există.");
}

// ---------- Planuri alternative ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountView)]
public sealed record GetAltChart(int Id) : IQuery<AltChartDto>;

internal sealed class GetAltChartHandler(IAltMappingStore store) : IQueryHandler<GetAltChart, AltChartDto>
{
    public async Task<AltChartDto> Handle(GetAltChart request, CancellationToken cancellationToken) =>
        await store.GetChartAsync(request.Id, cancellationToken).ConfigureAwait(false) ?? throw AltMappingRules.NotFound();
}

/// <summary>Adauga sau modifica un plan alternativ (C1 3.1, 4.2): cod, denumire, scop (1 grup, 2 IFRS, 3 compania-mama, 4 gestiune, 5 altul), limba, firme.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ChartManage)]
[Audited("Fin.Chart.Save")]
public sealed record SaveAltChart(int? Id, string Code, string Name, byte Purpose, string LanguageCode, bool RequireCompleteMapping, bool IsActive,
    IReadOnlyList<int> Companies, byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed class SaveAltChartValidator : AbstractValidator<SaveAltChart>
{
    public SaveAltChartValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9_-]+$").WithMessage("Codul are litere, cifre, cratimă sau liniuță jos.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Purpose).InclusiveBetween((byte)1, (byte)5);
        RuleFor(x => x.LanguageCode).NotEmpty().Length(2);
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null);
    }
}

internal sealed class SaveAltChartHandler(IAltMappingStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveAltChart, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveAltChart request, CancellationToken cancellationToken)
    {
        var saved = await store.SaveChartAsync(request.Id, request.Code.Trim().ToUpperInvariant(), request.Name.Trim(), request.Purpose,
            request.LanguageCode.Trim().ToLowerInvariant(), request.RequireCompleteMapping, request.IsActive, request.Companies.Distinct().ToList(),
            request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.ChartOfAccounts", saved.Id, request with { RowVersion = null });
        return saved;
    }
}

/// <summary>Adauga sau modifica un cont al unui plan alternativ (C1 4.2): cod liber, parinte din acelasi plan.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ChartManage)]
[Audited("Fin.AltGlAccount.Save")]
public sealed record SaveAltGlAccount(int? Id, int ChartId, int? ParentId, string Code, string Name, bool IsActive = true, byte[]? RowVersion = null)
    : ICommand<FinanceSaved>;

internal sealed class SaveAltGlAccountValidator : AbstractValidator<SaveAltGlAccount>
{
    public SaveAltGlAccountValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null);
    }
}

internal sealed class SaveAltGlAccountHandler(IAltMappingStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveAltGlAccount, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveAltGlAccount request, CancellationToken cancellationToken)
    {
        var saved = await store.SaveAccountAsync(request.Id, request.ChartId, request.ParentId, request.Code.Trim(), request.Name.Trim(), request.IsActive,
            request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.GlAccount", saved.Id, request with { RowVersion = null });
        return saved;
    }
}

/// <summary>Foaia de import a conturilor unui plan alternativ (C1 4.2): cod, denumire, cont parinte (cod din acelasi plan sau din fisier).</summary>
public static class AltAccountSheet
{
    public const string SheetName = "Conturi plan alternativ";

    public static readonly IReadOnlyList<SpreadsheetColumn> Columns =
    [
        new("Code", "Cont", true, "1200"),
        new("Name", "Denumire", true, "Trade receivables"),
        new("Parent", "Cont părinte", false, "1000"),
    ];
}

/// <summary>
/// Importa conturile unui plan alternativ din Excel (C1 4.2, 4.6): parintele e un cont al planului sau un rand din fisier (in orice ordine);
/// randul cu parintele lipsa e respins, celelalte se importa; conturile existente se sar. Confirm = false doar verifica.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.ChartManage)]
[Audited("Fin.AltGlAccount.Import")]
public sealed record ImportAltGlAccounts(int ChartId, string FileName, string FileBase64, bool Confirm) : ICommand<GlAccountImportDto>;

internal sealed class ImportAltGlAccountsHandler(ISpreadsheetFiles files, IChartOfAccountsStore accounts, IAltMappingStore store, ICurrentContext context,
    IAuditScope audit) : ICommandHandler<ImportAltGlAccounts, GlAccountImportDto>
{
    public async Task<GlAccountImportDto> Handle(ImportAltGlAccounts request, CancellationToken cancellationToken)
    {
        if ((await store.GetChartAsync(request.ChartId, cancellationToken).ConfigureAwait(false)) is not { Kind: 2 })
        {
            throw new ErpException(ErpErrorKind.Forbidden, "FIN_CHART_STATUTORY_READONLY", "Importul e pentru conturile unui plan alternativ.");
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.FileBase64);
        }
        catch (FormatException)
        {
            throw new ErpValidationException(new Dictionary<string, string[]> { ["file"] = ["Fișierul nu a putut fi citit."] });
        }

        var rows = files.Read(AltAccountSheet.SheetName, AltAccountSheet.Columns, content);
        var existing = (await accounts.AccountsAsync(request.ChartId, context.CompanyId, true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(a => a.Code.ToUpperInvariant(), a => a.Id, StringComparer.Ordinal);
        var parsed = rows.Select(r => (Row: r, Code: (r.Values[0] ?? "").Trim(), Name: (r.Values[1] ?? "").Trim(), Parent: (r.Values[2] ?? "").Trim()))
            .ToList();
        var inFile = parsed.Where(p => p.Code.Length > 0).GroupBy(p => p.Code.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var errors = new List<GlAccountImportIssueDto>();
        var warnings = new List<GlAccountImportIssueDto>();
        var errorRows = new List<(IReadOnlyList<string?> Values, string Message)>();
        var valid = new List<(int Row, string Code, string Name, string Parent)>();
        foreach (var p in parsed)
        {
            var key = p.Code.ToUpperInvariant();
            var problem = p.Code.Length is 0 or > 20 ? "Codul lipsește sau depășește 20 de caractere."
                : p.Name.Length is 0 or > 200 ? "Denumirea lipsește sau depășește 200 de caractere."
                : inFile[key] > 1 ? "Contul apare de mai multe ori în fișier."
                : p.Parent.Length > 0 && !existing.ContainsKey(p.Parent.ToUpperInvariant()) && !inFile.ContainsKey(p.Parent.ToUpperInvariant())
                    ? $"Contul părinte {p.Parent} nu există în plan sau în fișier." : null;
            if (problem is not null)
            {
                errors.Add(new GlAccountImportIssueDto(p.Row.RowNumber, p.Code, problem));
                errorRows.Add((p.Row.Values, problem));
            }
            else if (existing.ContainsKey(key))
            {
                warnings.Add(new GlAccountImportIssueDto(p.Row.RowNumber, p.Code, "Contul există deja; nu se importă din nou."));
            }
            else
            {
                valid.Add((p.Row.RowNumber, p.Code, p.Name, p.Parent));
            }
        }

        var imported = 0;
        if (request.Confirm)
        {
            // Parintii inaintea copiilor: un cont se creeaza dupa ce parintele lui exista.
            var pending = valid.ToList();
            while (pending.Count > 0)
            {
                var ready = pending.Where(v => v.Parent.Length == 0 || existing.ContainsKey(v.Parent.ToUpperInvariant())).ToList();
                if (ready.Count == 0)
                {
                    foreach (var v in pending)
                    {
                        errors.Add(new GlAccountImportIssueDto(v.Row, v.Code, "Contul părinte face parte dintr-un ciclu în fișier."));
                    }

                    break;
                }

                foreach (var v in ready)
                {
                    var saved = await store.SaveAccountAsync(null, request.ChartId, v.Parent.Length == 0 ? null : existing[v.Parent.ToUpperInvariant()], v.Code,
                        v.Name, true, null, context.UserId, cancellationToken).ConfigureAwait(false);
                    existing[v.Code.ToUpperInvariant()] = saved.Id;
                    imported++;
                }

                pending = pending.Except(ready).ToList();
            }

            audit.Describe("Fin.ChartOfAccounts", request.ChartId, new { request.FileName, Rows = rows.Count, Imported = imported, Errors = errors.Count });
        }

        var errorFile = errorRows.Count == 0 ? null : new DownloadedFile("Erori conturi plan alternativ.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            files.Write(AltAccountSheet.SheetName, AltAccountSheet.Columns, [.. errorRows.Select(r => r.Values)], [], [.. errorRows.Select(r => r.Message)]));
        return new GlAccountImportDto(rows.Count, valid.Count, warnings.Count, errors.Count, imported, [.. errors.OrderBy(e => e.Row)],
            [.. warnings.OrderBy(w => w.Row)], errorFile);
    }
}

// ---------- Reguli de mapare ----------

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountView)]
public sealed record GetMappingRules(int ChartId) : IQuery<IReadOnlyList<MappingRuleDto>>;

internal sealed class GetMappingRulesHandler(IAltMappingStore store) : IQueryHandler<GetMappingRules, IReadOnlyList<MappingRuleDto>>
{
    public Task<IReadOnlyList<MappingRuleDto>> Handle(GetMappingRules request, CancellationToken cancellationToken) =>
        store.RulesAsync(request.ChartId, cancellationToken);
}

/// <summary>
/// Adauga sau modifica o regula de mapare (C1 3.5): prioritatea unica, sursa (un cont, interval de coduri normalizate, lista), conditiile pe
/// linie sau semnul soldului, vigoarea si tintele cu procente care fac 100. Recalcularea liniilor deja mapate vine cu notele contabile (C1b).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.MappingManage)]
[Audited("Fin.MappingRule.Save")]
public sealed record SaveMappingRule(int? Id, int ChartId, int Priority, byte SourceKind, int? SourceAccountId, string? SourceFrom, string? SourceTo,
    IReadOnlyList<int>? SourceAccounts, int? CompanyId, byte Side, byte? PartnerKind, int? PartnerGroupId, int? PartnerId, string? DimensionType,
    byte DimensionCondition, int? DimensionValueId, int? ItemGroupId, string? CurrencyCode, byte BalanceSign, DateOnly ValidFrom, DateOnly? ValidTo,
    bool IsActive, string? Note, IReadOnlyList<MappingTargetInput> Targets, byte[]? RowVersion) : ICommand<FinanceSaved>;

internal sealed partial class SaveMappingRuleValidator : AbstractValidator<SaveMappingRule>
{
    public SaveMappingRuleValidator()
    {
        RuleFor(x => x.Priority).GreaterThan(0);
        RuleFor(x => x.SourceKind).InclusiveBetween((byte)1, (byte)3);
        RuleFor(x => x.SourceAccountId).NotNull().When(x => x.SourceKind == 1).WithMessage("Alegeți contul sursă.");
        RuleFor(x => x.SourceFrom).NotEmpty().Must(c => c is not null && Code().IsMatch(c)).When(x => x.SourceKind == 2)
            .WithMessage("Intervalul are simboluri de cont (de exemplu 6000).");
        RuleFor(x => x.SourceTo).NotEmpty().Must(c => c is not null && Code().IsMatch(c)).When(x => x.SourceKind == 2)
            .WithMessage("Intervalul are simboluri de cont (de exemplu 6999).");
        RuleFor(x => x.SourceAccounts).NotEmpty().When(x => x.SourceKind == 3).WithMessage("Alegeți conturile sursă.");
        RuleFor(x => x.Side).InclusiveBetween((byte)0, (byte)2);
        RuleFor(x => x.PartnerKind).InclusiveBetween((byte)1, (byte)4).When(x => x.PartnerKind is not null);
        RuleFor(x => x.DimensionCondition).InclusiveBetween((byte)0, (byte)3);
        RuleFor(x => x.DimensionType).NotEmpty().When(x => x.DimensionCondition > 0).WithMessage("Alegeți dimensiunea condiției.");
        RuleFor(x => x.DimensionValueId).NotNull().When(x => x.DimensionCondition == 1).WithMessage("Alegeți valoarea dimensiunii.");
        RuleFor(x => x.BalanceSign).InclusiveBetween((byte)0, (byte)2);
        RuleFor(x => x).Must(x => x.BalanceSign == 0 || (x.Side == 0 && x.PartnerKind is null && x.PartnerGroupId is null && x.PartnerId is null
                && x.DimensionCondition == 0 && x.ItemGroupId is null && x.CurrencyCode is null))
            .WithMessage("O regulă pe semnul soldului nu are condiții de linie.");
        RuleFor(x => x.ValidTo).GreaterThanOrEqualTo(x => x.ValidFrom).When(x => x.ValidTo is not null);
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.Targets).NotEmpty().WithMessage("Alegeți cel puțin un cont alternativ.");
        RuleForEach(x => x.Targets).Must(t => Money.ParseOptional(t.Percent) is > 0 and <= 100).WithMessage("Procentul e între 0 și 100.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null);
    }

    [GeneratedRegex("^[0-9]{1,6}$")]
    private static partial Regex Code();
}

internal sealed class SaveMappingRuleHandler(IAltMappingStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveMappingRule, FinanceSaved>
{
    public async Task<FinanceSaved> Handle(SaveMappingRule request, CancellationToken cancellationToken)
    {
        var range = request.SourceKind == 2;
        var (from, to) = range ? (MappingRules.Normalize(request.SourceFrom!.Trim()), MappingRules.Normalize(request.SourceTo!.Trim())) : (null, null);
        if (range && string.CompareOrdinal(from, to) > 0)
        {
            throw new ErpValidationException(new Dictionary<string, string[]> { ["sourceTo"] = ["Sfârșitul intervalului e înaintea începutului."] });
        }

        var saved = await store.SaveRuleAsync(new MappingRuleRecord(request.Id, request.ChartId, request.Priority, request.SourceKind,
            request.SourceKind == 1 ? request.SourceAccountId : null, from, to, request.SourceKind == 3 ? request.SourceAccounts ?? [] : [],
            request.CompanyId, request.Side, request.PartnerKind, request.PartnerGroupId, request.PartnerId,
            request.DimensionCondition == 0 ? null : request.DimensionType, request.DimensionCondition,
            request.DimensionCondition == 1 ? request.DimensionValueId : null, request.ItemGroupId,
            string.IsNullOrWhiteSpace(request.CurrencyCode) ? null : request.CurrencyCode.Trim().ToUpperInvariant(), request.BalanceSign, request.ValidFrom,
            request.ValidTo, request.IsActive, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            request.Targets.Select(t => (t.AccountId, Money.ParseOptional(t.Percent)!.Value)).ToList(), request.RowVersion), context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Fin.MappingRule", saved.Id, request with { RowVersion = null });
        return saved;
    }
}

public sealed record RulePriorityInput(int Id, int Priority);

/// <summary>Renumeroteaza prioritatile regulilor unui plan (macheta c1-03: mutarea unui rand).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.MappingManage)]
[Audited("Fin.MappingRule.Reorder")]
public sealed record ReorderMappingRules(int ChartId, IReadOnlyList<RulePriorityInput> Order) : ICommand<bool>;

internal sealed class ReorderMappingRulesHandler(IAltMappingStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<ReorderMappingRules, bool>
{
    public async Task<bool> Handle(ReorderMappingRules request, CancellationToken cancellationToken)
    {
        await store.ReorderAsync(request.ChartId, request.Order.Select(o => (o.Id, o.Priority)).ToList(), context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.ChartOfAccounts", request.ChartId, request.Order);
        return true;
    }
}

/// <summary>Simulatorul (C1 4.5): regula si contul alternativ pentru un cont romanesc si atributele unei linii, fara sa scrie nimic.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountView)]
public sealed record SimulateMapping(int ChartId, string AccountCode, byte Side = 0, int? PartnerId = null, int? CostCenterId = null, int? ProjectId = null,
    int? WarehouseId = null, int? SiteId = null, int? ItemGroupId = null, string? CurrencyCode = null, DateOnly? OnDate = null) : IQuery<SimulationDto>;

internal sealed class SimulateMappingHandler(IAltMappingStore store, ICurrentContext context, TimeProvider time) : IQueryHandler<SimulateMapping, SimulationDto>
{
    public Task<SimulationDto> Handle(SimulateMapping request, CancellationToken cancellationToken) =>
        store.SimulateAsync(request.ChartId, context.CompanyId, request.AccountCode.Trim(), request.Side, request.PartnerId, request.CostCenterId,
            request.ProjectId, request.WarehouseId, request.SiteId, request.ItemGroupId,
            string.IsNullOrWhiteSpace(request.CurrencyCode) ? null : request.CurrencyCode.Trim().ToUpperInvariant(),
            request.OnDate ?? Treasury.BusinessDay.Today(time), cancellationToken);
}

/// <summary>
/// Verificarea unui plan alternativ (C1 4.5; macheta c1-04): perechile de reguli active care se suprapun, cu cea care castiga si cea care nu
/// se mai aplica niciodata, si conturile alternative pe care nu le atinge nicio regula. Nemapatele cu rulaj vin cu notele contabile (C2).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountView)]
public sealed record CheckMapping(int ChartId) : IQuery<MappingCheckDto>;

internal sealed class CheckMappingHandler(IAltMappingStore store, IChartOfAccountsStore accounts, ICurrentContext context)
    : IQueryHandler<CheckMapping, MappingCheckDto>
{
    public async Task<MappingCheckDto> Handle(CheckMapping request, CancellationToken cancellationToken)
    {
        var rules = (await store.RulesAsync(request.ChartId, cancellationToken).ConfigureAwait(false)).Where(r => r.IsActive && r.BalanceSign == 0).ToList();
        var leaves = (await accounts.AccountsAsync(null, context.CompanyId, false, cancellationToken).ConfigureAwait(false))
            .Where(a => a.IsPostable).Select(a => a.Code).ToList();
        var shapes = rules.Select(r => new MappingRuleShape(r.Id, r.Priority, Sources(r, leaves), r.CompanyId, r.Side, r.PartnerKind, r.PartnerGroupId,
            r.PartnerId, r.DimensionType, r.DimensionCondition, r.DimensionValueId, r.ItemGroupId, r.CurrencyCode, r.ValidFrom, r.ValidTo)).ToList();
        var priority = rules.ToDictionary(r => r.Id, r => r.Priority);
        var overlaps = MappingRules.Overlaps(shapes)
            .Select(o => new MappingOverlapDto(o.WinnerId, priority[o.WinnerId], o.LoserId, priority[o.LoserId], o.IsDead, o.CommonAccounts)).ToList();
        return new MappingCheckDto(request.ChartId, overlaps, await store.AccountsWithoutSourceAsync(request.ChartId, cancellationToken).ConfigureAwait(false));
    }

    private static HashSet<string> Sources(MappingRuleDto rule, IReadOnlyList<string> leaves) => rule.SourceKind switch
    {
        1 => leaves.Where(c => MappingRules.Covers(rule.SourceAccountCode!, c)).ToHashSet(StringComparer.OrdinalIgnoreCase),
        2 => leaves.Where(c => string.CompareOrdinal(MappingRules.Normalize(c), rule.SourceFrom) >= 0
            && string.CompareOrdinal(MappingRules.Normalize(c), rule.SourceTo) <= 0).ToHashSet(StringComparer.OrdinalIgnoreCase),
        _ => leaves.Where(c => rule.SourceAccounts.Any(s => MappingRules.Covers(s.Code, c))).ToHashSet(StringComparer.OrdinalIgnoreCase),
    };
}
