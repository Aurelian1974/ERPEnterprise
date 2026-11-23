import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useOutletContext, useSearchParams } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { SearchSelectField } from '../components/SearchSelectField';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { formatAmount } from './cashApi';
import { parseAmount } from './TreasurySettingsTab';
import { AllocationTable } from './PartnerFinancePage';
import { kindLabel, pairAllocations, proposeFifo, Role, settlementKeys, useOpenItems, usePartnerAllocations, type OpenItem } from './settlementApi';

async function searchPartners(text: string): Promise<Option[]> {
  const page = unwrap(await api.GET('/api/core/partners', { params: { query: { ...(text === '' ? {} : { Search: text }), IsActive: true, PageSize: 30 } } }));
  return page.items.map((p) => ({ value: String(p.id), text: p.fiscalCode == null ? p.name : `${p.name} · ${p.fiscalCode}` }));
}

const warnings = new Set(['FIN_OPEN_ITEM_DISPUTED_UNCONFIRMED', 'FIN_OPEN_ITEM_NOT_IN_SPV_UNCONFIRMED']);

/**
 * Decontări › Alocare (lettraj) (02.02, F3 4.3; macheta f3-03): pentru un partener și un rol, debitele (facturi) în stânga și creditele
 * (încasări, avansuri, storno) în dreapta, cu restul; se bifează partidele și sumele, „Propune FIFO” le completează, „Alocă” (F9) creează
 * alocările în perechi, în ordinea listelor. Litigiul și factura neprimită în SPV cer confirmare. Dedesubt, alocările partenerului, cu desfacerea.
 */
export function AllocationPage() {
  const user = useOutletContext<CurrentUser>();
  const [params, setParams] = useSearchParams();
  const initial = Number(params.get('partener')) || null;
  const form = useForm<{ partnerId: number | null }>({ defaultValues: { partnerId: initial } });
  const partnerId = useWatch({ control: form.control, name: 'partnerId' }) ?? undefined;
  const current = useQuery({
    queryKey: ['core', 'partners', initial],
    queryFn: async () => unwrap(await api.GET('/api/core/partners/{id}', { params: { path: { id: initial ?? 0 } } })),
    enabled: initial !== null,
  });
  const [role, setRole] = useState<number>(Number(params.get('rol')) === Role.Supplier ? Role.Supplier : Role.Customer);
  const items = useOpenItems(partnerId, true, role);
  const allocations = usePartnerAllocations(partnerId, role);

  const choose = (patch: { partener?: number; rol?: number }) => {
    const next = new URLSearchParams(params);
    if (patch.partener !== undefined) next.set('partener', String(patch.partener));
    if (patch.rol !== undefined) next.set('rol', String(patch.rol));
    setParams(next, { replace: true });
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.settlements')} › {t('menu.allocation')}</div>
      <div className="head">
        <div>
          <h1>{t('settle.alloc.title')}</h1>
          <div className="sub2">{t('settle.alloc.subtitle')}</div>
        </div>
      </div>
      <section className="card">
        <div className="toolbar-row">
          <div className="alloc-partner">
            {(initial === null || current.data !== undefined) && (
              <SearchSelectField control={form.control} name="partnerId" label={t('settle.col.partner')} search={searchPartners}
                current={current.data === undefined ? undefined : { value: String(current.data.id), text: current.data.name }} />
            )}
          </div>
          <div className="chips-row" role="group" aria-label={t('settle.col.role')}>
            {[Role.Customer, Role.Supplier].map((r) => (
              <button key={r} type="button" className={role === r ? 'chip on' : 'chip'} aria-pressed={role === r}
                onClick={() => { setRole(r); choose({ rol: r, ...(partnerId === undefined ? {} : { partener: partnerId }) }); }}>
                {t(r === Role.Customer ? 'settle.alloc.asCustomer' : 'settle.alloc.asSupplier')}
              </button>
            ))}
          </div>
        </div>
        {partnerId === undefined ? <p className="muted">{t('settle.alloc.pick')}</p>
          : items.isError ? <ErrorBanner message={items.error.message} />
            : items.data === undefined ? <p className="muted" role="status">{t('app.loading')}</p>
              : <Workbench key={`${partnerId}-${role}-${items.dataUpdatedAt}`} partnerId={partnerId} role={role} items={items.data}
                canAllocate={user.permissions.includes('Fin.Allocation.Manage')} />}
      </section>
      {partnerId !== undefined && allocations.data !== undefined && allocations.data.length > 0 && (
        <section className="card">
          <h2>{t('settle.alloc.history')}</h2>
          <AllocationTable allocations={allocations.data} partnerId={partnerId} canUndo={user.permissions.includes('Fin.Allocation.Undo')} />
        </section>
      )}
    </div>
  );
}

