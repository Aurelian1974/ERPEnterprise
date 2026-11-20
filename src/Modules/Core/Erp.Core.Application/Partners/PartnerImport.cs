using System.Text.Json;
using Erp.Core.Application.Anaf;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using Erp.Core.Domain;
using FluentValidation;

namespace Erp.Core.Application.Partners;

/// <summary>O problema a unui rand importat: randul din foaie, coloana, valoarea si mesajul. Row 0 = problema a intregului fisier.</summary>
public sealed record ImportIssueDto(int Row, string Column, string? Value, string Message);

/// <summary>
/// Un import (02.01, 4.8; macheta 07g). Status: 1 validat (asteapta confirmarea), 2 importat, 3 anulat, 4 importat partial.
/// Errors si Warnings lipsesc din istoric (lista importurilor).
/// </summary>
public sealed record ImportJobDto(int Id, string EntityType, string FileName, byte Status, bool AnafCheck, int TotalRows, int ValidRows, int ErrorRows,
    int WarningRows, int ImportedRows, string? LastError, DateTime CreatedAt, string CreatedByName, DateTime? CompletedAt, byte[] RowVersion,
    IReadOnlyList<ImportIssueDto> Errors, IReadOnlyList<ImportIssueDto> Warnings);

/// <summary>Un rand valid, normalizat, gata de salvare (salvat in jobul de import pana la confirmare).</summary>
public sealed record PartnerImportRow(int RowNumber, string? Code, string Name, byte PartnerType, string? FiscalCode, bool IsCustomer, bool IsSupplier,
    int? PartnerGroupId, bool VatPayer, bool VatOnCollection, bool AnafInactive, DateTime? AnafCheckedAt, string? TradeRegisterNo, string Address,
    string City, string? CountyCode, string CountryCode, string? PostalCode, string? Phone, string? Email, bool IsActive);

/// <summary>Un rand cu erori, cu valorile din fisier, pentru descarcarea „randurilor cu erori” (4.8 pasul 4).</summary>
public sealed record ImportErrorRow(int RowNumber, IReadOnlyList<string?> Values, string Message);

/// <summary>Raportul salvat in job: erorile, avertizarile si randurile gresite.</summary>
public sealed record ImportReport(IReadOnlyList<ImportIssueDto> Errors, IReadOnlyList<ImportIssueDto> Warnings, IReadOnlyList<ImportErrorRow> ErrorRows);

/// <summary>Rezultatul validarii unui fisier, inainte sa devina job.</summary>
public sealed record PartnerImportAnalysis(string FileName, bool AnafCheck, int TotalRows, IReadOnlyList<PartnerImportRow> ValidRows, ImportReport Report)
{
    public int WarningRows => Report.Warnings.Where(w => w.Row > 0).Select(w => w.Row).Distinct().Count();
}

/// <summary>Jobul cu datele lui (raport si randuri valide), citit din baza.</summary>
public sealed record ImportJobRecord(ImportJobDto Job, int? CompanyId, string? ErrorReport, string? Payload);

/// <summary>Un partener existent gasit dupa cod sau CUI la import.</summary>
public sealed record ExistingPartner(int Id, string Code, string? FiscalCode, string Name, int? IntraGroupCompanyId, DateTime? AnafCheckedAt, byte[] RowVersion);

public interface IImportJobStore
{
    Task<SavedEntity> CreateAsync(string entityType, int companyId, PartnerImportAnalysis analysis, string report, string payload, int userId, CancellationToken ct);

    Task<ImportJobRecord?> GetAsync(int id, bool forUpdate, bool withData, CancellationToken ct);

    Task UpdateAsync(int id, byte status, int importedRows, string? lastError, bool clearPayload, int userId, CancellationToken ct);

    Task<IReadOnlyList<ImportJobDto>> ListAsync(string? entityType, CancellationToken ct);

    Task<IReadOnlyList<ExistingPartner>> FindPartnersAsync(IReadOnlyList<(int Row, string? Code, string? FiscalCode)> keys, CancellationToken ct);
}

