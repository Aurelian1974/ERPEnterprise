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
    /// <summary>Exista o licenta autentica in baza (valida sau expirata, 08, 4.1).</summary>
    bool HasLicense { get; }

    bool IsModuleActive(string moduleCode);

    /// <summary>Codurile modulelor active (meniul interfetei, UI-07); serverul ramane autoritatea prin IsModuleActive.</summary>
    IReadOnlyCollection<string> ActiveModules { get; }

    /// <summary>CUI-ul firmei e in licenta (08, 4.3); comparatie pe cifre, fara prefixul RO.</summary>
    bool IsFiscalCodeLicensed(string fiscalCode);

    /// <summary>Firma activa are CUI-ul in licenta (08, 4.3 pasul 7); altfel e doar pentru consultare.</summary>
    bool IsCompanyLicensed(int companyId);

    int? GetLimit(string limitCode);

    /// <summary>null = nelimitat sau fara licenta (vezi <see cref="HasLicense"/>).</summary>
    DateOnly? ValidUntil { get; }

    /// <summary>Dreptul la actualizari (08, 4.5); null = fara limita.</summary>
    DateOnly? UpdatesUntil { get; }

    /// <summary>Ceasul serverului e cu peste o zi in urma celei mai mari date vazute (08, 4.4): avertisment pentru administratori.</summary>
    bool ClockBehind { get; }

    /// <summary>Numele clientului din licenta (afisat pe paginile de autentificare); null fara licenta valida.</summary>
    string? CustomerName { get; }

    LicenseState State { get; }
}

/// <summary>Perioadele contabile inchise (P4). Implementarea vine din Finante; pana atunci nicio perioada nu e inchisa.</summary>
public interface IPeriodLock
{
    Task<bool> IsClosedAsync(int companyId, DateOnly date, CancellationToken ct);
}

/// <summary>
/// Consumatorul unui eveniment de integrare (02.01, 4.5): ruleaza in Worker, in tranzactia lui (IDbSession), impreuna cu nota din
/// core.InboxProcessed, deci o singura data pe eveniment. Contextul curent e al utilizatorului de sistem, in firma evenimentului.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent evt, CancellationToken ct);
}

/// <summary>
/// Contextul utilizatorului de sistem in procesele de fundal (Worker: consumatori de evenimente, joburi care trimit comenzi): firma in care
/// lucreaza scope-ul curent. Comenzile trimise apoi prin pipeline au contextul acestei firme.
/// </summary>
public interface ISystemContextScope
{
    void Enter(int companyId, Guid? correlationId = null);
}

/// <summary>Un atasament (02.01, 3 „Attachment”): fisierul legat de o entitate, cu hash-ul SHA-256 (hex).</summary>
public sealed record AttachmentInfo(int Id, string EntityType, long EntityId, string FileName, string ContentType, long Size, string Sha256, DateTime CreatedAt,
    string? CreatedByName);

/// <summary>
/// Atasamentele (02.01, 6), folosite de module in comenzile lor (care verifica drepturile pe documentul de care se leaga fisierul). Fisierele
/// au limita de dimensiune si lista de tipuri permise, verificate si dupa continut, si nu se executa niciodata (SEC-05); sunt ale firmei curente.
/// </summary>
public interface IAttachmentStore
{
    /// <summary>Salveaza fisierul (CORE_ATTACHMENT_INVALID: tip nepermis, gol, prea mare sau continut care nu corespunde tipului).</summary>
    Task<int> SaveAsync(string entityType, long entityId, string fileName, string contentType, Stream content, CancellationToken ct);

    /// <summary>Continutul unui atasament al firmei curente (CORE_ATTACHMENT_NOT_FOUND).</summary>
    Task<Stream> OpenAsync(int attachmentId, CancellationToken ct);

    Task<AttachmentInfo?> GetInfoAsync(int attachmentId, CancellationToken ct);

    Task<IReadOnlyList<AttachmentInfo>> ListAsync(string entityType, long entityId, CancellationToken ct);
}
