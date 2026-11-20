using System.Diagnostics;
using ClosedXML.Excel;
using Dapper;
using Erp.Core.Application.Anaf;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Partners;
using Microsoft.Data.SqlClient;

namespace Erp.Core.Tests.Sql;

/// <summary>Exportul si importul din Excel (02.01, 4.8 si A18) pe SQL Server real.</summary>
public sealed partial class PartnerIntegrationTests
{
    private static readonly PartnerSpreadsheet Sheet = new();

    private static IReadOnlyList<string?> SheetRow(string? code, string name, string type, string? cui, string customer = "Da", string supplier = "Nu",
        string? county = "SV", string? country = null, string? group = null) =>
        [code, name, type, customer, supplier, cui, null, "Nu", "Nu", "Nu", group, "Str. Gării nr. 21", "Suceava", county, country, "720004", null, null, null];

    private static string File(IEnumerable<IReadOnlyList<string?>> rows) =>
        Convert.ToBase64String(Sheet.Write([.. rows], new PartnerSheetLists([], [], [])));

    private async Task<ImportJobDto> Upload(string fileBase64, bool anafCheck = false)
    {
        var analysis = await Send(new AnalyzePartnerImport("parteneri.xlsx", fileBase64, anafCheck));
        return await Send(new CreatePartnerImportJob(analysis));
    }

    // Ca endpoint-ul de confirmare: loturi pana la capat.
    private async Task<ImportJobDto> Confirm(int jobId)
    {
        while (!(await Send(new ImportPartnerBatch(jobId))).Done)
        {
        }

        return await Send(new GetImportJob(jobId));
    }

    [Fact]
    public async Task Template_and_export_use_the_import_columns_and_an_export_can_be_imported_back()
    {
        var template = await Send(new GetPartnerImportTemplate());
        var templateRows = Sheet.Read(template.Content);
        Assert.Equal("Agro Nord Distribuție SRL", Assert.Single(templateRows)["Name"]);

        var prefix = "X" + Guid.NewGuid().ToString("N")[..6];
        var cui = NewFiscalCode();
        await Send(Legal(Code(), prefix + " Export SRL", cui, customer: false, supplier: true));
        var export = await Send(new ExportPartners(prefix));
        Assert.Equal(FileDto.Xlsx, export.ContentType);
        var exported = Assert.Single(Sheet.Read(export.Content));
        Assert.Equal(("RO" + cui, "Juridică RO", "Nu", "Da"), (exported["FiscalCode"], exported["PartnerType"], exported["IsCustomer"], exported["IsSupplier"]));

        // Reimportul exportului actualizeaza acelasi partener (avertizare), nu creeaza altul.
        var job = await Upload(Convert.ToBase64String(export.Content));
        Assert.Equal((1, 0, 1), (job.ValidRows, job.ErrorRows, job.WarningRows));
        await Confirm(job.Id);
        Assert.Equal(1, (await Send(new SearchPartners(prefix))).Total);
    }

    [Fact]
    [Trait("Criteriu", "02.01-A18")]
    public async Task Ten_thousand_rows_with_five_percent_errors_import_only_valid_rows_and_reimport_creates_no_duplicates()
    {
        var prefix = "I" + Guid.NewGuid().ToString("N")[..6];
        var rows = new List<IReadOnlyList<string?>>();
        for (var i = 0; i < 10_000; i++)
        {
            var bad = i % 20 == 0; // 5%: CUI cu cifra de control gresita
            rows.Add(SheetRow($"{prefix}-{i:00000}", $"{prefix} Partener {i}", "Juridică RO", bad ? "RO12345679" : NewFiscalCode()));
        }

        var watch = Stopwatch.StartNew();
        var job = await Upload(File(rows));
        Assert.Equal((10_000, 9_500, 500), (job.TotalRows, job.ValidRows, job.ErrorRows));
        Assert.All(job.Errors, e => Assert.Equal("CUI", e.Column));
        Assert.Equal(1, job.Errors[0].Row - 1); // primul rand de date (randul 2 din foaie) are i = 0

        var done = await Confirm(job.Id);
        Assert.Equal((ImportStatus.Imported, 9_500), (done.Status, done.ImportedRows));
        Assert.Equal(9_500, (await Send(new SearchPartners(prefix, IsActive: null))).Total);

        var errors = Sheet.Read((await Send(new DownloadImportErrors(job.Id))).Content);
        Assert.Equal(500, errors.Count);

        // Reluarea aceluiasi fisier: toate randurile valide exista deja, se actualizeaza, nimic nu se dubleaza.
        var again = await Upload(File(rows));
        Assert.Equal((9_500, 9_500), (again.ValidRows, again.WarningRows));
        await Confirm(again.Id);
        Assert.Equal(9_500, (await Send(new SearchPartners(prefix, IsActive: null))).Total);
        TestContext.Current.SendDiagnosticMessage($"A18: 2 x 10.000 rânduri în {watch.Elapsed}.");
    }

