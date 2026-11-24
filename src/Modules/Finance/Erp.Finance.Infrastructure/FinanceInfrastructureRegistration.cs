using Erp.Core.Contracts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Bank;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Infrastructure.Bank;
using Erp.Finance.Infrastructure.Bank.Formats;
using Erp.Finance.Infrastructure.Settlement;
using Erp.Finance.Infrastructure.Treasury;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddTransient<PaymentBehaviorRecalculation>();
        services.AddTransient<CashFlowJobs>();
        services.AddTransient<Periods.FiscalYearCreation>();
        services.AddScoped<Application.Periods.IPeriodCloseStore, Periods.PeriodCloseStore>();
        services.AddScoped<Application.Periods.PeriodCloseService>();
        services.AddScoped<Application.Periods.ClosingEntries>();
        services.AddScoped<Application.Periods.IFiscalYearCloseStore, Periods.FiscalYearCloseStore>();
        services.AddScoped<Application.Periods.FiscalYearClosing>();
        services.AddScoped<Application.Periods.IOpeningBalanceStore, Periods.OpeningBalanceStore>();
        services.AddScoped<Application.Declarations.IVatRegisterStore, Declarations.VatRegisterStore>();
        services.AddScoped<Application.Declarations.ITaxReturnStore, Declarations.TaxReturnStore>();
        services.AddScoped<Application.Declarations.TaxReturnService>();
        services.AddScoped<Application.Declarations.ITaxReturnFileGenerator, Declarations.D300XmlGenerator>();
        services.AddScoped<Application.Declarations.ITaxReturnFileGenerator, Declarations.D394XmlGenerator>();
        services.AddScoped<Application.Declarations.ITaxReturnFileGenerator, Declarations.D390XmlGenerator>();
        services.AddScoped<Application.Declarations.ISaftStore, Declarations.SaftStore>();
        services.AddScoped<Application.Declarations.IStreamingTaxFileGenerator, Declarations.SaftFileGenerator>();
        foreach (var (code, severity, allowOverride, ok, fail) in Periods.SqlPeriodCloseCheck.Shipped)
        {
            services.AddScoped<Contracts.Periods.IPeriodCloseCheck>(sp => new Periods.SqlPeriodCloseCheck(sp.GetRequiredService<IDbSession>(), code, severity,
                allowOverride, ok, fail));
        }

        services.AddScoped<IBankStatementStore, BankStatementStore>();
        services.AddTransient<BankFolderImport>();
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
        services.AddScoped<ISettlementStore, SettlementStore>();
        services.AddScoped<SettlementService>();
        services.AddScoped<ISettlementService>(sp => sp.GetRequiredService<SettlementService>());
        services.AddScoped<InvoiceOpenItems>();
        services.AddScoped<TreasurySettlement>();
        services.AddScoped<ICompensationStore, CompensationStore>();
        services.AddSingleton<ICompensationPrinter, CompensationPrinter>();
        services.AddScoped<IBalanceConfirmationStore, BalanceConfirmationStore>();
        services.AddSingleton<IBalanceConfirmationPrinter, BalanceConfirmationPrinter>();
        services.AddScoped<IPenaltyStore, PenaltyStore>();
        services.AddScoped<IOpeningBalanceStore, OpeningBalanceStore>();
        services.AddScoped<Application.Fx.IFxRevaluationStore, FxRevaluationStore>();
        services.AddScoped<Application.Fx.IFxTreasuryStore, FxTreasuryStore>();
        services.AddScoped<Application.Fx.IFxReportStore, FxReportStore>();
        services.AddScoped<Application.Planning.IPaymentProposalStore, PaymentProposalStore>();
        services.AddScoped<Application.Planning.ICashFlowStore, CashFlowStore>();
        services.AddScoped<Application.Planning.CashFlowForecaster>();
        services.AddScoped<Application.Planning.ICashFlowSnapshotStore, CashFlowSnapshotStore>();
        services.AddScoped<Application.Accounts.IChartOfAccountsStore, Accounts.ChartOfAccountsStore>();
        services.AddScoped<Application.Accounts.ChartOfAccountsQueries>();
        services.AddScoped<Application.Accounts.IAltMappingStore, Accounts.AltMappingStore>();
        services.AddScoped<Application.Accounts.IAltMappingResultStore, Accounts.AltMappingResultStore>();
        services.AddScoped<Application.Periods.IAccountingPeriodStore, Periods.AccountingPeriodStore>();
        services.AddScoped<Application.Periods.AccountingPeriodGuard>();
        services.AddScoped<Application.Accounting.IJournalEntryStore, Accounting.JournalEntryStore>();
        services.AddScoped<Application.Accounting.IPostingStore, Accounting.PostingStore>();
        services.AddScoped<Application.Accounting.PostingService>();
        services.AddTransient<Accounting.PostingQueueRetry>();
        services.AddScoped<Application.Accounting.IRecurringEntryStore, Accounting.RecurringEntryStore>();
        services.AddScoped<Application.Accounting.IPostingModelStore, Accounting.PostingModelStore>();
        services.AddScoped<Application.Reporting.IBalanceStore, Reporting.BalanceStore>();
        services.AddScoped<Application.Reporting.BalanceService>();
        services.AddScoped<Contracts.Reporting.IBalanceQuery>(sp => sp.GetRequiredService<Application.Reporting.BalanceService>());
        services.AddTransient<Accounting.RecurringEntryGeneration>();
        services.AddScoped<Contracts.Periods.IAccountingPeriodGuard>(sp => sp.GetRequiredService<Application.Periods.AccountingPeriodGuard>());
        // Perioadele contabile (C3a) inlocuiesc IPeriodLock-ul implicit al Nucleului (fara perioade inchise).
        services.Replace(ServiceDescriptor.Scoped<IPeriodLock>(sp => sp.GetRequiredService<Application.Periods.AccountingPeriodGuard>()));
        services.AddScoped<Contracts.Accounts.IChartOfAccountsQueries>(sp => sp.GetRequiredService<Application.Accounts.ChartOfAccountsQueries>());
        services.AddScoped<Contracts.Accounts.IGlAccountValidator>(sp => sp.GetRequiredService<Application.Accounts.ChartOfAccountsQueries>());
        services.AddScoped<ICommercialPaperStore, CommercialPaperStore>();
        services.AddScoped<IPartnerBalanceQueries, PartnerBalanceQueries>();
        return services;
    }
}

