using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Planning;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Propunerile de plata si regulile de aprobare prin procedurile fin.usp_PaymentProposal_*, fin.usp_PaymentApprovalRule_* (NET-06).</summary>
public sealed class PaymentProposalStore(IDbSession session) : IPaymentProposalStore
{
    public async Task<IReadOnlyList<PayableCandidate>> CandidatesAsync(int companyId, DateOnly dueUntil, IReadOnlyList<int>? partnerIds,
        IReadOnlyList<string>? currencies, byte? maxPriority, int? excludeProposalId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<CandidateRow>(Command("fin.usp_PaymentProposal_Candidates", new
        {
            CompanyId = companyId, DueUntil = Day(dueUntil), PartnerIds = partnerIds is null ? null : JsonSerializer.Serialize(partnerIds),
            Currencies = currencies is null ? null : JsonSerializer.Serialize(currencies), MaxPriority = maxPriority, ExcludeProposalId = excludeProposalId,
        }, ct)).ConfigureAwait(false))
        .Select(r => new PayableCandidate(r.OpenItemId, r.PartnerId, r.PartnerName, r.DocumentNo, DateOnly.FromDateTime(r.DueDate), r.CurrencyCode.Trim(), r.OpenAmount,
            r.ExchangeRate, r.PaymentPriority, r.PreferredTreasuryAccountId, r.AnafInactive, (EInvoiceStatus)r.EInvoiceStatus, r.SpvExempt, r.PartnerBankAccountId))
        .ToList();

