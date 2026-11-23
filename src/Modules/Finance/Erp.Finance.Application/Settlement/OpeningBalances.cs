using System.Globalization;
using System.Text;
using System.Text.Json;
using Erp.Core.Contracts;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Application.Settlement;

/// <summary>
/// Foaia Excel a soldurilor initiale (02.02, F3 4.9): un rand pe factura deschisa sau plata nealocata la data pornirii — partenerul (cod sau
/// CUI), rolul, tipul, numarul, data, scadenta, moneda si cursul, valoarea si restul, TVA la incasare si TVA-ul neexigibil ramas (lei).
/// </summary>
public static class OpeningSheet
{
    public const string EntityType = "FinOpeningItem";
    public const string SheetName = "Solduri initiale";
    public const string DocumentType = "Finance.OpeningBalance";
    public const int BatchSize = 500;

    public static readonly IReadOnlyList<SpreadsheetColumn> Columns =
    [
        new("Partner", "Partener (cod sau CUI)", true, "RO18547290"),
        new("Role", "Rol", true, "Client"),
        new("Kind", "Tip", false, "Factură"),
        new("Number", "Număr", true, "FV EXD 10162"),
        new("Date", "Data", true, "2026-09-20"),
        new("DueDate", "Scadența", false, "2026-10-20"),
        new("Currency", "Moneda", false, "RON"),
        new("Rate", "Curs", false, "1"),
        new("Amount", "Valoare", true, "21880.00"),
        new("Open", "Rest", true, "11880.00"),
        new("VatOnCollection", "TVA la încasare", false, "Nu"),
        new("VatOpen", "TVA neexigibil (lei)", false, "0"),
    ];

    public static readonly IReadOnlyList<SpreadsheetList> Lists =
    [
        new("Rol", ["Client", "Furnizor"], ["Role"]),
        new("Tip", ["Factură", "Plată nealocată"], ["Kind"]),
        new("Da / Nu", ["Da", "Nu"], ["VatOnCollection"]),
    ];