function Workbench({ partnerId, role, items, canAllocate }: { readonly partnerId: number; readonly role: number; readonly items: readonly OpenItem[];
  readonly canAllocate: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const debits = items.filter((i) => Number(i.openAmount) > 0);
  const credits = items.filter((i) => Number(i.openAmount) < 0);
  const [picked, setPicked] = useState<ReadonlyMap<number, string>>(new Map());
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);

  const amountOf = (id: number) => Number(parseAmount(picked.get(id) ?? '') ?? 0);
  const sum = (list: readonly OpenItem[]) => list.reduce((total, i) => total + (picked.has(i.openItemId) ? amountOf(i.openItemId) : 0), 0);
  const debitTotal = sum(debits);
  const creditTotal = sum(credits);
  const openTotal = (list: readonly OpenItem[]) => list.reduce((total, i) => total + Math.abs(Number(i.openAmount)), 0);

  const toggle = (item: OpenItem) => {
    const next = new Map(picked);
    if (next.has(item.openItemId)) next.delete(item.openItemId);
    else next.set(item.openItemId, Math.abs(Number(item.openAmount)).toFixed(2).replace('.', ','));
    setPicked(next);
  };
  const setAmount = (id: number, value: string) => setPicked(new Map(picked).set(id, value));

  const fifo = () => {
    const proposal = proposeFifo(items);
    const next = new Map<number, string>();
    for (const [id, value] of [...proposal.debits, ...proposal.credits]) next.set(id, value.toFixed(2).replace('.', ','));
    setPicked(next);
  };

  const allocate = async (confirm = false) => {
    setError(undefined);
    const take = (list: readonly OpenItem[]) => list.filter((i) => picked.has(i.openItemId)).map((i) => ({ id: i.openItemId, amount: amountOf(i.openItemId) }));
    const pairs = pairAllocations(take(debits), take(credits));
    if (pairs.length === 0) {
      setError(t('settle.alloc.nothing'));
      return;
    }
    setBusy(true);
    try {
      unwrap(await api.POST('/api/finance/allocations', { body: { partnerId, role, confirmWarnings: confirm,
        pairs: pairs.map((p) => ({ debitOpenItemId: p.debitId, creditOpenItemId: p.creditId, amount: p.amount.toFixed(2) })) } }));
      notify(t('settle.alloc.done', { count: pairs.length }));
      setPicked(new Map());
      await queryClient.invalidateQueries({ queryKey: settlementKeys.all });
    } catch (e) {
      if (!confirm && e instanceof ApiError && e.code !== undefined && warnings.has(e.code) && window.confirm(`${e.message}\n\n${t('cash.confirmWarning')}`)) {
        await allocate(true);
        return;
      }
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    } finally {
      setBusy(false);
    }
  };

  const difference = Math.round((debitTotal - creditTotal) * 100) / 100;
  return (
    <div onKeyDown={(e) => { if (e.key === 'F9' && canAllocate) { e.preventDefault(); void allocate(); } }}>
      <div className="alloc-grid">
        <Side title={t(role === Role.Customer ? 'settle.alloc.debitCustomer' : 'settle.alloc.debitSupplier')} items={debits} picked={picked} onToggle={toggle}
          onAmount={setAmount} total={debitTotal} open={openTotal(debits)} disabled={!canAllocate} />
        <Side title={t(role === Role.Customer ? 'settle.alloc.creditCustomer' : 'settle.alloc.creditSupplier')} items={credits} picked={picked} onToggle={toggle}
          onAmount={setAmount} total={creditTotal} open={openTotal(credits)} disabled={!canAllocate} />
      </div>
      {picked.size > 0 && difference !== 0 && (
        <div className="banner warn">{t('settle.alloc.difference', { amount: formatAmount(Math.abs(difference).toFixed(2)),
          side: t(difference > 0 ? 'settle.col.debit' : 'settle.col.credit') })}</div>
      )}
      <ErrorBanner message={error} />
      {canAllocate && (
        <div className="actions">
          <button type="button" className="btn ghost" onClick={fifo}>{t('settle.alloc.fifo')}</button>
          <button type="button" className="btn ghost" disabled={picked.size === 0} onClick={() => setPicked(new Map())}>{t('settle.alloc.clear')}</button>
          <button type="button" className="btn primary" disabled={busy || picked.size === 0} onClick={() => void allocate()}>{t('settle.alloc.allocate')}</button>
        </div>
      )}
    </div>
  );
}