public static class ImportStatus
{
    public const byte Validated = 1;
    public const byte Imported = 2;
    public const byte Cancelled = 3;
    public const byte Partial = 4;
}

/// <summary>
/// Pasul 2–3 al importului (4.8): citirea fisierului, validarea fiecarui rand cu acelasi validator ca fisa (SavePartnerValidator),
/// optional completarea dupa CUI de la ANAF, potrivirea cu partenerii existenti. Interogare: nu scrie nimic si ruleaza in afara
/// tranzactiei (apelurile ANAF nu au voie in tranzactie); jobul se creeaza apoi cu <see cref="CreatePartnerImportJob"/>.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
public sealed record AnalyzePartnerImport(string FileName, string FileBase64, bool AnafCheck) : IQuery<PartnerImportAnalysis>;

/// <summary>Salveaza analiza ca job de import, in asteptarea confirmarii. Nu e expusa direct; endpoint-ul o trimite dupa analiza.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
[Audited("Core.Import.Validate")]
public sealed record CreatePartnerImportJob(PartnerImportAnalysis Analysis) : ICommand<ImportJobDto>;

/// <summary>
/// Pasul 4: urmatorul lot de cel mult 500 de randuri, in tranzactia lui, cu audit pe fiecare partener si evenimentul PartenerModificat.
/// Un rand cu acelasi cod sau CUI actualizeaza partenerul existent (pasul 5). Done = toate randurile au fost importate.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
[Audited("Core.Partner.Import")]
public sealed record ImportPartnerBatch(int JobId) : ICommand<ImportBatchResult>;

public sealed record ImportBatchResult(int ImportedRows, bool Done);

/// <summary>Un lot a esuat: jobul ramane importat partial, cu motivul; reluarea fisierului nu dubleaza partenerii.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
[Audited("Core.Import.Fail")]
public sealed record FailImportJob(int JobId, string Message) : ICommand<ImportJobDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
[Audited("Core.Import.Cancel")]
public sealed record CancelImportJob(int JobId) : ICommand<ImportJobDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
public sealed record GetImportJob(int JobId) : IQuery<ImportJobDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
public sealed record ListImportJobs(string? EntityType = null) : IQuery<IReadOnlyList<ImportJobDto>>;

/// <summary>Randurile cu erori ale unui import, ca Excel cu coloana „Eroare”, de corectat si reincarcat.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
public sealed record DownloadImportErrors(int JobId) : IQuery<FileDto>;

internal static class ImportJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public const int BatchSize = 500;
    public const int MaxFileBytes = 10 * 1024 * 1024;

    public static ErpException NotFound() => ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Importul nu există.");

    public static ImportJobDto WithReport(ImportJobRecord record) =>
        record.ErrorReport is null
            ? record.Job
            : JsonSerializer.Deserialize<ImportReport>(record.ErrorReport, Options) is { } report
                ? record.Job with { Errors = report.Errors, Warnings = report.Warnings }
                : record.Job;
}

