import { useMemo, useState } from 'react';
import { useOutletContext, useSearchParams } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { useDimensions } from '../dimensions/dimensionsApi';
import { ErrorBanner } from '../components/Field';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { formatAmount, monthRange } from './cashApi';

/** Soldul (debit − credit) afisat cu partea lui: „1.200,00 D” sau „300,00 C”, zero fara parte. */
export function sideBalance(value: string): string {
  const n = Number(value);
  if (n === 0) {
    return formatAmount('0');
  }
  return `${formatAmount(n > 0 ? value : value.replace('-', ''))} ${n > 0 ? 'D' : 'C'}`;
}

/**
 * Rapoarte › Fisa contului (02.02, C5 3.3; macheta c5-02): soldul initial al lunii, rulajele, soldul final si fiecare linie de nota cu soldul
 * dupa ea; filtrele pe centrul de cost si pe notele de inchidere. Contul si luna vin si din balanta (parametrii cont si luna).
 */
export function AccountLedgerPage() {
  const user = useOutletContext<CurrentUser>();
  const [params, setParams] = useSearchParams();
  const account = params.get('cont') ?? '';
  const month = params.get('luna') ?? new Date().toISOString().slice(0, 7);
  const [draft, setDraft] = useState(account);
  const [costCenterId, setCostCenterId] = useState<number | undefined>();
  const [withClosing, setWithClosing] = useState(true);
  const accounts = useQuery({ queryKey: ['finance', 'gl-accounts'], queryFn: async () => unwrap(await api.GET('/api/finance/gl-accounts')) });
  const centers = useDimensions({ type: 'COST_CENTER', companyId: user.companyId, search: '', includeInactive: false });
  const codes = useMemo(() => (accounts.data ?? []).filter((a) => a.isActive), [accounts.data]);
  const range = monthRange(month);
  const ledger = useQuery({
    queryKey: ['finance', 'account-ledger', account, month, costCenterId ?? 0, withClosing],
    enabled: account !== '',
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/reports/account-ledger', {
      params: { query: { accountCode: account, from: range.from, to: range.to, includeClosingEntries: withClosing, take: 2000,
        ...(costCenterId === undefined ? {} : { costCenterId }) } },
      signal,
    })),
    placeholderData: keepPreviousData,
  });
  const l = ledger.data;
  const set = (key: string, value: string) => { const next = new URLSearchParams(params); next.set(key, value); setParams(next); };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.reports')} › {t('menu.accountLedger')}</div>
      <div className="head"><div><h1>{t('ledger.title')}{account === '' ? '' : ` ${account}`}</h1><div className="sub2">{l?.accountName ?? t('ledger.subtitle')}</div></div></div>
      <section className="card">
        <div className="chips filter-row" role="group" aria-label={t('balance.filters')}>
          <datalist id="ledger-accounts">{codes.map((a) => <option key={a.id} value={a.code}>{a.name}</option>)}</datalist>
          <input className="cell-input mono" list="ledger-accounts" aria-label={t('entry.account')} placeholder={t('entry.account')} value={draft}
            onChange={(e) => setDraft(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') set('cont', draft.trim()); }} onBlur={() => { if (draft.trim() !== account) set('cont', draft.trim()); }} />
          <input className="cell-input" type="month" aria-label={t('entry.month')} value={month} onChange={(e) => set('luna', e.target.value)} />
          <select className="cell-input" aria-label={t('manual.costCenter')} value={costCenterId ?? ''} onChange={(e) => setCostCenterId(e.target.value === '' ? undefined : Number(e.target.value))}>
            <option value="">{t('ledger.allCostCenters')}</option>
            {(centers.data?.values ?? []).map((c) => <option key={c.id} value={c.id}>{c.code} · {c.name}</option>)}
          </select>
          <button type="button" className={withClosing ? 'chip on' : 'chip'} aria-pressed={withClosing} onClick={() => setWithClosing(!withClosing)}>{t('balance.withClosing')}</button>
        </div>
      </section>
      <ErrorBanner message={ledger.isError ? ledger.error.message : undefined} />
      {account === '' ? <p className="muted">{t('ledger.pick')}</p> : l !== undefined && (
        <>
          <div className="kpi-row">
            <div className="kpi"><div className="kpi-label">{t('ledger.opening', { date: isoToRo(range.from) })}</div><div className="kpi-value">{sideBalance(l.opening)}</div></div>
            <div className="kpi"><div className="kpi-label">{t('ledger.debit')}</div><div className="kpi-value">{formatAmount(l.periodDebit)}</div></div>
            <div className="kpi"><div className="kpi-label">{t('ledger.credit')}</div><div className="kpi-value">{formatAmount(l.periodCredit)}</div></div>
            <div className="kpi"><div className="kpi-label">{t('ledger.closing', { date: isoToRo(range.to) })}</div><div className="kpi-value">{sideBalance(l.closing)}</div></div>
          </div>
          <section className="card" aria-label={t('ledger.title')}>
            {l.lines.length === 0 ? <p className="muted">{t('ledger.none')}</p> : (
              <table className="partner-list cash-list">
                <thead><tr><th scope="col">{t('queue.date')}</th><th scope="col">{t('entry.entry')}</th><th scope="col">{t('ledger.document')}</th>
                  <th scope="col">{t('ledger.dimensions')}</th><th scope="col" className="num">{t('entry.debit')}</th><th scope="col" className="num">{t('entry.credit')}</th>
                  <th scope="col" className="num">{t('ledger.balance')}</th></tr></thead>
                <tbody>{l.lines.map((x) => (
                  <tr key={x.lineId} className={x.sourceKind === 4 ? 'strong' : undefined}>
                    <td>{isoToRo(x.accountingDate)}</td>
                    <td className="mono strong">{x.displayNumber ?? ''}</td>
                    <td>{x.sourceDocumentNumber ?? x.sourceDocumentType ?? ''}{x.accountCode !== account ? <span className="muted mono"> · {x.accountCode}</span> : null}
                      <div className="muted small">{x.description ?? ''}</div></td>
                    <td className="small">{[x.partnerName, x.costCenterCode, x.projectCode].filter((v) => v != null).join(' · ')}</td>
                    <td className="num">{Number(x.debit) === 0 ? '' : formatAmount(x.debit)}</td>
                    <td className="num">{Number(x.credit) === 0 ? '' : formatAmount(x.credit)}</td>
                    <td className="num strong">{sideBalance(x.balance)}</td>
                  </tr>))}</tbody>
              </table>
            )}
            <p className="muted small">{t('ledger.count', { count: l.lineCount })}</p>
          </section>
        </>
      )}
    </div>
  );
}
