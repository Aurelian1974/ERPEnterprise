import { useMemo, useState } from 'react';
import { useNavigate, useOutletContext, useParams } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { useDimensions } from '../dimensions/dimensionsApi';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { formatAmount } from './cashApi';
import { balance, formula, fromCents, toCents } from './entryModel';
import { entryKeys } from './JournalEntriesPage';

type Entry = components['schemas']['JournalEntryDto'];

interface Row {
  readonly key: number;
  readonly accountCode: string;
  readonly side: number;
  readonly amountRon: string;
  readonly costCenterId: number | undefined;
  readonly projectId: number | undefined;
  readonly description: string;
}

let rowKey = 0;
const emptyRow = (side: number): Row => ({ key: ++rowKey, accountCode: '', side, amountRon: '', costCenterId: undefined, projectId: undefined, description: '' });

/**
 * Contabilitate › Nota manuala (02.02, C2 3.8, 4.3; macheta c2-02): jurnalul, data contabila, justificativul si liniile, cu echilibrul si
 * conturile necunoscute verificate pe loc; salvarea ca ciorna si validarea (verificarile pe set, numarul din jurnal). Partenerul pe linie
 * vine cu selectorul de parteneri al notelor (RAMAS-DE-FACUT).
 */
export function ManualEntryPage() {
  const { id } = useParams();
  const entryId = id === undefined ? undefined : Number(id);
  const existing = useQuery({
    queryKey: [...entryKeys.all, entryId ?? 0],
    enabled: entryId !== undefined,
    queryFn: async () => unwrap(await api.GET('/api/finance/journal-entries/{id}', { params: { path: { id: entryId ?? 0 } } })),
  });
  if (entryId !== undefined && existing.data === undefined) {
    return <div className="page"><ErrorBanner message={existing.isError ? existing.error.message : undefined} /></div>;
  }
  return <Editor key={entryId ?? 0} entry={existing.data} />;
}

