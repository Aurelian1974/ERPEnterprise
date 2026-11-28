namespace Erp.Inventory.Domain.Costing;

/// <summary>Metoda de evaluare a cheii (S1 3.4): identificarea directa se comporta ca FIFO, pentru ca lotul face parte din cheie.</summary>
public enum CostMethod : byte
{
    Fifo = 1,
    Average = 2,
    Lifo = 3,
    SpecificIdentification = 4,
}

/// <summary>
/// Un strat de cost (S3 2, inv.CostLayer): existent (Id) sau creat in calculul curent (Ref). Stratul negativ (S3 3.4) are cantitatea ramasa
/// negativa si tine miscarea de iesire care l-a creat, pentru regularizare.
/// </summary>
public sealed class CostLayerState
{
    public long? Id { get; init; }

    public int? Ref { get; init; }

    public DateOnly LayerDate { get; init; }

    public long Seq { get; init; }

    public decimal QtyIn { get; init; }

    public decimal QtyRemaining { get; set; }

    public decimal ValueRemaining { get; set; }

    public decimal UnitCost { get; init; }

    public bool IsNegative { get; init; }

    /// <summary>Miscarea care a creat stratul (intrarea sau, la stratul negativ, iesirea): Id-ul existent sau randul din calculul curent.</summary>
    public long? MovementId { get; init; }

    public int? MovementRow { get; init; }

    public long? OriginLayerId { get; init; }

    public bool Changed { get; set; }
}

/// <summary>Starea unei chei de cost (gestiune, depozit, varianta, lot): cantitatea, valoarea, straturile deschise si costurile de rezerva.</summary>
public sealed class CostKeyState
{
    public CostMethod Method { get; init; }

    public decimal Qty { get; set; }

    public decimal Value { get; set; }

    public decimal? LastUnitCost { get; set; }

    /// <summary>Costul de rezerva pentru iesirea pe minus fara cost cunoscut: media variantei in firma, apoi costul standard (S3 3.4).</summary>
    public decimal? FallbackUnitCost { get; init; }

    public List<CostLayerState> Layers { get; init; } = [];

    /// <summary>Ultima iesire pe minus la CMP, careia i se ataseaza regularizarea (S3 3.4 punctul 4).</summary>
    public long? LastNegativeMovementId { get; set; }

    public int? LastNegativeRow { get; set; }

    internal int NextRef { get; set; }
}

/// <summary>O parte consumata dintr-un strat (S3 3.2, inv.CostLayerConsumption).</summary>
public sealed record CostConsumption(CostLayerState Layer, decimal Qty, decimal Value);

/// <summary>O ajustare de valoare, fara cantitate (S3 2, inv.CostAdjustment), atasata unei miscari existente sau unui rand din calculul curent.</summary>
public sealed record CostAdjustmentResult(long? MovementId, int? MovementRow, decimal Amount);

/// <summary>Perechea de iesire a unei intrari dintr-un transfer: consumurile ei (straturile se muta cu data lor) sau, la CMP, valoarea ei.</summary>
public sealed record CostCounter(decimal Amount, IReadOnlyList<CostCounterPart> Parts);

/// <summary>O parte a iesirii pereche: cantitatea, valoarea si data originala a stratului consumat.</summary>
public sealed record CostCounterPart(decimal Qty, decimal Value, DateOnly LayerDate, long Seq, long? LayerId);

/// <summary>Stornarea unei miscari: valoarea originalului si fie consumurile iesirii stornate (se pun inapoi), fie stratul intrarii stornate (se scoate).</summary>
public sealed record CostReversal(decimal OriginalAmount, IReadOnlyList<(CostLayerState Layer, decimal Qty, decimal Value)> Consumptions,
    IReadOnlyList<CostLayerState> InboundLayers)
{
    public CostReversal(decimal originalAmount, IReadOnlyList<(CostLayerState Layer, decimal Qty, decimal Value)> consumptions, CostLayerState? inboundLayer)
        : this(originalAmount, consumptions, inboundLayer is null ? [] : [inboundLayer])
    {
    }
}

