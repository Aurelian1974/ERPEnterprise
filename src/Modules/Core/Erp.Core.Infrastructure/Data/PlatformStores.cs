using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Application.Abstractions;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Data;

/// <summary>core.usp_IdempotencyKey_* in tranzactia comenzii (4.1).</summary>
public sealed class IdempotencyStore(IDbSession session) : IIdempotencyStore
{
    public Task<IdempotentResult?> ClaimAsync(int userId, string key, string requestType, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<IdempotentResult>(new CommandDefinition("core.usp_IdempotencyKey_Claim",
            new { UserId = userId, Key = new DbString { Value = key, Length = 100 }, RequestType = new DbString { Value = requestType, Length = 300 } },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    public Task CompleteAsync(int userId, string key, string resultJson, CancellationToken ct) =>
        session.Connection.ExecuteAsync(new CommandDefinition("core.usp_IdempotencyKey_Complete",
            new { UserId = userId, Key = new DbString { Value = key, Length = 100 }, ResultJson = resultJson },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
}

/// <summary>IOutbox (P8): evenimentul, cu anvelopa completata din context si TimeProvider, in tranzactia curenta.</summary>
public sealed class OutboxWriter(IDbSession session, ICurrentContext context, TimeProvider time) : IOutbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task EnqueueAsync(IntegrationEvent evt, CancellationToken ct)
    {
        if (session.Transaction is null)
        {
            throw new InvalidOperationException("Evenimentele se scriu în outbox doar în tranzacția unei comenzi (P8).");
        }

        var envelope = evt with
        {
            OccurredAtUtc = time.GetUtcNow().UtcDateTime,
            CorrelationId = evt.CorrelationId == Guid.Empty ? context.CorrelationId : evt.CorrelationId,
            CompanyId = evt.CompanyId == 0 ? context.CompanyId : evt.CompanyId,
        };
        return session.Connection.ExecuteAsync(new CommandDefinition("core.usp_Outbox_Enqueue", new
        {
            envelope.EventId,
            EventType = new DbString { Value = evt.GetType().FullName ?? evt.GetType().Name, Length = 200 },
            EventVersion = envelope.Version,
            CompanyId = envelope.CompanyId == 0 ? (int?)null : envelope.CompanyId,
            envelope.CorrelationId,
            Payload = JsonSerializer.Serialize(envelope, envelope.GetType(), Json),
            OccurredAt = envelope.OccurredAtUtc,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
}

/// <summary>IAuditWriter (P5): intrarea de audit in tranzactia curenta, cu utilizatorul, firma, IP-ul si corelarea cererii.</summary>
public sealed class AuditWriter(IDbSession session, ICurrentContext context, TimeProvider time) : IAuditWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task WriteAsync(AuditRecord record, CancellationToken ct) =>
        session.Connection.ExecuteAsync(new CommandDefinition("core.usp_AuditEntry_Insert", new
        {
            OccurredAt = time.GetUtcNow().UtcDateTime,
            context.UserId,
            CompanyId = context.CompanyId == 0 ? (int?)null : context.CompanyId,
            Action = new DbString { Value = record.Action, Length = 100 },
            EntityType = new DbString { Value = record.EntityType, Length = 100 },
            record.EntityId,
            Changes = record.Changes is null ? null : JsonSerializer.Serialize(record.Changes, Json),
            IpAddress = new DbString { Value = context.IpAddress, Length = 45, IsAnsi = true },
            context.CorrelationId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
}

/// <summary>Pana la modulul Finante nu exista perioade inchise.</summary>
public sealed class NoClosedPeriods : IPeriodLock
{
    public Task<bool> IsClosedAsync(int companyId, DateOnly date, CancellationToken ct) => Task.FromResult(false);
}
