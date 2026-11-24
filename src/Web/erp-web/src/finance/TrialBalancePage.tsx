import { useState } from 'react';
import { useNavigate } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { t, type TextKey } from '../i18n';
import { formatAmount, monthRange } from './cashApi';
import { useAltCharts } from './AltChartsPage';

type Row = components['schemas']['TrialBalanceRowDto'];

/** Clasa unui cont (prima cifra), pentru titlurile de grup ale balantei; „Nemapat” are clasa goala. */
export function accountClass(code: string): string {
  return /^\d/.test(code) ? code.charAt(0) : '';
}

/** Randul are sold final sau rulaj (filtrul „Doar cu sold” pastreaza randurile cu sold final diferit de zero). */
export function hasClosing(row: Pick<Row, 'closingDebit' | 'closingCredit'>): boolean {
  return Number(row.closingDebit) !== 0 || Number(row.closingCredit) !== 0;
}

const amount = (value: string) => (Number(value) === 0 ? '' : formatAmount(value));
const pairs = [['openingDebit', 'openingCredit'], ['priorDebit', 'priorCredit'], ['periodDebit', 'periodCredit'], ['totalDebit', 'totalCredit'],
  ['closingDebit', 'closingCredit']] as const;

/**
 * Rapoarte › Balanta de verificare (02.02, C5 3.2, 4.1; macheta c5-01): cele 10 coloane pe luna aleasa, pe nivel, pe planul romanesc sau
 * pe un plan alternativ, grupata optional pe partener sau centru de cost, cu sau fara notele de inchidere, si verificarile egalitatilor. Clic pe
 * un cont deschide fisa lui.
 */
export function TrialBalancePage() {
  const navigate = useNavigate();
  const charts = useAltCharts();
  const [month, setMonth] = useState(new Date().toISOString().slice(0, 7));
  const [level, setLevel] = useState(3);
  const [chartId, setChartId] = useState<number | undefined>();
  const [byPartner, setByPartner] = useState(false);
  const [byCostCenter, setByCostCenter] = useState(false);
  const [withClosing, setWithClosing] = useState(true);
  const [onlyBalance, setOnlyBalance] = useState(false);
  const range = monthRange(month);
  const balance = useQuery({
    queryKey: ['finance', 'trial-balance', month, level, chartId ?? 0, byPartner, byCostCenter, withClosing],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/reports/trial-balance', {
      params: { query: { from: range.from, to: range.to, level, includeClosingEntries: withClosing, byPartner, byCostCenter, ...(chartId === undefined ? {} : { chartId }) } },
      signal,
    })),
    placeholderData: keepPreviousData,
  });
  const b = balance.data;
  const rows = (b?.rows ?? []).filter((r) => !onlyBalance || hasClosing(r));
  const toggle = (on: boolean, set: (value: boolean) => void, label: TextKey) => (
    <button type="button" className={on ? 'chip on' : 'chip'} aria-pressed={on} onClick={() => set(!on)}>{t(label)}</button>
  );

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.reports')} › {t('menu.trialBalance')}</div>
      <div className="head"><div><h1>{t('balance.title')}</h1><div className="sub2">{t('balance.subtitle')}</div></div></div>
      <section className="card" aria-label={t('balance.title')}>
        <div className="chips filter-row" role="group" aria-label={t('balance.filters')}>
          <input className="cell-input" type="month" aria-label={t('entry.month')} value={month} onChange={(e) => setMonth(e.target.value)} />
          <select className="cell-input" aria-label={t('balance.chart')} value={chartId ?? ''} onChange={(e) => setChartId(e.target.value === '' ? undefined : Number(e.target.value))}>
            <option value="">{t('balance.statutory')}</option>
            {(charts.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.code} · {c.name}</option>)}
          </select>
          {[1, 2, 3].map((l) => (
            <button key={l} type="button" className={level === l ? 'chip on' : 'chip'} disabled={chartId !== undefined} onClick={() => setLevel(l)}>
              {t(`balance.level.${String(l)}` as TextKey)}</button>
          ))}
          {toggle(byPartner, setByPartner, 'balance.byPartner')}
          {toggle(byCostCenter, setByCostCenter, 'balance.byCostCenter')}
          {toggle(withClosing, setWithClosing, 'balance.withClosing')}
          {toggle(onlyBalance, setOnlyBalance, 'balance.onlyBalance')}
        </div>
        <ErrorBanner message={balance.isError ? balance.error.message : undefined} />
        <div className="table-scroll">
          <table className="partner-list cash-list balance-table">
            <thead>
              <tr><th scope="col" rowSpan={2}>{t('entry.account')}</th>
                {(['opening', 'prior', 'period', 'totals', 'closing'] as const).map((c) => <th key={c} scope="colgroup" colSpan={2} className="num">{t(`balance.col.${c}` as TextKey)}</th>)}</tr>
              <tr>{pairs.map(([d, c]) => [<th key={d} scope="col" className="num">{t('entry.debit')}</th>, <th key={c} scope="col" className="num">{t('entry.credit')}</th>])}</tr>
            </thead>
            <tbody>
              {rows.map((r, i) => {
                const cls = accountClass(r.accountCode);
                const header = i === 0 || accountClass(rows[i - 1]?.accountCode ?? '') !== cls;
                return [
                  header && <tr key={`h${cls}`} className="group-row"><th scope="colgroup" colSpan={11}>{cls === '' ? t('balance.unmapped') : t('balance.class', { cls })}</th></tr>,
                  <tr key={`${r.accountCode}-${String(r.partnerId)}-${String(r.costCenterId)}`} className={chartId === undefined ? 'clickable' : undefined}
                    onClick={() => { if (chartId === undefined && r.accountCode !== '') void navigate(`/finante/rapoarte/fisa-contului?cont=${encodeURIComponent(r.accountCode)}&luna=${month}`); }}>
                    <td><span className="mono strong">{r.accountCode}</span> <span className="muted">{r.accountName}</span>
                      {(r.partnerId != null || r.costCenterId != null) && <span className="muted small"> · #{r.partnerId ?? r.costCenterId}</span>}</td>
                    {pairs.map(([d, c]) => [<td key={d} className="num">{amount(r[d])}</td>, <td key={c} className="num">{amount(r[c])}</td>])}
                  </tr>,
                ];
              })}
              {b !== undefined && (
                <tr className="strong">
                  <td>{t('balance.total', { count: rows.length })}</td>
                  {pairs.map(([d, c]) => [<td key={d} className="num">{formatAmount(b.total[d])}</td>, <td key={c} className="num">{formatAmount(b.total[c])}</td>])}
                </tr>
              )}
            </tbody>
          </table>
        </div>
        {b !== undefined && (
          <p className="small">{b.checks.map((c) => (
            <span key={c.name} className={c.passed ? 'check-ok' : 'check-bad'}>{c.passed ? '✓' : '✗'} {t(`balance.check.${c.name}` as TextKey)}{c.passed ? '' : ` (${formatAmount(c.difference)})`} · </span>
          ))}</p>
        )}
      </section>
    </div>
  );
}
