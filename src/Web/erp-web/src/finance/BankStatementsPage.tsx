import { useMemo, useRef, useState } from 'react';
import { Link, useOutletContext } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { bankApi, bankKeys, Continuity, sourceLabel, StatementStatus, statementStatusLabel, useBankAccounts, useBankStatements, type BankImportResult,
  type BankStatementItem } from './bankApi';
import { formatAmount } from './cashApi';

/**
 * Bancă › Extrase bancare (02.02, F2 3.3 și 4.1; macheta f2-01): extrasele pe cont și dată, cu soldurile, liniile (nevalidate),
 * continuitatea și starea; importul unui fișier CAMT.053 / MT940 / CSV, cu rezultatul ultimului import, și acceptarea unui gol cu motiv.
 */
export function BankStatementsPage() {
  const user = useOutletContext<CurrentUser>();
  const accounts = useBankAccounts();
  const [account, setAccount] = useState<number | undefined>();
  const [openOnly, setOpenOnly] = useState(false);
  const list = useBankStatements(account);
  const bankAccounts = useMemo(() => accounts.data ?? [], [accounts.data]);
  const [last, setLast] = useState<BankImportResult | undefined>();
  const statements = (list.data ?? []).filter((s) => !openOnly || s.lineCount > s.validatedCount);
  const gaps = statements.filter((s) => s.continuityStatus === Continuity.Gap);
  const canImport = user.permissions.includes('Fin.BankStatement.Import');

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.bank')} › {t('menu.bankStatements')}</div>
      <div className="head">
        <div>
          <h1>{t('bank.statements.title')}</h1>
          <div className="sub2">{t('bank.statements.subtitle')}</div>
        </div>
        <div className="actions">
          <Link to="/finante/banca/extras-de-cont" className="btn ghost">{t('menu.bankReconcile')}</Link>
        </div>
      </div>
      <div className="split cash-document">
        <section className="card" aria-label={t('bank.statements.title')}>
          <div className="chips-row" role="group" aria-label={t('bank.account')}>
            <button type="button" className={account === undefined ? 'chip on' : 'chip'} aria-pressed={account === undefined}
              onClick={() => setAccount(undefined)}>{t('bank.allAccounts')}</button>
            {bankAccounts.map((a) => (
              <button key={a.id} type="button" className={account === a.id ? 'chip on' : 'chip'} aria-pressed={account === a.id}
                onClick={() => setAccount(a.id)}>{a.code}</button>
            ))}
            <button type="button" className={openOnly ? 'chip on' : 'chip'} aria-pressed={openOnly} onClick={() => setOpenOnly(!openOnly)}>
              {t('bank.withOpenLines')}
            </button>
          </div>
          {list.isError && <ErrorBanner message={list.error.message} />}
          {list.data !== undefined && statements.length === 0 ? <p className="muted">{t('bank.statements.empty')}</p> : (
            <table className="partner-list cash-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('bank.col.statement')}</th>
                  <th scope="col">{t('bank.col.date')}</th>
                  <th scope="col" className="num">{t('bank.col.opening')}</th>
                  <th scope="col" className="num">{t('bank.col.closing')}</th>
                  <th scope="col" className="num">{t('bank.col.lines')}</th>
                  <th scope="col">{t('bank.col.continuity')}</th>
                  <th scope="col">{t('bank.col.status')}</th>
                </tr>
              </thead>
              <tbody>
                {statements.map((s) => <StatementRow key={s.id} statement={s} canAccept={user.permissions.includes('Fin.BankStatement.AcceptGap')} />)}
              </tbody>
            </table>
          )}
          {gaps.map((g) => (
            <div key={g.id} className="banner bad" role="alert">{t('bank.gapBanner', { account: g.accountCode, number: g.statementNo ?? String(g.id) })}</div>
          ))}
        </section>
        <aside className="card cash-side">
          {canImport ? <ImportPanel accounts={bankAccounts} onImported={setLast} /> : <p className="muted">{t('bank.import.noRight')}</p>}
          {last !== undefined && <LastImport result={last} />}
        </aside>
      </div>
    </div>
  );
}

