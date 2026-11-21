using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Partners;

/// <summary>Blocarile partenerilor si articolelor si limita de credit (core.usp_PartnerBlock_*, usp_ItemBlock_*, NET-06).</summary>
public sealed class MasterDataBlockStore(IDbSession session) : IMasterDataBlockStore
{
    public async Task<PartnerBlockDto?> GetPartnerAsync(int partnerId, int companyId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<PartnerRow>(Command("core.usp_PartnerBlock_Get",
            new { PartnerId = partnerId, CompanyId = companyId }, ct)).ConfigureAwait(false);
        return row is null ? null : new PartnerBlockDto(row.BlockedFor, row.Reason, Utc(row.BlockedAt), row.BlockedByName, row.BlockRowVersion,
            row.CreditLimit?.ToString("0.00", CultureInfo.InvariantCulture), row.OverLimitAction, row.CreditRowVersion);
    }

    public async Task<ItemBlockDto?> GetItemAsync(int itemId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<ItemRow>(Command("core.usp_ItemBlock_Get", new { ItemId = itemId }, ct))
            .ConfigureAwait(false);
        return row is null ? null : new ItemBlockDto(row.BlockedFor, row.Reason, Utc(row.BlockedAt), row.BlockedByName, row.RowVersion);
    }

    public Task<SavedEntity> SavePartnerBlockAsync(int partnerId, byte blockedFor, string? reason, byte[]? rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_PartnerBlock_Save", new
        {
            PartnerId = partnerId, BlockedFor = blockedFor, Reason = reason, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public Task<SavedEntity> SaveItemBlockAsync(int itemId, byte blockedFor, string? reason, byte[]? rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_ItemBlock_Save", new
        {
            ItemId = itemId, BlockedFor = blockedFor, Reason = reason, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public Task<SavedEntity> SaveCreditAsync(int partnerId, int companyId, decimal? creditLimit, byte overLimitAction, byte[]? rowVersion, int userId,
        CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_PartnerCredit_Save", new
        {
            PartnerId = partnerId, CompanyId = companyId, CreditLimit = creditLimit, OverLimitAction = overLimitAction, RowVersion = rowVersion,
            UserId = userId,
        }, ct));

    public Task<BlockState?> GetStateAsync(int companyId, int? partnerId, int? itemId, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<BlockState>(Command("core.usp_MasterDataBlock_Check", new
        {
            CompanyId = companyId, PartnerId = partnerId, ItemId = itemId,
        }, ct));

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private sealed record PartnerRow(byte BlockedFor, string? Reason, DateTime? BlockedAt, string? BlockedByName, byte[]? BlockRowVersion,
        decimal? CreditLimit, byte OverLimitAction, byte[]? CreditRowVersion);

    private sealed record ItemRow(byte BlockedFor, string? Reason, DateTime? BlockedAt, string? BlockedByName, byte[]? RowVersion);
}
