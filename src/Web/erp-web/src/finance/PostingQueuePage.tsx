import { useState } from 'react';
import { useOutletContext } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';

type QueueItem = components['schemas']['PostingQueueItemDto'];

const queueKeys = { all: ['finance', 'posting-queue'] as const };
export const reasonKey = (reason: number) => `queue.reason.${reason}` as TextKey;

/** Randurile in asteptare grupate pe motiv, in ordinea motivelor (C2 3.7). */
export function groupByReason(items: readonly QueueItem[]): [number, QueueItem[]][] {
  const groups = new Map<number, QueueItem[]>();
  for (const item of [...items].sort((a, b) => a.reason - b.reason || a.accountingDate.localeCompare(b.accountingDate))) {
    groups.set(item.reason, [...(groups.get(item.reason) ?? []), item]);
  }
  return [...groups.entries()];
}

/**
 * Contabilitate › De contat (02.02, C2 3.7, 4.4; macheta c2-04): documentele care nu au putut fi contate, grupate pe motiv, cu lantul
 * incercat; reincercarea unui rand sau a tuturor, ignorarea cu motiv. Reincercarea automata ruleaza oricum la 15 minute.
 */
export function PostingQueuePage() {
  const user = useOutletContext<CurrentUser>();
  const can = (permission: string) => user.permissions.includes(permission);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [selected, setSelected] = useState<number | undefined>();
  const [error, setError] = useState<string | undefined>();
  const queue = useQuery({ queryKey: queueKeys.all, queryFn: async () => unwrap(await api.GET('/api/finance/posting-queue', { params: { query: { status: 1 } } })) });
  const items = queue.data ?? [];
  const current = items.find((i) => i.id === selected);
  const count = (reason: number) => items.filter((i) => i.reason === reason).length;

  const retry = async (id: number | null) => {
    setError(undefined);
    try {
      const result = unwrap(await api.POST('/api/finance/posting-queue/retry', { body: { id, reason: null } }));
      notify(t('queue.retried', { posted: result.posted, left: result.stillQueued }));
      await queryClient.invalidateQueries({ queryKey: queueKeys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.postingQueue')}</div>
      <div className="head">
        <div><h1>{t('queue.title')}</h1><div className="sub2">{t('queue.subtitle')}</div></div>
        <div className="actions">
          {can('Fin.Posting.Retry') && items.length > 0 && <button type="button" className="btn" onClick={() => void retry(null)}>{t('queue.retryAll')}</button>}
        </div>
      </div>
      <ErrorBanner message={queue.isError ? queue.error.message : error} />
      <div className="kpi-row">
        <div className="kpi"><div className="kpi-label">{t('queue.waiting')}</div><div className={items.length > 0 ? 'kpi-value bad' : 'kpi-value'}>{items.length}</div></div>
        {[1, 3, 4].map((r) => <div key={r} className="kpi"><div className="kpi-label">{t(reasonKey(r))}</div><div className="kpi-value">{count(r)}</div></div>)}
      </div>
      {items.length === 0 ? <p className="muted">{t('queue.empty')}</p> : (
        <div className="split">
          <section className="card" aria-label={t('queue.title')}>
            <table className="partner-list cash-list">
              <thead><tr><th scope="col">{t('queue.document')}</th><th scope="col">{t('queue.date')}</th><th scope="col">{t('queue.event')}</th></tr></thead>
              {groupByReason(items).map(([reason, group]) => (
                <tbody key={reason}>
                  <tr className="group-row"><th scope="colgroup" colSpan={3}>{t(reasonKey(reason))}</th></tr>
                  {group.map((i) => (
                    <tr key={i.id} className={i.id === selected ? 'picked' : undefined} onClick={() => setSelected(i.id)}>
                      <td><span className="strong">{i.sourceDocumentNumber ?? i.sourceDocumentType ?? i.eventType}</span>
                        <div className="muted small">{(i.details ?? '').length > 90 ? `${(i.details ?? '').slice(0, 90)}…` : i.details}</div></td>
                      <td>{isoToRo(i.accountingDate)}</td>
                      <td className="small">{i.eventType}</td>
                    </tr>))}
                </tbody>
              ))}
            </table>
          </section>
          {current !== undefined && <QueueDetail key={current.id} item={current} can={can} onRetry={() => void retry(current.id)} />}
        </div>
      )}
    </div>
  );
}

function QueueDetail({ item, can, onRetry }: { readonly item: QueueItem; readonly can: (permission: string) => boolean; readonly onRetry: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | undefined>();
  const ignore = async () => {
    setError(undefined);
    try {
      unwrap(await api.POST('/api/finance/posting-queue/{id}/ignore', { params: { path: { id: item.id } }, body: { id: item.id, reason: reason.trim(), rowVersion: item.rowVersion } }));
      notify(t('queue.ignored'));
      await queryClient.invalidateQueries({ queryKey: queueKeys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  return (
    <section className="card" aria-label={item.sourceDocumentNumber ?? item.eventType}>
      <h2>{item.sourceDocumentNumber ?? item.sourceDocumentType ?? item.eventType} <span className="pill bad">{t(reasonKey(item.reason))}</span></h2>
      <p className="muted small">{t('queue.meta', { event: item.eventType, date: isoToRo(item.accountingDate), attempts: item.attempts })}</p>
      <h3>{t('queue.where')}</h3>
      <p>{item.details ?? ''}</p>
      <p className="muted small">{t(`queue.hint.${item.reason}` as TextKey)}</p>
      <ErrorBanner message={error} />
      <div className="actions">
        {can('Fin.Posting.Retry') && <button type="button" className="btn primary" onClick={onRetry}>{t('queue.retry')}</button>}
      </div>
      {can('Fin.Posting.Ignore') && (
        <div className="settings-form">
          <label>{t('queue.ignoreReason')}<input className="cell-input wide-input" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
          <div className="actions"><button type="button" className="btn" disabled={reason.trim() === ''} onClick={() => void ignore()}>{t('queue.ignore')}</button></div>
        </div>
      )}
    </section>
  );
}
