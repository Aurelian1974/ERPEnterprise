import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { formatAmount } from './cashApi';
import { signClass, signed } from './fxModel';

type Difference = components['schemas']['FxDifferenceDto'];
const sources: readonly { readonly value: number | undefined; readonly label: TextKey }[] = [
  { value: undefined, label: 'fx.diff.source.all' },
  { value: 1, label: 'fx.diff.source.1' },
  { value: 2, label: 'fx.diff.source.2' },
  { value: 4, label: 'fx.diff.source.4' },
];
const sourceLabel = (s: number): TextKey => (['fx.diff.source.1', 'fx.diff.source.2', 'fx.diff.source.3', 'fx.diff.source.4'] as const)[s - 1] ?? 'fx.diff.source.1';

/** Ce arata randul „Partener · document”: alocarea (factura ← documentul pereche) sau conturile schimbului valutar. */
export function differenceWhat(d: Pick<Difference, 'source' | 'partnerName' | 'documentNo' | 'counterDocumentNo' | 'accountCode' | 'counterAccountCode'>):
  readonly [string, string] {
  if (d.source === 2) return [`${d.counterAccountCode ?? ''} → ${d.accountCode ?? ''}`.trim(), ''];
  return [d.partnerName ?? '', [d.documentNo, d.counterDocumentNo].filter((x) => x != null && x !== '').join(' ← ')];
}

/**
 * Valută › Diferențe de curs (02.02, F4 3.2, 10; macheta f4-02): câștigurile și pierderile realizate pe interval, pe cont (765 / 768,
 * 665 / 668), corecțiile reevaluării, filtrele pe sursă, monedă și căutare și lista diferențelor. Doar citire.
 */
export function FxDifferencesPage() {
  const today = isoToday();
  const [from, setFrom] = useState(`${today.slice(0, 8)}01`);
  const [to, setTo] = useState(today);
  const [source, setSource] = useState<number | undefined>();
  const [currency, setCurrency] = useState('');
  const [search, setSearch] = useState('');
  const list = useQuery({
    queryKey: ['finance', 'fx', 'differences', from, to, source, currency, search],
    queryFn: async () => unwrap(await api.GET('/api/finance/fx/differences', { params: { query: {
      from, to, ...(source === undefined ? {} : { source }), ...(currency === '' ? {} : { currencyCode: currency }), ...(search.trim() === '' ? {} : { search }) } } })),
    placeholderData: keepPreviousData,
  });
  const d = list.data;
  const accounts = (codes: readonly string[]) => (d?.accounts ?? []).filter((a) => codes.includes(a.glAccountCode))
    .map((a) => `${a.glAccountCode} · ${formatAmount(a.differenceRon.replace('-', ''))}`).join('   ');

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.fxDifferences')}</div>
      <div className="head"><div><h1>{t('fx.diff.title')}</h1><div className="sub2">{t('fx.diff.subtitle')}</div></div></div>
      {d !== undefined && (
        <div className="kpi-row">
          <div className="kpi"><div className="kpi-label">{t('fx.diff.gain')}</div><div className="kpi-value">{formatAmount(d.gainRon)} <span className="muted small">RON</span></div>
            <div className="muted small">{accounts(['765', '768'])}</div></div>
          <div className="kpi bad"><div className="kpi-label">{t('fx.diff.loss')}</div><div className="kpi-value">{formatAmount(d.lossRon)} <span className="muted small">RON</span></div>
            <div className="muted small">{accounts(['665', '668'])}</div></div>
          <div className="kpi"><div className="kpi-label">{t('fx.diff.net')}</div><div className={`kpi-value ${signClass(d.netRon)}`}>{signed(d.netRon)} <span className="muted small">RON</span></div>
            <div className="muted small">{d.items.length}</div></div>
          <div className="kpi"><div className="kpi-label">{t('fx.diff.corrections')}</div><div className="kpi-value">{signed(d.correctionsRon)} <span className="muted small">RON</span></div></div>
        </div>
      )}
      <section className="card" aria-label={t('fx.diff.title')}>
        <div className="toolbar-row">
          <input className="cell-input search" type="search" placeholder={t('fx.diff.search')} aria-label={t('fx.diff.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
          <label className="inline">{t('fx.diff.from')} <input className="cell-input" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
          <label className="inline">{t('fx.diff.to')} <input className="cell-input" type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
          <label className="inline">{t('fx.diff.currency')} <input className="cell-input" maxLength={3} size={4} value={currency} onChange={(e) => setCurrency(e.target.value.toUpperCase())} /></label>
        </div>
        <div className="chips-row" role="group" aria-label={t('fx.diff.col.source')}>
          {sources.map((s) => (
            <button key={s.label} type="button" className={source === s.value ? 'chip on' : 'chip'} aria-pressed={source === s.value} onClick={() => setSource(s.value)}>{t(s.label)}</button>
          ))}
        </div>
        <ErrorBanner message={list.isError ? list.error.message : undefined} />
        {d !== undefined && d.items.length === 0 ? <p className="muted">{t('fx.diff.empty')}</p> : (
          <table className="partner-list cash-list" aria-busy={list.isFetching}>
            <thead>
              <tr>
                <th scope="col">{t('fx.diff.col.date')}</th><th scope="col">{t('fx.diff.col.source')}</th><th scope="col">{t('fx.diff.col.what')}</th>
                <th scope="col" className="num">{t('fx.diff.col.amount')}</th><th scope="col" className="num">{t('fx.diff.col.carrying')}</th>
                <th scope="col" className="num">{t('fx.diff.col.settlement')}</th><th scope="col" className="num">{t('fx.diff.col.difference')}</th>
                <th scope="col">{t('fx.diff.col.account')}</th>
              </tr>
            </thead>
            <tbody>
              {(d?.items ?? []).map((x) => {
                const [main, sub] = differenceWhat(x);
                return (
                  <tr key={x.id}>
                    <td>{isoToRo(x.differenceDate)}</td>
                    <td>{t(sourceLabel(x.source))}{x.isReversal && <span className="pill">{t('fx.diff.reversal')}</span>}{x.isCorrection && <span className="pill info">{t('fx.diff.correction')}</span>}</td>
                    <td><div className="strong">{main}</div><div className="muted small">{sub}</div></td>
                    <td className="num">{formatAmount(x.foreignAmount)} {x.currencyCode}</td>
                    <td className="num">{formatAmount(x.carryingAmountRon)}</td>
                    <td className="num">{formatAmount(x.settlementAmountRon)}</td>
                    <td className={signClass(x.differenceRon)}>{signed(x.differenceRon)}</td>
                    <td>{x.glAccountCode}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
        <p className="muted small">{t('fx.diff.note')}</p>
      </section>
    </div>
  );
}
