using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Messaging;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Treasury;
using Erp.Finance.Infrastructure.Events;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Consumatorii Finantelor livrati de procesatorul outbox-ului (02.01, 4.5; 02.02, F1 5.2): UtilizatorDezactivat inchide atribuirile la case
/// ale utilizatorului (azi inclusiv), sterge pe cele care nu incepusera, o singura data.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FinanceEventConsumerTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public FinanceEventConsumerTests(FinanceDatabase database) : base(database)
    {
    }

    private async Task<int> NewCash(DateOnly? validFrom) =>
        (await Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Cash, Unique("CASA-"), "Casa test", Database.HeadOfficeId, "RON", null, null,
            Database.CashierId, null, null, null, null, CashCloseMode.OnDemand, null, true,
            [new TreasuryCashierInput(Database.CashierId, true, validFrom, null)], null))).Id;

    [Fact]
    [Trait("Criteriu", "02.02-F1-5.2")]
    public async Task A_deactivated_user_loses_the_cash_desk_assignments_through_the_outbox()
    {
        var today = BusinessDay.Today(TimeProvider.System);
        var current = await NewCash(null);
        var future = await NewCash(today.AddDays(10));
        var evt = new UtilizatorDezactivat(Database.CashierId, Guid.NewGuid());
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            await connection.ExecuteAsync("""
                INSERT INTO core.Outbox (EventId, EventType, EventVersion, CorrelationId, Payload, OccurredAt)
                VALUES (@EventId, @EventType, 1, NEWID(), @Payload, SYSUTCDATETIME());
                """, new { evt.EventId, EventType = typeof(UtilizatorDezactivat).FullName,
                Payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        }

        var services = new ServiceCollection().AddLogging().AddOutboxDispatcher(Database.ConnectionString);
        services.AddIntegrationEventHandler<UtilizatorDezactivat, CloseCashierAssignments>();
        var dispatcher = services.BuildServiceProvider().GetRequiredService<OutboxDispatcher>();
        await dispatcher.RunOnceAsync(Ct);
        await dispatcher.RunOnceAsync(Ct);

        Assert.Equal(today, DateOnly.FromDateTime(await Query<DateTime>(
            "SELECT ValidTo FROM fin.TreasuryAccountUser WHERE TreasuryAccountId = @Account AND UserId = @User", new { Account = current, User = Database.CashierId })));
        Assert.Equal(0, await Query<int>("SELECT COUNT(*) FROM fin.TreasuryAccountUser WHERE TreasuryAccountId = @Account", new { Account = future }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.InboxProcessed WHERE EventId = @EventId", new { evt.EventId }));
        Assert.Equal((byte)1, await Query<byte>("SELECT Status FROM core.Outbox WHERE EventId = @EventId", new { evt.EventId }));
    }
}
