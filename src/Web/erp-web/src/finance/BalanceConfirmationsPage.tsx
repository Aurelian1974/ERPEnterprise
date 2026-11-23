import { useState } from 'react';
import { useOutletContext } from 'react-router';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { fileToBase64 } from './bankApi';
import { formatAmount } from './cashApi';
import { downloadFile } from './paymentOrderApi';
import { Role } from './settlementApi';
import { parseAmount } from './TreasurySettingsTab';

type Confirmation = components['schemas']['BalanceConfirmationDto'];
export type ConfirmationFilter = 'all' | 'confirmed' | 'contested' | 'unanswered';

const filters: readonly { readonly value: ConfirmationFilter; readonly label: TextKey }[] = [
  { value: 'all', label: 'conf.filter.all' },
  { value: 'confirmed', label: 'conf.filter.confirmed' },
  { value: 'contested', label: 'conf.filter.contested' },
  { value: 'unanswered', label: 'conf.filter.unanswered' },
];

/** Starea confirmarii (F3 3.9): 0 generata, 1 trimisa, 2 confirmata, 3 contestata, 4 fara raspuns. */
export const ConfirmationStatus = { Generated: 0, Sent: 1, Confirmed: 2, Contested: 3, NoAnswer: 4 } as const;

/** Filtrul machetei f3-07: „fara raspuns” cuprinde si confirmarile generate sau trimise, la care nu a venit inca nimic. */
export function filterConfirmations<T extends { readonly status: number }>(items: readonly T[], filter: ConfirmationFilter): T[] {
  switch (filter) {
    case 'confirmed': return items.filter((c) => c.status === ConfirmationStatus.Confirmed);
    case 'contested': return items.filter((c) => c.status === ConfirmationStatus.Contested);
    case 'unanswered': return items.filter((c) => c.status === ConfirmationStatus.Generated || c.status === ConfirmationStatus.Sent
      || c.status === ConfirmationStatus.NoAnswer);
    default: return [...items];
  }
}

const statusPills: Readonly<Record<number, string>> = { 0: 'pill', 1: 'pill info', 2: 'pill ok', 3: 'pill bad', 4: 'pill warn' };
const statusLabel = (status: number): TextKey => (['conf.status.0', 'conf.status.1', 'conf.status.2', 'conf.status.3', 'conf.status.4'] as const)[status] ?? 'conf.status.0';
const keys = { all: ['finance', 'balance-confirmations'] as const };
const download = (file: components['schemas']['DownloadedFile']) =>
  downloadFile({ fileName: file.fileName, contentType: file.contentType, contentBase64: file.content });

/**
 * Decontări › Confirmări de sold (02.02, F3 3.9, 4.6; macheta f3-07): loturile generate la o dată, pe rol și peste un prag de sold, cu PDF-ul
 * fiecărei confirmări, trimiterea pe email prin outbox (și retrimiterea celor fără răspuns), filtrele pe răspuns și fișa confirmării, unde se
 * înregistrează răspunsul partenerului (confirmat, contestat cu soldul lui și explicația diferenței, fișierul primit).
 */