function Side({ title, items, picked, onToggle, onAmount, total, open, disabled }: { readonly title: string; readonly items: readonly OpenItem[];
  readonly picked: ReadonlyMap<number, string>; readonly onToggle: (item: OpenItem) => void; readonly onAmount: (id: number, value: string) => void;
  readonly total: number; readonly open: number; readonly disabled: boolean }) {
  return (
    <div className="alloc-side">
      <h3 className="section-title">{title} <span className="muted small">{t('settle.alloc.openCount', { count: items.length })}</span></h3>
      {items.length === 0 ? <p className="muted">{t('settle.alloc.none')}</p> : (
        <table className="partner-list cash-list">
          <thead>
            <tr>
              <th scope="col"><span className="sr-only">{t('settle.alloc.pickColumn')}</span></th>
              <th scope="col">{t('settle.col.document')}</th>
              <th scope="col">{t('settle.col.due')}</th>
              <th scope="col" className="num">{t('settle.col.open')}</th>
              <th scope="col" className="num">{t('settle.alloc.amount')}</th>
            </tr>
          </thead>
          <tbody>
            {items.map((i) => {
              const on = picked.has(i.openItemId);
              return (
                <tr key={i.openItemId} className={on ? 'picked' : undefined}>
                  <td><input type="checkbox" checked={on} disabled={disabled} aria-label={t('settle.alloc.pickItem', { document: i.documentNo })}
                    onChange={() => onToggle(i)} /></td>
                  <td><div className="strong">{i.documentNo}</div>
                    <div className="muted small">{t(kindLabel(i.kind))}{i.isDisputed ? ` · ${t('settle.disputed')}` : ''}</div></td>
                  <td>{isoToRo(i.kind === 5 || i.kind === 10 ? i.documentDate : i.dueDate)}</td>
                  <td className="num">{formatAmount(Math.abs(Number(i.openAmount)).toFixed(2))}{i.currencyCode === 'RON' ? '' : ` ${i.currencyCode}`}</td>
                  <td className="num">{on && <input className="cell-input num alloc-amount" value={picked.get(i.openItemId) ?? ''} disabled={disabled}
                    aria-label={t('settle.alloc.amountFor', { document: i.documentNo })} onChange={(e) => onAmount(i.openItemId, e.target.value)} />}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      <div className="row-between muted small">
        <span>{t('settle.alloc.selected', { count: items.filter((i) => picked.has(i.openItemId)).length, amount: formatAmount(total.toFixed(2)) })}</span>
        <span>{t('settle.alloc.openTotal', { amount: formatAmount(open.toFixed(2)) })}</span>
      </div>
    </div>
  );
}