internal sealed class AnalyzePartnerImportHandler(IPartnerSpreadsheet spreadsheet, IPartnerStore partners, IOrganizationStore organization,
    IImportJobStore jobs, IAnafVatRegistry anaf, IValidator<SavePartner> validator, ICurrentContext context, TimeProvider clock)
    : IQueryHandler<AnalyzePartnerImport, PartnerImportAnalysis>
{
    // Campurile comenzii SavePartner si coloanele foii, ca erorile validatorului sa arate coloana din Excel.
    private static readonly Dictionary<string, string> FieldColumns = new(StringComparer.Ordinal)
    {
        ["Code"] = "Code", ["Name"] = "Name", ["PartnerType"] = "PartnerType", ["IsCustomer"] = "IsCustomer", ["IsSupplier"] = "IsSupplier",
        ["FiscalCode"] = "FiscalCode", ["TradeRegisterNo"] = "TradeRegisterNo", ["VatPayer"] = "VatPayer", ["VatOnCollection"] = "VatOnCollection",
        ["AnafInactive"] = "AnafInactive", ["PartnerGroupId"] = "PartnerGroup", ["Address"] = "Address", ["City"] = "City", ["CountyCode"] = "County",
        ["CountryCode"] = "Country", ["PostalCode"] = "PostalCode", ["Phone"] = "Phone", ["Email"] = "Email", ["IsActive"] = "IsActive",
    };

    public async Task<PartnerImportAnalysis> Handle(AnalyzePartnerImport request, CancellationToken cancellationToken)
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

        if (content.Length > ImportJson.MaxFileBytes)
        {
            throw File("Fișierul depășește 10 MB; împărțiți-l în mai multe importuri.");
        }

        var sheet = spreadsheet.Read(content);
        if (sheet.Count == 0)
        {
            throw File("Fișierul nu are rânduri de importat (sub antet).");
        }

        var partnerLookups = await partners.GetLookupsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var orgLookups = await organization.GetLookupsAsync(cancellationToken).ConfigureAwait(false);
        var errors = new List<ImportIssueDto>();
        var warnings = new List<ImportIssueDto>();
        var parsed = new List<(PartnerSheetRow Row, SavePartner Command)>();
        foreach (var row in sheet)
        {
            var issues = new List<ImportIssueDto>();
            var command = Parse(row, partnerLookups, orgLookups, issues);
            if (issues.Count == 0 && command is not null)
            {
                parsed.Add((row, command));
            }

            errors.AddRange(issues);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var checkedAt = new Dictionary<int, DateTime>();
        if (request.AnafCheck)
        {
            parsed = await EnrichFromAnafAsync(parsed, today, warnings, checkedAt, cancellationToken).ConfigureAwait(false);
        }

        var valid = new List<(PartnerSheetRow Row, SavePartner Command)>();
        foreach (var (row, command) in parsed)
        {
            var result = await validator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
            if (result.IsValid)
            {
                valid.Add((row, command));
                continue;
            }

            foreach (var failure in result.Errors)
            {
                var key = FieldColumns.GetValueOrDefault(failure.PropertyName, "Name");
                errors.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header(key), row[key], failure.ErrorMessage));
            }
        }

        valid = CheckDuplicatesInFile(valid, errors);
        valid = await MatchExistingAsync(valid, errors, warnings, cancellationToken).ConfigureAwait(false);

        var rows = valid.Select(v => ToRow(v.Row.RowNumber, v.Command, checkedAt.TryGetValue(v.Row.RowNumber, out var at) ? at : null)).ToList();
        var errorRows = errors.Where(e => e.Row > 0).GroupBy(e => e.Row).OrderBy(g => g.Key)
            .Select(g => new ImportErrorRow(g.Key, sheet.First(r => r.RowNumber == g.Key).Values, string.Join("; ", g.Select(e => $"{e.Column}: {e.Message}"))))
            .ToList();
        return new PartnerImportAnalysis(request.FileName, request.AnafCheck, sheet.Count, rows,
            new ImportReport([.. errors.OrderBy(e => e.Row)], [.. warnings.OrderBy(w => w.Row)], errorRows));
    }

    private SavePartner? Parse(PartnerSheetRow row, PartnerLookupsDto partnerLookups, OrgLookupsDto orgLookups, List<ImportIssueDto> issues)
    {
        void Issue(string key, string message) => issues.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header(key), row[key], message));

        bool YesNo(string key, bool fallback)
        {
            var value = PartnerSheet.YesNo(row[key], out var recognized);
            if (!recognized)
            {
                Issue(key, "Valoare nepermisă; scrieți Da sau Nu.");
            }

            return value ?? fallback;
        }

        var type = PartnerSheet.Type(row["PartnerType"]);
        if (type is null)
        {
            Issue("PartnerType", row["PartnerType"] is null ? "Câmp obligatoriu." : "Valoare nepermisă; alegeți din listă (" + string.Join(", ", PartnerSheet.TypeNames) + ").");
        }

        var customer = YesNo("IsCustomer", false);
        var supplier = YesNo("IsSupplier", false);
        var vatPayer = YesNo("VatPayer", false);
        var vatOnCollection = YesNo("VatOnCollection", false);
        var inactive = YesNo("AnafInactive", false);
        var active = YesNo("IsActive", true);

        int? groupId = null;
        if (row["PartnerGroup"] is { } groupCode)
        {
            var group = partnerLookups.Groups.FirstOrDefault(g => g.IsActive && string.Equals(g.Code, groupCode, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                Issue("PartnerGroup", "Grupă necunoscută sau inactivă; folosiți codul din foaia „Liste”.");
            }

            groupId = group?.Id;
        }

        var country = row["Country"] is { } countryText
            ? orgLookups.Countries.FirstOrDefault(c => string.Equals(c.Code, countryText, StringComparison.OrdinalIgnoreCase)
                || PartnerSheet.Fold(c.Name) == PartnerSheet.Fold(countryText))?.Code
            : type is null or PartnerType.LegalRomania or PartnerType.Individual ? "RO" : null;
        if (country is null)
        {
            Issue("Country", row["Country"] is null ? "Câmp obligatoriu pentru persoanele juridice străine." : "Țară necunoscută; folosiți codul ISO (de exemplu RO, DE).");
        }

        string? county = null;
        if (row["County"] is { } countyText)
        {
            // Codul (SV, B), codul ISO (RO-SV) sau denumirea (Suceava, Bucuresti), fara diacritice.
            var code = countyText.StartsWith("RO-", StringComparison.OrdinalIgnoreCase) ? countyText[3..] : countyText;
            county = orgLookups.Counties.FirstOrDefault(c => c.CountryCode == (country ?? "RO")
                && (string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase) || PartnerSheet.Fold(c.Name) == PartnerSheet.Fold(countyText)))?.Code;
            if (county is null)
            {
                Issue("County", "Județ necunoscut; folosiți codul din foaia „Liste” (de exemplu IF pentru Ilfov).");
            }
        }

        if (issues.Count > 0 || type is null || country is null)
        {
            return null;
        }

        return new SavePartner(null, row["Code"], row["Name"] ?? string.Empty, (byte)type.Value,
            type == PartnerType.Individual ? null : row["FiscalCode"], customer, supplier, groupId, vatPayer, vatOnCollection, inactive,
            row["TradeRegisterNo"], row["Address"] ?? string.Empty, row["City"] ?? string.Empty, county, country, PostalCode(row["PostalCode"]),
            row["Phone"], row["Email"], null, active, null);
    }

    // Excel transforma codul postal 010987 in numarul 10987.
    private static string? PostalCode(string? value) =>
        value is { Length: 5 } && value.All(char.IsAsciiDigit) ? "0" + value : value;

    private async Task<List<(PartnerSheetRow Row, SavePartner Command)>> EnrichFromAnafAsync(List<(PartnerSheetRow Row, SavePartner Command)> parsed,
        DateOnly today, List<ImportIssueDto> warnings, Dictionary<int, DateTime> checkedAt, CancellationToken ct)
    {
        var codes = parsed.Where(p => p.Command.PartnerType == (byte)PartnerType.LegalRomania && RomanianFiscalCode.IsValid(p.Command.FiscalCode))
            .Select(p => RomanianFiscalCode.Normalize(p.Command.FiscalCode)!).Distinct(StringComparer.Ordinal).ToList();
        IReadOnlyDictionary<string, AnafCompanyDto> found;
        try
        {
            found = await anaf.LookupManyAsync(codes, today, ct).ConfigureAwait(false);
        }
        catch (ErpException ex) when (ex.Kind == ErpErrorKind.Unavailable)
        {
            warnings.Add(new ImportIssueDto(0, PartnerSheet.Header("FiscalCode"), null, "Verificarea ANAF nu s-a putut face: " + ex.Message));
            return parsed;
        }

        var result = new List<(PartnerSheetRow Row, SavePartner Command)>(parsed.Count);
        foreach (var (row, command) in parsed)
        {
            var code = command.PartnerType == (byte)PartnerType.LegalRomania ? RomanianFiscalCode.Normalize(command.FiscalCode) : null;
            if (code is null || !codes.Contains(code))
            {
                result.Add((row, command));
                continue;
            }

            if (!found.TryGetValue(code, out var company))
            {
                warnings.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header("FiscalCode"), row["FiscalCode"], "CUI-ul nu figurează în registrele ANAF."));
                result.Add((row, command));
                continue;
            }

            // Golurile se completeaza de la ANAF; starea fiscala (TVA, TVA la incasare, inactiv) vine de la ANAF.
            var changed = command.VatPayer != company.VatPayer || command.VatOnCollection != (company.VatPayer && company.VatOnCollection)
                || command.AnafInactive != company.AnafInactive;
            var county = string.IsNullOrWhiteSpace(command.CountyCode) && command.CountryCode == "RO" ? company.CountyCode : command.CountyCode;
            result.Add((row, command with
            {
                Name = string.IsNullOrWhiteSpace(command.Name) ? company.Name : command.Name,
                TradeRegisterNo = string.IsNullOrWhiteSpace(command.TradeRegisterNo) ? company.TradeRegisterNo : command.TradeRegisterNo,
                Address = string.IsNullOrWhiteSpace(command.Address) ? company.Address ?? string.Empty : command.Address,
                City = string.IsNullOrWhiteSpace(command.City) ? company.City ?? string.Empty : command.City,
                CountyCode = county,
                PostalCode = string.IsNullOrWhiteSpace(command.PostalCode) ? company.PostalCode : command.PostalCode,
                Phone = string.IsNullOrWhiteSpace(command.Phone) ? company.Phone : command.Phone,
                VatPayer = company.VatPayer,
                VatOnCollection = company.VatPayer && company.VatOnCollection,
                AnafInactive = company.AnafInactive,
            }));
            checkedAt[row.RowNumber] = company.CheckedAt;
            if (changed)
            {
                warnings.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header("VatPayer"), row["VatPayer"],
                    "Starea TVA / TVA la încasare / inactiv a fost luată de la ANAF (diferă de fișier)."));
            }

            if (company.AnafInactive)
            {
                warnings.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header("AnafInactive"), row["AnafInactive"], "Contribuabil inactiv la ANAF."));
            }
        }

        return result;
    }

    // Acelasi cod sau acelasi CUI de doua ori in fisier: primul rand ramane, urmatoarele sunt erori.
    private static List<(PartnerSheetRow Row, SavePartner Command)> CheckDuplicatesInFile(List<(PartnerSheetRow Row, SavePartner Command)> rows,
        List<ImportIssueDto> errors)
    {
        var codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fiscal = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<(PartnerSheetRow Row, SavePartner Command)>();
        foreach (var (row, command) in rows)
        {
            var code = PartnerRules.Clean(command.Code);
            var cui = PartnerRules.NormalizeFiscalCode(command.PartnerType, command.FiscalCode);
            if (code is not null && codes.TryGetValue(code, out var first))
            {
                errors.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header("Code"), row["Code"], $"Codul apare și pe rândul {first}."));
                continue;
            }

            if (cui is not null && fiscal.TryGetValue(cui, out var firstCui))
            {
                errors.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header("FiscalCode"), row["FiscalCode"], $"CUI-ul apare și pe rândul {firstCui}."));
                continue;
            }

            if (code is not null)
            {
                codes[code] = row.RowNumber;
            }

            if (cui is not null)
            {
                fiscal[cui] = row.RowNumber;
            }

            result.Add((row, command));
        }

        return result;
    }

    // Reluarea importului actualizeaza (4.8 pasul 5): existentii sunt avertizari; codul si CUI-ul la parteneri diferiti sunt erori.
    private async Task<List<(PartnerSheetRow Row, SavePartner Command)>> MatchExistingAsync(List<(PartnerSheetRow Row, SavePartner Command)> rows,
        List<ImportIssueDto> errors, List<ImportIssueDto> warnings, CancellationToken ct)
    {
        var keys = rows.Select(r => (r.Row.RowNumber, PartnerRules.Clean(r.Command.Code), PartnerRules.NormalizeFiscalCode(r.Command.PartnerType, r.Command.FiscalCode)))
            .ToList();
        var existing = await jobs.FindPartnersAsync(keys, ct).ConfigureAwait(false);
        var result = new List<(PartnerSheetRow Row, SavePartner Command)>();
        foreach (var (row, command) in rows)
        {
            var match = ImportMatch.Find(existing, command);
            if (match.Conflict)
            {
                errors.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header("Code"), row["Code"],
                    "Codul și CUI-ul aparțin unor parteneri diferiți din nomenclator."));
                continue;
            }

            if (match.Partner is null && PartnerRules.Clean(command.Code) is null && context.CompanyId == 0)
            {
                errors.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header("Code"), null,
                    "Codul e obligatoriu la nivelul instalării; alegeți firma curentă ca să se aloce din serie."));
                continue;
            }

            if (match.Partner is { } partner)
            {
                warnings.Add(new ImportIssueDto(row.RowNumber, PartnerSheet.Header(match.ByCode ? "Code" : "FiscalCode"),
                    match.ByCode ? row["Code"] : row["FiscalCode"], $"Există deja partenerul „{partner.Name}” ({partner.Code}); se actualizează."));
            }

            result.Add((row, command));
        }

        return result;
    }

    private static PartnerImportRow ToRow(int rowNumber, SavePartner c, DateTime? anafCheckedAt) =>
        new(rowNumber, PartnerRules.Clean(c.Code), c.Name.Trim(), c.PartnerType, PartnerRules.NormalizeFiscalCode(c.PartnerType, c.FiscalCode),
            c.IsCustomer, c.IsSupplier, c.PartnerGroupId, c.VatPayer, c.VatOnCollection, c.AnafInactive, anafCheckedAt, PartnerRules.Clean(c.TradeRegisterNo),
            c.Address.Trim(), c.City.Trim(), PartnerRules.Clean(c.CountyCode), c.CountryCode, PartnerRules.Clean(c.PostalCode), PartnerRules.Clean(c.Phone),
            PartnerRules.Clean(c.Email), c.IsActive);

    private static ErpValidationException File(string message) => new(new Dictionary<string, string[]> { ["file"] = [message] });
}

