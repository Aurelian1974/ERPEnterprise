using Erp.Finance.Contracts.Accounting;
using Erp.Inventory.Contracts;

namespace Erp.Inventory.Application.Costing;

/// <summary>
/// Blocul de contare al evenimentelor Stocurilor (02.05, S3 3.6, 3.7; contractul C2 3.1). Stocurile nu stiu conturi: trimit componente cu tipul
/// miscarii (OperationTypeCode), gestiunea si centrul de cost; modelele livrate (seed-ul Finantelor, „16_InventoryPostingModels”) aleg conturile.
/// Componentele, cu semnul lor (stornarile vin cu semn opus, notate in rosu):
///   intrare: StockIn (stocul, la cost sau la pret cu amanuntul), CounterIn (contrapartida, la cost), MarkupIn si VatIn (378, 4428, la amanunt);
///   iesire:  StockOut, CounterOut (la cost sau, la amanunt cu metoda 1, la pretul cu amanuntul), MarkupOut si VatOut (la amanunt cu metoda 2).
/// Predarile si primirile nu au contrapartida (stocul trece dintr-o gestiune in alta), custodia nu se conteaza (bunuri ale tertilor).
/// </summary>
public static class StockAccounting
{
    private static readonly HashSet<string> Inbound = new(StringComparer.Ordinal)
    {
        "RECEIPT_PURCHASE", "RETURN_FROM_CUSTOMER", "TRANSFER_IN", "LOAD", "PRODUCTION_IN", "COUNT_PLUS", "CUSTODY_IN", "LOT_CHANGE_IN",
    };

    private static readonly HashSet<string> NoCounter = new(StringComparer.Ordinal) { "TRANSFER_IN", "TRANSFER_OUT" };

    private static readonly HashSet<string> NotPosted = new(StringComparer.Ordinal) { "CUSTODY_IN", "CUSTODY_OUT", "LOT_CHANGE_OUT", "LOT_CHANGE_IN" };

    public static AccountingPayload Movements(string module, string documentType, long documentId, string? number, DateOnly movementDate,
        DateOnly accountingDate, bool reversal, IReadOnlyList<StockMovementEventLine> lines)
    {
        var components = new List<AccountingComponent>();
        foreach (var m in lines.Where(m => !NotPosted.Contains(m.MovementTypeCode)))
        {
            var direction = Inbound.Contains(m.MovementTypeCode) ? 1 : -1;
            var cost = m.CostAmount * direction;
            var retail = m.RetailAmount is { } r ? r * direction : (decimal?)null;
            var vat = (m.RetailVatAmount ?? 0) * direction;
            AccountingComponent C(string name, decimal amount) => new(name, amount, amount)
            {
                OperationTypeCode = m.MovementTypeCode, WarehouseId = m.WarehouseId, CostCenterId = m.CostCenterId,
                SourceLineRef = m.MovementId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };

            if (direction > 0)
            {
                components.Add(C("StockIn", retail ?? cost));
                if (retail is { } inRetail)
                {
                    components.Add(C("MarkupIn", inRetail - vat - cost));
                    components.Add(C("VatIn", vat));
                }

                if (!NoCounter.Contains(m.MovementTypeCode))
                {
                    components.Add(C("CounterIn", cost));
                }
            }
            else
            {
                // La iesire, valorile vin negative; direction = -1 le face pozitive.
                var byK = retail is not null && m.RetailDischargeMethod != 2;
                components.Add(C("StockOut", retail ?? cost));
                if (retail is { } outRetail && !byK)
                {
                    components.Add(C("MarkupOut", outRetail - vat - cost));
                    components.Add(C("VatOut", vat));
                }

                if (!NoCounter.Contains(m.MovementTypeCode))
                {
                    components.Add(C("CounterOut", byK ? retail!.Value : cost));
                }
            }
        }

        return new AccountingPayload("Inventory." + documentType, number, movementDate, accountingDate, "RON", 1m, [], components.Where(c => c.AmountRon != 0).ToList())
        {
            SourceModule = "Inventory", DocumentId = documentId, IsReversal = reversal,
        };
    }

    /// <summary>
    /// Ajustarile de cost: la costul adaugat si diferenta de pret (Kind 3, 4; flagul Landed) partea pe stoc (AdjStock) si partea pe iesiri
    /// (AdjExpense) fata de 408; la recalculare (Kind 1) schimbarea costului iesirii (AdjExpense = − diferenta) fata de stoc; la regularizarea
    /// negativului (Kind 2) schimbarea valorii stocului (AdjStock) fata de costul vanzarilor.
    /// </summary>
    public static AccountingPayload Adjustment(byte kind, string documentType, long documentId, DateOnly accountingDate, IReadOnlyList<CostAdjustmentEventLine> lines)
    {
        var landed = kind is 3 or 4;
        var components = lines.Select(l =>
        {
            var (name, amount) = landed
                ? (l.TargetKind == "Stock" ? "AdjStock" : "AdjExpense", l.Amount)
                : kind == 2 ? ("AdjStock", l.Amount) : ("AdjExpense", -l.Amount);
            return new AccountingComponent(name, amount, amount)
            {
                WarehouseId = l.WarehouseId,
                SourceLineRef = l.SourceMovementId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }).Where(c => c.AmountRon != 0).ToList();
        return new AccountingPayload("Inventory.CostAdjustment." + documentType, $"{documentType} {documentId}", accountingDate, accountingDate, "RON", 1m,
            landed ? ["Landed"] : [], components)
        {
            SourceModule = "Inventory", DocumentId = documentId,
        };
    }

    /// <summary>Descarcarea lunara (DA): 378 = 607 adaosul, 4428 = 607 TVA-ul vanzarilor, pe gestiune.</summary>
    public static AccountingPayload Discharge(DescarcareAmanuntValidata e, int? costCenterId) =>
        new("Inventory.RetailDischarge", e.Number, e.AccountingDate, e.AccountingDate, "RON", 1m, [],
            new[] { new AccountingComponent("Markup", e.Markup, e.Markup), new AccountingComponent("Vat", e.SalesVat, e.SalesVat) }
                .Select(c => c with { WarehouseId = e.WarehouseId, CostCenterId = costCenterId }).Where(c => c.AmountRon != 0).ToList())
        {
            SourceModule = "Inventory", DocumentId = e.DocumentId,
        };

    /// <summary>Modificarea pretului (MP): 371 = 378 (diferenta fara TVA) + 4428 (TVA-ul diferentei), pe gestiune.</summary>
    public static AccountingPayload PriceChange(ModificarePretAmanuntValidata e)
    {
        var diff = e.Lines.Sum(l => l.DiffAmount);
        var vat = e.Lines.Sum(l => l.DiffVatAmount);
        return new AccountingPayload("Inventory.RetailPriceChange", e.Number, e.DocumentDate, e.DocumentDate, "RON", 1m, [],
            new[] { new AccountingComponent("Diff", diff, diff), new AccountingComponent("DiffMarkup", diff - vat, diff - vat),
                new AccountingComponent("DiffVat", vat, vat) }.Select(c => c with { WarehouseId = e.WarehouseId }).Where(c => c.AmountRon != 0).ToList())
        {
            SourceModule = "Inventory", DocumentId = e.DocumentId,
        };
    }
}
