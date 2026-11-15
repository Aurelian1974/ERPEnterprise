namespace Erp.Core.Contracts;

/// <summary>Anvelopa evenimentelor de integrare (02.01, 5).</summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>Setat de IOutbox din TimeProvider (NET-15).</summary>
    public DateTime OccurredAtUtc { get; init; }

    public int CompanyId { get; init; }

    public Guid CorrelationId { get; init; }

    public int Version { get; init; } = 1;
}

/// <summary>Scrie evenimentul in core.Outbox, in tranzactia curenta (P8).</summary>
public interface IOutbox
{
    Task EnqueueAsync(IntegrationEvent evt, CancellationToken ct);
}

/// <summary>O intrare de audit (P5). Changes = obiect serializat ca JSON, fara date personale (NET-18).</summary>
public sealed record AuditRecord(string Action, string EntityType, long? EntityId, object? Changes);

public interface IAuditWriter
{
    Task WriteAsync(AuditRecord record, CancellationToken ct);
}

/// <summary>
/// Detaliile de audit ale comenzii curente: handler-ul descrie entitatea modificata, iar AuditBehavior scrie intrarea
/// la final, in aceeasi tranzactie. Fara descriere, intrarea are doar actiunea comenzii.
/// </summary>
public interface IAuditScope
{
    void Describe(string entityType, long? entityId, object? changes = null);
}

public enum LicenseState
{
    Valid,
    ExpiringSoon,
    Grace,
    ReadOnly,
}

/// <summary>Licenta instalarii (4.4).</summary>
public interface ILicenseInfo
{
    bool IsModuleActive(string moduleCode);

    int? GetLimit(string limitCode);

    DateOnly ValidUntil { get; }

    LicenseState State { get; }
}

/// <summary>Perioadele contabile inchise (P4). Implementarea vine din Finante; pana atunci nicio perioada nu e inchisa.</summary>
public interface IPeriodLock
{
    Task<bool> IsClosedAsync(int companyId, DateOnly date, CancellationToken ct);
}
