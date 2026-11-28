using Erp.Core.Contracts;
using Erp.Inventory.Domain.Costing;

namespace Erp.Inventory.Application.Movements;

/// <summary>Cheia de cost (S3): gestiune, depozit, varianta, lot.</summary>
public readonly record struct CostKey(int WarehouseId, int? DepotId, int ItemVariantId, int? LotId);

/// <summary>Starea de cost citita pentru un apel (inv.usp_StockCost_Load): cheile blocate, straturile si datele stornarilor.</summary>
public sealed record CostLoad(IReadOnlyDictionary<CostKey, CostKeyState> Keys, IReadOnlyDictionary<long, CostLayerState> Layers,
    IReadOnlyDictionary<long, decimal> ReversalAmounts, IReadOnlyList<(long OutboundMovementId, long LayerId, decimal Qty, decimal Value)> ReversalConsumptions);

/// <summary>O linie de inregistrat: cererea, randul din apel si, la stornare, miscarea stornata.</summary>
public sealed record PostingLine(int Row, StockMovementRequest Request, long? ReversalOfId, int? CounterRow);

/// <summary>O linie costata, gata de scris (inv.tt_StockMovementCosted).</summary>
public sealed record CostedLine(PostingLine Line, decimal UnitCost, decimal CostAmount);

public sealed record LayerWrite(int? LayerRef, long? LayerId, int? Row, long? MovementId, CostKey Key, DateOnly LayerDate, long? SeqOverride, decimal QtyIn,
    decimal QtyRemaining, decimal ValueRemaining, decimal UnitCost, bool IsNegative, long? OriginLayerId);

public sealed record ConsumptionWrite(int? Row, long? MovementId, int? LayerRef, long? LayerId, decimal Qty, decimal Value);

public sealed record AdjustmentWrite(int? Row, long? MovementId, CostKey Key, byte Kind, bool InStock, decimal Amount);

/// <summary>Rezultatul calculului pentru un apel: liniile, starea finala a cheilor si scrierile de straturi, consumuri si regularizari.</summary>
public sealed record CostedPosting(IReadOnlyList<CostedLine> Lines, IReadOnlyDictionary<CostKey, CostKeyState> Keys, IReadOnlyList<LayerWrite> Layers,
    IReadOnlyList<ConsumptionWrite> Consumptions, IReadOnlyList<AdjustmentWrite> Adjustments);

/// <summary>
/// Aplica motorul de cost (S3 3.1–3.4) pe liniile unui apel, in ordinea lor; intrarile transferurilor dupa iesirile pereche, cu straturile
/// iesirii (S3 regula 4); stornarile cu consumurile si stratul miscarii stornate.
/// </summary>
public static class CostCalculator
{
    public const byte NegativeRegularization = 2;

    public static CostedPosting Calculate(IReadOnlyList<PostingLine> lines, DateOnly movementDate, CostLoad load)
    {
        var results = new Dictionary<int, CostLineResult>();
        var consumptionsByMovement = load.ReversalConsumptions.ToLookup(c => c.OutboundMovementId);
        foreach (var line in lines.OrderBy(l => l.CounterRow is null ? 0 : 1).ThenBy(l => l.Row))
        {
            var r = line.Request;
            var key = load.Keys[new CostKey(r.WarehouseId, r.DepotId, r.ItemVariantId, r.LotId)];
            CostReversal? reversal = null;
            if (line.ReversalOfId is { } original)
            {
                var consumptions = consumptionsByMovement[original].Select(c => (load.Layers[c.LayerId], c.Qty, c.Value)).ToList();
                var inbound = load.Layers.Values.Where(l => l.MovementId == original && !l.IsNegative).OrderBy(l => l.Id).ToList();
                foreach (var layer in inbound.Where(l => !key.Layers.Contains(l)))
                {
                    key.Layers.Add(layer);
                }

                reversal = new CostReversal(load.ReversalAmounts[original], consumptions, r.QtyBase < 0 ? inbound : []);
            }

            CostCounter? counter = null;
            if (line.CounterRow is { } counterRow && results.TryGetValue(counterRow, out var paired))
            {
                counter = new CostCounter(-paired.Amount, paired.Consumptions.Select(c => new CostCounterPart(c.Qty, c.Value, c.Layer.LayerDate, c.Layer.Seq,
                    c.Layer.Id)).ToList());
            }

            results[line.Row] = CostEngine.Apply(key, new CostLine(line.Row, r.QtyBase, movementDate, long.MaxValue / 2 + line.Row, r.InboundUnitCost, counter, reversal));
        }

        var keyOf = lines.ToDictionary(l => l.Row, l => new CostKey(l.Request.WarehouseId, l.Request.DepotId, l.Request.ItemVariantId, l.Request.LotId));
        var layers = new List<LayerWrite>();
        foreach (var (key, state) in load.Keys)
        {
            foreach (var layer in state.Layers.Where(l => l.Changed))
            {
                var transferred = layer.OriginLayerId is not null || (layer.Ref is not null && layer.MovementRow is { } row
                    && lines.First(l => l.Row == row).CounterRow is not null);
                layers.Add(new LayerWrite(layer.Id is null ? layer.Ref : null, layer.Id, layer.MovementRow, layer.MovementId, key, layer.LayerDate,
                    transferred && layer.Seq < long.MaxValue / 2 ? layer.Seq : null, layer.QtyIn, layer.QtyRemaining, layer.ValueRemaining, layer.UnitCost,
                    layer.IsNegative, layer.OriginLayerId));
            }
        }

        var consumptionsOut = results.Values.SelectMany(r => r.Consumptions.Select(c => new ConsumptionWrite(r.Row, null, c.Layer.Id is null ? c.Layer.Ref : null,
            c.Layer.Id, c.Qty, c.Value))).ToList();
        var adjustments = results.Values.SelectMany(r => r.Adjustments.Select(a => new AdjustmentWrite(a.MovementRow, a.MovementRow is null ? a.MovementId : null,
            a.MovementRow is { } row ? keyOf[row] : keyOf[r.Row], NegativeRegularization, true, a.Amount))).ToList();
        return new CostedPosting(lines.Select(l => new CostedLine(l, results[l.Row].UnitCost, results[l.Row].Amount)).ToList(), load.Keys, layers,
            consumptionsOut, adjustments);
    }
}
