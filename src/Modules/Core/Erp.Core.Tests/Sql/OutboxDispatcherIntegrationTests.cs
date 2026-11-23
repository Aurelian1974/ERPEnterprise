using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Messaging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Tests.Sql;

/// <summary>Evenimentul de test al procesatorului.</summary>
public sealed record EvenimentTest(string Text) : IntegrationEvent;

/// <summary>Livrarile primite de consumatorii de test, pe text; consumatorul care esueaza o face cat timp textul e in Failing.</summary>
public sealed class Deliveries
{
    public ConcurrentBag<(string Consumer, string Text, int CompanyId)> Calls { get; } = [];

    public ConcurrentDictionary<string, bool> Failing { get; } = new();

    public int Count(string consumer, string text) => Calls.Count(c => c.Consumer == consumer && c.Text == text);
}

public sealed class FirstConsumer(Deliveries deliveries, ICurrentContext context, IDbSession session) : IIntegrationEventHandler<EvenimentTest>
{
    public async Task HandleAsync(EvenimentTest evt, CancellationToken ct)
    {
        // Consumatorul lucreaza in tranzactia lui: o citire prin sesiune trebuie sa mearga.
        await session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", transaction: session.Transaction, cancellationToken: ct));
        deliveries.Calls.Add((nameof(FirstConsumer), evt.Text, context.CompanyId));
    }
}

public sealed class SecondConsumer(Deliveries deliveries) : IIntegrationEventHandler<EvenimentTest>
{
    public Task HandleAsync(EvenimentTest evt, CancellationToken ct)
    {
        if (deliveries.Failing.ContainsKey(evt.Text))
        {
            throw new InvalidOperationException("consumator indisponibil");
        }

        deliveries.Calls.Add((nameof(SecondConsumer), evt.Text, 0));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Procesatorul outbox-ului pe SQL Server real (02.01, 4.5; criteriul 02.01-A8): fiecare consumator primeste evenimentul o singura data,
/// si dupa o oprire intre livrare si marcarea mesajului; esecurile se reiau dupa 1 min, 5 min, 30 min, 2 h, apoi mesajul ramane Failed;
/// doua procesoare in paralel nu livreaza acelasi mesaj de doua ori.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxDispatcherIntegrationTests(CoreDatabase database) : IClassFixture<CoreDatabase>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Deliveries deliveries = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OutboxDispatcher Dispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(deliveries);
        services.AddOutboxDispatcher(database.ConnectionString);
        services.AddIntegrationEventHandler<EvenimentTest, FirstConsumer>();
        services.AddIntegrationEventHandler<EvenimentTest, SecondConsumer>();
        return services.BuildServiceProvider().GetRequiredService<OutboxDispatcher>();
    }

    private async Task<(long Id, Guid EventId)> Enqueue(string text, int? companyId = null)
    {
        var evt = new EvenimentTest(text) { CompanyId = companyId ?? 0 };
        await using var connection = new SqlConnection(database.ConnectionString);
        var id = await connection.ExecuteScalarAsync<long>("""
            INSERT INTO core.Outbox (EventId, EventType, EventVersion, CompanyId, CorrelationId, Payload, OccurredAt)
            VALUES (@EventId, @EventType, 1, @CompanyId, NEWID(), @Payload, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """, new { evt.EventId, EventType = typeof(EvenimentTest).FullName, CompanyId = companyId, Payload = JsonSerializer.Serialize(evt, Json) });
        return (id, evt.EventId);
    }

