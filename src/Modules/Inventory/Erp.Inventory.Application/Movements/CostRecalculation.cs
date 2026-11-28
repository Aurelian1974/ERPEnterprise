using Erp.Core.Contracts;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain.Costing;

namespace Erp.Inventory.Application.Movements;

/// <summary>O miscare din istoria unei chei (inv.usp_StockCost_History).</summary>
public sealed record CostHistoryMovement(long Id, DateOnly MovementDate, DateOnly AccountingDate, long PostingSeq, decimal QtyBase, decimal UnitCost,
    decimal CostAmount, long? CounterMovementId, long? ReversalOfId, string SourceModule, string SourceDocumentType, long SourceDocumentId);

/// <summary>Istoria unei chei: metoda, costul de rezerva, miscarile in ordine si, pentru intrarile din transferuri, iesirile pereche.</summary>
public sealed record CostHistory(CostMethod Method, decimal? FallbackUnitCost, IReadOnlyList<CostHistoryMovement> Movements,
    IReadOnlyDictionary<long, decimal> CounterAmounts, ILookup<long, CostCounterPart> CounterParts);

/// <summary>O cheie in coada de recalculare, cu numarul de miscari de refacut si setarea firmei (S3 3.5).</summary>
public sealed record QueuedCostKey(CostKey Key, DateOnly FromDate, int MovementCount, byte CostRecalcScope);

/// <summary>Rezultatul refacerii unei chei: miscarile schimbate (cu documentul lor) si cheile pereche de refacut.</summary>
public sealed record CostRebuild(IReadOnlyList<(CostHistoryMovement Movement, decimal NewAmount)> Changed, IReadOnlyList<(CostKey Key, DateOnly FromDate)> Followers);

/// <summary>O miscare al carei cost s-a schimbat la recalculare, cu cheia ei.</summary>
public sealed record CostChange(CostKey Key, CostHistoryMovement Movement, decimal NewAmount)
{
    public decimal Delta => NewAmount - Movement.CostAmount;
}

/// <summary>
/// Atribuirea unei recalculari unui document (costul adaugat, S3 3.3): suma adaugata pe fiecare intrare tinta, tipul ajustarii si documentul cu
/// data lui. Urmele schimbarilor poarta documentul, iar evenimentul il publica apelantul, nu recalcularea.
/// </summary>
public sealed record CostAttribution(byte AdjustmentKind, string DocumentType, long DocumentId, DateOnly AccountingDate,
    IReadOnlyDictionary<long, decimal> InboundDeltas);

/// <summary>Accesul la date pentru recalculare (inv.usp_CostRecalc_Enqueue, inv.usp_StockCost_History, _Rebuild, inv.usp_CostRecalcQueue_List).</summary>
public interface ICostRecalcStore
{
    Task<IReadOnlyList<QueuedCostKey>> EnqueueAsync(int companyId, IReadOnlyCollection<CostKey> keys, DateOnly fromDate, CancellationToken ct);

    Task<CostHistory> HistoryAsync(int companyId, CostKey key, CancellationToken ct);

    Task<IReadOnlyList<(CostKey Key, DateOnly FromDate)>> RebuildAsync(int companyId, CostKey key, IReadOnlyList<(long MovementId, decimal UnitCost, decimal Amount)> updates,
        IReadOnlyList<LayerWrite> layers, IReadOnlyList<ConsumptionWrite> consumptions, IReadOnlyList<AdjustmentWrite> adjustments, CostKeyState state, int userId,
        CostAttribution? attribution, CancellationToken ct);

    Task<IReadOnlyList<CostRecalcQueueItemDto>> QueueAsync(int? companyId, int top, CancellationToken ct);
}

/// <summary>O cheie din coada de recalculare (ecranul s3-02).</summary>
public sealed record CostRecalcQueueItemDto(int CompanyId, int WarehouseId, string WarehouseCode, int? DepotId, int ItemVariantId, string VariantCode, int? LotId,
    DateOnly FromDate, DateTime EnqueuedAt);

/// <summary>
/// Recalcularea costului (02.05, S3 3.5): refacerea unei chei pe toata istoria ei, cu acelasi motor ca la inregistrare (CostEngine), in ordinea
/// totala (data, PostingSeq). Miscarile al caror cost se schimba primesc costul nou si o ajustare de recalculare; straturile, consumurile si
/// regularizarile se refac; cheile intrarilor pereche (transferurile) se refac apoi la randul lor. Se publica CostStocAjustat pe document sursa.
/// </summary>
public sealed class CostRecalculator(ICostRecalcStore store, IOutbox outbox, ICurrentContext context)
{
    public const byte Recalculation = 1;

