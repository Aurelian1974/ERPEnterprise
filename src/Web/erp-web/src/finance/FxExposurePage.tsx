import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { formatAmount } from './cashApi';
import { rateText, signClass, signed } from './fxModel';

/**
 * Valută › Poziția valutară (02.02, F4 10; macheta f4-03): pe monedă conturile și casele, creanțele, efectele, datoriile, poziția netă,
 * cursul zilei, valoarea în lei și impactul unei aprecieri de 1%; detalierea pe active și pasive a unei monede.
 */
export function FxExposurePage() {
  const [asOf, setAsOf] = useState(isoToday());
  const [advances, setAdvances] = useState(false);
  const [open, setOpen] = useState<string | undefined>();
  const report = useQuery({
    queryKey: ['finance', 'fx', 'exposure', asOf, advances],
    queryFn: async () => unwrap(await api.GET('/api/finance/fx/exposure', { params: { query: { asOf, includeAdvances: advances } } })),
    placeholderData: keepPreviousData,
  });
  const r = report.data;
  const expanded = open ?? r?.currencies[0]?.currencyCode;
  const detail = r?.currencies.find((c) => c.currencyCode === expanded);

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.fxExposure')}</div>
      <div className="head">
        <div><h1>{t('fx.exp.title')}</h1><div className="sub2">{t('fx.exp.subtitle')}</div></div>
        <div className="actions">
          <label className="inline">{t('fx.exp.asOf')} <input className="cell-input" type="date" value={asOf} onChange={(e) => setAsOf(e.target.value)} /></label>
          <label className="check"><input type="checkbox" checked={advances} onChange={(e) => setAdvances(e.target.checked)} />{t('fx.exp.advances')}</label>
        </div>
      </div>
      <ErrorBanner message={report.isError ? report.error.message : undefined} />
      <section className="card" aria-label={t('fx.exp.title')}>
        {r !== undefined && r.currencies.length === 0 ? <p className="muted">{t('fx.exp.empty')}</p> : (
          <table className="partner-list cash-list" aria-busy={report.isFetching}>
            <thead>
              <tr>
                <th scope="col">{t('fx.exp.col.currency')}</th><th scope="col" className="num">{t('fx.exp.col.treasury')}</th>
                <th scope="col" className="num">{t('fx.exp.col.receivables')}</th><th scope="col" className="num">{t('fx.exp.col.papers')}</th>
                <th scope="col" className="num">{t('fx.exp.col.payables')}</th><th scope="col" className="num">{t('fx.exp.col.net')}</th>
                <th scope="col" className="num">{t('fx.exp.col.rate')}</th><th scope="col" className="num">{t('fx.exp.col.ron')}</th>
                <th scope="col" className="num">{t('fx.exp.col.impact')}</th>
              </tr>
            </thead>
            <tbody>
              {(r?.currencies ?? []).map((c) => (
                <tr key={c.currencyCode} className={c.currencyCode === expanded ? 'clickable picked' : 'clickable'} tabIndex={0} onClick={() => setOpen(c.currencyCode)}
                  onKeyDown={(e) => e.key === 'Enter' && setOpen(c.currencyCode)}>
                  <td className="strong">{c.currencyCode} {c.currencyCode === expanded ? '▾' : '›'}</td>
                  <td className="num">{formatAmount(c.treasury)}</td><td className="num">{formatAmount(c.receivables)}</td>
                  <td className="num">{formatAmount(c.papers)}</td><td className="num">{formatAmount(c.payables)}</td>
                  <td className="num strong">{formatAmount(c.net)}</td><td className="num">{c.rate == null ? t('fx.exp.noRate') : rateText(c.rate)}</td>
                  <td className="num">{formatAmount(c.netRon)}</td><td className={signClass(c.impactOnePercentRon)}>{signed(c.impactOnePercentRon)}</td>
                </tr>
              ))}
              {r?.totalRon != null && (
                <tr><td className="strong" colSpan={7}>{t('fx.exp.total')}</td><td className="num strong">{formatAmount(r.totalRon)}</td>
                  <td className={signClass(r.totalImpactRon)}>{signed(r.totalImpactRon)}</td></tr>
              )}
            </tbody>
          </table>
        )}
      </section>
      {detail !== undefined && (
        <section className="card" aria-label={detail.currencyCode}>
          {([[t('fx.exp.assets'), detail.items.filter((i) => Number(i.amount) > 0)], [t('fx.exp.liabilities'), detail.items.filter((i) => Number(i.amount) < 0)]] as const)
            .map(([title, rows]) => rows.length > 0 && (
              <div key={title}>
                <h3 className="section-title">{detail.currencyCode} · {title}</h3>
                <table className="partner-list cash-list">
                  <tbody>
                    {rows.map((i, n) => (
                      <tr key={n}>
                        <td><div className="strong">{i.name}</div><div className="muted small">{i.description}{i.dueDate == null ? '' : ` · ${isoToRo(i.dueDate)}`}</div></td>
                        <td className="num">{formatAmount(i.amount.replace('-', ''))}</td><td className="num">{formatAmount(i.valueRon?.replace('-', ''))}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ))}
          <p className="muted small">{t('fx.exp.note')}</p>
        </section>
      )}
    </div>
  );
}
