using Erp.Core.Application.Abstractions;
using Erp.Core.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Core.Api;

/// <summary>
/// Erorile API ca ProblemDetails (RFC 9457, NET-10): codul stabil in extensia „code”, corelarea in „correlationId”,
/// erorile pe camp in „errors”. Erorile neprevazute devin 500 fara detalii tehnice; detaliile raman in jurnal.
/// </summary>
public sealed class ErpExceptionHandler(
    IProblemDetailsService problems,
    ISqlErrorTranslator sqlErrors,
    ILogger<ErpExceptionHandler> log) : IExceptionHandler
{
    public const string UnexpectedCode = "CORE_UNEXPECTED_ERROR";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Clientul a renuntat (navigare, reincarcare): nu e o eroare a aplicatiei si nu mai are cine primi raspunsul.
        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            log.LogInformation("Cerere anulată de client: {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        // Handler-ul e singleton (ASP.NET Core); contextul cererii e scoped.
        var context = httpContext.RequestServices.GetRequiredService<ICurrentContext>();
        var error = exception as ErpException ?? sqlErrors.Translate(exception);
        var status = error is null ? StatusCodes.Status500InternalServerError : StatusFor(error.Kind);
        if (error is null)
        {
            log.LogError(exception, "Eroare neprevăzută la {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        var details = new ProblemDetails
        {
            Status = status,
            Title = error?.Message ?? "A apărut o eroare neprevăzută. Reîncercați sau contactați administratorul.",
            Type = $"https://httpstatuses.io/{status}",
        };
        details.Extensions["code"] = error?.Code ?? UnexpectedCode;
        details.Extensions["correlationId"] = context.CorrelationId;
        if (error is ErpValidationException validation)
        {
            details.Extensions["errors"] = validation.Errors;
        }

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = details,
            Exception = exception,
        }).ConfigureAwait(false);
    }

    public static int StatusFor(ErpErrorKind kind) => kind switch
    {
        ErpErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErpErrorKind.Unauthenticated => StatusCodes.Status401Unauthorized,
        ErpErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErpErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErpErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict,
    };
}
