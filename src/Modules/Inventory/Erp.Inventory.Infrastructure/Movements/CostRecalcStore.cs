using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Domain.Costing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Inventory.Infrastructure.Movements;

/// <summary>Recalcularea costului prin inv.usp_CostRecalc_Enqueue, inv.usp_StockCost_History, _Rebuild, inv.usp_CostRecalcQueue_List (NET-06).</summary>
public sealed class CostRecalcStore(IDbSession session) : ICostRecalcStore
{
    public async Task<IReadOnlyList<QueuedCostKey>> EnqueueAsync(int companyId, IReadOnlyCollection<CostKey> keys, DateOnly fromDate, CancellationToken ct)
    {
        var table = StockMovementStore.Table(("WarehouseId", typeof(int)), ("DepotId", typeof(int)), ("ItemVariantId", typeof(int)), ("LotId", typeof(int)));
        foreach (var k in keys.Distinct())
        {
            table.Rows.Add(k.WarehouseId, StockMovementStore.Db(k.DepotId), k.ItemVariantId, StockMovementStore.Db(k.LotId));
        }

        var date = fromDate == DateOnly.MinValue ? new DateTime(1900, 1, 1) : fromDate.ToDateTime(TimeOnly.MinValue);
        return (await session.Connection.QueryAsync<QueueRow>(new CommandDefinition("inv.usp_CostRecalc_Enqueue", new
        {
            CompanyId = companyId, Keys = table.AsTableValuedParameter("inv.tt_CostKey"), FromDate = date,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false))
        .Select(r => new QueuedCostKey(new CostKey(r.WarehouseId, r.DepotId, r.ItemVariantId, r.LotId), DateOnly.FromDateTime(r.FromDate), r.MovementCount,
            r.CostRecalcScope))
        .ToList();
    }

    public async Task<CostHistory> HistoryAsync(int companyId, CostKey key, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("inv.usp_StockCost_History", new
        {
            CompanyId = companyId, key.WarehouseId, key.DepotId, key.ItemVariantId, key.LotId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var head = await grid.ReadSingleAsync<(byte Method, decimal? FallbackUnitCost)>().ConfigureAwait(false);
        var movements = (await grid.ReadAsync<MovementRow>().ConfigureAwait(false)).Select(m => new CostHistoryMovement(m.Id, DateOnly.FromDateTime(m.MovementDate),
            DateOnly.FromDateTime(m.AccountingDate), m.PostingSeq, m.QtyBase, m.UnitCost, m.CostAmount, m.CounterMovementId, m.ReversalOfId, m.SourceModule,
            m.SourceDocumentType, m.SourceDocumentId)).ToList();
        var amounts = (await grid.ReadAsync<(long MovementId, decimal CostAmount)>().ConfigureAwait(false)).ToDictionary(a => a.MovementId, a => a.CostAmount);
        var parts = (await grid.ReadAsync<PartRow>().ConfigureAwait(false))
            .ToLookup(p => p.OutboundMovementId, p => new CostCounterPart(p.Qty, p.CostAmount, DateOnly.FromDateTime(p.LayerDate), p.PostingSeq, p.LayerId));
        return new CostHistory((CostMethod)head.Method, head.FallbackUnitCost, movements, amounts, parts);
    }

    public async Task<IReadOnlyList<(CostKey Key, DateOnly FromDate)>> RebuildAsync(int companyId, CostKey key,
        IReadOnlyList<(long MovementId, decimal UnitCost, decimal Amount)> updates, IReadOnlyList<LayerWrite> layers, IReadOnlyList<ConsumptionWrite> consumptions,
        IReadOnlyList<AdjustmentWrite> adjustments, CostKeyState state, int userId, CancellationToken ct)
    {
        var updateTable = StockMovementStore.Table(("MovementId", typeof(long)), ("UnitCost", typeof(decimal)), ("CostAmount", typeof(decimal)));
        foreach (var u in updates)
        {
            updateTable.Rows.Add(u.MovementId, u.UnitCost, u.Amount);
        }

        var consumptionTable = StockMovementStore.Table(("RowNo", typeof(int)), ("MovementId", typeof(long)), ("LayerRef", typeof(int)), ("LayerId", typeof(long)),
            ("Qty", typeof(decimal)), ("CostAmount", typeof(decimal)));
        foreach (var c in consumptions)
        {
            consumptionTable.Rows.Add(DBNull.Value, StockMovementStore.Db(c.MovementId), StockMovementStore.Db(c.LayerRef), DBNull.Value, c.Qty, c.Value);
        }

        return (await session.Connection.QueryAsync<FollowerRow>(new CommandDefinition("inv.usp_StockCost_Rebuild", new
        {
            CompanyId = companyId, key.WarehouseId, key.DepotId, key.ItemVariantId, key.LotId,
            Updates = updateTable.AsTableValuedParameter("inv.tt_MovementCostUpdate"),
            Layers = StockMovementStore.LayerTable(layers).AsTableValuedParameter("inv.tt_CostLayerWrite"),
            Consumptions = consumptionTable.AsTableValuedParameter("inv.tt_CostConsumptionWrite"),
            Adjustments = StockMovementStore.AdjustmentTable(adjustments).AsTableValuedParameter("inv.tt_CostAdjustmentWrite"),
            state.Qty, state.Value, state.LastUnitCost, UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false))
        .Select(f => (new CostKey(f.WarehouseId, f.DepotId, f.ItemVariantId, f.LotId), DateOnly.FromDateTime(f.FromDate)))
        .ToList();
    }

    public async Task<IReadOnlyList<CostRecalcQueueItemDto>> QueueAsync(int? companyId, int top, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(new CommandDefinition("inv.usp_CostRecalcQueue_List", new { CompanyId = companyId, Top = top },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false))
        .Select(r => new CostRecalcQueueItemDto(r.CompanyId, r.WarehouseId, r.WarehouseCode, r.DepotId, r.ItemVariantId, r.VariantCode, r.LotId,
            DateOnly.FromDateTime(r.FromDate), r.EnqueuedAt))
        .ToList();

    private sealed record QueueRow(int WarehouseId, int? DepotId, int ItemVariantId, int? LotId, DateTime FromDate, int MovementCount, byte CostRecalcScope);

    private sealed record MovementRow(long Id, DateTime MovementDate, DateTime AccountingDate, long PostingSeq, decimal QtyBase, decimal UnitCost, decimal CostAmount,
        long? CounterMovementId, long? ReversalOfId, string SourceModule, string SourceDocumentType, long SourceDocumentId);

    private sealed record PartRow(long OutboundMovementId, decimal Qty, decimal CostAmount, DateTime LayerDate, long PostingSeq, long LayerId);

    private sealed record FollowerRow(int WarehouseId, int? DepotId, int ItemVariantId, int? LotId, DateTime FromDate);

    private sealed record ItemRow(int CompanyId, int WarehouseId, string WarehouseCode, int? DepotId, int ItemVariantId, string VariantCode, int? LotId,
        DateTime FromDate, DateTime EnqueuedAt);
}

/// <summary>
/// Jobul de recalculare (02.05, S3 3.5): la 15 minute preia cheile din coada si reface fiecare in tranzactia proprie, prin comanda RecalculateCost,
/// cu contextul de sistem al firmei.
/// </summary>
public sealed class CostRecalcJob(IServiceScopeFactory scopes, ILogger<CostRecalcJob> log)
{
    public const string JobId = "inv-cost-recalc";

    public const string Schedule = "*/15 * * * *";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        IReadOnlyList<CostRecalcQueueItemDto> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<ICostRecalcStore>().QueueAsync(null, 500, ct).ConfigureAwait(false);
        }

        var done = 0;
        foreach (var key in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ISystemContextScope>().Enter(key.CompanyId);
            await scope.ServiceProvider.GetRequiredService<MediatR.ISender>()
                .Send(new RecalculateCost(key.WarehouseId, key.DepotId, key.ItemVariantId, key.LotId), ct).ConfigureAwait(false);
            done++;
        }

        if (done > 0)
        {
            log.LogInformation("Recalcularea costului: {Count} chei refăcute din coadă.", done);
        }

        return done;
    }
}
