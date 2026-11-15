using Erp.Core.Contracts;
using FluentValidation;
using MediatR;

namespace Erp.Core.Application.Pipeline;

/// <summary>4.1 pasul 4: validatorii FluentValidation ai comenzii; 400 cu erorile pe camp (NET-11).</summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!RequestMetadata<TRequest>.IsCommand)
        {
            return await next().ConfigureAwait(false);
        }

        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new ErpValidationException(failures
                .GroupBy(f => FieldName(f.PropertyName), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal));
        }

        return await next().ConfigureAwait(false);
    }

    // Numele campurilor ca in JSON-ul API (camelCase), de exemplu Lines[0].Quantity -> lines[0].quantity.
    internal static string FieldName(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}

/// <summary>4.1 pasul 5: comenzile cu data contabila intr-o perioada inchisa sunt refuzate (P4, 409).</summary>
internal sealed class PeriodLockBehavior<TRequest, TResponse>(IPeriodLock periods, ICurrentContext context) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (RequestMetadata<TRequest>.IsCommand && request is IAccountingDated dated
            && await periods.IsClosedAsync(context.CompanyId, dated.AccountingDate, cancellationToken).ConfigureAwait(false))
        {
            throw ErpException.Conflict(CoreErrorCodes.PeriodClosed,
                $"Perioada {dated.AccountingDate:MM.yyyy} este închisă; alegeți o dată dintr-o perioadă deschisă sau cereți redeschiderea.");
        }

        return await next().ConfigureAwait(false);
    }
}