/// <summary>
/// O miscare de costat: cantitatea cu semn, data, ordinea, costul intrarii din sursa si, dupa caz, perechea transferului sau stornarea.
/// InboundAmount fixeaza valoarea intrarii (la recalculare, valoarea scrisa, eventual cu costul adaugat al unui document CA, S3 3.3).
/// </summary>
public sealed record CostLine(int Row, decimal Qty, DateOnly Date, long Seq, decimal? InboundUnitCost, CostCounter? Counter = null, CostReversal? Reversal = null,
    decimal? InboundAmount = null);

/// <summary>Costul unei miscari si efectele lui: consumurile, stratul creat (sau straturile, la transfer) si regularizarile.</summary>
public sealed record CostLineResult(int Row, decimal UnitCost, decimal Amount, IReadOnlyList<CostConsumption> Consumptions, IReadOnlyList<CostLayerState> CreatedLayers,
    IReadOnlyList<CostAdjustmentResult> Adjustments, bool WentNegative);

/// <summary>Stornarea unei intrari al carei strat a fost consumat intre timp (S3 3.2; 54302).</summary>
public sealed class CostLayerConsumedException(string message) : Exception(message);

/// <summary>
/// Motorul de cost al Stocurilor (02.05, S3 3.1–3.4), pur si secvential pe o cheie: aceeasi functie costa miscarile la inregistrare si le reface
/// la recalculare (S3 3.5). Valorile se rotunjesc la 2 zecimale, costul unitar la 6; cand cheia se goleste, iesirea ia toata valoarea ramasa
/// (S3 regula 3). Valoarea cheii = suma valorilor miscarilor + regularizarile (S3 regula 2).
/// </summary>
public static class CostEngine
{
    public static CostLineResult Apply(CostKeyState key, CostLine line) =>
        line.Qty > 0 ? In(key, line) : Out(key, line);

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Unit(decimal amount, decimal qty) => qty == 0 ? 0 : decimal.Round(Math.Abs(amount) / Math.Abs(qty), 6, MidpointRounding.AwayFromZero);

    private static bool Layered(CostKeyState key) => key.Method != CostMethod.Average;

    private static decimal CurrentUnit(CostKeyState key) =>
        key.LastUnitCost ?? (key.Qty > 0 ? Unit(key.Value, key.Qty) : (decimal?)null) ?? key.FallbackUnitCost ?? 0;

