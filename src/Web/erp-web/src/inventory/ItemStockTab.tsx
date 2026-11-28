import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import {
  inventoryApi, inventoryKeys, methodLabel, quantityForApi, quantityForInput, quantityPattern, trackingLabel, useItemStock, ValuationMethod, type ItemStockTab as ItemStockTabData,
} from './inventoryApi';

const methods = [ValuationMethod.Fifo, ValuationMethod.WeightedAverage, ValuationMethod.Lifo, ValuationMethod.SpecificIdentification];
const trackings = [0, 1, 2, 3, 4];

const validQuantity = (value: string) => value.trim() === '' || quantityPattern.test(value.trim());

/**
 * Tab-ul Stoc pe fisa articolului (02.05, S1 3.4; macheta s1-03), pentru firma curenta: metoda de evaluare (aleasa la prima configurare,
 * apoi doar prin documentul de schimbare), urmarirea pe lot sau serie, gestiunea implicita, pragurile pe firma si pe gestiuni.
 */
export function ItemStockTab({ itemId, active, canManage }: { readonly itemId: number; readonly active: boolean; readonly canManage: boolean }) {
  const stock = useItemStock(itemId, active);
  if (stock.data === undefined) {
    return <p role="status">{stock.isError ? stock.error.message : t('app.loading')}</p>;
  }
  // Formularul se reinitializeaza din server dupa fiecare salvare (versiune noua), fara efecte care copiaza starea.
  return <ItemStockForm key={stock.data.company.rowVersion ?? 'new'} itemId={itemId} data={stock.data} canManage={canManage} />;
}

