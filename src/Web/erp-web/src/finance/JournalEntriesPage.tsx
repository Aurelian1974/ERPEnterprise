import { useState } from 'react';
import { Link, useNavigate, useOutletContext } from 'react-router';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { formatAmount, monthRange } from './cashApi';
import { formula } from './entryModel';

type Entry = components['schemas']['JournalEntryDto'];

export const entryKeys = { all: ['finance', 'journal-entries'] as const };
const statusPills: Record<number, string> = { 1: 'pill warn', 2: 'pill ok', 3: 'pill bad' };
const kindFilters = [undefined, 1, 2, 6] as const;

/** Tipul notei afisat in lista: ciorna, automata, manuala, reclasificare, storno, inchidere, deschidere. */
export function entryKindKey(sourceKind: number, status: number): TextKey {
  return status === 1 ? 'entry.kind.draft' : (`entry.kind.${sourceKind}` as TextKey);
}

/** Linia notei: de unde vine contul (C2 3.4), pentru notele automate. */
export function resolvedFromKey(value: number | null): TextKey | undefined {
  return value == null ? undefined : (`entry.from.${value}` as TextKey);
}

/**
 * Contabilitate › Note contabile (02.02, C2 4.3, 4.5; macheta c2-01): notele lunii cu filtre (jurnal, tip, cautare), fisa notei cu formula,
 * liniile si sursa contului, validarea si stergerea ciornelor, stornarea notelor manuale (si a celor automate, cu dreptul special).
 */
export function JournalEntriesPage() {
  const user = useOutletContext<CurrentUser>();
  const navigate = useNavigate();
  const can = (permission: string) => user.permissions.includes(permission);
  const [month, setMonth] = useState(new Date().toISOString().slice(0, 7));
  const [journalId, setJournalId] = useState<number | undefined>();
  const [kind, setKind] = useState<number | undefined>();
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<number | undefined>();
  const range = monthRange(month);
  const journals = useQuery({ queryKey: ['finance', 'journals'], queryFn: async () => unwrap(await api.GET('/api/finance/journals')) });
  const page = useQuery({
    queryKey: [...entryKeys.all, 'list', month, journalId ?? 0, kind ?? 0, search],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/journal-entries', {
      params: {
        query: {
          from: range.from, to: range.to, take: 200, ...(journalId === undefined ? {} : { journalId }), ...(kind === undefined ? {} : { sourceKind: kind }),
          ...(search.trim() === '' ? {} : { search: search.trim() }),
        },
      },
      signal,
    })),
    placeholderData: keepPreviousData,
  });

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.journalEntries')}</div>
      <div className="head">
        <div><h1>{t('entry.title')}</h1><div className="sub2">{t('entry.subtitle')}</div></div>
        <div className="actions">
          {can('Fin.ManualEntry.Create') && (
            <button type="button" className="btn primary" onClick={() => void navigate('/finante/contabilitate/note/noua')}>{t('entry.new')}</button>
          )}
        </div>
      </div>
      <ErrorBanner message={page.isError ? page.error.message : undefined} />
      <div className="split">
        <section className="card" aria-label={t('entry.title')}>
          <div className="row-between">
            <input className="cell-input" type="month" aria-label={t('entry.month')} value={month} onChange={(e) => { setMonth(e.target.value); setSelected(undefined); }} />
            <select className="cell-input" aria-label={t('entry.journal')} value={journalId ?? ''} onChange={(e) => setJournalId(e.target.value === '' ? undefined : Number(e.target.value))}>
              <option value="">{t('entry.allJournals')}</option>
              {(journals.data ?? []).map((j) => <option key={j.id} value={j.id}>{j.code} · {j.name}</option>)}
            </select>
          </div>
          <input className="cell-input wide-input" placeholder={t('entry.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
          <div className="chips" role="group" aria-label={t('entry.kind')}>
            {kindFilters.map((k) => (
              <button key={k ?? 0} type="button" className={kind === k ? 'chip on' : 'chip'} onClick={() => setKind(k)}>
                {k === undefined ? t('entry.kind.all') : t(`entry.kind.${k}` as TextKey)}
              </button>
            ))}
          </div>
          {(page.data?.items ?? []).length === 0 ? <p className="muted">{t('entry.none')}</p> : (
            <table className="partner-list cash-list">
              <thead><tr><th scope="col">{t('entry.entry')}</th><th scope="col" className="num">{t('entry.total')}</th><th scope="col">{t('entry.kind')}</th></tr></thead>
              <tbody>{(page.data?.items ?? []).map((e) => (
                <tr key={e.id} className={e.id === selected ? 'picked' : undefined} onClick={() => setSelected(e.id)}>
                  <td><span className="mono strong">{e.displayNumber ?? `${e.journalCode} —`}</span>
                    <div className="muted small">{isoToRo(e.accountingDate)} · {e.sourceDocumentNumber ?? e.description ?? ''}</div></td>
                  <td className="num">{formatAmount(e.totalRon)}</td>
                  <td><span className={statusPills[e.status] ?? 'pill'}>{t(entryKindKey(e.sourceKind, e.status))}</span></td>
                </tr>))}</tbody>
            </table>
          )}
          <p className="muted small">{t('entry.count', { shown: page.data?.items.length ?? 0, total: page.data?.total ?? 0 })}</p>
        </section>
        {selected !== undefined && <EntryDetail key={selected} id={selected} can={can} onGone={() => setSelected(undefined)} />}
      </div>
    </div>
  );
}

