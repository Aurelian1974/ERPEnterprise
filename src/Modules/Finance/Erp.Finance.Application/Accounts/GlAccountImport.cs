using System.Text.RegularExpressions;
using Erp.Core.Contracts;
using Erp.Finance.Application.Settlement;

namespace Erp.Finance.Application.Accounts;

/// <summary>Foaia de import a conturilor analitice (C1 4.1 pasul 2, 4.6): codul, denumirea, moneda optionala si restrictia pe firma.</summary>
public static class GlAccountSheet
{
    public const string SheetName = "Conturi analitice";
    public const int MaxRows = 5000;

    public static readonly IReadOnlyList<SpreadsheetColumn> Columns =
    [
        new("Code", "Cont", true, "5121.01"),
        new("Name", "Denumire", true, "BCR RON"),
        new("Currency", "Moneda (doar pentru conturi într-o singură monedă)", false, ""),
        new("OnlyCompany", "Doar firma curentă", false, "Nu"),
    ];

    public static readonly IReadOnlyList<SpreadsheetList> Lists = [new("Da / Nu", ["Da", "Nu"], ["OnlyCompany"])];
}

public sealed record GlAccountImportIssueDto(int Row, string? Code, string Message);

/// <summary>
/// Rezultatul importului de analitice: randurile citite, cele valide, cele care exista deja (nu se importa din nou), cele cu erori si cele
/// importate (0 la verificare); fisierul cu randurile respinse, cu motivul, de descarcat.
/// </summary>
public sealed record GlAccountImportDto(int TotalRows, int ValidRows, int ExistingRows, int ErrorRows, int ImportedRows,
    IReadOnlyList<GlAccountImportIssueDto> Errors, IReadOnlyList<GlAccountImportIssueDto> Warnings, DownloadedFile? ErrorFile);

/// <summary>Sablonul Excel pentru importul conturilor analitice.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountManage)]
public sealed record GetGlAccountImportTemplate : IQuery<DownloadedFile>;

internal sealed class GetGlAccountImportTemplateHandler(ISpreadsheetFiles files) : IQueryHandler<GetGlAccountImportTemplate, DownloadedFile>
{
    public Task<DownloadedFile> Handle(GetGlAccountImportTemplate request, CancellationToken cancellationToken) =>
        Task.FromResult(new DownloadedFile("Conturi analitice.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            files.Write(GlAccountSheet.SheetName, GlAccountSheet.Columns, [GlAccountSheet.Columns.Select(c => (string?)c.Example).ToList()],
                GlAccountSheet.Lists)));
}

/// <summary>
/// Importa conturi analitice din Excel (C1 4.1 pasul 2, 4.6): parintele e codul de dinaintea ultimului punct, cont existent sau rand anterior
/// din fisier; un rand cu parintele lipsa sau gresit e respins, celelalte se importa; un cont existent se sare (reluarea fisierului nu
/// dubleaza). Confirm = false doar verifica fisierul.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.AccountManage)]
[Audited("Fin.GlAccount.Import")]
public sealed record ImportGlAccounts(string FileName, string FileBase64, bool Confirm) : ICommand<GlAccountImportDto>;

