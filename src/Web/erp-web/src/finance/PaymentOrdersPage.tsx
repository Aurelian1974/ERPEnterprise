import { useMemo, useState } from 'react';
import { useOutletContext } from 'react-router';
import { useForm, useWatch } from 'react-hook-form';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { SearchSelectField } from '../components/SearchSelectField';
import { t } from '../i18n';
import { isoToRo, roToIso } from '../partners/partnerModel';
import { useBankAccounts, useBankReconciliation } from './bankApi';
import { formatAmount } from './cashApi';
import { BatchStatus, confirmationLabel, documentLabel, downloadFile, filterOrders, OrderStatus, orderApi, orderKeys, Origin, purposeLabel, purposes,
  usePaymentBatches, usePaymentOrder, usePaymentOrders, type OrderFilter, type PaymentOrderDetail } from './paymentOrderApi';
import { parseAmount } from './TreasurySettingsTab';

const filters: readonly { readonly value: OrderFilter; readonly label: Parameters<typeof t>[0] }[] = [
  { value: 'all', label: 'order.filter.all' },
  { value: 'drafts', label: 'order.filter.drafts' },
  { value: 'toApprove', label: 'order.filter.toApprove' },
  { value: 'unconfirmed', label: 'order.filter.unconfirmed' },
  { value: 'rejected', label: 'order.filter.rejected' },
];

async function searchPartners(text: string): Promise<Option[]> {
  const page = unwrap(await api.GET('/api/core/partners', { params: { query: { ...(text === '' ? {} : { Search: text }), IsActive: true, PageSize: 30 } } }));
  return page.items.map((p) => ({ value: String(p.id), text: p.fiscalCode == null ? p.name : `${p.name} · ${p.fiscalCode}` }));
}

/**
 * Bancă › Ordine de plată (02.02, F2 3.6 și 4.2; macheta f2-03): lista OP-urilor contului cu filtrele Ciorne / De aprobat / Neconfirmate /
 * Respinse, fișa OP-ului (beneficiar, IBAN din fișă, linii, aprobare, lot) cu acțiunile Validează, Aprobă (patru ochi), Adaugă în lot /
 * Scoate din lot, Stornează; OP nou și exportul lotului deschis al contului (pain.001 sau CSV, după profilul băncii).
 */
