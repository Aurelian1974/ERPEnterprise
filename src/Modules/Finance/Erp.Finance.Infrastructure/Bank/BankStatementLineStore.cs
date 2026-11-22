using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank;

/// <summary>Liniile de extras prin fin.usp_BankStatementLine_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class BankStatementLineStore(IDbSession session) : IBankStatementLineStore
{
    public async Task<(IReadOnlyList<BankLineRow> Lines, IReadOnlyList<BankLinePartRow> Parts)> GetAsync(int companyId, IReadOnlyCollection<long> ids,
        CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_BankStatementLine_Get",
            new { CompanyId = companyId, Lines = Versions(ids.Select(id => new BankLineVersion(id, null!))) }, ct)).ConfigureAwait(false);
        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).Select(l => new BankLineRow(l.Id, l.BankStatementId, l.StatementNo, l.LineNumber,
            DateOnly.FromDateTime(l.BookingDate), l.ValueDate is { } v ? DateOnly.FromDateTime(v) : null, l.Amount, l.CounterpartyName, l.CounterpartyIban,
            l.RemittanceInfo, l.EndToEndId, l.BankReference, (BankLineMatchStatus)l.MatchStatus, l.PartnerId, l.PartnerName, l.TreasuryAccountId,
            (TreasuryAccountKind)l.AccountKind, l.CurrencyCode.Trim(), l.ValidatedAt, l.RowVersion)).ToList();
        var parts = (await grid.ReadAsync<BankLinePartRow>().ConfigureAwait(false)).ToList();
        return (lines, parts);
    }

    public Task<byte[]> ExplainAsync(int companyId, long lineId, IReadOnlyList<BankLinePartRecord> parts, byte[] rowVersion, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("PartNumber", typeof(short));
        table.Columns.Add("TreasuryOperationTypeId", typeof(int));
        table.Columns.Add("Amount", typeof(decimal));
        table.Columns.Add("PartnerId", typeof(int));
        table.Columns.Add("EmployeeId", typeof(int));
        table.Columns.Add("SettlementRef", typeof(string));
        table.Columns.Add("PaymentOrderId", typeof(int));
        table.Columns.Add("CashDocumentId", typeof(int));
        table.Columns.Add("CounterTreasuryAccountId", typeof(int));
        table.Columns.Add("CardSettlementExpectedId", typeof(int));
        table.Columns.Add("BudgetObligationRef", typeof(string));
        table.Columns.Add("CostCenterId", typeof(int));
        table.Columns.Add("ProjectId", typeof(int));
        table.Columns.Add("Note", typeof(string));
        foreach (var p in parts)
        {
            table.Rows.Add(p.PartNumber, p.OperationTypeId, p.Amount, (object?)p.PartnerId ?? DBNull.Value, (object?)p.EmployeeId ?? DBNull.Value,
                (object?)p.SettlementRef ?? DBNull.Value, (object?)p.PaymentOrderId ?? DBNull.Value, (object?)p.CashDocumentId ?? DBNull.Value, (object?)p.CounterTreasuryAccountId ?? DBNull.Value,
                (object?)p.CardSettlementExpectedId ?? DBNull.Value, (object?)p.BudgetObligationRef ?? DBNull.Value, (object?)p.CostCenterId ?? DBNull.Value, (object?)p.ProjectId ?? DBNull.Value,
                (object?)p.Note ?? DBNull.Value);
        }

        return session.Connection.ExecuteScalarAsync<byte[]>(Command("fin.usp_BankStatementLine_Explain", new
        {
            CompanyId = companyId, BankStatementLineId = lineId, Parts = table.AsTableValuedParameter("fin.BankStatementLinePartList"), RowVersion = rowVersion,
            UserId = userId,
        }, ct))!;
    }

    public async Task<(IReadOnlyList<BankLineSaved> Saved, IReadOnlyList<PaymentOrderConfirmed> Confirmed,
        IReadOnlyList<(int PaymentOrderId, long BankStatementLineId)> Rejected)> ValidateAsync(int companyId, IReadOnlyList<BankLineVersion> lines,
        decimal cardFeeTolerancePercent, int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_BankStatementLine_Validate",
            new { CompanyId = companyId, Lines = Versions(lines), UserId = userId, CardFeeTolerancePercent = cardFeeTolerancePercent }, ct)).ConfigureAwait(false);
        var saved = (await grid.ReadAsync<(long Id, byte[] RowVersion)>().ConfigureAwait(false)).Select(r => new BankLineSaved(r.Id, r.RowVersion)).ToList();
        var confirmed = (await grid.ReadAsync<(int PaymentOrderId, long BankStatementLineId, decimal Amount, DateTime BookingDate)>().ConfigureAwait(false))
            .Select(c => new PaymentOrderConfirmed(c.PaymentOrderId, c.BankStatementLineId, c.Amount, DateOnly.FromDateTime(c.BookingDate))).ToList();
        var rejected = (await grid.ReadAsync<(int PaymentOrderId, long BankStatementLineId)>().ConfigureAwait(false)).ToList();
        return (saved, confirmed, rejected);
    }

    public Task<byte[]> UnvalidateAsync(int companyId, long lineId, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.ExecuteScalarAsync<byte[]>(Command("fin.usp_BankStatementLine_Unvalidate",
            new { CompanyId = companyId, BankStatementLineId = lineId, RowVersion = rowVersion, UserId = userId }, ct))!;

    private static SqlMapper.ICustomQueryParameter Versions(IEnumerable<BankLineVersion> lines)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(long));
        table.Columns.Add("RowVersion", typeof(byte[]));
        foreach (var l in lines)
        {
            table.Rows.Add(l.Id, (object?)l.RowVersion ?? DBNull.Value);
        }

        return table.AsTableValuedParameter("fin.BankLineVersionList");
    }

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 120);

    private sealed record LineRow(long Id, int BankStatementId, string? StatementNo, int LineNumber, DateTime BookingDate, DateTime? ValueDate, decimal Amount,
        string? CounterpartyName, string? CounterpartyIban, string? RemittanceInfo, string? EndToEndId, string BankReference, byte MatchStatus, int? PartnerId,
        string? PartnerName, int TreasuryAccountId, byte AccountKind, string CurrencyCode, DateTime? ValidatedAt, byte[] RowVersion);
}
