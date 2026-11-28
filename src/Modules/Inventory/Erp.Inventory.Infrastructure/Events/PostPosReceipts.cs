using Erp.Core.Contracts;
using Erp.Inventory.Contracts;

namespace Erp.Inventory.Infrastructure.Events;

/// <summary>
/// Consumatorul BonuriPOSSincronizate (02.05, S2 4.7): fiecare bon devine miscari POS_SALE (vanzari) si RETURN_FROM_CUSTOMER (retururi), ca fapte
/// deja petrecute (fara verificarea politicii, cu marcajul de stoc negativ fortat si alerta). Idempotent prin sursa: bonul (ExternalId) si linia
/// lui; reluarea sincronizarii nu dubleaza stocul (S2-A9). Luna inchisa ramane in coada evenimentului (eroarea lasa evenimentul nelivrat).
/// </summary>
public sealed class PostPosReceipts(IStockLedger ledger, IPeriodLock periods) : IIntegrationEventHandler<BonuriPOSSincronizate>
{
    public async Task HandleAsync(BonuriPOSSincronizate evt, CancellationToken ct)
    {
        foreach (var receipt in evt.Receipts)
        {
            if (await periods.IsClosedAsync(evt.CompanyId, receipt.SaleDate, ct).ConfigureAwait(false))
            {
                throw ErpException.Conflict(CoreErrorCodes.PeriodClosed, $"Bonul {receipt.Number} e datat într-o lună închisă; rămâne în coadă până la redeschidere.");
            }

            var lines = receipt.Lines.Where(l => l.QtyBase != 0).Select(l => new StockMovementRequest(l.ExternalLineId, 1,
                l.QtyBase > 0 ? "POS_SALE" : "RETURN_FROM_CUSTOMER", receipt.DepotId, receipt.WarehouseId, l.ItemVariantId, -l.QtyBase, null, null, null, null,
                null, null, null, null)).ToList();
            await ledger.PostAsync(new StockPostingRequest(evt.CompanyId, "pos", "POS_RECEIPT", receipt.ExternalId, receipt.Number, receipt.SaleDate,
                receipt.SaleDate, null, true, lines), ct).ConfigureAwait(false);
        }
    }
}
