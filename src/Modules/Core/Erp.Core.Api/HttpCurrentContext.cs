using System.Security.Claims;
using Erp.Core.Application.Abstractions;
using Erp.Core.Application.Me;
using Erp.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace Erp.Core.Api;

/// <summary>ICurrentContext al unei cereri HTTP, completat de <see cref="CurrentContextMiddleware"/> (scoped).</summary>
public sealed class HttpCurrentContext : ICurrentContext, ICurrentUserProfile
{
    private IReadOnlyCollection<string> permissions = [];

    public bool IsAuthenticated { get; private set; }

    public int UserId { get; private set; }

    public int CompanyId { get; private set; }

    public Guid CorrelationId { get; private set; } = Guid.NewGuid();

    public string? IdempotencyKey { get; private set; }

    public string? IpAddress { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Language { get; private set; } = "ro";

    public IReadOnlyCollection<string> Permissions => permissions;

    // Scope-ul pe dimensiuni (centre de cost, proiecte) se adauga odata cu dimensiunile (etapa 9).
    public bool HasPermission(string permission, DimensionRef? scope = null) => IsAuthenticated && permissions.Contains(permission);

    internal void SetRequest(Guid correlationId, string? idempotencyKey, string? ipAddress)
    {
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        IpAddress = ipAddress;
    }

    internal void SetUser(UserContextData user)
    {
        IsAuthenticated = true;
        UserId = user.UserId;
        CompanyId = user.CompanyId;
        UserName = user.UserName;
        DisplayName = user.DisplayName;
        Language = user.Language;
        permissions = user.Permissions;
    }
}

/// <summary>
/// Contextul cererii (4.1 pasul 1): corelarea (antetul X-Correlation-Id, intors si in raspuns), cheia de idempotenta,
/// IP-ul, utilizatorul din cookie (claim NameIdentifier = SubjectId) si firma curenta (antetul X-Company-Id).
/// Un utilizator dezactivat sau expirat ramane neautentificat (4.6).
/// </summary>
public sealed class CurrentContextMiddleware(RequestDelegate next)
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string CompanyHeader = "X-Company-Id";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const int MaxIdempotencyKeyLength = 100;

    public async Task InvokeAsync(HttpContext http, HttpCurrentContext context, IUserContextLoader users)
    {
        var correlationId = Guid.TryParse(http.Request.Headers[CorrelationHeader], out var received) ? received : Guid.NewGuid();
        var idempotencyKey = http.Request.Headers[IdempotencyHeader].ToString() is { Length: > 0 and <= MaxIdempotencyKeyLength } key ? key : null;
        context.SetRequest(correlationId, idempotencyKey, http.Connection.RemoteIpAddress?.ToString());
        // La pornirea raspunsului: tratarea erorilor goleste antetele setate inainte.
        http.Response.OnStarting(() =>
        {
            http.Response.Headers[CorrelationHeader] = correlationId.ToString();
            return Task.CompletedTask;
        });

        if (http.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var subjectId))
        {
            // 0 = nivelul instalarii (doar rolurile fara firma); lipsa = ultima firma a utilizatorului (usp_User_GetPermissions).
            int? companyId = int.TryParse(http.Request.Headers[CompanyHeader], out var requested) && requested >= 0 ? requested : null;
            var user = await users.LoadAsync(subjectId, companyId, http.RequestAborted).ConfigureAwait(false);
            if (user is not null)
            {
                context.SetUser(user);
            }
        }

        await next(http).ConfigureAwait(false);
    }
}
