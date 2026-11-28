using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Lots;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Inventory.Domain.Lots;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Inventory.Infrastructure.Lots;

/// <summary>Loturile prin inv.usp_Lot_*, inv.usp_PartnerShelfLifeRule_Save (NET-06); apelat si din portul de stoc, deci codurile THROW se traduc aici.</summary>
public sealed class LotStore(IDbSession session) : ILotStore
{
    private static readonly InventorySqlErrors Errors = new();

    private static async Task<T> Translated<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (SqlException e) when (Errors.Errors.TryGetValue(e.Number, out var error))
        {
            throw new ErpException(error.Kind, error.Code, e.Message, e);
        }
    }

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateTime? Date(DateOnly? date) => date?.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? Date(DateTime? date) => date is { } d ? DateOnly.FromDateTime(d) : null;

    public async Task<IReadOnlyList<LotItemDto>> ListAsync(int companyId, int? itemVariantId, int? warehouseId, string? search, int? expiringWithinDays,
        DateOnly today, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(Command("inv.usp_Lot_List", new
        {
            CompanyId = companyId, ItemVariantId = itemVariantId, WarehouseId = warehouseId, Search = search, ExpiringWithinDays = expiringWithinDays,
            Today = today.ToDateTime(TimeOnly.MinValue),
        }, ct)).ConfigureAwait(false))
        .Select(r => new LotItemDto(r.LotId, r.ItemVariantId, r.VariantCode, r.VariantName, r.LotCode, r.CkuCode, r.SupplierLotCode, Date(r.ExpiryDate), r.ExpiryKind,
            r.DaysLeft, (LotStatus)r.Status, r.IsTechnical, r.WarehouseId, r.WarehouseCode, Quantity.Format(r.QtyOnHand)) { WarehouseDimensionId = r.WarehouseDimensionId })
        .ToList();

    public async Task<LotDto?> GetAsync(int companyId, LotLookup lookup, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_Lot_Get", new
        {
            CompanyId = companyId, lookup.LotId, lookup.CkuCode, lookup.CkuUid, lookup.ItemVariantId, lookup.LotCode, ExpiryDate = Date(lookup.ExpiryDate),
        }, ct)).ConfigureAwait(false);
        var head = await grid.ReadSingleOrDefaultAsync<HeadRow>().ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }

        var history = (await grid.ReadAsync<HistoryRow>().ConfigureAwait(false))
            .Select(h => new LotHistoryDto((LotStatus?)h.FromStatus, (LotStatus)h.ToStatus, h.Reason, h.SourceDocumentType, h.SourceDocumentId, h.ChangedAt,
                h.ChangedByName)).ToList();
        var stock = (await grid.ReadAsync<(int WarehouseId, string WarehouseCode, decimal QtyOnHand)>().ConfigureAwait(false))
            .Select(s => new LotStockDto(s.WarehouseId, s.WarehouseCode, Quantity.Format(s.QtyOnHand))).ToList();
        return new LotDto(head.LotId, head.ItemVariantId, head.VariantCode, head.VariantName, head.LotCode, head.CkuCode, head.CkuUid, head.SupplierLotCode,
            head.SupplierPartnerId, head.SupplierName, Date(head.ProductionDate), Date(head.ExpiryDate), head.ExpiryKind, head.CountryOfOriginCode,
            (LotStatus)head.Status, head.StatusReason, head.IsTechnical, Date(head.FirstReceiptDate), history, stock, head.RowVersion);
    }

    public Task<LotStatusChange> ChangeStatusAsync(int companyId, int lotId, LotStatus to, string? reason, byte[]? rowVersion, string? documentType,
        long? documentId, bool isSystem, int userId, CancellationToken ct) =>
        Translated(async () =>
        {
            var r = await session.Connection.QuerySingleAsync<ChangeRow>(Command("inv.usp_Lot_ChangeStatus", new
            {
                CompanyId = companyId, LotId = lotId, ToStatus = (byte)to, Reason = reason, RowVersion = rowVersion, SourceDocumentType = documentType,
                SourceDocumentId = documentId, IsSystem = isSystem, UserId = userId,
            }, ct)).ConfigureAwait(false);
            return new LotStatusChange(r.LotId, r.ItemVariantId, r.LotCode, (LotStatus)r.FromStatus, (LotStatus)r.ToStatus, r.SupplierPartnerId, r.RowVersion);
        });

    public Task<InventorySaved> UpdateAsync(int companyId, int lotId, string? supplierLotCode, string? countryCode, DateOnly? productionDate, DateOnly? expiryDate,
        string reason, byte[] rowVersion, int userId, CancellationToken ct) =>
        Translated(() => session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_Lot_Update", new
        {
            CompanyId = companyId, LotId = lotId, SupplierLotCode = supplierLotCode, CountryOfOriginCode = countryCode, ProductionDate = Date(productionDate),
            ExpiryDate = Date(expiryDate), Reason = reason, RowVersion = rowVersion, UserId = userId,
        }, ct)));

    public async Task<LotExpiryScan> ExpiryScanAsync(int companyId, DateOnly today, int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_Lot_ExpiryScan",
            new { CompanyId = companyId, Today = today.ToDateTime(TimeOnly.MinValue), UserId = userId }, ct)).ConfigureAwait(false);
        var expired = (await grid.ReadAsync<(int LotId, int ItemVariantId, string LotCode, byte FromStatus, int? SupplierPartnerId)>().ConfigureAwait(false))
            .Select(e => (e.LotId, e.ItemVariantId, e.LotCode, (LotStatus)e.FromStatus, e.SupplierPartnerId)).ToList();
        var warnings = (await grid.ReadAsync<(int WarehouseId, int LotId, int ItemVariantId, DateTime ExpiryDate, decimal QtyOnHand)>().ConfigureAwait(false))
            .Select(w => (w.WarehouseId, new LotExpiryLine(w.LotId, w.ItemVariantId, DateOnly.FromDateTime(w.ExpiryDate), w.QtyOnHand))).ToList();
        return new LotExpiryScan(expired, warnings);
    }

    public async Task<(IReadOnlyList<LotCandidate> Lots, int MinRemainingDays, byte PickRule)> ProposeAsync(int companyId, int warehouseId, int? depotId,
        int itemVariantId, int? partnerId, DateOnly onDate, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_Lot_Propose", new
        {
            CompanyId = companyId, WarehouseId = warehouseId, DepotId = depotId, ItemVariantId = itemVariantId, PartnerId = partnerId,
            OnDate = onDate.ToDateTime(TimeOnly.MinValue),
        }, ct)).ConfigureAwait(false);
        var lots = (await grid.ReadAsync<CandidateRow>().ConfigureAwait(false))
            .Select(c => new LotCandidate(c.WarehouseId, c.DepotId, c.ItemVariantId, c.LotId, c.LotCode, c.Status, Date(c.ExpiryDate), c.ExpiryKind,
                Date(c.FirstReceiptDate), c.IsTechnical, c.Available) { CkuCode = c.CkuCode })
            .ToList();
        var settings = await grid.ReadSingleOrDefaultAsync<(int MinRemainingDays, byte PickRule)>().ConfigureAwait(false);
        return (lots, settings.MinRemainingDays, settings.PickRule == 0 ? (byte)1 : settings.PickRule);
    }

    public Task<IReadOnlyList<PartnerShelfLifeRuleDto>> SaveShelfLifeRuleAsync(int companyId, int partnerId, int? itemId, int? itemGroupId, int? minRemainingDays,
        int userId, CancellationToken ct) =>
        Translated<IReadOnlyList<PartnerShelfLifeRuleDto>>(async () => (await session.Connection.QueryAsync<PartnerShelfLifeRuleDto>(Command(
            "inv.usp_PartnerShelfLifeRule_Save", new
            {
                CompanyId = companyId, PartnerId = partnerId, ItemId = itemId, ItemGroupId = itemGroupId, MinRemainingDays = minRemainingDays, UserId = userId,
            }, ct)).ConfigureAwait(false)).ToList());

    private sealed record ItemRow(int LotId, int ItemVariantId, string VariantCode, string VariantName, string LotCode, string CkuCode, string? SupplierLotCode,
        DateTime? ExpiryDate, byte? ExpiryKind, int? DaysLeft, byte Status, bool IsTechnical, int WarehouseId, string WarehouseCode, int WarehouseDimensionId,
        decimal QtyOnHand);

    private sealed record HeadRow(int LotId, int ItemVariantId, string VariantCode, string VariantName, string LotCode, string CkuCode, Guid CkuUid,
        string? SupplierLotCode, int? SupplierPartnerId, string? SupplierName, DateTime? ProductionDate, DateTime? ExpiryDate, byte? ExpiryKind,
        string? CountryOfOriginCode, byte Status, string? StatusReason, bool IsTechnical, DateTime? FirstReceiptDate, byte[] RowVersion);

    private sealed record HistoryRow(byte? FromStatus, byte ToStatus, string? Reason, string? SourceDocumentType, long? SourceDocumentId, DateTime ChangedAt,
        string? ChangedByName);

    private sealed record ChangeRow(int LotId, int ItemVariantId, string LotCode, byte FromStatus, byte ToStatus, int? SupplierPartnerId, byte[] RowVersion);

    private sealed record CandidateRow(int WarehouseId, int? DepotId, int ItemVariantId, int LotId, string CkuCode, string LotCode, byte Status,
        DateTime? ExpiryDate, byte? ExpiryKind, DateTime? FirstReceiptDate, bool IsTechnical, decimal Available);
}

/// <summary>
/// Jobul zilnic de expirare (02.05, S4 3.2; 00:05 ora Romaniei): pentru fiecare firma initializata in Stocuri, comanda ScanLotExpiry in
/// contextul de sistem, in tranzactia proprie.
/// </summary>
public sealed class LotExpiryJob(IServiceScopeFactory scopes, TimeProvider time, ILogger<LotExpiryJob> log)
{
    public const string JobId = "inv-lot-expiry";

    public const string Schedule = "5 0 * * *";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        IReadOnlyList<int> companies;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<IDbSession>();
            companies = (await session.Connection.QueryAsync<int>(new CommandDefinition("SELECT CompanyId FROM inv.StockSettings", transaction: session.Transaction,
                cancellationToken: ct)).ConfigureAwait(false)).ToList();
        }

        var expired = 0;
        foreach (var company in companies)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ISystemContextScope>().Enter(company);
            expired += await scope.ServiceProvider.GetRequiredService<MediatR.ISender>().Send(new ScanLotExpiry(BusinessDay.Today(time)), ct).ConfigureAwait(false);
        }

        log.LogInformation("Expirarea loturilor: {Count} loturi trecute în Expirat.", expired);
        return expired;
    }
}
