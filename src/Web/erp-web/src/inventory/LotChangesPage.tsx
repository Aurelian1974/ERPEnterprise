import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { VariantPicker, WarehouseSelect } from './LotPickers';
import { quantityForApi, quantityForInput } from './inventoryApi';
import { lotKeys, lotsApi, qtyToRo, useLotChange, useLotChanges, useLots, type LotChange } from './lotsApi';
import { valuationStatusLabel, ValuationStatus } from './valuationApi';

interface LineDraft {
  readonly itemVariantId: number;
  readonly variantLabel: string;
  readonly fromLotId: number | undefined;
  readonly toLotId: number | undefined;
  readonly newLotCode: string;
  readonly newExpiryDate: string;
  readonly qty: string;
}

/** O linie SL e completa cand are lotul sursa, destinatia (lot existent diferit sau cod nou) si cantitatea pozitiva. */
export function lineComplete(l: Pick<LineDraft, 'fromLotId' | 'toLotId' | 'newLotCode' | 'qty'>): boolean {
  const qty = Number(quantityForApi(l.qty) ?? '0');
  const target = l.toLotId !== undefined ? l.toLotId !== l.fromLotId : l.newLotCode.trim() !== '';
  return l.fromLotId !== undefined && target && qty > 0;
}

/**
 * Stocuri › Stoc › Schimbarea lotului (02.05, S4 3.7): documentul SL muta cantitatea de pe un lot pe altul in aceeasi gestiune (corectarea lotului
 * NEIDENTIFICAT dupa un bon POS sau a unei etichete gresite), la cost si fara nota contabila.
 */