internal sealed partial class ImportGlAccountsHandler(ISpreadsheetFiles files, IChartOfAccountsStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ImportGlAccounts, GlAccountImportDto>
{
    private const int MaxFileBytes = 10 * 1024 * 1024;

    [GeneratedRegex("^[0-9]{3,4}(\\.[0-9A-Za-z]{1,10})+$")]
    private static partial Regex AnalyticCode();

    public async Task<GlAccountImportDto> Handle(ImportGlAccounts request, CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.FileBase64);
        }
        catch (FormatException)
        {
            throw File("Fișierul nu a putut fi citit.");
        }

        if (content.Length == 0 || content.Length > MaxFileBytes)
        {
            throw File("Fișierul este gol sau depășește 10 MB.");
        }

        var rows = files.Read(GlAccountSheet.SheetName, GlAccountSheet.Columns, content);
        if (rows.Count > GlAccountSheet.MaxRows)
        {
            throw File($"Fișierul are peste {GlAccountSheet.MaxRows} de rânduri.");
        }

        var existing = (await store.AccountsAsync(null, context.CompanyId, true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(a => a.Code.ToUpperInvariant(), StringComparer.Ordinal);
        var errors = new List<GlAccountImportIssueDto>();
        var warnings = new List<GlAccountImportIssueDto>();
        var errorRows = new List<(IReadOnlyList<string?> Values, string Message)>();
        var valid = new List<(int Row, string Code, string Name, string? Currency, bool OnlyCompany, string Parent)>();
        var inFile = new HashSet<string>(StringComparer.Ordinal);

        // Parintii scrisi in fisier dupa copiii lor sunt gasiti: randurile se verifica in ordinea lungimii codului.
        foreach (var row in rows.OrderBy(r => (r.Values[0] ?? "").Trim().Length).ThenBy(r => r.RowNumber))
        {
            var code = (row.Values[0] ?? "").Trim();
            var name = (row.Values[1] ?? "").Trim();
            var currency = string.IsNullOrWhiteSpace(row.Values[2]) ? null : row.Values[2]!.Trim().ToUpperInvariant();
            var only = (row.Values[3] ?? "").Trim().ToUpperInvariant() is "DA" or "D" or "1" or "TRUE";
            var key = code.ToUpperInvariant();
            string? problem = null;
            var parent = code.Contains('.', StringComparison.Ordinal) ? key[..key.LastIndexOf('.')] : "";
            if (!AnalyticCode().IsMatch(code))
            {
                problem = "Codul are simbolul sintetic, apoi analiticul după punct (de exemplu 5121.01).";
            }
            else if (name.Length is 0 or > 200)
            {
                problem = "Denumirea lipsește sau depășește 200 de caractere.";
            }
            else if (currency is not null && currency.Length != 3)
            {
                problem = "Moneda are trei litere (de exemplu EUR).";
            }
            else if (!inFile.Add(key))
            {
                problem = "Contul apare de mai multe ori în fișier.";
            }
            else if (existing.ContainsKey(key))
            {
                warnings.Add(new GlAccountImportIssueDto(row.RowNumber, code, "Contul există deja; nu se importă din nou."));
                continue;
            }
            else if (!(existing.TryGetValue(parent, out var p) && p.IsActive && p.Level >= 3 && (p.CompanyId is null || p.CompanyId == context.CompanyId))
                     && !valid.Any(v => string.Equals(v.Code, parent, StringComparison.OrdinalIgnoreCase)))
            {
                problem = $"Contul părinte {parent} nu există în plan sau în fișier.";
            }

            if (problem is not null)
            {
                errors.Add(new GlAccountImportIssueDto(row.RowNumber, code, problem));
                errorRows.Add((row.Values, problem));
                continue;
            }

            valid.Add((row.RowNumber, code, name, currency, only, parent));
        }

        var imported = 0;
        if (request.Confirm)
        {
            var ids = existing.ToDictionary(e => e.Key, e => e.Value.Id, StringComparer.Ordinal);
            foreach (var v in valid)
            {
                var saved = await store.SaveAsync(new GlAccountRecord(null, ids[v.Parent], v.Code, v.Name, v.OnlyCompany ? context.CompanyId : null,
                    v.Currency is null ? (byte)0 : (byte)2, v.Currency, 0, true, true, 1, null, null, null), context.UserId, cancellationToken).ConfigureAwait(false);
                ids[v.Code.ToUpperInvariant()] = saved.Id;
                imported++;
            }

            audit.Describe("Fin.GlAccount", context.CompanyId, new { request.FileName, Rows = rows.Count, Imported = imported, Errors = errors.Count });
        }

        var errorFile = errorRows.Count == 0 ? null : new DownloadedFile("Erori conturi analitice.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            files.Write(GlAccountSheet.SheetName, GlAccountSheet.Columns, [.. errorRows.Select(r => r.Values)], GlAccountSheet.Lists,
                [.. errorRows.Select(r => r.Message)]));
        return new GlAccountImportDto(rows.Count, valid.Count, warnings.Count, errors.Count, imported, [.. errors.OrderBy(e => e.Row)],
            [.. warnings.OrderBy(w => w.Row)], errorFile);
    }

    private static ErpValidationException File(string message) => new(new Dictionary<string, string[]> { ["file"] = [message] });
}