function EntryDetail({ id, can, onGone }: { readonly id: number; readonly can: (permission: string) => boolean; readonly onGone: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const [reversing, setReversing] = useState(false);
  const entry = useQuery({ queryKey: [...entryKeys.all, id], queryFn: async () => unwrap(await api.GET('/api/finance/journal-entries/{id}', { params: { path: { id } } })) });
  const e = entry.data;
  const run = async (action: () => Promise<unknown>, message: TextKey, after?: () => void) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: entryKeys.all });
      after?.();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('common.notSaved'));
    }
  };
  if (e === undefined) {
    return <section className="card"><ErrorBanner message={entry.isError ? entry.error.message : undefined} /></section>;
  }
  const manual = e.sourceKind === 2 || e.sourceKind === 3;
  const canReverse = e.status === 2 && can('Fin.JournalEntry.Reverse') && (manual || can('Fin.JournalEntry.ReverseAutomatic'));

  return (
    <section className="card" aria-label={e.displayNumber ?? t('entry.kind.draft')}>
      <h2><span className="mono">{e.displayNumber ?? e.journalCode}</span> {e.description ?? ''}{' '}
        <span className={statusPills[e.status] ?? 'pill'}>{t(`entry.status.${e.status}` as TextKey)}</span></h2>
      <p className="muted small">{t('entry.meta', { date: isoToRo(e.accountingDate), source: e.sourceDocumentType ?? t(entryKindKey(e.sourceKind, e.status)) })}
        {e.sourceDocumentNumber != null ? ` · ${e.sourceDocumentNumber}` : ''}{e.validatedByName != null ? ` · ${t('entry.validatedBy', { name: e.validatedByName })}` : ''}</p>
      {e.reversalOfId != null && <p className="small">{t('entry.reversalOf', { id: e.reversalOfId })}</p>}
      <pre className="entry-formula">{formula(e.lines).join('\n')}</pre>
      <table className="partner-list cash-list">
        <thead><tr><th scope="col">{t('entry.account')}</th><th scope="col" className="num">{t('entry.debit')}</th><th scope="col" className="num">{t('entry.credit')}</th>
          <th scope="col">{t('entry.dimensions')}</th><th scope="col">{t('entry.from')}</th></tr></thead>
        <tbody>
          {e.lines.map((l) => {
            const from = resolvedFromKey(l.resolvedFrom);
            return (
              <tr key={l.id}>
                <td><span className="mono strong">{l.accountCode}</span> {l.accountName}</td>
                <td className="num">{l.side === 1 ? formatAmount(l.amountRon) : ''}</td>
                <td className="num">{l.side === 2 ? formatAmount(l.amountRon) : ''}</td>
                <td className="small">{[l.partnerName, l.costCenterCode, l.projectCode, l.warehouseCode, l.siteCode].filter((x) => x != null).join(' · ')}</td>
                <td className="small">{from === undefined ? '' : t(from)}</td>
              </tr>
            );
          })}
          <tr className="strong"><td>{t('entry.total')}</td><td className="num">{formatAmount(e.totalDebit)}</td><td className="num">{formatAmount(e.totalCredit)}</td><td /><td /></tr>
        </tbody>
      </table>
      <ErrorBanner message={error} />
      {e.status === 1 && (
        <div className="actions">
          {can('Fin.ManualEntry.Create') && <Link className="btn" to={`/finante/contabilitate/note/${String(e.id)}`}>{t('common.edit')}</Link>}
          {can('Fin.ManualEntry.Create') && (
            <button type="button" className="btn" onClick={() => void run(async () => unwrap(await api.DELETE('/api/finance/journal-entries/{id}',
              { params: { path: { id: e.id } } })), 'entry.deleted', onGone)}>{t('entry.delete')}</button>
          )}
          {can('Fin.ManualEntry.Validate') && (
            <button type="button" className="btn primary" onClick={() => void run(async () => unwrap(await api.POST('/api/finance/journal-entries/{id}/validate',
              { params: { path: { id: e.id } }, body: { id: e.id, rowVersion: e.rowVersion } })), 'entry.validated')}>{t('entry.validate')}</button>
          )}
        </div>
      )}
      {e.status === 2 && !manual && !canReverse && <p className="muted small">{t('entry.reverseAtSource')}</p>}
      {canReverse && !reversing && <div className="actions"><button type="button" className="btn" onClick={() => setReversing(true)}>{t('entry.reverse')}</button></div>}
      {reversing && <ReverseForm entry={e} onDone={() => setReversing(false)} run={run} />}
    </section>
  );
}

function ReverseForm({ entry, onDone, run }: {
  readonly entry: Entry; readonly onDone: () => void;
  readonly run: (action: () => Promise<unknown>, message: TextKey, after?: () => void) => Promise<void>;
}) {
  const [date, setDate] = useState(entry.accountingDate);
  const [reason, setReason] = useState('');
  return (
    <div className="settings-form">
      <h3>{t('entry.reverseTitle')}</h3>
      <div className="row-between">
        <label>{t('entry.reverseDate')}<input className="cell-input" type="date" value={date} onChange={(e) => setDate(e.target.value)} /></label>
        <label>{t('period.reason')}<input className="cell-input wide-input" value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} /></label>
      </div>
      <div className="actions">
        <button type="button" className="btn" onClick={onDone}>{t('common.cancel')}</button>
        <button type="button" className="btn primary" disabled={reason.trim() === ''} onClick={() => void run(async () => unwrap(await api.POST(
          '/api/finance/journal-entries/{id}/reverse', { params: { path: { id: entry.id } }, body: { id: entry.id, accountingDate: date, reason: reason.trim() } })),
          'entry.reversed', onDone)}>{t('entry.reverse')}</button>
      </div>
    </div>
  );
}
