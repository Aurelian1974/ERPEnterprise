import { useEffect, useMemo, useState } from 'react';
import { useOutletContext, useSearchParams } from 'react-router';
import { Controller, useFieldArray, useForm, useWatch, type Control, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { TextBoxComponent } from '@syncfusion/ej2-react-inputs';
import { z } from 'zod';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, money, moneyForInput, required } from '../components/forms';
import { formatDate } from '../format/date';
import { t } from '../i18n';
import { isoToRo, roToIso } from '../partners/partnerModel';
import { HistoryView } from '../partners/PartnerTabs';
import { useDebounced } from '../partners/PartnersPage';
import {
  CloseMode, groupAccounts, groupLabel, isCash, kindLabel, proposedGlAccount, requiresBankAccount, treasuryApi, treasuryKeys, TreasuryKind,
  treasuryKinds, useTreasuryAccount, useTreasuryAccounts, useTreasuryHistory, type TreasuryDetail, type TreasuryFilter, type TreasuryLookups,
} from './treasuryApi';

const ManagePermission = 'Fin.TreasuryAccount.Manage';

function useSelection() {
  const [params, setParams] = useSearchParams();
  const id = Number(params.get('id'));
  return {
    id: Number.isInteger(id) && id > 0 ? id : undefined,
    creating: params.get('nou') === '1',
    select: (accountId: number) => setParams({ id: String(accountId) }),
    startCreate: () => setParams({ nou: '1' }),
    clear: () => setParams({}),
  };
}

const filters: readonly { readonly value: TreasuryFilter; readonly label: Parameters<typeof t>[0] }[] = [
  { value: 'all', label: 'treasury.filter.all' },
  { value: TreasuryKind.Bank, label: 'treasury.filter.banks' },
  { value: TreasuryKind.Cash, label: 'treasury.filter.cash' },
  { value: TreasuryKind.CompanyCard, label: 'treasury.filter.cards' },
  { value: TreasuryKind.Transit, label: 'treasury.filter.transit' },
  { value: 'inactive', label: 'treasury.filter.inactive' },
];

/**
 * Trezorerie › Conturi de trezorerie (02.02, F1 3.1 si 11; macheta f1-01): lista conturilor firmei curente, grupata pe tip, si fisa
 * contului cu datele generale (inclusiv lucrul zilnic, plafonul si seriile casei) si casierii.
 */
