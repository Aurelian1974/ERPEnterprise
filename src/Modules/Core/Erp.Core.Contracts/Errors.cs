namespace Erp.Core.Contracts;

public enum ErpErrorKind
{
    Validation,
    Unauthenticated,
    Forbidden,
    NotFound,
    Conflict,

    /// <summary>Un serviciu extern (de exemplu ANAF) nu raspunde sau raspunde cu eroare: 503, se poate reincerca.</summary>
    Unavailable,
}

/// <summary>
/// Eroare de business cu cod stabil MODUL_EROARE si mesaj in romana (NET-09). API-ul o intoarce ca ProblemDetails (NET-10):
/// 400 validare, 401 neautentificat, 403 drepturi sau licenta, 404 inexistent, 409 conflict, 503 serviciu extern indisponibil.
/// </summary>
public class ErpException(ErpErrorKind kind, string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public ErpErrorKind Kind { get; } = kind;

    public string Code { get; } = code;

    public static ErpException NotFound(string code, string message) => new(ErpErrorKind.NotFound, code, message);

    public static ErpException Conflict(string code, string message) => new(ErpErrorKind.Conflict, code, message);
}

/// <summary>400 cu erorile pe camp (4.1 pasul 4).</summary>
public sealed class ErpValidationException(IReadOnlyDictionary<string, string[]> errors)
    : ErpException(ErpErrorKind.Validation, CoreErrorCodes.Validation, "Datele trimise nu sunt valide.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Codurile API ale Nucleului folosite de pipeline (02.01, 4.1 si 7).</summary>
public static class CoreErrorCodes
{
    public const string Unauthenticated = "CORE_UNAUTHENTICATED";
    public const string Forbidden = "CORE_FORBIDDEN";
    public const string ModuleNotLicensed = "CORE_MODULE_NOT_LICENSED";
    public const string LicenseReadOnly = "CORE_LICENSE_READ_ONLY";
    public const string FiscalCodeNotLicensed = "CORE_FISCAL_CODE_NOT_LICENSED";
    public const string CompanyNotLicensed = "CORE_COMPANY_NOT_LICENSED";
    public const string LicenseInvalid = "CORE_LICENSE_INVALID";
    public const string LicenseOlder = "CORE_LICENSE_OLDER";
    public const string Validation = "CORE_VALIDATION";
    public const string PeriodClosed = "CORE_PERIOD_CLOSED";
    public const string ResourceBusy = "CORE_RESOURCE_BUSY";
    public const string IdempotencyKeyReused = "CORE_IDEMPOTENCY_KEY_REUSED";
    public const string ConcurrencyConflict = "CORE_CONCURRENCY_CONFLICT";
}
