namespace Erp.Core.Application.Abstractions;

/// <summary>Tranzactia comenzii curente (4.1 pasul 6), implementata peste IDbSession in Infrastructure.</summary>
public interface IUnitOfWork
{
    Task BeginAsync(CancellationToken ct);

    Task CommitAsync(CancellationToken ct);

    Task RollbackAsync(CancellationToken ct);
}

/// <summary>Rezultatul unei cereri anterioare cu aceeasi cheie de idempotenta.</summary>
public sealed record IdempotentResult(string RequestType, string? ResultJson);

/// <summary>core.IdempotencyKey (4.1): rezervarea cheii si rezultatul, in tranzactia comenzii.</summary>
public interface IIdempotencyStore
{
    /// <summary>null = cheia era libera si a fost rezervata; altfel rezultatul cererii care a folosit-o.</summary>
    Task<IdempotentResult?> ClaimAsync(int userId, string key, string requestType, CancellationToken ct);

    Task CompleteAsync(int userId, string key, string resultJson, CancellationToken ct);
}

/// <summary>Erorile care se reincearca (SP-17): deadlock si expirarea blocarii.</summary>
public interface ITransientFailureDetector
{
    bool IsTransient(Exception exception);
}

/// <summary>Traducerea erorilor THROW 5xxxx din SQL in erori cu cod stabil (NET-09).</summary>
public interface ISqlErrorTranslator
{
    Contracts.ErpException? Translate(Exception exception);
}
