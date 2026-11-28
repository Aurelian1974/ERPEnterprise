namespace Erp.Inventory.Domain.Lots;

/// <summary>Un lot candidat al unei chei (gestiune, depozit, varianta): starea, termenul, prima intrare si disponibilul la data miscarii.</summary>
public sealed record LotCandidate(int WarehouseId, int? DepotId, int ItemVariantId, int LotId, string LotCode, byte Status, DateOnly? ExpiryDate, byte? ExpiryKind,
    DateOnly? FirstReceiptDate, bool IsTechnical, decimal Available);

/// <summary>O iesire de alocat: randul, cheia, cantitatea (pozitiva), lotul ales (daca e), tipul miscarii si durata minima ramasa ceruta de client.</summary>
public sealed record LotDemand(int Row, int WarehouseId, int? DepotId, int ItemVariantId, decimal Qty, int? LotId, string MovementTypeCode,
    int MinRemainingDays = 0);

/// <summary>O parte alocata: randul cererii, lotul si cantitatea (pozitiva).</summary>
public sealed record LotAllocation(int Row, int LotId, decimal Qty);

/// <summary>Alocare imposibila: codul API (S4 7) si mesajul.</summary>
public sealed class LotAllocationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Alegerea lotului la iesire (02.05, S4 3.3, 3.5), pura: lotul ales se verifica (starea permite tipul miscarii, termenul la vanzare, cantitatea);
/// liniile fara lot primesc FEFO loturile disponibile (termenul, apoi prima intrare, apoi Id-ul; cu regula 2, prima intrare), fara cele sub
/// durata minima a clientului si, la vanzare, fara cele cu data limita de consum depasita. Faptele din afara ERP (POS) trec: ce nu acopera
/// loturile merge pe lotul tehnic NEIDENTIFICAT. Cererile din acelasi apel consuma acelasi disponibil, in ordine.
/// </summary>
public static class LotAllocator
{
    public const string InsufficientQty = "INV_LOT_INSUFFICIENT_QTY";
    public const string StatusForbidsIssue = "INV_LOT_STATUS_FORBIDS_ISSUE";
    public const string Expired = "INV_LOT_EXPIRED";
    public const string ShelfLifeTooShort = "INV_LOT_SHELF_LIFE_TOO_SHORT";

    private static readonly HashSet<string> CustomerIssues = new(StringComparer.Ordinal) { "ISSUE_SALE", "POS_SALE" };

    private static readonly HashSet<string> RestrictedIssues = new(StringComparer.Ordinal)
    {
        "RETURN_TO_SUPPLIER", "UNLOAD", "COUNT_MINUS", "LOT_CHANGE_OUT", "TRANSFER_OUT",
    };

    /// <summary>Iesirile permise pe starea lotului (S4 3.3): disponibil toate; carantina si blocat retur, descarcare, transfer, inventar; expirat si retras fara transfer.</summary>
    public static bool AllowsIssue(byte status, string movementTypeCode) => status switch
    {
        1 => true,
        2 or 3 => RestrictedIssues.Contains(movementTypeCode),
        _ => RestrictedIssues.Contains(movementTypeCode) && movementTypeCode != "TRANSFER_OUT",
    };

    public static bool IsCustomerIssue(string movementTypeCode) => CustomerIssues.Contains(movementTypeCode);

    private static bool UseByPassed(LotCandidate lot, DateOnly date) => lot.ExpiryKind == 1 && lot.ExpiryDate is { } expiry && expiry < date;

    private static bool TooShort(LotCandidate lot, LotDemand demand, DateOnly date) =>
        demand.MinRemainingDays > 0 && IsCustomerIssue(demand.MovementTypeCode) && lot.ExpiryDate is { } expiry && expiry.DayNumber - date.DayNumber < demand.MinRemainingDays;

    public static IReadOnlyList<LotAllocation> Allocate(IReadOnlyList<LotDemand> demands, IReadOnlyList<LotCandidate> candidates,
        IReadOnlyDictionary<int, int> technicalLots, DateOnly date, byte pickRule, bool isExternalFact)
    {
        ArgumentNullException.ThrowIfNull(demands);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(technicalLots);
        var left = candidates.ToDictionary(c => (c.WarehouseId, c.DepotId ?? 0, c.ItemVariantId, c.LotId), c => c.Available);
        var result = new List<LotAllocation>();
        foreach (var demand in demands)
        {
            var key = (demand.WarehouseId, demand.DepotId ?? 0, demand.ItemVariantId);
            var lots = candidates.Where(c => (c.WarehouseId, c.DepotId ?? 0, c.ItemVariantId) == key).ToList();
            var remaining = demand.Qty;
            decimal Take(LotCandidate lot, decimal wanted)
            {
                var k = (lot.WarehouseId, lot.DepotId ?? 0, lot.ItemVariantId, lot.LotId);
                var qty = Math.Min(wanted, Math.Max(left[k], 0));
                if (qty > 0)
                {
                    left[k] -= qty;
                    result.Add(new LotAllocation(demand.Row, lot.LotId, qty));
                }

                return qty;
            }

            if (demand.LotId is { } chosen)
            {
                var lot = lots.FirstOrDefault(l => l.LotId == chosen);
                if (lot is not null && !isExternalFact)
                {
                    if (!AllowsIssue(lot.Status, demand.MovementTypeCode))
                    {
                        throw new LotAllocationException(StatusForbidsIssue, $"Starea lotului {lot.LotCode} nu permite această ieșire.");
                    }

                    if (IsCustomerIssue(demand.MovementTypeCode) && UseByPassed(lot, date))
                    {
                        throw new LotAllocationException(Expired, $"Lotul {lot.LotCode} a expirat; nu se mai poate vinde.");
                    }

                    if (TooShort(lot, demand, date))
                    {
                        throw new LotAllocationException(ShelfLifeTooShort, $"Lotul {lot.LotCode} are prea puține zile până la expirare pentru acest client.");
                    }
                }

                if (lot is not null)
                {
                    remaining -= Take(lot, remaining);
                }

                if (remaining > 0 && !isExternalFact)
                {
                    throw new LotAllocationException(InsufficientQty,
                        $"Cantitate insuficientă pe lotul {lot?.LotCode ?? chosen.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
                }
            }

            if (remaining > 0)
            {
                var eligible = lots.Where(l => l.Status == 1 && !l.IsTechnical && l.LotId != demand.LotId
                        && !(IsCustomerIssue(demand.MovementTypeCode) && UseByPassed(l, date)) && !TooShort(l, demand, date))
                    .OrderBy(l => pickRule == 2 ? l.FirstReceiptDate ?? DateOnly.MaxValue : l.ExpiryDate ?? DateOnly.MaxValue)
                    .ThenBy(l => l.FirstReceiptDate ?? DateOnly.MaxValue)
                    .ThenBy(l => l.LotId);
                foreach (var lot in eligible)
                {
                    if (remaining == 0)
                    {
                        break;
                    }

                    remaining -= Take(lot, remaining);
                }
            }

            if (remaining > 0)
            {
                if (isExternalFact && technicalLots.TryGetValue(demand.ItemVariantId, out var technical))
                {
                    result.Add(new LotAllocation(demand.Row, technical, remaining));
                }
                else
                {
                    throw new LotAllocationException(InsufficientQty, "Cantitatea de pe loturile disponibile nu ajunge pentru această ieșire.");
                }
            }
        }

        return result;
    }
}
