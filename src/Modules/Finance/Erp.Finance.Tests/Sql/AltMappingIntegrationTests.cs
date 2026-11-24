using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>Planurile alternative si regulile de mapare (02.02, C1 3.5, 4.2, 4.5): regulile din macheta c1-03, simulatorul si verificarea.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AltMappingIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Day = new(2026, 11, 13);

    public AltMappingIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.ChartManage, FinancePermissions.MappingManage]);

    [Fact]
    [Trait("Criteriu", "02.02-C1-A5")]
    [Trait("Criteriu", "02.02-C1-A9")]
    public async Task Rules_split_628_by_cost_center_and_612_by_percent_and_the_simulator_finds_them_in_priority_order()
    {
        var chart = await Send(new SaveAltChart(null, Unique("IFRS"), "IFRS grup", 1, "en", true, true, [Database.CompanyId], null));
        var a = new Dictionary<string, int>();
        foreach (var (code, name) in new[] { ("1200", "Trade receivables"), ("6110", "Logistics services"), ("6210", "Selling expenses"),
                     ("6400", "Administrative expenses"), ("6900", "Other external services"), ("6120", "Transport costs"), ("6950", "Other operating expenses") })
        {
            a[code] = (await Send(new SaveAltGlAccount(null, chart.Id, null, code, name))).Id;
        }

        var ro = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var (logistics, sales) = (await CostCenter("LOG"), await CostCenter("VNZ"));
        Task<FinanceSaved> Rule(int priority, byte kind, int? account, IReadOnlyList<int>? list, (string, string)? range, MappingTargetInput[] targets,
            byte condition = 0, int? value = null) =>
            Send(new SaveMappingRule(null, chart.Id, priority, kind, account, range?.Item1, range?.Item2, list, null, 0, null, null, null,
                condition == 0 ? null : "COST_CENTER", condition, value, null, null, 0, new DateOnly(2026, 1, 1), null, true, null, targets, null));

        await Rule(10, 3, null, [ro["4111"], ro["413"]], null, [new(a["1200"], "100")]);
        await Rule(60, 1, ro["628"], null, null, [new(a["6110"], "100")], 1, logistics);
        await Rule(61, 1, ro["628"], null, null, [new(a["6210"], "100")], 1, sales);
        await Rule(62, 1, ro["612"], null, null, [new(a["6110"], "60"), new(a["6400"], "40")]);
        await Rule(69, 3, null, [ro["622"], ro["624"], ro["628"]], null, [new(a["6900"], "100")]);
        await Rule(70, 2, null, null, ("6000", "6999"), [new(a["6950"], "100")], 2);
        await Rule(72, 1, ro["624"], null, null, [new(a["6120"], "100")]);

        Assert.Equal("6110", await Simulate(chart.Id, "628", logistics));
        Assert.Equal("6210", await Simulate(chart.Id, "628", sales));
        Assert.Equal("6900", await Simulate(chart.Id, "628", null));
        Assert.Equal("1200", await Simulate(chart.Id, "413", null));
        Assert.Equal("6950", await Simulate(chart.Id, "6458", sales));
        Assert.Null(await Simulate(chart.Id, "6458", null));
        var split = await Send(new SimulateMapping(chart.Id, "612", OnDate: Day));
        Assert.Equal([("6110", "60"), ("6400", "40")], split.Targets.Select(t => (t.Code, t.Percent)));

        // Regula 72 (624) e acoperita de 69 si nu se aplica niciodata; 6950 si 6120 au surse, deci doar contul fara reguli apare la „fara sursa”.
        var check = await Send(new CheckMapping(chart.Id));
        Assert.Contains(check.Overlaps, o => o is { WinnerPriority: 69, LoserPriority: 72, IsDead: true });
        Assert.Contains(check.Overlaps, o => o is { WinnerPriority: 60, LoserPriority: 69, IsDead: false });
        Assert.Empty(check.AccountsWithoutSource);

        var percent = await Assert.ThrowsAnyAsync<ErpException>(() => Rule(80, 1, ro["623"], null, null, [new(a["6900"], "50")]));
        Assert.Equal("FIN_MAPPING_PERCENT_INVALID", percent.Code);
        var priority = await Assert.ThrowsAnyAsync<ErpException>(() => Rule(60, 1, ro["623"], null, null, [new(a["6900"], "100")]));
        Assert.Equal("FIN_MAPPING_PRIORITY_DUPLICATE", priority.Code);
        var inUse = await Assert.ThrowsAnyAsync<ErpException>(async () => Send(new SaveAltGlAccount(a["6900"], chart.Id, null, "6900", "Other external services",
            false, await Version(a["6900"]))).GetAwaiter().GetResult());
        Assert.Equal("FIN_MAPPING_TARGET_IN_USE", inUse.Code);

        // Mutarea regulii 61 deasupra lui 60 nu schimba rezultatul: conditiile lor nu se suprapun.
        var rules = await Send(new GetMappingRules(chart.Id));
        await Send(new ReorderMappingRules(chart.Id, rules.Select(r => new RulePriorityInput(r.Id, r.Priority switch { 60 => 61, 61 => 60, var p => p })).ToList()));
        Assert.Equal("6210", await Simulate(chart.Id, "628", sales));
    }

    [Fact]
    public async Task Alternative_accounts_are_imported_with_parents_from_the_file()
    {
        var chart = await Send(new SaveAltChart(null, Unique("MGMT"), "Plan de gestiune", 4, "ro", false, true, [], null));
        string?[][] rows = [["G-410", "Servicii externe", "G-400"], ["G-400", "Cheltuieli", null], ["G-999", "Orfan", "G-X"]];
        var file = Convert.ToBase64String(await InTransaction(sp => Task.FromResult(sp.GetRequiredService<ISpreadsheetFiles>().Write(AltAccountSheet.SheetName,
            AltAccountSheet.Columns, rows, []))));
        var result = await Send(new ImportAltGlAccounts(chart.Id, "gestiune.xlsx", file, true));
        Assert.Equal((2, 1), (result.ImportedRows, result.ErrorRows));
        var accounts = await Send(new GetGlAccounts(chart.Id));
        Assert.Equal(accounts.Single(x => x.Code == "G-400").Id, accounts.Single(x => x.Code == "G-410").ParentId);
        Assert.False(accounts.Single(x => x.Code == "G-400").IsPostable);
        Assert.Contains(await Send(new GetCharts()), c => c.Id == chart.Id && c.AccountCount == 2 && c.Kind == 2);
    }

    private async Task<string?> Simulate(int chartId, string code, int? costCenter) =>
        (await Send(new SimulateMapping(chartId, code, CostCenterId: costCenter, OnDate: Day))).Targets.FirstOrDefault()?.Code;

    private async Task<byte[]> Version(int accountId) => (await Send(new GetGlAccount(accountId))).RowVersion;

    private async Task<int> CostCenter(string code)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, @Code, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique(code) });
    }
}
