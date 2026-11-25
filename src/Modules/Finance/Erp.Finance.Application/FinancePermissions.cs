namespace Erp.Finance.Application;

/// <summary>
/// Permisiunile Finantelor (02.02, F1 8 si F2 8); aceleasi coduri ca in seed-ul fin (core.Permission, ModuleCode „Fin”). Prefixul „Fin.” e cel
/// din specificatia validata; codul modulului din licenta ramane „Finance” (FinanceModule.Code).
/// </summary>
public static class FinancePermissions
{
    public const string TreasuryAccountView = "Fin.TreasuryAccount.View";
    public const string TreasuryAccountManage = "Fin.TreasuryAccount.Manage";
    public const string CashView = "Fin.Cash.View";
    public const string CashViewAll = "Fin.Cash.ViewAll";
    public const string CashCreate = "Fin.Cash.Create";
    public const string CashValidate = "Fin.Cash.Validate";
    public const string CashReverse = "Fin.Cash.Reverse";
    public const string CashCloseDay = "Fin.Cash.CloseDay";
    public const string CashReopenDay = "Fin.Cash.ReopenDay";
    public const string CashLimitManage = "Fin.CashLimit.Manage";
    public const string AdvanceManage = "Fin.Advance.Manage";
    public const string ExpenseReportCreate = "Fin.ExpenseReport.Create";
    public const string ExpenseReportApprove = "Fin.ExpenseReport.Approve";
    public const string ExpenseReportValidate = "Fin.ExpenseReport.Validate";
    public const string TreasuryConfigure = "Fin.Treasury.Configure";
    public const string BankView = "Fin.Bank.View";
    public const string BankStatementImport = "Fin.BankStatement.Import";
    public const string BankStatementReconcile = "Fin.BankStatement.Reconcile";
    public const string BankStatementValidate = "Fin.BankStatement.Validate";
    public const string BankStatementUnvalidate = "Fin.BankStatement.Unvalidate";
    public const string BankStatementAcceptGap = "Fin.BankStatement.AcceptGap";
    public const string BankRuleManage = "Fin.BankRule.Manage";
    public const string BankFormatManage = "Fin.BankFormat.Manage";
    public const string PaymentOrderCreate = "Fin.PaymentOrder.Create";
    public const string PaymentOrderValidate = "Fin.PaymentOrder.Validate";
    public const string PaymentOrderReverse = "Fin.PaymentOrder.Reverse";
    public const string PaymentOrderApprove = "Fin.PaymentOrder.Approve";
    public const string PaymentOrderExport = "Fin.PaymentOrder.Export";
    public const string PaymentOrderReExport = "Fin.PaymentOrder.ReExport";
    public const string PaymentOrderCancel = "Fin.PaymentOrder.Cancel";
    public const string PaymentOrderFreeIban = "Fin.PaymentOrder.FreeIban";
    public const string PartnerBalanceView = "Fin.PartnerBalance.View";
    public const string AllocationManage = "Fin.Allocation.Manage";
    public const string AllocationUndo = "Fin.Allocation.Undo";
    public const string OpenItemSetDispute = "Fin.OpenItem.SetDispute";
    public const string OpenItemBlockPayment = "Fin.OpenItem.BlockPayment";
    public const string PartnerProfileManage = "Fin.PartnerProfile.Manage";
    public const string CompensationCreate = "Fin.Compensation.Create";
    public const string CompensationValidate = "Fin.Compensation.Validate";
    public const string CommercialPaperManage = "Fin.CommercialPaper.Manage";
    public const string WriteOffCreate = "Fin.WriteOff.Create";
    public const string WriteOffAboveThreshold = "Fin.WriteOff.AboveThreshold";
    public const string BalanceConfirmationManage = "Fin.BalanceConfirmation.Manage";
    public const string PenaltyCalculate = "Fin.Penalty.Calculate";
    public const string PenaltyApprove = "Fin.Penalty.Approve";
    public const string OpeningBalanceImport = "Fin.OpeningBalance.Import";
    public const string FxView = "Fin.Fx.View";
    public const string FxRevalue = "Fin.Fx.Revalue";
    public const string FxReverseRevaluation = "Fin.Fx.ReverseRevaluation";
    public const string FxManualCrossRate = "Fin.Fx.ManualCrossRate";
    public const string FxSettings = "Fin.Fx.Settings";
    public const string PaymentProposalView = "Fin.PaymentProposal.View";
    public const string PaymentProposalCreate = "Fin.PaymentProposal.Create";
    public const string PaymentProposalApprove = "Fin.PaymentProposal.Approve";
    public const string PaymentProposalGenerateOrders = "Fin.PaymentProposal.GenerateOrders";
    public const string PaymentApprovalRuleManage = "Fin.PaymentApprovalRule.Manage";
    public const string CashFlowView = "Fin.CashFlow.View";
    public const string CashFlowPlan = "Fin.CashFlow.Plan";
    public const string CashFlowSnapshot = "Fin.CashFlow.Snapshot";

