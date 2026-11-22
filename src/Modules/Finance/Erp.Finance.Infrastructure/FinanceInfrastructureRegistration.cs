using Erp.Core.Contracts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Bank;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Infrastructure.Bank;
using Erp.Finance.Infrastructure.Bank.Formats;
using Erp.Finance.Infrastructure.Treasury;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Infrastructure;

public static class FinanceInfrastructureRegistration
{
    /// <summary>Accesul la date al Finantelor si codurile de eroare SQL ale modulului (51000–51999). Conexiunea si tranzactia sunt ale Nucleului.</summary>
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(new FinanceConnection(connectionString));
        services.AddSingleton<ISqlErrorMap, FinanceSqlErrors>();
        services.AddScoped<ITreasuryAccountStore, TreasuryAccountStore>();
        services.AddScoped<ITreasuryAccountQueries, TreasuryAccountQueries>();
        services.AddScoped<ICashDocumentStore, CashDocumentStore>();
        services.AddScoped<ICashLimitLog, CashLimitLog>();
        services.AddScoped<ICashRegisterStore, CashRegisterStore>();
        services.AddTransient<CashDayAutoClose>();
        services.AddScoped<IAdvanceStore, AdvanceStore>();
        services.AddScoped<ITreasurySettingsStore, TreasurySettingsStore>();
        services.AddScoped<CashDraftFactory>();
        services.AddTransient<CashAdvanceOverdue>();
        services.AddScoped<IBankStatementStore, BankStatementStore>();
        services.AddScoped<IBankFormatProfileStore, BankFormatProfileStore>();
        services.AddScoped<IBankStatementLineStore, BankStatementLineStore>();
        services.AddScoped<IPaymentOrderStore, PaymentOrderStore>();
        services.AddScoped<IFinanceSettings, FinanceSettings>();
        services.AddScoped<PaymentOrderWorkflow>();
        services.AddScoped<BankLineValidation>();
        services.AddScoped<IBankMatchingStore, BankMatchingStore>();
        services.AddScoped<BankMatchingEngine>();
        services.AddScoped<ICardSettlementWriter, CardSettlementWriter>();
        services.AddScoped<IPaymentBatchStore, PaymentBatchStore>();
        services.AddSingleton<IPaymentFileWriter, Pain001Writer>();
        services.AddSingleton<IPaymentFileWriter, CsvPaymentWriter>();
        services.AddSingleton<IStatementParser, Camt053Parser>();
        services.AddSingleton<IStatementParser, Mt940Parser>();
        services.AddSingleton<IStatementParser, CsvStatementParser>();
        return services;
    }
}

/// <summary>
/// Codurile THROW ale Finantelor (02.02, F1 7): plaja 51000–51999, impartita pe sub-parti (F1 51000–51199, F2 51200–51399; 51224–51226 adaugate la implementare). 51010 e retras
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
        [51024] = (ErpErrorKind.NotFound, "FIN_EXPENSE_REPORT_NOT_FOUND"),
        [51025] = (ErpErrorKind.Conflict, "FIN_EXPENSE_REPORT_WRONG_STATUS"),
        [51201] = (ErpErrorKind.NotFound, "FIN_BANK_LINE_NOT_FOUND"),
        [51202] = (ErpErrorKind.Conflict, "FIN_CONCURRENCY_CONFLICT"),
        [51203] = (ErpErrorKind.Conflict, "FIN_BANK_LINE_NOT_EXPLAINED"),
        [51204] = (ErpErrorKind.Conflict, "FIN_BANK_STATEMENT_GAP"),
        [51205] = (ErpErrorKind.Validation, "FIN_BANK_LINE_PARTS_MISMATCH"),
        [51206] = (ErpErrorKind.Conflict, "FIN_BANK_FILE_ALREADY_IMPORTED"),
        [51207] = (ErpErrorKind.Conflict, "FIN_BANK_ACCOUNT_NOT_FOUND"),
        [51208] = (ErpErrorKind.Validation, "FIN_BANK_STATEMENT_BALANCE_MISMATCH"),
        [51209] = (ErpErrorKind.Validation, "FIN_BANK_STATEMENT_CURRENCY_MISMATCH"),
        [51210] = (ErpErrorKind.Validation, "FIN_BANK_FILE_INVALID"),
        [51211] = (ErpErrorKind.Conflict, "FIN_BANK_LINE_IMMUTABLE"),
        [51212] = (ErpErrorKind.Conflict, "FIN_PAYMENT_ORDER_NOT_DRAFT"),
        [51213] = (ErpErrorKind.Forbidden, "FIN_PAYMENT_ORDER_SELF_APPROVAL"),
        [51214] = (ErpErrorKind.Conflict, "FIN_PAYMENT_ORDER_NOT_APPROVED"),
        [51215] = (ErpErrorKind.Validation, "FIN_PAYMENT_ORDER_IBAN_INVALID"),
        [51216] = (ErpErrorKind.Validation, "FIN_PAYMENT_ORDER_BUDGET_REF_MISSING"),
        [51217] = (ErpErrorKind.Conflict, "FIN_PAYMENT_ORDER_ALREADY_CONFIRMED"),
        [51218] = (ErpErrorKind.Validation, "FIN_PAYMENT_ORDER_REEXPORT_REASON"),
        [51219] = (ErpErrorKind.Conflict, "FIN_BANK_LINE_PAYMENT_ORDER_TAKEN"),
        [51220] = (ErpErrorKind.Conflict, "FIN_BANK_TRANSIT_PAIR_TAKEN"),
        [51221] = (ErpErrorKind.Validation, "FIN_PAYMENT_ORDER_NOT_LINKED"),
        [51222] = (ErpErrorKind.Conflict, "FIN_PAYMENT_BATCH_NOT_READY"),
        [51223] = (ErpErrorKind.Validation, "FIN_BANK_PARTNER_PAYMENT_WITHOUT_OP"),
        [51224] = (ErpErrorKind.NotFound, "FIN_BANK_STATEMENT_NOT_FOUND"),
        [51225] = (ErpErrorKind.NotFound, "FIN_PAYMENT_ORDER_NOT_FOUND"),
        [51226] = (ErpErrorKind.NotFound, "FIN_PAYMENT_BATCH_NOT_FOUND"),
    };
}
