import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { VariantPicker, WarehouseSelect } from './LotPickers';
import {
  expiryKindLabel, expiryTone, lotKeys, lotsApi, LotStatus, lotStatusLabel, lotStatusTone, lotTotals, qtyToRo, useLot, useLots, type Lot,
} from './lotsApi';

/**
 * Stocuri › Stoc › Loturi (02.05, S4 3.1–3.3; macheta s4-01): loturile unei variante pe gestiuni, cu termenul, zilele ramase, starea si cantitatea;
 * fisa lotului cu CKU, atributele, stocul pe gestiuni, istoricul starii si actiunile de eliberare si blocare.
 */
export function LotsPage() {
  const [variant, setVariant] = useState<{ id: number; label: string } | undefined>();
  const [warehouse, setWarehouse] = useState<number | undefined>();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<number | undefined>();
  const lots = useLots(variant?.id, warehouse, search);
  const rows = (lots.data ?? []).filter((r) => status === undefined || r.status === status);
  const [selected, setSelected] = useState<number | undefined>();
  const totals = lotTotals(rows);

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.stock')} › {t('menu.lots')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.lots')}</h1>
          <div className="sub2">{t('inv.lot.subtitle')}</div>
        </div>
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.lots')}>
          <input className="cell-input" placeholder={t('inv.lot.search')} aria-label={t('inv.lot.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
          <div className="chips-row">
            <VariantPicker value={variant} onChange={setVariant} />
            <WarehouseSelect value={warehouse} onChange={setWarehouse} />
            <select className="chip" aria-label={t('inv.lot.statusFilter')} value={status ?? ''}
              onChange={(e) => setStatus(e.target.value === '' ? undefined : Number(e.target.value))}>
              <option value="">{t('inv.lot.allStatuses')}</option>
              {Object.values(LotStatus).map((s) => <option key={s} value={s}>{t(lotStatusLabel(s))}</option>)}
            </select>
          </div>
          {lots.isError && <p role="alert">{lots.error.message}</p>}
          {rows.length === 0 && !lots.isFetching && <p className="muted">{t('inv.lot.empty')}</p>}
          <table className="partner-list">
            <tbody>
              {rows.map((r) => {
                const tone = expiryTone(r.daysLeft);
                return (
                  <tr key={`${r.lotId}-${r.warehouseId}`} className={selected === r.lotId ? 'selected' : undefined} onClick={() => setSelected(r.lotId)}>
                    <td>
                      <code>{r.lotCode}</code>{' '}
                      {r.status === LotStatus.Available && tone !== undefined
                        ? <span className={`pill ${tone}`}>{t(tone === 'bad' ? 'inv.lot.status.expired' : 'inv.lot.expiringSoon')}</span>
                        : <span className={`pill ${lotStatusTone(r.status)}`}>{t(lotStatusLabel(r.status))}</span>}
                      <div className="muted small">
                        {[r.variantCode, r.warehouseCode, r.expiryDate == null ? t('inv.lot.noExpiry') : `${t('inv.lot.exp')} ${isoToRo(r.expiryDate)}`,
                          r.daysLeft == null ? null : t('inv.lot.daysLeft', { count: r.daysLeft })].filter(Boolean).join(' · ')}
                      </div>
                    </td>
                    <td className="num">{qtyToRo(r.qtyOnHand)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {rows.length > 0 && (
            <div className="muted small list-foot">
              {t('inv.lot.totals', { qty: qtyToRo(totals.qty), lots: totals.lots, available: qtyToRo(totals.available) })}
            </div>
          )}
        </section>
        <section className="card detail-card">
          {selected === undefined ? <p className="muted empty-detail">{t('inv.lot.select')}</p> : <LotPanel key={selected} id={selected} />}
        </section>
      </div>
    </div>
  );
}

function LotPanel({ id }: { readonly id: number }) {
  const lot = useLot(id);
  if (lot.data === undefined) {
    return <p role="status">{lot.isError ? lot.error.message : t('app.loading')}</p>;
  }
  return <LotCard key={lot.data.rowVersion} lot={lot.data} />;
}

function LotCard({ lot }: { readonly lot: Lot }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [supplierLot, setSupplierLot] = useState(lot.supplierLotCode ?? '');
  const [production, setProduction] = useState(lot.productionDate ?? '');
  const [expiry, setExpiry] = useState(lot.expiryDate ?? '');
  const [reason, setReason] = useState('');
  const changed = supplierLot !== (lot.supplierLotCode ?? '') || production !== (lot.productionDate ?? '') || expiry !== (lot.expiryDate ?? '');
  const run = async (action: () => Promise<unknown>) => {
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: lotKeys.all });
      setReason('');
      notify(t('common.saved'));
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };
  const status = (to: number) => run(() => lotsApi.changeStatus(lot.lotId, to, reason.trim() === '' ? null : reason.trim(), lot.rowVersion));
  const canRelease = lot.status === LotStatus.Quarantine || lot.status === LotStatus.Blocked;
  const canBlock = lot.status === LotStatus.Available || lot.status === LotStatus.Quarantine;

  return (
    <div className="node-form">
      <h2 className="form-title">
        <code>{lot.lotCode}</code> {t('inv.lot.lot')} <span className={`pill ${lotStatusTone(lot.status)}`}>{t(lotStatusLabel(lot.status))}</span>
        {lot.isTechnical && <span className="pill warn">{t('inv.lot.technical')}</span>}
      </h2>
      <div className="muted small">{`CKU ${lot.ckuCode} · id ${lot.ckuUid}`}</div>
      <div className="muted small">
        {[`SKU ${lot.variantCode} ${lot.variantName}`, lot.supplierName === null || lot.supplierName === undefined ? null : `${t('inv.lot.supplier')} ${lot.supplierName}`,
          lot.firstReceiptDate == null ? null : `${t('inv.lot.firstReceipt')} ${isoToRo(lot.firstReceiptDate)}`, lot.statusReason ?? null].filter(Boolean).join(' · ')}
      </div>
      <fieldset className="form-body plain" disabled={lot.isTechnical}>
        <div className="grid g4">
          <label className="field">
            <span className="label">{t('inv.lot.supplierLot')}</span>
            <input className="cell-input" value={supplierLot} onChange={(e) => setSupplierLot(e.target.value)} />
          </label>
          <label className="field">
            <span className="label">{t('inv.lot.production')}</span>
            <input className="cell-input" type="date" value={production} onChange={(e) => setProduction(e.target.value)} />
          </label>
          <label className="field">
            <span className="label">{t('inv.lot.expiry')}</span>
            <input className="cell-input" type="date" value={expiry} onChange={(e) => setExpiry(e.target.value)} />
          </label>
          <label className="field">
            <span className="label">{t('inv.lot.expiryKind')}</span>
            <input className="cell-input" value={t(expiryKindLabel(lot.expiryKind))} readOnly />
          </label>
        </div>
      </fieldset>
      <table className="inline-table">
        <thead>
          <tr>
            <th scope="col">{t('inv.col.warehouse')}</th>
            <th scope="col" className="num">{t('inv.lot.inStock')}</th>
            <th scope="col" className="num">{t('inv.lot.available')}</th>
            <th scope="col">{t('inv.lot.why')}</th>
          </tr>
        </thead>
        <tbody>
          {lot.stock.map((s) => (
            <tr key={s.warehouseId}>
              <td><code>{s.warehouseCode}</code></td>
              <td className="num"><strong>{qtyToRo(s.qtyOnHand)}</strong></td>
              <td className="num">{lot.status === LotStatus.Available ? qtyToRo(s.qtyOnHand) : '0'}</td>
              <td className="muted small">{lot.status === LotStatus.Available ? '' : t('inv.lot.notAvailableBecause', { status: t(lotStatusLabel(lot.status)) })}</td>
            </tr>
          ))}
          {lot.stock.length === 0 && <tr><td colSpan={4} className="muted">{t('inv.lot.noStock')}</td></tr>}
        </tbody>
      </table>
      <h3 className="section-title">{t('inv.lot.history')}</h3>
      <table className="inline-table">
        <thead>
          <tr>
            <th scope="col">{t('inv.lot.when')}</th>
            <th scope="col">{t('inv.lot.from')}</th>
            <th scope="col">{t('inv.lot.to')}</th>
            <th scope="col">{t('inv.lot.reason')}</th>
            <th scope="col">{t('inv.lot.who')}</th>
          </tr>
        </thead>
        <tbody>
          {lot.history.map((h, i) => (
            <tr key={i}>
              <td>{`${isoToRo(h.changedAt.slice(0, 10))}, ${h.changedAt.slice(11, 16)}`}</td>
              <td>{h.fromStatus == null ? '—' : <span className={`pill ${lotStatusTone(h.fromStatus)}`}>{t(lotStatusLabel(h.fromStatus))}</span>}</td>
              <td><span className={`pill ${lotStatusTone(h.toStatus)}`}>{t(lotStatusLabel(h.toStatus))}</span></td>
              <td>{[h.reason, h.sourceDocumentType === null || h.sourceDocumentType === undefined ? null : `${h.sourceDocumentType} ${h.sourceDocumentId ?? ''}`].filter(Boolean).join(' · ')}</td>
              <td>{h.changedByName ?? t('inv.lot.system')}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {!lot.isTechnical && (
        <div className="form-footer">
          <input className="cell-input" placeholder={t('inv.lot.reasonPlaceholder')} aria-label={t('inv.lot.reason')} value={reason}
            onChange={(e) => setReason(e.target.value)} />
          <div className="actions">
            {changed && (
              <button type="button" className="btn ghost" disabled={reason.trim() === ''} onClick={() => void run(() => lotsApi.update({
                id: lot.lotId, supplierLotCode: supplierLot.trim() === '' ? null : supplierLot.trim(), countryOfOriginCode: lot.countryOfOriginCode ?? null,
                productionDate: production === '' ? null : production, expiryDate: expiry === '' ? null : expiry, reason: reason.trim(), rowVersion: lot.rowVersion,
              }))}>
                {t('inv.lot.saveAttributes')}
              </button>
            )}
            {canBlock && <button type="button" className="btn ghost" disabled={reason.trim() === ''} onClick={() => void status(LotStatus.Blocked)}>{t('inv.lot.block')}</button>}
            {canRelease && (
              <button type="button" className="btn primary" onClick={() => void status(LotStatus.Available)}>
                {t(lot.status === LotStatus.Quarantine ? 'inv.lot.release' : 'inv.lot.unblock')}
              </button>
            )}
          </div>
        </div>
      )}
      {!lot.isTechnical && <div className="muted small">{t('inv.lot.hint')}</div>}
    </div>
  );
}
