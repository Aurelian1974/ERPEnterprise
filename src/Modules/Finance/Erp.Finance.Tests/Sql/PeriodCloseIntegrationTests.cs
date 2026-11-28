using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Periods;
using Erp.Finance.Contracts.Settlement;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Lista de verificari la inchiderea lunii (02.02, C3 3.3, 4.1): coada „De contat” si evenimentele in tranzit blocheaza fara trecere; o ciorna
/// de nota blocheaza, dar se poate trece peste ea cu motiv; avertismentele nu blocheaza.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PeriodCloseIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public PeriodCloseIntegrationTests(FinanceDatabase database) : base(database) =>
        Permissions.UnionWith([FinancePermissions.AccountView, FinancePermissions.JournalEntryView, FinancePermissions.ManualEntryCreate,
            FinancePermissions.ManualEntryValidate, FinancePermissions.PeriodView, FinancePermissions.PeriodSoftClose, FinancePermissions.PeriodClose,
            FinancePermissions.PeriodCloseOverride, FinancePermissions.FiscalYearClose, FinancePermissions.PostingIgnore]);

    [Fact]
    [Trait("Criteriu", "02.02-C3-A2")]
    [Trait("Criteriu", "02.02-C3-A3")]
    public async Task The_queue_and_events_in_transit_block_closing_without_override_and_a_draft_can_be_overridden()
    {
        await Send(new CreateFiscalYear(2026));
        var january = Period(await Send(new GetAccountingPeriods()), 1);
        var a = (await Send(new GetGlAccounts())).ToDictionary(x => x.Code, x => x.Id);
        var journal = (await Send(new GetJournals())).Single(j => j.Code == "DV").Id;

        // C3-A2: un document datat in ianuarie asteapta in coada; inchiderea e refuzata, iar peste pas nu se poate trece.
        var sale = new FacturaEmisa(950_001, 1, "FV 1", new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15), "RON", 1m, 100m, 100m, 0m, 0m, false, false,
            null, false, [new InvoiceDueDate(new DateOnly(2026, 2, 15), 100m)], null, null, EInvoiceStatus.Accepted, null) { CompanyId = Database.CompanyId };
        await Execute("DELETE FROM fin.GlAccountRoleAssignment WHERE CompanyId IS NULL AND Role = 'VatCollected'; SELECT 0;");
        try
        {
            var queued = await InTransaction(sp => sp.GetRequiredService<PostingService>().PostAsync(PostingAdapters.SalesInvoice(sale), Ct));
            Assert.False(queued.Posted);
        }
        finally
        {
            await Execute("""
                INSERT INTO fin.GlAccountRoleAssignment (CompanyId, Role, AccountId, ValidFrom, CreatedBy)
                SELECT NULL, 'VatCollected', a.Id, '20000101', 1 FROM fin.GlAccount AS a JOIN fin.ChartOfAccounts AS c ON c.Id = a.ChartId AND c.Kind = 1 WHERE a.Code = '4427';
                SELECT 0;
                """);
        }

        var run = await Send(new RunPeriodClose(january.Id));
        var queueStep = run.Steps.Single(s => s.StepCode == "POSTING_QUEUE");
        Assert.Equal((CloseCheckOutcome.Failed, false, false), (queueStep.Outcome, queueStep.AllowOverride, run.CanClose));
        var forbidden = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new OverrideCloseStep(run.Id, "POSTING_QUEUE", "Urgent")));
        Assert.Equal("FIN_PERIOD_OVERRIDE_FORBIDDEN", forbidden.Code);
        var blocked = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new ClosePeriod(january.Id, january.RowVersion)));
        Assert.Equal("FIN_PERIOD_CLOSE_BLOCKED", blocked.Code);
        var row = Assert.Single(await Send(new GetPostingQueue()));
        await Send(new IgnorePostingQueueItem(row.Id, "Factură de test", row.RowVersion));

        // C3-A3: un document validat chiar inainte de inchidere (evenimentul inca in outbox) blocheaza pana e tratat.
        var eventId = Guid.NewGuid();
        await Execute("""
            INSERT INTO core.Outbox (EventId, EventType, EventVersion, CompanyId, CorrelationId, Payload, OccurredAt)
            VALUES (@EventId, 'Erp.Finance.Contracts.Settlement.FacturaEmisa', 1, @Company, NEWID(), N'{}', SYSUTCDATETIME());
            SELECT 0;
            """, new { EventId = eventId, Company = Database.CompanyId });
        var transit = await Assert.ThrowsAnyAsync<ErpException>(() => Send(new ClosePeriod(january.Id, january.RowVersion)));
        Assert.Equal("FIN_PERIOD_EVENTS_IN_TRANSIT", transit.Code);
        await Execute("UPDATE core.Outbox SET ProcessedAt = SYSUTCDATETIME(), Status = 1 WHERE EventId = @EventId; SELECT 0;", new { EventId = eventId });

        // O ciorna de nota blocheaza; Contabilul sef trece peste ea cu motiv, iar trecerea se pastreaza la rularea inchiderii.
        await Send(new SaveManualEntry(null, journal, new DateOnly(2026, 1, 20), "Ciornă uitată", null, [new(a["5121"], 1, "10"), new(a["462"], 2, "10")]));
        run = await Send(new RunPeriodClose(january.Id));
        Assert.Equal(CloseCheckOutcome.Failed, run.Steps.Single(s => s.StepCode == "DRAFT_ENTRIES").Outcome);
        Assert.False(run.CanClose);
        await Send(new OverrideCloseStep(run.Id, "DRAFT_ENTRIES", "Ciorna se șterge în februarie"));
        var latest = await Send(new GetPeriodCloseRun(january.Id));
        Assert.True(latest!.CanClose);
        Assert.Equal("Ciorna se șterge în februarie", latest.Steps.Single(s => s.StepCode == "DRAFT_ENTRIES").OverrideReason);

        await Send(new ClosePeriod(january.Id, january.RowVersion));
        Assert.Equal(PeriodState.Closed, Period(await Send(new GetAccountingPeriods()), 1).Status);
        Assert.Contains((await Send(new GetPeriodCloseRun(january.Id)))!.Steps, s => s is { StepCode: "DRAFT_ENTRIES", OverrideReason: not null });

        // Februarie: luna anterioara e inchisa, lista trece fara probleme.
        var february = Period(await Send(new GetAccountingPeriods()), 2);
        Assert.Equal(CloseCheckOutcome.Ok, (await Send(new RunPeriodClose(february.Id))).Steps.Single(s => s.StepCode == "PREV_PERIOD").Outcome);
    }

    private static AccountingPeriodDto Period(IReadOnlyList<FiscalYearDto> years, int month) => years.Single(y => y.Year == 2026).Periods.Single(p => p.PeriodNo == month);

    private async Task Execute(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }
}