/// <summary>Potrivirea unui rand cu partenerii existenti: dupa cod (fara majuscule) sau dupa CUI.</summary>
internal static class ImportMatch
{
    public static (ExistingPartner? Partner, bool ByCode, bool Conflict) Find(IReadOnlyList<ExistingPartner> existing, SavePartner command) =>
        Find(existing, PartnerRules.Clean(command.Code), PartnerRules.NormalizeFiscalCode(command.PartnerType, command.FiscalCode));

    public static (ExistingPartner? Partner, bool ByCode, bool Conflict) Find(IReadOnlyList<ExistingPartner> existing, string? code, string? fiscalCode)
    {
        var byCode = code is null ? null : existing.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        var byFiscal = fiscalCode is null ? null : existing.FirstOrDefault(p => p.FiscalCode == fiscalCode);
        return byCode is not null && byFiscal is not null && byCode.Id != byFiscal.Id
            ? (null, false, true)
            : (byCode ?? byFiscal, byCode is not null, false);
    }
}

internal sealed class CreatePartnerImportJobHandler(IImportJobStore jobs, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<CreatePartnerImportJob, ImportJobDto>
{
    public async Task<ImportJobDto> Handle(CreatePartnerImportJob request, CancellationToken cancellationToken)
    {
        var analysis = request.Analysis;
        var saved = await jobs.CreateAsync("Partner", context.CompanyId, analysis, JsonSerializer.Serialize(analysis.Report, ImportJson.Options),
            JsonSerializer.Serialize(analysis.ValidRows, ImportJson.Options), context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ImportJob", saved.Id, new
        {
            EntityType = "Partner", analysis.FileName, analysis.TotalRows, ValidRows = analysis.ValidRows.Count,
            ErrorRows = analysis.Report.ErrorRows.Count, analysis.AnafCheck,
        });
        var record = await jobs.GetAsync(saved.Id, forUpdate: false, withData: true, cancellationToken).ConfigureAwait(false) ?? throw ImportJson.NotFound();
        return ImportJson.WithReport(record);
    }
}

internal sealed class ImportPartnerBatchHandler(IImportJobStore jobs, IPartnerStore partners, INumberSeriesService series, ICurrentContext context,
    IAuditScope audit, IOutbox outbox, TimeProvider clock) : ICommandHandler<ImportPartnerBatch, ImportBatchResult>
{
    public async Task<ImportBatchResult> Handle(ImportPartnerBatch request, CancellationToken cancellationToken)
    {
        var record = await jobs.GetAsync(request.JobId, forUpdate: true, withData: true, cancellationToken).ConfigureAwait(false) ?? throw ImportJson.NotFound();
        var job = record.Job;
        if (job.EntityType != "Partner" || job.Status != ImportStatus.Validated)
        {
            throw ErpException.Conflict("CORE_IMPORT_NOT_PENDING", "Importul a fost deja confirmat, anulat sau încheiat.");
        }

        var rows = JsonSerializer.Deserialize<List<PartnerImportRow>>(record.Payload ?? "[]", ImportJson.Options) ?? [];
        var batch = rows.Skip(job.ImportedRows).Take(ImportJson.BatchSize).ToList();
        var existing = await jobs.FindPartnersAsync([.. batch.Select(r => (r.RowNumber, r.Code, r.FiscalCode))], cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        foreach (var row in batch)
        {
            var (match, _, conflict) = ImportMatch.Find(existing, row.Code, row.FiscalCode);
            if (conflict)
            {
                throw ErpException.Conflict("CORE_IMPORT_CONFLICT", $"Rândul {row.RowNumber}: codul și CUI-ul aparțin între timp unor parteneri diferiți.");
            }

            var code = row.Code ?? match?.Code;
            if (code is null)
            {
                code = (await PartnerCodes.NextAsync(partners, series, record.CompanyId ?? context.CompanyId, today, context.UserId, cancellationToken)
                    .ConfigureAwait(false)).Code;
            }

            var partner = new PartnerRecord(match?.Id, code, row.Name, row.PartnerType, row.FiscalCode, false, null, row.IsCustomer, row.IsSupplier,
                row.PartnerGroupId, row.VatPayer, row.VatOnCollection, row.AnafInactive, row.TradeRegisterNo, row.Address, row.City, row.CountyCode,
                row.CountryCode, row.PostalCode, row.Phone, row.Email, match?.IntraGroupCompanyId, row.IsActive, null, match?.RowVersion, row.AnafCheckedAt);
            var saved = await partners.SaveAsync(partner, context.UserId, cancellationToken).ConfigureAwait(false);
            var individual = row.PartnerType == (byte)PartnerType.Individual;
            audit.Describe("Core.Partner", saved.Id, new
            {
                ImportJobId = job.Id, row.RowNumber, Updated = match is not null, partner.Code, Name = individual ? null : partner.Name, partner.PartnerType,
                partner.FiscalCode, partner.IsCustomer, partner.IsSupplier, partner.VatPayer, partner.VatOnCollection, partner.AnafInactive, partner.IsActive,
            });
            await outbox.EnqueueAsync(new PartenerModificat(saved.Id, code, row.Name, row.FiscalCode, row.VatPayer, row.VatOnCollection, row.AnafInactive,
                row.IsActive) { CompanyId = context.CompanyId }, cancellationToken).ConfigureAwait(false);
        }

        var imported = job.ImportedRows + batch.Count;
        var done = imported >= rows.Count;
        await jobs.UpdateAsync(job.Id, done ? ImportStatus.Imported : ImportStatus.Validated, imported, null, done, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        return new ImportBatchResult(imported, done);
    }

    private async Task<string> NextCodeAsync(int companyId, DateOnly today, CancellationToken ct) =>
        (await series.NextAsync(companyId, PartnerRules.DocumentType, today, ct).ConfigureAwait(false)).Formatted;
}

internal sealed class FailImportJobHandler(IImportJobStore jobs, ICurrentContext context, IAuditScope audit) : ICommandHandler<FailImportJob, ImportJobDto>
{
    public async Task<ImportJobDto> Handle(FailImportJob request, CancellationToken cancellationToken)
    {
        var record = await jobs.GetAsync(request.JobId, forUpdate: true, withData: false, cancellationToken).ConfigureAwait(false) ?? throw ImportJson.NotFound();
        if (record.Job.Status == ImportStatus.Validated)
        {
            var message = request.Message.Length > 1000 ? request.Message[..1000] : request.Message;
            await jobs.UpdateAsync(record.Job.Id, ImportStatus.Partial, record.Job.ImportedRows, message, true, context.UserId, cancellationToken)
                .ConfigureAwait(false);
            audit.Describe("Core.ImportJob", record.Job.Id, new { record.Job.ImportedRows, Error = message });
        }

        var updated = await jobs.GetAsync(request.JobId, forUpdate: false, withData: true, cancellationToken).ConfigureAwait(false) ?? throw ImportJson.NotFound();
        return ImportJson.WithReport(updated);
    }
}

internal sealed class CancelImportJobHandler(IImportJobStore jobs, ICurrentContext context, IAuditScope audit) : ICommandHandler<CancelImportJob, ImportJobDto>
{
    public async Task<ImportJobDto> Handle(CancelImportJob request, CancellationToken cancellationToken)
    {
        var record = await jobs.GetAsync(request.JobId, forUpdate: true, withData: false, cancellationToken).ConfigureAwait(false) ?? throw ImportJson.NotFound();
        if (record.Job.Status != ImportStatus.Validated || record.Job.ImportedRows > 0)
        {
            throw ErpException.Conflict("CORE_IMPORT_NOT_PENDING", "Importul a fost deja confirmat, anulat sau încheiat.");
        }

        await jobs.UpdateAsync(record.Job.Id, ImportStatus.Cancelled, 0, null, true, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ImportJob", record.Job.Id);
        var updated = await jobs.GetAsync(request.JobId, forUpdate: false, withData: true, cancellationToken).ConfigureAwait(false) ?? throw ImportJson.NotFound();
        return ImportJson.WithReport(updated);
    }
}

internal sealed class GetImportJobHandler(IImportJobStore jobs) : IQueryHandler<GetImportJob, ImportJobDto>
{
    public async Task<ImportJobDto> Handle(GetImportJob request, CancellationToken cancellationToken) =>
        ImportJson.WithReport(await jobs.GetAsync(request.JobId, forUpdate: false, withData: true, cancellationToken).ConfigureAwait(false)
            ?? throw ImportJson.NotFound());
}

internal sealed class ListImportJobsHandler(IImportJobStore jobs) : IQueryHandler<ListImportJobs, IReadOnlyList<ImportJobDto>>
{
    public Task<IReadOnlyList<ImportJobDto>> Handle(ListImportJobs request, CancellationToken cancellationToken) =>
        jobs.ListAsync(request.EntityType, cancellationToken);
}

internal sealed class DownloadImportErrorsHandler(IImportJobStore jobs, IPartnerSpreadsheet spreadsheet, PartnerSheetListsProvider lists)
    : IQueryHandler<DownloadImportErrors, FileDto>
{
    public async Task<FileDto> Handle(DownloadImportErrors request, CancellationToken cancellationToken)
    {
        var record = await jobs.GetAsync(request.JobId, forUpdate: false, withData: true, cancellationToken).ConfigureAwait(false) ?? throw ImportJson.NotFound();
        var report = record.ErrorReport is null ? null : JsonSerializer.Deserialize<ImportReport>(record.ErrorReport, ImportJson.Options);
        var rows = report?.ErrorRows ?? [];
        var content = spreadsheet.Write([.. rows.Select(r => r.Values)], await lists.GetAsync(cancellationToken).ConfigureAwait(false),
            [.. rows.Select(r => $"Rândul {r.RowNumber}: {r.Message}")]);
        var name = Path.GetFileNameWithoutExtension(record.Job.FileName);
        return new FileDto($"{name}-erori.xlsx", FileDto.Xlsx, content);
    }
}