export function LotChangesPage() {
  const list = useLotChanges();
  const [selected, setSelected] = useState<number | 'new' | undefined>();
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.stock')} › {t('menu.lotChanges')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.lotChanges')}</h1>
          <div className="sub2">{t('inv.sl.subtitle')}</div>
        </div>
        <button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('inv.sl.new')}</button>
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.lotChanges')}>
          {list.data?.length === 0 && <p className="muted">{t('inv.sl.empty')}</p>}
          <table className="partner-list">
            <tbody>
              {(list.data ?? []).map((d) => (
                <tr key={d.id} className={selected === d.id ? 'selected' : undefined} onClick={() => setSelected(d.id)}>
                  <td>
                    {`SL ${d.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(valuationStatusLabel(d.status))}</span>
                    <div className="muted small">{`${d.warehouseCode} · ${isoToRo(d.documentDate)} · ${t('inv.mp.lines', { count: d.lineCount })}`}</div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
        <section className="card detail-card">
          {selected === undefined && <p className="muted empty-detail">{t('inv.sl.select')}</p>}
          {selected === 'new' && <LotChangeEditor key="new" document={undefined} onSaved={setSelected} />}
          {typeof selected === 'number' && <LotChangePanel key={selected} id={selected} onSaved={setSelected} />}
        </section>
      </div>
    </div>
  );
}

function LotChangePanel({ id, onSaved }: { readonly id: number; readonly onSaved: (id: number) => void }) {
  const document = useLotChange(id);
  if (document.data === undefined) {
    return <p role="status">{document.isError ? document.error.message : t('app.loading')}</p>;
  }
  return <LotChangeEditor key={document.data.rowVersion} document={document.data} onSaved={onSaved} />;
}

function LotChangeEditor({ document, onSaved }: { readonly document: LotChange | undefined; readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const draft = document === undefined || document.status === ValuationStatus.Draft;
  const [warehouse, setWarehouse] = useState<number | undefined>(document?.warehouseId);
  const [date, setDate] = useState(document?.documentDate ?? new Date().toISOString().slice(0, 10));
  const [notes, setNotes] = useState(document?.notes ?? '');
  const [lines, setLines] = useState<LineDraft[]>(() => (document?.lines ?? []).map((l) => ({
    itemVariantId: l.itemVariantId, variantLabel: l.variantCode, fromLotId: l.fromLotId, toLotId: l.toLotId ?? undefined, newLotCode: l.newLotCode ?? '',
    newExpiryDate: l.newExpiryDate ?? '', qty: quantityForInput(l.qtyBase),
  })));
  const update = (index: number, patch: Partial<LineDraft>) => setLines(lines.map((l, i) => (i === index ? { ...l, ...patch } : l)));
  const run = async (action: () => Promise<{ id: number }>) => {
    try {
      const saved = await action();
      await queryClient.invalidateQueries({ queryKey: lotKeys.all });
      notify(t('common.saved'));
      onSaved(saved.id);
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };
  const save = () => lotsApi.saveLotChange({
    id: document?.id ?? null, warehouseId: warehouse ?? 0, depotId: document?.depotId ?? null, documentDate: date, notes: notes.trim() === '' ? null : notes.trim(),
    lines: lines.map((l) => ({
      itemVariantId: l.itemVariantId, fromLotId: l.fromLotId ?? 0, toLotId: l.toLotId ?? null, newLotCode: l.toLotId === undefined ? l.newLotCode.trim() : null,
      newExpiryDate: l.toLotId === undefined && l.newExpiryDate !== '' ? l.newExpiryDate : null, qtyBase: quantityForApi(l.qty) ?? '',
    })),
    rowVersion: document?.rowVersion ?? null,
  });
  const ready = warehouse !== undefined && lines.length > 0 && lines.every(lineComplete);

  return (
    <div className="node-form">
      <h2 className="form-title">{`SL ${document?.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(valuationStatusLabel(document?.status ?? 1))}</span></h2>
      <fieldset className="form-body plain" disabled={!draft}>
        <div className="grid g3">
          <label className="field">
            <span className="label">{t('inv.col.warehouse')}</span>
            <WarehouseSelect value={warehouse} onChange={setWarehouse} allowAll={false} />
          </label>
          <label className="field">
            <span className="label">{t('inv.doc.date')}</span>
            <input className="cell-input" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </label>
          <label className="field">
            <span className="label">{t('inv.sl.notes')}</span>
            <input className="cell-input" value={notes} onChange={(e) => setNotes(e.target.value)} />
          </label>
        </div>
      </fieldset>
      {draft ? (
        <>
          {lines.map((l, index) => (
            <LineEditor key={index} line={l} warehouse={warehouse} onChange={(patch) => update(index, patch)} onRemove={() => setLines(lines.filter((_, i) => i !== index))} />
          ))}
          <div className="chips-row">
            <span className="muted small">{t('inv.sl.addLine')}</span>
            <VariantPicker value={undefined} onChange={(v) => { if (v !== undefined) { setLines([...lines, { itemVariantId: v.id, variantLabel: v.label, fromLotId: undefined, toLotId: undefined, newLotCode: '', newExpiryDate: '', qty: '' }]); } }} />
          </div>
        </>
      ) : (
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('inv.item.variant')}</th>
              <th scope="col">{t('inv.sl.from')}</th>
              <th scope="col">{t('inv.sl.to')}</th>
              <th scope="col" className="num">{t('inv.serial.pieces')}</th>
            </tr>
          </thead>
          <tbody>
            {(document?.lines ?? []).map((l) => (
              <tr key={l.lineId}>
                <td><code>{l.variantCode}</code></td>
                <td>{l.fromCku}</td>
                <td>{l.toCku ?? l.newLotCode ?? '—'}</td>
                <td className="num">{qtyToRo(l.qtyBase)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {draft && (
        <div className="form-footer">
          <span className="muted">{t('inv.sl.hint')}</span>
          <div className="actions">
            {document !== undefined && (
              <button type="button" className="btn ghost" onClick={() => void run(() => lotsApi.cancelLotChange(document.id, document.rowVersion))}>{t('inv.doc.cancel')}</button>
            )}
            <button type="button" className="btn ghost" disabled={!ready} onClick={() => void run(save)}>{t('common.save')}</button>
            <button type="button" className="btn primary" disabled={!ready} onClick={() => void run(async () => {
              const saved = await save();
              return lotsApi.validateLotChange(saved.id, saved.rowVersion);
            })}>
              {t('inv.doc.validate')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function LineEditor({ line, warehouse, onChange, onRemove }: {
  readonly line: LineDraft; readonly warehouse: number | undefined; readonly onChange: (patch: Partial<LineDraft>) => void; readonly onRemove: () => void;
}) {
  const lots = useLots(line.itemVariantId, warehouse, '');
  const rows = [...new Map((lots.data ?? []).filter((l) => warehouse === undefined || l.warehouseId === warehouse).map((l) => [l.lotId, l])).values()];
  const label = (l: (typeof rows)[number]) => `${l.lotCode}${l.isTechnical ? ` (${t('inv.lot.technical')})` : ''} · ${qtyToRo(l.qtyOnHand)}`;
  return (
    <div className="grid g4" aria-label={line.variantLabel}>
      <label className="field">
        <span className="label">{`${line.variantLabel} · ${t('inv.sl.from')}`}</span>
        <select className="cell-input" value={line.fromLotId ?? ''} onChange={(e) => onChange({ fromLotId: e.target.value === '' ? undefined : Number(e.target.value) })}>
          <option value="">{t('inv.sl.chooseLot')}</option>
          {rows.map((l) => <option key={l.lotId} value={l.lotId}>{label(l)}</option>)}
        </select>
      </label>
      <label className="field">
        <span className="label">{t('inv.sl.to')}</span>
        <select className="cell-input" value={line.toLotId ?? ''} onChange={(e) => onChange({ toLotId: e.target.value === '' ? undefined : Number(e.target.value) })}>
          <option value="">{t('inv.sl.newLot')}</option>
          {rows.filter((l) => l.lotId !== line.fromLotId).map((l) => <option key={l.lotId} value={l.lotId}>{label(l)}</option>)}
        </select>
      </label>
      {line.toLotId === undefined ? (
        <label className="field">
          <span className="label">{t('inv.sl.newLotCode')}</span>
          <span className="picker">
            <input className="cell-input" aria-label={t('inv.sl.newLotCode')} value={line.newLotCode} onChange={(e) => onChange({ newLotCode: e.target.value })} />
            <input className="cell-input" type="date" aria-label={t('inv.lot.expiry')} value={line.newExpiryDate} onChange={(e) => onChange({ newExpiryDate: e.target.value })} />
          </span>
        </label>
      ) : <span />}
      <label className="field">
        <span className="label">{t('inv.serial.pieces')}</span>
        <span className="picker">
          <input className="cell-input num" aria-label={t('inv.serial.pieces')} value={line.qty} onChange={(e) => onChange({ qty: e.target.value })} />
          <button type="button" className="btn ghost" aria-label={t('inv.sl.removeLine')} onClick={onRemove}>✕</button>
        </span>
      </label>
    </div>
  );
}
