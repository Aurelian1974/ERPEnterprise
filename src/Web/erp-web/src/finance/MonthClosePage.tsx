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
import { formatAmount } from './cashApi';
import { blocks, defaultPeriod, runSummary } from './closingModel';
import { monthName, periodStateKey } from './AccountingPeriodsPage';

type Step = components['schemas']['CloseStepDto'];
type Run = components['schemas']['CloseRunDto'];

const periodKeys = { all: ['finance', 'periods'] as const };

/** Alegerea lunii (an si luna), comuna ecranelor de inchidere. */
export function usePeriodPicker() {
  const years = useQuery({ queryKey: periodKeys.all, queryFn: async () => unwrap(await api.GET('/api/finance/periods')) });
  const [picked, setPicked] = useState<number | undefined>();
  const all = (years.data ?? []).flatMap((y) => y.periods.map((p) => ({ ...p, year: y.year })));
  const period = all.find((p) => p.id === picked) ?? (() => { const d = defaultPeriod(years.data ?? []); return all.find((p) => p.id === d?.id); })();
  const picker = (
    <select className="cell-input" aria-label={t('close.month')} value={period?.id ?? ''} onChange={(e) => setPicked(Number(e.target.value))}>
      {all.map((p) => <option key={p.id} value={p.id}>{monthName(p.startDate)} {p.year} · {t(periodStateKey(p.status))}</option>)}
    </select>
  );
  return { years, period, picker };
}

const icon = (s: Step) => (s.outcome === 1 ? '✓' : s.outcome === 3 ? '…' : s.overrideReason != null ? '↷' : s.severity === 2 ? '!' : '✗');
const iconClass = (s: Step) => (s.outcome === 1 ? 'check-ok' : blocks(s) ? 'check-bad' : '');

/**
 * Inchideri › Inchiderea lunii (02.02, C3 3.3, 4.1; macheta c3-02): lista de verificari a lunii, rulata oricand; pasul ales cu problemele lui,
 * trecerea peste un pas blocant cu motiv (unde se poate), inchiderea operativa si inchiderea lunii (cu notele de inchidere).
 */
