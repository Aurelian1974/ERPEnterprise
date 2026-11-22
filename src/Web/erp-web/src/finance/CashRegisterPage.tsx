import { useMemo, useState } from 'react';
import { useOutletContext, useSearchParams } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { cashDayApi, cashKeys, cashKindLabel, formatAmount, monthCalendar, monthRange, shiftMonth, useCashDesk, useCashRegister } from './cashApi';

const weekdays = ['Lu', 'Ma', 'Mi', 'Jo', 'Vi', 'Sâ', 'Du'] as const;
const monthTitle = new Intl.DateTimeFormat('ro-RO', { month: 'long', year: 'numeric', timeZone: 'UTC' });
const dayTitle = new Intl.DateTimeFormat('ro-RO', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'UTC' });

function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/**
 * Trezorerie › Registrul de casă (02.02, F1 3.4 și 4.2; macheta f1-04): calendarul lunii cu starea zilelor casei alese și registrul
 * zilei alese: soldul reportat, documentele validate în ordinea validării cu soldul după fiecare, totalurile. Ziua se închide (în ordine,
 * fără ciorne) sau se redeschide cu motiv; ziua închisă cu sold peste plafon primește avertizarea serverului.
 */
export function CashRegisterPage() {
  const user = useOutletContext<CurrentUser>();
  const [params, setParams] = useSearchParams();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const accountParam = Number(params.get('casa'));
  const desk = useCashDesk(Number.isInteger(accountParam) && accountParam > 0 ? accountParam : undefined, undefined);
  const accountId = desk.data?.day?.treasuryAccountId;
  const today = desk.data?.day?.day ?? new Date().toISOString().slice(0, 10);
  const month = params.get('luna') ?? today.slice(0, 7);
  const selected = params.get('zi') ?? today;
  const range = monthRange(month);
  const register = useCashRegister(accountId, range.from, range.to);
  const [busy, setBusy] = useState(false);
  const data = register.data;
  const cells = useMemo(() => monthCalendar(month, data?.days ?? []), [month, data?.days]);
  const day = data?.days.find((d) => d.day === selected);
  const entries = useMemo(() => (data?.entries ?? []).filter((e) => e.day === selected), [data?.entries, selected]);
  const canClose = user.permissions.includes('Fin.Cash.CloseDay');
  const canReopen = user.permissions.includes('Fin.Cash.ReopenDay');

  const go = (next: { casa?: number; luna?: string; zi?: string }) =>
    setParams({ casa: String(next.casa ?? accountId ?? ''), luna: next.luna ?? month, zi: next.zi ?? selected });

  const run = async (action: () => Promise<string>) => {
    setBusy(true);
    try {
      notify(await action());
      await queryClient.invalidateQueries({ queryKey: cashKeys.all });
    } catch (e) {
      notify(e instanceof ApiError ? e.message : t('app.loadError'), 'error');
    } finally {
      setBusy(false);
    }
  };

  const close = () => accountId !== undefined && void run(async () => {
    const closed = await cashDayApi.close(accountId, selected);
    return closed.balanceWarning == null ? t('register.closed', { day: isoToRo(selected) }) : `${t('register.closed', { day: isoToRo(selected) })} ${closed.balanceWarning}`;
  });

  const reopen = () => {
    const reason = accountId === undefined ? null : window.prompt(t('register.reopenReason'));
    if (accountId === undefined || reason === null || reason.trim() === '') return;
    void run(async () => {
      const days = await cashDayApi.reopen(accountId, selected, reason.trim());
      return t('register.reopened', { count: days.length });
    });
  };

  if (desk.data === undefined) {
    return <div className="page" role="status">{desk.isError ? desk.error.message : t('app.loading')}</div>;
  }
  if (accountId === undefined) {
    return <div className="page"><div className="banner info">{t('cash.noAccounts')}</div></div>;
  }

  const closedCount = data?.days.filter((d) => d.isClosed).length ?? 0;
  const openCount = (data?.days.length ?? 0) - closedCount;
  const totalIn = day?.totalIn;
  const totalOut = day?.totalOut;
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.treasury')} › {t('menu.cashRegister')}</div>
      <div className="head">
        <div>
          <h1>{t('register.title')}</h1>
          <div className="sub2">{t('register.subtitle')}</div>
        </div>
      </div>

      <div className="split cash-register">
        <section className="card" aria-label={t('register.calendar')}>
          <div className="card-head">
            <select className="chip" aria-label={t('cash.account')} value={accountId} onChange={(e) => go({ casa: Number(e.target.value) })}>
              {desk.data.lookups.accounts.map((a) => (
                <option key={a.id} value={a.id}>{`${a.code} · ${a.currencyCode}`}</option>
              ))}
            </select>
            <div className="month-nav">
              <button type="button" className="btn ghost small" aria-label={t('register.previousMonth')} onClick={() => go({ luna: shiftMonth(month, -1) })}>‹</button>
              <strong>{capitalize(monthTitle.format(new Date(`${month}-01T00:00:00Z`)))}</strong>
              <button type="button" className="btn ghost small" aria-label={t('register.nextMonth')} onClick={() => go({ luna: shiftMonth(month, 1) })}>›</button>
            </div>
          </div>
          <div className="register-calendar" role="grid" aria-busy={register.isFetching}>
            {weekdays.map((w) => <div key={w} className="weekday" role="columnheader">{w}</div>)}
            {cells.map((cell, index) => cell === null ? <div key={`e${index}`} /> : (
              <button key={cell.iso} type="button" role="gridcell" aria-selected={cell.iso === selected}
                className={['register-day', cell.day === undefined ? (cell.iso > today ? 'future' : 'idle') : cell.day.isClosed ? 'closed' : 'open',
                  cell.iso === selected ? 'selected' : ''].join(' ').trim()}
                onClick={() => go({ zi: cell.iso })}>
                <strong>{cell.dayOfMonth}</strong>
                <span className="small">{cell.day === undefined ? (cell.iso > today ? '' : t('register.idle'))
                  : t(cell.day.isClosed ? 'register.dayClosed' : 'register.dayOpen')}</span>
              </button>
            ))}
          </div>
          <div className="banner dashed">{t('register.rule')}</div>
          <div className="muted small list-foot">{t('register.monthSummary', { closed: closedCount, open: openCount })}</div>
        </section>

        <section className="card" aria-label={t('register.day')}>
          <div className="panel-head">
            <div>
              <h2>
                {capitalize(dayTitle.format(new Date(`${selected}T00:00:00Z`)))}{' '}
                {day !== undefined && <span className={day.isClosed ? 'pill ok' : 'pill info'}>{t(day.isClosed ? 'cash.dayClosed' : 'cash.dayOpen')}</span>}
              </h2>
              {day !== undefined && (
                <div className="muted">
                  {[day.firstPageNo == null ? undefined : t('register.pages', { first: day.firstPageNo, last: day.lastPageNo ?? day.firstPageNo }),
                    day.closedByName == null ? undefined : t('register.closedBy', { name: day.closedByName }),
                    day.reopenReason == null ? undefined : t('register.reopenedFor', { reason: day.reopenReason })].filter(Boolean).join(' · ')}
                </div>
              )}
              {day !== undefined && (
                <div className="chips-row">
                  <span className="chip static">{t('register.opening', { amount: formatAmount(day.openingBalance) })}</span>
                  <span className="chip static">{t('register.in', { amount: formatAmount(day.totalIn) })}</span>
                  <span className="chip static">{t('register.out', { amount: formatAmount(day.totalOut) })}</span>
                  <span className="chip static"><strong>{t('register.closing', { amount: formatAmount(day.closingBalance) })}</strong></span>
                  <span className="chip static">{t('register.documents', { count: entries.length })}</span>
                  {day.draftCount > 0 && <span className="pill warn">{t('register.drafts', { count: day.draftCount })}</span>}
                </div>
              )}
            </div>
          </div>
          {day === undefined ? (
            <p className="muted">{t('register.noActivity')}</p>
          ) : (
            <table className="partner-list cash-list register-table">
              <thead>
                <tr>
                  <th scope="col">{t('cash.col.document')}</th>
                  <th scope="col">{t('register.col.kind')}</th>
                  <th scope="col">{t('register.col.explanation')}</th>
                  <th scope="col" className="num">{t('cash.col.in')}</th>
                  <th scope="col" className="num">{t('cash.col.out')}</th>
                  <th scope="col" className="num">{t('register.col.balance')}</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td colSpan={5}>{t('register.carried')}</td>
                  <td className="num"><strong>{formatAmount(day.openingBalance)}</strong></td>
                </tr>
                {entries.map((e) => (
                  <tr key={e.id}>
                    <td><strong>{e.number ?? t('cash.noNumber')}</strong></td>
                    <td className="small">{t(cashKindLabel(e.kind))}</td>
                    <td>{[e.personName, e.description].filter(Boolean).join(' · ')}</td>
                    <td className="num amount-in">{formatAmount(e.cashIn)}</td>
                    <td className="num amount-out">{formatAmount(e.cashOut)}</td>
                    <td className="num"><strong>{formatAmount(e.balance)}</strong></td>
                  </tr>
                ))}
                <tr className="total-row">
                  <td colSpan={3}><strong>{t('register.total')}</strong></td>
                  <td className="num amount-in"><strong>{formatAmount(totalIn)}</strong></td>
                  <td className="num amount-out"><strong>{formatAmount(totalOut)}</strong></td>
                  <td className="num"><strong>{formatAmount(day.closingBalance)}</strong></td>
                </tr>
              </tbody>
            </table>
          )}
          <div className="form-footer">
            <span className="muted small">{t('register.reopenHint')}</span>
            <div className="actions">
              {canReopen && day?.isClosed === true && (
                <button type="button" className="btn danger" disabled={busy} onClick={reopen}>{t('register.reopen')}</button>
              )}
              {canClose && day?.isClosed !== true && selected <= today && (
                <button type="button" className="btn primary" disabled={busy} onClick={close}>{t('register.close')}</button>
              )}
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
