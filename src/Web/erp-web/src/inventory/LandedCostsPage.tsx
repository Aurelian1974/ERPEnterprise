import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { searchVariants, useStockMovements, type VariantOption } from './documentsApi';
import {
  allocationBasisLabel, landedSourceLabel, moneyForApi, moneyToRo, useLandedCost, useLandedCosts, valuationApi, valuationKeys, valuationStatusLabel,
  ValuationStatus, type LandedCost,
} from './valuationApi';

/**
 * Stocuri › Evaluare › Costuri adaugate (02.05, S3 3.3; macheta s3-01): documentele CA; documentul cu tipul, baza de repartizare si intrarile
 * tinta, suma repartizata pe fiecare si, dupa validare, partea ramasa pe stoc si cea dusa pe iesirile deja facute.
 */
export function LandedCostsPage() {
  const [status, setStatus] = useState<number | undefined>();
  const list = useLandedCosts(status);
  const [selected, setSelected] = useState<number | 'new' | undefined>();

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.valuation')} › {t('menu.landedCosts')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.landedCosts')}</h1>
          <div className="sub2">{t('inv.landed.subtitle')}</div>
        </div>
        <button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('inv.landed.new')}</button>
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.landedCosts')}>
          <div className="chips-row">
            {[undefined, ValuationStatus.Draft, ValuationStatus.Validated].map((s) => (
              <button key={s ?? 0} type="button" className={`chip${status === s ? ' on' : ''}`} onClick={() => setStatus(s)}>
                {s === undefined ? t('inv.filter.all') : t(valuationStatusLabel(s))}
              </button>
            ))}
          </div>
          {list.data === undefined && <p role="status">{list.isError ? t('app.loadError') : t('app.loading')}</p>}
          {list.data?.length === 0 && <p className="muted">{t('inv.landed.empty')}</p>}
          <table className="partner-list">
            <tbody>
              {(list.data ?? []).map((d) => (
                <tr key={d.id} className={selected === d.id ? 'selected' : undefined} onClick={() => setSelected(d.id)}>
                  <td>
                    {`CA ${d.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(valuationStatusLabel(d.status))}</span>
                    <div className="muted small">{`${isoToRo(d.documentDate)} · ${t(landedSourceLabel(d.sourceKind))} · ${t(allocationBasisLabel(d.allocationBasis))}`}</div>
                  </td>
                  <td className="num">{moneyToRo(d.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
        <section className="card detail-card">
          {selected === undefined && <p className="muted empty-detail">{t('inv.landed.select')}</p>}
          {selected === 'new' && <LandedCostEditor key="new" document={undefined} onSaved={setSelected} />}
          {typeof selected === 'number' && <LandedCostPanel key={selected} id={selected} onSaved={setSelected} />}
        </section>
      </div>
    </div>
  );
}

function LandedCostPanel({ id, onSaved }: { readonly id: number; readonly onSaved: (id: number) => void }) {
  const document = useLandedCost(id);
  if (document.data === undefined) {
    return <p role="status">{document.isError ? document.error.message : t('app.loading')}</p>;
  }
  return <LandedCostEditor key={document.data.rowVersion} document={document.data} onSaved={onSaved} />;
}

interface Target {
  readonly movementId: number;
  readonly label: string;
  readonly manual: string;
  readonly allocated?: string;
}

function LandedCostEditor({ document, onSaved }: { readonly document: LandedCost | undefined; readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const draft = document === undefined || document.status === ValuationStatus.Draft;
  const [sourceKind, setSourceKind] = useState(document?.sourceKind ?? 1);
  const [basis, setBasis] = useState(document?.allocationBasis ?? 1);
  const [date, setDate] = useState(document?.accountingDate ?? new Date().toISOString().slice(0, 10));
  const [amount, setAmount] = useState(document?.amount.replace('.', ',') ?? '');
  const [notes, setNotes] = useState(document?.notes ?? '');
  const [targets, setTargets] = useState<Target[]>(() => (document?.lines ?? []).map((l) => ({
    movementId: l.movementId, label: `${l.sourceDocumentType} ${l.sourceDocumentNumber ?? ''} · ${l.variantCode} · ${l.warehouseCode}`,
    manual: l.manualAmount?.replace('.', ',') ?? '', allocated: l.allocatedAmount,
  })));
  const [search, setSearch] = useState('');
  const [options, setOptions] = useState<VariantOption[]>([]);
  const [variant, setVariant] = useState<VariantOption | undefined>();
  const receipts = useStockMovements(undefined, variant?.id, undefined);

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
  const save = () => {
    const value = moneyForApi(amount);
    if (value === null) {
      throw new Error(t('inv.landed.amountInvalid'));
    }
    return valuationApi.saveLandedCost({
      id: document?.id ?? null, documentDate: date, accountingDate: date, sourceKind, supplierInvoiceId: document?.supplierInvoiceId ?? null,
      partnerId: document?.partnerId ?? null, amount: value, allocationBasis: basis, notes: notes.trim() === '' ? null : notes,
      lines: targets.map((x) => ({ movementId: x.movementId, manualAmount: basis === 5 ? moneyForApi(x.manual) : null })), rowVersion: document?.rowVersion ?? null,
    });
  };

  return (
    <div className="node-form">
      <h2 className="form-title">
        {`CA ${document?.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(valuationStatusLabel(document?.status ?? 1))}</span>
      </h2>
      <fieldset className="form-body plain" disabled={!draft}>
        <div className="grid g3">
          <label className="field">
            <span className="label">{t('inv.landed.kind')}</span>
            <select className="cell-input" value={sourceKind} onChange={(e) => setSourceKind(Number(e.target.value))}>
              {[1, 2, 3].map((k) => <option key={k} value={k}>{t(landedSourceLabel(k))}</option>)}
            </select>
          </label>
          <label className="field">
            <span className="label">{t('inv.landed.basis')}</span>
            <select className="cell-input" value={basis} onChange={(e) => setBasis(Number(e.target.value))}>
              {[1, 2, 3, 4, 5].map((b) => <option key={b} value={b}>{t(allocationBasisLabel(b))}</option>)}
            </select>
          </label>
          <label className="field">
            <span className="label">{t('inv.landed.accountingDate')}</span>
            <input className="cell-input" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </label>
          <label className="field">
            <span className="label">{t('inv.landed.amount')}</span>
            <input className="cell-input num" value={amount} onChange={(e) => setAmount(e.target.value)} />
          </label>
          <label className="field span2">
            <span className="label">{t('inv.handover.notes')}</span>
            <input className="cell-input" value={notes} onChange={(e) => setNotes(e.target.value)} />
          </label>
        </div>
      </fieldset>
      <table className="inline-table">
        <thead>
          <tr>
            <th scope="col">{t('inv.landed.receipt')}</th>
            {basis === 5 && <th scope="col">{t('inv.landed.manual')}</th>}
            <th scope="col" className="num">{t('inv.landed.allocated')}</th>
            {draft && <th scope="col" />}
          </tr>
        </thead>
        <tbody>
          {targets.map((x, index) => (
            <tr key={x.movementId}>
              <td>{x.label}</td>
              {basis === 5 && (
                <td>
                  <input className="cell-input num" value={x.manual} disabled={!draft} aria-label={t('inv.landed.manual')}
                    onChange={(e) => setTargets(targets.map((r, i) => (i === index ? { ...r, manual: e.target.value } : r)))} />
                </td>
              )}
              <td className="num">{moneyToRo(x.allocated)}</td>
              {draft && (
                <td><button type="button" className="btn ghost small" onClick={() => setTargets(targets.filter((_, i) => i !== index))}>✕</button></td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
      {document?.status === ValuationStatus.Validated && (
        <div className="banner dashed">{t('inv.landed.split', { stock: moneyToRo(document.stockAmount), expense: moneyToRo(document.expenseAmount) })}</div>
      )}
      {draft && (
        <div className="chips-row">
          <input className="cell-input" placeholder={t('inv.doc.searchVariant')} aria-label={t('inv.doc.searchVariant')} value={search}
            onChange={(e) => setSearch(e.target.value)} />
          <button type="button" className="btn ghost" onClick={() => void searchVariants(search, undefined).then(setOptions, () => setOptions([]))}>
            {t('inv.doc.find')}
          </button>
          {options.map((o) => (
            <button key={o.id} type="button" className={`chip${variant?.id === o.id ? ' on' : ''}`} onClick={() => setVariant(o)}>{`${o.code} · ${o.name}`}</button>
          ))}
        </div>
      )}
      {draft && variant !== undefined && (
        <ul className="plain-list">
          {(receipts.data ?? []).filter((m) => !m.qtyBase.startsWith('-') && m.reversalOfId == null && !targets.some((x) => x.movementId === m.id)).map((m) => (
            <li key={m.id}>
              <button type="button" className="btn ghost small" onClick={() => setTargets([...targets, {
                movementId: m.id, label: `${m.sourceDocumentType} ${m.sourceDocumentNumber ?? ''} · ${m.variantCode} · ${m.warehouseCode}`, manual: '',
              }])}>
                {`+ ${isoToRo(m.movementDate)} · ${m.sourceDocumentType} ${m.sourceDocumentNumber ?? ''} · ${m.warehouseCode} · ${m.qtyBase.replace('.', ',')} · ${moneyToRo(m.costAmount)}`}
              </button>
            </li>
          ))}
        </ul>
      )}
      {draft && (
        <div className="form-footer">
          <span className="muted">{t('inv.landed.hint')}</span>
          <div className="actions">
            {document !== undefined && (
              <button type="button" className="btn ghost" onClick={() => void run(() => valuationApi.cancelLandedCost(document.id, document.rowVersion))}>
                {t('inv.doc.cancel')}
              </button>
            )}
            <button type="button" className="btn ghost" onClick={() => void run(save)}>{t('common.save')}</button>
            <button type="button" className="btn primary" onClick={() => void run(async () => {
              const saved = await save();
              return valuationApi.validateLandedCost(saved.id, saved.rowVersion);
            })}>
              {t('inv.doc.validate')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
