using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Erp.Core.Infrastructure.Messaging;

/// <summary>Un consumator inregistrat: tipul evenimentului (numele lui complet e EventType din outbox) si tipul handler-ului.</summary>
public sealed record IntegrationEventRegistration(Type EventType, Type HandlerType, Func<object, IntegrationEvent, CancellationToken, Task> Invoke)
{
    /// <summary>Numele consumatorului in core.InboxProcessed: handler-ul, stabil intre versiuni cat timp clasa nu se redenumeste.</summary>
    public string Consumer => HandlerType.FullName ?? HandlerType.Name;

    public static IntegrationEventRegistration For<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent> =>
        new(typeof(TEvent), typeof(THandler), (handler, evt, ct) => ((THandler)handler).HandleAsync((TEvent)evt, ct));
}

/// <summary>
/// Contextul consumatorilor (02.01, 4.5): utilizatorul de sistem (Id 1), in firma si cu corelarea evenimentului; toate permisiunile,
/// pentru ca evenimentul a fost deja autorizat la sursa.
/// </summary>
public sealed class SystemEventContext : ICurrentContext
{
    public const int SystemUserId = 1;

    public bool IsAuthenticated => true;

    public int UserId => SystemUserId;

    public int CompanyId { get; set; }

    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public string? IdempotencyKey => null;

    public string? IpAddress => null;

    public bool HasPermission(string permission, DimensionRef? scope = null) => true;
}

/// <summary>Sirul de conexiune al procesatorului (citirea si marcarea lotului, in afara tranzactiilor consumatorilor).</summary>
public sealed record OutboxConnection(string ConnectionString);

/// <summary>Rezultatul unei treceri a procesatorului.</summary>
public sealed record OutboxRunResult(int Fetched, int Delivered, int Retried, int Failed);

public static class OutboxRegistration
{
    /// <summary>
    /// Procesatorul outbox-ului (02.01, 4.5) pentru Worker: sesiunea de date, outbox-ul (consumatorii pot publica la randul lor), contextul
    /// de sistem si procesatorul. Infrastructura modulelor (store-urile folosite de consumatori) se inregistreaza separat.
    /// </summary>
    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped(_ => new DbSession(connectionString));
        services.TryAddScoped<IDbSession>(sp => sp.GetRequiredService<DbSession>());
        services.TryAddScoped<IOutbox, OutboxWriter>();
        services.TryAddScoped<SystemEventContext>();
        services.TryAddScoped<ICurrentContext>(sp => sp.GetRequiredService<SystemEventContext>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new OutboxConnection(connectionString));
        services.TryAddSingleton<OutboxDispatcher>();
        return services;
    }

    /// <summary>Inregistreaza un consumator de eveniment (02.01, 4.5), rezolvat in scope-ul fiecarei livrari.</summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(IntegrationEventRegistration.For<TEvent, THandler>());
        return services;
    }
}

/// <summary>
/// Procesatorul outbox-ului (02.01, 4.5): ia loturi de mesaje scadente (core.usp_Outbox_FetchBatch, READPAST, cu termen), si le livreaza
/// fiecarui consumator inregistrat, fiecare in scope-ul si tranzactia lui, impreuna cu nota din core.InboxProcessed (o singura data, si dupa
/// o oprire fortata intre livrare si marcarea mesajului). Mesajul fara consumatori se marcheaza procesat. La eroare: reincercare dupa 1 min,
/// 5 min, 30 min, 2 h, apoi starea Failed (monitorul de integrari; notificarea Core.OutboxFailed vine cu notificarile).
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, IEnumerable<IntegrationEventRegistration> registrations, OutboxConnection connection,
    TimeProvider clock, ILogger<OutboxDispatcher> log)
{
    public const int DefaultBatchSize = 100;

    /// <summary>Cat timp un mesaj luat nu e vazut de alt procesor (livrarea unui lot trebuie sa se incheie in acest timp).</summary>
    public const int LeaseSeconds = 300;

    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ILookup<string, IntegrationEventRegistration> consumers =
        registrations.ToLookup(r => r.EventType.FullName ?? r.EventType.Name, StringComparer.Ordinal);

    public async Task<OutboxRunResult> RunOnceAsync(CancellationToken ct, int batchSize = DefaultBatchSize)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        List<Message> batch;
        await using (var sql = new SqlConnection(connection.ConnectionString))
        {
            batch = (await sql.QueryAsync<Message>(new CommandDefinition("core.usp_Outbox_FetchBatch",
                new { BatchSize = batchSize, LeaseSeconds, Now = now }, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false))
                .ToList();
        }

        int delivered = 0, retried = 0, failed = 0;
        foreach (var message in batch)
        {
            var error = await DeliverAsync(message, ct).ConfigureAwait(false);
            await using var sql = new SqlConnection(connection.ConnectionString);
            if (error is null)
            {
                await sql.ExecuteAsync(new CommandDefinition("core.usp_Outbox_Complete", new { message.Id, Now = clock.GetUtcNow().UtcDateTime },
                    commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
                delivered++;
                continue;
            }

            var final = message.Attempts >= Backoff.Length;
            await sql.ExecuteAsync(new CommandDefinition("core.usp_Outbox_Fail", new
            {
                message.Id, Error = error.Length <= 2000 ? error : error[..2000],
                NextAttemptAt = final ? (DateTime?)null : clock.GetUtcNow().UtcDateTime + Backoff[message.Attempts], Failed = final,
            }, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
            if (final)
            {
                failed++;
                log.LogError("Mesajul outbox {Id} ({EventType}) a eșuat definitiv: {Error}", message.Id, message.EventType, error);
            }
            else
            {
                retried++;
                log.LogWarning("Mesajul outbox {Id} ({EventType}) se reia, încercarea {Attempt}: {Error}", message.Id, message.EventType, message.Attempts + 1, error);
            }
        }

        return new OutboxRunResult(batch.Count, delivered, retried, failed);
    }

    /// <summary>Livreaza mesajul tuturor consumatorilor; intoarce null la succes sau prima eroare (ceilalti consumatori raman notati).</summary>
    private async Task<string?> DeliverAsync(Message message, CancellationToken ct)
    {
        string? firstError = null;
        foreach (var registration in consumers[message.EventType])
        {
            try
            {
                await DeliverToAsync(message, registration, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                firstError ??= $"{registration.Consumer}: {ex.GetType().Name}: {ex.Message}";
            }
        }

        return firstError;
    }

    private async Task DeliverToAsync(Message message, IntegrationEventRegistration registration, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<SystemEventContext>();
        context.CompanyId = message.CompanyId ?? 0;
        context.CorrelationId = message.CorrelationId;
        var session = services.GetRequiredService<DbSession>();
        await session.BeginAsync(ct).ConfigureAwait(false);
        try
        {
            var claimed = await session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("core.usp_InboxProcessed_Claim",
                new { message.EventId, registration.Consumer }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct))
                .ConfigureAwait(false);
            if (claimed)
            {
                var evt = (IntegrationEvent)(JsonSerializer.Deserialize(message.Payload, registration.EventType, Json)
                    ?? throw new InvalidOperationException("Mesajul outbox nu are conținut."));
                await registration.Invoke(services.GetRequiredService(registration.HandlerType), evt, ct).ConfigureAwait(false);
            }

            await session.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await session.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private sealed record Message(long Id, Guid EventId, string EventType, int EventVersion, int? CompanyId, Guid CorrelationId, string Payload, int Attempts);
}