    private async Task<(byte Status, int Attempts, DateTime? NextAttemptAt, DateTime? ProcessedAt)> State(long id)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleAsync<(byte, int, DateTime?, DateTime?)>(
            "SELECT Status, Attempts, NextAttemptAt, ProcessedAt FROM core.Outbox WHERE Id = @Id", new { Id = id });
    }

    private async Task MakeDue(long id)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync("UPDATE core.Outbox SET NextAttemptAt = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE Id = @Id", new { Id = id });
    }

    [Fact]
    [Trait("Criteriu", "02.01-A8")]
    public async Task Each_consumer_receives_the_event_once_in_the_company_of_the_event()
    {
        var text = Guid.NewGuid().ToString("N");
        var (id, eventId) = await Enqueue(text, 42);
        var dispatcher = Dispatcher();

        await dispatcher.RunOnceAsync(Ct);
        await dispatcher.RunOnceAsync(Ct);

        Assert.Equal(1, deliveries.Count(nameof(FirstConsumer), text));
        Assert.Equal(1, deliveries.Count(nameof(SecondConsumer), text));
        Assert.Contains(deliveries.Calls, c => c.Text == text && c.CompanyId == 42);
        var state = await State(id);
        Assert.Equal((byte)1, state.Status);
        Assert.NotNull(state.ProcessedAt);
        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM core.InboxProcessed WHERE EventId = @EventId", new { EventId = eventId }));
    }

    [Fact]
    [Trait("Criteriu", "02.01-A8")]
    public async Task After_a_stop_between_delivery_and_marking_the_message_is_not_delivered_again()
    {
        var text = Guid.NewGuid().ToString("N");
        var (id, eventId) = await Enqueue(text);
        // Oprire simulata: primul consumator si-a facut treaba si nota (commit), dar mesajul n-a mai fost marcat procesat.
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync("INSERT INTO core.InboxProcessed (EventId, Consumer) VALUES (@EventId, @Consumer)",
                new { EventId = eventId, Consumer = typeof(FirstConsumer).FullName });
        }

        await Dispatcher().RunOnceAsync(Ct);

        Assert.Equal(0, deliveries.Count(nameof(FirstConsumer), text));
        Assert.Equal(1, deliveries.Count(nameof(SecondConsumer), text));
        Assert.Equal((byte)1, (await State(id)).Status);
    }

    [Fact]
    public async Task A_failing_consumer_is_retried_with_backoff_without_repeating_the_others_and_ends_as_failed()
    {
        var text = Guid.NewGuid().ToString("N");
        deliveries.Failing[text] = true;
        var (id, _) = await Enqueue(text);
        var dispatcher = Dispatcher();

        var before = DateTime.UtcNow;
        var first = await dispatcher.RunOnceAsync(Ct);
        var state = await State(id);
        Assert.True(first.Retried >= 1);
        Assert.Equal(((byte)0, 1), (state.Status, state.Attempts));
        Assert.InRange(state.NextAttemptAt!.Value, before.AddSeconds(50), before.AddSeconds(90));

        // Inca patru esecuri: dupa 5 min, 30 min, 2 h, apoi Failed. Primul consumator nu se repeta.
        for (var attempt = 2; attempt <= 5; attempt++)
        {
            await MakeDue(id);
            await dispatcher.RunOnceAsync(Ct);
        }

        state = await State(id);
        Assert.Equal(((byte)2, 5), (state.Status, state.Attempts));
        Assert.Null(state.NextAttemptAt);
        Assert.Equal(1, deliveries.Count(nameof(FirstConsumer), text));
        Assert.Equal(0, deliveries.Count(nameof(SecondConsumer), text));
        await using var connection = new SqlConnection(database.ConnectionString);
        Assert.Contains("consumator indisponibil", await connection.ExecuteScalarAsync<string>("SELECT LastError FROM core.Outbox WHERE Id = @Id", new { Id = id }),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_retried_message_succeeds_once_the_consumer_recovers()
    {
        var text = Guid.NewGuid().ToString("N");
        deliveries.Failing[text] = true;
        var (id, _) = await Enqueue(text);
        var dispatcher = Dispatcher();
        await dispatcher.RunOnceAsync(Ct);

        // Inainte de termen mesajul nu se reia.
        await dispatcher.RunOnceAsync(Ct);
        Assert.Equal(1, (await State(id)).Attempts);

        deliveries.Failing.TryRemove(text, out _);
        await MakeDue(id);
        await dispatcher.RunOnceAsync(Ct);

        Assert.Equal((byte)1, (await State(id)).Status);
        Assert.Equal(1, deliveries.Count(nameof(SecondConsumer), text));
        Assert.Equal(1, deliveries.Count(nameof(FirstConsumer), text));
    }

    [Fact]
    public async Task Two_processors_in_parallel_deliver_each_message_exactly_once()
    {
        var texts = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid().ToString("N")).ToList();
        foreach (var text in texts)
        {
            await Enqueue(text);
        }

        var first = Dispatcher();
        var second = Dispatcher();
        for (var round = 0; round < 3; round++)
        {
            await Task.WhenAll(first.RunOnceAsync(Ct, 25), second.RunOnceAsync(Ct, 25));
        }

        Assert.All(texts, t => Assert.Equal(1, deliveries.Count(nameof(FirstConsumer), t)));
        Assert.All(texts, t => Assert.Equal(1, deliveries.Count(nameof(SecondConsumer), t)));
    }

    [Fact]
    public async Task A_message_without_consumers_is_marked_processed()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var id = await connection.ExecuteScalarAsync<long>("""
            INSERT INTO core.Outbox (EventId, EventType, EventVersion, CorrelationId, Payload, OccurredAt)
            VALUES (NEWID(), N'Erp.Nimeni.EvenimentFaraConsumator', 1, NEWID(), N'{}', SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """);

        await new ServiceCollection().AddLogging().AddOutboxDispatcher(database.ConnectionString).BuildServiceProvider()
            .GetRequiredService<OutboxDispatcher>().RunOnceAsync(Ct);

        Assert.Equal((byte)1, (await State(id)).Status);
    }
}
