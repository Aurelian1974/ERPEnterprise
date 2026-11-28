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

type Period = components['schemas']['AccountingPeriodDto'];

const periodKeys = { all: ['finance', 'periods'] as const };
const statePills: Record<number, string> = { 1: 'pill ok', 2: 'pill warn', 3: 'pill', 4: 'pill bad' };
export const periodStateKey = (state: number) => `period.state.${state}` as TextKey;
const months: readonly TextKey[] = ['period.m.1', 'period.m.2', 'period.m.3', 'period.m.4', 'period.m.5', 'period.m.6', 'period.m.7', 'period.m.8',
  'period.m.9', 'period.m.10', 'period.m.11', 'period.m.12'];

/** Numele lunii unei perioade, după data de început (exercițiul poate să nu înceapă în ianuarie). */
export function monthName(startIso: string): string {
  return t(months[Number(startIso.slice(5, 7)) - 1] ?? 'period.m.1');
}

/**
 * Închideri › Perioade contabile (02.02, C3 3.1, 3.2, 4.1, 4.2; macheta c3-01): anii financiari ai firmei cu starea fiecărei luni, istoricul
 * unei luni, închiderea operativă, închiderea, redeschiderea cu motiv și crearea unui an.
 */
export function AccountingPeriodsPage() {
  const user = useOutletContext<CurrentUser>();
  const can = (permission: string) => user.permissions.includes(permission);
  const years = useQuery({ queryKey: periodKeys.all, queryFn: async () => unwrap(await api.GET('/api/finance/periods')) });
  const [yearPick, setYearPick] = useState<number | undefined>();
  const [selected, setSelected] = useState<number | undefined>();
  const [creating, setCreating] = useState(false);
  const year = years.data?.find((y) => y.year === yearPick) ?? years.data?.[0];
  const current = year?.periods.find((p) => p.id === selected);

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.periods')}</div>
      <div className="head">
        <div><h1>{t('period.title')}</h1><div className="sub2">{t('period.subtitle')}</div></div>
        <div className="actions">
          {(years.data ?? []).length > 0 && (
            <select className="cell-input" aria-label={t('period.year')} value={year?.year ?? ''} onChange={(e) => { setYearPick(Number(e.target.value)); setSelected(undefined); }}>
              {(years.data ?? []).map((y) => <option key={y.id} value={y.year}>{y.year}</option>)}
            </select>
          )}
          {can('Fin.FiscalYear.Close') && <button type="button" className="btn primary" onClick={() => setCreating(true)}>{t('period.newYear')}</button>}
        </div>
      </div>
      <ErrorBanner message={years.isError ? years.error.message : undefined} />
      {creating && <NewYear onDone={() => setCreating(false)} />}
      {years.data !== undefined && years.data.length === 0 && !creating ? <p className="muted">{t('period.noYears')}</p> : year !== undefined && (
        <section className="card" aria-label={t('period.title')}>
          <p className="muted small">{t('period.rule')} · {t(`period.vat.${year.vatPeriodicity}` as TextKey)} · {t(`period.yearState.${year.status}` as TextKey)}</p>
          <div className="period-grid" role="list">
            {year.periods.map((p) => (
              <button key={p.id} type="button" role="listitem" className={p.id === selected ? 'period-cell picked' : 'period-cell'} onClick={() => setSelected(p.id)}>
                <span className="strong">{monthName(p.startDate)}</span>
                <span className={statePills[p.status] ?? 'pill'}>{t(periodStateKey(p.status))}</span>
              </button>
            ))}
          </div>
          {current !== undefined && <PeriodDetail key={`${current.id}-${current.status}`} period={current} year={year.year} can={can} />}
        </section>
      )}
    </div>
  );
}

