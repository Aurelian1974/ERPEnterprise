using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Reporting;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Impozitul pe profit (02.02, C4 3.6, 4.4): fisa cumulata din rulajele claselor 6 si 7 pe tratamentul fiscal, randurile manuale cu motiv,
/// nota 691 = 4411 dupa inchiderea operativa a ultimei luni, impozitul trimestrelor anterioare scazut si D100 din fisa.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ProfitTaxIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public ProfitTaxIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate, FinancePermissions.ManualEntryControlledAccounts, FinancePermissions.PeriodView, FinancePermissions.FiscalYearClose,
            FinancePermissions.ProfitTaxManage, FinancePermissions.TaxView, FinancePermissions.TaxPrepare, FinancePermissions.ReportTrialBalance]);

    [Fact]
    public async Task The_quarterly_sheet_adds_back_non_deductible_items_posts_the_tax_and_feeds_d100()
    {
        await Send(new CreateFiscalYear(2026));
        var a = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;
        var center = await CostCenter();
        var day = new DateOnly(2026, 2, 10);
        await Entry(journal, day, [new(a["5121"], 1, "100000"), new(a["707"], 2, "100000", CostCenterId: center)]);
        await Entry(journal, day, [new(a["628"], 1, "40000", CostCenterId: center), new(a["5121"], 2, "40000")]);
        await Entry(journal, day, [new(a["6581"], 1, "2000", CostCenterId: center), new(a["5121"], 2, "2000")]);
        await Entry(journal, day, [new(a["6231"], 1, "3000", CostCenterId: center), new(a["5121"], 2, "3000")]);
        await Entry(journal, day, [new(a["5121"], 1, "5000"), new(a["7611"], 2, "5000", CostCenterId: center)]);

        // Rezultat 60.000; + amenzi 2.000; + protocol peste 2% x 63.000 = 1.260, adica 1.740; − dividende 5.000 → 58.740, impozit 9.398.
        await Send(new CalculateProfitTax(2026, 1));
        var sheet = (await Send(new GetProfitTax(2026, 1)))!;
        var rows = sheet.Lines.ToDictionary(l => l.RowCode, l => l.Amount);
        Assert.Equal(("60000.00", "2000.00", "1740.00", "5000.00", "58740.00", "9398.00"),
            (rows["RESULT"], rows["NON_DEDUCTIBLE"], rows["PROTOCOL_OVER"], rows["NON_TAXABLE"], rows["TAXABLE"], sheet.TaxDue));

        // Rand manual cu motiv (decizia 6): deducerea rezervei legale.
        await Assert.ThrowsAnyAsync<Exception>(() => Send(new SetProfitTaxManualRow(2026, 1, "DEDUCTIONS", "740", "")));
        await Send(new SetProfitTaxManualRow(2026, 1, "DEDUCTIONS", "740", "Rezerva legală deductibilă"));
        Assert.Equal("9280.00", (await Send(new GetProfitTax(2026, 1)))!.TaxDue);

        // Contarea cere luna martie inchisa operativ (51954); apoi nota 691 = 4411 in 31 martie.
        var open = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new PostProfitTax(2026, 1)));
        Assert.Equal("FIN_PROFIT_TAX_PERIOD_NOT_SOFT_CLOSED", open.Code);
        await SetMonth(3, 2);
        Assert.NotNull(await Send(new PostProfitTax(2026, 1)));
        var march = (await Send(new RunTrialBalance(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)))).Rows.ToDictionary(r => r.AccountCode);
        Assert.Equal(("9280.00", "9280.00"), (march["691"].ClosingDebit, march["4411"].ClosingCredit));
        Assert.Equal("9280.00", (await Send(new GetProfitTax(2026, 1)))!.PostedAmount);

        // D100 pe trimestrul 1, din fisa.
        var d100 = await Send(new GetTaxReturn(await Send(new CalculateTaxReturn("D100", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)))));
        Assert.Equal("9280.00", d100.AmountDue);

        // Trimestrul 2, cumulat: venituri inca 10.000; limita de protocol creste la 2% x 73.000 = 1.460 → profit impozabil 67.800, impozit 10.848,
        // minus 9.280 declarat → 1.568.
        await Entry(journal, new DateOnly(2026, 5, 5), [new(a["5121"], 1, "10000"), new(a["707"], 2, "10000", CostCenterId: center)]);
        await Send(new SetProfitTaxManualRow(2026, 2, "DEDUCTIONS", "740", "Rezerva legală deductibilă"));
        Assert.Equal("1568.00", (await Send(new GetProfitTax(2026, 2)))!.TaxDue);
    }

    private async Task Entry(int journal, DateOnly day, JournalLineInput[] lines)
    {
        var draft = await Send(new SaveManualEntry(null, journal, day, "Test impozit", null, lines));
        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id))).RowVersion));
    }

    private async Task SetMonth(int month, byte status)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        await connection.ExecuteAsync("UPDATE fin.AccountingPeriod SET Status = @Status WHERE CompanyId = @Company AND StartDate = @Start",
            new { Company = Database.CompanyId, Status = status, Start = new DateTime(2026, month, 1) });
    }

    private async Task<int> CostCenter()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Sediu', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("PT") });
    }
}
