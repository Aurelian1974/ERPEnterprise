namespace Erp.Finance.Contracts.Reporting;

/// <summary>Cererea de balanta (C5 6): firma, planul (null = romanesc), perioada intr-un an financiar, nivelul (1, 2 sintetic, 3 analitic) si grupari.</summary>
public sealed record BalanceRequest(int CompanyId, int? ChartId, DateOnly From, DateOnly To, byte Level, bool IncludeClosingEntries, string? AccountFilter,
    bool ByPartner, bool ByCostCenter, bool ByProject);

/// <summary>Un rand de balanta cu 10 coloane (C5 6).</summary>
public sealed record BalanceRow(string AccountCode, string AccountName, int? PartnerId, int? CostCenterId, int? ProjectId, decimal OpeningDebit,
    decimal OpeningCredit, decimal PriorDebit, decimal PriorCredit, decimal PeriodDebit, decimal PeriodCredit, decimal ClosingDebit, decimal ClosingCredit);

/// <summary>Singurul mod de a citi solduri (C5 regula 1), pentru C5 si pentru Raportare (02.13).</summary>
public interface IBalanceQuery
{
    Task<IReadOnlyList<BalanceRow>> GetBalanceAsync(BalanceRequest request, CancellationToken ct);
}