function Editor({ entry }: { readonly entry: Entry | undefined }) {
  const user = useOutletContext<CurrentUser>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const journals = useQuery({ queryKey: ['finance', 'journals'], queryFn: async () => unwrap(await api.GET('/api/finance/journals')) });
  const accounts = useQuery({ queryKey: ['finance', 'gl-accounts'], queryFn: async () => unwrap(await api.GET('/api/finance/gl-accounts')) });
  const centers = useDimensions({ type: 'COST_CENTER', companyId: user.companyId, search: '', includeInactive: false });
  const projects = useDimensions({ type: 'PROJECT', companyId: user.companyId, search: '', includeInactive: false });
  const postable = useMemo(() => new Map((accounts.data ?? []).filter((a) => a.isPostable && a.isActive).map((a) => [a.code, a])), [accounts.data]);
  const [journalId, setJournalId] = useState<number | undefined>(entry?.journalId);
  const [date, setDate] = useState(entry?.accountingDate ?? new Date().toISOString().slice(0, 10));
  const [description, setDescription] = useState(entry?.description ?? '');
  const [documentRef, setDocumentRef] = useState(entry?.sourceDocumentNumber ?? '');
  const [rows, setRows] = useState<Row[]>(() => entry === undefined ? [emptyRow(1), emptyRow(2)] : entry.lines.map((l) => ({
    key: ++rowKey, accountCode: l.accountCode, side: l.side, amountRon: l.amountRon, costCenterId: l.costCenterId ?? undefined,
    projectId: l.projectId ?? undefined, description: l.description ?? '',
  })));
  const [error, setError] = useState<string | undefined>();
  const journal = journalId ?? journals.data?.find((j) => j.code === 'DV')?.id;
  const totals = balance(rows);
  const unknown = rows.filter((r) => r.accountCode.trim() !== '' && !postable.has(r.accountCode.trim()));
  const update = (key: number, change: Partial<Row>) => setRows((all) => all.map((r) => r.key === key ? { ...r, ...change } : r));

  const save = async (validate: boolean) => {
    setError(undefined);
    const lines = rows.filter((r) => r.accountCode.trim() !== '' || r.amountRon.trim() !== '').map((r) => ({
      accountId: postable.get(r.accountCode.trim())?.id ?? 0, side: r.side, amountRon: fromCents(toCents(r.amountRon) ?? 0),
      costCenterId: r.costCenterId ?? null, projectId: r.projectId ?? null, description: r.description.trim() === '' ? null : r.description.trim(),
    }));
    if (journal === undefined || unknown.length > 0 || lines.some((l) => l.accountId === 0)) {
      setError(t('manual.fixAccounts'));
      return;
    }
    try {
      const body = { id: entry?.id ?? null, journalId: journal, accountingDate: date, description: description.trim() === '' ? null : description.trim(),
        documentRef: documentRef.trim() === '' ? null : documentRef.trim(), lines, isReclassification: false, rowVersion: entry?.rowVersion ?? null };
      const saved = entry === undefined
        ? unwrap(await api.POST('/api/finance/journal-entries', { body }))
        : unwrap(await api.PUT('/api/finance/journal-entries/{id}', { params: { path: { id: entry.id } }, body }));
      if (validate) {
        unwrap(await api.POST('/api/finance/journal-entries/{id}/validate', { params: { path: { id: saved.id } }, body: { id: saved.id, rowVersion: saved.rowVersion } }));
      }
      notify(t(validate ? 'entry.validated' : 'manual.saved'));
      await queryClient.invalidateQueries({ queryKey: entryKeys.all });
      void navigate('/finante/contabilitate/note');
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.journalEntries')} › {t('manual.title')}</div>
      <div className="head">
        <div><h1>{entry?.displayNumber ?? t('manual.title')}</h1><div className="sub2">{t('manual.subtitle')}</div></div>
      </div>
      <section className="card settings-form">
        <div className="row-between">
          <label>{t('entry.journal')}
            <select className="cell-input" value={journal ?? ''} onChange={(e) => setJournalId(Number(e.target.value))}>
              {(journals.data ?? []).map((j) => <option key={j.id} value={j.id}>{j.code} · {j.name}</option>)}
            </select>
          </label>
          <label>{t('manual.date')}<input className="cell-input" type="date" value={date} onChange={(e) => setDate(e.target.value)} /></label>
          <label>{t('manual.documentRef')}<input className="cell-input" maxLength={50} value={documentRef} onChange={(e) => setDocumentRef(e.target.value)} /></label>
        </div>
        <label>{t('manual.description')}<input className="cell-input wide-input" maxLength={400} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
      </section>
      <section className="card" aria-label={t('manual.lines')}>
        <datalist id="postable-accounts">{[...postable.values()].map((a) => <option key={a.id} value={a.code}>{a.name}</option>)}</datalist>
        <table className="partner-list cash-list">
          <thead><tr><th scope="col">{t('entry.account')}</th><th scope="col">{t('manual.side')}</th><th scope="col" className="num">{t('manual.amount')}</th>
            <th scope="col">{t('manual.costCenter')}</th><th scope="col">{t('manual.project')}</th><th scope="col">{t('manual.lineText')}</th><th scope="col" /></tr></thead>
          <tbody>{rows.map((r) => {
            const account = postable.get(r.accountCode.trim());
            return (
              <tr key={r.key}>
                <td><input className="cell-input mono" list="postable-accounts" aria-label={t('entry.account')} value={r.accountCode}
                  onChange={(e) => update(r.key, { accountCode: e.target.value })} />
                  <div className="muted small">{account?.name ?? (r.accountCode.trim() === '' ? '' : t('manual.unknownAccount'))}</div></td>
                <td><select className="cell-input" aria-label={t('manual.side')} value={r.side} onChange={(e) => update(r.key, { side: Number(e.target.value) })}>
                  <option value={1}>{t('entry.debit')}</option><option value={2}>{t('entry.credit')}</option></select></td>
                <td><input className="cell-input num" aria-label={t('manual.amount')} inputMode="decimal" value={r.amountRon}
                  onChange={(e) => update(r.key, { amountRon: e.target.value })} /></td>
                <td><select className="cell-input" aria-label={t('manual.costCenter')} value={r.costCenterId ?? ''}
                  onChange={(e) => update(r.key, { costCenterId: e.target.value === '' ? undefined : Number(e.target.value) })}>
                  <option value="" />{(centers.data?.values ?? []).map((c) => <option key={c.id} value={c.id}>{c.code}</option>)}</select></td>
                <td><select className="cell-input" aria-label={t('manual.project')} value={r.projectId ?? ''}
                  onChange={(e) => update(r.key, { projectId: e.target.value === '' ? undefined : Number(e.target.value) })}>
                  <option value="" />{(projects.data?.values ?? []).map((c) => <option key={c.id} value={c.id}>{c.code}</option>)}</select></td>
                <td><input className="cell-input" aria-label={t('manual.lineText')} maxLength={400} value={r.description}
                  onChange={(e) => update(r.key, { description: e.target.value })} /></td>
                <td><button type="button" className="btn small" aria-label={t('entry.delete')} onClick={() => setRows((all) => all.filter((x) => x.key !== r.key))}>✕</button></td>
              </tr>
            );
          })}</tbody>
        </table>
        <div className="actions">
          <button type="button" className="btn" onClick={() => setRows((all) => [...all, emptyRow(1)])}>{t('manual.addDebit')}</button>
          <button type="button" className="btn" onClick={() => setRows((all) => [...all, emptyRow(2)])}>{t('manual.addCredit')}</button>
        </div>
        <div className="kpi-row">
          <div className="kpi"><div className="kpi-label">{t('entry.debit')}</div><div className="kpi-value">{formatAmount(fromCents(totals.debit))}</div></div>
          <div className="kpi"><div className="kpi-label">{t('entry.credit')}</div><div className="kpi-value">{formatAmount(fromCents(totals.credit))}</div></div>
          <div className="kpi"><div className="kpi-label">{t('manual.difference')}</div>
            <div className={totals.difference === 0 ? 'kpi-value ok' : 'kpi-value bad'}>{formatAmount(fromCents(totals.difference))}</div></div>
        </div>
        <pre className="entry-formula">{formula(rows.filter((r) => postable.has(r.accountCode.trim())).map((r) => ({ ...r, accountCode: r.accountCode.trim() }))).join('\n')}</pre>
        <p className="muted small">{t('manual.checksHint')}</p>
        <ErrorBanner message={error} />
        <div className="actions">
          <button type="button" className="btn" onClick={() => void navigate('/finante/contabilitate/note')}>{t('common.cancel')}</button>
          <button type="button" className="btn" onClick={() => void save(false)}>{t('manual.saveDraft')}</button>
          {user.permissions.includes('Fin.ManualEntry.Validate') && (
            <button type="button" className="btn primary" disabled={totals.difference !== 0 || totals.debit === 0} onClick={() => void save(true)}>
              {t('manual.saveAndValidate')}</button>
          )}
        </div>
      </section>
    </div>
  );
}