    [Fact]
    public async Task Rows_are_checked_like_the_card_and_against_the_file_and_the_existing_partners()
    {
        var existing = await Send(Legal(Code(), "Existent SRL", NewFiscalCode(), customer: false, supplier: true));
        var detail = await Send(new GetPartner(existing.Id));
        var other = await Send(new GetPartner((await Send(Legal(Code(), "Alt existent SRL", NewFiscalCode(), customer: false, supplier: true))).Id));
        var cui = NewFiscalCode();
        IReadOnlyList<string?>[] rows =
        [
            SheetRow(null, "Nou fără cod", "juridica ro", cui, county: "RO-SV"),           // 2: valid, cod din serie, judet ISO
            SheetRow(Code(), "Dublură CUI", "Juridică RO", "RO" + cui),                       // 3: acelasi CUI ca randul 2
            SheetRow(Code(), "Tip greșit", "Juridic UE", NewFiscalCode()),                    // 4: tip nepermis
            SheetRow(Code(), "Județ greșit", "Juridică RO", NewFiscalCode(), county: "Ilfovv"), // 5: judet necunoscut
            SheetRow(Code(), "", "Juridică RO", NewFiscalCode()),                             // 6: denumire lipsa (validatorul fisei)
            SheetRow(detail.Code, "Existent SRL actualizat", "Juridică RO", detail.FiscalCode, customer: "Da", supplier: "Da"), // 7: actualizare
            SheetRow(detail.Code, "Conflict", "Juridică RO", other.FiscalCode),               // 8: cod si CUI la parteneri diferiti
            SheetRow(Code(), "Persoană", "Persoană fizică", null, county: "Suceava"),         // 9: persoana fizica, judet dupa nume
        ];
        var job = await Upload(File(rows));

        Assert.Equal([3, 4, 5, 6, 8], job.Errors.Select(e => e.Row).Distinct());
        Assert.Equal(("Tip", "Județ", "Denumire"), (job.Errors.Single(e => e.Row == 4).Column, job.Errors.Single(e => e.Row == 5).Column,
            job.Errors.Single(e => e.Row == 6).Column));
        Assert.Contains(job.Warnings, w => w.Row == 7);

        var done = await Confirm(job.Id);
        Assert.Equal(3, done.ImportedRows);
        var updated = await Send(new GetPartner(existing.Id));
        Assert.Equal(("Existent SRL actualizat", true, true), (updated.Name, updated.IsCustomer, updated.IsSupplier));
        var created = (await Send(new SearchPartners(cui))).Items.Single();
        Assert.StartsWith("P", created.Code, StringComparison.Ordinal);

        var cancelled = await Assert.ThrowsAsync<ErpException>(() => Send(new CancelImportJob(job.Id)));
        Assert.Equal("CORE_IMPORT_NOT_PENDING", cancelled.Code);
        var history = await Send(new ListImportJobs("Partner"));
        Assert.Contains(history, h => h.Id == job.Id && h.Status == ImportStatus.Imported);
    }

    [Fact]
    public async Task Anaf_check_fills_blanks_and_takes_the_fiscal_state_from_anaf()
    {
        var cui = NewFiscalCode();
        var unknown = NewFiscalCode();
        anaf.Companies[cui] = new AnafCompanyDto(cui, "DIN ANAF SRL", "J33/1/2020", true, true, true, false, true, null,
            "Str. ANAF nr. 1", "Mun. Suceava", "SV", "720001", null, new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc));
        IReadOnlyList<string?> blank = [Code(), "Nume din fișier", "Juridică RO", "Da", "Nu", cui, null, "Nu", "Nu", "Nu", null, null, null, null, null, null, null, null, null];
        var job = await Upload(File([blank, SheetRow(Code(), "Necunoscut ANAF", "Juridică RO", unknown)]), anafCheck: true);

        Assert.Equal((2, 0), (job.ValidRows, job.ErrorRows));
        Assert.Contains(job.Warnings, w => w.Row == 3 && w.Message.Contains("nu figurează", StringComparison.Ordinal));
        Assert.Contains(job.Warnings, w => w.Row == 2 && w.Message.Contains("inactiv", StringComparison.Ordinal));
        await Confirm(job.Id);

        var partner = await Send(new GetPartner((await Send(new SearchPartners(cui))).Items.Single().Id));
        Assert.Equal(("Nume din fișier", "Str. ANAF nr. 1", "SV", true, true, true), (partner.Name, partner.Address, partner.CountyCode, partner.VatPayer,
            partner.VatOnCollection, partner.AnafInactive));
        Assert.NotNull(partner.AnafCheckedAt);
    }

    [Fact]
    public async Task A_file_that_is_not_the_template_is_refused_and_a_cancelled_job_cannot_be_confirmed()
    {
        var notExcel = await Assert.ThrowsAsync<ErpValidationException>(() => Upload(Convert.ToBase64String("nu e excel"u8.ToArray())));
        Assert.Contains("file", notExcel.Errors.Keys);

        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Foaie").Cell(1, 1).Value = "Altceva";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var wrongColumns = await Assert.ThrowsAsync<ErpValidationException>(() => Upload(Convert.ToBase64String(stream.ToArray())));
        Assert.Contains("Denumire", wrongColumns.Errors["file"][0], StringComparison.Ordinal);

        var job = await Upload(File([SheetRow(Code(), "De anulat", "Juridică RO", NewFiscalCode())]));
        await Send(new CancelImportJob(job.Id));
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new ImportPartnerBatch(job.Id)));
        Assert.Equal("CORE_IMPORT_NOT_PENDING", refused.Code);
        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.Null(await connection.ExecuteScalarAsync<string>("SELECT Payload FROM core.ImportJob WHERE Id = @Id;", new { job.Id }));
    }
}
