using Erp.Core.Application.Abstractions;
using Erp.Core.Contracts;
using Microsoft.Data.SqlClient;

namespace Erp.Core.Infrastructure.Data;

/// <summary>Maparea codurilor THROW ale unui modul la erori API (NET-09): un singur tabel per modul.</summary>
public interface ISqlErrorMap
{
    IReadOnlyDictionary<int, (ErpErrorKind Kind, string Code)> Errors { get; }
}

/// <summary>Codurile 50000–50999 ale Nucleului (02.01, 7). 50009 si 50016 raman erori neprevazute (500).</summary>
public sealed class CoreSqlErrors : ISqlErrorMap
{
    public IReadOnlyDictionary<int, (ErpErrorKind Kind, string Code)> Errors { get; } = new Dictionary<int, (ErpErrorKind, string)>
    {
        [50001] = (ErpErrorKind.Conflict, "CORE_NUMBER_SERIES_NOT_FOUND"),
        [50002] = (ErpErrorKind.Conflict, "CORE_PARTNER_DUPLICATE_FISCAL_CODE"),
        [50003] = (ErpErrorKind.Validation, "CORE_PARTNER_INVALID_FISCAL_CODE"),
        [50004] = (ErpErrorKind.Conflict, "CORE_ITEM_DUPLICATE_CODE"),
        [50005] = (ErpErrorKind.Conflict, "CORE_ITEM_DUPLICATE_BARCODE"),
        [50006] = (ErpErrorKind.Conflict, CoreErrorCodes.ConcurrencyConflict),
        [50007] = (ErpErrorKind.Conflict, "CORE_EXCHANGE_RATE_NOT_FOUND"),
        [50008] = (ErpErrorKind.Conflict, "CORE_BNR_RATE_READONLY"),
        [50010] = (ErpErrorKind.Conflict, "CORE_ENTITY_IN_USE"),
        [50011] = (ErpErrorKind.Conflict, "CORE_UOM_FACTOR_LOCKED"),
        [50012] = (ErpErrorKind.Conflict, "CORE_EMPLOYEE_ALREADY_LINKED"),
        [50013] = (ErpErrorKind.Validation, "CORE_EXTERNAL_USER_INCOMPLETE"),
        [50014] = (ErpErrorKind.Conflict, "CORE_INITIAL_ADMIN_EXISTS"),
        [50015] = (ErpErrorKind.Conflict, "CORE_LAST_ADMINISTRATOR"),
        [50017] = (ErpErrorKind.Validation, "CORE_LICENSE_INVALID_DATES"),
        [50018] = (ErpErrorKind.Conflict, "CORE_DUPLICATE_CODE"),
        [50019] = (ErpErrorKind.Conflict, "CORE_COMPANY_DUPLICATE_FISCAL_CODE"),
        [50020] = (ErpErrorKind.Conflict, "CORE_NODE_HAS_ACTIVE_CHILDREN"),
        [50021] = (ErpErrorKind.Validation, "CORE_INVALID_REFERENCE"),
        [50022] = (ErpErrorKind.Validation, "CORE_INVALID_ADDRESS"),
        [50023] = (ErpErrorKind.Conflict, "CORE_BANK_ACCOUNT_DUPLICATE_IBAN"),
        [50024] = (ErpErrorKind.Conflict, "CORE_NUMBER_SERIES_LOCKED"),
        [50025] = (ErpErrorKind.Conflict, "CORE_NUMBER_SERIES_CLOSED"),
        [50026] = (ErpErrorKind.Conflict, "CORE_NUMBER_SERIES_DUPLICATE"),
        [50027] = (ErpErrorKind.NotFound, "CORE_ENTITY_NOT_FOUND"),
        [50029] = (ErpErrorKind.Conflict, "CORE_ADDRESS_TYPE_DUPLICATE"),
        [50030] = (ErpErrorKind.Validation, "CORE_ADDRESS_TYPE_PROTECTED"),
    };
}

public sealed class SqlErrorTranslator(IEnumerable<ISqlErrorMap> maps) : ISqlErrorTranslator
{
    private readonly Dictionary<int, (ErpErrorKind Kind, string Code)> errors = maps
        .SelectMany(m => m.Errors)
        .ToDictionary(e => e.Key, e => e.Value);

    public ErpException? Translate(Exception exception) =>
        exception is SqlException sql && errors.TryGetValue(sql.Number, out var error)
            ? new ErpException(error.Kind, error.Code, sql.Message, exception)
            : null;
}
