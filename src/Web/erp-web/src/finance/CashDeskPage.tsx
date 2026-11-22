import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useOutletContext, useSearchParams } from 'react-router';
import type { CurrentUser } from '../api/session';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { amountColumns, CashKind, cashKindLabel, filterDay, formatAmount, statusLabel, useCashDesk, type DayFilter } from './cashApi';

const dayFilters: readonly { readonly value: DayFilter; readonly label: Parameters<typeof t>[0] }[] = [
  { value: 'all', label: 'cash.filter.all' },
  { value: 'in', label: 'cash.filter.in' },
  { value: 'out', label: 'cash.filter.out' },
  { value: 'drafts', label: 'cash.filter.drafts' },
];

const time = new Intl.DateTimeFormat('ro-RO', { hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Bucharest' });

/** Adresa documentului de casa: unul existent sau unul nou, de felul dat, pe casa si ziua din ecranul Casa. */
export function documentPath(params: { readonly id?: number; readonly kind?: number; readonly account?: number; readonly day?: string }): string {
  const query = new URLSearchParams();
  if (params.id !== undefined) {
    query.set('id', String(params.id));
  } else {
    query.set('nou', '1');
    if (params.kind !== undefined) query.set('fel', String(params.kind));
    if (params.account !== undefined) query.set('casa', String(params.account));
    if (params.day !== undefined) query.set('zi', params.day);
  }
  return `/finante/trezorerie/casa/document?${query.toString()}`;
}

/**
 * Trezorerie › Casa (02.02, F1 4.1 si 11; macheta f1-02): ecranul de lucru al casierului. Casa si ziua se aleg din antet; soldul de
 * inceput, incasarile, platile si soldul curent vin de pe server; documentele zilei, in ordinea validarii. F2 = incasare, F3 = plata.
 */
export function CashDeskPage() {
  const user = useOutletContext<CurrentUser>();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const accountParam = Number(params.get('casa'));
  const accountId = Number.isInteger(accountParam) && accountParam > 0 ? accountParam : undefined;
  const dayParam = params.get('zi') ?? undefined;
  const desk = useCashDesk(accountId, dayParam);
  const [filter, setFilter] = useState<DayFilter>('all');
  const data = desk.data;
  const day = data?.day;
  const canCreate = user.permissions.includes('Fin.Cash.Create');
  const documents = useMemo(() => filterDay(data?.documents ?? [], filter), [data?.documents, filter]);
  const open = (kind: number) => {
    if (day != null && !day.isClosed) {
      void navigate(documentPath({ kind, account: day.treasuryAccountId, day: day.day }));
    }
  };

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (!canCreate || day == null || day.isClosed) return;
      if (event.key === 'F2') {
        event.preventDefault();
        void navigate(documentPath({ kind: CashKind.Receipt, account: day.treasuryAccountId, day: day.day }));
      } else if (event.key === 'F3') {
        event.preventDefault();
        void navigate(documentPath({ kind: CashKind.CashOut, account: day.treasuryAccountId, day: day.day }));
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [canCreate, day, navigate]);

  if (data === undefined) {
    return <div className="page" role="status">{desk.isError ? desk.error.message : t('app.loading')}</div>;
  }

  const select = (next: { casa?: number; zi?: string }) =>
    setParams({ casa: String(next.casa ?? day?.treasuryAccountId ?? ''), zi: next.zi ?? day?.day ?? '' });
  const validated = data.documents.filter((d) => d.status !== 1).length;

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.treasury')} › {t('menu.cashDesk')}</div>
      <div className="head">
        <div>
          <h1>{t('cash.title')}</h1>
          <div className="sub2">{t('cash.subtitle')}</div>
        </div>
      </div>

      {data.lookups.accounts.length === 0 && <div className="banner info">{t('cash.noAccounts')}</div>}
      {day != null && (
        <>
          <section className="card cash-summary" aria-label={t('cash.summary')}>
            <div className="cash-pick">
              <label className="label" htmlFor="cash-account">{t('cash.account')}</label>
              <select id="cash-account" className="chip" value={day.treasuryAccountId} onChange={(e) => select({ casa: Number(e.target.value) })}>
                {data.lookups.accounts.map((a) => (
                  <option key={a.id} value={a.id}>{`${a.code} · ${a.currencyCode}`}</option>
                ))}
              </select>
              <span className="muted small">{day.name}</span>
            </div>
            <div className="cash-pick">
              <label className="label" htmlFor="cash-day">{t('cash.day')}</label>
              <input id="cash-day" type="date" className="cell-input" value={day.day} onChange={(e) => e.target.value !== '' && select({ zi: e.target.value })} />
              <span className={day.isClosed ? 'pill off' : 'pill info'}>{t(day.isClosed ? 'cash.dayClosed' : 'cash.dayOpen')}</span>
            </div>
            <Stat label={t('cash.opening')} value={formatAmount(day.openingBalance)} />
            <Stat label={t('cash.totalIn')} value={`+${formatAmount(day.totalIn)}`} tone="in" />
            <Stat label={t('cash.totalOut')} value={`−${formatAmount(day.totalOut)}`} tone="out" />
            <Stat label={t('cash.current')} value={formatAmount(day.closingBalance)} tone="current" suffix={day.currencyCode} />
          </section>

          {canCreate && (
            <div className="cash-actions">
              <button type="button" className="cash-action in" disabled={day.isClosed} onClick={() => open(CashKind.Receipt)}>
                <span className="ic" aria-hidden="true">+</span>
                <span><strong>{t('cash.action.in')}</strong><span className="muted small">{t('cash.action.inHint')}</span></span>
                <kbd>F2</kbd>
              </button>
              <button type="button" className="cash-action out" disabled={day.isClosed} onClick={() => open(CashKind.CashOut)}>
                <span className="ic" aria-hidden="true">−</span>
                <span><strong>{t('cash.action.out')}</strong><span className="muted small">{t('cash.action.outHint')}</span></span>
                <kbd>F3</kbd>
              </button>
            </div>
          )}

          <section className="card" aria-label={t('cash.documents')}>
            <div className="card-head">
              <h2>{t('cash.documents')} <span className="muted">{t('cash.documentsCount', { count: data.documents.length, validated,
                drafts: data.documents.length - validated })}</span></h2>
              <div className="chips-row" role="group" aria-label={t('cash.filter.label')}>
                {dayFilters.map((f) => (
                  <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                    onClick={() => setFilter(f.value)}>
                    {t(f.label)}
                  </button>
                ))}
              </div>
            </div>
            {documents.length === 0 ? (
              <p className="muted">{t('cash.empty')}</p>
            ) : (
              <table className="partner-list cash-list" aria-busy={desk.isFetching}>
                <thead>
                  <tr>
                    <th scope="col">{t('cash.col.time')}</th>
                    <th scope="col">{t('cash.col.document')}</th>
                    <th scope="col">{t('cash.col.person')}</th>
                    <th scope="col" className="num">{t('cash.col.in')}</th>
                    <th scope="col" className="num">{t('cash.col.out')}</th>
                    <th scope="col">{t('cash.col.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {documents.map((d) => {
                    const amounts = amountColumns(d);
                    return (
                      <tr key={d.id} onClick={() => void navigate(documentPath({ id: d.id }))}>
                        <td className="small">{d.validatedAt == null ? '' : time.format(new Date(d.validatedAt))}</td>
                        <td>
                          <button type="button" className="row-link" onClick={(e) => {
                            e.stopPropagation();
                            void navigate(documentPath({ id: d.id }));
                          }}>
                            {d.number ?? t('cash.noNumber')}
                          </button>
                          <div className="muted small">{t(cashKindLabel(d.kind))}</div>
                        </td>
                        <td>
                          {d.personName ?? d.operationName ?? t('common.none')}
                          <div className="muted small">{d.description ?? (d.personName == null ? '' : d.operationName ?? '')}</div>
                        </td>
                        <td className="num amount-in">{formatAmount(amounts.cashIn)}</td>
                        <td className="num amount-out">{formatAmount(amounts.cashOut)}</td>
                        <td><span className={d.status === 1 ? 'pill off' : d.status === 3 ? 'pill bad' : d.reversalOfId == null ? 'pill ok' : 'pill info'}>
                          {t(statusLabel(d))}</span></td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
            <div className="muted small list-foot">
              {t('cash.footer', { balance: formatAmount(day.closingBalance), currency: day.currencyCode,
                lastClosed: day.lastClosedDay == null ? t('common.none') : isoToRo(day.lastClosedDay) })}
            </div>
          </section>
        </>
      )}
    </div>
  );
}

function Stat({ label, value, tone, suffix }: { readonly label: string; readonly value: string; readonly tone?: 'in' | 'out' | 'current'; readonly suffix?: string }) {
  return (
    <div className={tone === undefined ? 'cash-stat' : `cash-stat ${tone}`}>
      <span className="label">{label}</span>
      <strong>{value}{suffix === undefined ? '' : ` ${suffix}`}</strong>
    </div>
  );
}