    public async Task<IReadOnlyList<AccountFunds>> FundsAsync(int companyId, int? excludeProposalId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int TreasuryAccountId, string Code, string CurrencyCode, decimal BookBalance, decimal OverdraftLimit,
            decimal AlreadyProposed)>(Command("fin.usp_PaymentProposal_Funds", new { CompanyId = companyId, ExcludeProposalId = excludeProposalId }, ct))
            .ConfigureAwait(false))
        .Select(f => new AccountFunds(f.TreasuryAccountId, f.Code, f.CurrencyCode.Trim(), f.BookBalance, f.OverdraftLimit, f.AlreadyProposed)).ToList();

    public async Task<FinanceSaved> SaveAsync(int companyId, int? id, string? name, DateOnly proposalDate, DateOnly plannedPaymentDate, DateOnly dueDateUntil,
        int defaultAccountId, IReadOnlyList<ProposalLineRecord> lines, byte[]? rowVersion, int userId, CancellationToken ct)
    {
        using var table = new DataTable();
        foreach (var (column, type) in new (string, Type)[]
        {
            ("LineNumber", typeof(int)), ("OpenItemId", typeof(long)), ("PartnerId", typeof(int)), ("DueDate", typeof(DateTime)), ("CurrencyCode", typeof(string)),
            ("OpenAmount", typeof(decimal)), ("ProposedAmount", typeof(decimal)), ("ProposedAmountRon", typeof(decimal)), ("TreasuryAccountId", typeof(int)),
            ("Priority", typeof(byte)), ("Warnings", typeof(string)), ("LineStatus", typeof(byte)), ("RejectReason", typeof(string)),
        })
        {
            table.Columns.Add(column, type);
        }

        foreach (var l in lines)
        {
            table.Rows.Add(l.LineNumber, l.OpenItemId, l.PartnerId, Day(l.DueDate), l.CurrencyCode, l.OpenAmount, l.ProposedAmount, l.ProposedAmountRon, l.TreasuryAccountId,
                l.Priority, l.Warnings.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(l.Warnings), (byte)l.LineStatus, (object?)l.RejectReason ?? DBNull.Value);
        }

        var saved = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_PaymentProposal_Save", new
        {
            Id = id, CompanyId = companyId, Name = name, ProposalDate = Day(proposalDate), PlannedPaymentDate = Day(plannedPaymentDate),
            DueDateUntil = Day(dueDateUntil), DefaultTreasuryAccountId = defaultAccountId, Lines = table.AsTableValuedParameter("fin.PaymentProposalLineList"),
            RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(saved.Id, saved.RowVersion);
    }

    public async Task<ProposalDto?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PaymentProposal_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>().ConfigureAwait(false);
        if (h is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false))
            .Select(l => new ProposalLineDto(l.Id, l.LineNumber, l.OpenItemId, l.DocumentNo, l.PartnerId, l.PartnerName, DateOnly.FromDateTime(l.DueDate),
                l.CurrencyCode.Trim(), Money.Format(l.OpenAmount), Money.Format(l.ProposedAmount), Money.Format(l.ProposedAmountRon), l.TreasuryAccountId,
                l.TreasuryAccountCode, l.TreasuryCurrencyCode.Trim(), l.Priority,
                l.Warnings is null ? [] : JsonSerializer.Deserialize<List<string>>(l.Warnings) ?? [], (ProposalLineStatus)l.LineStatus, l.RejectReason,
                l.PaymentOrderId, l.PaymentOrderNumber)).ToList();
        var approvals = (await grid.ReadAsync<(long Id, int SubmissionNo, byte Level, int UserId, string UserName, byte Decision, DateTime DecidedAt, string? Comment,
                string? RejectedLineIds)>().ConfigureAwait(false))
            .Select(a => new ProposalApprovalDto(a.Id, a.SubmissionNo, a.Level, a.UserId, a.UserName, (ApprovalDecision)a.Decision, a.DecidedAt, a.Comment)).ToList();
        return new ProposalDto(h.Id, h.Number, h.Name, DateOnly.FromDateTime(h.ProposalDate), DateOnly.FromDateTime(h.PlannedPaymentDate),
            DateOnly.FromDateTime(h.DueDateUntil), h.DefaultTreasuryAccountId, h.DefaultTreasuryAccountCode, Money.Format(h.TotalAmountRon), Money.Format(h.MaxLineRon),
            h.RequiredApprovalLevel, h.CurrentApprovalLevel, h.SubmissionNo, (ProposalStatus)h.Status, h.LastApproverId, h.ReturnReason, h.SubmittedAt, h.ApprovedAt,
            h.GeneratedAt, h.CreatedBy, h.CreatedByName, lines, approvals, h.RowVersion);
    }

    public async Task<IReadOnlyList<ProposalItemDto>> ListAsync(int companyId, ProposalStatus? status, int? pendingForUser, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ListRow>(Command("fin.usp_PaymentProposal_List",
            new { CompanyId = companyId, Status = (byte?)status, PendingForUser = pendingForUser }, ct)).ConfigureAwait(false))
        .Select(r => new ProposalItemDto(r.Id, r.Number, r.Name, DateOnly.FromDateTime(r.ProposalDate), DateOnly.FromDateTime(r.PlannedPaymentDate),
            Money.Format(r.TotalAmountRon), (ProposalStatus)r.Status, r.RequiredApprovalLevel, r.CurrentApprovalLevel, r.CreatedByName, r.LineCount, r.PartnerCount))
        .ToList();

    public Task<byte[]?> TransitionAsync(int companyId, int id, byte action, int? requiredLevel, string? reason, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<byte[]?>(Command("fin.usp_PaymentProposal_Transition", new
        {
            Id = id, CompanyId = companyId, Action = action, RequiredLevel = (byte?)requiredLevel, Reason = reason, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public async Task<(ProposalStatus Status, byte[] RowVersion, int? LastApproverId)> DecideAsync(int companyId, int id, ApprovalDecision decision, string? comment,
        IReadOnlyList<long>? rejectedLineIds, byte[] rowVersion, int userId, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleAsync<(byte Status, byte[] RowVersion, int? LastApproverId)>(Command("fin.usp_PaymentProposal_Decide", new
        {
            Id = id, CompanyId = companyId, Decision = (byte)decision, Comment = comment,
            RejectedLineIds = rejectedLineIds is { Count: > 0 } ? JsonSerializer.Serialize(rejectedLineIds) : null, RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return ((ProposalStatus)r.Status, r.RowVersion, r.LastApproverId);
    }

    public Task<byte[]?> MarkGeneratedAsync(int companyId, int id, IReadOnlyList<(long LineId, int? PaymentOrderId, string? Reason)> lines, int userId,
        CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<byte[]?>(Command("fin.usp_PaymentProposal_MarkGenerated", new
        {
            Id = id, CompanyId = companyId,
            Lines = JsonSerializer.Serialize(lines.Select(l => new { id = l.LineId, paymentOrderId = l.PaymentOrderId, reason = l.Reason })), UserId = userId,
        }, ct));

    public Task ApproveOrdersAsync(int companyId, IReadOnlyList<int> paymentOrderIds, int approverId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_PaymentOrder_ApproveFromProposal",
            new { CompanyId = companyId, Ids = JsonSerializer.Serialize(paymentOrderIds), ApproverId = approverId }, ct));

    public async Task<ApprovalRulesDto> RulesAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PaymentApprovalRule_List", new { CompanyId = companyId }, ct))
            .ConfigureAwait(false);
        var rules = (await grid.ReadAsync<(int Id, byte Level, decimal ThresholdRon, byte Basis, int ApproverRoleId, string ApproverRoleCode, string ApproverRoleName)>()
            .ConfigureAwait(false))
            .Select(r => new ApprovalRuleDto(r.Level, Money.Format(r.ThresholdRon), r.Basis, r.ApproverRoleId, r.ApproverRoleCode, r.ApproverRoleName)).ToList();
        var roles = (await grid.ReadAsync<RoleOptionDto>().ConfigureAwait(false)).ToList();
        return new ApprovalRulesDto(rules, roles);
    }

    public Task SaveRulesAsync(int companyId, IReadOnlyList<(int Level, decimal ThresholdRon, byte Basis, int ApproverRoleId)> rules, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_PaymentApprovalRule_Save", new
        {
            CompanyId = companyId, UserId = userId,
            Rules = JsonSerializer.Serialize(rules.Select(r => new { level = r.Level, thresholdRon = r.ThresholdRon, basis = r.Basis, approverRoleId = r.ApproverRoleId })),
        }, ct));

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record CandidateRow(long OpenItemId, int PartnerId, string PartnerName, string DocumentNo, DateTime DueDate, string CurrencyCode, decimal OpenAmount,
        decimal ExchangeRate, byte PaymentPriority, int? PreferredTreasuryAccountId, bool AnafInactive, byte EInvoiceStatus, bool SpvExempt, int? PartnerBankAccountId);

    private sealed record HeaderRow(int Id, string Number, string? Name, DateTime ProposalDate, DateTime PlannedPaymentDate, DateTime DueDateUntil,
        int DefaultTreasuryAccountId, string DefaultTreasuryAccountCode, decimal TotalAmountRon, byte? RequiredApprovalLevel, byte CurrentApprovalLevel, int SubmissionNo,
        byte Status, int? LastApproverId, string? ReturnReason, DateTime? SubmittedAt, DateTime? ApprovedAt, DateTime? GeneratedAt, int CreatedBy, string? CreatedByName,
        DateTime CreatedAt, byte[] RowVersion, decimal MaxLineRon);

    private sealed record LineRow(long Id, int LineNumber, long OpenItemId, string DocumentNo, int PartnerId, string PartnerName, DateTime DueDate, string CurrencyCode,
        decimal OpenAmount, decimal ProposedAmount, decimal ProposedAmountRon, int TreasuryAccountId, string TreasuryAccountCode, string TreasuryCurrencyCode, byte Priority,
        string? Warnings, byte LineStatus, string? RejectReason, int? PaymentOrderId, string? PaymentOrderNumber);

    private sealed record ListRow(int Id, string Number, string? Name, DateTime ProposalDate, DateTime PlannedPaymentDate, decimal TotalAmountRon, byte Status,
        byte? RequiredApprovalLevel, byte CurrentApprovalLevel, string? CreatedByName, int LineCount, int PartnerCount);
}