function StatementRow({ statement: s, canAccept }: { readonly statement: BankStatementItem; readonly canAccept: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const open = s.lineCount - s.validatedCount;
  const accept = async () => {
    const reason = window.prompt(t('bank.acceptGap.prompt'));
    if (reason === null || reason.trim() === '') {
      return;
    }
    try {
      await bankApi.acceptGap(s.id, reason.trim(), s.rowVersion);
      notify(t('bank.acceptGap.done'));
      await queryClient.invalidateQueries({ queryKey: bankKeys.all });
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };
  return (
    <tr>
      <td>
        <strong>{s.accountCode}</strong>
        <div className="muted small">{[s.statementNo === null || s.statementNo === undefined ? undefined : `nr. ${s.statementNo}`, sourceLabel(s.source)]
          .filter(Boolean).join(' · ')}</div>
      </td>
      <td className="small">{isoToRo(s.statementDate)}</td>
      <td className="num">{formatAmount(s.openingBalance)}</td>
      <td className="num"><strong>{formatAmount(s.closingBalance)}</strong></td>
      <td className="num">{s.lineCount} · <span className={open > 0 ? 'neg' : undefined}>{open}</span></td>
      <td>
        {s.continuityStatus === Continuity.Gap ? (
          <>
            <span className="pill bad">{t('bank.continuity.gap')}</span>
            {canAccept && <button type="button" className="btn ghost small" onClick={() => void accept()}>{t('bank.acceptGap')}</button>}
          </>
        ) : <span className="pill ok">{t(s.continuityStatus === Continuity.GapAccepted ? 'bank.continuity.accepted' : 'bank.continuity.ok')}</span>}
      </td>
      <td><span className={s.status === StatementStatus.Closed ? 'pill ok' : s.status === StatementStatus.Reconciling ? 'pill info' : 'pill'}>
        {t(statementStatusLabel(s.status))}</span></td>
    </tr>
  );
}

function ImportPanel({ accounts, onImported }: { readonly accounts: readonly { id: number; code: string; name: string }[];
  readonly onImported: (result: BankImportResult) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const input = useRef<HTMLInputElement>(null);
  const [account, setAccount] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const upload = async (file: File | undefined) => {
    if (file === undefined) {
      return;
    }
    setBusy(true);
    setError(undefined);
    try {
      const result = await bankApi.importFile(account, file);
      onImported(result);
      notify(t('bank.import.done', { count: result.statements.length }));
      await queryClient.invalidateQueries({ queryKey: bankKeys.all });
    } catch (e) {
      setError(e instanceof Error ? e.message : t('common.notSaved'));
    } finally {
      setBusy(false);
      if (input.current !== null) input.current.value = '';
    }
  };
  return (
    <div className="bank-import">
      <h2>{t('bank.import.title')}</h2>
      <label className="drop-zone" onDragOver={(e) => e.preventDefault()} onDrop={(e) => { e.preventDefault(); void upload(e.dataTransfer.files[0]); }}>
        <span>⇧</span>
        <strong>{t('bank.import.drop')}</strong>
        <span className="muted small">{t('bank.import.formats')}</span>
        <input ref={input} type="file" accept=".xml,.sta,.txt,.940,.csv" className="visually-hidden" disabled={busy}
          aria-label={t('bank.import.file')} onChange={(e) => void upload(e.target.files?.[0])} />
      </label>
      <label>{t('bank.account')}
        <select className="cell-input" value={account ?? ''} onChange={(e) => setAccount(e.target.value === '' ? null : Number(e.target.value))}>
          <option value="">{t('bank.import.byIban')}</option>
          {accounts.map((a) => <option key={a.id} value={a.id}>{`${a.code} · ${a.name}`}</option>)}
        </select>
      </label>
      {busy && <p role="status" className="muted">{t('bank.import.busy')}</p>}
      <ErrorBanner message={error} />
    </div>
  );
}

function LastImport({ result }: { readonly result: BankImportResult }) {
  return (
    <div className="bank-last">
      <h2>{t('bank.import.last')} <span className="pill ok">{t('bank.import.ok')}</span></h2>
      {result.statements.map((s) => (
        <dl key={s.bankStatementId} className="kv">
          <dt>{t('bank.import.statement', { number: s.statementNo ?? String(s.bankStatementId), date: isoToRo(s.statementDate) })}</dt>
          <dd>{t('bank.import.lines', { count: s.lineCount })}</dd>
          <dt>{t('bank.import.skipped')}</dt>
          <dd>{s.skippedCount}</dd>
          <dt>{t('bank.col.continuity')}</dt>
          <dd>{s.continuityStatus === Continuity.Gap ? <span className="pill bad">{t('bank.continuity.gap')}</span> : '✓'}</dd>
        </dl>
      ))}
      <Link to={`/finante/banca/extras-de-cont?cont=${result.statements[0]?.treasuryAccountId ?? ''}`} className="btn primary">{t('bank.openReconcile')}</Link>
    </div>
  );
}
