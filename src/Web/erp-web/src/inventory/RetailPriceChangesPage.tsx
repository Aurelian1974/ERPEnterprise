import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { searchVariants, type VariantOption } from './documentsApi';
import { quantityForApi, useWarehouses, ValuationBasis } from './inventoryApi';
import {
  moneyToRo, useRetailPriceChange, useRetailPriceChanges, valuationApi, valuationKeys, valuationStatusLabel, ValuationStatus, type RetailPriceChange,
} from './valuationApi';

/**
 * Stocuri › Evaluare › Modificari de pret (02.05, S3 3.7; macheta s3-04): documentele MP ale gestiunilor la pret cu amanuntul; variantele cu
 * pretul nou, iar dupa validare stocul, pretul vechi si diferenta cu TVA-ul inclus (371 = 378 + 4428).
 */
export function RetailPriceChangesPage() {
  const warehouses = useWarehouses('');
  const stores = (warehouses.data?.warehouses ?? []).filter((w) => w.valuationBasis === ValuationBasis.RetailPrice && w.isActive);
  const [warehouse, setWarehouse] = useState<number | undefined>();
  const list = useRetailPriceChanges(warehouse);
  const [selected, setSelected] = useState<number | 'new' | undefined>();

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.valuation')} › {t('menu.retailPriceChanges')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.retailPriceChanges')}</h1>
          <div className="sub2">{t('inv.mp.subtitle')}</div>
        </div>
        <button type="button" className="btn primary" disabled={stores.length === 0} onClick={() => setSelected('new')}>{t('inv.mp.new')}</button>
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.retailPriceChanges')}>
          <div className="chips-row">
            <select className="chip" aria-label={t('inv.col.warehouse')} value={warehouse ?? ''}
              onChange={(e) => setWarehouse(e.target.value === '' ? undefined : Number(e.target.value))}>
              <option value="">{t('inv.movements.allWarehouses')}</option>
              {stores.map((w) => <option key={w.id} value={w.id}>{`${w.code} · ${w.name}`}</option>)}
            </select>
          </div>
          {list.data?.length === 0 && <p className="muted">{t('inv.mp.empty')}</p>}
          <table className="partner-list">
            <tbody>
              {(list.data ?? []).map((d) => (
                <tr key={d.id} className={selected === d.id ? 'selected' : undefined} onClick={() => setSelected(d.id)}>
                  <td>
                    {`MP ${d.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(valuationStatusLabel(d.status))}</span>
                    <div className="muted small">{`${d.warehouseCode} · ${isoToRo(d.documentDate)} · ${t('inv.mp.lines', { count: d.lineCount })}`}</div>
                  </td>
                  <td className="num">{moneyToRo(d.diffAmount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
        <section className="card detail-card">
          {selected === undefined && <p className="muted empty-detail">{t('inv.mp.select')}</p>}
          {selected === 'new' && <PriceChangeEditor key="new" document={undefined} stores={stores} onSaved={setSelected} />}
          {typeof selected === 'number' && <PriceChangePanel key={selected} id={selected} stores={stores} onSaved={setSelected} />}
        </section>
      </div>
    </div>
  );
}

type Store = { readonly id: number; readonly code: string; readonly name: string };

function PriceChangePanel({ id, stores, onSaved }: { readonly id: number; readonly stores: readonly Store[]; readonly onSaved: (id: number) => void }) {
  const document = useRetailPriceChange(id);
  if (document.data === undefined) {
    return <p role="status">{document.isError ? document.error.message : t('app.loading')}</p>;
  }
  return <PriceChangeEditor key={document.data.rowVersion} document={document.data} stores={stores} onSaved={onSaved} />;
}

function PriceChangeEditor({ document, stores, onSaved }: {
  readonly document: RetailPriceChange | undefined; readonly stores: readonly Store[]; readonly onSaved: (id: number) => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const draft = document === undefined || document.status === ValuationStatus.Draft;
  const [warehouse, setWarehouse] = useState(document?.warehouseId ?? stores[0]?.id ?? 0);
  const [date, setDate] = useState(document?.documentDate ?? new Date().toISOString().slice(0, 10));
  const [lines, setLines] = useState(() => (document?.lines ?? []).map((l) => ({ itemVariantId: l.itemVariantId, label: `${l.variantCode} · ${l.variantName}`,
    price: l.newPrice.replace('.', ',') })));
  const [search, setSearch] = useState('');
  const [options, setOptions] = useState<VariantOption[]>([]);
  const run = async (action: () => Promise<{ id: number }>) => {
    try {
      const saved = await action();
      await queryClient.invalidateQueries({ queryKey: valuationKeys.all });
      notify(t('common.saved'));
      onSaved(saved.id);
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };
  const save = () => valuationApi.savePriceChange({
    id: document?.id ?? null, warehouseId: warehouse, documentDate: date, notes: null,
    lines: lines.map((l) => ({ itemVariantId: l.itemVariantId, newPrice: quantityForApi(l.price) ?? '' })), rowVersion: document?.rowVersion ?? null,
  });

  return (
    <div className="node-form">
      <h2 className="form-title">{`MP ${document?.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(valuationStatusLabel(document?.status ?? 1))}</span></h2>
      <fieldset className="form-body plain" disabled={!draft}>
        <div className="grid g3">
          <label className="field">
            <span className="label">{t('inv.col.warehouse')}</span>
            <select className="cell-input" value={warehouse} onChange={(e) => setWarehouse(Number(e.target.value))}>
              {stores.map((w) => <option key={w.id} value={w.id}>{`${w.code} · ${w.name}`}</option>)}
            </select>
          </label>
          <label className="field">
            <span className="label">{t('inv.doc.date')}</span>
            <input className="cell-input" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </label>
        </div>
      </fieldset>
      <table className="inline-table">
        <thead>
          <tr>
            <th scope="col">{t('inv.item.variant')}</th>
            <th scope="col" className="num">{t('inv.mp.qty')}</th>
            <th scope="col" className="num">{t('inv.mp.oldPrice')}</th>
            <th scope="col" className="num">{t('inv.mp.newPrice')}</th>
            <th scope="col" className="num">{t('inv.mp.diff')}</th>
            <th scope="col" className="num">{t('inv.mp.diffVat')}</th>
          </tr>
        </thead>
        <tbody>
          {lines.map((l, index) => {
            const saved = document?.lines.find((x) => x.itemVariantId === l.itemVariantId);
            return (
              <tr key={l.itemVariantId}>
                <td>{l.label}</td>
                <td className="num">{saved?.qty?.replace('.', ',') ?? '—'}</td>
                <td className="num">{saved?.oldPrice?.replace('.', ',') ?? '—'}</td>
                <td>
                  <input className="cell-input num" value={l.price} disabled={!draft} aria-label={t('inv.mp.newPrice')}
                    onChange={(e) => setLines(lines.map((x, i) => (i === index ? { ...x, price: e.target.value } : x)))} />
                </td>
                <td className="num">{moneyToRo(saved?.diffAmount)}</td>
                <td className="num">{moneyToRo(saved?.diffVatAmount)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {draft && (
        <div className="chips-row">
          <input className="cell-input" placeholder={t('inv.doc.searchVariant')} aria-label={t('inv.doc.searchVariant')} value={search}
            onChange={(e) => setSearch(e.target.value)} />
          <button type="button" className="btn ghost" onClick={() => void searchVariants(search, undefined).then(setOptions, () => setOptions([]))}>
            {t('inv.doc.find')}
          </button>
          {options.filter((o) => !lines.some((l) => l.itemVariantId === o.id)).map((o) => (
            <button key={o.id} type="button" className="chip" onClick={() => setLines([...lines, { itemVariantId: o.id, label: `${o.code} · ${o.name}`, price: '' }])}>
              {`+ ${o.code} · ${o.name}`}
            </button>
          ))}
        </div>
      )}
      {document?.status === ValuationStatus.Validated && (
        <div className="banner dashed">{t('inv.mp.posting', { diff: moneyToRo(document.diffAmount), vat: moneyToRo(document.diffVatAmount) })}</div>
      )}
      {draft && (
        <div className="form-footer">
          <span className="muted">{t('inv.mp.hint')}</span>
          <div className="actions">
            {document !== undefined && (
              <button type="button" className="btn ghost" onClick={() => void run(() => valuationApi.cancelPriceChange(document.id, document.rowVersion))}>
                {t('inv.doc.cancel')}
              </button>
            )}
            <button type="button" className="btn ghost" onClick={() => void run(save)}>{t('common.save')}</button>
            <button type="button" className="btn primary" onClick={() => void run(async () => {
              const saved = await save();
              return valuationApi.validatePriceChange(saved.id, saved.rowVersion);
            })}>
              {t('inv.doc.validate')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
