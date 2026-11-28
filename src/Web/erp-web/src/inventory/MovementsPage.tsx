import { useState } from 'react';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { searchVariants, useStockMovements, type VariantOption } from './documentsApi';
import { useWarehouses } from './inventoryApi';

/**
 * Stocuri › Registrul de miscari (02.05, S2 10; macheta s2-05): miscarile unei gestiuni sau variante in ordinea inregistrarii, cu sursa,
 * cantitatea, costul si, cand sunt alese gestiunea si varianta, soldul dupa fiecare miscare; pagina urmatoare continua dupa ultima miscare.
 */
export function MovementsPage() {
  const warehouses = useWarehouses('');
  const [warehouse, setWarehouse] = useState<number | undefined>();
  const [variant, setVariant] = useState<VariantOption | undefined>();
  const [search, setSearch] = useState('');
  const [options, setOptions] = useState<VariantOption[]>([]);
  const [after, setAfter] = useState<number | undefined>();
  const movements = useStockMovements(warehouse, variant?.id, after);
  const rows = movements.data ?? [];

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.stock')} › {t('menu.movements')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.movements')}</h1>
          <div className="sub2">{t('inv.movements.subtitle')}</div>
        </div>
      </div>
      <section className="card">
        <div className="chips-row">
          <select className="chip" aria-label={t('inv.col.warehouse')} value={warehouse ?? ''} onChange={(e) => {
            setWarehouse(e.target.value === '' ? undefined : Number(e.target.value));
            setAfter(undefined);
          }}>
            <option value="">{t('inv.movements.allWarehouses')}</option>
            {(warehouses.data?.warehouses ?? []).map((w) => <option key={w.id} value={w.id}>{`${w.code} · ${w.name}`}</option>)}
          </select>
          <input className="cell-input" placeholder={t('inv.doc.searchVariant')} aria-label={t('inv.doc.searchVariant')} value={search}
            onChange={(e) => setSearch(e.target.value)} />
          <button type="button" className="btn ghost" onClick={() => void searchVariants(search, warehouse).then(setOptions, () => setOptions([]))}>
            {t('inv.doc.find')}
          </button>
          {variant !== undefined && (
            <button type="button" className="chip on" onClick={() => {
              setVariant(undefined);
              setAfter(undefined);
            }}>
              {`${variant.code} ✕`}
            </button>
          )}
          {options.map((o) => (
            <button key={o.id} type="button" className="chip" onClick={() => {
              setVariant(o);
              setOptions([]);
              setAfter(undefined);
            }}>
              {`${o.code} · ${o.name}`}
            </button>
          ))}
        </div>
        {movements.data === undefined && <p role="status">{movements.isError ? t('app.loadError') : t('app.loading')}</p>}
        {movements.data !== undefined && rows.length === 0 && <p className="muted">{t('inv.movements.empty')}</p>}
        {rows.length > 0 && (
          <table className="inline-table" aria-busy={movements.isFetching}>
            <thead>
              <tr>
                <th scope="col">{t('inv.doc.date')}</th>
                <th scope="col">{t('inv.col.warehouse')}</th>
                <th scope="col">{t('inv.item.variant')}</th>
                <th scope="col">{t('inv.movements.type')}</th>
                <th scope="col">{t('inv.movements.source')}</th>
                <th scope="col">{t('inv.doc.qty')}</th>
                <th scope="col">{t('inv.doc.unitCost')}</th>
                <th scope="col">{t('inv.doc.cost')}</th>
                {warehouse !== undefined && variant !== undefined && <th scope="col">{t('inv.movements.balance')}</th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((m) => (
                <tr key={m.id} className={m.isForcedNegative ? 'inactive' : ''}>
                  <td>{isoToRo(m.movementDate)}</td>
                  <td>{m.warehouseCode}</td>
                  <td>{m.variantCode}</td>
                  <td className="small">{[m.movementTypeCode, m.reasonCode].filter(Boolean).join(' · ')}{m.reversalOfId != null ? ` · ${t('inv.movements.reversal')}` : ''}</td>
                  <td className="small">{`${m.sourceDocumentType} ${m.sourceDocumentNumber ?? m.sourceDocumentId}`}</td>
                  <td className="num">{m.qtyBase.replace('.', ',')}</td>
                  <td className="num">{m.unitCost.replace('.', ',')}</td>
                  <td className="num">{m.costAmount.replace('.', ',')}</td>
                  {warehouse !== undefined && variant !== undefined && <td className="num">{(m.balanceQty ?? '').replace('.', ',')}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <div className="actions">
          {after !== undefined && <button type="button" className="btn ghost" onClick={() => setAfter(undefined)}>{t('inv.movements.first')}</button>}
          {rows.length === 200 && (
            <button type="button" className="btn ghost" onClick={() => setAfter(rows[rows.length - 1]?.postingSeq)}>{t('inv.movements.next')}</button>
          )}
        </div>
      </section>
    </div>
  );
}
