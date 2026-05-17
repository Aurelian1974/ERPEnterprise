---
name: error-handling
description: >-
  Pattern complet de error handling pentru ERP: Result<T>, Error, ProblemDetails,
  global exception middleware, extensii ToActionResult, erori de validare,
  erori de domeniu vs excepții tehnice.
---

# Error Handling

## Când se aplică
Când utilizatorul cere să implementeze error handling, să returneze erori
corecte din handler sau controller, sau să configureze global exception middleware.

## Principiu fundamental

```
Erori business   → Result<T>.Failure(error)   — niciodată throw
Excepții tehnice → throw                       — baze de date down, null reference, etc.
Validare input   → ValidationException         — din FluentValidation pipeline behavior
Toate excepțiile → prinse de GlobalExceptionMiddleware → ProblemDetails response
```

---

## 1. Result Pattern

```csharp
// Shared.Kernel/Primitives/Result.cs
public sealed class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error    { get; }

    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error     = error;
    }

    public static Result Success()              => new(true, Error.None);
    public static Result Failure(Error error)   => new(false, error);
}

public sealed class Result<TValue>
{
    public bool    IsSuccess { get; }
    public bool    IsFailure => !IsSuccess;
    public TValue? Value     { get; }
    public Error   Error     { get; }

    private Result(TValue? value, Error error, bool isSuccess)
    {
        Value     = value;
        Error     = error;
        IsSuccess = isSuccess;
    }

    public static Result<TValue> Success(TValue value)
        => new(value, Error.None, true);

    public static Result<TValue> Failure(Error error)
        => new(default, error, false);

    // Implicit conversion din valoare
    public static implicit operator Result<TValue>(TValue value)
        => Success(value);
}
```

## 2. Error Type

```csharp
// Shared.Kernel/Primitives/Error.cs
public sealed record Error(string Code, string Description)
{
    public static readonly Error None =
        new(string.Empty, string.Empty);

    public static readonly Error NullValue =
        new("general.null_value", "A null value was provided.");

    // Factory pentru erori comune
    public static Error NotFound(string entity, Guid id) =>
        new($"{entity.ToLower()}.not_found",
            $"{entity} with id '{id}' was not found.");

    public static Error Forbidden =>
        new("general.forbidden",
            "You do not have permission to perform this action.");

    public static Error Conflict(string message) =>
        new("general.conflict", message);

    public static Error Validation(string message) =>
        new("general.validation", message);
}

// Shared.Kernel/Primitives/ErrorType.cs (pentru categorii)
public enum ErrorType
{
    Failure    = 0,
    Validation = 1,
    NotFound   = 2,
    Conflict   = 3,
    Forbidden  = 4,
}
```

## 3. Extensii Controller — ToActionResult

```csharp
// Shared.Infrastructure/Extensions/ResultExtensions.cs
public static class ResultExtensions
{
    public static IActionResult ToActionResult(this Error error)
    {
        return error.Code switch
        {
            var c when c.EndsWith(".not_found")    => new NotFoundObjectResult(error.ToProblemDetails()),
            var c when c.EndsWith(".forbidden")    => new ForbidResult(),
            var c when c.EndsWith(".conflict")     => new ConflictObjectResult(error.ToProblemDetails()),
            var c when c.EndsWith(".validation")   => new UnprocessableEntityObjectResult(error.ToProblemDetails()),
            _                                      => new BadRequestObjectResult(error.ToProblemDetails()),
        };
    }

    public static ProblemDetails ToProblemDetails(this Error error) =>
        new()
        {
            Title    = error.Code,
            Detail   = error.Description,
            Status   = error.ToHttpStatusCode(),
            Extensions = { ["errorCode"] = error.Code }
        };

    private static int ToHttpStatusCode(this Error error) =>
        error.Code switch
        {
            var c when c.EndsWith(".not_found")  => StatusCodes.Status404NotFound,
            var c when c.EndsWith(".forbidden")  => StatusCodes.Status403Forbidden,
            var c when c.EndsWith(".conflict")   => StatusCodes.Status409Conflict,
            var c when c.EndsWith(".validation") => StatusCodes.Status422UnprocessableEntity,
            _                                    => StatusCodes.Status400BadRequest,
        };
}

// Utilizare în controller
public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
{
    var result = await _sender.Send(new ApproveInvoiceCommand(id), ct);
    return result.IsSuccess
        ? NoContent()
        : result.Error.ToActionResult();   // extensie
}
```

