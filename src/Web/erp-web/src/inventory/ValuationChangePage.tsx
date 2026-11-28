import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { searchVariants, type VariantOption } from './documentsApi';
import { methodLabel, ValuationMethod } from './inventoryApi';
import {
  monthStart, moneyToRo, useValuationChange, useValuationChanges, valuationApi, valuationKeys, valuationStatusLabel, ValuationStatus, type ValuationChange,
} from './valuationApi';

/**
 * Stocuri › Evaluare › Schimbare metoda (02.05, S1 4.5; macheta s1-04): documentul de schimbare a politicii contabile, cu modelele, metoda noua,
 * luna de la care se aplica si motivul; previzualizarea pe gestiuni arata ca valoarea stocului ramane aceeasi.
 */
export function ValuationChangePage() {
  const list = useValuationChanges();
  const [selected, setSelected] = useState<number | 'new' | undefined>();

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.valuation')} › {t('menu.valuationChange')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.valuationChange')}</h1>
          <div className="sub2">{t('inv.sm.subtitle')}</div>
        </div>
        <button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('inv.sm.new')}</button>
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.valuationChange')}>
          {list.data?.length === 0 && <p className="muted">{t('inv.sm.empty')}</p>}
          <table className="partner-list">
            <tbody>
              {(list.data ?? []).map((d) => (
                <tr key={d.id} className={selected === d.id ? 'selected' : undefined} onClick={() => setSelected(d.id)}>
                  <td>
                    {`SM ${d.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(valuationStatusLabel(d.status))}</span>
                    <div className="muted small">
                      {`${d.firstItemName ?? ''}${d.itemCount > 1 ? ` +${d.itemCount - 1}` : ''} · ${t(methodLabel(d.newMethod))} · ${isoToRo(d.effectiveDate)}`}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
        <section className="card detail-card">
          {selected === undefined && <p className="muted empty-detail">{t('inv.sm.select')}</p>}
          {selected === 'new' && <ChangeEditor key="new" document={undefined} onSaved={setSelected} />}
          {typeof selected === 'number' && <ChangePanel key={selected} id={selected} onSaved={setSelected} />}
        </section>
      </div>
    </div>
  );
}

function ChangePanel({ id, onSaved }: { readonly id: number; readonly onSaved: (id: number) => void }) {
  const document = useValuationChange(id);
  if (document.data === undefined) {
    return <p role="status">{document.isError ? document.error.message : t('app.loading')}</p>;
  }
  return <ChangeEditor key={document.data.rowVersion} document={document.data} onSaved={onSaved} />;
}

function ChangeEditor({ document, onSaved }: { readonly document: ValuationChange | undefined; readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const draft = document === undefined || document.status === ValuationStatus.Draft;
  const [method, setMethod] = useState<number>(document?.newMethod ?? ValuationMethod.WeightedAverage);
  const [month, setMonth] = useState((document?.effectiveDate ?? new Date().toISOString()).slice(0, 7));
  const [reason, setReason] = useState(document?.reason ?? '');
  const [items, setItems] = useState(() => (document?.items ?? []).map((i) => ({ id: i.itemId, label: `${i.itemCode} · ${i.itemName}` })));
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
  const save = () => valuationApi.saveValuationChange({
    id: document?.id ?? null, newMethod: method as ValuationChange['newMethod'], effectiveDate: monthStart(month), reason, itemIds: items.map((i) => i.id),
    rowVersion: document?.rowVersion ?? null,
  });

  return (
    <div className="node-form">
      <h2 className="form-title">{`SM ${document?.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(valuationStatusLabel(document?.status ?? 1))}</span></h2>
      {document !== undefined && <div className="muted small">{t('inv.sm.created', { name: document.createdByName ?? '', date: isoToRo(document.createdAt.slice(0, 10)) })}</div>}
      <fieldset className="form-body plain" disabled={!draft}>
        <div className="grid g3">
          <label className="field">
            <span className="label">{t('inv.sm.newMethod')}</span>
            <select className="cell-input" value={method} onChange={(e) => setMethod(Number(e.target.value))}>
              {[ValuationMethod.Fifo, ValuationMethod.WeightedAverage, ValuationMethod.Lifo, ValuationMethod.SpecificIdentification].map((m) => (
                <option key={m} value={m}>{t(methodLabel(m))}</option>
              ))}
            </select>
          </label>
          <label className="field">
            <span className="label">{t('inv.sm.from')}</span>
            <input className="cell-input" type="month" value={month} onChange={(e) => setMonth(e.target.value)} />
          </label>
          <label className="field">
            <span className="label">{t('inv.sm.posting')}</span>
            <input className="cell-input" value={t('inv.sm.noPosting')} readOnly />
          </label>
          <label className="field span2">
            <span className="label">{t('inv.sm.reason')}</span>
            <input className="cell-input" value={reason} onChange={(e) => setReason(e.target.value)} />
          </label>
        </div>
      </fieldset>
      <table className="inline-table">
        <thead>
          <tr>
            <th scope="col">{t('inv.sm.item')}</th>
            <th scope="col">{t('inv.sm.oldMethod')}</th>
            {draft && <th scope="col" />}
          </tr>
        </thead>
        <tbody>
          {items.map((i) => (
            <tr key={i.id}>
              <td>{i.label}</td>
              <td>{(() => {
                const saved = document?.items.find((x) => x.itemId === i.id);
                return saved === undefined ? '—' : `${t(methodLabel(saved.oldMethod))} → ${t(methodLabel(method))}`;
              })()}</td>
              {draft && <td><button type="button" className="btn ghost small" onClick={() => setItems(items.filter((x) => x.id !== i.id))}>✕</button></td>}
            </tr>
          ))}
        </tbody>
      </table>
      {draft && (
        <div className="chips-row">
          <input className="cell-input" placeholder={t('inv.doc.searchVariant')} aria-label={t('inv.doc.searchVariant')} value={search}
            onChange={(e) => setSearch(e.target.value)} />
          <button type="button" className="btn ghost" onClick={() => void searchVariants(search, undefined).then(setOptions, () => setOptions([]))}>
            {t('inv.doc.find')}
          </button>
          {options.filter((o, index, all) => all.findIndex((x) => x.itemId === o.itemId) === index && !items.some((i) => i.id === o.itemId)).map((o) => (
            <button key={o.itemId} type="button" className="chip" onClick={() => setItems([...items, { id: o.itemId, label: o.name }])}>{`+ ${o.name}`}</button>
          ))}
        </div>
      )}
      {document !== undefined && document.preview.length > 0 && (
        <>
          <h3 className="form-title">{t('inv.sm.preview')}</h3>
          <table className="inline-table">
            <thead>
              <tr>
                <th scope="col">{t('inv.item.variant')}</th>
                <th scope="col">{t('inv.col.warehouse')}</th>
                <th scope="col" className="num">{t('inv.sm.stock')}</th>
                <th scope="col" className="num">{t('inv.sm.layers')}</th>
                <th scope="col" className="num">{t('inv.sm.value')}</th>
                <th scope="col" className="num">{t('inv.sm.average')}</th>
              </tr>
            </thead>
            <tbody>
              {document.preview.map((p) => (
                <tr key={`${p.variantCode}-${p.warehouseCode}-${p.depotCode ?? ''}`}>
                  <td>{`${p.variantCode} · ${p.variantName}`}</td>
                  <td>{`${p.warehouseCode}${p.depotCode ? ` · ${p.depotCode}` : ''}`}</td>
                  <td className="num">{p.qty.replace('.', ',')}</td>
                  <td className="num">{p.openLayers}</td>
                  <td className="num">{moneyToRo(p.value)}</td>
                  <td className="num">{p.averageCost?.replace('.', ',') ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
      {draft && <div className="banner warn">{t('inv.sm.warning', { date: isoToRo(monthStart(month)) })}</div>}
      {draft && (
        <div className="form-footer">
          <span className="muted">{t('inv.sm.hint')}</span>
          <div className="actions">
            {document !== undefined && (
              <button type="button" className="btn ghost" onClick={() => void run(() => valuationApi.cancelValuationChange(document.id, document.rowVersion))}>
                {t('inv.doc.cancel')}
              </button>
            )}
            <button type="button" className="btn ghost" onClick={() => void run(save)}>{t('common.save')}</button>
            <button type="button" className="btn primary" disabled={document === undefined} onClick={() => void run(async () => {
              const saved = await save();
              return valuationApi.validateValuationChange(saved.id, saved.rowVersion);
            })}>
              {t('inv.doc.validate')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
