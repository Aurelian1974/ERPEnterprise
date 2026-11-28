using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Reporting;
using Erp.Finance.Contracts.Periods;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Notele de inchidere (02.02, C3 3.5, 4.1, 4.2): regularizarea TVA pe 4423, clasele 6 si 7 aduse la zero cu rezultatul in 121, iar la
/// reinchiderea dupa o redeschidere aceleasi note (acelasi numar) rescrise, cu versiunea veche in istoric.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClosingEntryIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 1, 31);

    public ClosingEntryIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate, FinancePermissions.ManualEntryControlledAccounts, FinancePermissions.PeriodView, FinancePermissions.PeriodClose,
            FinancePermissions.PeriodReopen, FinancePermissions.FiscalYearClose, FinancePermissions.ReportTrialBalance]);

    [Fact]
    [Trait("Criteriu", "02.02-C3-A5")]
    [Trait("Criteriu", "02.02-C3-A6")]
    [Trait("Criteriu", "02.02-C3-A7")]
    public async Task Closing_settles_vat_brings_classes_6_and_7_to_zero_and_a_reclose_rewrites_the_same_entries()
    {
        await Send(new CreateFiscalYear(2026));
        var a = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;
        var center = await CostCenter();
        await Entry(journal, new DateOnly(2026, 1, 10), [new(a["5121"], 1, "1190"), new(a["707"], 2, "1000", CostCenterId: center), new(a["4427"], 2, "190")]);
        await Entry(journal, new DateOnly(2026, 1, 15), [new(a["628"], 1, "500", CostCenterId: center), new(a["4426"], 1, "95"), new(a["5121"], 2, "595")]);

        // Simularea arata notele inainte de inchidere.
        var january = await Period(1);
        var preview = await Send(new PreviewClosingEntries(january.Id));
        Assert.Contains(preview.VatSettlement, l => l is { AccountCode: "4423", Side: 2, AmountRon: "95.00" });
        Assert.Contains(preview.ProfitAndLoss, l => l is { AccountCode: "121", Side: 2, AmountRon: "1000.00" });

        await Send(new ClosePeriod(january.Id, january.RowVersion));

        // C3-A5 si C3-A6: 628 si 707 la zero, 121 = rezultatul lunii (500 credit), 4427 si 4426 la zero, 4423 = 95.
        var balance = (await Send(new RunTrialBalance(Start, End))).Rows.ToDictionary(r => r.AccountCode);
        Assert.Equal(("0.00", "0.00"), (balance["628"].ClosingDebit, balance["707"].ClosingCredit));
        Assert.Equal(("0.00", "500.00"), (balance["121"].ClosingDebit, balance["121"].ClosingCredit));
        Assert.Equal(("0.00", "0.00", "95.00"), (balance["4427"].ClosingCredit, balance["4426"].ClosingDebit, balance["4423"].ClosingCredit));
        var numbers = await Numbers(january.Id);
        Assert.Equal(2, numbers.Count);

        // C3-A7: redeschiderea, o cheltuiala noua, reinchiderea: aceleasi note, aceleasi numere, versiunea veche in istoric, 121 = 400.
        january = await Period(1);
        await Send(new ReopenPeriod(january.Id, "Factură uitată", false, january.RowVersion));
        await Entry(journal, new DateOnly(2026, 1, 28), [new(a["628"], 1, "100", CostCenterId: center), new(a["5121"], 2, "100")]);
        january = await Period(1);
        await Send(new ClosePeriod(january.Id, january.RowVersion));
        Assert.Equal(numbers, await Numbers(january.Id));
        Assert.Equal("400.00", (await Send(new RunTrialBalance(Start, End))).Rows.Single(r => r.AccountCode == "121").ClosingCredit);
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM fin.JournalEntryHistory AS h JOIN fin.PeriodClosingEntry AS pc ON pc.EntryId = h.EntryId WHERE pc.PeriodId = @Id AND pc.Kind = 2
            """, new { january.Id }));
        Assert.Equal(0, await Query<int>("SELECT COUNT(*) FROM fin.JournalEntry WHERE SourceKind = 4 AND TotalDebit <> TotalCredit"));
    }

    private async Task<AccountingPeriodDto> Period(int month) =>
        (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Periods.Single(p => p.PeriodNo == month);

    private async Task<List<(byte, int)>> Numbers(int periodId)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return (await connection.QueryAsync<(byte, int)>("""
            SELECT pc.Kind, e.Number FROM fin.PeriodClosingEntry AS pc JOIN fin.JournalEntry AS e ON e.Id = pc.EntryId WHERE pc.PeriodId = @Id ORDER BY pc.Kind
            """, new { Id = periodId })).ToList();
    }

    private async Task Entry(int journal, DateOnly day, JournalLineInput[] lines)
    {
        var draft = await Send(new SaveManualEntry(null, journal, day, "Test închidere", null, lines));
        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id))).RowVersion));
    }

    private async Task<int> CostCenter()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Sediu', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("CL") });
    }
}
