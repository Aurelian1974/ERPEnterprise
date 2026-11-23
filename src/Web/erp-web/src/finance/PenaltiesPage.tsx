import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useOutletContext } from 'react-router';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { SearchSelectField } from '../components/SearchSelectField';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { formatAmount } from './cashApi';
import { addDays } from './DueSchedulePage';

type Calculation = components['schemas']['PenaltyCalculationDto'];
type Line = components['schemas']['PenaltyLineDto'];
type Total = components['schemas']['CurrencyTotalDto'];
type Filter = 'all' | 'drafts' | 'approved' | 'invoiced';

const filters: readonly { readonly value: Filter; readonly label: TextKey; readonly status?: number }[] = [
  { value: 'all', label: 'pen.filter.all' },
  { value: 'drafts', label: 'pen.filter.drafts', status: 1 },
  { value: 'approved', label: 'pen.filter.approved', status: 2 },
  { value: 'invoiced', label: 'pen.filter.invoiced', status: 3 },
];
const statusPills: Readonly<Record<number, string>> = { 1: 'pill warn', 2: 'pill info', 3: 'pill ok', 4: 'pill' };
const statusLabel = (status: number): TextKey => (['pen.status.1', 'pen.status.2', 'pen.status.3', 'pen.status.4'] as const)[status - 1] ?? 'pen.status.1';
const keys = { all: ['finance', 'penalties'] as const };
/** Un procent in format romanesc, cu 2–4 zecimale (6,50; 0,0500). */
export const percent = (value: string | number): string =>
  Number(value).toLocaleString('ro-RO', { minimumFractionDigits: 2, maximumFractionDigits: 4 });

/** Totalurile pe moneda, ca în macheta f3-08: „822,38 + 50,49 EUR” (leii fără monedă, primii). */
export function totalsText(totals: readonly Pick<Total, 'currencyCode' | 'total'>[]): string {
  if (totals.length === 0) return formatAmount('0.00');
  return totals.map((x) => (x.currencyCode === 'RON' ? formatAmount(x.total) : `${formatAmount(x.total)} ${x.currencyCode}`)).join(' + ');
}

/** Rata liniei: „0,10%/zi contract” sau „14,50%/an dobândă legală”; suma fixă de recuperare nu are rată. */
export function rateText(line: Pick<Line, 'kind' | 'ratePerDay' | 'annualRate'>): string {
  if (line.kind === 2) return t('pen.fixed');
  if (line.ratePerDay != null) return t('pen.rate.contract', { rate: percent(line.ratePerDay) });
  return line.annualRate == null ? '' : t('pen.rate.legal', { rate: percent(line.annualRate) });
}

async function searchPartners(text: string): Promise<Option[]> {
  const page = unwrap(await api.GET('/api/core/partners', { params: { query: { ...(text === '' ? {} : { Search: text }), IsActive: true, PageSize: 30 } } }));
  return page.items.map((p) => ({ value: String(p.id), text: p.fiscalCode == null ? p.name : `${p.name} · ${p.fiscalCode}` }));
}

/** Prima zi și ultima zi a lunii trecute, perioada propusă pentru un calcul nou. */
function lastMonth(today: string): { readonly from: string; readonly to: string } {
  const to = addDays(`${today.slice(0, 8)}01`, -1);
  return { from: `${to.slice(0, 8)}01`, to };
}

/**
 * Decontări › Penalități (02.02, F3 3.10, 4.7; macheta f3-08): calculele pe interval, cu totalurile pe monedă și starea, rata de referință
 * BNR (dobânda legală) cu întreținerea ei, fișa calculului cu liniile pe intervale (baza, zilele, rata, penalitatea) și acțiunile pe ciornă:
 * scoate o linie, recalculează, anulează, aprobă (evenimentul către Vânzări).
 */
