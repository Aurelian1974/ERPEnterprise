using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Periods;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>Perioadele contabile (02.02, C3 3.1, 3.2, 4.1, 4.2): anul cu lunile lui, ordinea la inchidere si redeschidere, starea pentru module.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AccountingPeriodIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public AccountingPeriodIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.PeriodView, FinancePermissions.PeriodSoftClose, FinancePermissions.PeriodClose, FinancePermissions.PeriodReopen,
            FinancePermissions.FiscalYearClose]);

    [Fact]
    [Trait("Criteriu", "02.02-C3-A1")]
    [Trait("Criteriu", "02.02-C3-A4")]
    public async Task Months_close_in_order_reopen_from_the_last_and_a_soft_closed_month_refuses_documents_but_not_entries()
    {
        await Send(new CreateFiscalYear(2026));
        Assert.Equal(await Send(new CreateFiscalYear(2026)), await Send(new CreateFiscalYear(2026)));
        var year = (await Send(new GetAccountingPeriods())).Single(y => y.Year == 2026);
        Assert.Equal(12, year.Periods.Count);
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), (year.Periods[1].StartDate, year.Periods[1].EndDate));

        // A1: noiembrie nu se inchide cu octombrie deschisa.
        var early = await Assert.ThrowsAsync<ErpException>(() => Send(new ClosePeriod(Period(year, 11).Id, Period(year, 11).RowVersion)));
        Assert.Equal("FIN_PERIOD_PREVIOUS_OPEN", early.Code);
        for (var month = 1; month <= 11; month++)
        {
            var p = Period(await Year(), month);
            await Send(new ClosePeriod(p.Id, p.RowVersion));
        }

        var october = Period(await Year(), 10);
        var order = await Assert.ThrowsAsync<ErpException>(() => Send(new ReopenPeriod(october.Id, "Factură uitată", false, october.RowVersion)));
        Assert.Equal("FIN_PERIOD_REOPEN_ORDER", order.Code);
        await Assert.ThrowsAnyAsync<ErpException>(() => Send(new ReopenPeriod(october.Id, "", false, october.RowVersion)));

        var november = Period(await Year(), 11);
        await Send(new ReopenPeriod(november.Id, "Corecție", false, november.RowVersion));
        october = Period(await Year(), 10);
        await Send(new ReopenPeriod(october.Id, "Factură uitată", true, october.RowVersion));
        Assert.Equal((PeriodState.SoftClosed, PeriodState.Open), (Period(await Year(), 10).Status, Period(await Year(), 11).Status));
        Assert.Single(await Send(new GetPeriodHistory(october.Id)), h => h.ToStatus == PeriodState.SoftClosed && h.Reason == "Factură uitată");

        // A4: in luna inchisa operativ modulele nu mai valideaza documente, dar contabilitatea poate scrie note; in luna inchisa, nimic.
        var day = new DateOnly(2026, 10, 15);
        var (documents, entries, closedEntries, noYear) = await InTransaction(async sp =>
        {
            var guard = sp.GetRequiredService<IAccountingPeriodGuard>();
            return (await sp.GetRequiredService<AccountingPeriodGuard>().IsClosedAsync(Database.CompanyId, day, Ct),
                await guard.CanWriteAsync(Database.CompanyId, day, PeriodWriteKind.ManualEntry, Ct),
                await guard.CanWriteAsync(Database.CompanyId, new DateOnly(2026, 9, 1), PeriodWriteKind.ManualEntry, Ct),
                await guard.CanWriteAsync(Database.CompanyId, new DateOnly(2030, 1, 1), PeriodWriteKind.SourceDocument, Ct));
        });
        Assert.Equal((true, true, false, true), (documents, entries, closedEntries, noYear));
        Assert.True(await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE '%PerioadaContabilaRedeschisa'") >= 2);
    }

    private Task<FiscalYearDto> Year() => Send(new GetAccountingPeriods()).ContinueWith(t => t.Result.Single(y => y.Year == 2026), TaskScheduler.Default);

    private static AccountingPeriodDto Period(FiscalYearDto year, int month) => year.Periods.Single(p => p.PeriodNo == month);
}
