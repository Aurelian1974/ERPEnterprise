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
import { useBankAccounts } from './bankApi';
import { formatAmount } from './cashApi';
import { addDays } from './DueSchedulePage';
import { invoiceChoices, invoiceText } from './InvoicePicker';
import { Role, useOpenItems } from './settlementApi';
import { parseAmount } from './TreasurySettingsTab';

type Paper = components['schemas']['CommercialPaperDetailDto'];
type PaperItem = components['schemas']['CommercialPaperItemDto'];
type Filter = 'active' | 'received' | 'issued' | 'due7' | 'history';

const filters: readonly { readonly value: Filter; readonly label: TextKey }[] = [
  { value: 'active', label: 'paper.filter.active' },
  { value: 'received', label: 'paper.filter.received' },
  { value: 'issued', label: 'paper.filter.issued' },
  { value: 'due7', label: 'paper.filter.due7' },
  { value: 'history', label: 'paper.filter.history' },
];

/** Eticheta starii unui efect (F3 3.5); la cele emise, „in portofoliu” inseamna emis, de platit, iar „incasat” platit. */
export function paperStatusLabel(status: number, direction: number): TextKey {
  if (direction === 2 && status === 1) return 'paper.status.issued';
  if (direction === 2 && status === 3) return 'paper.status.paid';
  return (['paper.status.portfolio', 'paper.status.deposited', 'paper.status.collected', 'paper.status.refused', 'paper.status.endorsed', 'paper.status.reversed'] as const)[
    status - 1] ?? 'paper.status.portfolio';
}

const statusPills: Readonly<Record<number, string>> = { 1: 'pill info', 2: 'pill warn', 3: 'pill ok', 4: 'pill bad', 5: 'pill', 6: 'pill' };
const keys = { all: ['finance', 'papers'] as const };

async function searchPartners(text: string): Promise<Option[]> {
  const page = unwrap(await api.GET('/api/core/partners', { params: { query: { ...(text === '' ? {} : { Search: text }), IsActive: true, PageSize: 30 } } }));
  return page.items.map((p) => ({ value: String(p.id), text: p.fiscalCode == null ? p.name : `${p.name} · ${p.fiscalCode}` }));
}

/**
 * Decontări › Efecte comerciale (02.02, F3 3.5, 4.5; macheta f3-06): portofoliul de CEC-uri și bilete la ordin primite și emise, cu
 * totalurile, filtrele, fișa efectului (facturile stinse la primire, istoricul stărilor) și acțiunile: depunere la bancă, încasat / plătit,
 * refuzat (facturile redevin deschise), gir către un furnizor; înregistrarea unui efect primit sau emis.
 */