    public const string AccountView = "Fin.Account.View";
    public const string AccountManage = "Fin.Account.Manage";
    public const string ChartManage = "Fin.Chart.Manage";
    public const string MappingManage = "Fin.Mapping.Manage";
    public const string MappingFreeze = "Fin.Mapping.Freeze";
    public const string MappingUnfreeze = "Fin.Mapping.Unfreeze";

    public const string ReportTrialBalance = "Fin.Report.TrialBalance";

    public const string ReportDefine = "Fin.Report.Define";

    public const string ReportGroup = "Fin.Report.Group";

    public const string ReportInventoryRegister = "Fin.Report.InventoryRegister";

    public const string ReportStatements = "Fin.Report.Statements";

    public const string ReportStatementsSubmit = "Fin.Report.StatementsSubmit";
    public const string TaxView = "Fin.Tax.View";
    public const string TaxPrepare = "Fin.Tax.Prepare";
    public const string TaxGenerate = "Fin.Tax.Generate";
    public const string TaxSubmit = "Fin.Tax.Submit";

    public const string ProfitTaxManage = "Fin.ProfitTax.Manage";
    public const string PeriodView = "Fin.Period.View";
    public const string PeriodSoftClose = "Fin.Period.SoftClose";
    public const string PeriodClose = "Fin.Period.Close";
    public const string PeriodCloseOverride = "Fin.Period.CloseOverride";
    public const string PeriodReopen = "Fin.Period.Reopen";
    public const string PeriodManageChecklist = "Fin.Period.ManageChecklist";
    public const string FiscalYearClose = "Fin.FiscalYear.Close";
    public const string FiscalYearLock = "Fin.FiscalYear.Lock";
    public const string FiscalYearUnlock = "Fin.FiscalYear.Unlock";

    public const string JournalEntryView = "Fin.JournalEntry.View";
    public const string ManualEntryCreate = "Fin.ManualEntry.Create";
    public const string ManualEntryValidate = "Fin.ManualEntry.Validate";
    public const string ManualEntryApprove = "Fin.ManualEntry.Approve";
    public const string ManualEntryControlledAccounts = "Fin.ManualEntry.ControlledAccounts";
    public const string JournalEntryReverse = "Fin.JournalEntry.Reverse";
    public const string JournalEntryReverseAutomatic = "Fin.JournalEntry.ReverseAutomatic";
    public const string PostingModelView = "Fin.PostingModel.View";
    public const string PostingModelManage = "Fin.PostingModel.Manage";
    public const string PostingRetry = "Fin.Posting.Retry";
    public const string PostingIgnore = "Fin.Posting.Ignore";
    public const string PostingRepost = "Fin.Posting.Repost";
}

/// <summary>Codul modulului in licenta (08, 4.3) si in [Module].</summary>
public static class FinanceModule
{
    public const string Code = "Finance";
}

/// <summary>Rezultatul unei salvari: identificatorul si versiunea noua (pentru urmatoarea modificare, SQL-19).</summary>
public sealed record FinanceSaved(int Id, byte[] RowVersion);

/// <summary>Assembly-ul cazurilor de utilizare, dat pipeline-ului comun (AddErpMediator) de gazda.</summary>
public static class FinanceApplication
{
    public static System.Reflection.Assembly Assembly => typeof(FinanceApplication).Assembly;
}
