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