export function CommercialPapersPage() {
  const user = useOutletContext<CurrentUser>();
  const [filter, setFilter] = useState<Filter>('active');
  const [selected, setSelected] = useState<number | 'new-1' | 'new-2' | undefined>();
  const list = useQuery({
    queryKey: [...keys.all, 'list', filter],
    queryFn: async () => unwrap(await api.GET('/api/finance/commercial-papers', { params: { query: {
      ...(filter === 'received' ? { direction: 1 } : {}), ...(filter === 'issued' ? { direction: 2 } : {}),
      ...(filter === 'active' || filter === 'due7' ? { activeOnly: true } : {}), ...(filter === 'due7' ? { maturityTo: addDays(isoToday(), 7) } : {}) } } })),
    placeholderData: keepPreviousData,
  });
  const totals = list.data?.totals;
  const items = list.data?.items ?? [];
  const current = selected ?? items[0]?.id;
  const canManage = user.permissions.includes('Fin.CommercialPaper.Manage');

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.settlements')} › {t('menu.commercialPapers')}</div>
      <div className="head">
        <div>
          <h1>{t('paper.title')}</h1>
          <div className="sub2">{t('paper.subtitle')}</div>
        </div>
        {canManage && (
          <div className="actions">
            <button type="button" className="btn primary" onClick={() => setSelected('new-1')}>{t('paper.newReceived')}</button>
            <button type="button" className="btn ghost" onClick={() => setSelected('new-2')}>{t('paper.newIssued')}</button>
          </div>
        )}
      </div>
      {totals !== undefined && (
        <div className="kpi-row">
          <div className="kpi"><div className="kpi-label">{t('paper.kpi.portfolio')}</div><div className="kpi-value">{formatAmount(totals.inPortfolio)} <span className="muted small">RON</span></div>
            <div className="muted small">{t('paper.kpi.count', { count: totals.inPortfolioCount })}</div></div>
          <div className="kpi"><div className="kpi-label">{t('paper.kpi.deposited')}</div><div className="kpi-value">{formatAmount(totals.deposited)} <span className="muted small">RON</span></div>
            <div className="muted small">{t('paper.kpi.count', { count: totals.depositedCount })}</div></div>
          <div className="kpi warn"><div className="kpi-label">{t('paper.kpi.issued')}</div><div className="kpi-value">{formatAmount(totals.issuedToPay)} <span className="muted small">RON</span></div>
            <div className="muted small">{t('paper.kpi.count', { count: totals.issuedToPayCount })}</div></div>
          <div className="kpi bad"><div className="kpi-label">{t('paper.kpi.refused')}</div><div className="kpi-value">{formatAmount(totals.refused)} <span className="muted small">RON</span></div>
            <div className="muted small">{t('paper.kpi.count', { count: totals.refusedCount })}</div></div>
        </div>
      )}
      <div className="split cash-document">
        <section className="card" aria-label={t('paper.title')}>
          <div className="chips-row" role="group" aria-label={t('paper.filter')}>
            {filters.map((f) => (
              <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                onClick={() => setFilter(f.value)}>{t(f.label)}</button>
            ))}
          </div>
          {list.isError && <ErrorBanner message={list.error.message} />}
          {list.data !== undefined && items.length === 0 ? <p className="muted">{t('paper.empty')}</p> : (
            <table className="partner-list cash-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('paper.col.instrument')}</th>
                  <th scope="col" className="num">{t('settle.col.amount')}</th>
                  <th scope="col">{t('paper.col.maturity')}</th>
                  <th scope="col">{t('bank.col.status')}</th>
                </tr>
              </thead>
              <tbody>
                {items.map((p) => <Row key={p.id} paper={p} selected={p.id === current} onSelect={() => setSelected(p.id)} />)}
              </tbody>
            </table>
          )}
          <p className="muted small">{t('paper.legal')}</p>
        </section>
        <aside className="card cash-side">
          {current === 'new-1' || current === 'new-2'
            ? <RegisterForm key={current} direction={current === 'new-1' ? 1 : 2} onSaved={(id) => setSelected(id)} />
            : current === undefined ? <p className="muted">{t('paper.pick')}</p> : <Detail key={current} id={current} canManage={canManage} />}
        </aside>
      </div>
    </div>
  );
}

function Row({ paper: p, selected, onSelect }: { readonly paper: PaperItem; readonly selected: boolean; readonly onSelect: () => void }) {
  return (
    <tr className={selected ? 'clickable picked' : 'clickable'} tabIndex={0} onClick={onSelect} onKeyDown={(e) => e.key === 'Enter' && onSelect()}>
      <td><div className="strong">{t(p.kind === 1 ? 'paper.kind.cheque' : 'paper.kind.note')} · {p.partnerName}</div>
        <div className="muted small">{p.series} {p.number} · {t(p.direction === 1 ? 'paper.received' : 'paper.issued')}</div></td>
      <td className="num">{formatAmount(p.amount)}{p.currencyCode === 'RON' ? '' : ` ${p.currencyCode}`}</td>
      <td>{isoToRo(p.maturityDate)}</td>
      <td><span className={statusPills[p.status] ?? 'pill'}>{t(paperStatusLabel(p.status, p.direction))}</span></td>
    </tr>
  );
}