## 4. Global Exception Middleware

```csharp
// Shared.Infrastructure/Middleware/ExceptionHandlingMiddleware.cs
public sealed class ExceptionHandlingMiddleware : IMiddleware
{
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger)
        => _logger = logger;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(
                "Validation failed for {Path}: {Errors}",
                context.Request.Path,
                ex.Errors.Select(e => e.ErrorMessage));

            context.Response.StatusCode  = StatusCodes.Status422UnprocessableEntity;
            context.Response.ContentType = "application/problem+json";

            var problem = new ValidationProblemDetails(
                ex.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Title  = "Validation failed",
                Status = StatusCodes.Status422UnprocessableEntity,
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(
                "Domain exception: {Code} — {Message}",
                ex.Error.Code, ex.Error.Description);

            context.Response.StatusCode  = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsJsonAsync(ex.Error.ToProblemDetails());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Path}", context.Request.Path);

            context.Response.StatusCode  = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Title  = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                // Nu expune stack trace în producție
                Detail = context.RequestServices
                    .GetRequiredService<IWebHostEnvironment>()
                    .IsDevelopment() ? ex.ToString() : null,
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}

// DomainException — pentru aruncarea excepțiilor din Domain
public sealed class DomainException : Exception
{
    public Error Error { get; }

    public DomainException(Error error)
        : base(error.Description)
        => Error = error;
}
```

## 5. Validation Behavior (MediatR Pipeline)

```csharp
// Shared.Infrastructure/Behaviors/ValidationBehavior.cs
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Any())
            throw new ValidationException(failures);

        return await next();
    }
}
```

## 6. Răspunsuri HTTP — convenție

| Situație | HTTP Status | Body |
|---|---|---|
| Success cu date | `200 OK` | DTO |
| Creat cu succes | `201 Created` + `Location` header | Id |
| Succes fără date | `204 No Content` | — |
| Validare input eșuată | `422 Unprocessable Entity` | `ValidationProblemDetails` |
| Eroare business | `400 Bad Request` | `ProblemDetails` cu `errorCode` |
| Nu există resursa | `404 Not Found` | `ProblemDetails` |
| Fără permisiuni | `403 Forbidden` | — |
| Conflict (duplicate) | `409 Conflict` | `ProblemDetails` |
| Eroare server | `500 Internal Server Error` | `ProblemDetails` (fără stack trace) |

## 7. Frontend — Error handling

```typescript
// lib/axios.ts — interceptor global pentru erori
api.interceptors.response.use(
  (response) => response.data,
  (error: AxiosError<ProblemDetails>) => {
    const status  = error.response?.status;
    const problem = error.response?.data;

    if (status === 401) {
      useAuthStore.getState().clear();
      window.location.href = '/login';
      return Promise.reject(error);
    }

    if (status === 403) {
      toast.error('You do not have permission to perform this action.');
      return Promise.reject(error);
    }

    // 422 — validare
    if (status === 422 && problem?.errors) {
      // Erorile de validare sunt tratate de React Hook Form
      return Promise.reject(error);
    }

    // Altele — toast generic
    toast.error(problem?.detail ?? 'An unexpected error occurred.');
    return Promise.reject(error);
  },
);

// Tip local pentru ProblemDetails
interface ProblemDetails {
  title?:     string;
  detail?:    string;
  status?:    number;
  errorCode?: string;
  errors?:    Record<string, string[]>;
}
```

## Reguli obligatorii

```
Business errors  — Result<T>.Failure(error), niciodată throw din handler
Domain errors    — throw new DomainException(error) din entitate, prins de middleware
Stack trace      — niciodată expus în producție (IsDevelopment check)
ProblemDetails   — format standard RFC 7807 pe toate răspunsurile de eroare
errorCode        — prezent în ProblemDetails.Extensions pentru frontend
ValidationErrors — 422 cu ValidationProblemDetails, câmpuri grupate
500 errors       — logate cu LogError + correlation ID
```
