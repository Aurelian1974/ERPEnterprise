using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank;

/// <summary>Ordinele de plata prin fin.usp_PaymentOrder_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class PaymentOrderStore(IDbSession session) : IPaymentOrderStore
{
    public async Task<FinanceSaved> SaveAsync(int companyId, PaymentOrderDraft draft, int userId, CancellationToken ct)
    {
        var lines = new DataTable();
        lines.Columns.Add("LineNumber", typeof(short));
        lines.Columns.Add("TreasuryOperationTypeId", typeof(int));
        lines.Columns.Add("SettlementRef", typeof(string));
        lines.Columns.Add("Amount", typeof(decimal));
        lines.Columns.Add("CostCenterId", typeof(int));
        lines.Columns.Add("ProjectId", typeof(int));
        foreach (var l in draft.Lines)
        {
            lines.Rows.Add(l.LineNumber, l.OperationTypeId, (object?)l.SettlementRef ?? DBNull.Value, l.Amount, (object?)l.CostCenterId ?? DBNull.Value,
                (object?)l.ProjectId ?? DBNull.Value);
        }

        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_PaymentOrder_Save", new
        {
            draft.Id, CompanyId = companyId, draft.TreasuryAccountId, DocumentDate = draft.DocumentDate.ToDateTime(TimeOnly.MinValue),
            RequestedExecutionDate = draft.RequestedExecutionDate.ToDateTime(TimeOnly.MinValue), draft.IsUrgent, Origin = (byte)draft.Origin,
            Purpose = (byte)draft.Purpose, draft.PartnerId, draft.EmployeeId, draft.PartnerBankAccountId, draft.BeneficiaryName, draft.BeneficiaryIban,
            draft.BeneficiaryBic, draft.BeneficiaryFiscalCode, draft.AllowFreeIban, draft.RemittanceInfo, draft.BudgetObligationRef, draft.SourceProposalId,
            Lines = lines.AsTableValuedParameter("fin.PaymentOrderLineList"), draft.RowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public Task DeleteAsync(int companyId, int id, byte[] rowVersion, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_PaymentOrder_Delete", new { Id = id, CompanyId = companyId, RowVersion = rowVersion }, ct));

    public async Task<PaymentOrderState?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PaymentOrder_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        if (grid.IsConsumed)
        {
            return null;
        }

        var h = await grid.ReadSingleAsync<HeaderRow>().ConfigureAwait(false);
        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).ToList();
        var order = new PaymentOrderDetailDto(h.Id, h.TreasuryAccountId, h.AccountCode, h.Number, DateOnly.FromDateTime(h.DocumentDate),
            DateOnly.FromDateTime(h.RequestedExecutionDate), h.IsUrgent, (PaymentOrderOrigin)h.Origin, (PaymentPurpose)h.Purpose, h.PartnerId, h.PartnerName,
            h.PartnerAnafInactive, h.EmployeeId, h.PartnerBankAccountId, h.BeneficiaryName, h.BeneficiaryIban, h.BeneficiaryBic, h.BeneficiaryFiscalCode,
            h.IsFreeIban, h.CurrencyCode.Trim(), Money.Format(h.Amount), h.RemittanceInfo, h.EndToEndId, h.BudgetObligationRef, (PaymentOrderStatus)h.Status,
            h.ConfirmationStatus is { } c ? (PaymentConfirmationStatus)c : null, h.BankStatementLineId, h.PaymentBatchId, h.ValidatedAt, h.ValidatedByName,
            h.ApprovedAt, h.ApprovedByName, h.ReversalOfId, h.ReversedById,
            lines.Select(l => new PaymentOrderLineDto(l.Id, l.LineNumber, l.OperationTypeId, l.OperationTypeCode, l.OperationTypeName, l.SettlementRef,
                Money.Format(l.Amount), l.CostCenterId, l.ProjectId)).ToList(), h.RowVersion);
        return new PaymentOrderState(order, h.BranchId, h.ValidatedBy,
            lines.Select(l => new PaymentOrderLineRecord(l.LineNumber, l.OperationTypeId, l.SettlementRef, l.Amount, l.CostCenterId, l.ProjectId)).ToList());
    }

    public async Task<IReadOnlyList<PaymentOrderItemDto>> ListAsync(int companyId, int? treasuryAccountId, PaymentOrderStatus? status, bool unconfirmed,
        DateOnly? from, DateOnly? to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(Command("fin.usp_PaymentOrder_List", new
        {
            CompanyId = companyId, TreasuryAccountId = treasuryAccountId, Status = (byte?)status, Unconfirmed = unconfirmed,
            From = from?.ToDateTime(TimeOnly.MinValue), To = to?.ToDateTime(TimeOnly.MinValue),
        }, ct)).ConfigureAwait(false)).Select(o => new PaymentOrderItemDto(o.Id, o.TreasuryAccountId, o.AccountCode, o.Number, DateOnly.FromDateTime(o.DocumentDate),
            DateOnly.FromDateTime(o.RequestedExecutionDate), (PaymentOrderOrigin)o.Origin, (PaymentPurpose)o.Purpose, o.PartnerId, o.BeneficiaryName,
            o.BeneficiaryIban, o.CurrencyCode.Trim(), Money.Format(o.Amount), o.RemittanceInfo, (PaymentOrderStatus)o.Status,
            o.ConfirmationStatus is { } c ? (PaymentConfirmationStatus)c : null, o.ApprovedAt is not null, o.PaymentBatchId, o.ReversalOfId, o.RowVersion)).ToList();

    public Task<byte[]> ValidateAsync(int companyId, int id, string number, bool anafInactiveConfirmed, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.ExecuteScalarAsync<byte[]>(Command("fin.usp_PaymentOrder_Validate", new
        {
            Id = id, CompanyId = companyId, Number = number, AnafInactiveConfirmed = anafInactiveConfirmed, RowVersion = rowVersion, UserId = userId,
        }, ct))!;

    public async Task<IReadOnlyList<PaymentOrderVersion>> ApproveAsync(int companyId, IReadOnlyList<PaymentOrderVersion> orders, bool approve,
        bool requireApproval, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("RowVersion", typeof(byte[]));
        foreach (var o in orders)
        {
            table.Rows.Add(o.Id, o.RowVersion);
        }

        return (await session.Connection.QueryAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_PaymentOrder_Approve", new
        {
            CompanyId = companyId, Orders = table.AsTableValuedParameter("fin.IdVersionList"), Approve = approve, RequireApproval = requireApproval,
            UserId = userId,
        }, ct)).ConfigureAwait(false)).Select(r => new PaymentOrderVersion(r.Id, r.RowVersion)).ToList();
    }

    public async Task<FinanceSaved> ReverseAsync(int companyId, int id, string number, DateOnly reversalDate, byte[] rowVersion, int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_PaymentOrder_Reverse", new
        {
            Id = id, CompanyId = companyId, Number = number, ReversalDate = reversalDate.ToDateTime(TimeOnly.MinValue), RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record HeaderRow(int Id, int TreasuryAccountId, string AccountCode, int? BranchId, string? Number, DateTime DocumentDate,
        DateTime RequestedExecutionDate, bool IsUrgent, byte Origin, byte Purpose, int? PartnerId, string? PartnerName, bool PartnerAnafInactive, int? EmployeeId,
        int? PartnerBankAccountId, string BeneficiaryName, string BeneficiaryIban, string? BeneficiaryBic, string? BeneficiaryFiscalCode, bool IsFreeIban,
        string CurrencyCode, decimal Amount, string? RemittanceInfo, string? EndToEndId, string? BudgetObligationRef, byte Status, byte? ConfirmationStatus,
        long? BankStatementLineId, int? PaymentBatchId, DateTime? ValidatedAt, string? ValidatedByName, int? ValidatedBy, DateTime? ApprovedAt,
        string? ApprovedByName, int? ReversalOfId, int? ReversedById, byte[] RowVersion);

    private sealed record LineRow(long Id, short LineNumber, int OperationTypeId, string OperationTypeCode, string OperationTypeName, string? SettlementRef,
        decimal Amount, int? CostCenterId, int? ProjectId);

    private sealed record ItemRow(int Id, int TreasuryAccountId, string AccountCode, string? Number, DateTime DocumentDate, DateTime RequestedExecutionDate,
        byte Origin, byte Purpose, int? PartnerId, string BeneficiaryName, string BeneficiaryIban, string CurrencyCode, decimal Amount, string? RemittanceInfo,
        byte Status, byte? ConfirmationStatus, DateTime? ApprovedAt, int? PaymentBatchId, int? ReversalOfId, byte[] RowVersion);

    public Task<int> OperationTypeIdAsync(string code, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(new CommandDefinition("fin.usp_TreasuryOperationType_IdByCode", new { Code = code }, session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
}

/// <summary>
/// Setarile Finantelor pe firma din fin.CompanySetting; o setare lipsa are valoarea implicita din specificatie (toleranta comisionului de
/// card: implicit 3%, decizia din 23.11.2026).
/// </summary>
public sealed class FinanceSettings(IDbSession session) : IFinanceSettings
{
    public const string RequireApproval = "Fin.PaymentOrder.RequireApproval";
    public const string AutoValidateLevel = "Fin.Bank.AutoValidateLevel";
    public const string CardFeeTolerancePercent = "Fin.Card.FeeTolerancePercent";
    public const string CardSettlementDays = "Fin.Card.SettlementDays";
    public const string RequireSpvInvoice = "Fin.Payment.RequireSpvInvoice";
    public const string WriteOffMaxAmountRon = "Fin.WriteOff.MaxAmountRon";
    public const string RevalueAdvances = "Fin.Fx.RevalueAdvances";
    public const string FxCrossCurrencyRule = "Fin.Fx.CrossCurrencyRule";
    public const string FxCrossCurrencyMarkupPercent = "Fin.Fx.CrossCurrencyMarkupPercent";
    public const string BlockAllocationsInRevaluedMonth = "Fin.Fx.BlockAllocationsInRevaluedMonth";

    public async Task<Domain.Settlement.CrossRateRule> FxCrossCurrencyRuleAsync(int companyId, CancellationToken ct) =>
        byte.TryParse(await GetAsync(companyId, FxCrossCurrencyRule, ct).ConfigureAwait(false), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var rule) && rule is >= 1 and <= 3 ? (Domain.Settlement.CrossRateRule)rule
            : Domain.Settlement.CrossRateRule.BnrOnPaymentDate;

    public async Task<decimal> FxCrossCurrencyMarkupPercentAsync(int companyId, CancellationToken ct) =>
        decimal.TryParse(await GetAsync(companyId, FxCrossCurrencyMarkupPercent, ct).ConfigureAwait(false), System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var percent) && percent is >= 0 and < 100 ? percent : 1m;

    public async Task<bool> BlockAllocationsInRevaluedMonthAsync(int companyId, CancellationToken ct) =>
        string.Equals(await GetAsync(companyId, BlockAllocationsInRevaluedMonth, ct).ConfigureAwait(false), "true", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> RevalueAdvancesAsync(int companyId, CancellationToken ct) =>
        string.Equals(await GetAsync(companyId, RevalueAdvances, ct).ConfigureAwait(false), "true", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> RequireApprovalAsync(int companyId, CancellationToken ct)
    {
        var value = await GetAsync(companyId, RequireApproval, ct).ConfigureAwait(false);
        return value is null || !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<int> AutoValidateLevelAsync(int companyId, CancellationToken ct) =>
        int.TryParse(await GetAsync(companyId, AutoValidateLevel, ct).ConfigureAwait(false), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var level) && level is >= 0 and <= 2 ? level : 2;

    public async Task<decimal> CardFeeTolerancePercentAsync(int companyId, CancellationToken ct) =>
        decimal.TryParse(await GetAsync(companyId, CardFeeTolerancePercent, ct).ConfigureAwait(false), System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var percent) && percent is >= 0 and <= 100 ? percent : 3m;

    public async Task<int> CardSettlementDaysAsync(int companyId, CancellationToken ct) =>
        int.TryParse(await GetAsync(companyId, CardSettlementDays, ct).ConfigureAwait(false), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var days) && days is >= 0 and <= 30 ? days : 2;

    public async Task<decimal> WriteOffMaxAmountRonAsync(int companyId, CancellationToken ct) =>
        decimal.TryParse(await GetAsync(companyId, WriteOffMaxAmountRon, ct).ConfigureAwait(false), System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var amount) && amount >= 0 ? amount : 5m;

    public async Task<int> RequireSpvInvoiceAsync(int companyId, CancellationToken ct) =>
        int.TryParse(await GetAsync(companyId, RequireSpvInvoice, ct).ConfigureAwait(false), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var level) && level is >= 0 and <= 2 ? level : 2;

    public Task SaveAsync(int companyId, string code, string value, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(new CommandDefinition("fin.usp_CompanySetting_Save", new { CompanyId = companyId, Code = code, Value = value, UserId = userId },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    private Task<string?> GetAsync(int companyId, string code, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition("fin.usp_CompanySetting_Get", new { CompanyId = companyId, Code = code },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
}