export function PenaltiesPage() {
  const user = useOutletContext<CurrentUser>();
  const [filter, setFilter] = useState<Filter>('all');
  const [selected, setSelected] = useState<number | 'new' | undefined>();
  const status = filters.find((f) => f.value === filter)?.status;
  const list = useQuery({
    queryKey: [...keys.all, 'list', filter],
    queryFn: async () => unwrap(await api.GET('/api/finance/penalties', { params: { query: status === undefined ? {} : { status } } })),
    placeholderData: keepPreviousData,
  });
  const rates = useQuery({ queryKey: [...keys.all, 'rates'], queryFn: async () => unwrap(await api.GET('/api/finance/reference-rates')) });
  const items = list.data ?? [];
  const current = selected ?? items[0]?.id;
  const canCalculate = user.permissions.includes('Fin.Penalty.Calculate');
  const canApprove = user.permissions.includes('Fin.Penalty.Approve');
  const latest = rates.data?.find((r) => r.validFrom <= isoToday()) ?? rates.data?.[0];

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.settlements')} › {t('menu.penalties')}</div>
      <div className="head">
        <div>
          <h1>{t('pen.title')}</h1>
          <div className="sub2">{t('pen.subtitle')}</div>
        </div>
        {canCalculate && <div className="actions"><button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('pen.new')}</button></div>}
      </div>
      <div className="split cash-document penalty-split">
        <section className="card" aria-label={t('pen.title')}>
          <div className="chips-row" role="group" aria-label={t('pen.filter')}>
            {filters.map((f) => (
              <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                onClick={() => setFilter(f.value)}>{t(f.label)}</button>
            ))}
          </div>
          {list.isError && <ErrorBanner message={list.error.message} />}
          {list.data !== undefined && items.length === 0 ? <p className="muted">{t('pen.empty')}</p> : (
            <table className="partner-list cash-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('pen.col.calculation')}</th>
                  <th scope="col" className="num">{t('pen.col.total')}</th>
                  <th scope="col">{t('bank.col.status')}</th>
                </tr>
              </thead>
              <tbody>
                {items.map((c) => (
                  <tr key={c.id} className={c.id === current ? 'clickable picked' : 'clickable'} tabIndex={0} onClick={() => setSelected(c.id)}
                    onKeyDown={(e) => e.key === 'Enter' && setSelected(c.id)}>
                    <td><div className="strong">{c.number}</div>
                      <div className="muted small">{t('pen.period', { from: isoToRo(c.periodFrom), to: isoToRo(c.periodTo) })} · {c.partnerName ?? t('pen.allPartners')}</div></td>
                    <td className="num">{totalsText(c.totals)}</td>
                    <td><span className={statusPills[c.status] ?? 'pill'}>{t(statusLabel(c.status))}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="muted small">
            {latest === undefined ? t('pen.refRateMissing')
              : t('pen.refRate', { date: isoToRo(latest.validFrom), rate: percent(latest.rate), legal: percent(Number(latest.rate) + 8) })}
          </p>
        </section>
        <aside className="card cash-side">
          {current === 'new' ? <CalculateForm onSaved={(id) => setSelected(id)} />
            : current === undefined ? <p className="muted">{t('pen.pick')}</p>
              : <Detail key={current} id={current} canCalculate={canCalculate} canApprove={canApprove} />}
        </aside>
      </div>
    </div>
  );
}

function CalculateForm({ onSaved }: { readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [period, setPeriod] = useState(() => lastMonth(isoToday()));
  const form = useForm<{ partnerId: number | null }>({ defaultValues: { partnerId: null } });
  const partnerId = useWatch({ control: form.control, name: 'partnerId' }) ?? null;
  const [error, setError] = useState<string | undefined>();

  const calculate = async () => {
    setError(undefined);
    try {
      const saved = unwrap(await api.POST('/api/finance/penalties', { body: { periodFrom: period.from, periodTo: period.to, partnerId } }));
      notify(t('pen.calculated'));
      await queryClient.invalidateQueries({ queryKey: keys.all });
      onSaved(saved.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="settings-form">
      <h2>{t('pen.new')}</h2>
      <div className="row-between">
        <label>{t('pen.from')}<input className="cell-input" type="date" value={period.from} onChange={(e) => setPeriod({ ...period, from: e.target.value })} /></label>
        <label>{t('pen.to')}<input className="cell-input" type="date" value={period.to} onChange={(e) => setPeriod({ ...period, to: e.target.value })} /></label>
      </div>
      <SearchSelectField control={form.control} name="partnerId" label={t('pen.partner')} current={undefined} search={searchPartners} />
      <ErrorBanner message={error} />
      <div className="actions"><button type="button" className="btn primary" onClick={() => void calculate()}>{t('pen.calculate')}</button></div>
    </div>
  );
}

function Detail({ id, canCalculate, canApprove }: { readonly id: number; readonly canCalculate: boolean; readonly canApprove: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const detail = useQuery({
    queryKey: [...keys.all, 'detail', id],
    queryFn: async () => unwrap(await api.GET('/api/finance/penalties/{id}', { params: { path: { id } } })),
  });
  if (detail.isError) return <ErrorBanner message={detail.error.message} />;
  const c: Calculation | undefined = detail.data;
  if (c === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  const draft = c.status === 1;
  const partners = new Set(c.lines.map((l) => l.partnerId)).size;

  const run = async (action: () => Promise<unknown>, message: TextKey) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: keys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  const body = { id, rowVersion: c.rowVersion };

  return (
    <div>
      <h2>{c.number} <span className={statusPills[c.status] ?? 'pill'}>{t(statusLabel(c.status))}</span></h2>
      <p className="muted small">
        {t('pen.meta', { date: isoToRo(c.calculationDate), period: t('pen.period', { from: isoToRo(c.periodFrom), to: isoToRo(c.periodTo) }), disputed: c.disputedCount })}
        {' · '}{c.partnerName ?? t('pen.allPartners')}
      </p>
      <div className="kpi-row">
        {c.totals.map((x) => (
          <div key={x.currencyCode} className="kpi"><div className="kpi-label">{t('pen.col.total')}</div>
            <div className="kpi-value">{formatAmount(x.total)} <span className="muted small">{x.currencyCode}</span></div></div>
        ))}
        <div className="kpi"><div className="kpi-label">{t('conf.col.partner')}</div><div className="kpi-value">{t('pen.partners', { count: partners })}</div></div>
      </div>
      {c.lines.length === 0 ? <p className="muted">{t('pen.noLines')}</p> : (
        <table className="partner-list cash-list">
          <thead>
            <tr>
              <th scope="col">{t('pen.col.line')}</th>
              <th scope="col" className="num">{t('pen.col.base')}</th>
              <th scope="col">{t('pen.col.interval')}</th>
              <th scope="col" className="num">{t('pen.col.days')}</th>
              <th scope="col">{t('pen.col.rate')}</th>
              <th scope="col" className="num">{t('pen.col.penalty')}</th>
              {draft && canCalculate && <th scope="col"><span className="sr-only">{t('pen.removeLine')}</span></th>}
            </tr>
          </thead>
          <tbody>
            {c.lines.map((l) => (
              <tr key={l.id}>
                <td><div className="strong">{l.partnerName}</div><div className="muted small">{l.documentNo} · {t('pen.due', { date: isoToRo(l.dueDate) })}</div></td>
                <td className="num">{formatAmount(l.baseAmount)}</td>
                <td>{l.fromDate == null || l.toDate == null ? '' : t('pen.period', { from: isoToRo(l.fromDate).slice(0, 5), to: isoToRo(l.toDate).slice(0, 5) })}</td>
                <td className="num">{l.kind === 1 ? l.days : ''}</td>
                <td className="muted small">{rateText(l)}</td>
                <td className="num">{formatAmount(l.penaltyAmount)}{l.currencyCode === 'RON' ? '' : ` ${l.currencyCode}`}</td>
                {draft && canCalculate && (
                  <td><button type="button" className="btn ghost" aria-label={t('pen.removeLine')}
                    onClick={() => void run(async () => unwrap(await api.POST('/api/finance/penalties/{id}/remove-lines', { params: { path: { id } },
                      body: { ...body, lineIds: [l.id] } })), 'pen.lineRemoved')}>×</button></td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <p className="muted small">{t('pen.baseNote')}</p>
      <p className="muted small">{t('pen.salesNote')}</p>
      {c.approvedAt != null && <p className="muted small">{t('pen.approvedBy', { user: c.approvedByName ?? '', date: isoToRo(c.approvedAt.slice(0, 10)) })}</p>}
      <ErrorBanner message={error} />
      {draft && (canCalculate || canApprove) && (
        <div className="actions">
          {canCalculate && <button type="button" className="btn ghost" onClick={() => void run(async () => unwrap(await api.POST('/api/finance/penalties/{id}/recalculate',
            { params: { path: { id } }, body })), 'pen.recalculated')}>{t('pen.recalculate')}</button>}
          {canCalculate && <button type="button" className="btn ghost" onClick={() => void run(async () => unwrap(await api.POST('/api/finance/penalties/{id}/cancel',
            { params: { path: { id } }, body })), 'pen.cancelled')}>{t('pen.cancel')}</button>}
          {canApprove && <button type="button" className="btn primary" disabled={c.lines.length === 0} onClick={() => void run(async () => unwrap(await api.POST(
            '/api/finance/penalties/{id}/approve', { params: { path: { id } }, body })), 'pen.approved')}>✓ {t('pen.approve')}</button>}
        </div>
      )}
    </div>
  );
}
