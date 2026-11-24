using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Reporting;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>Rapoartele definite (02.02, C5 3.5, decizia 4): validarea formulelor la salvare si calculul peste balanta, cu filtru de dimensiune.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReportDefinitionIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public ReportDefinitionIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate, FinancePermissions.ManualEntryControlledAccounts, FinancePermissions.FiscalYearClose,
            FinancePermissions.ReportTrialBalance, FinancePermissions.ReportDefine]);

    [Fact]
    [Trait("Criteriu", "02.02-C5-A8")]
    public async Task A_client_report_is_validated_on_save_and_computed_from_the_balance()
    {
        await Send(new CreateFiscalYear(2026));
        var a = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;
        var code = Unique("LOG");
        var center = await CostCenter(code);
        var other = await CostCenter(Unique("ADM"));
        var day = new DateOnly(2026, 1, 15);
        await Entry(journal, day, [new(a["5121"], 1, "1000"), new(a["707"], 2, "1000", CostCenterId: center)]);
        await Entry(journal, day, [new(a["628"], 1, "300", CostCenterId: center), new(a["628"], 1, "100", CostCenterId: other), new(a["5121"], 2, "400")]);

        // C5-A8: referinta circulara si cont inexistent, refuzate la salvare.
        var circular = await Assert.ThrowsAnyAsync<ErpException>(() => Send(Report("CIRC", [Row("A", "[B]"), Row("B", "[A] + 1")])));
        Assert.Equal("FIN_REPORT_FORMULA_INVALID", circular.Code);
        var missing = await Assert.ThrowsAnyAsync<ErpException>(() => Send(Report("MISS", [Row("A", "RD(999)")])));
        Assert.Equal("FIN_REPORT_FORMULA_INVALID", missing.Code);

        var id = await Send(Report("MARJA", [Row("V", "RC(701..708)"), Row("C", "RD(6)"), Row("M", "[V] - [C]"), Row("L", $"RD(628 | CC={code})"),
            Row("B", "SD(5121)"), new ReportRowDto("T", "Titlu", null, false, 0)]));
        var result = await Send(new RunReport(id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        var values = result.Rows.ToDictionary(r => r.RowCode, r => r.Current);
        Assert.Equal(("1000.00", "400.00", "600.00", "300.00", "600.00", (string?)null), (values["V"], values["C"], values["M"], values["L"], values["B"], values["T"]));
        Assert.Contains(await Send(new GetReportDefinitions()), d => d.Id == id && d.Kind == 2);
    }

    private static ReportRowDto Row(string code, string formula) => new(code, $"Rândul {code}", formula, false, 0);

    private static SaveReportDefinition Report(string code, ReportRowDto[] rows) => new(null, null, code, $"Raport {code}", null, rows);

    private async Task Entry(int journal, DateOnly day, JournalLineInput[] lines)
    {
        var draft = await Send(new SaveManualEntry(null, journal, day, "Test raport", null, lines));
        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id))).RowVersion));
    }

    private async Task<int> CostCenter(string code)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, @Code, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = code });
    }
}
