using Erp.Core.Contracts;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Erp.Core.Application.Pipeline;

/// <summary>4.1 pasul 1: corelarea, utilizatorul, firma si comanda, in toate jurnalele cererii (NET-18).</summary>
internal sealed class CorrelationBehavior<TRequest, TResponse>(ICurrentContext context, ILogger<CorrelationBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        using var scope = log.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = context.CorrelationId,
            ["UserId"] = context.UserId,
            ["CompanyId"] = context.CompanyId,
            ["Request"] = RequestMetadata<TRequest>.Name,
            ["Module"] = RequestMetadata<TRequest>.Module ?? string.Empty,
        });
        return await next().ConfigureAwait(false);
    }
}

/// <summary>
/// 4.1 pasul 2: modulul trebuie sa fie licentiat. In modul doar-citire (4.4) orice comanda e refuzata, la fel pe o firma al carei
/// CUI nu mai e in licenta (08, 4.3 pasul 7), cu exceptia comenzilor care permit iesirea din aceasta stare (incarcarea licentei).
/// </summary>
internal sealed class LicenseBehavior<TRequest, TResponse>(ILicenseInfo license, ICurrentContext context) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var module = RequestMetadata<TRequest>.Module;
        if (module is not null && !license.IsModuleActive(module))
        {
            throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.ModuleNotLicensed, $"Modulul {module} nu este inclus în licență.");
        }

        if (RequestMetadata<TRequest>.IsCommand && !RequestMetadata<TRequest>.AllowedWhenLicenseReadOnly)
        {
            if (license.State == LicenseState.ReadOnly)
            {
                throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.LicenseReadOnly,
                    license.HasLicense
                        ? "Licența a expirat: aplicația funcționează doar pentru consultare. Încărcați o licență nouă."
                        : "Instalarea nu are o licență validă: aplicația funcționează doar pentru consultare. Încărcați licența.");
            }

            if (context.CompanyId != 0 && !license.IsCompanyLicensed(context.CompanyId))
            {
                throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.CompanyNotLicensed,
                    "CUI-ul firmei curente nu mai figurează în licență: firma este doar pentru consultare. Încărcați o licență care o include.");
            }
        }

        return next();
    }
}

/// <summary>
/// 4.1 pasul 3: utilizator autentificat si permisiunea declarata pe cerere. O cerere fara permisiune declarata e refuzata
/// (fail closed), cu exceptia interogarilor marcate [AuthenticatedUser].
/// </summary>
internal sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentContext context, ILogger<AuthorizationBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!context.IsAuthenticated)
        {
            throw new ErpException(ErpErrorKind.Unauthenticated, CoreErrorCodes.Unauthenticated, "Autentificați-vă pentru a continua.");
        }

        var permission = RequestMetadata<TRequest>.Permission;
        if (permission is null)
        {
            if (RequestMetadata<TRequest>.AnyAuthenticatedUser && !RequestMetadata<TRequest>.IsCommand)
            {
                return next();
            }

            log.LogError("Cererea {Request} nu declară o permisiune; este refuzată", RequestMetadata<TRequest>.Name);
            throw Forbidden();
        }

        if (!context.HasPermission(permission))
        {
            log.LogWarning("Permisiune lipsă {Permission} pentru {Request}", permission, RequestMetadata<TRequest>.Name);
            throw Forbidden();
        }

        return next();
    }

    private static ErpException Forbidden() =>
        new(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, "Nu aveți dreptul să efectuați această operațiune.");
}
