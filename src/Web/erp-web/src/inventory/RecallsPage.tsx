import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import {
  groupCustomers, lotKeys, lotsApi, lotStatusLabel, lotStatusTone, qtyToRo, recallOutcomeLabel, RecallStatus, recallStatusLabel, useLots, useRecall, useRecalls,
  type Recall,
} from './lotsApi';

/** Stocuri › Stoc › Retrageri (02.05, S4 3.6; macheta s4-05): retragerile de lot, validarea care blocheaza loturile, clientii si inchiderea. */
export function RecallsPage() {
  const [params, setParams] = useSearchParams();
  const selected = params.get('id') === null ? undefined : params.get('id') === 'new' ? 'new' as const : Number(params.get('id'));
  const select = (id: number | 'new') => setParams({ id: String(id) });
  const list = useRecalls();

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.stock')} › {t('menu.recalls')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.recalls')}</h1>
          <div className="sub2">{t('inv.recall.subtitle')}</div>
        </div>
        <button type="button" className="btn primary" onClick={() => select('new')}>{t('inv.recall.new')}</button>
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.recalls')}>
          {list.data?.length === 0 && <p className="muted">{t('inv.recall.empty')}</p>}
          <table className="partner-list">
            <tbody>
              {(list.data ?? []).map((r) => (
                <tr key={r.id} className={selected === r.id ? 'selected' : undefined} onClick={() => select(r.id)}>
                  <td>
                    {`RL ${r.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(recallStatusLabel(r.status))}</span>
                    <div className="muted small">
                      {[isoToRo(r.documentDate), r.supplierLotCode, t('inv.recall.lotCount', { count: r.lotCount }), t('inv.recall.customerCount', { count: r.customerCount })]
                        .filter(Boolean).join(' · ')}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
        <section className="card detail-card">
          {selected === undefined && <p className="muted empty-detail">{t('inv.recall.select')}</p>}
          {selected === 'new' && <RecallEditor key="new" recall={undefined} onSaved={select} />}
          {typeof selected === 'number' && <RecallPanel key={selected} id={selected} onSaved={select} />}
        </section>
      </div>
    </div>
  );
}

function RecallPanel({ id, onSaved }: { readonly id: number; readonly onSaved: (id: number) => void }) {
  const recall = useRecall(id);
  if (recall.data === undefined) {
    return <p role="status">{recall.isError ? recall.error.message : t('app.loading')}</p>;
  }
  return <RecallEditor key={recall.data.rowVersion} recall={recall.data} onSaved={onSaved} />;
}

function RecallEditor({ recall, onSaved }: { readonly recall: Recall | undefined; readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const draft = recall === undefined || recall.status === RecallStatus.Draft;
  const [reason, setReason] = useState(recall?.reason ?? '');
  const [date, setDate] = useState(recall?.documentDate ?? new Date().toISOString().slice(0, 10));
  const [sourceKind, setSourceKind] = useState(recall?.sourceKind ?? 1);
  const [supplierLot, setSupplierLot] = useState(recall?.supplierLotCode ?? '');
  const [lots, setLots] = useState(() => (recall?.lots ?? []).map((l) => ({ id: l.lotId, label: l.ckuCode })));
  const [search, setSearch] = useState('');
  const found = useLots(undefined, undefined, search);
  const options = search.trim() === '' ? [] : [...new Map((found.data ?? []).map((l) => [l.lotId, l])).values()].filter((l) => !lots.some((x) => x.id === l.lotId)).slice(0, 8);
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
  const save = () => lotsApi.saveRecall({
    id: recall?.id ?? null, documentDate: date, sourceKind, reason, supplierPartnerId: recall?.supplierPartnerId ?? null,
    supplierLotCode: supplierLot.trim() === '' ? null : supplierLot.trim(), lotIds: lots.map((l) => l.id), rowVersion: recall?.rowVersion ?? null,
  });
  const customers = groupCustomers(recall?.customers ?? []);

  return (
    <div className="node-form">
      <h2 className="form-title">{`RL ${recall?.number ?? t('inv.doc.draftNumber')} ${t('inv.recall.title')}`} <span className="pill">{t(recallStatusLabel(recall?.status ?? 1))}</span></h2>
      <fieldset className="form-body plain" disabled={!draft}>
        <label className="field">
          <span className="label">{t('inv.lot.reason')}</span>
          <input className="cell-input" value={reason} onChange={(e) => setReason(e.target.value)} />
        </label>
        <div className="grid g3">
          <label className="field">
            <span className="label">{t('inv.doc.date')}</span>
            <input className="cell-input" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </label>
          <label className="field">
            <span className="label">{t('inv.recall.source')}</span>
            <select className="cell-input" value={sourceKind} onChange={(e) => setSourceKind(Number(e.target.value))}>
              <option value={1}>{t('inv.recall.source.supplier')}</option>
              <option value={2}>{t('inv.recall.source.internal')}</option>
              <option value={3}>{t('inv.recall.source.authority')}</option>
            </select>
          </label>
          <label className="field">
            <span className="label">{t('inv.lot.supplierLot')}</span>
            <input className="cell-input" value={supplierLot} onChange={(e) => setSupplierLot(e.target.value)} />
          </label>
        </div>
      </fieldset>
      <h3 className="section-title">{t('inv.recall.lots')}</h3>
      {draft ? (
        <div className="chips-row">
          {lots.map((l) => <button key={l.id} type="button" className="chip on" onClick={() => setLots(lots.filter((x) => x.id !== l.id))}>{`${l.label} ✕`}</button>)}
          <input className="cell-input" placeholder={t('inv.lot.search')} aria-label={t('inv.lot.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
          {options.map((l) => (
            <button key={l.lotId} type="button" className="chip" onClick={() => { setLots([...lots, { id: l.lotId, label: l.ckuCode }]); setSearch(''); }}>{`+ ${l.ckuCode}`}</button>
          ))}
        </div>
      ) : (
        <table className="inline-table">
          <tbody>
            {(recall?.lots ?? []).map((l) => (
              <tr key={l.lotId}>
                <td><Link to={`/stocuri/trasabilitate?lot=${l.lotId}`}>{l.ckuCode}</Link></td>
                <td><span className={`pill ${lotStatusTone(l.status)}`}>{t(lotStatusLabel(l.status))}</span></td>
                <td className="num">{t('inv.recall.inStock', { qty: qtyToRo(l.qtyOnHand) })}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {draft && (
        <ul className="validation-steps" aria-label={t('inv.recall.whatHappens')}>
          <li><span className="pill bad">!</span><span>{t('inv.recall.step.block')}</span></li>
          <li><span className="pill warn">!</span><span>{t('inv.recall.step.drafts')}</span></li>
          <li><span className="pill info">i</span><span>{t('inv.recall.step.customers')}</span></li>
        </ul>
      )}
      {recall !== undefined && recall.status !== RecallStatus.Draft && (
        <>
          <h3 className="section-title">{t('inv.recall.customers', { count: customers.length })}</h3>
          <table className="inline-table">
            <thead>
              <tr>
                <th scope="col">{t('inv.trace.customer')}</th>
                <th scope="col">{t('inv.serial.document')}</th>
                <th scope="col" className="num">{t('inv.serial.pieces')}</th>
                <th scope="col">{t('inv.recall.outcome')}</th>
              </tr>
            </thead>
            <tbody>
              {recall.customers.map((c) => (
                <tr key={c.id}>
                  <td>{c.partnerName ?? '—'}</td>
                  <td>{`${c.sourceDocumentType} ${c.sourceDocumentNumber ?? ''} · ${isoToRo(c.movementDate)}`}</td>
                  <td className="num">{qtyToRo(c.qtyBase)}</td>
                  <td>
                    {recall.status === RecallStatus.Open ? (
                      <select className="cell-input" aria-label={t('inv.recall.outcome')} value={c.outcome ?? ''}
                        onChange={(e) => void run(async () => { await lotsApi.setOutcome(c.id, Number(e.target.value), null); return { id: recall.id }; })}>
                        <option value="" disabled>{t('inv.recall.outcome.pending')}</option>
                        {[1, 2, 3].map((o) => <option key={o} value={o}>{t(recallOutcomeLabel(o))}</option>)}
                      </select>
                    ) : t(recallOutcomeLabel(c.outcome))}
                  </td>
                </tr>
              ))}
              {recall.customers.length === 0 && <tr><td colSpan={4} className="muted">{t('inv.trace.noCustomers')}</td></tr>}
            </tbody>
          </table>
        </>
      )}
      <div className="banner dashed">{t('inv.recall.closeRule')}</div>
      <div className="form-footer">
        <span className="muted">{t('inv.recall.hint')}</span>
        <div className="actions">
          {draft && <button type="button" className="btn ghost" onClick={() => void run(save)}>{t('common.save')}</button>}
          {draft && (
            <button type="button" className="btn primary" disabled={reason.trim() === '' || (lots.length === 0 && supplierLot.trim() === '')}
              onClick={() => void run(async () => {
                const saved = await save();
                return lotsApi.validateRecall(saved.id, saved.rowVersion);
              })}>
              {t('inv.recall.validate')}
            </button>
          )}
          {recall?.status === RecallStatus.Open && (
            <button type="button" className="btn primary" onClick={() => void run(() => lotsApi.closeRecall(recall.id, recall.rowVersion))}>{t('inv.recall.close')}</button>
          )}
        </div>
      </div>
    </div>
  );
}