function Detail({ id, canManage }: { readonly id: number; readonly canManage: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const accounts = useBankAccounts();
  const [error, setError] = useState<string | undefined>();
  const [action, setAction] = useState<'deposit' | 'endorse' | undefined>();
  const [account, setAccount] = useState<number | undefined>();
  const form = useForm<{ supplierId: number | null }>({ defaultValues: { supplierId: null } });
  const supplierId = useWatch({ control: form.control, name: 'supplierId' }) ?? undefined;
  const supplierItems = useOpenItems(supplierId, true, Role.Supplier);
  const [invoices, setInvoices] = useState<readonly number[]>([]);
  const detail = useQuery({
    queryKey: [...keys.all, 'detail', id],
    queryFn: async () => unwrap(await api.GET('/api/finance/commercial-papers/{id}', { params: { path: { id } } })),
  });
  if (detail.isError) return <ErrorBanner message={detail.error.message} />;
  const p: Paper | undefined = detail.data;
  if (p === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  const received = p.direction === 1;
  const active = p.status === 1 || p.status === 2;

  const change = async (status: number, extra: { treasuryAccountId?: number; endorsedToPartnerId?: number; endorsedInvoiceIds?: number[]; note?: string } = {},
    confirm = false): Promise<void> => {
    setError(undefined);
    try {
      unwrap(await api.POST('/api/finance/commercial-papers/{id}/status', { params: { path: { id } }, body: { id, status, eventDate: null,
        treasuryAccountId: extra.treasuryAccountId ?? null, bankStatementLineId: null, endorsedToPartnerId: extra.endorsedToPartnerId ?? null,
        endorsedInvoiceIds: extra.endorsedInvoiceIds ?? null, note: extra.note ?? null, rowVersion: p.rowVersion, confirmWarnings: confirm } }));
      notify(t('paper.changed'));
      setAction(undefined);
      await queryClient.invalidateQueries({ queryKey: keys.all });
      await queryClient.invalidateQueries({ queryKey: ['finance', 'settlement'] });
    } catch (e) {
      if (!confirm && e instanceof ApiError && e.code === 'FIN_OPEN_ITEM_NOT_IN_SPV_UNCONFIRMED' && window.confirm(`${e.message}\n\n${t('cash.confirmWarning')}`)) {
        await change(status, extra, true);
        return;
      }
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  const refuse = () => {
    const note = window.prompt(t('paper.refuseReason'), '');
    if (note != null && note.trim() !== '') void change(4, { note });
  };

  return (
    <div>
      <h2>{t(p.kind === 1 ? 'paper.kind.chequeLong' : 'paper.kind.noteLong')} {p.series} {p.number}{' '}
        <span className={statusPills[p.status] ?? 'pill'}>{t(paperStatusLabel(p.status, p.direction))}</span></h2>
      <p className="muted small">{t(received ? 'paper.from' : 'paper.to', { partner: p.partnerName })} · {t('paper.issuedOn', { date: isoToRo(p.issueDate) })} ·{' '}
        {t('paper.maturity', { date: isoToRo(p.maturityDate) })}{p.issuerBank == null ? '' : ` · ${p.issuerBank}`}</p>
      <div className="kpi-row">
        <div className="kpi"><div className="kpi-label">{t('settle.col.amount')}</div><div className="kpi-value">{formatAmount(p.amount)} {p.currencyCode}</div>
          <div className="muted small">{p.treasuryAccountCode == null ? '' : t('paper.depositedIn', { account: p.treasuryAccountCode })}</div></div>
      </div>
      <h3 className="section-title">{t('paper.invoices')}</h3>
      {p.invoices.length === 0 ? <p className="muted small">{t('paper.noInvoices')}</p> : (
        <table className="partner-list cash-list">
          <tbody>
            {p.invoices.map((i) => (
              <tr key={i.openItemId} className={i.isActive ? undefined : 'muted'}>
                <td>{i.documentNo}</td><td>{isoToRo(i.documentDate)}</td><td className="num">{formatAmount(i.amount)}</td>
                <td>{!i.isActive && <span className="pill">{t('paper.undone')}</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <h3 className="section-title">{t('paper.history')}</h3>
      <ol className="paper-history">
        {p.events.map((e, n) => (
          <li key={n}><span className="strong">{t(paperStatusLabel(e.status, p.direction))}</span> {isoToRo(e.eventDate)}
            {e.treasuryAccountCode == null ? '' : ` · ${e.treasuryAccountCode}`}{e.note == null ? '' : ` · ${e.note}`}</li>
        ))}
      </ol>
      {received && active && <p className="muted small">{t('paper.matchNote')}</p>}
      <ErrorBanner message={error} />
      {action === 'deposit' && (
        <div className="order-from-invoices">
          <label>{t('bank.account')}
            <select className="cell-input" value={account ?? accounts.data?.[0]?.id ?? ''} onChange={(e) => setAccount(Number(e.target.value))}>
              {(accounts.data ?? []).map((a) => <option key={a.id} value={a.id}>{`${a.code} · ${a.currencyCode}`}</option>)}
            </select>
          </label>
          <button type="button" className="btn primary" onClick={() => void change(2, { treasuryAccountId: account ?? accounts.data?.[0]?.id ?? 0 })}>{t('paper.confirmDeposit')}</button>
        </div>
      )}
      {action === 'endorse' && (
        <div className="order-from-invoices">
          <SearchSelectField control={form.control} name="supplierId" label={t('paper.endorseTo')} current={undefined} search={searchPartners} />
          {invoiceChoices(supplierItems.data ?? [], Role.Supplier).map((c) => (
            <label key={c.openItemId} className="check"><input type="checkbox" checked={invoices.includes(c.openItemId)}
              onChange={(e) => setInvoices(e.target.checked ? [...invoices, c.openItemId] : invoices.filter((x) => x !== c.openItemId))} />{invoiceText(c)}</label>
          ))}
          <button type="button" className="btn primary" disabled={supplierId === undefined}
            onClick={() => void change(5, { endorsedToPartnerId: supplierId ?? 0, endorsedInvoiceIds: [...invoices] })}>{t('paper.confirmEndorse')}</button>
        </div>
      )}
      {canManage && active && (
        <div className="actions">
          <button type="button" className="btn ghost" onClick={refuse}>{t('paper.refuse')}</button>
          {received && p.status === 1 && <button type="button" className="btn ghost" onClick={() => setAction('deposit')}>{t('paper.deposit')}</button>}
          {received && p.status === 1 && <button type="button" className="btn ghost" onClick={() => setAction('endorse')}>{t('paper.endorse')}</button>}
          <button type="button" className="btn primary" onClick={() => void change(3)}>{t(received ? 'paper.collect' : 'paper.pay')}</button>
        </div>
      )}
    </div>
  );
}

function RegisterForm({ direction, onSaved }: { readonly direction: 1 | 2; readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const form = useForm<{ partnerId: number | null }>({ defaultValues: { partnerId: null } });
  const partnerId = useWatch({ control: form.control, name: 'partnerId' }) ?? undefined;
  const role = direction === 1 ? Role.Customer : Role.Supplier;
  const items = useOpenItems(partnerId, true, role);
  const today = isoToday();
  const [draft, setDraft] = useState({ kind: 1, series: '', number: '', issuerBank: '', amount: '', issueDate: today, maturityDate: today });
  const [invoices, setInvoices] = useState<readonly number[]>([]);
  const [error, setError] = useState<string | undefined>();
  const set = (patch: Partial<typeof draft>) => setDraft({ ...draft, ...patch });
  const amount = parseAmount(draft.amount);

  const save = async () => {
    setError(undefined);
    if (partnerId === undefined || amount === null) {
      setError(t('paper.required'));
      return;
    }
    try {
      const saved = unwrap(await api.POST('/api/finance/commercial-papers', { body: { partnerId, direction, kind: draft.kind, series: draft.series, number: draft.number,
        issuerBank: draft.issuerBank === '' ? null : draft.issuerBank, issuerAccount: null, amount, currencyCode: 'RON', issueDate: draft.issueDate,
        maturityDate: draft.maturityDate, note: null, invoiceIds: [...invoices] } }));
      notify(t('paper.registered'));
      await queryClient.invalidateQueries({ queryKey: keys.all });
      await queryClient.invalidateQueries({ queryKey: ['finance', 'settlement'] });
      onSaved(saved.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="settings-form">
      <h2>{t(direction === 1 ? 'paper.newReceived' : 'paper.newIssued')}</h2>
      <SearchSelectField control={form.control} name="partnerId" label={t(direction === 1 ? 'settle.role.customer' : 'settle.role.supplier')} current={undefined}
        search={searchPartners} />
      <label>{t('paper.kind')}
        <select className="cell-input" value={draft.kind} onChange={(e) => set({ kind: Number(e.target.value) })}>
          <option value={1}>{t('paper.kind.chequeLong')}</option>
          <option value={2}>{t('paper.kind.noteLong')}</option>
        </select>
      </label>
      <div className="row-between">
        <label>{t('paper.series')}<input className="cell-input" value={draft.series} onChange={(e) => set({ series: e.target.value })} /></label>
        <label>{t('paper.number')}<input className="cell-input" value={draft.number} onChange={(e) => set({ number: e.target.value })} /></label>
      </div>
      <label>{t('paper.bank')}<input className="cell-input" value={draft.issuerBank} onChange={(e) => set({ issuerBank: e.target.value })} /></label>
      <label>{t('settle.col.amount')}<input className="cell-input num" value={draft.amount} aria-invalid={draft.amount !== '' && amount === null}
        onChange={(e) => set({ amount: e.target.value })} /></label>
      <div className="row-between">
        <label>{t('paper.issueDate')}<input className="cell-input" type="date" value={draft.issueDate} onChange={(e) => set({ issueDate: e.target.value })} /></label>
        <label>{t('paper.maturityDate')}<input className="cell-input" type="date" value={draft.maturityDate} onChange={(e) => set({ maturityDate: e.target.value })} /></label>
      </div>
      {partnerId !== undefined && (
        <>
          <h3 className="section-title">{t('paper.settles')}</h3>
          {invoiceChoices(items.data ?? [], role).map((c) => (
            <label key={c.openItemId} className="check"><input type="checkbox" checked={invoices.includes(c.openItemId)}
              onChange={(e) => setInvoices(e.target.checked ? [...invoices, c.openItemId] : invoices.filter((x) => x !== c.openItemId))} />{invoiceText(c)}</label>
          ))}
        </>
      )}
      <ErrorBanner message={error} />
      <div className="actions"><button type="button" className="btn primary" onClick={() => void save()}>{t('paper.register')}</button></div>
    </div>
  );
}
