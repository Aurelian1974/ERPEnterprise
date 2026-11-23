import { useState } from 'react';
import { Link, useOutletContext, useParams } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { formatAmount } from './cashApi';
import { allocationSourceLabel, eInvoiceLabel, eInvoicePill, kindLabel, overdueDays, Role, settlementKeys, useFinanceProfile, useOpenItems,
  usePartnerAllocations, usePartnerBalance, type FinanceProfile, type OpenItem, type PartnerAllocation } from './settlementApi';

type Tab = 'open' | 'documents' | 'allocations' | 'profile';

/**
 * Decontări › Fișa financiară a partenerului (02.02, F3 11; macheta f3-02): soldurile ca client și ca furnizor, partidele deschise pe rol
 * (scadență, întârziere, rest, TVA, e-Factura, litigiu, blocare la plată), toate documentele, alocările (cu desfacerea) și profilul financiar.
 */
export function PartnerFinancePage() {
  const user = useOutletContext<CurrentUser>();
  const partnerId = Number(useParams().id);
  const partner = useQuery({
    queryKey: ['core', 'partners', partnerId],
    queryFn: async () => unwrap(await api.GET('/api/core/partners/{id}', { params: { path: { id: partnerId } } })),
  });
  const balance = usePartnerBalance(partnerId);
  const [tab, setTab] = useState<Tab>('open');
  const ron = (role: number) => (balance.data ?? []).filter((b) => b.role === role).reduce((sum, b) => sum + b.balanceRon, 0);
  const overdue = (balance.data ?? []).filter((b) => b.role === Role.Customer).reduce((sum, b) => sum + b.overdueRon, 0);
  const customer = ron(Role.Customer);
  const supplier = ron(Role.Supplier);
  const tabs: readonly [Tab, string][] = [
    ['open', t('settle.tab.open')], ['documents', t('settle.tab.documents')], ['allocations', t('settle.tab.allocations')], ['profile', t('settle.tab.profile')],
  ];

  return (
    <div className="page wide">
      <div className="crumb">
        {t('module.Finance')} › {t('menu.settlements')} › <Link to="/finante/decontari/solduri">{t('menu.partnerBalances')}</Link> › {partner.data?.name ?? ''}
      </div>
      <div className="head">
        <div>
          <h1>{partner.data?.name ?? t('settle.sheet.title')}</h1>
          <div className="sub2">{partner.data === undefined ? '' : `${partner.data.code}${partner.data.fiscalCode == null ? '' : ` · ${partner.data.fiscalCode}`}`}</div>
        </div>
        <div className="actions">
          <Link to={`/finante/decontari/alocare?partener=${partnerId}`} className="btn ghost">{t('menu.allocation')}</Link>
        </div>
      </div>
      {partner.isError && <ErrorBanner message={partner.error.message} />}
      <div className="kpi-row" role="group" aria-label={t('settle.sheet.balances')}>
        <div className="kpi"><div className="kpi-label">{t('settle.sheet.customerBalance')}</div><div className="kpi-value">{formatAmount(customer.toFixed(2))}</div></div>
        <div className="kpi bad"><div className="kpi-label">{t('settle.kpi.overdue')}</div><div className="kpi-value">{formatAmount(overdue.toFixed(2))}</div></div>
        <div className="kpi"><div className="kpi-label">{t('settle.sheet.supplierBalance')}</div><div className="kpi-value">{formatAmount((-supplier).toFixed(2))}</div></div>
        <div className="kpi"><div className="kpi-label">{t('settle.sheet.net')}</div><div className="kpi-value">{formatAmount((customer + supplier).toFixed(2))}</div></div>
      </div>
      <div className="tabs" role="tablist" aria-label={partner.data?.name ?? t('settle.sheet.title')}>
        {tabs.map(([key, label]) => (
          <button key={key} type="button" role="tab" aria-selected={tab === key} className={tab === key ? 'tab on' : 'tab'} onClick={() => setTab(key)}>{label}</button>
        ))}
      </div>
      <section className="card tab-body" role="tabpanel">
        {tab === 'open' && <OpenItemsTab partnerId={partnerId} user={user} />}
        {tab === 'documents' && <DocumentsTab partnerId={partnerId} />}
        {tab === 'allocations' && <AllocationsTab partnerId={partnerId} canUndo={user.permissions.includes('Fin.Allocation.Undo')} />}
        {tab === 'profile' && <ProfileTab partnerId={partnerId} user={user} />}
      </section>
      {customer > 0 && supplier < 0 && (
        <div className="banner info">{t('settle.sheet.canCompensate', { amount: formatAmount(Math.min(customer, -supplier).toFixed(2)) })}</div>
      )}
    </div>
  );
}

