using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Reporting;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Balanta de grup (02.02, C5 3.7, 4.3; decizia 3): soldurile firmelor pe planul alternativ, eliminarea liniilor cu partener din grup pe conturile
/// marcate, diferentele intra-grup pe perechi (factura in tranzit) si rularea finala doar cu luna inchisa la toate firmele.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class GroupBalanceIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public GroupBalanceIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.ChartManage, FinancePermissions.MappingManage, FinancePermissions.JournalEntryView,
            FinancePermissions.ManualEntryCreate, FinancePermissions.ManualEntryValidate, FinancePermissions.ManualEntryControlledAccounts, FinancePermissions.ReportGroup]);

    [Fact]
    [Trait("Criteriu", "02.02-C5-A7")]
    public async Task Intercompany_balances_are_eliminated_and_the_invoice_in_transit_is_reported_as_a_difference()
    {
        var (seller, buyer) = (Database.CompanyId, Database.OtherCompanyId);
        var chart = await Send(new SaveAltChart(null, Unique("GR"), "Grup", 1, "en", false, true, [seller, buyer], null));
        var alt = new Dictionary<string, int>();
        foreach (var (code, name) in new[] { ("1100", "IC receivables"), ("2100", "IC payables"), ("7000", "Revenue"), ("6000", "Expenses"), ("5000", "Cash") })
        {
            alt[code] = (await Send(new SaveAltGlAccount(null, chart.Id, null, code, name))).Id;
        }

        var ro = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var priority = 10;
        foreach (var (source, target) in new[] { ("4111", "1100"), ("401", "2100"), ("707", "7000"), ("628", "6000"), ("5121", "5000") })
        {
            await Send(new SaveMappingRule(null, chart.Id, priority += 10, 1, ro[source], null, null, null, null, 0, null, null, null, null, 0, null, null, null, 0,
                new DateOnly(2026, 1, 1), null, true, null, [new(alt[target], "100")], null));
        }

        await Send(new SaveIntercompanyPair(chart.Id, "Creanțe ↔ datorii", alt["1100"], alt["2100"]));
        await Send(new SaveIntercompanyPair(chart.Id, "Cheltuieli ↔ venituri", alt["6000"], alt["7000"]));

        // Transportul facturat de firma vanzatoare (10.000) e inregistrat la cumparator doar 7.500 (o factura de 2.500 e in tranzit).
        var buyerPartner = await GroupPartner(buyer, "Exemplu Producție SRL");
        var sellerPartner = await GroupPartner(seller, "Exemplu Distribuție SRL");
        var day = new DateOnly(2026, 1, 20);
        await Entry(seller, day, [new(ro["4111"], 1, "10000", PartnerId: buyerPartner), new(ro["707"], 2, "10000", CostCenterId: await CostCenter(seller))]);
        await Entry(seller, day, [new(ro["5121"], 1, "3000"), new(ro["707"], 2, "3000", CostCenterId: await CostCenter(seller))]);
        await Entry(buyer, day, [new(ro["628"], 1, "7500", CostCenterId: await CostCenter(buyer)), new(ro["401"], 2, "7500", PartnerId: sellerPartner)]);

        var runId = await Send(new RunGroupBalance(chart.Id, 2026, 1, false));
        var run = await Send(new GetGroupBalance(runId));
        var rows = run.Rows.ToDictionary(r => r.AccountCode);
        Assert.Equal(("10000.00", "-10000.00", "0.00"), (rows["1100"].ByCompany[seller], rows["1100"].Eliminations, rows["1100"].Consolidated));
        Assert.Equal(("7500.00", "0.00"), (rows["2100"].Eliminations, rows["2100"].Consolidated));
        Assert.Equal(("-13000.00", "10000.00", "-3000.00"), (rows["7000"].ByCompany[seller], rows["7000"].Eliminations, rows["7000"].Consolidated));
        Assert.Equal("0.00", rows["6000"].Consolidated);

        // C5-A7: diferenta de 2.500 pe ambele perechi, raportata, nu ajustata.
        var differences = await Send(new GetIntercompanyDifferences(chart.Id, 2026, 1));
        var receivables = differences.Single(d => d.PairName == "Creanțe ↔ datorii" && d.FirstCompanyId == seller);
        Assert.Equal(("10000.00", "7500.00", "2500.00"), (receivables.FirstAmount, receivables.SecondAmount, receivables.Difference));
        Assert.Equal("-2500.00", differences.Single(d => d.PairName == "Cheltuieli ↔ venituri" && d.FirstCompanyId == buyer).Difference);

        // Rularea finala cere luna inchisa la toate firmele.
        var open = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new RunGroupBalance(chart.Id, 2026, 1, true)));
        Assert.Equal("FIN_GROUP_BALANCE_PERIOD_OPEN", open.Code);
    }

    private async Task Entry(int company, DateOnly day, JournalLineInput[] lines)
    {
        var journal = (await Send(new GetJournals(), companyId: company)).Single(j => j.Code == "DV").Id;
        var draft = await Send(new SaveManualEntry(null, journal, day, "Test grup", null, lines), companyId: company);
        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id), companyId: company)).RowVersion), companyId: company);
    }

    private async Task<int> GroupPartner(int company, string name)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, IntraGroupCompanyId, CreatedBy)
            VALUES (@Code, @Name, 1, @Fiscal, 1, 1, 1, N'Str. Grupului 1', N'Cluj-Napoca', N'CJ', 'RO', 1, @Company, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("GP-"), Name = name, Fiscal = Unique("RO5"), Company = company });
    }

    private async Task<int> CostCenter(int company)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Sediu', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = company, Code = Unique("GB") });
    }
}