    /// <summary>Limita recalcularii in tranzactia miscarii (S3 3.5, decizia 5); peste ea cheia ramane jobului.</summary>
    public const int InlineLimit = 2000;

    /// <summary>Pune in coada cheile atinse de o miscare cu data in trecut si le reface imediat, daca setarea si limita permit.</summary>
    public async Task AfterPostingAsync(int companyId, IReadOnlyCollection<CostKey> keys, DateOnly movementDate, CancellationToken ct)
    {
        var queued = await store.EnqueueAsync(companyId, keys, movementDate, ct).ConfigureAwait(false);
        foreach (var key in queued.Where(q => q.CostRecalcScope == 1 && q.MovementCount <= InlineLimit))
        {
            await RecalculateAsync(companyId, key.Key, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Reface cheia si, in lant, cheile pereche ale transferurilor schimbate (cel mult 50 de pasi, apoi raman in coada).</summary>
    public async Task<int> RecalculateAsync(int companyId, CostKey key, CancellationToken ct) =>
        (await RunAsync(companyId, [key], null, ct).ConfigureAwait(false)).Count;

    /// <summary>
    /// Reface cheile date si lantul transferurilor. Fara atribuire, publica CostStocAjustat (recalculare) pe document sursa; cu atribuire (documentul
    /// CA), intrarile tinta primesc suma adaugata, urmele poarta documentul, iar schimbarile se intorc apelantului, care publica evenimentul.
    /// </summary>
    public async Task<IReadOnlyList<CostChange>> RunAsync(int companyId, IReadOnlyCollection<CostKey> keys, CostAttribution? attribution, CancellationToken ct)
    {
        var pending = new Queue<CostKey>(keys.Distinct());
        var changes = new List<CostChange>();
        for (var step = 0; step < 50 && pending.Count > 0; step++)
        {
            var current = pending.Dequeue();
            var rebuild = await RebuildOneAsync(companyId, current, attribution, ct).ConfigureAwait(false);
            changes.AddRange(rebuild.Changed.Select(c => new CostChange(current, c.Movement, c.NewAmount)));
            foreach (var follower in rebuild.Followers.Where(f => !pending.Contains(f.Key)))
            {
                pending.Enqueue(follower.Key);
            }
        }

        foreach (var left in pending)
        {
            await store.EnqueueAsync(companyId, [left], DateOnly.MinValue, ct).ConfigureAwait(false);
        }

        return changes;
    }

    private async Task<CostRebuild> RebuildOneAsync(int companyId, CostKey key, CostAttribution? attribution, CancellationToken ct)
    {
        var history = await store.HistoryAsync(companyId, key, ct).ConfigureAwait(false);
        var state = new CostKeyState { Method = history.Method, FallbackUnitCost = history.FallbackUnitCost };
        var results = new Dictionary<long, CostLineResult>();
        var rowOf = new Dictionary<int, long>();
        var row = 0;
        foreach (var m in history.Movements)
        {
            row++;
            rowOf[row] = m.Id;
            CostCounter? counter = m.CounterMovementId is { } outId && history.CounterAmounts.TryGetValue(outId, out var outAmount)
                ? new CostCounter(-outAmount, history.CounterParts[outId].ToList())
                : null;
            CostReversal? reversal = null;
            if (m.ReversalOfId is { } original && results.TryGetValue(original, out var originalResult))
            {
                reversal = new CostReversal(originalResult.Amount, originalResult.Consumptions.Select(c => (c.Layer, c.Qty, c.Value)).ToList(),
                    m.QtyBase < 0 ? originalResult.CreatedLayers.Where(l => !l.IsNegative).ToList() : []);
            }

            // Intrarea simpla se reface la valoarea scrisa; intrarea tinta a unui CA, la valoarea scrisa plus suma adaugata (S3 3.3).
            var plainInbound = counter is null && reversal is null && m.QtyBase > 0;
            decimal? added = plainInbound && attribution is not null && attribution.InboundDeltas.TryGetValue(m.Id, out var delta) ? delta : null;
            results[m.Id] = CostEngine.Apply(state, new CostLine(row, m.QtyBase, m.MovementDate, m.PostingSeq,
                plainInbound && added is null ? m.UnitCost : null, counter, reversal, plainInbound ? m.CostAmount + (added ?? 0) : null));
        }

        var byId = history.Movements.ToDictionary(m => m.Id);
        var changed = history.Movements.Where(m => results[m.Id].Amount != m.CostAmount).Select(m => (m, results[m.Id].Amount)).ToList();
        var layers = state.Layers.Select(l => new LayerWrite(l.Ref, null, null, rowOf[l.MovementRow ?? 0], key, l.LayerDate,
            byId[rowOf[l.MovementRow ?? 0]].CounterMovementId is not null ? l.Seq : null, l.QtyIn, l.QtyRemaining, l.ValueRemaining, l.UnitCost, l.IsNegative,
            l.OriginLayerId)).ToList();
        var consumptions = results.SelectMany(r => r.Value.Consumptions.Select(c => new ConsumptionWrite(null, r.Key, c.Layer.Ref, null, c.Qty, c.Value))).ToList();
        var traceKind = attribution?.AdjustmentKind ?? Recalculation;
        var adjustments = results.Values.SelectMany(r => r.Adjustments.Select(a => new AdjustmentWrite(null, rowOf[a.MovementRow ?? r.Row], key,
                CostCalculator.NegativeRegularization, true, a.Amount)))
            .Concat(changed.Select(c => new AdjustmentWrite(null, c.m.Id, key, traceKind, false, c.Item2 - c.m.CostAmount)))
            .ToList();
        var followers = await store.RebuildAsync(companyId, key, changed.Select(c => (c.m.Id, results[c.m.Id].UnitCost, c.Item2)).ToList(), layers, consumptions,
            adjustments, state, context.UserId, attribution, ct).ConfigureAwait(false);

        if (attribution is null)
        {
            foreach (var group in changed.GroupBy(c => (c.m.SourceModule, c.m.SourceDocumentType, c.m.SourceDocumentId, c.m.AccountingDate)))
            {
                var lines = group.Select(c => new CostAdjustmentEventLine(key.WarehouseId, key.ItemVariantId, c.m.Id, c.Item2 - c.m.CostAmount, "Expense")).ToList();
                await outbox.EnqueueAsync(new CostStocAjustat(Recalculation, group.Key.SourceModule, group.Key.SourceDocumentType, group.Key.SourceDocumentId,
                    group.Key.AccountingDate, lines)
                {
                    CompanyId = companyId,
                    Accounting = Costing.StockAccounting.Adjustment(Recalculation, group.Key.SourceDocumentType, group.Key.SourceDocumentId, group.Key.AccountingDate,
                        lines),
                }, ct).ConfigureAwait(false);
            }
        }

        return new CostRebuild(changed.Select(c => (c.m, c.Item2)).ToList(), followers);
    }
}

/// <summary>Coada de recalculare (S3 3.5; ecranul s3-02).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostView)]
public sealed record GetCostRecalcQueue : IQuery<IReadOnlyList<CostRecalcQueueItemDto>>;

/// <summary>Reface la cerere costul unei chei (S3 3.5).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostRecalculate)]
[Audited("Inv.Cost.Recalculate")]
public sealed record RecalculateCost(int WarehouseId, int? DepotId, int ItemVariantId, int? LotId) : ICommand<int>;

internal sealed class CostRecalcHandler(ICostRecalcStore store, CostRecalculator recalculator, ICurrentContext context, IAuditScope audit)
    : IQueryHandler<GetCostRecalcQueue, IReadOnlyList<CostRecalcQueueItemDto>>, ICommandHandler<RecalculateCost, int>
{
    public Task<IReadOnlyList<CostRecalcQueueItemDto>> Handle(GetCostRecalcQueue request, CancellationToken cancellationToken) =>
        store.QueueAsync(context.CompanyId, 500, cancellationToken);

    public async Task<int> Handle(RecalculateCost request, CancellationToken cancellationToken)
    {
        var changed = await recalculator.RecalculateAsync(context.CompanyId, new CostKey(request.WarehouseId, request.DepotId, request.ItemVariantId, request.LotId),
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.CostKey", request.ItemVariantId, new { request.WarehouseId, request.DepotId, request.LotId, Changed = changed });
        return changed;
    }
}
