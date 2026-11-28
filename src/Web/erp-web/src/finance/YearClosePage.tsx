import { useState } from 'react';
import { useOutletContext } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { formatAmount } from './cashApi';

/**
 * Inchideri › Inchiderea anului (02.02, C3 3.6, 4.3; macheta c3-05): anii firmei cu starea lor; pentru anul ales, cifrele rezervei legale si
 * propunerea, contarea rezervei, inchiderea anului (soldurile reportate si preluarea rezultatului), blocarea si deblocarea cu motiv.
 */
export function YearClosePage() {
  const user = useOutletContext<CurrentUser>();
  const can = (permission: string) => user.permissions.includes(permission);
  const years = useQuery({ queryKey: ['finance', 'periods'], queryFn: async () => unwrap(await api.GET('/api/finance/periods')) });
  const [picked, setPicked] = useState<number | undefined>();
  const list = years.data ?? [];
  const year = list.find((y) => y.year === picked) ?? list[0];

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.closing')} › {t('menu.yearClose')}</div>
      <div className="head"><div><h1>{t('year.title')}</h1><div className="sub2">{t('year.subtitle')}</div></div></div>
      <ErrorBanner message={years.isError ? years.error.message : undefined} />
      {list.length === 0 ? <p className="muted">{t('period.noYears')}</p> : (
        <div className="split">
          <section className="card" aria-label={t('year.title')}>
            <table className="partner-list cash-list">
              <tbody>{list.map((y) => (
                <tr key={y.id} className={y.year === year?.year ? 'picked' : undefined} onClick={() => setPicked(y.year)}>
                  <td><span className="strong">{y.year}</span><div className="muted small">{t('year.monthsClosed', { closed: y.periods.filter((p) => p.status >= 3).length })}</div></td>
                  <td><span className={y.status === 1 ? 'pill ok' : 'pill'}>{t(`period.yearState.${String(y.status)}` as TextKey)}</span></td>
                </tr>))}</tbody>
            </table>
          </section>
          {year !== undefined && <YearDetail key={year.year} year={year.year} can={can} />}
        </div>
      )}
    </div>
  );
}

function YearDetail({ year, can }: { readonly year: number; readonly can: (permission: string) => boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [amount, setAmount] = useState<string | undefined>();
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | undefined>();
  const state = useQuery({ queryKey: ['finance', 'year-close', year], queryFn: async () => unwrap(await api.GET('/api/finance/fiscal-years/{year}/close', { params: { path: { year } } })) });
  const s = state.data;
  const act = async (action: () => Promise<unknown>, message: TextKey) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: ['finance'] });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  if (s === undefined) {
    return <section className="card"><ErrorBanner message={state.isError ? state.error.message : undefined} /></section>;
  }
  const reserve = amount ?? s.proposedReserve;

  return (
    <section className="card" aria-label={String(year)}>
      <h2>{t('year.fiscal', { year })} <span className="pill">{t(`period.yearState.${String(s.status)}` as TextKey)}</span></h2>
      <p className="muted small">{t('year.figures', { profit: formatAmount(s.grossProfit), capital: formatAmount(s.shareCapital), reserve: formatAmount(s.existingReserve) })}</p>
      <ol className="year-steps">
        <li>
          <span className="strong">{t('year.step.reserve')}</span>
          <div className="small">{t('year.reserveHint', { proposed: formatAmount(s.proposedReserve) })}</div>
          {s.status === 1 && can('Fin.FiscalYear.Close') && (
            <div className="row-between">
              <label>{t('year.reserveAmount')}<input className="cell-input num" value={reserve} onChange={(e) => setAmount(e.target.value)} /></label>
              <label>{t('period.reason')}<input className="cell-input" maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
              <button type="button" className="btn" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/fiscal-years/{year}/legal-reserve',
                { params: { path: { year } }, body: { year, amount: reserve, reason: reason.trim() === '' ? null : reason.trim() } })), 'year.reservePosted')}>{t('year.postReserve')}</button>
            </div>
          )}
        </li>
        <li>
          <span className="strong">{t('year.step.months')}</span>
          <div className={s.openMonths === 0 ? 'small check-ok' : 'small check-bad'}>{s.openMonths === 0 ? t('year.allClosed') : t('year.openMonths', { count: s.openMonths })}</div>
        </li>
        <li>
          <span className="strong">{t('year.step.close')}</span>
          <div className="small">{s.nextYearExists ? t('year.closeHint') : t('year.noNextYear')}</div>
          {s.status === 1 && can('Fin.FiscalYear.Close') && (
            <button type="button" className="btn primary" disabled={s.openMonths > 0 || !s.nextYearExists} onClick={() => void act(async () => unwrap(await api.POST(
              '/api/finance/fiscal-years/{year}/close', { params: { path: { year } } })), 'year.closed')}>{t('year.close')}</button>
          )}
        </li>
        <li>
          <span className="strong">{t('year.step.lock')}</span>
          <div className="small">{s.status === 3 ? t('year.lockedHint') : t('year.lockHint')}</div>
          {s.status === 2 && can('Fin.FiscalYear.Lock') && (
            <button type="button" className="btn" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/fiscal-years/{year}/lock',
              { params: { path: { year } } })), 'year.locked')}>{t('year.lock')}</button>
          )}
          {s.status === 3 && can('Fin.FiscalYear.Unlock') && (
            <div className="row-between">
              <label>{t('period.reason')}<input className="cell-input wide-input" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
              <button type="button" className="btn" disabled={reason.trim() === ''} onClick={() => void act(async () => unwrap(await api.POST('/api/finance/fiscal-years/{year}/unlock',
                { params: { path: { year } }, body: { year, reason: reason.trim() } })), 'year.unlocked')}>{t('year.unlock')}</button>
            </div>
          )}
        </li>
      </ol>
      <ErrorBanner message={error} />
      <OpeningBalancesInfo />
    </section>
  );
}

function OpeningBalancesInfo() {
  const data = useQuery({ queryKey: ['finance', 'opening-balances'], queryFn: async () => unwrap(await api.GET('/api/finance/gl-opening-balances')) });
  const o = data.data;
  if (o == null) {
    return null;
  }
  return (
    <p className="muted small">{t('year.openingImport', { start: isoToRo(o.startDate), state: t(`year.import.${String(o.status)}` as TextKey), rows: o.rows.length })}</p>
  );
}
