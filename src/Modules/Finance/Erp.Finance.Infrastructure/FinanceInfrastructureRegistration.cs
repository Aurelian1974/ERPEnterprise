using Erp.Core.Contracts;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Infrastructure.Treasury;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Infrastructure;

public static class FinanceInfrastructureRegistration
{
    /// <summary>Accesul la date al Finantelor si codurile de eroare SQL ale modulului (51000–51999). Conexiunea si tranzactia sunt ale Nucleului.</summary>
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISqlErrorMap, FinanceSqlErrors>();
        services.AddScoped<ITreasuryAccountStore, TreasuryAccountStore>();
        services.AddScoped<ITreasuryAccountQueries, TreasuryAccountQueries>();
        return services;
    }
}

/// <summary>
/// Codurile THROW ale Finantelor (02.02, F1 7): plaja 51000–51999, impartita pe sub-parti (F1 51000–51199). 51010 e retras
/// (anularea chitantei nu mai exista, decizia din 15.11.2026) si nu se refoloseste.
/// </summary>
public sealed class FinanceSqlErrors : ISqlErrorMap
{
    public IReadOnlyDictionary<int, (ErpErrorKind Kind, string Code)> Errors { get; } = new Dictionary<int, (ErpErrorKind, string)>
    {
        [51001] = (ErpErrorKind.NotFound, "FIN_CASH_DOCUMENT_NOT_FOUND"),
        [51002] = (ErpErrorKind.Conflict, "FIN_CONCURRENCY_CONFLICT"),
        [51003] = (ErpErrorKind.Conflict, "FIN_CASH_DOCUMENT_NOT_DRAFT"),
        [51004] = (ErpErrorKind.Conflict, "FIN_CASH_DAY_CLOSED"),
        [51005] = (ErpErrorKind.Conflict, "FIN_CASH_NEGATIVE_BALANCE"),
        [51006] = (ErpErrorKind.Conflict, "FIN_CASH_LIMIT_EXCEEDED"),
        [51007] = (ErpErrorKind.Conflict, "FIN_CASH_LIMIT_WARNING_UNCONFIRMED"),
        [51008] = (ErpErrorKind.Validation, "FIN_CASH_LINE_DIRECTION_MISMATCH"),
        [51009] = (ErpErrorKind.Validation, "FIN_CASH_TOTAL_MISMATCH"),
        [51011] = (ErpErrorKind.Conflict, "FIN_CASH_ALREADY_REVERSED"),
        [51012] = (ErpErrorKind.Conflict, "FIN_CASH_PREVIOUS_DAY_OPEN"),
        [51013] = (ErpErrorKind.Conflict, "FIN_CASH_DAY_HAS_DRAFTS"),
        [51014] = (ErpErrorKind.Forbidden, "FIN_CASHIER_NOT_ASSIGNED"),
        [51015] = (ErpErrorKind.Conflict, "FIN_TREASURY_ACCOUNT_CURRENCY_LOCKED"),
        [51016] = (ErpErrorKind.Conflict, "FIN_TREASURY_ACCOUNT_NOT_EMPTY"),
        [51017] = (ErpErrorKind.Conflict, "FIN_OPENING_BALANCE_EXISTS"),
        [51018] = (ErpErrorKind.Conflict, "FIN_ADVANCE_OVERDUE_UNCONFIRMED"),
        [51019] = (ErpErrorKind.Forbidden, "FIN_EXPENSE_REPORT_SELF_APPROVAL"),
        [51020] = (ErpErrorKind.Conflict, "FIN_EXPENSE_REPORT_ADVANCE_MISMATCH"),
        [51021] = (ErpErrorKind.Validation, "FIN_INVALID_REFERENCE"),
        [51022] = (ErpErrorKind.Conflict, "FIN_DUPLICATE_CODE"),
        [51023] = (ErpErrorKind.NotFound, "FIN_TREASURY_ACCOUNT_NOT_FOUND"),
    };
}
