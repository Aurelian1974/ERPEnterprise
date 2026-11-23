using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank;

/// <summary>Potrivirea, regulile, cardurile si tranzitul prin procedurile fin (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class BankMatchingStore(IDbSession session) : IBankMatchingStore
{
    public async Task<MatchingRun> RunAsync(int companyId, int? bankStatementId, int? treasuryAccountId, decimal feeTolerancePercent, int userId,
        CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_BankMatching_Run", new
        {
            CompanyId = companyId, BankStatementId = bankStatementId, TreasuryAccountId = treasuryAccountId, FeeTolerancePercent = feeTolerancePercent,
            UserId = userId,
        }, ct, 300)).ConfigureAwait(false);
        var matched = (await grid.ReadAsync<(long Id, byte Source, bool AutoValidate, byte[] RowVersion, DateTime BookingDate, byte ContinuityStatus)>()
            .ConfigureAwait(false)).Select(m => new MatchedLine(m.Id, (BankMatchSource)m.Source, m.AutoValidate, m.RowVersion, DateOnly.FromDateTime(m.BookingDate),
            (ContinuityStatus)m.ContinuityStatus)).ToList();
        var (considered, proposed) = await grid.ReadSingleAsync<(int Considered, int Proposed)>().ConfigureAwait(false);
        return new MatchingRun(matched, considered, proposed);
    }

    public async Task<IReadOnlyList<MatchingRuleDto>> ListRulesAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<RuleRow>(Command("fin.usp_MatchingRule_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(r => new MatchingRuleDto(r.Id, r.Priority, r.Name, r.TreasuryAccountId, r.Direction, r.CounterpartyIban, r.CounterpartyFiscalCode,
            r.RemittanceContains, r.BankTransactionCode, r.AmountFrom is { } f ? Money.Format(f) : null, r.AmountTo is { } t ? Money.Format(t) : null,
            r.OperationTypeId, r.OperationTypeCode, r.PartnerId, r.PartnerName, r.CostCenterId, r.ProjectId, r.NoteTemplate, r.AutoValidate, r.CreatedFromLineId,
            r.IsActive, r.RowVersion) { UsageCount = r.UsageCount }).ToList();

    public async Task<FinanceSaved> SaveRuleAsync(int companyId, MatchingRuleRecord rule, int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_MatchingRule_Save", new
        {
            rule.Id, CompanyId = companyId, rule.Priority, rule.Name, rule.TreasuryAccountId, rule.Direction, rule.CounterpartyIban, rule.CounterpartyFiscalCode,
            rule.RemittanceContains, rule.BankTransactionCode, rule.AmountFrom, rule.AmountTo, TreasuryOperationTypeId = rule.OperationTypeId, rule.PartnerId,
            rule.CostCenterId, rule.ProjectId, rule.NoteTemplate, rule.AutoValidate, rule.CreatedFromLineId, rule.IsActive, rule.RowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public async Task<IReadOnlyList<CardAcquirerDto>> ListAcquirersAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<CardAcquirerDto>(Command("fin.usp_CardAcquirer_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .ToList();

    public async Task<FinanceSaved> SaveAcquirerAsync(int companyId, int? id, string code, string name, string? iban, bool isActive, byte[]? rowVersion,
        int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_CardAcquirer_Save", new
        {
            Id = id, CompanyId = companyId, Code = code, Name = name, CounterpartyIban = iban, IsActive = isActive, RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public Task<int> RegisterCardSettlementAsync(int companyId, string acquirerCode, DateOnly salesDate, decimal grossAmount, DateOnly expectedSettlementDate,
        string sourceModule, string sourceRef, int userId, CancellationToken ct) =>
        session.Connection.ExecuteScalarAsync<int>(Command("fin.usp_CardSettlement_Register", new
        {
            CompanyId = companyId, AcquirerCode = acquirerCode, SalesDate = salesDate.ToDateTime(TimeOnly.MinValue), GrossAmount = grossAmount,
            ExpectedSettlementDate = expectedSettlementDate.ToDateTime(TimeOnly.MinValue), SourceModule = sourceModule, SourceRef = sourceRef, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<CardSettlementDto>> ListCardSettlementsAsync(int companyId, CardSettlementStatus? status, CancellationToken ct) =>
        (await session.Connection.QueryAsync<CardRow>(Command("fin.usp_CardSettlement_List", new { CompanyId = companyId, Status = (byte?)status }, ct))
            .ConfigureAwait(false)).Select(c => new CardSettlementDto(c.Id, c.AcquirerCode, DateOnly.FromDateTime(c.SalesDate), Money.Format(c.GrossAmount),
            DateOnly.FromDateTime(c.ExpectedSettlementDate), c.SourceModule, c.SourceRef, c.SettledAmount is { } s ? Money.Format(s) : null,
            c.FeeAmount is { } f ? Money.Format(f) : null, (CardSettlementStatus)c.Status, c.BankStatementLineId, c.Note, c.RowVersion)).ToList();

    public Task<byte[]> ResolveCardSettlementAsync(int companyId, int id, string note, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.ExecuteScalarAsync<byte[]>(Command("fin.usp_CardSettlement_Resolve",
            new { CompanyId = companyId, Id = id, Note = note, RowVersion = rowVersion, UserId = userId }, ct))!;

    public async Task<IReadOnlyList<TransitItemDto>> ListTransitAsync(int companyId, int? treasuryAccountId, DateOnly asOf, CancellationToken ct) =>
        (await session.Connection.QueryAsync<TransitRow>(Command("fin.usp_TransitItem_List", new
        {
            CompanyId = companyId, TreasuryAccountId = treasuryAccountId, AsOf = asOf.ToDateTime(TimeOnly.MinValue),
        }, ct)).ConfigureAwait(false)).Select(t => new TransitItemDto((TransitKind)t.Kind, t.TreasuryAccountId, t.SourceAccountId, DateOnly.FromDateTime(t.ItemDate),
            Money.Format(t.Amount), t.Reference, t.SourceId, t.AgeDays)).ToList();

    private CommandDefinition Command(string name, object parameters, CancellationToken ct, int? timeout = null) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: timeout);

    private sealed record RuleRow(int Id, int Priority, string Name, int? TreasuryAccountId, byte? Direction, string? CounterpartyIban,
        string? CounterpartyFiscalCode, string? RemittanceContains, string? BankTransactionCode, decimal? AmountFrom, decimal? AmountTo, int OperationTypeId,
        string OperationTypeCode, int? PartnerId, string? PartnerName, int? CostCenterId, int? ProjectId, string? NoteTemplate, bool AutoValidate,
        long? CreatedFromLineId, bool IsActive, byte[] RowVersion, int UsageCount);

    private sealed record CardRow(int Id, string AcquirerCode, DateTime SalesDate, decimal GrossAmount, DateTime ExpectedSettlementDate, string SourceModule,
        string SourceRef, decimal? SettledAmount, decimal? FeeAmount, byte Status, long? BankStatementLineId, string? Note, byte[] RowVersion);

    private sealed record TransitRow(int Kind, int? TreasuryAccountId, int? SourceAccountId, DateTime ItemDate, decimal Amount, string? Reference, long SourceId,
        int AgeDays);
}
