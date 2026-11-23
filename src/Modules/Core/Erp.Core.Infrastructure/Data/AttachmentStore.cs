using System.Data;
using System.Security.Cryptography;
using Dapper;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Data;

/// <summary>Tipurile permise si verificarile de continut ale atasamentelor (SEC-05).</summary>
public static class AttachmentRules
{
    /// <summary>Dimensiunea maxima a unui atasament: 10 MB.</summary>
    public const long MaxSize = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".xml"] = "application/xml",
    };

    /// <summary>
    /// Numele afisat (fara cale, fara caractere de control, cel mult 255) si tipul de continut stabilit dupa extensie, nu dupa ce trimite
    /// browserul; null daca extensia nu e permisa.
    /// </summary>
    public static (string FileName, string ContentType)? Normalize(string fileName)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0 || !Types.TryGetValue(Path.GetExtension(name), out var type))
        {
            return null;
        }

        return (name.Length <= 255 ? name : name[^255..], type);
    }

    /// <summary>Continutul corespunde tipului (semnatura de la inceputul fisierului; XML-ul e text care incepe cu „&lt;”).</summary>
    public static bool Matches(string contentType, ReadOnlySpan<byte> head) => contentType switch
    {
        "application/pdf" => head.StartsWith("%PDF-"u8),
        "image/png" => head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/jpeg" => head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        "image/tiff" => head.StartsWith("II*\0"u8) || head.StartsWith("MM\0*"u8),
        "application/xml" => TrimBom(head).TrimStart(" \t\r\n"u8).StartsWith("<"u8),
        _ => false,
    };

    private static ReadOnlySpan<byte> TrimBom(ReadOnlySpan<byte> head) => head.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? head[3..] : head;
}

/// <summary>Atasamentele in baza de date (core.usp_Attachment_*), pe firma curenta, in tranzactia apelantului (02.01, 6).</summary>
public sealed class AttachmentStore(IDbSession session, ICurrentContext context) : IAttachmentStore
{
    public async Task<int> SaveAsync(string entityType, long entityId, string fileName, string contentType, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var normalized = AttachmentRules.Normalize(fileName) ?? throw Invalid("Tipul fișierului nu este permis (PDF, PNG, JPG, TIFF sau XML).");
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(content, buffer, ct).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            throw Invalid("Fișierul este gol.");
        }

        if (!AttachmentRules.Matches(normalized.ContentType, bytes.AsSpan(0, Math.Min(bytes.Length, 16))))
        {
            throw Invalid("Conținutul fișierului nu corespunde tipului lui.");
        }

        return await session.Connection.QuerySingleAsync<int>(new CommandDefinition("core.usp_Attachment_Save", new
        {
            CompanyId = context.CompanyId == 0 ? (int?)null : context.CompanyId, EntityType = entityType, EntityId = entityId, normalized.FileName,
            normalized.ContentType, Size = (long)bytes.Length, Sha256 = SHA256.HashData(bytes), Content = bytes, context.UserId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<Stream> OpenAsync(int attachmentId, CancellationToken ct)
    {
        var row = await GetRowAsync(attachmentId, true, ct).ConfigureAwait(false) ?? throw NotFound();
        return new MemoryStream(row.Content ?? [], writable: false);
    }

    public async Task<AttachmentInfo?> GetInfoAsync(int attachmentId, CancellationToken ct) =>
        await GetRowAsync(attachmentId, false, ct).ConfigureAwait(false) is { } row ? Info(row) : null;

    public async Task<IReadOnlyList<AttachmentInfo>> ListAsync(string entityType, long entityId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<Row>(new CommandDefinition("core.usp_Attachment_List", new
        {
            CompanyId = context.CompanyId == 0 ? (int?)null : context.CompanyId, EntityType = entityType, EntityId = entityId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false)).Select(Info).ToList();

    private Task<Row?> GetRowAsync(int id, bool withContent, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<Row?>(new CommandDefinition("core.usp_Attachment_Get", new
        {
            Id = id, CompanyId = context.CompanyId == 0 ? (int?)null : context.CompanyId, WithContent = withContent,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    private static async Task CopyLimitedAsync(Stream source, Stream target, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > AttachmentRules.MaxSize)
            {
                throw Invalid("Fișierul depășește 10 MB.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    private static AttachmentInfo Info(Row r) =>
        new(r.Id, r.EntityType, r.EntityId, r.FileName, r.ContentType, r.Size, Convert.ToHexStringLower(r.Sha256), r.CreatedAt, r.CreatedByName);

    private static ErpException Invalid(string message) => new(ErpErrorKind.Validation, "CORE_ATTACHMENT_INVALID", message);

    private static ErpException NotFound() => ErpException.NotFound("CORE_ATTACHMENT_NOT_FOUND", "Atașamentul nu există.");

    private sealed record Row(int Id, string EntityType, long EntityId, string FileName, string ContentType, long Size, byte[] Sha256, DateTime CreatedAt,
        string? CreatedByName, byte[]? Content)
    {
        public Row(int id, string entityType, long entityId, string fileName, string contentType, long size, byte[] sha256, DateTime createdAt, string? createdByName)
            : this(id, entityType, entityId, fileName, contentType, size, sha256, createdAt, createdByName, null)
        {
        }
    }
}