/// <summary>
/// Codurile THROW ale Finantelor (02.02, F1 7): plaja 51000–51999, impartita pe sub-parti (F1 51000–51199, F2 51200–51399, F3 51400–51599; 51224–51226 si 51418 adaugate la implementare). 51010 e retras
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
        [51401] = (ErpErrorKind.Validation, "FIN_ALLOCATION_SIDES_INVALID"),
        [51402] = (ErpErrorKind.Validation, "FIN_ALLOCATION_PARTNER_MISMATCH"),
        [51403] = (ErpErrorKind.Conflict, "FIN_ALLOCATION_EXCEEDS_OPEN"),
        [51404] = (ErpErrorKind.Conflict, "FIN_ALLOCATION_UNDO_NOT_ALLOWED"),
        [51405] = (ErpErrorKind.Conflict, "FIN_OPEN_ITEM_DUPLICATE_SOURCE"),
        [51406] = (ErpErrorKind.Conflict, "FIN_OPEN_ITEM_BLOCKED_FOR_PAYMENT"),
        [51407] = (ErpErrorKind.Conflict, "FIN_OPEN_ITEM_NOT_IN_SPV"),
        [51408] = (ErpErrorKind.Conflict, "FIN_OPEN_ITEM_NOT_IN_SPV_UNCONFIRMED"),
        [51409] = (ErpErrorKind.Conflict, "FIN_OPEN_ITEM_DISPUTED_UNCONFIRMED"),
        [51410] = (ErpErrorKind.Validation, "FIN_COMPENSATION_NOT_BALANCED"),
        [51411] = (ErpErrorKind.Conflict, "FIN_COMPENSATION_NOT_DRAFT"),
        [51412] = (ErpErrorKind.Conflict, "FIN_COMMERCIAL_PAPER_DUPLICATE"),
        [51413] = (ErpErrorKind.Conflict, "FIN_COMMERCIAL_PAPER_STATUS_INVALID"),
        [51414] = (ErpErrorKind.Forbidden, "FIN_WRITEOFF_ABOVE_THRESHOLD"),
        [51415] = (ErpErrorKind.Conflict, "FIN_PENALTY_ALREADY_INVOICED"),
        [51416] = (ErpErrorKind.Conflict, "FIN_REFERENCE_RATE_MISSING"),
        [51417] = (ErpErrorKind.Conflict, "FIN_CONCURRENCY_CONFLICT"),
        [51418] = (ErpErrorKind.Validation, "FIN_ALLOCATION_CURRENCY_MISMATCH"),
        [51419] = (ErpErrorKind.NotFound, "FIN_COMPENSATION_NOT_FOUND"),
        [51420] = (ErpErrorKind.NotFound, "FIN_COMMERCIAL_PAPER_NOT_FOUND"),
        [51421] = (ErpErrorKind.NotFound, "FIN_BALANCE_CONFIRMATION_NOT_FOUND"),
        [51422] = (ErpErrorKind.NotFound, "FIN_PENALTY_CALCULATION_NOT_FOUND"),
        [51423] = (ErpErrorKind.Conflict, "FIN_PENALTY_NOT_DRAFT"),
        [51601] = (ErpErrorKind.NotFound, "FIN_FX_RUN_NOT_FOUND"),
        [51602] = (ErpErrorKind.Conflict, "FIN_CONCURRENCY_CONFLICT"),
        [51603] = (ErpErrorKind.Conflict, "FIN_FX_RUN_NOT_DRAFT"),
        [51604] = (ErpErrorKind.Conflict, "FIN_FX_RUN_ALREADY_VALIDATED"),
        [51605] = (ErpErrorKind.Conflict, "FIN_FX_RATE_MISSING"),
        [51607] = (ErpErrorKind.Conflict, "FIN_FX_MONTH_REVALUED"),
        [51606] = (ErpErrorKind.Conflict, "FIN_FX_LATER_RUN_EXISTS"),
        [51609] = (ErpErrorKind.Conflict, "FIN_FX_RUN_OUT_OF_ORDER"),
        [51610] = (ErpErrorKind.Conflict, "FIN_FX_RUN_STALE"),
        [51611] = (ErpErrorKind.Conflict, "FIN_FX_RUN_SETTLED_AFTER"),
        [51701] = (ErpErrorKind.NotFound, "FIN_PROPOSAL_NOT_FOUND"),
        [51702] = (ErpErrorKind.Conflict, "FIN_CONCURRENCY_CONFLICT"),
        [51703] = (ErpErrorKind.Conflict, "FIN_PROPOSAL_NOT_IN_APPROVAL"),
        [51704] = (ErpErrorKind.Forbidden, "FIN_PROPOSAL_SELF_APPROVAL"),
        [51705] = (ErpErrorKind.Forbidden, "FIN_PROPOSAL_SAME_APPROVER_TWICE"),
        [51706] = (ErpErrorKind.Forbidden, "FIN_PROPOSAL_APPROVER_NOT_ALLOWED"),
        [51707] = (ErpErrorKind.Conflict, "FIN_PROPOSAL_INVOICE_ALREADY_PROPOSED"),
        [51708] = (ErpErrorKind.Validation, "FIN_PROPOSAL_AMOUNT_EXCEEDS_OPEN"),
        [51711] = (ErpErrorKind.Validation, "FIN_CASHFLOW_HORIZON_TOO_LONG"),
        [51712] = (ErpErrorKind.NotFound, "FIN_CASHFLOW_PLAN_ITEM_NOT_FOUND"),
        [51801] = (ErpErrorKind.NotFound, "FIN_ACCOUNT_NOT_FOUND"),
        [51802] = (ErpErrorKind.Conflict, "FIN_CONCURRENCY_CONFLICT"),
        [51803] = (ErpErrorKind.Conflict, "FIN_ACCOUNT_CODE_DUPLICATE"),
        [51804] = (ErpErrorKind.Conflict, "FIN_ACCOUNT_HAS_MOVEMENTS"),
        [51805] = (ErpErrorKind.Validation, "FIN_ACCOUNT_PARENT_INVALID"),
        [51806] = (ErpErrorKind.Validation, "FIN_ACCOUNT_NOT_POSTABLE"),
        [51807] = (ErpErrorKind.Forbidden, "FIN_ACCOUNT_SYSTEM_READONLY"),
        [51808] = (ErpErrorKind.Validation, "FIN_MAPPING_PERCENT_INVALID"),
        [51809] = (ErpErrorKind.Conflict, "FIN_MAPPING_PRIORITY_DUPLICATE"),
        [51810] = (ErpErrorKind.Conflict, "FIN_MAPPING_TARGET_IN_USE"),
        [51812] = (ErpErrorKind.Forbidden, "FIN_CHART_STATUTORY_READONLY"),
        [51813] = (ErpErrorKind.Validation, "FIN_ACCOUNT_DIMENSION_RULE_LOOSER"),
        [51814] = (ErpErrorKind.Conflict, "FIN_ACCOUNT_HAS_ACTIVE_CHILDREN"),
        [51815] = (ErpErrorKind.Conflict, "FIN_MAPPING_INCOMPLETE"),
        [51841] = (ErpErrorKind.NotFound, "FIN_ENTRY_NOT_FOUND"),
        [51842] = (ErpErrorKind.Conflict, "FIN_CONCURRENCY_CONFLICT"),
        [51843] = (ErpErrorKind.Validation, "FIN_ENTRY_UNBALANCED"),
        [51844] = (ErpErrorKind.Validation, "FIN_ENTRY_ACCOUNT_INVALID"),
        [51845] = (ErpErrorKind.Validation, "FIN_ENTRY_DIMENSION_MISSING"),
        [51846] = (ErpErrorKind.Validation, "FIN_ENTRY_PARTY_MISSING"),
        [51847] = (ErpErrorKind.Validation, "FIN_ENTRY_CURRENCY_NOT_ALLOWED"),
        [51848] = (ErpErrorKind.Conflict, "FIN_PERIOD_CLOSED"),
        [51849] = (ErpErrorKind.Conflict, "FIN_ENTRY_ALREADY_POSTED"),
        [51850] = (ErpErrorKind.Conflict, "FIN_ENTRY_NOT_DRAFT"),
        [51852] = (ErpErrorKind.Forbidden, "FIN_ENTRY_AUTOMATIC_REVERSE_FORBIDDEN"),
        [51853] = (ErpErrorKind.Forbidden, "FIN_ENTRY_FOUR_EYES"),
        [51854] = (ErpErrorKind.Conflict, "FIN_REPOST_PERIOD_CLOSED"),
        [51855] = (ErpErrorKind.NotFound, "FIN_POSTING_MODEL_MISSING"),
        [51973] = (ErpErrorKind.Validation, "FIN_BALANCE_CROSSES_YEARS"),
        [51941] = (ErpErrorKind.NotFound, "FIN_TAX_RETURN_NOT_FOUND"),
        [51942] = (ErpErrorKind.Conflict, "FIN_TAX_RETURN_CONCURRENCY_CONFLICT"),
        [51943] = (ErpErrorKind.Conflict, "FIN_TAX_RETURN_PERIOD_OPEN"),
        [51944] = (ErpErrorKind.Conflict, "FIN_TAX_RETURN_DATA_CHANGED"),
        [51946] = (ErpErrorKind.Validation, "FIN_TAX_RETURN_REASON_REQUIRED"),
        [51947] = (ErpErrorKind.Conflict, "FIN_TAX_RETURN_ALREADY_SUBMITTED"),
        [51948] = (ErpErrorKind.Conflict, "FIN_TAX_RETURN_NOT_GENERATED"),
        [51951] = (ErpErrorKind.Validation, "FIN_SAFT_ACCOUNT_CODE_MISSING"),
        [51952] = (ErpErrorKind.Validation, "FIN_SAFT_PARTNER_INVALID"),
        [51901] = (ErpErrorKind.NotFound, "FIN_PERIOD_NOT_FOUND"),
        [51902] = (ErpErrorKind.Conflict, "FIN_PERIOD_CONCURRENCY_CONFLICT"),
        [51903] = (ErpErrorKind.Conflict, "FIN_PERIOD_CLOSE_BLOCKED"),
        [51904] = (ErpErrorKind.Conflict, "FIN_PERIOD_PREVIOUS_OPEN"),
        [51905] = (ErpErrorKind.Conflict, "FIN_PERIOD_REOPEN_ORDER"),
        [51906] = (ErpErrorKind.Conflict, "FIN_PERIOD_LOCKED"),
        [51907] = (ErpErrorKind.Validation, "FIN_PERIOD_REASON_REQUIRED"),
        [51908] = (ErpErrorKind.Conflict, "FIN_PERIOD_EVENTS_IN_TRANSIT"),
        [51909] = (ErpErrorKind.Forbidden, "FIN_PERIOD_OVERRIDE_FORBIDDEN"),
        [51911] = (ErpErrorKind.Conflict, "FIN_FISCAL_YEAR_NOT_CLOSABLE"),
        [51913] = (ErpErrorKind.Validation, "FIN_LEGAL_RESERVE_INVALID"),
        [51914] = (ErpErrorKind.Validation, "FIN_OPENING_BALANCE_UNBALANCED"),
        [51915] = (ErpErrorKind.Validation, "FIN_OPENING_BALANCE_SUBLEDGER_MISMATCH"),
        [51916] = (ErpErrorKind.Conflict, "FIN_OPENING_BALANCE_HAS_ENTRIES"),
        [51912] = (ErpErrorKind.Conflict, "FIN_FISCAL_YEAR_LOCKED"),
    };
}
