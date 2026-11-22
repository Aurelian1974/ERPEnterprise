import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo, roToIso } from '../partners/partnerModel';
import { formatAmount } from './cashApi';

type OperationType = components['schemas']['OperationTypeDto'];
type ExpenseType = components['schemas']['ExpenseTypeDto'];
type CashLimit = components['schemas']['CashLimitRuleDto'];
type Section = 'operations' | 'expenses' | 'limits';

const keys = {
  operations: ['finance', 'settings', 'operations'] as const,
  expenses: ['finance', 'settings', 'expenses'] as const,
  limits: ['finance', 'settings', 'limits'] as const,
};

export const limitCategories = ['IncasarePJ', 'IncasarePF', 'PlataPJ', 'PlataPF', 'AvansPersoanaZi', 'SoldCasa'] as const;

/** Suma introdusa romaneste (1.500,50 sau 1500.50) -> sir invariant pentru API; null daca nu e o suma valida. */
export function parseAmount(text: string): string | null {
  const clean = text.trim().replace(/\./g, '').replace(',', '.');
  return /^\d{1,15}(\.\d{1,2})?$/.test(clean) ? clean : null;
}

/**
 * Administrare › Configurare › Finanțe › Trezorerie (02.02, F1 3.2, 3.6, 3.7; machetele cf-01…cf-03): tipurile de operațiuni comune casei
 * și băncii, tipurile de cheltuieli și diurna (cu versiuni după data de vigoare) și plafoanele de numerar (cele legale doar citire).
 */
export function TreasurySettingsTab({ user }: { readonly user: CurrentUser }) {
  const canConfigure = user.permissions.includes('Fin.Treasury.Configure');
  const canLimits = user.permissions.includes('Fin.CashLimit.Manage');
  const [section, setSection] = useState<Section>(canConfigure ? 'operations' : 'limits');
  return (
    <section className="card" aria-label={t('config.tab.treasury')}>
      <div className="chips-row" role="tablist" aria-label={t('config.tab.treasury')}>
        {canConfigure && <SectionButton value="operations" current={section} onSelect={setSection} label={t('settings.operations')} />}
        {canConfigure && <SectionButton value="expenses" current={section} onSelect={setSection} label={t('settings.expenses')} />}
        {canLimits && <SectionButton value="limits" current={section} onSelect={setSection} label={t('settings.limits')} />}
      </div>
      {section === 'operations' && canConfigure && <OperationTypes />}
      {section === 'expenses' && canConfigure && <ExpenseTypes />}
      {section === 'limits' && canLimits && <CashLimits />}
    </section>
  );
}

function SectionButton({ value, current, onSelect, label }: { readonly value: Section; readonly current: Section; readonly onSelect: (s: Section) => void;
  readonly label: string }) {
  return (
    <button type="button" role="tab" aria-selected={value === current} className={value === current ? 'chip on' : 'chip'} onClick={() => onSelect(value)}>
      {label}
    </button>
  );
}

function useSave() {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  return async (action: () => Promise<unknown>, key: readonly string[]) => {
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: key });
      notify(t('common.saved'));
      return true;
    } catch (e) {
      notify(e instanceof ApiError ? e.message : t('common.notSaved'), 'error');
      return false;
    }
  };
}

// ---------- Tipuri de operatiuni (cf-01) ----------

interface OperationDraft {
  readonly id: number | null;
  readonly code: string;
  readonly name: string;
  readonly direction: number;
  readonly requires: 'none' | 'partner' | 'employee' | 'counter';
  readonly settlementKind: number;
  readonly counterGlAccountCode: string;
  readonly cashLimitCategory: string;
  readonly appliesToCash: boolean;
  readonly appliesToBank: boolean;
  readonly isActive: boolean;
  readonly isSystem: boolean;
  readonly rowVersion: string | null;
}

const newOperation: OperationDraft = { id: null, code: '', name: '', direction: 1, requires: 'none', settlementKind: 0, counterGlAccountCode: '',
  cashLimitCategory: '', appliesToCash: true, appliesToBank: false, isActive: true, isSystem: false, rowVersion: null };