    public static int Index(string key)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Key == key)
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(key), key, "Coloana nu există.");
    }

    public static string Header(string key) => Columns[Index(key)].Header;

    /// <summary>Textul fara diacritice, spatii la capete si majuscule, pentru valorile din liste.</summary>
    public static string Fold(string? value)
    {
        var text = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    public static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    /// <summary>O suma scrisa cu punct sau cu virgula zecimala („1.234,56” sau „1234.56”).</summary>
    public static decimal? Number(string? value)
    {
        var text = value?.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Contains(',', StringComparison.Ordinal))
        {
            text = text.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>Cheia de idempotenta a partidei: firma, tipul si numarul (partenerul si rolul sunt in documentul sursa si in rol).</summary>
    public static string LineKey(int companyId, OpenItemKind kind, string number) =>
        string.Create(CultureInfo.InvariantCulture, $"{companyId}:{(byte)kind}:{number}");
}

/// <summary>Un rand valid, gata de inserat, cu semnul conventiei pe rol (debit +, credit −).</summary>
public sealed record OpeningItemRow(int RowNumber, int PartnerId, PartnerRole Role, OpenItemKind Kind, string LineKey, string DocumentNo, DateOnly DocumentDate,
    DateOnly DueDate, string CurrencyCode, decimal ExchangeRate, decimal Amount, decimal AmountRon, decimal VatAmountRon, bool IsVatOnCollection);

/// <summary>O eroare sau o avertizare a importului, pe rand si coloana (aceeasi forma ca la importurile Nucleului).</summary>
public sealed record OpeningIssueDto(int Row, string Column, string? Value, string Message);

/// <summary>Un rand cu erori, cu valorile din fisier, pentru descarcarea randurilor de corectat.</summary>
public sealed record OpeningErrorRow(int RowNumber, IReadOnlyList<string?> Values, string Message);

/// <summary>Totalul importat pe rol si moneda (soldul cu semnul conventiei), de comparat cu balanta de pornire (F3 4.9 pasul 3).</summary>
public sealed record OpeningTotalDto(PartnerRole Role, string CurrencyCode, int Rows, string Total, string TotalRon);

/// <summary>Raportul salvat pe job (compatibil cu raportul importurilor din Nucleu, plus totalurile).</summary>
public sealed record OpeningReport(IReadOnlyList<OpeningIssueDto> Errors, IReadOnlyList<OpeningIssueDto> Warnings, IReadOnlyList<OpeningErrorRow> ErrorRows,
    IReadOnlyList<OpeningTotalDto> Totals);

public sealed record OpeningImportDto(int JobId, string FileName, byte Status, int TotalRows, int ValidRows, int ErrorRows, int ExistingRows, int ImportedRows,
    string? LastError, DateTime CreatedAt, DateTime? CompletedAt, IReadOnlyList<OpeningIssueDto> Errors, IReadOnlyList<OpeningIssueDto> Warnings,
    IReadOnlyList<OpeningTotalDto> Totals);

public sealed record OpeningBatchResult(int ImportedRows, bool Done);

/// <summary>Accesul la date pentru soldurile initiale (fin.usp_OpeningBalance_*, NET-06).</summary>
public interface IOpeningBalanceStore
{
    Task<(IReadOnlyDictionary<int, (int? PartnerId, bool Exists)> Rows, IReadOnlySet<string> Currencies)> LookupAsync(int companyId,
        IReadOnlyList<(int Row, string Partner, PartnerRole Role, string LineKey)> keys, CancellationToken ct);

    Task<int> ImportAsync(int companyId, IReadOnlyList<OpeningItemRow> rows, int userId, CancellationToken ct);
}

internal static class OpeningJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ErpException NotFound() => ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Importul nu există.");

    public static ErpValidationException File(string message) => new(new Dictionary<string, string[]> { ["file"] = [message] });

    public static async Task<ImportJobState> JobAsync(IImportJobs jobs, ICurrentContext context, int id, bool forUpdate, CancellationToken ct)
    {
        var job = await jobs.GetAsync(id, forUpdate, ct).ConfigureAwait(false);
        return job is null || job.EntityType != OpeningSheet.EntityType || job.CompanyId != context.CompanyId ? throw NotFound() : job;
    }

    public static OpeningImportDto Dto(ImportJobState job)
    {
        var report = job.ReportJson is null ? null : JsonSerializer.Deserialize<OpeningReport>(job.ReportJson, Options);
        var existing = report?.Warnings.Select(w => w.Row).Distinct().Count() ?? 0;
        return new OpeningImportDto(job.Id, job.FileName, job.Status, job.TotalRows, job.ValidRows, job.ErrorRows, existing, job.ImportedRows, job.LastError,
            DateTime.SpecifyKind(job.CreatedAt, DateTimeKind.Utc), job.CompletedAt, report?.Errors ?? [], report?.Warnings ?? [], report?.Totals ?? []);
    }
}

// ---------- Sablon si validare ----------

/// <summary>Sablonul Excel al soldurilor initiale (F3 4.9 pasul 1), cu listele de valori.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
public sealed record GetOpeningBalanceTemplate : IQuery<DownloadedFile>;

