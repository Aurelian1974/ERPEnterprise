using System.Text.Json;
using Erp.Core.Application.Abstractions;
using Erp.Core.Contracts;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Erp.Core.Application.Pipeline;

/// <summary>
/// 4.1 pasul 6 si 9: tranzactia comenzii pe conexiunea unica a cererii (IDbSession, RCSI). Cheia de idempotenta se rezerva
/// si rezultatul se salveaza in aceeasi tranzactie; o retrimitere primeste rezultatul primei cereri (A16).
/// Deadlock-ul si expirarea blocarii se reincearca de 3 ori (SP-17); erorile THROW 5xxxx devin erori cu cod (NET-09).
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    IIdempotencyStore idempotency,
    ICurrentContext context,
    ITransientFailureDetector transient,
    ISqlErrorTranslator sqlErrors,
    TimeProvider time,
    ILogger<TransactionBehavior<TRequest, TResponse>> log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public const int MaxRetries = 3;

    internal static readonly JsonSerializerOptions ResultJson = new(JsonSerializerDefaults.Web);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!RequestMetadata<TRequest>.IsCommand)
        {
            return await next().ConfigureAwait(false);
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await RunOnceAsync(next, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (transient.IsTransient(ex))
            {
                if (attempt >= MaxRetries)
                {
                    log.LogWarning(ex, "{Request}: resursă ocupată după {Attempts} încercări", RequestMetadata<TRequest>.Name, attempt + 1);
                    throw ErpException.Conflict(CoreErrorCodes.ResourceBusy, "Resursa este ocupată de alt utilizator; reîncercați în câteva momente.");
                }

                log.LogInformation("{Request}: deadlock sau blocare expirată, reîncerc ({Attempt}/{Max})", RequestMetadata<TRequest>.Name, attempt + 1, MaxRetries);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), time, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (sqlErrors.Translate(ex) is { } translated)
            {
                throw translated;
            }
        }
    }

    private async Task<TResponse> RunOnceAsync(RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        await unitOfWork.BeginAsync(ct).ConfigureAwait(false);
        try
        {
            var key = context.IdempotencyKey;
            if (key is not null)
            {
                var previous = await idempotency.ClaimAsync(context.UserId, key, RequestMetadata<TRequest>.Name, ct).ConfigureAwait(false);
                if (previous is not null)
                {
                    await unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
                    return Replay(previous);
                }
            }

            var response = await next().ConfigureAwait(false);
            if (key is not null)
            {
                await idempotency.CompleteAsync(context.UserId, key, JsonSerializer.Serialize(response, ResultJson), ct).ConfigureAwait(false);
            }

            await unitOfWork.CommitAsync(ct).ConfigureAwait(false);
            return response;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static TResponse Replay(IdempotentResult previous)
    {
        if (!string.Equals(previous.RequestType, RequestMetadata<TRequest>.Name, StringComparison.Ordinal) || previous.ResultJson is null)
        {
            throw ErpException.Conflict(CoreErrorCodes.IdempotencyKeyReused, "Cheia de idempotență a fost folosită pentru altă operațiune.");
        }

        return JsonSerializer.Deserialize<TResponse>(previous.ResultJson, ResultJson)!;
    }
}

/// <summary>4.1 pasul 8: intrarile de audit ale comenzilor [Audited], in tranzactia comenzii (P5).</summary>
internal sealed class AuditBehavior<TRequest, TResponse>(IAuditWriter writer, AuditScope scope) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var action = RequestMetadata<TRequest>.AuditAction;
        if (action is null || !RequestMetadata<TRequest>.IsCommand)
        {
            return await next().ConfigureAwait(false);
        }

        // O reincercare dupa deadlock reia handler-ul: descrierile incercarii anterioare nu se pastreaza.
        scope.Reset();
        var response = await next().ConfigureAwait(false);

        var entries = scope.Entries.Count > 0
            ? scope.Entries.Select(e => new AuditRecord(action, e.EntityType, e.EntityId, e.Changes)).ToList()
            : [new AuditRecord(action, DefaultEntityType(action), null, null)];
        foreach (var entry in entries)
        {
            await writer.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    // Core.Partner.Create -> Core.Partner
    internal static string DefaultEntityType(string action) =>
        action.LastIndexOf('.') is var index and > 0 ? action[..index] : action;
}

/// <summary>Descrierile de audit ale cererii curente (scoped).</summary>
public sealed class AuditScope : IAuditScope
{
    private readonly List<(string EntityType, long? EntityId, object? Changes)> entries = [];

    public IReadOnlyList<(string EntityType, long? EntityId, object? Changes)> Entries => entries;

    public void Describe(string entityType, long? entityId, object? changes = null) => entries.Add((entityType, entityId, changes));

    internal void Reset() => entries.Clear();
}
