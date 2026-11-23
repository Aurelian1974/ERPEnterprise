import { useState } from 'react';
import { Link, useOutletContext } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { useBankAccounts } from './bankApi';
import { formatAmount } from './cashApi';
import { eInvoiceLabel, Role, type DueItem } from './settlementApi';

type Range = 14 | 28;
/** Ziua selectata: data ISO sau „overdue” (restantele). */
type Selected = string | 'overdue';

const weekdays: readonly TextKey[] = ['due.wd.sun', 'due.wd.mon', 'due.wd.tue', 'due.wd.wed', 'due.wd.thu', 'due.wd.fri', 'due.wd.sat'];

/** Ziua scurta din scadentar: „Lu 16.11”. */
export function dayLabel(iso: string): string {
  const [y = 0, m = 1, d = 1] = iso.split('-').map(Number);
  const weekday = weekdays[new Date(Date.UTC(y, m - 1, d)).getUTCDay()] ?? 'due.wd.mon';
  return `${t(weekday)} ${String(d).padStart(2, '0')}.${String(m).padStart(2, '0')}`;
}

/** Adauga zile la o data ISO (fara fus orar). */
export function addDays(iso: string, days: number): string {
  const [y = 0, m = 1, d = 1] = iso.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/**
 * Decontări › Scadențar (02.02, F3 10; macheta f3-04): încasările și plățile scadente pe zile, cu restantele și netul; detaliul zilei pe
 * încasări sau plăți, cu observațiile (litigiu, blocare, e-Factura, ANAF); din plățile alese se creează ordine de plată ciornă (F2 „Din
 * facturi scadente”), câte unul pe furnizor.
 */
export function DueSchedulePage() {
  const user = useOutletContext<CurrentUser>();
  const [range, setRange] = useState<Range>(14);
  const from = isoToday();
  const to = addDays(from, range - 1);
  const schedule = useQuery({
    queryKey: ['finance', 'settlement', 'due', from, to],
    queryFn: async () => unwrap(await api.GET('/api/finance/due-schedule', { params: { query: { from, to } } })),
    placeholderData: keepPreviousData,
  });
  const [selected, setSelected] = useState<Selected | undefined>();
  const days = schedule.data?.days ?? [];

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.settlements')} › {t('menu.dueSchedule')}</div>
      <div className="head">
        <div>
          <h1>{t('due.title')}</h1>
          <div className="sub2">{t('due.subtitle')}</div>
        </div>
      </div>
      <div className="split cash-document">
        <section className="card" aria-label={t('due.title')}>
          <div className="chips-row" role="group" aria-label={t('due.range')}>
            {([14, 28] as const).map((r) => (
              <button key={r} type="button" className={range === r ? 'chip on' : 'chip'} aria-pressed={range === r} onClick={() => setRange(r)}>
                {t(r === 14 ? 'due.range2' : 'due.range4')}</button>
            ))}
          </div>
          {schedule.isError && <ErrorBanner message={schedule.error.message} />}
          {schedule.data !== undefined && days.length === 0 ? <p className="muted">{t('due.empty')}</p> : (
            <table className="partner-list cash-list" aria-busy={schedule.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('due.col.day')}</th>
                  <th scope="col" className="num">{t('due.col.receivable')}</th>
                  <th scope="col" className="num">{t('due.col.payable')}</th>
                  <th scope="col" className="num">{t('due.col.net')}</th>
                </tr>
              </thead>
              <tbody>
                {days.map((d) => {
                  const key: Selected = d.day ?? 'overdue';
                  const net = Number(d.receivableRon) - Number(d.payableRon);
                  return (
                    <tr key={key} className={selected === key ? 'clickable picked' : 'clickable'} tabIndex={0} onClick={() => setSelected(key)}
                      onKeyDown={(e) => e.key === 'Enter' && setSelected(key)}>
                      <td className={d.day == null ? 'strong neg' : 'strong'}>{d.day == null ? t('due.overdue') : dayLabel(d.day)}</td>
                      <td className="num">{d.receivableCount} · {formatAmount(d.receivableRon)}</td>
                      <td className="num">{d.payableCount} · {formatAmount(d.payableRon)}</td>
                      <td className={net < 0 ? 'num neg' : 'num pos'}>{net > 0 ? '+' : ''}{formatAmount(net.toFixed(2))}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
          <p className="muted small">{t('due.hint')}</p>
        </section>
        <aside className="card cash-side">
          {selected === undefined ? <p className="muted">{t('due.pick')}</p>
            : <DayDetail key={selected} selected={selected} from={from} canCreate={user.permissions.includes('Fin.PaymentOrder.Create')} />}
        </aside>
      </div>
    </div>
  );
}

function DayDetail({ selected, from, canCreate }: { readonly selected: Selected; readonly from: string; readonly canCreate: boolean }) {
  const [role, setRole] = useState<number>(Role.Supplier);
  const overdue = selected === 'overdue';
  const day = overdue ? from : selected;
  const items = useQuery({
    queryKey: ['finance', 'settlement', 'due-items', role, day, overdue],
    queryFn: async () => unwrap(await api.GET('/api/finance/due-schedule/items', { params: { query: { role, day, overdue } } })),
  });
  const accounts = useBankAccounts();
  const [picked, setPicked] = useState<ReadonlySet<number>>(new Set());
  const [account, setAccount] = useState<number | undefined>();
  const [error, setError] = useState<string | undefined>();
  const [created, setCreated] = useState<number | undefined>();
  const list = items.data ?? [];
  const chosen = list.filter((i) => picked.has(i.openItemId));
  const toggle = (id: number) => {
    const next = new Set(picked);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setPicked(next);
  };

  const create = async () => {
    setError(undefined);
    setCreated(undefined);
    const target = account ?? accounts.data?.[0]?.id;
    if (target === undefined) return;
    try {
      const ids = unwrap(await api.POST('/api/finance/payment-orders/from-invoices', { body: { treasuryAccountId: target, openItemIds: chosen.map((i) => i.openItemId) } }));
      setCreated(ids.length);
      setPicked(new Set());
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div>
      <h2>{overdue ? t('due.overdue') : dayLabel(selected)}</h2>
      <div className="chips-row" role="group" aria-label={t('settle.col.role')}>
        <button type="button" className={role === Role.Customer ? 'chip on' : 'chip'} aria-pressed={role === Role.Customer}
          onClick={() => { setRole(Role.Customer); setPicked(new Set()); }}>{t('due.receipts')}</button>
        <button type="button" className={role === Role.Supplier ? 'chip on' : 'chip'} aria-pressed={role === Role.Supplier}
          onClick={() => { setRole(Role.Supplier); setPicked(new Set()); }}>{t('due.payments')}</button>
      </div>
      {items.isError && <ErrorBanner message={items.error.message} />}
      {items.data !== undefined && list.length === 0 ? <p className="muted">{t('due.noItems')}</p> : (
        <table className="partner-list cash-list">
          <thead>
            <tr>
              {role === Role.Supplier && canCreate && <th scope="col"><span className="sr-only">{t('settle.alloc.pickColumn')}</span></th>}
              <th scope="col">{t('due.col.invoice')}</th>
              <th scope="col" className="num">{t('settle.col.open')}</th>
              <th scope="col">{t('due.col.notes')}</th>
            </tr>
          </thead>
          <tbody>
            {list.map((i) => (
              <tr key={`${i.openItemId}-${i.dueDate}`}>
                {role === Role.Supplier && canCreate && (
                  <td><input type="checkbox" checked={picked.has(i.openItemId)} disabled={!payable(i)} aria-label={t('settle.alloc.pickItem', { document: i.documentNo })}
                    onChange={() => toggle(i.openItemId)} /></td>
                )}
                <td><div className="strong">{i.documentNo}</div><div className="muted small">{i.partnerName}{overdue ? ` · ${isoToRo(i.dueDate)}` : ''}</div></td>
                <td className="num">{formatAmount(i.openAmount)}{i.currencyCode === 'RON' ? '' : ` ${i.currencyCode}`}</td>
                <td className="small due-notes">{notes(i).map((n) => <span key={n} className="pill warn">{t(n)}</span>)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {created !== undefined && (
        <div className="banner info" role="status">{t('due.created', { count: created })} <Link to="/finante/banca/ordine-de-plata">{t('menu.paymentOrders')}</Link></div>
      )}
      {role === Role.Supplier && canCreate && chosen.length > 0 && (
        <div className="order-from-invoices">
          <label>{t('bank.account')}
            <select className="cell-input" value={account ?? accounts.data?.[0]?.id ?? ''} onChange={(e) => setAccount(Number(e.target.value))}>
              {(accounts.data ?? []).map((a) => <option key={a.id} value={a.id}>{`${a.code} · ${a.currencyCode}`}</option>)}
            </select>
          </label>
          {chosen.some((i) => i.anafInactive || (i.eInvoiceStatus === 5 && !i.spvExempt)) && <div className="banner warn">{t('due.warnings')}</div>}
          <ErrorBanner message={error} />
          <button type="button" className="btn primary" onClick={() => void create()}>{t('due.createOrders', { count: chosen.length })}</button>
        </div>
      )}
    </div>
  );
}

/** O factura poate intra intr-un OP: nu e in litigiu si nu e blocata (ea sau furnizorul). */
export const payable = (i: Pick<DueItem, 'isDisputed' | 'isBlockedForPayment' | 'partnerBlocked'>) => !i.isDisputed && !i.isBlockedForPayment && !i.partnerBlocked;

/** Observatiile unei facturi scadente (macheta f3-04). */
export function notes(i: Pick<DueItem, 'anafInactive' | 'eInvoiceStatus' | 'spvExempt' | 'isDisputed' | 'isBlockedForPayment' | 'partnerBlocked'
  | 'partnerBankAccountId'>): TextKey[] {
  const result: TextKey[] = [];
  if (i.anafInactive) result.push('due.note.anaf');
  if (i.eInvoiceStatus === 5 && !i.spvExempt) result.push(eInvoiceLabel(5) ?? 'settle.einv.missing');
  if (i.isDisputed) result.push('settle.disputed');
  if (i.isBlockedForPayment || i.partnerBlocked) result.push('settle.blocked');
  if (i.partnerBankAccountId == null) result.push('due.note.noAccount');
  return result;
}
