import { useState } from 'react';
import { useOutletContext } from 'react-router';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { formatAmount } from './cashApi';
import { monthLabel, previousMonth, rateText, signClass, signed } from './fxModel';

type Run = components['schemas']['FxRunDto'];
const keys = { all: ['finance', 'fx', 'runs'] as const };
const statusPills: Readonly<Record<number, string>> = { 1: 'pill warn', 2: 'pill ok', 3: 'pill' };
const statusLabel = (s: number): TextKey => (['fx.run.status.1', 'fx.run.status.2', 'fx.run.status.3'] as const)[s - 1] ?? 'fx.run.status.1';

/**
 * Valută › Reevaluare valutară (02.02, F4 3.6, 4.4, 4.5; macheta f4-01): rulările pe luni, cu rezultatul net și starea; fișa rulării cu
 * cursurile folosite, câștigul, pierderea, avertizarea de regenerare și liniile (conturi, facturi, corecții); acțiunile Generează /
 * Regenerează, Validează, Stornează, Șterge ciorna.
 */
export function FxRevaluationPage() {
  const user = useOutletContext<CurrentUser>();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const proposed = previousMonth(isoToday());
  const [year, setYear] = useState(proposed.year);
  const [target, setTarget] = useState(proposed);
  const [selected, setSelected] = useState<number | undefined>();
  const [error, setError] = useState<string | undefined>();
  const list = useQuery({
    queryKey: [...keys.all, 'list', year],
    queryFn: async () => unwrap(await api.GET('/api/finance/fx/revaluations', { params: { query: { year } } })),
    placeholderData: keepPreviousData,
  });
  const items = list.data ?? [];
  const current = selected ?? items[0]?.id;
  const canRevalue = user.permissions.includes('Fin.Fx.Revalue');
  const canReverse = user.permissions.includes('Fin.Fx.ReverseRevaluation');

  const generate = async (y: number, m: number) => {
    setError(undefined);
    try {
      const id = unwrap(await api.POST('/api/finance/fx/revaluations', { body: { year: y, month: m } }));
      notify(t('fx.run.generated'));
      await queryClient.invalidateQueries({ queryKey: keys.all });
      setYear(y);
      setSelected(id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.fxRevaluation')}</div>
      <div className="head">
        <div>
          <h1>{t('fx.run.title')}</h1>
          <div className="sub2">{t('fx.run.subtitle')}</div>
        </div>
        {canRevalue && (
          <div className="actions">
            <label className="inline">{t('fx.run.month')}{' '}
              <select className="cell-input" value={`${target.year}-${target.month}`}
                onChange={(e) => { const [y, m] = e.target.value.split('-').map(Number); setTarget({ year: y ?? proposed.year, month: m ?? proposed.month }); }}>
                {Array.from({ length: 12 }, (_, i) => previousMonthOffset(proposed, i)).map((p) => (
                  <option key={`${p.year}-${p.month}`} value={`${p.year}-${p.month}`}>{monthLabel(p.year, p.month)}</option>
                ))}
              </select>
            </label>
            <button type="button" className="btn primary" onClick={() => void generate(target.year, target.month)}>+ {t('fx.run.generate')}</button>
          </div>
        )}
      </div>
      <ErrorBanner message={list.isError ? list.error.message : error} />
      <div className="split cash-document penalty-split">
        <section className="card" aria-label={t('fx.run.title')}>
          <div className="chips-row" role="group" aria-label={t('fx.run.year')}>
            {[proposed.year, proposed.year - 1].map((y) => (
              <button key={y} type="button" className={year === y ? 'chip on' : 'chip'} aria-pressed={year === y} onClick={() => { setYear(y); setSelected(undefined); }}>
                {y}
              </button>
            ))}
          </div>
          {list.data !== undefined && items.length === 0 ? <p className="muted">{t('fx.run.empty')}</p> : (
            <table className="partner-list cash-list" aria-busy={list.isFetching}>
              <thead>
                <tr><th scope="col">{t('fx.run.col.run')}</th><th scope="col" className="num">{t('fx.run.col.net')}</th><th scope="col">{t('bank.col.status')}</th></tr>
              </thead>
              <tbody>
                {items.map((r) => (
                  <tr key={r.id} className={r.id === current ? 'clickable picked' : 'clickable'} tabIndex={0} onClick={() => setSelected(r.id)}
                    onKeyDown={(e) => e.key === 'Enter' && setSelected(r.id)}>
                    <td><div className="strong">{r.number}</div><div className="muted small">{monthLabel(r.periodYear, r.periodMonth)} · {t('fx.run.rateFrom', { date: isoToRo(r.rateDate) })}</div></td>
                    <td className={signClass(r.netRon)}>{signed(r.netRon)}</td>
                    <td><span className={statusPills[r.status] ?? 'pill'}>{t(statusLabel(r.status))}</span>{r.isStale && <span className="pill warn">⟳</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="muted small">{t('fx.run.rule')}</p>
        </section>
        <aside className="card cash-side">
          {current === undefined ? <p className="muted">{t('fx.run.pick')}</p>
            : <Detail key={current} id={current} stale={items.find((r) => r.id === current)?.isStale ?? false} canRevalue={canRevalue} canReverse={canReverse}
                onRegenerate={(y, m) => void generate(y, m)} onDeleted={() => setSelected(undefined)} />}
        </aside>
      </div>
    </div>
  );
}

function previousMonthOffset(start: { readonly year: number; readonly month: number }, offset: number): { readonly year: number; readonly month: number } {
  const index = start.year * 12 + (start.month - 1) - offset;
  return { year: Math.floor(index / 12), month: (index % 12) + 1 };
}

function Detail({ id, stale, canRevalue, canReverse, onRegenerate, onDeleted }: {
  readonly id: number;
  readonly stale: boolean;
  readonly canRevalue: boolean;
  readonly canReverse: boolean;
  readonly onRegenerate: (year: number, month: number) => void;
  readonly onDeleted: () => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const detail = useQuery({
    queryKey: [...keys.all, 'detail', id],
    queryFn: async () => unwrap(await api.GET('/api/finance/fx/revaluations/{id}', { params: { path: { id } } })),
  });
  if (detail.isError) return <ErrorBanner message={detail.error.message} />;
  const r: Run | undefined = detail.data;
  if (r === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  const net = (Number(r.totalGainRon) - Number(r.totalLossRon)).toFixed(2);
  const gain = r.status === 1 ? r.lines.filter((l) => Number(l.differenceRon) > 0).reduce((s, l) => s + Number(l.differenceRon), 0).toFixed(2) : r.totalGainRon;
  const loss = r.status === 1 ? (-r.lines.filter((l) => Number(l.differenceRon) < 0).reduce((s, l) => s + Number(l.differenceRon), 0)).toFixed(2) : r.totalLossRon;
  const shownNet = r.status === 1 ? (Number(gain) - Number(loss)).toFixed(2) : net;

  const run = async (action: () => Promise<unknown>, message: TextKey, after?: () => void) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: keys.all });
      after?.();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  const body = { id, rowVersion: r.rowVersion };
  const element = (l: Run['lines'][number]): string => l.isCorrection ? t('fx.run.correction')
    : l.elementKind === 2 ? t('fx.run.kind.account') : t(l.role === 2 ? 'fx.run.kind.supplier' : 'fx.run.kind.customer');

  return (
    <div>
      <h2>{r.number} · {monthLabel(r.periodYear, r.periodMonth)} <span className={statusPills[r.status] ?? 'pill'}>{t(statusLabel(r.status))}</span></h2>
      <p className="muted small">{t('fx.run.meta', { date: isoToRo(r.revaluationDate), rateDate: isoToRo(r.rateDate) })}</p>
      <p className="muted small">{r.rates.map((x) => `${x.currencyCode} ${rateText(x.rate)}`).join(' · ')}</p>
      <div className="kpi-row">
        <div className="kpi"><div className="kpi-label">{t('fx.run.gain')}</div><div className="kpi-value">{formatAmount(gain)}</div></div>
        <div className="kpi"><div className="kpi-label">{t('fx.run.loss')}</div><div className="kpi-value">{formatAmount(loss)}</div></div>
        <div className="kpi"><div className="kpi-label">{t('fx.run.net')}</div><div className={`kpi-value ${signClass(shownNet)}`}>{signed(shownNet)} <span className="muted small">RON</span></div></div>
        <div className="kpi"><div className="kpi-label">{t('fx.run.lines')}</div><div className="kpi-value">{r.lineCount}</div></div>
      </div>
      {stale && r.status === 1 && <div className="banner warn" role="status">{t('fx.run.stale')}</div>}
      <table className="partner-list cash-list">
        <thead>
          <tr>
            <th scope="col">{t('fx.run.col.element')}</th><th scope="col" className="num">{t('fx.run.col.amount')}</th>
            <th scope="col" className="num">{t('fx.run.col.before')}</th><th scope="col" className="num">{t('fx.run.col.after')}</th>
            <th scope="col" className="num">{t('fx.run.col.difference')}</th>
          </tr>
        </thead>
        <tbody>
          {r.lines.map((l) => (
            <tr key={l.id}>
              <td><div className="strong">{l.partnerName ?? l.documentNo}</div><div className="muted small">{element(l)}{l.partnerName == null ? '' : ` · ${l.documentNo ?? ''}`}</div></td>
              <td className="num">{formatAmount(l.foreignAmount)} {l.currencyCode}</td>
              <td className="num">{l.isCorrection ? '' : formatAmount(l.carryingAmountRon)}</td>
              <td className="num">{l.isCorrection ? '' : formatAmount(l.revaluedAmountRon)}</td>
              <td className={signClass(l.differenceRon)}>{signed(l.differenceRon)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="muted small">{t('fx.run.note')}</p>
      <ErrorBanner message={error} />
      <div className="actions">
        {canRevalue && r.status === 1 && (
          <>
            <button type="button" className="btn ghost" onClick={() => void run(async () => unwrap(await api.POST('/api/finance/fx/revaluations/{id}/delete',
              { params: { path: { id } }, body })), 'fx.run.deleted', onDeleted)}>{t('fx.run.delete')}</button>
            <button type="button" className="btn ghost" onClick={() => onRegenerate(r.periodYear, r.periodMonth)}>⟳ {t('fx.run.regenerate')}</button>
            <button type="button" className="btn primary" onClick={() => void run(async () => unwrap(await api.POST('/api/finance/fx/revaluations/{id}/validate',
              { params: { path: { id } }, body })), 'fx.run.validated')}>✓ {t('fx.run.validate')}</button>
          </>
        )}
        {canReverse && r.status === 2 && (
          <button type="button" className="btn ghost" onClick={() => { if (window.confirm(t('fx.run.confirmReverse'))) void run(async () => unwrap(await api.POST(
            '/api/finance/fx/revaluations/{id}/reverse', { params: { path: { id } }, body })), 'fx.run.reversed'); }}>{t('fx.run.reverse')}</button>
        )}
      </div>
    </div>
  );
}
