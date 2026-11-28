import { useState } from 'react';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { searchVariants, type VariantOption } from './documentsApi';
import { useWarehouses } from './inventoryApi';
import { moneyToRo, useCostLayers } from './valuationApi';

/**
 * Stocuri › Evaluare › Straturi de cost (02.05, S3 3.2; macheta s3-05): straturile FIFO, LIFO si pe loturi ale unei variante, cu intrarea care
 * le-a creat (si stratul de origine la transfer), data, cantitatile si costul, apoi ultimele consumuri.
 */
export function CostLayersPage() {
  const warehouses = useWarehouses('');
  const [warehouse, setWarehouse] = useState<number | undefined>();
  const [variant, setVariant] = useState<VariantOption | undefined>();
  const [openOnly, setOpenOnly] = useState(true);
  const [search, setSearch] = useState('');
  const [options, setOptions] = useState<VariantOption[]>([]);
  const layers = useCostLayers(variant?.id, warehouse, openOnly);

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.valuation')} › {t('menu.costLayers')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.costLayers')}</h1>
          <div className="sub2">{t('inv.layers.subtitle')}</div>
        </div>
      </div>
      <section className="card">
        <div className="chips-row">
          <input className="cell-input" placeholder={t('inv.doc.searchVariant')} aria-label={t('inv.doc.searchVariant')} value={search}
            onChange={(e) => setSearch(e.target.value)} />
          <button type="button" className="btn ghost" onClick={() => void searchVariants(search, undefined).then(setOptions, () => setOptions([]))}>
            {t('inv.doc.find')}
          </button>
          {options.map((o) => (
            <button key={o.id} type="button" className={`chip${variant?.id === o.id ? ' on' : ''}`} onClick={() => setVariant(o)}>{`${o.code} · ${o.name}`}</button>
          ))}
          <select className="chip" aria-label={t('inv.col.warehouse')} value={warehouse ?? ''}
            onChange={(e) => setWarehouse(e.target.value === '' ? undefined : Number(e.target.value))}>
            <option value="">{t('inv.movements.allWarehouses')}</option>
            {(warehouses.data?.warehouses ?? []).map((w) => <option key={w.id} value={w.id}>{`${w.code} · ${w.name}`}</option>)}
          </select>
          <button type="button" className={`chip${openOnly ? ' on' : ''}`} onClick={() => setOpenOnly(!openOnly)}>{t('inv.layers.openOnly')}</button>
        </div>
        {variant === undefined && <p className="muted">{t('inv.layers.select')}</p>}
        {layers.data !== undefined && (
          <table className="inline-table">
            <thead>
              <tr>
                <th scope="col">{t('inv.col.warehouse')}</th>
                <th scope="col">{t('inv.layers.receipt')}</th>
                <th scope="col">{t('inv.layers.date')}</th>
                <th scope="col" className="num">{t('inv.layers.qtyIn')}</th>
                <th scope="col" className="num">{t('inv.layers.remaining')}</th>
                <th scope="col" className="num">{t('inv.layers.unitCost')}</th>
                <th scope="col" className="num">{t('inv.layers.value')}</th>
              </tr>
            </thead>
            <tbody>
              {layers.data.layers.map((l) => (
                <tr key={l.id}>
                  <td>{`${l.warehouseCode}${l.depotCode ? ` · ${l.depotCode}` : ''}`}</td>
                  <td>{l.originDocument ? `${l.sourceDocument} ← ${l.originDocument}` : l.sourceDocument}{l.isNegative ? ` · ${t('inv.layers.negative')}` : ''}</td>
                  <td>{isoToRo(l.layerDate)}</td>
                  <td className="num">{l.qtyIn.replace('.', ',')}</td>
                  <td className="num"><strong>{l.qtyRemaining.replace('.', ',')}</strong></td>
                  <td className="num">{l.unitCost.replace('.', ',')}</td>
                  <td className="num">{moneyToRo(l.valueRemaining)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      {layers.data !== undefined && layers.data.consumptions.length > 0 && (
        <section className="card">
          <h2 className="form-title">{t('inv.layers.consumptions')}</h2>
          <table className="inline-table">
            <thead>
              <tr>
                <th scope="col">{t('inv.layers.date')}</th>
                <th scope="col">{t('inv.layers.outbound')}</th>
                <th scope="col" className="num">{t('inv.layers.qty')}</th>
                <th scope="col">{t('inv.layers.fromLayer')}</th>
                <th scope="col" className="num">{t('inv.layers.unitCost')}</th>
                <th scope="col" className="num">{t('inv.layers.value')}</th>
              </tr>
            </thead>
            <tbody>
              {layers.data.consumptions.map((c, index) => (
                <tr key={`${c.outbound}-${c.layerDocument}-${index}`}>
                  <td>{isoToRo(c.movementDate)}</td>
                  <td>{c.outbound}</td>
                  <td className="num">{c.qty.replace('.', ',')}</td>
                  <td>{c.layerDocument}</td>
                  <td className="num">{c.unitCost.replace('.', ',')}</td>
                  <td className="num">{moneyToRo(c.value)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </div>
  );
}