export function PaymentOrdersPage() {
  const user = useOutletContext<CurrentUser>();
  const accounts = useBankAccounts();
  const bankAccounts = useMemo(() => (accounts.data ?? []).filter((a) => a.kind === 1), [accounts.data]);
  const [chosen, setChosen] = useState<number | undefined>();
  const account = chosen ?? bankAccounts[0]?.id;
  const list = usePaymentOrders(account);
  const batches = usePaymentBatches(account);
  const [filter, setFilter] = useState<OrderFilter>('all');
  const [selected, setSelected] = useState<number | 'new' | undefined>();
  const orders = filterOrders(list.data ?? [], filter);
  const current = selected ?? orders[0]?.id;
  const openBatch = (batches.data ?? []).find((b) => b.status === BatchStatus.Open);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const can = (permission: string) => user.permissions.includes(permission);
  const unconfirmedTotal = (list.data ?? []).filter((o) => o.status === OrderStatus.Validated && o.confirmationStatus === 1)
    .reduce((sum, o) => sum + Math.round(Number(o.amount) * 100), 0);

  const exportBatch = async () => {
    if (openBatch === undefined) return;
    try {
      downloadFile(await orderApi.exportBatch(openBatch.id, openBatch.rowVersion, null));
      notify(t('order.exported', { batch: openBatch.number ?? String(openBatch.id) }));
      await queryClient.invalidateQueries({ queryKey: orderKeys.all });
    } catch (e) {
      notify(e instanceof ApiError ? e.message : t('common.notSaved'), 'error');
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.bank')} › {t('menu.paymentOrders')}</div>
      <div className="head">
        <div>
          <h1>{t('order.title')}</h1>
          <div className="sub2">{t('order.subtitle')}</div>
        </div>
        <div className="actions">
          {openBatch !== undefined && openBatch.orderCount > 0 && can('Fin.PaymentOrder.Export') && (
            <button type="button" className="btn ghost" onClick={() => void exportBatch()}>{t('order.exportBatch', { batch: openBatch.number ?? '' })}</button>
          )}
          {can('Fin.PaymentOrder.Create') && <button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('order.new')}</button>}
        </div>
      </div>
      <div className="split cash-document bank-split">
        <section className="card" aria-label={t('order.title')}>
          <div className="chips-row" role="group" aria-label={t('order.filter.label')}>
            <select className="cell-input" value={account ?? ''} aria-label={t('bank.account')} onChange={(e) => { setChosen(Number(e.target.value)); setSelected(undefined); }}>
              {bankAccounts.map((a) => <option key={a.id} value={a.id}>{a.code}</option>)}
            </select>
            {filters.map((f) => (
              <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                onClick={() => setFilter(f.value)}>{t(f.label)}</button>
            ))}
          </div>
          {list.isError && <ErrorBanner message={list.error.message} />}
          {list.data !== undefined && orders.length === 0 ? <p className="muted">{t('order.empty')}</p> : (
            <table className="partner-list cash-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('order.col.order')}</th>
                  <th scope="col" className="num">{t('bank.col.amount')}</th>
                  <th scope="col">{t('order.col.document')}</th>
                  <th scope="col">{t('order.col.exportStatement')}</th>
                </tr>
              </thead>
              <tbody>
                {orders.map((o) => {
                  const doc = documentLabel(o);
                  const confirmation = confirmationLabel(o.confirmationStatus);
                  return (
                    <tr key={o.id} className={o.id === current ? 'selected' : undefined} onClick={() => setSelected(o.id)} aria-selected={o.id === current}>
                      <td><strong>{o.number ?? t('order.noNumber')}</strong><div className="muted small">{o.beneficiaryName} · {isoToRo(o.documentDate)}</div></td>
                      <td className="num"><strong>{formatAmount(o.amount)}</strong></td>
                      <td><span className={doc.pill}>{t(doc.label)}</span></td>
                      <td>
                        {o.status === OrderStatus.Validated && o.reversalOfId == null && o.origin !== Origin.BankStatement && (
                          <div><span className={o.paymentBatchId != null ? 'pill ok' : o.isApproved ? 'pill info' : 'pill warn'}>
                            {t(o.paymentBatchId != null ? 'order.inBatch' : o.isApproved ? 'order.approved' : 'order.toApprove')}</span></div>
                        )}
                        {confirmation !== undefined && <span className={confirmation.pill}>{t(confirmation.label)}</span>}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
          <div className="muted small footer-hint">{t('order.unconfirmedTotal', {
            total: formatAmount((unconfirmedTotal / 100).toFixed(2)) })}</div>
        </section>
        <aside className="card cash-side">
          {selected === 'new' && account !== undefined
            ? <NewOrder accountId={account} onCreated={(id) => setSelected(id)} onCancel={() => setSelected(undefined)} />
            : typeof current === 'number' ? <OrderPanel key={current} id={current} accountId={account ?? 0} openBatch={openBatch} can={can} />
              : <p className="muted">{t('order.pick')}</p>}
        </aside>
      </div>
    </div>
  );
}

function OrderPanel({ id, accountId, openBatch, can }: { readonly id: number; readonly accountId: number;
  readonly openBatch: { readonly id: number; readonly rowVersion: string } | undefined; readonly can: (permission: string) => boolean }) {
  const query = usePaymentOrder(id);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const order = query.data;
  if (order === undefined) {
    return <p role="status" className="muted">{query.isError ? query.error.message : t('app.loading')}</p>;
  }

  const run = async (action: () => Promise<unknown>, done: Parameters<typeof t>[0]) => {
    try {
      await action();
      notify(t(done));
      await queryClient.invalidateQueries({ queryKey: orderKeys.all });
    } catch (e) {
      notify(e instanceof ApiError ? e.message : t('common.notSaved'), 'error');
    }
  };
  const validate = () => run(async () => {
    try {
      await orderApi.validate(order.id, order.rowVersion, false);
    } catch (e) {
      if (e instanceof ApiError && e.code === 'FIN_PAYMENT_ORDER_ANAF_INACTIVE_UNCONFIRMED' && window.confirm(`${e.message}\n\n${t('order.confirmAnaf')}`)) {
        await orderApi.validate(order.id, order.rowVersion, true);
        return;
      }
      throw e;
    }
  }, 'order.validated');
  const toggleBatch = () => run(async () => {
    const current = openBatch === undefined ? [] : await orderApi.batchOrders(openBatch.id);
    const next = order.paymentBatchId == null ? [...current, order.id] : current.filter((o) => o !== order.id);
    await orderApi.setBatch(openBatch, accountId, next);
  }, order.paymentBatchId == null ? 'order.addedToBatch' : 'order.removedFromBatch');

  const doc = documentLabel(order);
  const confirmation = confirmationLabel(order.confirmationStatus);
  const validated = order.status === OrderStatus.Validated && order.reversalOfId == null;
  const unconfirmed = order.confirmationStatus === 1;
  return (
    <div className="bank-line">
      <h2>{order.number ?? t('order.noNumber')} · {order.beneficiaryName} <span className={doc.pill}>{t(doc.label)}</span>
        {confirmation !== undefined && <> <span className={confirmation.pill}>{t(confirmation.label)}</span></>}</h2>
      <p className="muted small">{t('order.meta', { account: order.accountCode, date: isoToRo(order.documentDate), execution: isoToRo(order.requestedExecutionDate) })}</p>
      <div className="chips-row">
        <span className="chip">{`${formatAmount(order.amount)} ${order.currencyCode}`}</span>
        <span className="chip">{t(purposeLabel(order.purpose))}</span>
        {order.isUrgent && <span className="chip">{t('order.urgent')}</span>}
      </div>
      <dl className="kv order-kv">
        <dt>{t('order.beneficiary')}</dt><dd>{order.partnerName ?? order.beneficiaryName}{order.partnerAnafInactive && <> <span className="pill bad">{t('order.anafInactive')}</span></>}</dd>
        <dt>{t('order.iban')}</dt><dd className="mono">{order.beneficiaryIban} {order.isFreeIban ? <span className="pill warn">{t('order.freeIban')}</span>
          : <span className="pill ok">{t('order.fromFile')}</span>}</dd>
        <dt>{t('order.remittance')}</dt><dd className="mono">{order.remittanceInfo ?? '—'}</dd>
        {order.endToEndId != null && <><dt>EndToEndId</dt><dd className="mono">{order.endToEndId}</dd></>}
      </dl>
      <table className="partner-list">
        <thead><tr><th scope="col">{t('bank.col.type')}</th><th scope="col">{t('order.col.invoice')}</th><th scope="col" className="num">{t('bank.col.amount')}</th></tr></thead>
        <tbody>
          {order.lines.map((l) => (
            <tr key={l.id}><td>{l.operationTypeName}</td><td>{l.settlementRef ?? '—'}</td><td className="num">{formatAmount(l.amount)}</td></tr>
          ))}
        </tbody>
      </table>
      <StatusBoxes order={order} />
      <div className="actions">
        {order.status === OrderStatus.Draft && can('Fin.PaymentOrder.Create') && (
          <button type="button" className="btn ghost" onClick={() => { if (window.confirm(t('order.confirmDelete'))) void run(() => orderApi.remove(order.id, order.rowVersion), 'order.deleted'); }}>
            {t('order.delete')}</button>
        )}
        {validated && unconfirmed && can('Fin.PaymentOrder.Reverse') && (
          <button type="button" className="btn ghost" onClick={() => { if (window.confirm(t('order.confirmReverse'))) void run(() => orderApi.reverse(order.id, order.rowVersion), 'order.reversed'); }}>
            {t('order.reverse')}</button>
        )}
        {order.confirmationStatus === 3 && can('Fin.PaymentOrder.Reverse') && (
          <button type="button" className="btn ghost" onClick={() => void run(() => orderApi.reverse(order.id, order.rowVersion), 'order.reversed')}>{t('order.reverse')}</button>
        )}
        {validated && unconfirmed && order.origin !== Origin.BankStatement && can('Fin.PaymentOrder.Export') && (
          <button type="button" className="btn ghost" onClick={() => void toggleBatch()}>{t(order.paymentBatchId == null ? 'order.addToBatch' : 'order.removeFromBatch')}</button>
        )}
        {order.status === OrderStatus.Draft && can('Fin.PaymentOrder.Validate') && (
          <button type="button" className="btn primary" onClick={() => void validate()}>{t('order.validate')}</button>
        )}
        {validated && unconfirmed && order.approvedAt == null && order.origin !== Origin.BankStatement && can('Fin.PaymentOrder.Approve') && (
          <button type="button" className="btn primary" onClick={() => void run(() => orderApi.approve(order.id, order.rowVersion), 'order.approvedDone')}>{t('order.approve')}</button>
        )}
      </div>
    </div>
  );
}

function StatusBoxes({ order }: { readonly order: PaymentOrderDetail }) {
  if (order.status === OrderStatus.Draft) {
    return <div className="banner dashed">{t('order.draftHint')}</div>;
  }
  return (
    <div className="status-boxes">
      <div className="banner info">{t('order.validatedBy', { user: order.validatedByName ?? '—', at: order.validatedAt == null ? '' : new Date(order.validatedAt).toLocaleString('ro-RO') })}</div>
      {order.origin === Origin.BankStatement ? <div className="banner info">{t('order.fromStatement')}</div>
        : order.approvedAt == null ? <div className="banner warn">{t('order.fourEyes')}</div>
          : <div className="banner info">{t('order.approvedBy', { user: order.approvedByName ?? '—' })}</div>}
    </div>
  );
}

interface NewOrderForm {
  partnerId: number | null;
  partnerBankAccountId: number | null;
  purpose: number;
  documentDate: string;
  requestedExecutionDate: string;
  isUrgent: boolean;
  operationTypeId: number;
  amount: string;
  settlementRef: string;
  budgetObligationRef: string;
  beneficiaryFiscalCode: string;
}

function NewOrder({ accountId, onCreated, onCancel }: { readonly accountId: number; readonly onCreated: (id: number) => void; readonly onCancel: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const lookups = useBankReconciliation(accountId);
  const types = (lookups.data?.operationTypes ?? []).filter((ty) => ty.direction === 2 || ty.direction === 3);
  const today = isoToRo(new Date().toISOString().slice(0, 10));
  const form = useForm<NewOrderForm>({ defaultValues: { partnerId: null, partnerBankAccountId: null, purpose: 1, documentDate: today, requestedExecutionDate: today,
    isUrgent: false, operationTypeId: 0, amount: '', settlementRef: '', budgetObligationRef: '', beneficiaryFiscalCode: '' } });
  const partnerId = useWatch({ control: form.control, name: 'partnerId' });
  const partner = useQuery({
    queryKey: ['core', 'partners', 'detail', partnerId ?? 0],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/partners/{id}', { params: { path: { id: partnerId ?? 0 } }, signal })),
    enabled: partnerId !== null,
  });
  const [error, setError] = useState<string | undefined>();
  const values = useWatch({ control: form.control });
  const bankAccounts = (partner.data?.bankAccounts ?? []).filter((b) => b.isActive);
  const firstType = types.find((ty) => ty.code === 'PLATA-FURNIZOR') ?? types[0];

  const submit = form.handleSubmit(async (v) => {
    setError(undefined);
    const amount = parseAmount(v.amount);
    const documentDate = roToIso(v.documentDate);
    const execution = roToIso(v.requestedExecutionDate);
    const typeId = v.operationTypeId === 0 ? firstType?.id : v.operationTypeId;
    if (amount === null || documentDate === undefined || execution === undefined || typeId === undefined) {
      setError(t('order.invalidForm'));
      return;
    }
    try {
      const saved = await orderApi.save({
        id: null, treasuryAccountId: accountId, documentDate, requestedExecutionDate: execution, isUrgent: v.isUrgent, purpose: v.purpose,
        partnerId: v.partnerId, employeeId: null, partnerBankAccountId: v.partnerBankAccountId ?? bankAccounts.find((b) => b.isDefault)?.id ?? bankAccounts[0]?.id ?? null,
        beneficiaryName: null, beneficiaryIban: null, beneficiaryBic: null, beneficiaryFiscalCode: v.beneficiaryFiscalCode.trim() === '' ? null : v.beneficiaryFiscalCode.trim(),
        remittanceInfo: null, budgetObligationRef: v.budgetObligationRef.trim() === '' ? null : v.budgetObligationRef.trim(),
        lines: [{ operationTypeId: typeId, amount, settlementRef: v.settlementRef.trim() === '' ? null : v.settlementRef.trim(), costCenterId: null, projectId: null }],
        rowVersion: null,
      });
      notify(t('order.saved'));
      await queryClient.invalidateQueries({ queryKey: orderKeys.all });
      onCreated(saved.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  });

  return (
    <form className="bank-line settings-form" onSubmit={(e) => void submit(e)}>
      <h2>{t('order.newTitle')}</h2>
      <SearchSelectField control={form.control} name="partnerId" label={t('order.beneficiary')} current={undefined} search={searchPartners} />
      <label>{t('order.iban')}
        <select className="cell-input" value={values.partnerBankAccountId ?? ''} disabled={bankAccounts.length === 0}
          onChange={(e) => form.setValue('partnerBankAccountId', e.target.value === '' ? null : Number(e.target.value))}>
          {bankAccounts.length === 0 && <option value="">{t('order.noPartnerAccounts')}</option>}
          {bankAccounts.map((b) => <option key={b.id} value={b.id}>{`${b.iban} · ${b.bankName}${b.isDefault ? ' ★' : ''}`}</option>)}
        </select>
      </label>
      <label>{t('order.purpose')}
        <select className="cell-input" {...form.register('purpose', { valueAsNumber: true })}>
          {purposes.map((p) => <option key={p} value={p}>{t(purposeLabel(p))}</option>)}
        </select>
      </label>
      <label>{t('bank.col.type')}
        <select className="cell-input" value={values.operationTypeId === 0 ? firstType?.id ?? '' : values.operationTypeId}
          onChange={(e) => form.setValue('operationTypeId', Number(e.target.value))}>
          {types.map((ty) => <option key={ty.id} value={ty.id}>{ty.name}</option>)}
        </select>
      </label>
      <label>{t('order.col.invoice')}<input className="cell-input" {...form.register('settlementRef')} placeholder="FA-123" /></label>
      <label>{t('bank.col.amount')}<input className="cell-input num" {...form.register('amount')} /></label>
      {values.purpose === 3 && (
        <>
          <label>{t('order.budgetRef')}<input className="cell-input" {...form.register('budgetObligationRef')} /></label>
          <label>{t('order.payerFiscalCode')}<input className="cell-input" {...form.register('beneficiaryFiscalCode')} /></label>
        </>
      )}
      <label>{t('order.date')}<input className="cell-input" {...form.register('documentDate')} /></label>
      <label>{t('order.execution')}<input className="cell-input" {...form.register('requestedExecutionDate')} /></label>
      <label className="check"><input type="checkbox" {...form.register('isUrgent')} />{t('order.urgent')}</label>
      <ErrorBanner message={error} />
      <div className="actions">
        <button type="button" className="btn ghost" onClick={onCancel}>{t('order.cancel')}</button>
        <button type="submit" className="btn primary">{t('order.saveDraft')}</button>
      </div>
    </form>
  );
}
