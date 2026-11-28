using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Domain.Costing;
using Microsoft.Data.SqlClient;

namespace Erp.Inventory.Infrastructure.Movements;

/// <summary>
/// Registrul de miscari (NET-06): starea de cost (inv.usp_StockCost_Load), scrierea miscarilor costate de motor cu straturile, consumurile si
/// regularizarile (inv.usp_StockMovement_Post, TVP-uri SP-07), miscarile de stornat, stocul la o data. Portul e apelat si din afara pipeline-ului
/// comenzilor (consumatori, alte module), deci codurile THROW ale Stocurilor se traduc aici (NET-09).
/// </summary>
public sealed class StockMovementStore(IDbSession session) : IStockMovementStore
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

    public async Task<BackdatingSettings> GetBackdatingAsync(int companyId, CancellationToken ct) =>
        await session.Connection.QuerySingleOrDefaultAsync<BackdatingSettings>(new CommandDefinition("""
            SELECT BackdatingDays, BackdatingWithRight FROM inv.StockSettings WHERE CompanyId = @CompanyId;
            """, new { CompanyId = companyId }, session.Transaction, cancellationToken: ct)).ConfigureAwait(false)
        ?? new BackdatingSettings(5, true);

    public async Task<CostLoad> LoadCostAsync(int companyId, IReadOnlyCollection<CostKey> keys, IReadOnlyCollection<long> reversalOf, CancellationToken ct)
    {
        var keyTable = Table(("WarehouseId", typeof(int)), ("DepotId", typeof(int)), ("ItemVariantId", typeof(int)), ("LotId", typeof(int)));
        foreach (var k in keys)
        {
            keyTable.Rows.Add(k.WarehouseId, (object?)k.DepotId ?? DBNull.Value, k.ItemVariantId, (object?)k.LotId ?? DBNull.Value);
        }

        var ids = Table(("Id", typeof(long)));
        foreach (var id in reversalOf.Distinct())
        {
            ids.Rows.Add(id);
        }

        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("inv.usp_StockCost_Load", new
        {
            CompanyId = companyId, Keys = keyTable.AsTableValuedParameter("inv.tt_CostKey"), ReversalOf = ids.AsTableValuedParameter("inv.BigIdList"),
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var keyRows = (await grid.ReadAsync<KeyRow>().ConfigureAwait(false)).ToList();
        var layerRows = (await grid.ReadAsync<LayerRow>().ConfigureAwait(false)).ToList();
        var amounts = (await grid.ReadAsync<(long MovementId, decimal CostAmount)>().ConfigureAwait(false)).ToDictionary(r => r.MovementId, r => r.CostAmount);
        var consumptions = (await grid.ReadAsync<(long OutboundMovementId, long CostLayerId, decimal Qty, decimal CostAmount)>().ConfigureAwait(false)).ToList();

        static CostKey Key(int warehouse, int depotKey, int variant, int lotKey) =>
            new(warehouse, depotKey == 0 ? null : depotKey, variant, lotKey == 0 ? null : lotKey);
        var layers = layerRows.ToDictionary(l => l.Id, l => (Key: Key(l.WarehouseId, l.DepotKey, l.ItemVariantId, l.LotKey), Layer: new CostLayerState
        {
            Id = l.Id, LayerDate = DateOnly.FromDateTime(l.LayerDate), Seq = l.PostingSeq, QtyIn = l.QtyIn, QtyRemaining = l.QtyRemaining,
            ValueRemaining = l.ValueRemaining, UnitCost = l.UnitCost, IsNegative = l.IsNegative, MovementId = l.MovementId, OriginLayerId = l.OriginLayerId,
        }));
        var states = keyRows.ToDictionary(k => Key(k.WarehouseId, k.DepotKey, k.ItemVariantId, k.LotKey), k => new CostKeyState
        {
            Method = (CostMethod)k.Method, Qty = k.QtyOnHand, Value = k.CostOnHand, LastUnitCost = k.LastUnitCost, FallbackUnitCost = k.FallbackUnitCost,
        });
        foreach (var (key, layer) in layers.Values)
        {
            if (states.TryGetValue(key, out var state) && layer.QtyRemaining != 0)
            {
                state.Layers.Add(layer);
            }
        }

        return new CostLoad(states, layers.ToDictionary(l => l.Key, l => l.Value.Layer), amounts,
            consumptions.Select(c => (c.OutboundMovementId, c.CostLayerId, c.Qty, c.CostAmount)).ToList());
    }

    public async Task<StockPostingOutcome> PostAsync(StockPostingRequest request, CostedPosting costed, int? employeeId, int userId, CancellationToken ct)
    {
        var lines = Table(("RowNo", typeof(int)), ("SourceLineId", typeof(long)), ("SourceRole", typeof(byte)), ("MovementTypeCode", typeof(string)),
            ("DepotId", typeof(int)), ("WarehouseId", typeof(int)), ("ItemVariantId", typeof(int)), ("QtyBase", typeof(decimal)), ("UnitCost", typeof(decimal)),
            ("CostAmount", typeof(decimal)), ("LotId", typeof(int)), ("SerialId", typeof(int)), ("CostCenterId", typeof(int)), ("ProjectId", typeof(int)),
            ("ReasonId", typeof(int)), ("CounterRowNo", typeof(int)), ("ReversalOfId", typeof(long)));
        foreach (var c in costed.Lines)
        {
            var l = c.Line.Request;
            lines.Rows.Add(c.Line.Row, l.SourceLineId, l.SourceRole, l.MovementTypeCode, Db(l.DepotId), l.WarehouseId, l.ItemVariantId, l.QtyBase, c.UnitCost,
                c.CostAmount, Db(l.LotId), Db(l.SerialId), Db(l.CostCenterId), Db(l.ProjectId), Db(l.ReasonId), Db(c.Line.CounterRow), Db(c.Line.ReversalOfId));
        }

        var keys = Table(("WarehouseId", typeof(int)), ("DepotId", typeof(int)), ("ItemVariantId", typeof(int)), ("LotId", typeof(int)), ("Qty", typeof(decimal)),
            ("Value", typeof(decimal)), ("LastUnitCost", typeof(decimal)));
        foreach (var (k, state) in costed.Keys)
        {
            keys.Rows.Add(k.WarehouseId, Db(k.DepotId), k.ItemVariantId, Db(k.LotId), state.Qty, state.Value, Db(state.LastUnitCost));
        }

        var layers = LayerTable(costed.Layers);
        var consumptions = Table(("RowNo", typeof(int)), ("MovementId", typeof(long)), ("LayerRef", typeof(int)), ("LayerId", typeof(long)), ("Qty", typeof(decimal)),
            ("CostAmount", typeof(decimal)));
        foreach (var c in costed.Consumptions)
        {
            consumptions.Rows.Add(Db(c.Row), Db(c.MovementId), Db(c.LayerRef), Db(c.LayerId), c.Qty, c.Value);
        }

        var adjustments = AdjustmentTable(costed.Adjustments);
        var rows = await Translated(() => session.Connection.QueryAsync<Row>(new CommandDefinition("inv.usp_StockMovement_Post", new
        {
            request.CompanyId, request.SourceModule, request.SourceDocumentType, request.SourceDocumentId, request.SourceDocumentNumber,
            MovementDate = request.MovementDate.ToDateTime(TimeOnly.MinValue), AccountingDate = request.AccountingDate.ToDateTime(TimeOnly.MinValue),
            request.PartnerId, EmployeeId = employeeId, request.IsExternalFact, Lines = lines.AsTableValuedParameter("inv.tt_StockMovementCosted"),
            KeyStates = keys.AsTableValuedParameter("inv.tt_CostKeyState"), Layers = layers.AsTableValuedParameter("inv.tt_CostLayerWrite"),
            Consumptions = consumptions.AsTableValuedParameter("inv.tt_CostConsumptionWrite"),
            Adjustments = adjustments.AsTableValuedParameter("inv.tt_CostAdjustmentWrite"), UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct))).ConfigureAwait(false);
        return Outcome(rows.ToList());
    }

    public async Task<IReadOnlyList<(long MovementId, StockMovementRequest Request)>> ForReversalAsync(StockReversalRequest request, CancellationToken ct)
    {
        var ids = Table(("Id", typeof(long)));
        foreach (var id in request.SourceLineIds ?? [])
        {
            ids.Rows.Add(id);
        }

        var rows = await Translated(() => session.Connection.QueryAsync<ReversalRow>(new CommandDefinition("inv.usp_StockMovement_ForReversal", new
        {
            request.CompanyId, request.SourceModule, request.SourceDocumentId, SourceLineIds = ids.AsTableValuedParameter("inv.BigIdList"),
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct))).ConfigureAwait(false);
        return rows.Select(r => (r.Id, new StockMovementRequest(r.SourceLineId, r.SourceRole, r.MovementTypeCode, r.DepotId, r.WarehouseId, r.ItemVariantId,
            -r.QtyBase, null, r.LotId, null, null, r.SerialId, r.CostCenterId, r.ProjectId, r.ReasonId))).ToList();
    }

    public async Task<IReadOnlyList<StockAvailabilityDto>> GetAvailabilityAsync(int companyId, int? depotId, int? warehouseId,
        IReadOnlyCollection<int> itemVariantIds, DateOnly onDate, CancellationToken ct)
    {
        var ids = Table(("Id", typeof(int)));
        foreach (var id in itemVariantIds.Distinct())
        {
            ids.Rows.Add(id);
        }

        return (await session.Connection.QueryAsync<StockAvailabilityDto>(new CommandDefinition("inv.usp_Stock_Availability", new
        {
            CompanyId = companyId, DepotId = depotId, WarehouseId = warehouseId, Variants = ids.AsTableValuedParameter("inv.IdList"),
            OnDate = onDate.ToDateTime(TimeOnly.MinValue),
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false)).ToList();
    }

    internal static DataTable LayerTable(IEnumerable<LayerWrite> writes)
    {
        var table = Table(("LayerRef", typeof(int)), ("LayerId", typeof(long)), ("RowNo", typeof(int)), ("MovementId", typeof(long)), ("WarehouseId", typeof(int)),
            ("DepotId", typeof(int)), ("ItemVariantId", typeof(int)), ("LotId", typeof(int)), ("LayerDate", typeof(DateTime)), ("SeqOverride", typeof(long)),
            ("QtyIn", typeof(decimal)), ("QtyRemaining", typeof(decimal)), ("ValueRemaining", typeof(decimal)), ("UnitCost", typeof(decimal)),
            ("IsNegative", typeof(bool)), ("OriginLayerId", typeof(long)));
        foreach (var l in writes)
        {
            table.Rows.Add(Db(l.LayerRef), Db(l.LayerId), Db(l.Row), Db(l.MovementId), l.Key.WarehouseId, Db(l.Key.DepotId), l.Key.ItemVariantId, Db(l.Key.LotId),
                l.LayerDate.ToDateTime(TimeOnly.MinValue), Db(l.SeqOverride), l.QtyIn, l.QtyRemaining, l.ValueRemaining, l.UnitCost, l.IsNegative,
                Db(l.OriginLayerId));
        }

        return table;
    }

    internal static DataTable AdjustmentTable(IEnumerable<AdjustmentWrite> writes)
    {
        var table = Table(("RowNo", typeof(int)), ("MovementId", typeof(long)), ("WarehouseId", typeof(int)), ("DepotId", typeof(int)), ("ItemVariantId", typeof(int)),
            ("LotId", typeof(int)), ("Kind", typeof(byte)), ("InStock", typeof(bool)), ("Amount", typeof(decimal)));
        foreach (var a in writes)
        {
            table.Rows.Add(Db(a.Row), Db(a.MovementId), a.Key.WarehouseId, Db(a.Key.DepotId), a.Key.ItemVariantId, Db(a.Key.LotId), a.Kind, a.InStock, a.Amount);
        }

        return table;
    }

    internal static object Db<T>(T? value) where T : struct => value is { } v ? v : DBNull.Value;

    internal static DataTable Table(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns)
        {
            table.Columns.Add(name, type);
        }

        return table;
    }

    private static StockPostingOutcome Outcome(IReadOnlyList<Row> rows) =>
        new(rows.Select(r => new PostedStockMovement(r.MovementId, r.SourceLineId, r.SourceRole, r.WarehouseId, r.DepotId, r.ItemVariantId, r.QtyBase,
            r.UnitCost, r.CostAmount, r.IsForcedNegative, r.MovementTypeCode, r.ReasonCode)).ToList(), rows.Count > 0 && rows.All(r => r.Replayed));

    private sealed record Row(long MovementId, long SourceLineId, byte SourceRole, int WarehouseId, int? DepotId, int ItemVariantId, decimal QtyBase,
        decimal UnitCost, decimal CostAmount, bool IsForcedNegative, string MovementTypeCode, string? ReasonCode, bool Replayed);

    private sealed record KeyRow(int WarehouseId, int DepotKey, int ItemVariantId, int LotKey, decimal QtyOnHand, decimal CostOnHand, decimal? LastUnitCost,
        byte Method, decimal? FallbackUnitCost);

    private sealed record LayerRow(long Id, int WarehouseId, int DepotKey, int ItemVariantId, int LotKey, DateTime LayerDate, long PostingSeq, decimal QtyIn,
        decimal QtyRemaining, decimal ValueRemaining, decimal UnitCost, bool IsNegative, long MovementId, long? OriginLayerId);

    private sealed record ReversalRow(long Id, long SourceLineId, byte SourceRole, string MovementTypeCode, int? DepotId, int WarehouseId, int ItemVariantId,
        decimal QtyBase, int? LotId, int? SerialId, int? CostCenterId, int? ProjectId, int? ReasonId, decimal CostAmount, long? CounterMovementId);
}

/// <summary>Registrul de miscari pentru ecran (inv.usp_StockMovement_Search); cantitatile si valorile ca text.</summary>
public sealed class StockMovementQueries(IDbSession session) : IStockMovementQueries
{
    public async Task<IReadOnlyList<(Erp.Inventory.Contracts.StockMovementDto Movement, int WarehouseDimension)>> SearchAsync(int companyId, int? warehouseId,
        int? itemVariantId, DateOnly? from, DateOnly? to, long? afterSeq, int top, CancellationToken ct) =>
        (await session.Connection.QueryAsync<Row>(new CommandDefinition("inv.usp_StockMovement_Search", new
        {
            CompanyId = companyId, WarehouseId = warehouseId, ItemVariantId = itemVariantId, DateFrom = from?.ToDateTime(TimeOnly.MinValue),
            DateTo = to?.ToDateTime(TimeOnly.MinValue), AfterSeq = afterSeq, Top = top,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false))
        .Select(r => (new Erp.Inventory.Contracts.StockMovementDto(r.Id, r.PostingSeq, DateOnly.FromDateTime(r.MovementDate), r.WarehouseId, r.WarehouseCode,
            r.DepotId, r.ItemVariantId, r.VariantCode, r.MovementTypeCode, Erp.Inventory.Domain.Quantity.Format(r.QtyBase),
            Erp.Inventory.Domain.Quantity.Format(r.UnitCost), r.CostAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Erp.Inventory.Domain.Quantity.Format(r.BalanceQty), r.SourceModule, r.SourceDocumentType, r.SourceDocumentId, r.SourceDocumentNumber, r.ReasonCode,
            r.IsForcedNegative, r.ReversalOfId), r.WarehouseDimensionId))
        .ToList();

    private sealed record Row(long Id, long PostingSeq, DateTime MovementDate, int WarehouseId, string WarehouseCode, int WarehouseDimensionId, int? DepotId,
        int ItemVariantId, string VariantCode, string MovementTypeCode, decimal QtyBase, decimal UnitCost, decimal CostAmount, decimal? BalanceQty,
        string SourceModule, string SourceDocumentType, long SourceDocumentId, string? SourceDocumentNumber, string? ReasonCode, bool IsForcedNegative,
        long? ReversalOfId);
}

/// <summary>Cautarea variantelor stocabile prin inv.usp_StockVariant_Search.</summary>
public sealed class StockVariantSearch(IDbSession session) : IStockVariantSearch
{
    public async Task<IReadOnlyList<StockVariantOptionDto>> SearchAsync(int companyId, string? search, int? warehouseId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<Row>(new CommandDefinition("inv.usp_StockVariant_Search",
            new { CompanyId = companyId, Search = search, WarehouseId = warehouseId }, session.Transaction, commandType: CommandType.StoredProcedure,
            cancellationToken: ct)).ConfigureAwait(false))
        .Select(r => new StockVariantOptionDto(r.Id, r.Code, r.Name, r.ItemId, r.BaseUomCode, Erp.Inventory.Domain.Quantity.Format(r.OnHand)))
        .ToList();

    private sealed record Row(int Id, string Code, string Name, int ItemId, string BaseUomCode, decimal? OnHand);
}