function toDraft(o: OperationType): OperationDraft {
  return { id: o.id, code: o.code, name: o.name, direction: o.direction,
    requires: o.requiresPartner ? 'partner' : o.requiresEmployee ? 'employee' : o.requiresCounterAccount ? 'counter' : 'none',
    settlementKind: o.settlementKind, counterGlAccountCode: o.counterGlAccountCode ?? '', cashLimitCategory: o.cashLimitCategory ?? '',
    appliesToCash: o.appliesToCash, appliesToBank: o.appliesToBank, isActive: o.isActive, isSystem: o.isSystem, rowVersion: o.rowVersion };
}

function OperationTypes() {
  const list = useQuery({ queryKey: keys.operations, queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/operation-types', { signal })) });
  const [draft, setDraft] = useState<OperationDraft | undefined>();
  const save = useSave();
  const submit = async (d: OperationDraft) => {
    const body = { id: d.id, code: d.code, name: d.name, direction: d.direction, requiresPartner: d.requires === 'partner',
      requiresEmployee: d.requires === 'employee', requiresCounterAccount: d.requires === 'counter', settlementKind: d.settlementKind,
      counterGlAccountCode: d.counterGlAccountCode === '' ? null : d.counterGlAccountCode, cashLimitCategory: d.cashLimitCategory === '' ? null : d.cashLimitCategory,
      appliesToCash: d.appliesToCash, appliesToBank: d.appliesToBank, isActive: d.isActive, rowVersion: d.rowVersion };
    const ok = await save(async () => d.id === null
      ? unwrap(await api.POST('/api/finance/operation-types', { body }))
      : unwrap(await api.PUT('/api/finance/operation-types/{id}', { params: { path: { id: d.id } }, body })), keys.operations);
    if (ok) setDraft(undefined);
  };
  if (list.data === undefined) return <p role="status">{list.isError ? list.error.message : t('app.loading')}</p>;
  return (
    <>
      <div className="banner dashed">{t('settings.operations.hint')}</div>
      <table className="partner-list cash-list">
        <thead>
          <tr>
            <th scope="col">{t('treasury.field.code')}</th>
            <th scope="col">{t('treasury.field.name')}</th>
            <th scope="col">{t('settings.direction')}</th>
            <th scope="col">{t('settings.where')}</th>
            <th scope="col">{t('settings.limit')}</th>
            <th scope="col">{t('settings.account')}</th>
            <th scope="col">{t('cash.col.status')}</th>
          </tr>
        </thead>
        <tbody>
          {list.data.map((o) => (
            <tr key={o.id} className={o.isActive ? '' : 'inactive'} onClick={() => setDraft(toDraft(o))}>
              <td><button type="button" className="row-link mono">{o.code}</button></td>
              <td>{o.name}</td>
              <td className="small">{t(o.direction === 1 ? 'settings.in' : o.direction === 2 ? 'settings.out' : 'settings.both')}</td>
              <td className="small">{[o.appliesToCash ? t('settings.cash') : '', o.appliesToBank ? t('settings.bank') : ''].filter(Boolean).join(' · ')}</td>
              <td className="small">{o.cashLimitCategory ?? t('common.none')}</td>
              <td className="small">{o.counterGlAccountCode ?? t('common.none')}</td>
              <td className="small">{o.isSystem ? t('settings.shipped') : t('settings.client')}{o.isActive ? '' : ` · ${t('common.inactive')}`}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {draft === undefined ? (
        <div className="actions"><button type="button" className="btn ghost" onClick={() => setDraft(newOperation)}>{t('settings.newRow')}</button></div>
      ) : (
        <div className="settings-editor" role="group" aria-label={t('settings.edit')}>
          <label>{t('treasury.field.code')}<input className="cell-input" value={draft.code} disabled={draft.isSystem}
            onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase() })} /></label>
          <label>{t('treasury.field.name')}<input className="cell-input" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /></label>
          <label>{t('settings.direction')}<select className="cell-input" value={draft.direction} disabled={draft.isSystem}
            onChange={(e) => setDraft({ ...draft, direction: Number(e.target.value), cashLimitCategory: '' })}>
            <option value={1}>{t('settings.in')}</option><option value={2}>{t('settings.out')}</option></select></label>
          <label>{t('settings.requires')}<select className="cell-input" value={draft.requires} disabled={draft.isSystem}
            onChange={(e) => setDraft({ ...draft, requires: e.target.value as OperationDraft['requires'] })}>
            <option value="none">{t('common.none')}</option><option value="partner">{t('cash.payer.partner')}</option>
            <option value="employee">{t('cash.payer.employee')}</option><option value="counter">{t('settings.counter')}</option></select></label>
          <label>{t('settings.limit')}<select className="cell-input" value={draft.cashLimitCategory}
            onChange={(e) => setDraft({ ...draft, cashLimitCategory: e.target.value })}>
            <option value="">{t('common.none')}</option>
            {(draft.direction === 1 ? ['Incasare'] : ['Plata', 'AvansPersoanaZi']).map((c) => <option key={c} value={c}>{c}</option>)}</select></label>
          <label>{t('settings.account')}<input className="cell-input" value={draft.counterGlAccountCode}
            onChange={(e) => setDraft({ ...draft, counterGlAccountCode: e.target.value })} /></label>
          <label className="check"><input type="checkbox" checked={draft.appliesToCash} onChange={(e) => setDraft({ ...draft, appliesToCash: e.target.checked })} />
            {t('settings.cash')}</label>
          <label className="check"><input type="checkbox" checked={draft.appliesToBank} onChange={(e) => setDraft({ ...draft, appliesToBank: e.target.checked })} />
            {t('settings.bank')}</label>
          <label className="check"><input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />
            {t('field.isActive')}</label>
          <div className="actions">
            <button type="button" className="btn ghost" onClick={() => setDraft(undefined)}>{t('common.cancel')}</button>
            <button type="button" className="btn primary" onClick={() => void submit(draft)}>{t('common.save')}</button>
          </div>
        </div>
      )}
    </>
  );
}

// ---------- Cheltuieli si diurna (cf-02) ----------

function ExpenseTypes() {
  const list = useQuery({ queryKey: keys.expenses, queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/expense-types', { signal })) });
  const [draft, setDraft] = useState<(ExpenseType & { readonly fromText: string; readonly daily: string; readonly limit: string }) | undefined>();
  const save = useSave();
  const startNew = (base?: ExpenseType) => setDraft({
    id: 0, code: base?.code ?? '', name: base?.name ?? '', isPerDiem: base?.isPerDiem ?? false, dailyAmount: null, nonTaxableDailyLimit: null,
    vatDeductibleOnReceipt: base?.vatDeductibleOnReceipt ?? false, validFrom: '', validTo: null, counterGlAccountCode: base?.counterGlAccountCode ?? null,
    isSystem: false, isActive: true, rowVersion: '', fromText: '', daily: base?.dailyAmount?.replace('.', ',') ?? '', limit: base?.nonTaxableDailyLimit?.replace('.', ',') ?? '',
  });
  const submit = async () => {
    if (draft === undefined) return;
    const isNew = draft.id === 0;
    const body = { id: isNew ? null : draft.id, code: draft.code, name: draft.name, isPerDiem: draft.isPerDiem,
      dailyAmount: draft.isPerDiem ? parseAmount(draft.daily) : null, nonTaxableDailyLimit: draft.isPerDiem ? parseAmount(draft.limit) : null,
      vatDeductibleOnReceipt: draft.vatDeductibleOnReceipt, validFrom: isNew ? roToIso(draft.fromText) ?? '' : draft.validFrom, validTo: draft.validTo ?? null,
      counterGlAccountCode: draft.counterGlAccountCode === '' ? null : draft.counterGlAccountCode, isActive: draft.isActive, rowVersion: isNew ? null : draft.rowVersion };
    const ok = await save(async () => isNew
      ? unwrap(await api.POST('/api/finance/expense-types', { body }))
      : unwrap(await api.PUT('/api/finance/expense-types/{id}', { params: { path: { id: draft.id } }, body })), keys.expenses);
    if (ok) setDraft(undefined);
  };
  if (list.data === undefined) return <p role="status">{list.isError ? list.error.message : t('app.loading')}</p>;
  return (
    <>
      <div className="banner dashed">{t('settings.expenses.hint')}</div>
      <table className="partner-list cash-list">
        <thead>
          <tr>
            <th scope="col">{t('treasury.field.code')}</th>
            <th scope="col">{t('treasury.field.name')}</th>
            <th scope="col" className="num">{t('settings.daily')}</th>
            <th scope="col" className="num">{t('settings.nonTaxable')}</th>
            <th scope="col">{t('settings.validFrom')}</th>
            <th scope="col">{t('settings.account')}</th>
            <th scope="col" />
          </tr>
        </thead>
        <tbody>
          {list.data.map((e) => (
            <tr key={e.id} className={e.isActive ? '' : 'inactive'}>
              <td className="mono">{e.code}</td>
              <td>{e.name}</td>
              <td className="num">{formatAmount(e.dailyAmount)}</td>
              <td className="num">{formatAmount(e.nonTaxableDailyLimit)}</td>
              <td className="small">{isoToRo(e.validFrom)}{e.validTo == null ? '' : ` – ${isoToRo(e.validTo)}`}</td>
              <td className="small">{e.counterGlAccountCode ?? t('common.none')}</td>
              <td>
                <button type="button" className="btn ghost small" onClick={() => setDraft({ ...e, fromText: isoToRo(e.validFrom),
                  daily: e.dailyAmount?.replace('.', ',') ?? '', limit: e.nonTaxableDailyLimit?.replace('.', ',') ?? '' })}>{t('settings.edit')}</button>{' '}
                <button type="button" className="btn ghost small" onClick={() => startNew(e)}>{t('settings.newVersion')}</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {draft === undefined ? (
        <div className="actions"><button type="button" className="btn ghost" onClick={() => startNew()}>{t('settings.newRow')}</button></div>
      ) : (
        <div className="settings-editor" role="group" aria-label={t('settings.edit')}>
          <label>{t('treasury.field.code')}<input className="cell-input" value={draft.code} disabled={draft.id !== 0}
            onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase() })} /></label>
          <label>{t('treasury.field.name')}<input className="cell-input" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /></label>
          <label>{t('settings.validFrom')}<input className="cell-input" value={draft.fromText} disabled={draft.id !== 0} placeholder="zz.ll.aaaa"
            onChange={(e) => setDraft({ ...draft, fromText: e.target.value })} /></label>
          <label className="check"><input type="checkbox" checked={draft.isPerDiem} disabled={draft.id !== 0}
            onChange={(e) => setDraft({ ...draft, isPerDiem: e.target.checked })} />{t('settings.perDiem')}</label>
          {draft.isPerDiem && (
            <>
              <label>{t('settings.daily')}<input className="cell-input" value={draft.daily} disabled={draft.id !== 0 && draft.isSystem}
                onChange={(e) => setDraft({ ...draft, daily: e.target.value })} /></label>
              <label>{t('settings.nonTaxable')}<input className="cell-input" value={draft.limit} disabled={draft.id !== 0 && draft.isSystem}
                onChange={(e) => setDraft({ ...draft, limit: e.target.value })} /></label>
            </>
          )}
          <label>{t('settings.account')}<input className="cell-input" value={draft.counterGlAccountCode ?? ''}
            onChange={(e) => setDraft({ ...draft, counterGlAccountCode: e.target.value })} /></label>
          <label className="check"><input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />
            {t('field.isActive')}</label>
          <div className="actions">
            <button type="button" className="btn ghost" onClick={() => setDraft(undefined)}>{t('common.cancel')}</button>
            <button type="button" className="btn primary" onClick={() => void submit()}>{t('common.save')}</button>
          </div>
        </div>
      )}
    </>
  );
}

// ---------- Plafoane de numerar (cf-03) ----------

function CashLimits() {
  const list = useQuery({ queryKey: keys.limits, queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/cash-limits', { signal })) });
  const [draft, setDraft] = useState<{ readonly id: number | null; readonly category: string; readonly amount: string; readonly action: number;
    readonly from: string; readonly to: string; readonly rowVersion: string | null } | undefined>();
  const save = useSave();
  const submit = async () => {
    if (draft === undefined) return;
    const body = { id: draft.id, category: draft.category, amount: parseAmount(draft.amount) ?? draft.amount, action: draft.action,
      validFrom: roToIso(draft.from) ?? '', validTo: draft.to === '' ? null : roToIso(draft.to) ?? null, rowVersion: draft.rowVersion };
    const ok = await save(async () => draft.id === null
      ? unwrap(await api.POST('/api/finance/cash-limits', { body }))
      : unwrap(await api.PUT('/api/finance/cash-limits/{id}', { params: { path: { id: draft.id } }, body })), keys.limits);
    if (ok) setDraft(undefined);
  };
  if (list.data === undefined) return <p role="status">{list.isError ? list.error.message : t('app.loading')}</p>;
  const edit = (l: CashLimit) => setDraft({ id: l.id, category: l.category, amount: l.amount.replace('.', ','), action: l.action, from: isoToRo(l.validFrom),
    to: l.validTo == null ? '' : isoToRo(l.validTo), rowVersion: l.rowVersion });
  return (
    <>
      <div className="banner dashed">{t('settings.limits.hint')}</div>
      <table className="partner-list cash-list">
        <thead>
          <tr>
            <th scope="col">{t('settings.category')}</th>
            <th scope="col" className="num">{t('settings.amount')}</th>
            <th scope="col">{t('settings.action')}</th>
            <th scope="col">{t('settings.validFrom')}</th>
            <th scope="col">{t('settings.source')}</th>
          </tr>
        </thead>
        <tbody>
          {list.data.map((l) => (
            <tr key={l.id} onClick={() => !l.isLegal && edit(l)}>
              <td className="mono">{l.category}</td>
              <td className="num">{formatAmount(l.amount)}</td>
              <td className="small">{t(l.action === 1 ? 'settings.block' : 'settings.warn')}</td>
              <td className="small">{isoToRo(l.validFrom)}{l.validTo == null ? '' : ` – ${isoToRo(l.validTo)}`}</td>
              <td className="small">{l.isLegal ? `🔒 ${l.legalReference ?? t('settings.legal')}` : t('settings.own')}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {draft === undefined ? (
        <div className="actions">
          <button type="button" className="btn ghost" onClick={() => setDraft({ id: null, category: 'IncasarePJ', amount: '', action: 1, from: '', to: '', rowVersion: null })}>
            {t('settings.newOwnLimit')}</button>
        </div>
      ) : (
        <div className="settings-editor" role="group" aria-label={t('settings.edit')}>
          <label>{t('settings.category')}<select className="cell-input" value={draft.category} onChange={(e) => setDraft({ ...draft, category: e.target.value })}>
            {limitCategories.map((c) => <option key={c} value={c}>{c}</option>)}</select></label>
          <label>{t('settings.amount')}<input className="cell-input" value={draft.amount} onChange={(e) => setDraft({ ...draft, amount: e.target.value })} /></label>
          <label>{t('settings.action')}<select className="cell-input" value={draft.action} onChange={(e) => setDraft({ ...draft, action: Number(e.target.value) })}>
            <option value={1}>{t('settings.block')}</option><option value={2}>{t('settings.warn')}</option></select></label>
          <label>{t('settings.validFrom')}<input className="cell-input" value={draft.from} placeholder="zz.ll.aaaa" onChange={(e) => setDraft({ ...draft, from: e.target.value })} /></label>
          <label>{t('settings.validTo')}<input className="cell-input" value={draft.to} placeholder="zz.ll.aaaa" onChange={(e) => setDraft({ ...draft, to: e.target.value })} /></label>
          <div className="actions">
            <button type="button" className="btn ghost" onClick={() => setDraft(undefined)}>{t('common.cancel')}</button>
            <button type="button" className="btn primary" onClick={() => void submit()}>{t('common.save')}</button>
          </div>
        </div>
      )}
    </>
  );
}
