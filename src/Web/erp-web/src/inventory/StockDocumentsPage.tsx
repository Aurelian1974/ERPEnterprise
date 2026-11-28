import { useState } from 'react';
import { useOutletContext, useSearchParams } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import type { CurrentUser } from '../api/session';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { isoToRo, roToIso } from '../partners/partnerModel';
import {
  ApprovalStatus, approvalLabel, documentKeys, documentsApi, documentTypes, DocumentStatus, searchVariants, statusLabel, toLinesBody, useMovementReasons,
  useStockDocument, useStockDocuments, type EditorLine, type StockDocument, type VariantOption,
} from './documentsApi';
import { useWarehouses } from './inventoryApi';

/**
 * Stocuri › Documente (02.05, S2 3.3–3.9, 11; machetele s2-01, s2-03, s2-04): lista documentelor de tipurile date (note de predare, incarcari si
 * descarcari, bonuri de consum, custodie, note de predare produse) si documentul cu liniile lui; ciorna se salveaza si se valideaza, descarcarea
 * peste prag se aproba (patru ochi), documentul validat se storneaza.
 */
export function StockDocumentsPage({ types, title }: { readonly types: readonly string[]; readonly title: TextKey }) {
  const user = useOutletContext<CurrentUser>();
  const [params, setParams] = useSearchParams();
  const [status, setStatus] = useState<number | undefined>();
  const list = useStockDocuments(types, status);
  const id = Number(params.get('id'));
  const selected = Number.isInteger(id) && id > 0 ? id : undefined;
  const creating = params.get('nou');

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.stockDocuments')} › {t(title)}</div>
      <div className="head">
        <div>
          <h1>{t(title)}</h1>
        </div>
        <div className="actions">
          {types.map((type) => (
            <button key={type} type="button" className="btn primary" onClick={() => setParams({ nou: type })}>
              {t('inv.doc.new', { type: t(documentTypes[type]?.label ?? 'inv.doc.NI') })}
            </button>
          ))}
        </div>
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t(title)}>
          <div className="chips-row" role="group" aria-label={t('inv.col.status')}>
            {[undefined, DocumentStatus.Draft, DocumentStatus.Validated, DocumentStatus.Reversed].map((value) => (
              <button key={value ?? 0} type="button" className={status === value ? 'chip on' : 'chip'} aria-pressed={status === value}
                onClick={() => setStatus(value)}>
                {value === undefined ? t('inv.filter.all') : t(statusLabel(value))}
              </button>
            ))}
          </div>
          {list.data === undefined && <p role="status">{list.isError ? t('app.loadError') : t('app.loading')}</p>}
          {list.data !== undefined && list.data.length === 0 && <p className="muted">{t('inv.doc.empty')}</p>}
          {list.data !== undefined && list.data.length > 0 && (
            <table className="partner-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('inv.doc.number')}</th>
                  <th scope="col">{t('inv.col.warehouse')}</th>
                  <th scope="col">{t('inv.col.status')}</th>
                </tr>
              </thead>
              <tbody>
                {list.data.map((d) => (
                  <tr key={d.id} className={d.id === selected ? 'current' : ''} onClick={() => setParams({ id: String(d.id) })}>
                    <td>
                      <button type="button" className="row-link" onClick={(e) => {
                        e.stopPropagation();
                        setParams({ id: String(d.id) });
                      }}>
                        {`${d.documentType} ${d.number ?? t('inv.doc.draftNumber')}`}
                      </button>
                      <div className="muted small">{[isoToRo(d.documentDate), d.reasonName].filter(Boolean).join(' · ')}</div>
                    </td>
                    <td className="small">{[d.fromWarehouseCode, d.toWarehouseCode].filter(Boolean).join(' → ')}</td>
                    <td>
                      <span className={d.status === DocumentStatus.Validated ? 'pill ok' : d.status === DocumentStatus.Draft ? 'pill warn' : 'pill off'}>
                        {t(statusLabel(d.status))}
                      </span>
                      {approvalLabel(d.approvalStatus) !== undefined && <div className="muted small">{t(approvalLabel(d.approvalStatus) ?? 'inv.approval.pending')}</div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <section className="card detail-card">
          {creating !== null && documentTypes[creating] !== undefined ? (
            <DocumentEditor key={`new-${creating}`} type={creating} document={undefined} user={user} onSaved={(saved) => setParams({ id: String(saved) })} />
          ) : selected === undefined ? (
            <p className="muted empty-detail">{t('inv.doc.select')}</p>
          ) : (
            <DocumentPanel key={selected} id={selected} user={user} onChanged={(next) => setParams({ id: String(next) })} />
          )}
        </section>
      </div>
    </div>
  );
}

function DocumentPanel({ id, user, onChanged }: { readonly id: number; readonly user: CurrentUser; readonly onChanged: (id: number) => void }) {
  const document = useStockDocument(id);
  if (document.data === undefined) {
    return <p role="status">{document.isError ? document.error.message : t('app.loading')}</p>;
  }
  return <DocumentEditor key={document.data.rowVersion} type={document.data.documentType} document={document.data} user={user} onSaved={onChanged} />;
}

function DocumentEditor({ type, document, user, onSaved }: {
  readonly type: string;
  readonly document: StockDocument | undefined;
  readonly user: CurrentUser;
  readonly onSaved: (id: number) => void;
}) {
  const info = documentTypes[type] ?? { code: type, label: 'inv.doc.NI', from: false, to: true };
  const draft = document === undefined || document.status === DocumentStatus.Draft;
  const warehouses = useWarehouses('');
  const reasons = useMovementReasons();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [date, setDate] = useState(document === undefined ? isoToRo(new Date().toISOString().slice(0, 10)) : isoToRo(document.documentDate));
  const [from, setFrom] = useState(document?.fromWarehouseId == null ? '' : String(document.fromWarehouseId));
  const [to, setTo] = useState(document?.toWarehouseId == null ? '' : String(document.toWarehouseId));
  const [reason, setReason] = useState(document?.reasonId == null ? '' : String(document.reasonId));
  const [notes, setNotes] = useState(document?.notes ?? '');
  const [lines, setLines] = useState<EditorLine[]>(() => (document?.lines ?? []).map((l) => ({
    itemVariantId: l.itemVariantId, label: `${l.variantCode} · ${l.itemName}`, qty: l.qty.replace('.', ','), unitCost: (l.unitCost ?? '').replace('.', ','),
  })));
  const [search, setSearch] = useState('');
  const [options, setOptions] = useState<VariantOption[]>([]);
  const [rejectNote, setRejectNote] = useState('');

  const eligible = (warehouses.data?.warehouses ?? []).filter((w) => w.isActive && w.kind !== 3 && (info.custody === true ? w.kind === 4 : w.kind !== 4 || type === 'NP'));
  const stockWarehouse = info.from ? Number(from) || undefined : Number(to) || undefined;
  const run = async (action: () => Promise<{ id: number }>) => {
    try {
      const result = await action();
      await queryClient.invalidateQueries({ queryKey: documentKeys.all });
      notify(t('common.saved'));
      onSaved(result.id);
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };
  const save = () => run(() => documentsApi.save({
    id: document?.id ?? null, documentType: type, documentDate: roToIso(date) ?? '', accountingDate: null, fromDepotId: null,
    fromWarehouseId: info.from && from !== '' ? Number(from) : null, toDepotId: null, toWarehouseId: info.to && to !== '' ? Number(to) : null,
    reasonId: info.reason !== undefined && reason !== '' ? Number(reason) : null, partnerId: null, costCenterId: null, projectId: null, employeeId: null,
    notes: notes.trim() === '' ? null : notes.trim(), lines: toLinesBody(lines), rowVersion: document?.rowVersion ?? null,
  }));
  const canReverse = user.permissions.includes('Inv.Document.Reverse');
  const canApprove = user.permissions.includes('Inv.LoadUnload.Approve');

  return (
    <div className="node-form">
      <h2 className="form-title">
        {document === undefined ? t('inv.doc.new', { type: t(info.label) }) : `${type} ${document.number ?? t('inv.doc.draftNumber')}`}{' '}
        {document !== undefined && <span className="pill">{t(statusLabel(document.status))}</span>}
      </h2>
      {document?.isReversal === true && <div className="banner dashed">{t('inv.doc.isReversal')}</div>}
      {document !== undefined && document.approvalStatus !== ApprovalStatus.None && (
        <div className="banner warn">{t(approvalLabel(document.approvalStatus) ?? 'inv.approval.pending')}{document.approvalNote ? ` · ${document.approvalNote}` : ''}</div>
      )}
      <fieldset className="form-body plain" disabled={!draft}>
        <div className="grid g3">
          <label className="field">
            <span className="label">{t('inv.doc.date')}</span>
            <input className="cell-input" placeholder="zz.ll.aaaa" value={date} onChange={(e) => setDate(e.target.value)} />
          </label>
          {info.from && (
            <label className="field">
              <span className="label">{t(type === 'NP' ? 'inv.doc.fromWarehouse' : 'inv.col.warehouse')}</span>
              <select className="cell-input" value={from} onChange={(e) => setFrom(e.target.value)}>
                <option value="">{t('common.none')}</option>
                {eligible.map((w) => <option key={w.id} value={w.id}>{`${w.code} · ${w.name}`}</option>)}
              </select>
            </label>
          )}
          {info.to && (
            <label className="field">
              <span className="label">{t(type === 'NP' ? 'inv.doc.toWarehouse' : 'inv.col.warehouse')}</span>
              <select className="cell-input" value={to} onChange={(e) => setTo(e.target.value)}>
                <option value="">{t('common.none')}</option>
                {eligible.map((w) => <option key={w.id} value={w.id}>{`${w.code} · ${w.name}`}</option>)}
              </select>
            </label>
          )}
          {info.reason !== undefined && (
            <label className="field">
              <span className="label">{t('inv.doc.reason')}</span>
              <select className="cell-input" value={reason} onChange={(e) => setReason(e.target.value)}>
                <option value="">{t('common.none')}</option>
                {(reasons.data ?? []).filter((r) => r.direction === info.reason && r.isActive).map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
              </select>
            </label>
          )}
          <label className="field span2">
            <span className="label">{t('inv.handover.notes')}</span>
            <input className="cell-input" value={notes} onChange={(e) => setNotes(e.target.value)} />
          </label>
        </div>
        {info.costCenter === true && <p className="muted small">{t('inv.doc.costCenterHint')}</p>}
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('inv.item.variant')}</th>
              <th scope="col">{t('inv.doc.qty')}</th>
              {info.unitCost !== undefined && <th scope="col">{t('inv.doc.unitCost')}</th>}
              {!draft && <th scope="col">{t('inv.doc.cost')}</th>}
              {draft && <th scope="col"><span className="sr-only">{t('inv.depots.remove')}</span></th>}
            </tr>
          </thead>
          <tbody>
            {lines.map((line, index) => (
              <tr key={`${line.itemVariantId}-${index}`}>
                <td>{line.label}</td>
                <td>
                  <input className="cell-input num" value={line.qty} aria-label={t('inv.doc.qty')}
                    onChange={(e) => setLines(lines.map((l, i) => (i === index ? { ...l, qty: e.target.value } : l)))} />
                </td>
                {info.unitCost !== undefined && (
                  <td>
                    <input className="cell-input num" value={line.unitCost} aria-label={t('inv.doc.unitCost')}
                      onChange={(e) => setLines(lines.map((l, i) => (i === index ? { ...l, unitCost: e.target.value } : l)))} />
                  </td>
                )}
                {!draft && <td className="num">{document?.lines[index]?.costAmountPosted ?? ''}</td>}
                {draft && (
                  <td>
                    <button type="button" className="btn ghost small" onClick={() => setLines(lines.filter((_, i) => i !== index))}>{t('inv.depots.remove')}</button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
        {draft && (
          <div className="chips-row">
            <input className="cell-input" placeholder={t('inv.doc.searchVariant')} aria-label={t('inv.doc.searchVariant')} value={search}
              onChange={(e) => setSearch(e.target.value)} />
            <button type="button" className="btn ghost" onClick={() => void searchVariants(search, stockWarehouse).then(setOptions, () => setOptions([]))}>
              {t('inv.doc.find')}
            </button>
            {options.map((o) => (
              <button key={o.id} type="button" className="chip" onClick={() => {
                setLines([...lines, { itemVariantId: o.id, label: `${o.code} · ${o.name}`, qty: '1', unitCost: '' }]);
                setOptions([]);
                setSearch('');
              }}>
                {`${o.code} · ${o.name}${o.onHand == null ? '' : ` (${o.onHand.replace('.', ',')} ${o.baseUomCode})`}`}
              </button>
            ))}
          </div>
        )}
      </fieldset>
      <div className="form-footer">
        <span className="muted">{document?.totalCost == null ? '' : t('inv.doc.total', { amount: document.totalCost.replace('.', ',') })}</span>
        <div className="actions">
          {draft && <button type="button" className="btn ghost" onClick={() => void save()}>{t('common.save')}</button>}
          {draft && document !== undefined && (
            <>
              <button type="button" className="btn ghost" onClick={() => void run(() => documentsApi.cancel(document.id, document.rowVersion))}>
                {t('inv.doc.cancel')}
              </button>
              <button type="button" className="btn primary" onClick={() => void run(() => documentsApi.validate(document.id, document.rowVersion))}>
                {t('inv.doc.validate')}
              </button>
            </>
          )}
          {draft && document?.approvalStatus === ApprovalStatus.Pending && canApprove && (
            <>
              <input className="cell-input" placeholder={t('inv.doc.rejectNote')} aria-label={t('inv.doc.rejectNote')} value={rejectNote}
                onChange={(e) => setRejectNote(e.target.value)} />
              <button type="button" className="btn ghost" disabled={rejectNote.trim() === ''}
                onClick={() => void run(() => documentsApi.approve(document.id, false, rejectNote.trim(), document.rowVersion))}>
                {t('inv.doc.reject')}
              </button>
              <button type="button" className="btn primary" onClick={() => void run(() => documentsApi.approve(document.id, true, null, document.rowVersion))}>
                {t('inv.doc.approve')}
              </button>
            </>
          )}
          {document?.status === DocumentStatus.Validated && !document.isReversal && canReverse && (
            <button type="button" className="btn ghost" onClick={() => void run(() => documentsApi.reverse(document.id, document.rowVersion))}>
              {t('inv.doc.reverse')}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
