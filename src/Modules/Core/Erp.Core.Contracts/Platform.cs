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

/// <summary>Un email de trimis din aplicatie (02.01, 4.7): destinatarul, subiectul, textul si atasamentele (core.Attachment ale firmei).</summary>
public sealed record EmailRequest(string To, string? Cc, string Subject, string Body, bool IsHtml, IReadOnlyList<int> AttachmentIds, string? EntityType = null,
    long? EntityId = null);

/// <summary>
/// Emailul pleaca din outbox (02.01, 4.7): mesajul se scrie in tranzactia operatiei, impreuna cu evenimentul EmailCerut; Worker-ul il trimite
/// prin serverul SMTP al instalarii, cu reincercarile outbox-ului.
/// </summary>
public interface IEmailOutbox
{
    Task<int> EnqueueAsync(EmailRequest email, CancellationToken ct);
}

/// <summary>Cererea de trimitere a unui email (02.01, 4.7), consumata de Worker.</summary>
public sealed record EmailCerut(int EmailMessageId) : IntegrationEvent;

/// <summary>O coloana a foii Excel de import (02.01, 4.8): cheia tehnica, antetul din fisier, daca e obligatorie si un exemplu pentru sablon.</summary>
public sealed record SpreadsheetColumn(string Key, string Header, bool Required, string Example);

/// <summary>O lista de valori permise, pusa in foaia „Liste” a sablonului si legata ca lista derulanta de coloanele date.</summary>
public sealed record SpreadsheetList(string Header, IReadOnlyList<string> Values, IReadOnlyList<string> ColumnKeys);

/// <summary>Un rand nevid citit din foaie: numarul lui in Excel si valorile, in ordinea coloanelor.</summary>
public sealed record SpreadsheetRow(int RowNumber, IReadOnlyList<string?> Values);

/// <summary>
/// Foile Excel ale importurilor (02.01, 4.8), pentru modulele care isi importa singure datele (soldurile initiale): sablonul cu antetul si
/// listele, fisierul cu randurile gresite (coloana „Eroare”) si citirea randurilor; un fisier care nu are antetul sablonului e refuzat cu
/// ErpValidationException pe „file”. Cel mult 50.000 de randuri.
/// </summary>
public interface ISpreadsheetFiles
{
    byte[] Write(string sheetName, IReadOnlyList<SpreadsheetColumn> columns, IReadOnlyList<IReadOnlyList<string?>> rows, IReadOnlyList<SpreadsheetList> lists,
        IReadOnlyList<string>? errors = null);

    IReadOnlyList<SpreadsheetRow> Read(string sheetName, IReadOnlyList<SpreadsheetColumn> columns, byte[] content);
}

/// <summary>Starea unui job de import (core.ImportJob).</summary>
public static class ImportStatus
{
    public const byte Validated = 1;
    public const byte Imported = 2;
    public const byte Cancelled = 3;
    public const byte Partial = 4;
}

/// <summary>Un import validat, de salvat ca job pana la confirmare: raportul si randurile valide, ca JSON in formatul modulului.</summary>
public sealed record ImportJobDraft(string EntityType, string FileName, int TotalRows, int ValidRows, int ErrorRows, int WarningRows, string ReportJson,
    string PayloadJson);

/// <summary>Un job de import citit din baza, cu raportul si randurile lui (PayloadJson lipseste dupa incheiere).</summary>
public sealed record ImportJobState(int Id, string EntityType, int? CompanyId, string FileName, byte Status, int TotalRows, int ValidRows, int ErrorRows,
    int ImportedRows, string? LastError, DateTime CreatedAt, DateTime? CompletedAt, string? ReportJson, string? PayloadJson);

/// <summary>
/// Joburile de import ale Nucleului (02.01, 4.8, core.ImportJob), pentru importurile altor module: validarea devine job, confirmarea il
/// importa in loturi, fiecare in tranzactia lui, iar starea si randurile importate raman pe job (reluare, istoric). Firma si utilizatorul
/// vin din contextul curent.
/// </summary>
public interface IImportJobs
{
    Task<int> CreateAsync(ImportJobDraft job, CancellationToken ct);

    Task<ImportJobState?> GetAsync(int id, bool forUpdate, CancellationToken ct);

    Task UpdateAsync(int id, byte status, int importedRows, string? lastError, bool clearPayload, CancellationToken ct);
}