    private static CostLineResult In(CostKeyState key, CostLine line)
    {
        var qty = line.Qty;
        if (line.Reversal is { } reversal)
        {
            // Stornarea unei iesiri: cantitatile se pun inapoi in straturile din care au iesit, la valoarea lor (S3 3.2).
            foreach (var (layer, q, v) in reversal.Consumptions)
            {
                layer.QtyRemaining += q;
                layer.ValueRemaining += v;
                layer.Changed = true;
                if (!key.Layers.Contains(layer))
                {
                    key.Layers.Add(layer);
                }
            }

            var amount = -reversal.OriginalAmount;
            key.Qty += qty;
            key.Value += amount;
            return new CostLineResult(line.Row, Unit(amount, qty), amount, [], [], [], false);
        }

        var parts = line.Counter is { } counter
            ? (Layered(key) && counter.Parts.Count > 0
                ? counter.Parts.Select(p => (p.Qty, p.Value, p.LayerDate, p.Seq, p.LayerId)).ToList()
                : [(qty, counter.Amount, line.Date, line.Seq, (long?)null)])
            : [(qty, line.InboundAmount ?? Money(qty * (line.InboundUnitCost ?? CurrentUnit(key))), line.Date, line.Seq, null)];
        var total = parts.Sum(p => p.Item2);
        var unit = line.Counter is null && line.InboundUnitCost is { } given ? given : Unit(total, qty);
        var adjustments = new List<CostAdjustmentResult>();
        var created = new List<CostLayerState>();

        if (!Layered(key))
        {
            if (key.Qty < 0)
            {
                // CMP pe minus: partea acoperita se regularizeaza la costul intrarii (S3 3.4 punctul 4).
                var covered = Math.Min(-key.Qty, qty);
                var provisional = covered == -key.Qty ? -key.Value : Money(covered * Unit(key.Value, key.Qty));
                var actual = covered == qty ? total : Money(covered * unit);
                if (provisional != actual)
                {
                    adjustments.Add(new CostAdjustmentResult(key.LastNegativeMovementId, key.LastNegativeRow, provisional - actual));
                }
            }

            key.Qty += qty;
            key.Value += total + adjustments.Sum(a => a.Amount);
            key.LastUnitCost = unit;
            return new CostLineResult(line.Row, unit, total, [], [], adjustments, false);
        }

        foreach (var (partQty, partValue, partDate, partSeq, partLayer) in parts)
        {
            var remainingQty = partQty;
            var remainingValue = partValue;
            // Intai straturile negative, cele mai vechi intai; diferenta fata de costul provizoriu e regularizarea iesirii pe minus (S3 3.4).
            foreach (var negative in key.Layers.Where(l => l.IsNegative && l.QtyRemaining < 0).OrderBy(l => l.LayerDate).ThenBy(l => l.Seq).ToList())
            {
                if (remainingQty == 0)
                {
                    break;
                }

                var covered = Math.Min(-negative.QtyRemaining, remainingQty);
                var provisional = covered == -negative.QtyRemaining ? -negative.ValueRemaining : Money(covered * negative.UnitCost);
                var actual = covered == remainingQty ? remainingValue : Money(covered * Unit(partValue, partQty));
                negative.QtyRemaining += covered;
                negative.ValueRemaining += provisional;
                negative.Changed = true;
                remainingQty -= covered;
                remainingValue -= actual;
                if (provisional != actual)
                {
                    adjustments.Add(new CostAdjustmentResult(negative.MovementId, negative.MovementRow, provisional - actual));
                }
            }

            if (remainingQty > 0)
            {
                var layer = new CostLayerState
                {
                    Ref = key.NextRef++, LayerDate = partDate, Seq = partSeq, QtyIn = remainingQty, QtyRemaining = remainingQty, ValueRemaining = remainingValue,
                    UnitCost = Unit(remainingValue, remainingQty), MovementRow = line.Row, OriginLayerId = partLayer, Changed = true,
                };
                key.Layers.Add(layer);
                created.Add(layer);
            }
        }

        key.Qty += qty;
        key.Value += total + adjustments.Sum(a => a.Amount);
        key.LastUnitCost = unit;
        return new CostLineResult(line.Row, unit, total, [], created, adjustments, false);
    }

