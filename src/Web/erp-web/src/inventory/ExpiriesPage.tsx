import { useState } from 'react';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { WarehouseSelect } from './LotPickers';
import { expiryKindLabel, LotStatus, qtyToRo, useLots, type LotItem } from './lotsApi';

/**
 * Actiunea propusa pentru un lot care expira (macheta s4-02): cel expirat cu data limita de consum se descarca pentru casare, cel cu
 * durabilitate minimala depasita se doneaza sau se vinde cu drept, cel care expira in cel mult 7 zile primeste reducere de pret, restul vanzare cu
 * prioritate.
 */
export function proposedAction(row: Pick<LotItem, 'daysLeft' | 'expiryKind' | 'status'>): TextKey {
  const days = row.daysLeft ?? Number.MAX_SAFE_INTEGER;
  if (row.status === LotStatus.Expired || (days < 0 && row.expiryKind === 1)) {
    return 'inv.exp.action.scrap';
  }
  if (days < 0) {
    return 'inv.exp.action.donate';
  }
  return days <= 7 ? 'inv.exp.action.discount' : 'inv.exp.action.priority';
}

/** Stocuri › Rapoarte › Expirari (02.05, S4 3.2; macheta s4-02): loturile care expira in curand sau au expirat, pe gestiuni, cu actiunea propusa. */
export function ExpiriesPage() {
  const [warehouse, setWarehouse] = useState<number | undefined>();
  const [days, setDays] = useState(30);
  const [kind, setKind] = useState<number | undefined>();
  const lots = useLots(undefined, warehouse, '', days);
  const rows = (lots.data ?? []).filter((r) => Number(r.qtyOnHand) !== 0 && !r.isTechnical && (kind === undefined || r.expiryKind === kind))
    .sort((a, b) => (a.daysLeft ?? 0) - (b.daysLeft ?? 0));
  const expired = rows.filter((r) => (r.daysLeft ?? 0) < 0 || r.status === LotStatus.Expired);
  const soon = rows.filter((r) => !expired.includes(r));

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.inventoryReports')} › {t('menu.expiries')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.expiries')}</h1>
          <div className="sub2">{t('inv.exp.subtitle')}</div>
        </div>
      </div>
      <div className="tiles">
        <div className="tile warn">
          <span className="tile-label">{t('inv.exp.soon', { days })}</span>
          <strong>{t('inv.exp.lots', { count: new Set(soon.map((r) => r.lotId)).size })}</strong>
          <span className="muted small">{t('inv.exp.pieces', { qty: qtyToRo(soon.reduce((s, r) => s + Number(r.qtyOnHand), 0)) })}</span>
        </div>
        <div className="tile bad">
          <span className="tile-label">{t('inv.exp.expired')}</span>
          <strong>{t('inv.exp.lots', { count: new Set(expired.map((r) => r.lotId)).size })}</strong>
          <span className="muted small">{t('inv.exp.pieces', { qty: qtyToRo(expired.reduce((s, r) => s + Number(r.qtyOnHand), 0)) })}</span>
        </div>
      </div>
      <section className="card">
        <div className="chips-row">
          <select className="chip on" aria-label={t('inv.exp.horizon')} value={days} onChange={(e) => setDays(Number(e.target.value))}>
            {[7, 15, 30, 60, 90].map((d) => <option key={d} value={d}>{t('inv.exp.horizonOption', { days: d })}</option>)}
          </select>
          <WarehouseSelect value={warehouse} onChange={setWarehouse} />
          <select className="chip" aria-label={t('inv.lot.expiryKind')} value={kind ?? ''} onChange={(e) => setKind(e.target.value === '' ? undefined : Number(e.target.value))}>
            <option value="">{t('inv.exp.allKinds')}</option>
            <option value={1}>{t('inv.lot.expiry.useBy')}</option>
            <option value={2}>{t('inv.lot.expiry.bestBefore')}</option>
          </select>
          <span className="muted small">{t('inv.exp.ordered')}</span>
        </div>
        {lots.isError && <p role="alert">{lots.error.message}</p>}
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('inv.exp.item')}</th>
              <th scope="col">{t('inv.lot.lot')}</th>
              <th scope="col">{t('inv.col.warehouse')}</th>
              <th scope="col">{t('inv.lot.expiryKind')}</th>
              <th scope="col">{t('inv.lot.expiry')}</th>
              <th scope="col" className="num">{t('inv.exp.days')}</th>
              <th scope="col" className="num">{t('inv.exp.qty')}</th>
              <th scope="col">{t('inv.exp.action')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={`${r.lotId}-${r.warehouseId}`}>
                <td><code>{r.variantCode}</code> {r.variantName}</td>
                <td><code>{r.lotCode}</code></td>
                <td><code>{r.warehouseCode}</code></td>
                <td>{t(expiryKindLabel(r.expiryKind))}</td>
                <td>{r.expiryDate == null ? '—' : isoToRo(r.expiryDate)}</td>
                <td className="num">
                  {(r.daysLeft ?? 0) < 0 || r.status === LotStatus.Expired
                    ? <span className="pill bad">{r.status === LotStatus.Expired ? t('inv.lot.status.expired') : t('inv.exp.daysAgo', { count: -(r.daysLeft ?? 0) })}</span>
                    : <strong>{r.daysLeft}</strong>}
                </td>
                <td className="num">{qtyToRo(r.qtyOnHand)}</td>
                <td>{t(proposedAction(r))}</td>
              </tr>
            ))}
            {rows.length === 0 && !lots.isFetching && <tr><td colSpan={8} className="muted">{t('inv.exp.empty')}</td></tr>}
          </tbody>
        </table>
        <div className="banner dashed">{t('inv.exp.note')}</div>
      </section>
    </div>
  );
}
