using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Reporting;
using Erp.Finance.Contracts.Periods;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Inchiderea anului (02.02, C3 3.6, 4.3): rezerva legala, soldurile reportate egale cu soldurile finale ale lui decembrie pe cont si partener,
/// preluarea rezultatului in 117, recalcularea la reinchidere si blocarea / deblocarea anului.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FiscalYearCloseIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    private static readonly string[] ResultAccounts = ["121", "129", "1171"];

    public FiscalYearCloseIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate, FinancePermissions.ManualEntryControlledAccounts, FinancePermissions.PeriodView, FinancePermissions.PeriodClose,
            FinancePermissions.PeriodReopen, FinancePermissions.FiscalYearClose, FinancePermissions.FiscalYearLock, FinancePermissions.FiscalYearUnlock,
            FinancePermissions.ReportTrialBalance]);

    [Fact]
    [Trait("Criteriu", "02.02-C3-A9")]
    public async Task Carried_balances_equal_december_closing_balances_and_are_recalculated_after_a_reclose()
    {
        await Send(new CreateFiscalYear(2026));
        await Send(new CreateFiscalYear(2027));
        var a = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;
        var center = await Scalar("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@Company, 'COST_CENTER', @Code, N'Sediu', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Company = Database.CompanyId, Code = Unique("FY") });
        var partner = await Scalar("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Debitor SRL', 1, N'11223344', 1, 0, 1, N'Str. Mare 1', N'Cluj-Napoca', N'CJ', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("DB-") });

        await Entry(journal, new DateOnly(2026, 1, 1), [new(a["5121"], 1, "200000"), new(a["1012"], 2, "200000")], opening: true);
        await Entry(journal, new DateOnly(2026, 6, 10), [new(a["461"], 1, "1000", PartnerId: partner), new(a["707"], 2, "1000", CostCenterId: center)]);
        await Entry(journal, new DateOnly(2026, 12, 10), [new(a["5121"], 1, "100000"), new(a["707"], 2, "100000", CostCenterId: center)]);
        await Entry(journal, new DateOnly(2026, 12, 15), [new(a["628"], 1, "40000", CostCenterId: center), new(a["5121"], 2, "40000")]);
        for (var month = 1; month <= 11; month++)
        {
            await Close(month);
        }

        // Rezerva legala: 5% din profitul brut 61.000 = 3.050, sub plafonul de 20% din capitalul de 200.000.
        var state = await Send(new GetYearClose(2026));
        Assert.Equal(("61000.00", "200000.00", "3050.00"), (state.GrossProfit, state.ShareCapital, state.ProposedReserve));
        var tooMuch = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new PostLegalReserve(2026, "40000.01")));
        Assert.Equal("FIN_LEGAL_RESERVE_INVALID", tooMuch.Code);
        await Send(new PostLegalReserve(2026, state.ProposedReserve));
        await Close(12);
        await Send(new CloseFiscalYear(2026));

        // C3-A9: soldurile de deschidere ale lui 2027 = soldurile finale ale lui decembrie 2026, pe cont si partener (fara conturile rezultatului).
        await AssertCarried();
        var opening = await OpeningRows();
        Assert.Equal(("0.00", "57950.00"), (opening["1171"].OpeningDebit, opening["1171"].OpeningCredit));
        Assert.Equal(("0.00", "0.00"), (opening["121"].ClosingDebit, opening["121"].ClosingCredit));
        Assert.Equal(2, (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Status);

        // Redeschiderea lui decembrie redeschide anul; o incasare noua si reinchiderea recalculeaza soldurile reportate, in aceleasi note.
        var ids = await EntryIds();
        await Send(new ReopenPeriod((await Period(12)).Id, "Încasare uitată", false, (await Period(12)).RowVersion));
        Assert.Equal(1, (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Status);
        await Entry(journal, new DateOnly(2026, 12, 20), [new(a["5121"], 1, "500"), new(a["461"], 2, "500", PartnerId: partner)]);
        await Close(12);
        await Send(new CloseFiscalYear(2026));
        await AssertCarried();
        Assert.Equal("260500.00", (await OpeningRows())["5121"].OpeningDebit);
        Assert.Equal(ids, await EntryIds());

        // Blocarea anului blocheaza lunile; deblocarea cere motiv si le readuce in Inchisa.
        await Send(new LockFiscalYear(2026));
        Assert.Equal(PeriodState.Locked, (await Period(6)).Status);
        await Assert.ThrowsAnyAsync<Exception>(() => Send(new UnlockFiscalYear(2026, "")));
        await Send(new UnlockFiscalYear(2026, "Corecție după control"));
        Assert.Equal(PeriodState.Closed, (await Period(6)).Status);
    }

    private async Task AssertCarried()
    {
        var december = (await Send(new RunTrialBalance(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), ByPartner: true))).Rows
            .Where(r => "12345".Contains(r.AccountCode[0], StringComparison.Ordinal) && !ResultAccounts.Contains(r.AccountCode)
                && (r.ClosingDebit != "0.00" || r.ClosingCredit != "0.00"))
            .Select(r => (r.AccountCode, r.PartnerId, r.ClosingDebit, r.ClosingCredit)).OrderBy(x => x.AccountCode).ToList();
        var january = (await Send(new RunTrialBalance(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), ByPartner: true))).Rows
            .Where(r => !ResultAccounts.Contains(r.AccountCode) && (r.OpeningDebit != "0.00" || r.OpeningCredit != "0.00"))
            .Select(r => (r.AccountCode, r.PartnerId, r.OpeningDebit, r.OpeningCredit)).OrderBy(x => x.AccountCode).ToList();
        Assert.Equal(december, january);
    }

    private async Task<Dictionary<string, TrialBalanceRowDto>> OpeningRows() =>
        (await Send(new RunTrialBalance(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31)))).Rows.ToDictionary(r => r.AccountCode);

    private async Task Close(int month)
    {
        var p = await Period(month);
        await Send(new ClosePeriod(p.Id, p.RowVersion));
    }

    private async Task<AccountingPeriodDto> Period(int month) =>
        (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026).Periods.Single(p => p.PeriodNo == month);

    private async Task<List<long>> EntryIds()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return (await connection.QueryAsync<long>("SELECT EntryId FROM fin.FiscalYearCloseEntry ORDER BY Kind")).ToList();
    }

    private async Task Entry(int journal, DateOnly day, JournalLineInput[] lines, bool opening = false)
    {
        var draft = await Send(new SaveManualEntry(null, journal, day, "Test an", null, lines));
        if (opening)
        {
            await Scalar("UPDATE fin.JournalEntry SET SourceKind = 5 WHERE Id = @Id; SELECT 0;", new { draft.Id });
        }

        await Send(new ValidateManualEntry(draft.Id, (await Send(new GetJournalEntry(draft.Id))).RowVersion));
    }

    private async Task<int> Scalar(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }
}
