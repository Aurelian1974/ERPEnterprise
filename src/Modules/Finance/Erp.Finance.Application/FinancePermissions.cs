namespace Erp.Finance.Application;

/// <summary>
/// Permisiunile Finantelor (02.02, F1 8); aceleasi coduri ca in seed-ul fin (core.Permission, ModuleCode „Fin”). Prefixul „Fin.” e cel
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