internal sealed class GetOpeningBalanceTemplateHandler(ISpreadsheetFiles files) : IQueryHandler<GetOpeningBalanceTemplate, DownloadedFile>
{
    public Task<DownloadedFile> Handle(GetOpeningBalanceTemplate request, CancellationToken cancellationToken) =>
        Task.FromResult(new DownloadedFile("Sold initial parteneri.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            files.Write(OpeningSheet.SheetName, OpeningSheet.Columns, [OpeningSheet.Columns.Select(c => (string?)c.Example).ToList()], OpeningSheet.Lists)));
}

/// <summary>
/// Incarca si valideaza fisierul (02.01, 4.8 pasii 2–3): fiecare rand, partenerul gasit dupa cod sau CUI, randurile deja importate (avertizare,
/// nu se mai importa) si totalurile pe rol si moneda; randurile valide raman in jobul de import pana la confirmare.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
[Audited("Fin.OpeningBalance.Validate")]
public sealed record UploadOpeningBalances(string FileName, string FileBase64) : ICommand<OpeningImportDto>;

internal sealed class UploadOpeningBalancesHandler(ISpreadsheetFiles files, IOpeningBalanceStore store, IImportJobs jobs, ICurrentContext context,
    IAuditScope audit) : ICommandHandler<UploadOpeningBalances, OpeningImportDto>
{
    private const int MaxFileBytes = 10 * 1024 * 1024;

    public async Task<OpeningImportDto> Handle(UploadOpeningBalances request, CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.FileBase64);
        }
        catch (FormatException)
        {
            throw OpeningJson.File("Fișierul nu a putut fi citit.");
        }

        if (content.Length == 0 || content.Length > MaxFileBytes)
        {
            throw OpeningJson.File("Fișierul este gol sau depășește 10 MB.");
        }

        var rows = files.Read(OpeningSheet.SheetName, OpeningSheet.Columns, content);
        var parsed = rows.Select(r => OpeningRowParser.Parse(context.CompanyId, r)).ToList();
        var keys = parsed.Where(p => p.Draft is not null).Select(p => (p.Row.RowNumber, p.Draft!.Partner, p.Draft.Role, p.Draft.LineKey)).ToList();
        var (lookup, currencies) = await store.LookupAsync(context.CompanyId, keys, cancellationToken).ConfigureAwait(false);

        var errors = new List<OpeningIssueDto>();
        var warnings = new List<OpeningIssueDto>();
        var errorRows = new List<OpeningErrorRow>();
        var valid = new List<OpeningItemRow>();
        var seen = new HashSet<(int, PartnerRole, string)>();
        foreach (var (row, draft, issues) in parsed)
        {
            var rowIssues = issues.ToList();
            (int? PartnerId, bool Exists) found = draft is null ? (null, false) : lookup.GetValueOrDefault(row.RowNumber);
            if (draft is not null)
            {
                if (found.PartnerId is null)
                {
                    rowIssues.Add(new OpeningIssueDto(row.RowNumber, OpeningSheet.Header("Partner"), draft.Partner, "Partenerul nu există (cod sau CUI)."));
                }

                if (!currencies.Contains(draft.CurrencyCode))
                {
                    rowIssues.Add(new OpeningIssueDto(row.RowNumber, OpeningSheet.Header("Currency"), draft.CurrencyCode, "Moneda nu există."));
                }

                if (found.PartnerId is { } id && !seen.Add((id, draft.Role, draft.LineKey)))
                {
                    rowIssues.Add(new OpeningIssueDto(row.RowNumber, OpeningSheet.Header("Number"), draft.DocumentNo,
                        "Documentul apare de mai multe ori în fișier pentru același partener și rol."));
                }
            }

            if (rowIssues.Count > 0 || draft is null)
            {
                errors.AddRange(rowIssues);
                errorRows.Add(new OpeningErrorRow(row.RowNumber, row.Values, string.Join(" ", rowIssues.Select(i => i.Message))));
                continue;
            }

            if (found.Exists)
            {
                warnings.Add(new OpeningIssueDto(row.RowNumber, OpeningSheet.Header("Number"), draft.DocumentNo, "Partida a fost deja importată; nu se importă din nou."));
                continue;
            }

            valid.Add(draft.ToRow(row.RowNumber, found.PartnerId!.Value));
        }

        var totals = valid.GroupBy(v => (v.Role, v.CurrencyCode)).OrderBy(g => g.Key.Role).ThenBy(g => g.Key.CurrencyCode == "RON" ? 0 : 1)
            .ThenBy(g => g.Key.CurrencyCode, StringComparer.Ordinal)
            .Select(g => new OpeningTotalDto(g.Key.Role, g.Key.CurrencyCode, g.Count(), Money.Format(g.Sum(v => v.Amount)), Money.Format(g.Sum(v => v.AmountRon))))
            .ToList();
        var report = new OpeningReport(errors, warnings, errorRows, totals);
        var fileName = Path.GetFileName(request.FileName);
        var jobId = await jobs.CreateAsync(new ImportJobDraft(OpeningSheet.EntityType, fileName.Length > 200 ? fileName[..200] : fileName, rows.Count, valid.Count,
            errorRows.Count, warnings.Count, JsonSerializer.Serialize(report, OpeningJson.Options), JsonSerializer.Serialize(valid, OpeningJson.Options)),
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ImportJob", jobId, new { EntityType = OpeningSheet.EntityType, fileName, TotalRows = rows.Count, ValidRows = valid.Count });
        return OpeningJson.Dto(await OpeningJson.JobAsync(jobs, context, jobId, false, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Validarea unui rand, fara baza de date: valorile obligatorii, listele, datele si sumele.</summary>
internal static class OpeningRowParser
{
    public sealed record Draft(string Partner, PartnerRole Role, OpenItemKind Kind, string LineKey, string DocumentNo, DateOnly DocumentDate, DateOnly DueDate,
        string CurrencyCode, decimal ExchangeRate, decimal Open, decimal VatOpen, bool IsVatOnCollection)
    {
        /// <summary>Semnul pe rol: factura e debit la client si credit la furnizor; plata nealocata invers.</summary>
        public OpeningItemRow ToRow(int rowNumber, int partnerId)
        {
            var debit = (Role == PartnerRole.Customer) == (Kind == OpenItemKind.OpeningBalance);
            var sign = debit ? 1m : -1m;
            return new OpeningItemRow(rowNumber, partnerId, Role, Kind, LineKey, DocumentNo, DocumentDate, DueDate, CurrencyCode, ExchangeRate, sign * Open,
                sign * Math.Round(Open * ExchangeRate, 2, MidpointRounding.AwayFromZero), sign * VatOpen, IsVatOnCollection);
        }
    }

    public static (SpreadsheetRow Row, Draft? Draft, IReadOnlyList<OpeningIssueDto> Issues) Parse(int companyId, SpreadsheetRow row)
    {
        var issues = new List<OpeningIssueDto>();
        string? Value(string key) => row.Values[OpeningSheet.Index(key)];
        void Error(string key, string message) => issues.Add(new OpeningIssueDto(row.RowNumber, OpeningSheet.Header(key), Value(key), message));

        var partner = Value("Partner")?.Trim();
        if (string.IsNullOrEmpty(partner) || partner.Length > 50)
        {
            Error("Partner", "Partenerul (cod sau CUI) este obligatoriu.");
        }

        PartnerRole? role = OpeningSheet.Fold(Value("Role")) switch { "client" or "1" => PartnerRole.Customer, "furnizor" or "2" => PartnerRole.Supplier, _ => null };
        if (role is null)
        {
            Error("Role", "Rolul este Client sau Furnizor.");
        }

        OpenItemKind? kind = OpeningSheet.Fold(Value("Kind")) switch
        {
            "" or "factura" or "sold initial" => OpenItemKind.OpeningBalance,
            "plata nealocata" or "plata" or "avans" or "incasare nealocata" => OpenItemKind.Payment,
            _ => null,
        };
        if (kind is null)
        {
            Error("Kind", "Tipul este Factură sau Plată nealocată.");
        }

        var number = Value("Number")?.Trim();
        if (string.IsNullOrEmpty(number) || number.Length > 40)
        {
            Error("Number", "Numărul documentului este obligatoriu (cel mult 40 de caractere).");
        }

        var date = OpeningSheet.Date(Value("Date"));
        if (date is null)
        {
            Error("Date", "Data documentului lipsește sau nu e o dată (AAAA-LL-ZZ sau ZZ.LL.AAAA).");
        }

        var due = Value("DueDate") is null ? date : OpeningSheet.Date(Value("DueDate"));
        if (due is null && Value("DueDate") is not null)
        {
            Error("DueDate", "Scadența nu e o dată.");
        }
        else if (due is not null && date is not null && due < date)
        {
            Error("DueDate", "Scadența este înaintea datei documentului.");
        }

        var currency = (Value("Currency") ?? "RON").Trim().ToUpperInvariant();
        var rate = currency == "RON" ? 1m : OpeningSheet.Number(Value("Rate"));
        if (currency.Length != 3)
        {
            Error("Currency", "Moneda are 3 litere (RON, EUR...).");
        }
        else if (rate is null or <= 0 || decimal.Round(rate.Value, 6) != rate)
        {
            Error("Rate", "Cursul este obligatoriu la valută (pozitiv, cel mult 6 zecimale).");
        }

        var amount = OpeningSheet.Number(Value("Amount"));
        var open = OpeningSheet.Number(Value("Open"));
        if (amount is null or <= 0 || decimal.Round(amount.Value, 2) != amount)
        {
            Error("Amount", "Valoarea este o sumă pozitivă, cu cel mult 2 zecimale.");
        }

        if (open is null or <= 0 || decimal.Round(open.Value, 2) != open)
        {
            Error("Open", "Restul este o sumă pozitivă, cu cel mult 2 zecimale (rândurile achitate nu se importă).");
        }
        else if (amount is > 0 && open > amount)
        {
            Error("Open", "Restul nu poate depăși valoarea.");
        }

        bool? vatOnCollection = OpeningSheet.Fold(Value("VatOnCollection")) switch { "" or "nu" or "0" => false, "da" or "1" => true, _ => null };
        if (vatOnCollection is null)
        {
            Error("VatOnCollection", "TVA la încasare este Da sau Nu.");
        }

        var vatOpen = Value("VatOpen") is null ? 0m : OpeningSheet.Number(Value("VatOpen"));
        if (vatOpen is null or < 0 || decimal.Round(vatOpen.Value, 2) != vatOpen)
        {
            Error("VatOpen", "TVA-ul neexigibil este o sumă în lei, pozitivă.");
        }
        else if (vatOpen > 0 && (vatOnCollection != true || kind != OpenItemKind.OpeningBalance))
        {
            Error("VatOpen", "TVA neexigibil se completează doar la facturile cu TVA la încasare.");
        }
        else if (open is > 0 && rate is > 0 && vatOpen > Math.Round(open.Value * rate.Value, 2))
        {
            Error("VatOpen", "TVA-ul neexigibil nu poate depăși restul în lei.");
        }

        if (issues.Count > 0)
        {
            return (row, null, issues);
        }

        return (row, new Draft(partner!, role!.Value, kind!.Value, OpeningSheet.LineKey(companyId, kind.Value, number!), number!, date!.Value, due!.Value, currency,
            rate!.Value, open!.Value, vatOpen!.Value, vatOnCollection!.Value), issues);
    }
}

// ---------- Confirmare ----------

/// <summary>
/// Urmatorul lot de cel mult 500 de randuri (02.01, 4.8 pasul 4), in tranzactia lui: partidele de sold initial sau de plata nealocata, pe seturi;
/// cele importate deja se sar (F3-A13). Done = toate randurile au fost importate.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
[Audited("Fin.OpeningBalance.Import")]
public sealed record ImportOpeningOpenItems(int JobId) : ICommand<OpeningBatchResult>;

internal sealed class ImportOpeningOpenItemsHandler(IImportJobs jobs, IOpeningBalanceStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<ImportOpeningOpenItems, OpeningBatchResult>
{
    public async Task<OpeningBatchResult> Handle(ImportOpeningOpenItems request, CancellationToken cancellationToken)
    {
        var job = await OpeningJson.JobAsync(jobs, context, request.JobId, true, cancellationToken).ConfigureAwait(false);
        if (job.Status != ImportStatus.Validated)
        {
            throw ErpException.Conflict("CORE_IMPORT_NOT_PENDING", "Importul a fost deja confirmat, anulat sau încheiat.");
        }

        var rows = JsonSerializer.Deserialize<List<OpeningItemRow>>(job.PayloadJson ?? "[]", OpeningJson.Options) ?? [];
        var batch = rows.Skip(job.ImportedRows).Take(OpeningSheet.BatchSize).ToList();
        var created = await store.ImportAsync(context.CompanyId, batch, context.UserId, cancellationToken).ConfigureAwait(false);
        var imported = job.ImportedRows + batch.Count;
        var done = imported >= rows.Count;
        await jobs.UpdateAsync(job.Id, done ? ImportStatus.Imported : ImportStatus.Validated, imported, null, done, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ImportJob", job.Id, new { EntityType = OpeningSheet.EntityType, From = job.ImportedRows + 1, To = imported, Created = created });
        return new OpeningBatchResult(imported, done);
    }
}

/// <summary>Un lot a esuat: importul ramane partial, cu motivul; reluarea fisierului nu dubleaza partidele.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
[Audited("Fin.OpeningBalance.Fail")]
public sealed record FailOpeningBalanceImport(int JobId, string Message) : ICommand<OpeningImportDto>;

internal sealed class FailOpeningBalanceImportHandler(IImportJobs jobs, ICurrentContext context) : ICommandHandler<FailOpeningBalanceImport, OpeningImportDto>
{
    public async Task<OpeningImportDto> Handle(FailOpeningBalanceImport request, CancellationToken cancellationToken)
    {
        var job = await OpeningJson.JobAsync(jobs, context, request.JobId, true, cancellationToken).ConfigureAwait(false);
        var message = request.Message.Length > 500 ? request.Message[..500] : request.Message;
        await jobs.UpdateAsync(job.Id, ImportStatus.Partial, job.ImportedRows, message, false, cancellationToken).ConfigureAwait(false);
        return OpeningJson.Dto(await OpeningJson.JobAsync(jobs, context, request.JobId, false, cancellationToken).ConfigureAwait(false));
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
[Audited("Fin.OpeningBalance.Cancel")]
public sealed record CancelOpeningBalanceImport(int JobId) : ICommand<OpeningImportDto>;

internal sealed class CancelOpeningBalanceImportHandler(IImportJobs jobs, ICurrentContext context) : ICommandHandler<CancelOpeningBalanceImport, OpeningImportDto>
{
    public async Task<OpeningImportDto> Handle(CancelOpeningBalanceImport request, CancellationToken cancellationToken)
    {
        var job = await OpeningJson.JobAsync(jobs, context, request.JobId, true, cancellationToken).ConfigureAwait(false);
        if (job.Status != ImportStatus.Validated)
        {
            throw ErpException.Conflict("CORE_IMPORT_NOT_PENDING", "Importul a fost deja confirmat, anulat sau încheiat.");
        }

        await jobs.UpdateAsync(job.Id, ImportStatus.Cancelled, job.ImportedRows, null, true, cancellationToken).ConfigureAwait(false);
        return OpeningJson.Dto(await OpeningJson.JobAsync(jobs, context, request.JobId, false, cancellationToken).ConfigureAwait(false));
    }
}

[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
public sealed record GetOpeningBalanceImport(int JobId) : IQuery<OpeningImportDto>;

internal sealed class GetOpeningBalanceImportHandler(IImportJobs jobs, ICurrentContext context) : IQueryHandler<GetOpeningBalanceImport, OpeningImportDto>
{
    public async Task<OpeningImportDto> Handle(GetOpeningBalanceImport request, CancellationToken cancellationToken) =>
        OpeningJson.Dto(await OpeningJson.JobAsync(jobs, context, request.JobId, false, cancellationToken).ConfigureAwait(false));
}

/// <summary>Randurile cu erori, ca Excel cu coloana „Eroare”, de corectat si reincarcat (4.8 pasul 4).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.OpeningBalanceImport)]
public sealed record DownloadOpeningBalanceErrors(int JobId) : IQuery<DownloadedFile>;

internal sealed class DownloadOpeningBalanceErrorsHandler(IImportJobs jobs, ISpreadsheetFiles files, ICurrentContext context)
    : IQueryHandler<DownloadOpeningBalanceErrors, DownloadedFile>
{
    public async Task<DownloadedFile> Handle(DownloadOpeningBalanceErrors request, CancellationToken cancellationToken)
    {
        var job = await OpeningJson.JobAsync(jobs, context, request.JobId, false, cancellationToken).ConfigureAwait(false);
        var report = job.ReportJson is null ? null : JsonSerializer.Deserialize<OpeningReport>(job.ReportJson, OpeningJson.Options);
        var rows = report?.ErrorRows ?? [];
        return new DownloadedFile($"Erori sold initial {job.Id}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            files.Write(OpeningSheet.SheetName, OpeningSheet.Columns, [.. rows.Select(r => r.Values)], OpeningSheet.Lists, [.. rows.Select(r => r.Message)]));
    }
}
