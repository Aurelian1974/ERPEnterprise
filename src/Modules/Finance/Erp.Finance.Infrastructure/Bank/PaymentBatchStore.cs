using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank;

/// <summary>Loturile de plati prin fin.usp_PaymentBatch_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class PaymentBatchStore(IDbSession session) : IPaymentBatchStore
{
    public async Task<FinanceSaved> SaveAsync(int companyId, int? id, int treasuryAccountId, IReadOnlyList<int> orderIds, byte[]? rowVersion, int userId,
        CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("RowVersion", typeof(byte[]));
        foreach (var orderId in orderIds)
        {
            table.Rows.Add(orderId, DBNull.Value);
        }

        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_PaymentBatch_Save", new
        {
            Id = id, CompanyId = companyId, TreasuryAccountId = treasuryAccountId, Orders = table.AsTableValuedParameter("fin.IdVersionList"),
            RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public async Task<PaymentBatchData?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PaymentBatch_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        if (grid.IsConsumed)
        {
            return null;
        }

        var h = await grid.ReadSingleAsync<HeaderRow>().ConfigureAwait(false);
        var orders = (await grid.ReadAsync<OrderRow>().ConfigureAwait(false)).Select(o => new PaymentBatchOrder(o.Id, o.Number, (PaymentOrderStatus)o.Status,
            o.ReversalOfId, o.ConfirmationStatus is { } c ? (PaymentConfirmationStatus)c : null, o.ApprovedAt, (PaymentOrderOrigin)o.Origin, o.PartnerId,
            o.BeneficiaryName, o.BeneficiaryIban, o.BeneficiaryBic, o.Amount, o.RemittanceInfo, o.EndToEndId, DateOnly.FromDateTime(o.RequestedExecutionDate),
            o.IsUrgent, o.RowVersion)).ToList();
        return new PaymentBatchData(h.Id, h.TreasuryAccountId, h.AccountCode, h.CurrencyCode.Trim(), h.AccountIban, h.BankBic,
            h.PaymentFormat is { } pf ? (PaymentFileFormat)pf : null,
            h.PaymentMapping, h.MaxRemittanceLength, h.CompanyName, h.CompanyFiscalCode, h.Number, h.Format is { } f ? (PaymentFileFormat)f : null, h.OrderCount,
            h.TotalAmount, (PaymentBatchStatus)h.Status, h.ExportCount, h.ExportedAt, h.ExportedByName, h.FileName, h.FileSha256?.Trim(), h.LastReExportReason,
            h.CancelledAt, h.CancelReason, orders, h.RowVersion);
    }

    public async Task<IReadOnlyList<PaymentBatchItemDto>> ListAsync(int companyId, int? treasuryAccountId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(Command("fin.usp_PaymentBatch_List", new { CompanyId = companyId, TreasuryAccountId = treasuryAccountId }, ct))
            .ConfigureAwait(false)).Select(b => new PaymentBatchItemDto(b.Id, b.TreasuryAccountId, b.AccountCode, b.Number,
            b.Format is { } f ? (PaymentFileFormat)f : null, b.OrderCount, Money.Format(b.TotalAmount), (PaymentBatchStatus)b.Status, b.ExportCount, b.ExportedAt,
            b.RowVersion)).ToList();

    public Task<byte[]> MarkExportedAsync(int companyId, int id, byte[] rowVersion, PaymentFileFormat format, PaymentFile file, string sha256,
        string? reExportReason, bool requireApproval, int userId, CancellationToken ct) =>
        session.Connection.ExecuteScalarAsync<byte[]>(Command("fin.usp_PaymentBatch_MarkExported", new
        {
            CompanyId = companyId, Id = id, RowVersion = rowVersion, Format = (byte)format, file.FileName, FileSha256 = sha256, FileContent = file.Content,
            ReExportReason = reExportReason, RequireApproval = requireApproval, UserId = userId,
        }, ct))!;

    public Task<byte[]> CancelAsync(int companyId, int id, string reason, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.ExecuteScalarAsync<byte[]>(Command("fin.usp_PaymentBatch_Cancel",
            new { CompanyId = companyId, Id = id, Reason = reason, RowVersion = rowVersion, UserId = userId }, ct))!;

    public async Task<(string FileName, string Sha256, byte[] Content)?> GetFileAsync(int companyId, int id, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<FileRow>(Command("fin.usp_PaymentBatch_File", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        return row is null ? null : (row.FileName, row.FileSha256.Trim(), row.FileContent);
    }

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record HeaderRow(int Id, int TreasuryAccountId, string AccountCode, string CurrencyCode, string? AccountIban, string? BankBic,
        byte? PaymentFormat, string? PaymentMapping, short MaxRemittanceLength, string CompanyName, string CompanyFiscalCode, string? Number, byte? Format,
        int OrderCount, decimal TotalAmount, byte Status, int ExportCount, DateTime? ExportedAt, string? ExportedByName, string? FileName, string? FileSha256,
        string? LastReExportReason, DateTime? CancelledAt, string? CancelReason, byte[] RowVersion);

    private sealed record OrderRow(int Id, string? Number, byte Status, int? ReversalOfId, byte? ConfirmationStatus, DateTime? ApprovedAt, byte Origin,
        int? PartnerId, string BeneficiaryName, string BeneficiaryIban, string? BeneficiaryBic, decimal Amount, string? RemittanceInfo, string? EndToEndId,
        DateTime RequestedExecutionDate, bool IsUrgent, byte[] RowVersion);

    private sealed record ItemRow(int Id, int TreasuryAccountId, string AccountCode, string? Number, byte? Format, int OrderCount, decimal TotalAmount,
        byte Status, int ExportCount, DateTime? ExportedAt, byte[] RowVersion);

    private sealed record FileRow(string FileName, string FileSha256, byte[] FileContent);
}
