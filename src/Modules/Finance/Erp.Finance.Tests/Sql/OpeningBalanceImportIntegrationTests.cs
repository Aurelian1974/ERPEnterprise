using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Reporting;
using Erp.Finance.Contracts.Periods;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Soldurile initiale la punerea in functiune (02.02, C3 3.7, decizia 7): balanta dezechilibrata refuzata, furnizorii diferiti de partide refuzati,
/// pornirea in aprilie cu soldurile initiale si rulajele cumulate in balanta, lunile de dinainte inchise ca importate, anularea.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OpeningBalanceImportIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly April = new(2026, 4, 1);

    public OpeningBalanceImportIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.OpeningBalanceImport, FinancePermissions.PeriodView, FinancePermissions.FiscalYearClose,
            FinancePermissions.ReportTrialBalance]);

    [Fact]
    [Trait("Criteriu", "02.02-C3-A11")]
    public async Task An_unbalanced_or_mismatching_import_is_refused_and_a_mid_year_start_carries_opening_balances_and_turnovers()
    {
        await Send(new CreateFiscalYear(2026));
        var supplier = await Scalar("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Furnizor Preluat SRL', 1, N'55667788', 0, 1, 1, N'Str. Mare 1', N'Cluj-Napoca', N'CJ', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("FP-") });

        // Dezechilibrata: 51914.
        var unbalanced = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new ImportOpeningBalances(April, "balanta.xlsx",
            [Row(1, "5121", od: "100", cd: "100"), Row(2, "1012", oc: "90", cc: "90")])));
        Assert.Equal("FIN_OPENING_BALANCE_UNBALANCED", unbalanced.Code);

        // 401 pe furnizor fara partida preluata in F3: validarea e refuzata (51915) si nimic nu ramane scris.
        var mismatch = await Send(new ImportOpeningBalances(April, "balanta.xlsx", [Row(1, "628", td: "595", cd: "595"), Row(2, "401", tc: "595", cc: "595", partner: supplier)]));
        var refused = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new ValidateOpeningBalances(mismatch)));
        Assert.Equal("FIN_OPENING_BALANCE_SUBLEDGER_MISMATCH", refused.Code);
        Assert.Equal(0, await Scalar("SELECT COUNT(*) FROM fin.JournalEntry WHERE SourceDocumentType = 'Finance.OpeningBalanceImport'", new { }));

        // Pornirea in aprilie: soldurile initiale ale anului si rulajele ianuarie–martie intra in balanta lui aprilie; lunile de dinainte sunt importate.
        var id = await Send(new ImportOpeningBalances(April, "balanta.xlsx",
        [
            Row(1, "5121", od: "10000", td: "5000", tc: "3000", cd: "12000"), Row(2, "1012", oc: "10000", cc: "10000"),
            Row(3, "628", td: "2000", cd: "2000"), Row(4, "462", tc: "4000", cc: "4000"),
        ]));
        await Send(new ValidateOpeningBalances(id));
        var bank = (await Send(new RunTrialBalance(April, new DateOnly(2026, 4, 30)))).Rows.Single(r => r.AccountCode == "5121");
        Assert.Equal(("10000.00", "5000.00", "3000.00", "12000.00"), (bank.OpeningDebit, bank.PriorDebit, bank.PriorCredit, bank.ClosingDebit));
        var year = (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026);
        Assert.All(year.Periods.Where(p => p.PeriodNo < 4), p => Assert.Equal((PeriodState.Closed, true), (p.Status, p.IsImported)));
        Assert.Equal(PeriodState.Open, year.Periods.Single(p => p.PeriodNo == 4).Status);
        Assert.Equal((byte)2, (await Send(new GetOpeningBalances()))!.Status);

        // Anularea, cat timp aprilie nu are note: notele dispar, lunile se redeschid.
        await Send(new CancelOpeningBalances(id));
        Assert.Empty((await Send(new RunTrialBalance(April, new DateOnly(2026, 4, 30)))).Rows);
        Assert.All((await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Periods, p => Assert.Equal(PeriodState.Open, p.Status));
    }

    private static OpeningBalanceRowInput Row(int n, string code, string od = "0", string oc = "0", string td = "0", string tc = "0", string cd = "0", string cc = "0",
        int? partner = null) => new(n, code, partner, null, null, od, oc, td, tc, cd, cc);

    private async Task<int> Scalar(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }
}
