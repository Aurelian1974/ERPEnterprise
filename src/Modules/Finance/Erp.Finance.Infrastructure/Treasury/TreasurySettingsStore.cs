using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Treasury;

namespace Erp.Finance.Infrastructure.Treasury;

/// <summary>Configurarea trezoreriei prin fin.usp_TreasuryOperationType_*, fin.usp_ExpenseType_*, fin.usp_CashLimitRule_* (NET-06).</summary>
public sealed class TreasurySettingsStore(IDbSession session) : ITreasurySettingsStore
{
    public async Task<IReadOnlyList<OperationTypeDto>> ListOperationTypesAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<OperationRow>(Proc("fin.usp_TreasuryOperationType_List", new { }, ct)).ConfigureAwait(false))
        .Select(r => new OperationTypeDto(r.Id, r.Code, r.Name, (CashDirection)r.Direction, r.RequiresPartner, r.RequiresEmployee, r.RequiresCounterAccount,
            r.SettlementKind, r.CounterGlAccountCode, r.CashLimitCategory, r.AppliesToCash, r.AppliesToBank, r.IsSystem, r.IsActive, r.RowVersion)).ToList();

    public Task<FinanceSaved> SaveOperationTypeAsync(SaveOperationType type, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<FinanceSaved>(Proc("fin.usp_TreasuryOperationType_Save", new
        {
            type.Id, type.Code, type.Name, Direction = (byte)type.Direction, type.RequiresPartner, type.RequiresEmployee, type.RequiresCounterAccount,
            type.SettlementKind, type.CounterGlAccountCode,
            type.CashLimitCategory,
            type.AppliesToCash, type.AppliesToBank, type.IsActive, type.RowVersion, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<ExpenseTypeDto>> ListExpenseTypesAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<ExpenseRow>(Proc("fin.usp_ExpenseType_List", new { }, ct)).ConfigureAwait(false))
        .Select(r => new ExpenseTypeDto(r.Id, r.Code, r.Name, r.IsPerDiem, Money.Format(r.DailyAmount), Money.Format(r.NonTaxableDailyLimit),
            r.VatDeductibleOnReceipt, DateOnly.FromDateTime(r.ValidFrom), r.ValidTo is { } to ? DateOnly.FromDateTime(to) : null, r.CounterGlAccountCode,
            r.IsSystem, r.IsActive, r.RowVersion)).ToList();

    public Task<FinanceSaved> SaveExpenseTypeAsync(SaveExpenseType type, decimal? dailyAmount, decimal? nonTaxableLimit, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<FinanceSaved>(Proc("fin.usp_ExpenseType_Save", new
        {
            type.Id, type.Code, type.Name, type.IsPerDiem, DailyAmount = dailyAmount, NonTaxableDailyLimit = nonTaxableLimit, type.VatDeductibleOnReceipt,
            ValidFrom = type.ValidFrom.ToDateTime(TimeOnly.MinValue), ValidTo = type.ValidTo?.ToDateTime(TimeOnly.MinValue), type.CounterGlAccountCode,
            type.IsActive, type.RowVersion, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<CashLimitRuleDto>> ListCashLimitsAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<LimitRow>(Proc("fin.usp_CashLimitRule_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(r => new CashLimitRuleDto(r.Id, r.Category, Money.Format(r.Amount), (CashLimitAction)r.Action, DateOnly.FromDateTime(r.ValidFrom),
            r.ValidTo is { } to ? DateOnly.FromDateTime(to) : null, r.LegalReference, r.IsLegal, r.RowVersion)).ToList();

    public Task<FinanceSaved> SaveCashLimitAsync(int companyId, SaveCashLimitRule rule, decimal amount, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<FinanceSaved>(Proc("fin.usp_CashLimitRule_Save", new
        {
            rule.Id, CompanyId = companyId, Category = new DbString { Value = rule.Category, Length = 20, IsAnsi = true }, Amount = amount,
            Action = (byte)rule.Action, ValidFrom = rule.ValidFrom.ToDateTime(TimeOnly.MinValue), ValidTo = rule.ValidTo?.ToDateTime(TimeOnly.MinValue),
            rule.RowVersion, UserId = userId,
        }, ct));

    private CommandDefinition Proc(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record OperationRow(int Id, string Code, string Name, byte Direction, bool RequiresPartner, bool RequiresEmployee,
        bool RequiresCounterAccount, byte SettlementKind, string? CounterGlAccountCode, string? CashLimitCategory, bool AppliesToCash, bool AppliesToBank,
        bool IsSystem, bool IsActive, byte[] RowVersion);

    private sealed record ExpenseRow(int Id, string Code, string Name, bool IsPerDiem, decimal? DailyAmount, decimal? NonTaxableDailyLimit, string? CurrencyCode,
        string? CountryCode, bool VatDeductibleOnReceipt, DateTime ValidFrom, DateTime? ValidTo, string? CounterGlAccountCode, bool IsSystem, bool IsActive,
        byte[] RowVersion);

    private sealed record LimitRow(int Id, int? CompanyId, string Category, string CurrencyCode, decimal Amount, byte Action, DateTime ValidFrom,
        DateTime? ValidTo, string? LegalReference, bool IsLegal, byte[] RowVersion);
}