function ItemStockForm({ itemId, data, canManage }: { readonly itemId: number; readonly data: ItemStockTabData; readonly canManage: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const company = data.company;
  const [method, setMethod] = useState<number>(company.valuationMethod);
  const [tracking, setTracking] = useState<number>(company.trackingMode);
  const [warehouse, setWarehouse] = useState(company.defaultWarehouseId == null ? '' : String(company.defaultWarehouseId));
  const [min, setMin] = useState(quantityForInput(company.minQty));
  const [max, setMax] = useState(quantityForInput(company.maxQty));
  const [blocked, setBlocked] = useState(company.isBlockedForIssue);
  const [rowWarehouse, setRowWarehouse] = useState('');
  const [rowVariant, setRowVariant] = useState('');
  const [rowMin, setRowMin] = useState('');
  const [rowMax, setRowMax] = useState('');

  const configured = company.id != null;
  const run = async (action: () => Promise<unknown>, after?: () => void) => {
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: inventoryKeys.itemStock(itemId) });
      notify(t('common.saved'));
      after?.();
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };

  return (
    <div className="form-body plain">
      <fieldset className="grid g3" disabled={!canManage}>
        <label className="field">
          <span className="label">{t('inv.item.method')}</span>
          <select className="cell-input" value={method} disabled={configured} onChange={(e) => setMethod(Number(e.target.value))}>
            {methods.map((m) => <option key={m} value={m}>{t(methodLabel(m))}</option>)}
          </select>
          {configured && <span className="muted small">{t('inv.item.methodLocked')}</span>}
        </label>
        <label className="field">
          <span className="label">{t('inv.item.tracking')}</span>
          <select className="cell-input" value={tracking} onChange={(e) => setTracking(Number(e.target.value))}>
            {trackings.map((m) => <option key={m} value={m}>{t(trackingLabel(m))}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('inv.item.defaultWarehouse')}</span>
          <select className="cell-input" value={warehouse} onChange={(e) => setWarehouse(e.target.value)}>
            <option value="">{t('common.none')}</option>
            {data.defaultWarehouses.map((w) => <option key={w.id} value={w.id}>{`${w.code} · ${w.name}`}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('inv.item.min')}</span>
          <input className="cell-input num" value={min} aria-invalid={!validQuantity(min)} onChange={(e) => setMin(e.target.value)} />
        </label>
        <label className="field">
          <span className="label">{t('inv.item.max')}</span>
          <input className="cell-input num" value={max} aria-invalid={!validQuantity(max)} onChange={(e) => setMax(e.target.value)} />
        </label>
        <label className="check">
          <input type="checkbox" checked={blocked} onChange={(e) => setBlocked(e.target.checked)} /> {t('inv.item.blockedForIssue')}
        </label>
      </fieldset>
      {canManage && (
        <div className="actions">
          <button type="button" className="btn primary" disabled={!validQuantity(min) || !validQuantity(max)}
            onClick={() => void run(() => inventoryApi.saveItemStock({
              itemId, defaultWarehouseId: warehouse === '' ? null : Number(warehouse), valuationMethod: method, trackingMode: tracking,
              minQty: quantityForApi(min), maxQty: quantityForApi(max), replenishmentMethod: null, isBlockedForIssue: blocked,
              rowVersion: company.rowVersion ?? null,
            }))}>
            {t('common.save')}
          </button>
        </div>
      )}

      <h3 className="section">{t('inv.item.perWarehouse')}</h3>
      {data.warehouses.length === 0 && <p className="muted">{t('inv.item.noThresholds')}</p>}
      {data.warehouses.length > 0 && (
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('inv.col.warehouse')}</th>
              <th scope="col">{t('inv.item.variant')}</th>
              <th scope="col">{t('inv.item.min')}</th>
              <th scope="col">{t('inv.item.max')}</th>
              <th scope="col">{t('inv.item.blocks')}</th>
              {canManage && <th scope="col"><span className="sr-only">{t('inv.depots.remove')}</span></th>}
            </tr>
          </thead>
          <tbody>
            {data.warehouses.map((w) => (
              <tr key={w.id}>
                <td>{w.warehouseCode}</td>
                <td>{w.variantCode ?? t('inv.item.allVariants')}</td>
                <td className="num">{quantityForInput(w.minQty)}</td>
                <td className="num">{quantityForInput(w.maxQty)}</td>
                <td className="small">
                  {[w.isBlockedForIssue ? t('inv.item.noIssue') : '', w.isBlockedForReceipt ? t('inv.item.noReceipt') : ''].filter(Boolean).join(' · ')}
                </td>
                {canManage && (
                  <td>
                    <button type="button" className="btn ghost small" onClick={() => void run(() => inventoryApi.deleteItemWarehouse(w.id, w.rowVersion))}>
                      {t('inv.depots.remove')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {canManage && (
        <div className="chips-row">
          <select className="chip" aria-label={t('inv.col.warehouse')} value={rowWarehouse} onChange={(e) => setRowWarehouse(e.target.value)}>
            <option value="">{t('inv.col.warehouse')}</option>
            {data.defaultWarehouses.map((w) => <option key={w.id} value={w.id}>{w.code}</option>)}
          </select>
          <select className="chip" aria-label={t('inv.item.variant')} value={rowVariant} onChange={(e) => setRowVariant(e.target.value)}>
            <option value="">{t('inv.item.allVariants')}</option>
            {data.variants.map((v) => <option key={v.id} value={v.id}>{v.code}</option>)}
          </select>
          <input className="cell-input num" placeholder={t('inv.item.min')} aria-label={t('inv.item.min')} value={rowMin} onChange={(e) => setRowMin(e.target.value)} />
          <input className="cell-input num" placeholder={t('inv.item.max')} aria-label={t('inv.item.max')} value={rowMax} onChange={(e) => setRowMax(e.target.value)} />
          <button type="button" className="btn ghost" disabled={rowWarehouse === '' || !validQuantity(rowMin) || !validQuantity(rowMax)}
            onClick={() => void run(() => inventoryApi.saveItemWarehouse({
              id: null, warehouseId: Number(rowWarehouse), itemId, itemVariantId: rowVariant === '' ? null : Number(rowVariant), minQty: quantityForApi(rowMin),
              maxQty: quantityForApi(rowMax), reorderQty: null, replenishmentMethod: null, isBlockedForIssue: false, isBlockedForReceipt: false,
              isListed: true, rowVersion: null,
            }), () => {
              setRowMin('');
              setRowMax('');
            })}>
            {t('inv.item.addThreshold')}
          </button>
        </div>
      )}
      <p className="muted small">{t('inv.item.hint')}</p>
    </div>
  );
}