    private static CostLineResult Out(CostKeyState key, CostLine line)
    {
        var qty = -line.Qty;
        if (line.Reversal is { InboundLayers.Count: > 0 } reversal)
        {
            // Stornarea unei intrari scoate straturile ei (unul, sau cate unul pe strat mutat la transfer), neatinse; consumate intre timp,
            // stornarea e refuzata (S3 3.2; 54302).
            if (reversal.InboundLayers.Any(l => l.QtyRemaining != l.QtyIn) || reversal.InboundLayers.Sum(l => l.QtyIn) != qty)
            {
                throw new CostLayerConsumedException("Stratul intrării stornate a fost consumat între timp; stornarea nu se poate face.");
            }

            var left = reversal.OriginalAmount;
            for (var i = 0; i < reversal.InboundLayers.Count; i++)
            {
                var layer = reversal.InboundLayers[i];
                var value = i == reversal.InboundLayers.Count - 1 ? left : layer.ValueRemaining;
                left -= value;
                layer.QtyRemaining = 0;
                layer.ValueRemaining -= value;
                layer.Changed = true;
            }

            var reversed = -reversal.OriginalAmount;
            key.Qty -= qty;
            key.Value += reversed;
            return new CostLineResult(line.Row, Unit(reversed, qty), reversed, [], [], [], false);
        }

        if (line.Reversal is { } plain)
        {
            var reversed = -plain.OriginalAmount;
            key.Qty -= qty;
            key.Value += reversed;
            return new CostLineResult(line.Row, Unit(reversed, qty), reversed, [], [], [], key.Qty < 0);
        }

        if (!Layered(key))
        {
            decimal amount;
            var negative = false;
            if (key.Qty > 0 && key.Qty == qty)
            {
                amount = -key.Value;
            }
            else if (key.Qty > 0 && key.Qty > qty)
            {
                amount = -Money(qty * Unit(key.Value, key.Qty));
            }
            else
            {
                // Pe minus: stocul ramas la medie, restul la ultimul cost cunoscut (S3 3.4).
                var covered = Math.Max(key.Qty, 0);
                var coveredValue = covered > 0 ? key.Value : 0;
                amount = -(coveredValue + Money((qty - covered) * (key.LastUnitCost ?? (covered > 0 ? Unit(key.Value, key.Qty) : key.FallbackUnitCost ?? 0))));
                negative = true;
                key.LastNegativeMovementId = null;
                key.LastNegativeRow = line.Row;
            }

            key.Qty -= qty;
            key.Value += amount;
            if (key.Qty == 0 && key.Value != 0)
            {
                amount -= key.Value;
                key.Value = 0;
            }

            return new CostLineResult(line.Row, Unit(amount, qty), amount, [], [], [], negative);
        }

        var consumptions = new List<CostConsumption>();
        var created = new List<CostLayerState>();
        var remaining = qty;
        var open = key.Layers.Where(l => !l.IsNegative && l.QtyRemaining > 0).ToList();
        // Straturile existente la data iesirii, apoi (la iesirile cu data in trecut) restul; FIFO cel mai vechi, LIFO cel mai nou (S3 3.2).
        var ordered = key.Method == CostMethod.Lifo
            ? open.OrderBy(l => l.LayerDate > line.Date).ThenByDescending(l => l.LayerDate).ThenByDescending(l => l.Seq)
            : open.OrderBy(l => l.LayerDate > line.Date).ThenBy(l => l.LayerDate).ThenBy(l => l.Seq);
        foreach (var layer in ordered.ToList())
        {
            if (remaining == 0)
            {
                break;
            }

            var take = Math.Min(remaining, layer.QtyRemaining);
            var value = take == layer.QtyRemaining ? layer.ValueRemaining : Money(take * layer.UnitCost);
            layer.QtyRemaining -= take;
            layer.ValueRemaining -= value;
            layer.Changed = true;
            consumptions.Add(new CostConsumption(layer, take, value));
            remaining -= take;
        }

        var went = false;
        if (remaining > 0)
        {
            // Stratul negativ, la ultimul cost cunoscut (S3 3.4 punctele 1–2).
            var unit = key.LastUnitCost ?? key.FallbackUnitCost ?? 0;
            var layer = new CostLayerState
            {
                Ref = key.NextRef++, LayerDate = line.Date, Seq = line.Seq, QtyIn = 0, QtyRemaining = -remaining, ValueRemaining = -Money(remaining * unit),
                UnitCost = unit, IsNegative = true, MovementRow = line.Row, Changed = true,
            };
            key.Layers.Add(layer);
            created.Add(layer);
            went = true;
        }

        var total = -(consumptions.Sum(c => c.Value) - created.Sum(c => c.ValueRemaining));
        key.Qty -= qty;
        key.Value += total;
        if (key.Qty == 0 && key.Value != 0 && consumptions.Count > 0)
        {
            // Restul de rotunjire merge pe iesirea care goleste cheia (S3 regula 3), pe ultimul consum.
            var last = consumptions[^1];
            var fixedValue = last.Value + key.Value;
            last.Layer.ValueRemaining -= key.Value;
            consumptions[^1] = last with { Value = fixedValue };
            total -= key.Value;
            key.Value = 0;
        }

        return new CostLineResult(line.Row, Unit(total, qty), total, consumptions, created, [], went);
    }
}