function OpenItemsTab({ partnerId, user }: { readonly partnerId: number; readonly user: CurrentUser }) {
  const items = useOpenItems(partnerId);
  if (items.isError) return <ErrorBanner message={items.error.message} />;
  if (items.data === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  const customer = items.data.filter((i) => i.role === Role.Customer);
  const supplier = items.data.filter((i) => i.role === Role.Supplier);
  if (items.data.length === 0) return <p className="muted">{t('settle.sheet.noOpen')}</p>;
  return (
    <>
      {customer.length > 0 && <ItemsTable title={t('settle.sheet.asCustomer')} items={customer} partnerId={partnerId} user={user} />}
      {supplier.length > 0 && <ItemsTable title={t('settle.sheet.asSupplier')} items={supplier} partnerId={partnerId} user={user} />}
    </>
  );
}

function ItemsTable({ title, items, partnerId, user }: { readonly title: string; readonly items: readonly OpenItem[]; readonly partnerId: number;
  readonly user: CurrentUser }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const today = isoToday();
  const total = items.reduce((sum, i) => sum + Number(i.openAmount), 0);
  const canDispute = user.permissions.includes('Fin.OpenItem.SetDispute');
  const canBlock = user.permissions.includes('Fin.OpenItem.BlockPayment');

  const toggleDispute = async (item: OpenItem) => {
    setError(undefined);
    const note = item.isDisputed ? null : window.prompt(t('settle.disputePrompt'), '');
    if (!item.isDisputed && (note == null || note.trim() === '')) return;
    try {
      unwrap(await api.POST('/api/finance/open-items/{id}/dispute', { params: { path: { id: item.openItemId } },
        body: { openItemId: item.openItemId, isDisputed: !item.isDisputed, note, rowVersion: item.rowVersion } }));
      notify(t('common.saved'));
      await queryClient.invalidateQueries({ queryKey: settlementKeys.partner(partnerId) });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  const toggleBlock = async (item: OpenItem) => {
    setError(undefined);
    try {
      unwrap(await api.POST('/api/finance/open-items/{id}/payment-block', { params: { path: { id: item.openItemId } },
        body: { openItemId: item.openItemId, isBlocked: !item.isBlockedForPayment, rowVersion: item.rowVersion } }));
      notify(t('common.saved'));
      await queryClient.invalidateQueries({ queryKey: settlementKeys.partner(partnerId) });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="items-block">
      <h3 className="section-title">{title}</h3>
      <ErrorBanner message={error} />
      <table className="partner-list cash-list">
        <thead>
          <tr>
            <th scope="col">{t('settle.col.document')}</th>
            <th scope="col">{t('bank.col.date')}</th>
            <th scope="col">{t('settle.col.due')}</th>
            <th scope="col">{t('settle.col.delay')}</th>
            <th scope="col" className="num">{t('settle.col.amount')}</th>
            <th scope="col" className="num">{t('settle.col.open')}</th>
            <th scope="col" className="num">{t('settle.col.vat')}</th>
            <th scope="col">{t('settle.col.einvoice')}</th>
            <th scope="col"><span className="sr-only">{t('common.actions')}</span></th>
          </tr>
        </thead>
        <tbody>
          {items.map((i) => {
            const isInvoice = i.kind <= 4 || i.kind === 6;
            const delay = isInvoice ? overdueDays(i.dueDate, today) : 0;
            const einv = eInvoiceLabel(i.eInvoiceStatus);
            return (
              <tr key={i.openItemId}>
                <td><div className="strong">{i.documentNo}</div><div className="muted small">{t(kindLabel(i.kind))}</div></td>
                <td>{isoToRo(i.documentDate)}</td>
                <td>{isInvoice ? isoToRo(i.dueDate) : '—'}</td>
                <td>{!isInvoice ? <span className="pill">{t('settle.unallocatedPill')}</span>
                  : delay > 0 ? <span className="pill bad">{t('settle.days', { count: delay })}</span>
                    : <span className="muted">{i.dueDate === today ? t('settle.dueToday') : t('settle.inTerm')}</span>}</td>
                <td className="num">{formatAmount(i.amount)}{i.currencyCode === 'RON' ? '' : ` ${i.currencyCode}`}</td>
                <td className="num strong">{formatAmount(i.openAmount)}</td>
                <td className="num">{i.isVatOnCollection ? <span title={t('settle.vatNonExigible')}>{formatAmount(i.vatNonExigibleRon)} *</span>
                  : i.vatAmount === '0.00' ? '—' : formatAmount(i.vatAmount)}</td>
                <td>{einv === undefined ? '—' : <span className={eInvoicePill(i.eInvoiceStatus)}>{t(einv)}</span>}
                  {i.isDisputed && <span className="pill bad" title={i.disputeNote ?? undefined}>{t('settle.disputed')}</span>}
                  {i.isBlockedForPayment && <span className="pill warn">{t('settle.blocked')}</span>}</td>
                <td className="row-actions">
                  {canDispute && isInvoice && <button type="button" className="btn ghost small" onClick={() => void toggleDispute(i)}>
                    {t(i.isDisputed ? 'settle.undispute' : 'settle.dispute')}</button>}
                  {canBlock && isInvoice && i.role === Role.Supplier && <button type="button" className="btn ghost small" onClick={() => void toggleBlock(i)}>
                    {t(i.isBlockedForPayment ? 'settle.unblock' : 'settle.block')}</button>}
                </td>
              </tr>
            );
          })}
        </tbody>
        <tfoot>
          <tr><td colSpan={5} className="strong">{t('settle.total')}</td><td className="num strong">{formatAmount(total.toFixed(2))}</td><td colSpan={3} /></tr>
        </tfoot>
      </table>
      {items.some((i) => i.isVatOnCollection) && <p className="muted small">{t('settle.vatNote')}</p>}
    </div>
  );
}

function DocumentsTab({ partnerId }: { readonly partnerId: number }) {
  const items = useOpenItems(partnerId, false);
  if (items.isError) return <ErrorBanner message={items.error.message} />;
  if (items.data === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  return (
    <table className="partner-list cash-list">
      <thead>
        <tr>
          <th scope="col">{t('settle.col.document')}</th>
          <th scope="col">{t('settle.col.role')}</th>
          <th scope="col">{t('bank.col.date')}</th>
          <th scope="col" className="num">{t('settle.col.amount')}</th>
          <th scope="col" className="num">{t('settle.col.open')}</th>
          <th scope="col">{t('settle.col.closed')}</th>
        </tr>
      </thead>
      <tbody>
        {[...items.data].sort((a, b) => b.documentDate.localeCompare(a.documentDate)).map((i) => (
          <tr key={i.openItemId}>
            <td><div className="strong">{i.documentNo}</div><div className="muted small">{t(kindLabel(i.kind))}</div></td>
            <td>{t(i.role === Role.Customer ? 'settle.role.customer' : 'settle.role.supplier')}</td>
            <td>{isoToRo(i.documentDate)}</td>
            <td className="num">{formatAmount(i.amount)}</td>
            <td className="num">{formatAmount(i.openAmount)}</td>
            <td>{i.closedAt == null ? '—' : isoToRo(i.closedAt)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function AllocationsTab({ partnerId, canUndo }: { readonly partnerId: number; readonly canUndo: boolean }) {
  const allocations = usePartnerAllocations(partnerId);
  if (allocations.isError) return <ErrorBanner message={allocations.error.message} />;
  if (allocations.data === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  if (allocations.data.length === 0) return <p className="muted">{t('settle.sheet.noAllocations')}</p>;
  return <AllocationTable allocations={allocations.data} partnerId={partnerId} canUndo={canUndo} />;
}

/** Alocarile cu desfacerea lor (motiv obligatoriu), comune fisei si ecranului Alocare. */
export function AllocationTable({ allocations, partnerId, canUndo }: { readonly allocations: readonly PartnerAllocation[]; readonly partnerId: number;
  readonly canUndo: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [undoing, setUndoing] = useState<number | undefined>();
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | undefined>();

  const undo = async (id: number) => {
    setError(undefined);
    try {
      unwrap(await api.POST('/api/finance/allocations/{id}/undo', { params: { path: { id } }, body: { allocationId: id, reason } }));
      notify(t('settle.undone'));
      setUndoing(undefined);
      setReason('');
      await queryClient.invalidateQueries({ queryKey: settlementKeys.partner(partnerId) });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <>
      <ErrorBanner message={error} />
      <table className="partner-list cash-list">
        <thead>
          <tr>
            <th scope="col">{t('bank.col.date')}</th>
            <th scope="col">{t('settle.col.debit')}</th>
            <th scope="col">{t('settle.col.credit')}</th>
            <th scope="col" className="num">{t('settle.col.amount')}</th>
            <th scope="col">{t('settle.col.source')}</th>
            <th scope="col"><span className="sr-only">{t('common.actions')}</span></th>
          </tr>
        </thead>
        <tbody>
          {allocations.map((a) => (
            <tr key={a.id} className={a.status === 2 ? 'muted' : undefined}>
              <td>{isoToRo(a.allocationDate)}</td>
              <td>{a.debitDocumentNo}<div className="muted small">{t(kindLabel(a.debitKind))}</div></td>
              <td>{a.creditDocumentNo}<div className="muted small">{t(kindLabel(a.creditKind))}</div></td>
              <td className="num">{formatAmount(a.amount)}{a.currencyCode === 'RON' ? '' : ` ${a.currencyCode}`}</td>
              <td>{t(allocationSourceLabel(a.source))}{a.status === 2 && <div className="muted small">{t('settle.undoneWith', { reason: a.undoReason ?? '' })}</div>}</td>
              <td className="row-actions">
                {canUndo && a.status === 1 && (undoing === a.id ? (
                  <span className="inline-form">
                    <input className="cell-input" value={reason} aria-label={t('settle.undoReason')} placeholder={t('settle.undoReason')}
                      onChange={(e) => setReason(e.target.value)} />
                    <button type="button" className="btn primary small" disabled={reason.trim() === ''} onClick={() => void undo(a.id)}>{t('settle.undo')}</button>
                    <button type="button" className="btn ghost small" onClick={() => setUndoing(undefined)}>{t('common.cancel')}</button>
                  </span>
                ) : <button type="button" className="btn ghost small" onClick={() => setUndoing(a.id)}>{t('settle.undo')}</button>)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}

function ProfileTab({ partnerId, user }: { readonly partnerId: number; readonly user: CurrentUser }) {
  return (
    <div className="split two-cols">
      <ProfileForm partnerId={partnerId} role={Role.Customer} user={user} />
      <ProfileForm partnerId={partnerId} role={Role.Supplier} user={user} />
    </div>
  );
}

function ProfileForm({ partnerId, role, user }: { readonly partnerId: number; readonly role: number; readonly user: CurrentUser }) {
  const profile = useFinanceProfile(partnerId, role);
  if (profile.data === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  return <ProfileEditor key={profile.data.rowVersion ?? 'new'} partnerId={partnerId} role={role} profile={profile.data} user={user} />;
}

function ProfileEditor({ partnerId, role, profile, user }: { readonly partnerId: number; readonly role: number; readonly profile: FinanceProfile;
  readonly user: CurrentUser }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [draft, setDraft] = useState(profile);
  const [error, setError] = useState<string | undefined>();
  const canEdit = user.permissions.includes('Fin.PartnerProfile.Manage');
  const canBlock = user.permissions.includes('Fin.OpenItem.BlockPayment');
  const set = (patch: Partial<FinanceProfile>) => setDraft({ ...draft, ...patch });

  const save = async () => {
    setError(undefined);
    try {
      unwrap(await api.PUT('/api/finance/partners/{partnerId}/finance-profile/{role}', { params: { path: { partnerId, role } }, body: {
        partnerId, role, autoAllocationMode: draft.autoAllocationMode, penaltyRatePerDay: draft.penaltyRatePerDay.replace(',', '.'),
        penaltyGraceDays: draft.penaltyGraceDays, penaltyCapAtPrincipal: draft.penaltyCapAtPrincipal, applyFixedRecoveryCost: draft.applyFixedRecoveryCost,
        isPaymentBlocked: draft.isPaymentBlocked, blockReason: draft.blockReason, balanceConfirmationEmail: draft.balanceConfirmationEmail,
        rowVersion: draft.rowVersion,
      } }));
      notify(t('common.saved'));
      await queryClient.invalidateQueries({ queryKey: settlementKeys.partner(partnerId) });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="settings-form">
      <h3 className="section-title">{t(role === Role.Customer ? 'settle.sheet.asCustomer' : 'settle.sheet.asSupplier')}</h3>
      <label>{t('settle.profile.mode')}
        <select className="cell-input" value={draft.autoAllocationMode} disabled={!canEdit} onChange={(e) => set({ autoAllocationMode: Number(e.target.value) })}>
          <option value={2}>{t('settle.profile.mode2')}</option>
          <option value={1}>{t('settle.profile.mode1')}</option>
          <option value={3}>{t('settle.profile.mode3')}</option>
        </select>
      </label>
      <div className="row-between">
        <label>{t('settle.profile.rate')}<input className="cell-input num" value={draft.penaltyRatePerDay} disabled={!canEdit}
          onChange={(e) => set({ penaltyRatePerDay: e.target.value })} /></label>
        <label>{t('settle.profile.grace')}<input className="cell-input num" type="number" min={0} value={draft.penaltyGraceDays} disabled={!canEdit}
          onChange={(e) => set({ penaltyGraceDays: Number(e.target.value) })} /></label>
      </div>
      <span className="muted small">{t('settle.profile.rateHint')}</span>
      <label className="check"><input type="checkbox" checked={draft.penaltyCapAtPrincipal} disabled={!canEdit}
        onChange={(e) => set({ penaltyCapAtPrincipal: e.target.checked })} />{t('settle.profile.cap')}</label>
      <label className="check"><input type="checkbox" checked={draft.applyFixedRecoveryCost} disabled={!canEdit}
        onChange={(e) => set({ applyFixedRecoveryCost: e.target.checked })} />{t('settle.profile.fixedCost')}</label>
      {role === Role.Supplier && (
        <>
          <label className="check"><input type="checkbox" checked={draft.isPaymentBlocked} disabled={!canEdit || !canBlock}
            onChange={(e) => set({ isPaymentBlocked: e.target.checked })} />{t('settle.profile.blocked')}</label>
          {draft.isPaymentBlocked && <label>{t('settle.profile.blockReason')}<input className="cell-input" value={draft.blockReason ?? ''} disabled={!canEdit || !canBlock}
            onChange={(e) => set({ blockReason: e.target.value })} /></label>}
        </>
      )}
      <label>{t('settle.profile.email')}<input className="cell-input" type="email" value={draft.balanceConfirmationEmail ?? ''} disabled={!canEdit}
        onChange={(e) => set({ balanceConfirmationEmail: e.target.value === '' ? null : e.target.value })} /></label>
      <ErrorBanner message={error} />
      {canEdit && <div className="actions"><button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button></div>}
    </div>
  );
}