function NewYear({ onDone }: { readonly onDone: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [year, setYear] = useState(new Date().getFullYear());
  const [vat, setVat] = useState(1);
  const [error, setError] = useState<string | undefined>();
  const save = async () => {
    setError(undefined);
    try {
      unwrap(await api.POST('/api/finance/fiscal-years', { body: { year, startDate: null, endDate: null, vatPeriodicity: vat } }));
      notify(t('period.yearCreated'));
      await queryClient.invalidateQueries({ queryKey: periodKeys.all });
      onDone();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  return (
    <section className="card settings-form">
      <h2>{t('period.newYear')}</h2>
      <div className="row-between">
        <label>{t('period.year')}<input className="cell-input" type="number" value={year} onChange={(e) => setYear(Number(e.target.value))} /></label>
        <label>{t('period.vatPeriodicity')}
          <select className="cell-input" value={vat} onChange={(e) => setVat(Number(e.target.value))}>
            {[1, 3, 6, 12].map((v) => <option key={v} value={v}>{t(`period.vat.${v}` as TextKey)}</option>)}
          </select>
        </label>
      </div>
      <p className="muted small">{t('period.newYearHint')}</p>
      <ErrorBanner message={error} />
      <div className="actions">
        <button type="button" className="btn" onClick={onDone}>{t('common.cancel')}</button>
        <button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button>
      </div>
    </section>
  );
}

function PeriodDetail({ period: p, year, can }: { readonly period: Period; readonly year: number; readonly can: (permission: string) => boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [reason, setReason] = useState('');
  const [toSoft, setToSoft] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const history = useQuery({
    queryKey: [...periodKeys.all, p.id, 'history'],
    queryFn: async () => unwrap(await api.GET('/api/finance/periods/{id}/history', { params: { path: { id: p.id } } })),
  });

  const run = async (action: () => Promise<unknown>, message: TextKey) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: periodKeys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  const body = { id: p.id, rowVersion: p.rowVersion };

  return (
    <div className="period-detail">
      <h2>{monthName(p.startDate)} {year} <span className={statePills[p.status] ?? 'pill'}>{t(periodStateKey(p.status))}</span></h2>
      <p className="muted small">{isoToRo(p.startDate)} – {isoToRo(p.endDate)}
        {p.closedAt != null ? ` · ${t('period.closedBy', { name: p.closedByName ?? '', date: isoToRo(p.closedAt.slice(0, 10)) })}` : ''}</p>
      <p className="small">{t(`period.means.${p.status}` as TextKey)}</p>
      <div className="actions">
        {p.status === 1 && can('Fin.Period.SoftClose') && (
          <button type="button" className="btn" onClick={() => void run(async () => unwrap(await api.POST('/api/finance/periods/{id}/soft-close',
            { params: { path: { id: p.id } }, body })), 'period.softClosed')}>{t('period.softClose')}</button>
        )}
        {p.status <= 2 && can('Fin.Period.Close') && (
          <button type="button" className="btn primary" onClick={() => void run(async () => unwrap(await api.POST('/api/finance/periods/{id}/close',
            { params: { path: { id: p.id } }, body })), 'period.closed')}>{t('period.close')}</button>
        )}
      </div>
      {(p.status === 2 || p.status === 3) && can('Fin.Period.Reopen') && (
        <div className="settings-form">
          <h3>{t('period.reopenTitle')}</h3>
          {p.status === 3 && (
            <label className="check"><input type="checkbox" checked={toSoft} onChange={(e) => setToSoft(e.target.checked)} />{t('period.reopenToSoft')}</label>
          )}
          <label>{t('period.reason')}<input className="cell-input wide-input" value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} /></label>
          <div className="actions">
            <button type="button" className="btn" disabled={reason.trim() === ''} onClick={() => void run(async () => unwrap(await api.POST('/api/finance/periods/{id}/reopen',
              { params: { path: { id: p.id } }, body: { ...body, reason: reason.trim(), toSoftClosed: p.status === 3 && toSoft } })), 'period.reopened')}>
              {t('period.reopen')}</button>
          </div>
        </div>
      )}
      <ErrorBanner message={error} />
      <h3>{t('period.history')}</h3>
      {(history.data ?? []).length === 0 ? <p className="muted">{t('period.noHistory')}</p> : (
        <table className="partner-list cash-list">
          <thead><tr><th scope="col">{t('period.when')}</th><th scope="col">{t('period.who')}</th><th scope="col">{t('period.change')}</th><th scope="col">{t('period.reason')}</th></tr></thead>
          <tbody>{(history.data ?? []).map((h) => (
            <tr key={h.changedAt}><td>{isoToRo(h.changedAt.slice(0, 10))} {h.changedAt.slice(11, 16)}</td><td>{h.changedByName ?? ''}</td>
              <td>{t(periodStateKey(h.fromStatus))} → {t(periodStateKey(h.toStatus))}</td><td>{h.reason ?? ''}</td></tr>))}</tbody>
        </table>
      )}
    </div>
  );
}
