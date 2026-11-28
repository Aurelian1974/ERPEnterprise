import { useState } from 'react';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { PartnerPicker, VariantPicker, WarehouseSelect } from './LotPickers';
import { quantityForApi } from './inventoryApi';
import { LotStatus, lotStatusLabel, lotStatusTone, qtyToRo, useLotProposal, useLots, type LotAllocation, type LotItem } from './lotsApi';

/** De ce un lot din gestiune nu e in propunere (macheta s4-03): starea, termenul depasit sau durata minima a clientului; altfel nu a mai fost nevoie de el. */
export function exclusionReason(lot: Pick<LotItem, 'status' | 'daysLeft'>, proposal: readonly LotAllocation[], lotId: number): TextKey | undefined {
  if (proposal.some((p) => p.lotId === lotId)) {
    return undefined;
  }
  if (lot.status !== LotStatus.Available) {
    return 'inv.prop.excludedStatus';
  }
  if (lot.daysLeft != null && lot.daysLeft < 0) {
    return 'inv.prop.excludedExpired';
  }
  return proposal.length === 0 ? 'inv.prop.excludedShelfLife' : 'inv.prop.notNeeded';
}

/**
 * Stocuri › Stoc › Alegerea loturilor (02.05, S4 3.5; macheta s4-03): propunerea FEFO pentru o iesire, din gestiune, pentru client si data livrarii,
 * cu aceeasi functie ca la validare (IStockLedger.ProposeLotsAsync). Documentele de iesire ale Vanzarilor o vor deschide pe linie.
 */
export function LotProposalPage() {
  const [warehouse, setWarehouse] = useState<number | undefined>();
  const [variant, setVariant] = useState<{ id: number; label: string } | undefined>();
  const [qty, setQty] = useState('');
  const [customer, setCustomer] = useState<{ id: number; name: string } | undefined>();
  const [date, setDate] = useState(new Date().toISOString().slice(0, 10));
  const qtyApi = quantityForApi(qty) ?? '';
  const proposal = useLotProposal(warehouse, variant?.id, qtyApi, customer?.id, date);
  const lots = useLots(variant?.id, warehouse, '');
  const allocations = proposal.data ?? [];
  const rows = warehouse === undefined || variant === undefined ? [] : (lots.data ?? []).filter((l) => l.warehouseId === warehouse && Number(l.qtyOnHand) > 0);
  const proposed = allocations.reduce((s, a) => s + a.qtyBase, 0);

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.stock')} › {t('menu.lotProposal')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.lotProposal')}</h1>
          <div className="sub2">{t('inv.prop.subtitle')}</div>
        </div>
      </div>
      <section className="card">
        <div className="chips-row">
          <WarehouseSelect value={warehouse} onChange={setWarehouse} allowAll={false} />
          <VariantPicker value={variant} onChange={setVariant} />
          <input className="cell-input num" placeholder={t('inv.prop.qty')} aria-label={t('inv.prop.qty')} value={qty} onChange={(e) => setQty(e.target.value)} />
          <PartnerPicker value={customer} onChange={setCustomer} placeholder={t('inv.prop.customer')} />
          <label className="chips-row inline">
            <span className="muted small">{t('inv.prop.delivery')}</span>
            <input className="cell-input" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </label>
        </div>
        {proposal.isError && <p role="alert">{proposal.error.message}</p>}
        {variant !== undefined && warehouse !== undefined && (
          <h2 className="form-title">{t('inv.prop.title', { variant: variant.label, qty: qtyToRo(qtyApi) })}</h2>
        )}
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('inv.prop.lotCku')}</th>
              <th scope="col">{t('inv.lot.expiry')}</th>
              <th scope="col" className="num">{t('inv.exp.days')}</th>
              <th scope="col">{t('inv.prop.status')}</th>
              <th scope="col" className="num">{t('inv.lot.inStock')}</th>
              <th scope="col" className="num">{t('inv.prop.proposed')}</th>
              <th scope="col">{t('inv.lot.why')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((l) => {
              const allocation = allocations.find((a) => a.lotId === l.lotId);
              const order = allocation === undefined ? undefined : allocations.indexOf(allocation) + 1;
              const reason = exclusionReason(l, allocations, l.lotId);
              return (
                <tr key={l.lotId}>
                  <td><code>{l.lotCode}</code><div className="muted small">{l.ckuCode}</div></td>
                  <td>{l.expiryDate == null ? '—' : isoToRo(l.expiryDate)}</td>
                  <td className="num">{l.daysLeft ?? '—'}</td>
                  <td><span className={`pill ${lotStatusTone(l.status)}`}>{t(lotStatusLabel(l.status))}</span></td>
                  <td className="num">{qtyToRo(l.qtyOnHand)}</td>
                  <td className="num"><strong>{allocation === undefined ? '—' : qtyToRo(allocation.qtyBase)}</strong></td>
                  <td>
                    {order !== undefined && <><span className="pill ok">{order}</span> {t(allocation?.isFirstChoice === true ? 'inv.prop.first' : 'inv.prop.completes')}</>}
                    {reason !== undefined && <span className={`pill ${reason === 'inv.prop.notNeeded' ? 'off' : 'bad'}`}>{t(reason)}</span>}
                  </td>
                </tr>
              );
            })}
            {rows.length > 0 && (
              <tr>
                <td colSpan={4}><strong>{t('inv.prop.total')}</strong></td>
                <td className="num"><strong>{qtyToRo(rows.reduce((s, l) => s + Number(l.qtyOnHand), 0))}</strong></td>
                <td className="num"><strong>{qtyToRo(proposed)}</strong></td>
                <td>{proposed < Number(qtyApi || 0) ? <span className="pill bad">{t('inv.prop.short', { qty: qtyToRo(Number(qtyApi) - proposed) })}</span> : null}</td>
              </tr>
            )}
          </tbody>
        </table>
        <div className="banner dashed">{t('inv.prop.note')}</div>
      </section>
    </div>
  );
}