export function TreasuryAccountsPage() {
  const user = useOutletContext<CurrentUser>();
  const selection = useSelection();
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<TreasuryFilter>('all');
  const debounced = useDebounced(search);
  const list = useTreasuryAccounts(debounced);
  const canManage = user.permissions.includes(ManagePermission);
  const data = list.data;
  const groups = useMemo(() => groupAccounts(data?.accounts ?? [], filter), [data?.accounts, filter]);

  if (data === undefined) {
    return <div className="page" role="status">{list.isError ? t('app.loadError') : t('app.loading')}</div>;
  }

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.treasury')} › {t('menu.treasuryAccounts')}</div>
      <div className="head">
        <div>
          <h1>{t('treasury.title')}</h1>
          <div className="sub2">{t('treasury.subtitle')}</div>
        </div>
        <div className="actions">
          {canManage && (
            <button type="button" className="btn primary" onClick={selection.startCreate}>
              {t('treasury.new')}
            </button>
          )}
        </div>
      </div>

      <div className="split partners">
        <section className="card list-card" aria-label={t('treasury.title')}>
          <TextBoxComponent placeholder={t('treasury.searchPlaceholder')} value={search} input={(args: { value: string }) => setSearch(args.value)}
            showClearButton htmlAttributes={{ 'aria-label': t('treasury.searchPlaceholder') }} />
          <div className="chips-row" role="group" aria-label={t('treasury.filter.label')}>
            {filters.map((f) => (
              <button key={String(f.value)} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                onClick={() => setFilter(f.value)}>
                {t(f.label)}
              </button>
            ))}
          </div>
          {groups.length === 0 && <p className="muted">{t('treasury.empty')}</p>}
          {groups.length > 0 && (
            <table className="partner-list treasury-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('treasury.col.account')}</th>
                  <th scope="col">{t('treasury.col.symbol')}</th>
                  <th scope="col">{t('treasury.col.currency')}</th>
                  <th scope="col">{t('treasury.col.status')}</th>
                </tr>
              </thead>
              {groups.map((group) => (
                <tbody key={group.kind}>
                  <tr className="group-row">
                    <th scope="rowgroup" colSpan={4}>{`${t(groupLabel(group.kind))} · ${group.accounts.length}`}</th>
                  </tr>
                  {group.accounts.map((item) => {
                    const current = item.id === selection.id;
                    return (
                      <tr key={item.id} className={[current ? 'current' : '', item.isActive ? '' : 'inactive'].join(' ').trim()}
                        onClick={() => selection.select(item.id)}>
                        <td>
                          <button type="button" className="row-link" aria-current={current ? 'true' : undefined} onClick={(e) => {
                            e.stopPropagation();
                            selection.select(item.id);
                          }}>
                            {item.code}
                          </button>
                          <div className="muted small">{item.iban == null ? item.name : `${item.bankName ?? ''} · ${item.iban}`}</div>
                        </td>
                        <td className="small">{[item.glAccountCode, item.branchName].filter(Boolean).join(' · ') || t('common.none')}</td>
                        <td className="small">{item.currencyCode}</td>
                        <td>
                          <span className={item.isActive ? 'pill ok' : 'pill off'}>{item.isActive ? t('common.active') : t('common.inactive')}</span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              ))}
            </table>
          )}
          <div className="muted small list-foot">{t('treasury.count', { count: data.accounts.length })}</div>
        </section>

        <section className="card detail-card">
          {selection.creating ? (
            <TreasuryForm key="new" account={undefined} lookups={data.lookups} canManage={canManage} onSaved={(id) => selection.select(id)}
              onCancel={selection.clear} />
          ) : selection.id === undefined ? (
            <p className="muted empty-detail">{t('treasury.select')}</p>
          ) : (
            <TreasuryPanel key={selection.id} accountId={selection.id} lookups={data.lookups} canManage={canManage} />
          )}
        </section>
      </div>
    </div>
  );
}

function TreasuryPanel({ accountId, lookups, canManage }: { readonly accountId: number; readonly lookups: TreasuryLookups; readonly canManage: boolean }) {
  const account = useTreasuryAccount(accountId);
  if (account.data === undefined) {
    return <p role="status">{account.isError ? account.error.message : t('app.loading')}</p>;
  }
  const data = account.data;
  const branch = lookups.branches.find((b) => b.id === data.branchId)?.name;
  return (
    <div className="company-panel">
      <div className="panel-head">
        <div>
          <h2>
            {`${data.code} · ${data.name}`}{' '}
            <span className={data.isActive ? 'pill ok' : 'pill off'}>{data.isActive ? t('common.active') : t('common.inactive')}</span>
          </h2>
          <div className="muted">
            {[`${t(kindLabel(data.kind))} ${data.currencyCode}`, branch, data.openingDate == null ? undefined
              : t('treasury.since', { date: isoToRo(data.openingDate) })].filter(Boolean).join(' · ')}
          </div>
          {isCash(data.kind) && (
            <div className="chips-row">
              <span className="chip static">{t('treasury.cashierCount', { count: data.cashiers.length })}</span>
              {data.maxClosingBalance != null && (
                <span className="chip static">{t('treasury.maxBalanceChip', { amount: formatMoney(data.maxClosingBalance) })}</span>
              )}
            </div>
          )}
        </div>
      </div>
      <TreasuryForm account={data} lookups={lookups} canManage={canManage} onSaved={() => undefined} onCancel={() => undefined} />
    </div>
  );
}

function formatMoney(value: string): string {
  return Number(value).toLocaleString('ro-RO', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

const roDate = z.string().trim().refine((v) => v === '' || roToIso(v) !== undefined, t('validation.date'));

const cashierSchema = z.object({ userId: z.number(), canValidate: z.boolean(), validFrom: roDate, validTo: roDate });

const treasurySchema = z
  .object({
    kind: z.number(),
    code: required.max(30).regex(/^[A-Za-z0-9][A-Za-z0-9._/-]{0,29}$/, t('treasury.validation.code')),
    name: required.max(200),
    branchId: z.number().nullable(),
    currencyCode: required,
    companyBankAccountId: z.number().nullable(),
    glAccountCode: z.string().trim().refine((v) => v === '' || /^[0-9]{3,6}(\.[0-9A-Za-z]{1,10})?$/.test(v), t('treasury.validation.gl')),
    responsibleUserId: z.number().nullable(),
    receiptSeriesId: z.number().nullable(),
    cashInSeriesId: z.number().nullable(),
    cashOutSeriesId: z.number().nullable(),
    maxClosingBalance: money,
    closeMode: z.number(),
    openingDate: roDate,
    isActive: z.boolean(),
    cashiers: z.array(cashierSchema),
  })
  .superRefine((v, ctx) => {
    if (isCash(v.kind) && v.branchId === null) {
      ctx.addIssue({ code: 'custom', path: ['branchId'], message: t('treasury.validation.branch') });
    }
    if (requiresBankAccount(v.kind) && v.companyBankAccountId === null) {
      ctx.addIssue({ code: 'custom', path: ['companyBankAccountId'], message: t('treasury.validation.bankAccount') });
    }
  });
type TreasuryInput = z.input<typeof treasurySchema>;
type TreasuryValues = z.output<typeof treasurySchema>;

const fields = ['kind', 'code', 'name', 'branchId', 'currencyCode', 'companyBankAccountId', 'glAccountCode', 'responsibleUserId', 'receiptSeriesId',
  'cashInSeriesId', 'cashOutSeriesId', 'maxClosingBalance', 'closeMode', 'openingDate', 'isActive'];

function defaultsFor(account: TreasuryDetail | undefined): TreasuryInput {
  return account === undefined
    ? { kind: TreasuryKind.Cash, code: '', name: '', branchId: null, currencyCode: 'RON', companyBankAccountId: null, glAccountCode: '',
        responsibleUserId: null, receiptSeriesId: null, cashInSeriesId: null, cashOutSeriesId: null, maxClosingBalance: '',
        closeMode: CloseMode.OnDemand, openingDate: '', isActive: true, cashiers: [] }
    : { kind: account.kind, code: account.code, name: account.name, branchId: account.branchId ?? null, currencyCode: account.currencyCode,
        companyBankAccountId: account.companyBankAccountId ?? null, glAccountCode: account.glAccountCode ?? '',
        responsibleUserId: account.responsibleUserId ?? null, receiptSeriesId: account.receiptSeriesId ?? null,
        cashInSeriesId: account.cashInSeriesId ?? null, cashOutSeriesId: account.cashOutSeriesId ?? null,
        maxClosingBalance: moneyForInput(account.maxClosingBalance ?? null), closeMode: account.closeMode,
        openingDate: account.openingDate == null ? '' : isoToRo(account.openingDate), isActive: account.isActive,
        cashiers: account.cashiers.map((c) => ({ userId: c.userId, canValidate: c.canValidate, validFrom: c.validFrom == null ? '' : isoToRo(c.validFrom),
          validTo: c.validTo == null ? '' : isoToRo(c.validTo) })) };
}

/** Corpul cererii de salvare din valorile formularului (datele romanesti devin ISO; campurile de casa raman doar pe case). */
export function toSaveBody(values: TreasuryValues, account: TreasuryDetail | undefined) {
  const cash = isCash(values.kind);
  return {
    id: account?.id ?? null,
    rowVersion: account?.rowVersion ?? null,
    kind: values.kind,
    code: values.code,
    name: values.name,
    branchId: values.branchId,
    currencyCode: values.currencyCode,
    companyBankAccountId: requiresBankAccount(values.kind) ? values.companyBankAccountId : null,
    glAccountCode: values.glAccountCode === '' ? null : values.glAccountCode,
    responsibleUserId: cash ? values.responsibleUserId : null,
    receiptSeriesId: cash ? values.receiptSeriesId : null,
    cashInSeriesId: cash ? values.cashInSeriesId : null,
    cashOutSeriesId: cash ? values.cashOutSeriesId : null,
    maxClosingBalance: cash ? values.maxClosingBalance : null,
    closeMode: cash ? values.closeMode : CloseMode.OnDemand,
    openingDate: roToIso(values.openingDate) ?? null,
    isActive: values.isActive,
    cashiers: cash ? values.cashiers.map((c) => ({ userId: c.userId, canValidate: c.canValidate, validFrom: roToIso(c.validFrom) ?? null,
      validTo: roToIso(c.validTo) ?? null })) : [],
  };
}

function TreasuryForm({ account, lookups, canManage, onSaved, onCancel }: {
  readonly account: TreasuryDetail | undefined;
  readonly lookups: TreasuryLookups;
  readonly canManage: boolean;
  readonly onSaved: (id: number) => void;
  readonly onCancel: () => void;
}) {
  const isNew = account === undefined;
  const defaults = useMemo(() => defaultsFor(account), [account]);
  const form = useForm<TreasuryInput, unknown, TreasuryValues>({
    resolver: zodResolver(treasurySchema) as Resolver<TreasuryInput, unknown, TreasuryValues>,
    defaultValues: defaults,
  });
  useEffect(() => form.reset(defaults), [account?.id, account?.rowVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const guard = useUnsavedChanges(form.formState.isDirty);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const [tab, setTab] = useState<'general' | 'cashiers' | 'history'>('general');
  const history = useTreasuryHistory(account?.id, tab === 'history');
  const kind = useWatch({ control: form.control, name: 'kind' });
  const currency = useWatch({ control: form.control, name: 'currencyCode' });
  const cash = isCash(kind);
  const tabs = [...(['general'] as const), ...(cash ? (['cashiers'] as const) : []), ...(account === undefined ? [] : (['history'] as const))];

  const branches = useMemo<Option[]>(() => lookups.branches.map((b) => ({ value: String(b.id), text: b.name })), [lookups.branches]);
  const currencies = useMemo<Option[]>(() => lookups.currencies.map((c) => ({ value: c, text: c })), [lookups.currencies]);
  const users = useMemo<Option[]>(() => lookups.users.map((u) => ({ value: String(u.id), text: u.name })), [lookups.users]);
  const bankAccounts = useMemo<Option[]>(
    () => lookups.bankAccounts.filter((b) => b.currencyCode === currency).map((b) => ({ value: String(b.id), text: `${b.bankName} · ${b.iban}` })),
    [lookups.bankAccounts, currency],
  );
  const series = (documentType: string): Option[] =>
    lookups.series.filter((s) => s.documentType === documentType).map((s) => ({ value: String(s.id), text: `${s.prefix} · ${s.year}` }));
  const receiptSeries = useMemo(() => series('Finance.Receipt'), [lookups.series]); // eslint-disable-line react-hooks/exhaustive-deps
  const cashInSeries = useMemo(() => series('Finance.CashIn'), [lookups.series]); // eslint-disable-line react-hooks/exhaustive-deps
  const cashOutSeries = useMemo(() => series('Finance.Payment'), [lookups.series]); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      const saved = await treasuryApi.save(toSaveBody(values, account));
      form.reset(form.getValues());
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: treasuryKeys.all });
      notify(t('common.saved'));
      onSaved(saved.id);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, fields);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  const info = account === undefined ? undefined
    : t('common.lastChanged', { name: account.modifiedByName ?? '', date: formatDate(new Date(account.modifiedAt ?? account.createdAt)) });
  const proposed = proposedGlAccount(kind, currency);
  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(e)} aria-label={account === undefined ? t('treasury.newTitle') : account.code}>
      {isNew && <h2 className="form-title">{t('treasury.newTitle')}</h2>}
      {tabs.length > 1 && (
        <div className="tabs" role="tablist" aria-label={t('treasury.title')}>
          {tabs.map((key) => (
            <button key={key} type="button" role="tab" id={`treasury-tab-${key}`} aria-selected={tab === key} tabIndex={tab === key ? 0 : -1}
              className={tab === key ? 'tab on' : 'tab'} onClick={() => setTab(key)}>
              {t(key === 'general' ? 'treasury.tab.general' : key === 'cashiers' ? 'treasury.tab.cashiers' : 'partner.tab.history')}
            </button>
          ))}
        </div>
      )}
      <ErrorBanner message={error} />
      <fieldset className="form-body plain" disabled={!canManage} hidden={tab !== 'general'}>
        <Controller
          control={form.control}
          name="kind"
          render={({ field }) => (
            <div className="field">
              <span className="label" id="treasury-kind-label">{t('treasury.field.kind')} *</span>
              <div className="segmented five" role="radiogroup" aria-labelledby="treasury-kind-label">
                {treasuryKinds.map((value) => (
                  <button key={value} type="button" role="radio" aria-checked={field.value === value} className={field.value === value ? 'on' : ''}
                    disabled={!isNew} onClick={() => field.onChange(value)}>
                    {t(kindLabel(value))}
                  </button>
                ))}
              </div>
            </div>
          )}
        />
        <div className="grid g3">
          <TextField control={form.control} name="code" label={`${t('treasury.field.code')} *`} upper />
          <TextField control={form.control} name="name" label={`${t('treasury.field.name')} *`} className="span2" />
          <SelectField control={form.control} name="branchId" label={cash ? `${t('field.branch')} *` : t('field.branch')} options={branches}
            allowEmpty={t('common.none')} numeric />
          <SelectField control={form.control} name="currencyCode" label={`${t('treasury.field.currency')} *`} options={currencies}
            disabled={account?.hasDocuments === true} />
          <div>
            <TextField control={form.control} name="glAccountCode" label={t('treasury.field.gl')} />
            <span className="muted small">{proposed === undefined ? t('treasury.hint.gl') : t('treasury.hint.glProposed', { symbol: proposed })}</span>
          </div>
          {requiresBankAccount(kind) && (
            <SelectField control={form.control} name="companyBankAccountId" label={`${t('treasury.field.bankAccount')} *`} options={bankAccounts}
              allowEmpty={t('common.none')} numeric className="span2" />
          )}
        </div>
        {cash && (
          <>
            <h3 className="section">{t('treasury.section.daily')}</h3>
            <div className="grid g3">
              <SelectField control={form.control} name="responsibleUserId" label={t('treasury.field.responsible')} options={users}
                allowEmpty={t('common.none')} numeric />
              <Controller
                control={form.control}
                name="closeMode"
                render={({ field }) => (
                  <div className="field">
                    <span className="label" id="treasury-close-label">{t('treasury.field.closeMode')}</span>
                    <div className="segmented two" role="radiogroup" aria-labelledby="treasury-close-label">
                      {([CloseMode.OnDemand, CloseMode.Automatic] as const).map((value) => (
                        <button key={value} type="button" role="radio" aria-checked={field.value === value} className={field.value === value ? 'on' : ''}
                          onClick={() => field.onChange(value)}>
                          {t(value === CloseMode.OnDemand ? 'treasury.close.onDemand' : 'treasury.close.automatic')}
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              />
              <TextField control={form.control} name="openingDate" label={t('treasury.field.openingDate')} />
            </div>
            <h3 className="section">{t('treasury.section.limitSeries')}</h3>
            <div className="grid g3">
              <div>
                <TextField control={form.control} name="maxClosingBalance" label={t('treasury.field.maxBalance')} />
                <span className="muted small">{t('treasury.hint.maxBalance')}</span>
              </div>
              <SelectField control={form.control} name="receiptSeriesId" label={t('treasury.field.receiptSeries')} options={receiptSeries}
                allowEmpty={t('treasury.defaultSeries')} numeric />
              <SelectField control={form.control} name="cashInSeriesId" label={t('treasury.field.cashInSeries')} options={cashInSeries}
                allowEmpty={t('treasury.defaultSeries')} numeric />
              <SelectField control={form.control} name="cashOutSeriesId" label={t('treasury.field.cashOutSeries')} options={cashOutSeries}
                allowEmpty={t('treasury.defaultSeries')} numeric />
            </div>
          </>
        )}
        <div className="grid g3">
          <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
        </div>
        <div className="banner dashed">{t('treasury.hint.rules')}</div>
      </fieldset>
      {cash && tab === 'cashiers' && <CashiersEditor control={form.control} users={users} disabled={!canManage} />}
      {tab === 'history' && <div role="tabpanel" aria-labelledby="treasury-tab-history"><HistoryView history={history} /></div>}
      <div className="form-footer">
        <span className="muted">{info}</span>
        {canManage && (
          <div className="actions">
            <button type="button" className="btn ghost" onClick={() => (isNew ? onCancel() : form.reset(defaults))}>
              {t('common.cancel')}
            </button>
            <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>
              {t('common.save')}
            </button>
          </div>
        )}
      </div>
    </form>
  );
}

/** Tab-ul Casieri: utilizatorii atribuiti casei, cu dreptul de validare si perioada atribuirii (F1 3.1, TreasuryAccountUser). */
function CashiersEditor({ control, users, disabled }: { readonly control: Control<TreasuryInput>; readonly users: readonly Option[]; readonly disabled: boolean }) {
  const { fields: rows, append, remove } = useFieldArray({ control, name: 'cashiers' });
  const [adding, setAdding] = useState('');
  const assigned = new Set(rows.map((r) => String(r.userId)));
  const available = users.filter((u) => !assigned.has(u.value));
  return (
    <div role="tabpanel" aria-labelledby="treasury-tab-cashiers" className="form-body plain">
      {rows.length === 0 && <p className="muted">{t('treasury.cashiers.empty')}</p>}
      {rows.length > 0 && (
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('treasury.cashiers.user')}</th>
              <th scope="col">{t('treasury.cashiers.canValidate')}</th>
              <th scope="col">{t('treasury.cashiers.validFrom')}</th>
              <th scope="col">{t('treasury.cashiers.validTo')}</th>
              <th scope="col"><span className="sr-only">{t('treasury.cashiers.remove')}</span></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <tr key={row.id}>
                <td>{users.find((u) => u.value === String(row.userId))?.text ?? String(row.userId)}</td>
                <td>
                  <Controller control={control} name={`cashiers.${index}.canValidate`} render={({ field }) => (
                    <input type="checkbox" checked={field.value} disabled={disabled} aria-label={t('treasury.cashiers.canValidate')}
                      onChange={(e) => field.onChange(e.target.checked)} />
                  )} />
                </td>
                <td>
                  <Controller control={control} name={`cashiers.${index}.validFrom`} render={({ field, fieldState }) => (
                    <input className="cell-input" value={field.value} disabled={disabled} placeholder="zz.ll.aaaa" aria-label={t('treasury.cashiers.validFrom')}
                      aria-invalid={fieldState.error !== undefined} onChange={(e) => field.onChange(e.target.value)} />
                  )} />
                </td>
                <td>
                  <Controller control={control} name={`cashiers.${index}.validTo`} render={({ field, fieldState }) => (
                    <input className="cell-input" value={field.value} disabled={disabled} placeholder="zz.ll.aaaa" aria-label={t('treasury.cashiers.validTo')}
                      aria-invalid={fieldState.error !== undefined} onChange={(e) => field.onChange(e.target.value)} />
                  )} />
                </td>
                <td>
                  {!disabled && (
                    <button type="button" className="btn ghost small" onClick={() => remove(index)}>{t('treasury.cashiers.remove')}</button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {!disabled && available.length > 0 && (
        <div className="chips-row">
          <select className="chip" aria-label={t('treasury.cashiers.add')} value={adding} onChange={(e) => setAdding(e.target.value)}>
            <option value="">{t('treasury.cashiers.choose')}</option>
            {available.map((u) => (
              <option key={u.value} value={u.value}>{u.text}</option>
            ))}
          </select>
          <button type="button" className="btn ghost" disabled={adding === ''} onClick={() => {
            append({ userId: Number(adding), canValidate: true, validFrom: '', validTo: '' });
            setAdding('');
          }}>
            {t('treasury.cashiers.add')}
          </button>
        </div>
      )}
      <p className="muted small">{t('treasury.cashiers.hint')}</p>
    </div>
  );
}
