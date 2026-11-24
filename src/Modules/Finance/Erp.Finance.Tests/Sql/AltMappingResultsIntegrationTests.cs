using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Periods;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Maparea notelor in planurile alternative (02.02, C1 3.6, 4.3, 4.4): la validare, cu tinta procentuala si randul nemapat; totalurile egale cu
/// planul romanesc; recalcularea care sare lunile inghetate; inchiderea lunii refuzata cand planul cere mapare completa.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AltMappingResultsIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public AltMappingResultsIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.ChartManage, FinancePermissions.MappingManage, FinancePermissions.MappingFreeze,
            FinancePermissions.MappingUnfreeze, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate, FinancePermissions.ManualEntryValidate,
            FinancePermissions.PeriodView, FinancePermissions.PeriodClose, FinancePermissions.FiscalYearClose]);

    [Fact]
    [Trait("Criteriu", "02.02-C1-A7")]
    [Trait("Criteriu", "02.02-C1-A8")]
    [Trait("Criteriu", "02.02-C1-A11")]
    public async Task Validated_entries_are_mapped_totals_match_frozen_months_are_skipped_and_incomplete_mapping_blocks_closing()
    {
        var chart = await Send(new SaveAltChart(null, Unique("GRP"), "Raportare grup", 1, "en", true, true, [Database.CompanyId], null));
        var services = (await Send(new SaveAltGlAccount(null, chart.Id, null, "6100", "Services"))).Id;
        var admin = (await Send(new SaveAltGlAccount(null, chart.Id, null, "6400", "Administrative"))).Id;
        var payables = (await Send(new SaveAltGlAccount(null, chart.Id, null, "2100", "Other payables"))).Id;
        var ro = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        await Send(new SaveMappingRule(null, chart.Id, 10, 1, ro["628"], null, null, null, null, 0, null, null, null, null, 0, null, null, null, 0,
            new DateOnly(2026, 1, 1), null, true, null, [new(services, "40"), new(admin, "60")], null));
        var center = await CostCenter();
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;

        // Nota din octombrie si cea din noiembrie: 628 se imparte pe doua tinte (ultima ia restul), 462 ramane nemapat.
        var october = await Entry(journal, new DateOnly(2026, 10, 15), ro, center, "1000.01");
        var november = await Entry(journal, new DateOnly(2026, 11, 13), ro, center, "250");
        var rows = await Mapped(october);
        Assert.Equal([("6100", 1, 400.00m), ("6400", 1, 600.01m), (null, 2, 1000.01m)], rows);

        // C1-A7: pe plan alternativ (cu „Nemapat”), totalul debit si credit = totalul planului romanesc, pe firma si luna.
        Assert.Equal(0, await Query<int>("""
            SELECT COUNT(*) FROM (
                SELECT l.PeriodYear, l.PeriodMonth, SUM(l.DebitRon) AS D, SUM(l.CreditRon) AS C FROM fin.JournalLine AS l JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
                WHERE l.CompanyId = @Company AND e.Status >= 2 GROUP BY l.PeriodYear, l.PeriodMonth) AS r
            FULL JOIN (
                SELECT PeriodYear, PeriodMonth, SUM(CASE WHEN Side = 1 THEN AmountRon ELSE 0 END) AS D, SUM(CASE WHEN Side = 2 THEN AmountRon ELSE 0 END) AS C
                FROM fin.JournalLineAltAccount WHERE ChartId = @Chart AND CompanyId = @Company GROUP BY PeriodYear, PeriodMonth) AS a
              ON a.PeriodYear = r.PeriodYear AND a.PeriodMonth = r.PeriodMonth
            WHERE ISNULL(a.D, -1) <> ISNULL(r.D, -2) OR ISNULL(a.C, -1) <> ISNULL(r.C, -2)
            """, new { Company = Database.CompanyId, Chart = chart.Id }));
        Assert.Contains(await Send(new GetUnmappedAccounts(chart.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30))), u => u.Code == "462" && u.LineCount == 2);

        // C1-A8: regula noua pentru 462; noiembrie e inghetat, deci recalcularea il sare si il listeaza, iar octombrie se mapeaza.
        await Send(new SaveMappingRule(null, chart.Id, 20, 1, ro["462"], null, null, null, null, 0, null, null, null, null, 0, null, null, null, 0,
            new DateOnly(2026, 1, 1), null, true, null, [new(payables, "100")], null));
        await Send(new SetAltPeriodFrozen(chart.Id, 2026, 11, true));
        var result = await Send(new RecalculateAltMapping(chart.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30)));
        Assert.Contains(result.Recalculated, m => m is { Year: 2026, Month: 10, UnmappedCount: 0 });
        Assert.Contains(result.SkippedFrozen, m => m is { Year: 2026, Month: 11 });
        Assert.Contains(("2100", 2, 1000.01m), await Mapped(october));
        Assert.Contains((null, 2, 250m), await Mapped(november));
        Assert.True(await Query<int>("SELECT COUNT(*) FROM fin.AltMappingRun WHERE ChartId = @Chart AND PeriodMonth = 10", new { Chart = chart.Id }) >= 1);

        // C1-A11: in ianuarie, o linie nemapata (4111 fara regula) nu blocheaza validarea, dar blocheaza inchiderea lunii (plan cu mapare completa).
        await Send(new SetAltPeriodFrozen(chart.Id, 2026, 11, false));
        await Send(new CreateFiscalYear(2026));
        var january = await EntryOn(journal, new DateOnly(2026, 1, 20), ro["7588"], ro["462"], center);
        Assert.Contains(await Mapped(january), r => r.Item1 is null);
        var period = (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Periods[0];
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new ClosePeriod(period.Id, period.RowVersion)));
        Assert.Equal("FIN_MAPPING_INCOMPLETE", refused.Code);
    }

    private async Task<long> Entry(int journal, DateOnly day, Dictionary<string, int> ro, int center, string amount) =>
        await Validate(new SaveManualEntry(null, journal, day, "Servicii", null, [new(ro["628"], 1, amount, CostCenterId: center), new(ro["462"], 2, amount)]));

    private async Task<long> EntryOn(int journal, DateOnly day, int credit, int debit, int center) =>
        await Validate(new SaveManualEntry(null, journal, day, "Rotunjire", null, [new(debit, 1, "5"), new(credit, 2, "5", CostCenterId: center)]));

    private async Task<long> Validate(SaveManualEntry entry)
    {
        var draft = await Send(entry);
        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id))).RowVersion));
        return draft.Id;
    }

    private async Task<List<(string?, int, decimal)>> Mapped(long entryId)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return (await connection.QueryAsync<(string?, int, decimal)>("""
            SELECT a.Code, m.Side, m.AmountRon
            FROM fin.JournalLineAltAccount AS m JOIN fin.JournalLine AS l ON l.Id = m.JournalLineId LEFT JOIN fin.GlAccount AS a ON a.Id = m.TargetAccountId
            WHERE l.EntryId = @Id ORDER BY l.LineNumber, a.Code
            """, new { Id = entryId })).ToList();
    }

    private async Task<int> CostCenter()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Sediu', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("SD") });
    }
}