export function MonthClosePage() {
  const user = useOutletContext<CurrentUser>();
  const can = (permission: string) => user.permissions.includes(permission);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const { years, period, picker } = usePeriodPicker();
  const [selected, setSelected] = useState<string | undefined>();
  const [error, setError] = useState<string | undefined>();
  const run = useQuery({
    queryKey: ['finance', 'close-run', period?.id ?? 0],
    enabled: period !== undefined,
    queryFn: async () => unwrap(await api.GET('/api/finance/periods/{id}/close-run', { params: { path: { id: period?.id ?? 0 } } })),
  });
  const r: Run | null | undefined = run.data;
  const steps = r?.steps ?? [];
  const summary = runSummary(steps);
  const current = steps.find((s) => s.stepCode === selected) ?? steps.find(blocks) ?? steps[0];

  const act = async (action: () => Promise<unknown>, message: TextKey) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: ['finance'] });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
      await queryClient.invalidateQueries({ queryKey: ['finance', 'close-run'] });
    }
  };
  const p = period;

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.closing')} › {t('menu.monthClose')}</div>
      <div className="head">
        <div><h1>{t('close.title')}</h1><div className="sub2">{p === undefined ? t('period.noYears') : t(`period.means.${String(p.status)}` as TextKey)}</div></div>
        <div className="actions">
          {picker}
          {p !== undefined && p.status === 1 && can('Fin.Period.SoftClose') && (
            <button type="button" className="btn" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/periods/{id}/soft-close',
              { params: { path: { id: p.id } }, body: { id: p.id, rowVersion: p.rowVersion } })), 'period.softClosed')}>{t('period.softClose')}</button>
          )}
          {p !== undefined && p.status < 3 && can('Fin.Period.SoftClose') && (
            <button type="button" className="btn" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/periods/{id}/close-run',
              { params: { path: { id: p.id } } })), 'close.ran')}>{t('close.run')}</button>
          )}
          {p !== undefined && p.status < 3 && can('Fin.Period.Close') && (
            <button type="button" className="btn primary" disabled={r != null && !r.canClose} onClick={() => void act(async () => unwrap(await api.POST(
              '/api/finance/periods/{id}/close', { params: { path: { id: p.id } }, body: { id: p.id, rowVersion: p.rowVersion } })), 'period.closed')}>{t('period.close')}</button>
          )}
        </div>
      </div>
      <ErrorBanner message={years.isError ? years.error.message : run.isError ? run.error.message : error} />
      {r == null ? <p className="muted">{t('close.notRun')}</p> : (
        <>
          <div className="kpi-row">
            <div className="kpi"><div className="kpi-label">{t('close.blocking')}</div><div className={summary.blocking > 0 ? 'kpi-value bad' : 'kpi-value ok'}>{summary.blocking}</div></div>
            <div className="kpi"><div className="kpi-label">{t('close.warnings')}</div><div className="kpi-value">{summary.warnings}</div></div>
            <div className="kpi"><div className="kpi-label">{t('close.ok')}</div><div className="kpi-value ok">{summary.ok}</div></div>
            <div className="kpi"><div className="kpi-label">{t('close.overridden')}</div><div className="kpi-value">{summary.overridden}</div></div>
          </div>
          <div className="split">
            <section className="card" aria-label={t('close.checklist')}>
              <h2>{t('close.checklist')}</h2>
              <p className="muted small">{t('close.ranAt', { when: r.startedAt.slice(0, 16).replace('T', ' '), who: r.startedByName ?? '' })}</p>
              <table className="partner-list cash-list">
                <tbody>{steps.map((s) => (
                  <tr key={s.stepCode} className={s.stepCode === current?.stepCode ? 'picked' : undefined} onClick={() => setSelected(s.stepCode)}>
                    <td className={iconClass(s)}>{icon(s)}</td>
                    <td><span className="strong">{s.name}</span><div className="muted small">{s.summary}</div></td>
                    <td className="small muted">{s.severity === 2 ? t('close.warning') : s.allowOverride ? t('close.blockingStep') : t('close.noOverride')}</td>
                  </tr>))}</tbody>
              </table>
            </section>
            {current !== undefined && (
              <StepDetail key={`${String(r.id)}-${current.stepCode}`} runId={r.id} step={current} canOverride={can('Fin.Period.CloseOverride')}
                onOverride={(reason) => act(async () => unwrap(await api.POST('/api/finance/period-close-runs/{id}/override',
                  { params: { path: { id: r.id } }, body: { runId: r.id, stepCode: current.stepCode, reason } })), 'close.overriddenDone')} />
            )}
          </div>
        </>
      )}
    </div>
  );
}

function StepDetail({ step, canOverride, onOverride }: { readonly runId: number; readonly step: Step; readonly canOverride: boolean;
  readonly onOverride: (reason: string) => Promise<void> }) {
  const [reason, setReason] = useState('');
  return (
    <section className="card" aria-label={step.name}>
      <h2>{step.name} <span className={blocks(step) ? 'pill bad' : step.outcome === 1 ? 'pill ok' : 'pill warn'}>{step.summary}</span></h2>
      {step.overrideReason != null && <p className="small">{t('close.overriddenBy', { reason: step.overrideReason, who: step.overriddenByName ?? '' })}</p>}
      {step.issues.length > 0 && (
        <table className="partner-list cash-list">
          <tbody>{step.issues.map((i, n) => (
            <tr key={n}><td>{i.message}</td><td className="num">{i.amount == null ? '' : formatAmount(String(i.amount))}</td></tr>))}</tbody>
        </table>
      )}
      {!step.allowOverride && step.outcome === 2 && step.severity === 1 && <p className="muted small">{t('close.cannotOverride')}</p>}
      {blocks(step) && step.allowOverride && canOverride && (
        <div className="settings-form">
          <label>{t('close.overrideReason')}<input className="cell-input wide-input" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
          <div className="actions"><button type="button" className="btn" disabled={reason.trim() === ''} onClick={() => void onOverride(reason.trim())}>{t('close.override')}</button></div>
        </div>
      )}
    </section>
  );
}