export function BalanceConfirmationsPage() {
  const user = useOutletContext<CurrentUser>();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [batchId, setBatchId] = useState<number | undefined>();
  const [filter, setFilter] = useState<ConfirmationFilter>('all');
  const [selected, setSelected] = useState<number | undefined>();
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const [warning, setWarning] = useState<string | undefined>();
  const list = useQuery({
    queryKey: [...keys.all, batchId ?? 'last'],
    queryFn: async () => unwrap(await api.GET('/api/finance/balance-confirmations', { params: { query: batchId === undefined ? {} : { batchId } } })),
    placeholderData: keepPreviousData,
  });
  const canManage = user.permissions.includes('Fin.BalanceConfirmation.Manage');
  const batches = list.data?.batches ?? [];
  const batch = batches.find((b) => b.id === (batchId ?? list.data?.items[0]?.batchId)) ?? batches[0];
  const items = filterConfirmations(list.data?.items ?? [], filter);
  const current = (list.data?.items ?? []).find((c) => c.id === selected) ?? items[0];

  const send = async (onlyUnanswered: boolean) => {
    if (batch === undefined) return;
    setError(undefined);
    setWarning(undefined);
    try {
      const result = unwrap(await api.POST('/api/finance/balance-confirmations/batches/{batchId}/send', { params: { path: { batchId: batch.id } },
        body: { batchId: batch.id, onlyUnanswered } }));
      notify(t('conf.sent', { count: result.sent }));
      if (result.withoutEmail.length > 0) setWarning(t('conf.withoutEmail', { partners: result.withoutEmail.join(', ') }));
      await queryClient.invalidateQueries({ queryKey: keys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.settlements')} › {t('menu.balanceConfirmations')}</div>
      <div className="head">
        <div>
          <h1>{t('conf.title')}</h1>
          <div className="sub2">{t('conf.subtitle')}</div>
        </div>
        <div className="actions">
          {batches.length > 0 && (
            <label className="inline">{t('conf.batch')}{' '}
              <select className="cell-input" value={batch?.id ?? ''} onChange={(e) => { setBatchId(Number(e.target.value)); setSelected(undefined); }}>
                {batches.map((b) => <option key={b.id} value={b.id}>{t('conf.batchText', { number: b.number, date: isoToRo(b.asOfDate) })}</option>)}
              </select>
            </label>
          )}
          {canManage && <button type="button" className="btn primary" onClick={() => setCreating(true)}>{t('conf.newBatch')}</button>}
        </div>
      </div>
      {creating && <GenerateForm onDone={(id) => { setCreating(false); if (id !== undefined) { setBatchId(id); setSelected(undefined); } }} />}
      {batch !== undefined && (
        <div className="kpi-row">
          <div className="kpi"><div className="kpi-label">{t('conf.kpi.total')}</div><div className="kpi-value">{batch.total}</div>
            <div className="muted small">{t('conf.minBalance')}: {formatAmount(batch.minBalance)}</div></div>
          <div className="kpi"><div className="kpi-label">{t('conf.kpi.confirmed')}</div><div className="kpi-value">{batch.confirmed}</div></div>
          <div className="kpi bad"><div className="kpi-label">{t('conf.kpi.contested')}</div><div className="kpi-value">{batch.contested}</div></div>
          <div className="kpi warn"><div className="kpi-label">{t('conf.kpi.unanswered')}</div><div className="kpi-value">{batch.unanswered}</div></div>
        </div>
      )}
      <ErrorBanner message={list.isError ? list.error.message : error} />
      {warning !== undefined && <div className="banner warn" role="status">{warning}</div>}
      {list.data !== undefined && batches.length === 0 ? <p className="muted">{t('conf.noBatches')}</p> : (
        <div className="split cash-document">
          <section className="card" aria-label={t('conf.title')}>
            <div className="row-between">
              <div className="chips-row" role="group" aria-label={t('conf.filter')}>
                {filters.map((f) => (
                  <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                    onClick={() => setFilter(f.value)}>{t(f.label)}</button>
                ))}
              </div>
              {canManage && batch !== undefined && (
                <div className="actions">
                  <button type="button" className="btn ghost" onClick={() => void send(true)}>{t('conf.resend')}</button>
                  <button type="button" className="btn primary" onClick={() => void send(false)}>{t('conf.send')}</button>
                </div>
              )}
            </div>
            {items.length === 0 ? <p className="muted">{t('conf.empty')}</p> : (
              <table className="partner-list cash-list" aria-busy={list.isFetching}>
                <thead>
                  <tr>
                    <th scope="col">{t('conf.col.partner')}</th>
                    <th scope="col" className="num">{t('conf.col.ours')}</th>
                    <th scope="col" className="num">{t('conf.col.theirs')}</th>
                    <th scope="col" className="num">{t('conf.col.difference')}</th>
                    <th scope="col">{t('bank.col.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((c) => (
                    <tr key={c.id} className={c.id === current?.id ? 'clickable picked' : 'clickable'} tabIndex={0} onClick={() => setSelected(c.id)}
                      onKeyDown={(e) => e.key === 'Enter' && setSelected(c.id)}>
                      <td><div className="strong">{c.partnerName}</div>
                        <div className="muted small">{c.number} · {t(c.role === Role.Customer ? 'settle.role.customer' : 'settle.role.supplier')}</div></td>
                      <td className="num">{formatAmount(c.ourBalance)}</td>
                      <td className="num">{formatAmount(c.partnerBalance)}</td>
                      <td className={c.difference != null && c.difference !== '0.00' ? 'num diff-bad' : 'num'}>{formatAmount(c.difference)}</td>
                      <td><span className={statusPills[c.status] ?? 'pill'}>{t(statusLabel(c.status))}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <p className="muted small">{t('conf.legal')}</p>
          </section>
          <aside className="card cash-side">
            {current === undefined ? <p className="muted">{t('conf.pick')}</p> : <Detail key={current.id} confirmation={current} canManage={canManage} />}
          </aside>
        </div>
      )}
    </div>
  );
}

function GenerateForm({ onDone }: { readonly onDone: (batchId?: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [draft, setDraft] = useState({ asOf: isoToday(), role: '' as '' | '1' | '2', minBalance: '5000' });
  const [error, setError] = useState<string | undefined>();
  const minBalance = parseAmount(draft.minBalance);

  const generate = async () => {
    setError(undefined);
    try {
      const id = unwrap(await api.POST('/api/finance/balance-confirmations', { body: { asOf: draft.asOf, role: draft.role === '' ? null : Number(draft.role),
        minBalance: minBalance ?? '', partnerIds: null } }));
      notify(t('conf.generated'));
      await queryClient.invalidateQueries({ queryKey: keys.all });
      onDone(id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <section className="card order-from-invoices" aria-label={t('conf.newBatch')}>
      <label>{t('conf.asOf')}<input className="cell-input" type="date" value={draft.asOf} onChange={(e) => setDraft({ ...draft, asOf: e.target.value })} /></label>
      <label>{t('conf.role')}
        <select className="cell-input" value={draft.role} onChange={(e) => setDraft({ ...draft, role: e.target.value as '' | '1' | '2' })}>
          <option value="">{t('conf.role.both')}</option>
          <option value="1">{t('settle.role.customer')}</option>
          <option value="2">{t('settle.role.supplier')}</option>
        </select>
      </label>
      <label>{t('conf.minBalance')}<input className="cell-input num" value={draft.minBalance} aria-invalid={minBalance === null}
        onChange={(e) => setDraft({ ...draft, minBalance: e.target.value })} /></label>
      <ErrorBanner message={error} />
      <div className="actions">
        <button type="button" className="btn ghost" onClick={() => onDone()}>{t('common.cancel')}</button>
        <button type="button" className="btn primary" disabled={minBalance === null} onClick={() => void generate()}>{t('conf.generate')}</button>
      </div>
    </section>
  );
}

function Detail({ confirmation: c, canManage }: { readonly confirmation: Confirmation; readonly canManage: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [draft, setDraft] = useState({
    status: c.status >= ConfirmationStatus.Confirmed ? c.status : ConfirmationStatus.Confirmed,
    responseDate: c.responseDate ?? isoToday(), partnerBalance: c.partnerBalance ?? '', note: c.responseNote ?? '', isResolved: c.isResolved,
  });
  const [file, setFile] = useState<File | undefined>();
  const [error, setError] = useState<string | undefined>();
  const contested = draft.status === ConfirmationStatus.Contested;

  const fetchFile = async (path: '/api/finance/balance-confirmations/{id}/pdf' | '/api/finance/balance-confirmations/{id}/response-file') => {
    setError(undefined);
    try {
      download(unwrap(await api.GET(path, { params: { path: { id: c.id } } })));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  const save = async () => {
    setError(undefined);
    try {
      unwrap(await api.POST('/api/finance/balance-confirmations/{id}/response', { params: { path: { id: c.id } }, body: {
        id: c.id, status: draft.status, responseDate: draft.status === ConfirmationStatus.NoAnswer ? null : draft.responseDate,
        partnerBalance: contested ? parseAmount(draft.partnerBalance) : null, note: draft.note === '' ? null : draft.note, isResolved: draft.isResolved,
        fileName: file?.name ?? null, content: file === undefined ? null : await fileToBase64(file), rowVersion: c.rowVersion } }));
      notify(t('conf.saved'));
      await queryClient.invalidateQueries({ queryKey: keys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div>
      <h2>{c.partnerName} <span className={statusPills[c.status] ?? 'pill'}>{t(statusLabel(c.status))}</span></h2>
      <p className="muted small">{c.number} · {t(c.role === Role.Customer ? 'settle.role.customer' : 'settle.role.supplier')} · {isoToRo(c.asOfDate)}</p>
      <p className="muted small">
        {c.sentTo != null && c.sentAt != null ? t('conf.sentTo', { email: c.sentTo, date: isoToRo(c.sentAt.slice(0, 10)) }) : c.email == null ? t('conf.noEmail') : c.email}
      </p>
      {c.emailError != null && <div className="banner warn" role="status">{t('conf.email.failed', { error: c.emailError })}</div>}
      <div className="kpi-row">
        <div className="kpi"><div className="kpi-label">{t('conf.col.ours')}</div><div className="kpi-value">{formatAmount(c.ourBalance)}</div></div>
        <div className="kpi"><div className="kpi-label">{t('conf.col.theirs')}</div><div className="kpi-value">{formatAmount(c.partnerBalance) || '—'}</div></div>
        <div className={c.difference != null && c.difference !== '0.00' ? 'kpi bad' : 'kpi'}><div className="kpi-label">{t('conf.col.difference')}</div>
          <div className="kpi-value">{formatAmount(c.difference) || '—'}</div></div>
      </div>
      {c.responseNote != null && <p>{c.responseNote}</p>}
      <div className="actions">
        {c.generatedAttachmentId != null && <button type="button" className="btn ghost" onClick={() => void fetchFile('/api/finance/balance-confirmations/{id}/pdf')}>{t('conf.pdf')}</button>}
        {c.responseAttachmentId != null && (
          <button type="button" className="btn ghost" onClick={() => void fetchFile('/api/finance/balance-confirmations/{id}/response-file')}>{t('conf.responseFile')}</button>
        )}
      </div>
      {canManage && (
        <div className="settings-form">
          <h3 className="section-title">{t('conf.response')}</h3>
          <div className="chips-row" role="group" aria-label={t('conf.response')}>
            {[ConfirmationStatus.Confirmed, ConfirmationStatus.Contested, ConfirmationStatus.NoAnswer].map((s) => (
              <button key={s} type="button" className={draft.status === s ? 'chip on' : 'chip'} aria-pressed={draft.status === s}
                onClick={() => setDraft({ ...draft, status: s })}>{t(statusLabel(s))}</button>
            ))}
          </div>
          {draft.status !== ConfirmationStatus.NoAnswer && (
            <label>{t('conf.responseDate')}<input className="cell-input" type="date" value={draft.responseDate}
              onChange={(e) => setDraft({ ...draft, responseDate: e.target.value })} /></label>
          )}
          {contested && (
            <label>{t('conf.partnerBalance')}<input className="cell-input num" value={draft.partnerBalance}
              aria-invalid={draft.partnerBalance !== '' && parseAmount(draft.partnerBalance) === null}
              onChange={(e) => setDraft({ ...draft, partnerBalance: e.target.value })} /></label>
          )}
          <label>{t('conf.note')}<textarea className="cell-input" rows={3} value={draft.note} onChange={(e) => setDraft({ ...draft, note: e.target.value })} /></label>
          <label>{t('conf.file')}<input type="file" accept=".pdf,.png,.jpg,.jpeg,.tif,.tiff,.xml" onChange={(e) => setFile(e.target.files?.[0])} /></label>
          {contested && (
            <label className="check"><input type="checkbox" checked={draft.isResolved} onChange={(e) => setDraft({ ...draft, isResolved: e.target.checked })} />
              {t('conf.resolved')}</label>
          )}
          <ErrorBanner message={error} />
          <div className="actions"><button type="button" className="btn primary" onClick={() => void save()}>{t('conf.save')}</button></div>
        </div>
      )}
      {!canManage && <ErrorBanner message={error} />}
    </div>
  );
}
