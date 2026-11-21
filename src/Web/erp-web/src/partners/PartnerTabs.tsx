import { useCallback, useEffect, useMemo, useState } from 'react';
import { useForm, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient, type UseQueryResult } from '@tanstack/react-query';
import { ColumnDirective, ColumnsDirective, GridComponent } from '@syncfusion/ej2-react-grids';
import { z } from 'zod';
import { ErrorBanner, FieldIdPrefix, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { currencyOptions, useCurrencies } from '../rates/ratesApi';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, optionalEmail, optionalPhone, optionalText, required, ROMANIA } from '../components/forms';
import { formatDate } from '../format/date';
import { t, type TextKey } from '../i18n';
import { CityField } from '../org/CityField';
import { ro } from '../i18n/ro';
import { actionOption, fieldLabel } from '../audit/auditModel';
import { addressSchema } from '../org/CompanyTabs';
import { countryOptions, countyOptions, useCountyFollowsCountry } from '../org/OrgForms';
import type { OrgLookups } from '../org/orgApi';
import { partnerApi, partnerKeys, usePartnerHistory, type HistoryEntry, type PartnerAddress, type PartnerBankAccount, type PartnerContact, type PartnerDetail } from './partnerApi';

/** Salvarea unui rand copil: erori pe camp, notificare, reincarcarea fisei si a istoricului (UI-11). */
function useChildSave(partnerId: number) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  return {
    error,
    setError,
    notify,
    async refresh() {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: partnerKeys.detail(partnerId) }),
        queryClient.invalidateQueries({ queryKey: partnerKeys.history(partnerId) }),
      ]);
    },
  };
}

function SubformActions({ onCancel, submitting }: { readonly onCancel: () => void; readonly submitting: boolean }) {
  return (
    <div className="actions end">
      <button type="button" className="btn ghost" onClick={onCancel}>
        {t('common.cancel')}
      </button>
      <button type="submit" className="btn primary" disabled={submitting}>
        {t('common.save')}
      </button>
    </div>
  );
}

type AddressInput = z.input<typeof addressSchema>;
type AddressValues = z.output<typeof addressSchema>;

/** Adresele partenerului pe tip (livrare, facturare, ...); sediul social ramane unic, in Date generale. */
export function PartnerAddressesTab({ partner, orgLookups, canManage }: { readonly partner: PartnerDetail; readonly orgLookups: OrgLookups; readonly canManage: boolean }) {
  const [selected, setSelected] = useState<PartnerAddress | 'new' | undefined>();
  const address = selected === 'new' ? undefined : selected;
  const save = useChildSave(partner.id);
  const types = orgLookups.addressTypes;
  const defaults: AddressInput = address === undefined
    ? { addressType: types.find((x) => x.isActive)?.id ?? 0, name: null, address: '', countryCode: partner.countryCode, countyCode: null, city: '', citySirutaCode: null,
        postalCode: null, phone: null, email: null, isDefault: !partner.addresses.some((a) => a.isDefault), isActive: true }
    : { addressType: address.addressType, name: address.name ?? null, address: address.address, countryCode: address.countryCode,
        countyCode: address.countyCode ?? null, city: address.city, citySirutaCode: address.citySirutaCode, postalCode: address.postalCode ?? null, phone: address.phone ?? null,
        email: address.email ?? null, isDefault: address.isDefault, isActive: address.isActive };
  const form = useForm<AddressInput, unknown, AddressValues>({
    resolver: zodResolver(addressSchema) as Resolver<AddressInput, unknown, AddressValues>,
    defaultValues: defaults,
  });
  useEffect(() => form.reset(defaults), [selected]); // eslint-disable-line react-hooks/exhaustive-deps
  const country = useCountyFollowsCountry(form as never, orgLookups);
  const guard = useUnsavedChanges(selected !== undefined && form.formState.isDirty);
  const rows = useMemo(() => {
    const names = new Map(types.map((x) => [x.id, x.name]));
    return partner.addresses.map((a) => ({ ...a, typeName: names.get(a.addressType) ?? String(a.addressType) }));
  }, [partner.addresses, types]);
  const typeOptions = useMemo<Option[]>(
    () => types.filter((x) => x.isActive || x.id === address?.addressType).map((x) => ({ value: String(x.id), text: x.name })),
    [types, address?.addressType],
  );
  const onRowSelected = useCallback((args: { data: PartnerAddress }) => {
    if (canManage) {
      setSelected(args.data);
    }
  }, [canManage]);

  const submit = form.handleSubmit(async (values) => {
    save.setError(undefined);
    try {
      await partnerApi.saveAddress({ ...values, id: address?.id ?? null, partnerId: partner.id, rowVersion: address?.rowVersion ?? null });
      form.reset(form.getValues());
      guard.markClean();
      await save.refresh();
      save.notify(t('common.saved'));
      setSelected(undefined);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      save.setError(message);
      save.notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => save.notify(t('common.notSaved'), 'error'));

  return (
    <div className="tab-body">
      <div className="toolbar">
        <span className="muted">{t('partner.address.hint')}</span>
        {canManage && (
          <button type="button" className="btn ghost" onClick={() => setSelected('new')}>
            {t('address.add')}
          </button>
        )}
      </div>
      {rows.length === 0 ? (
        <p className="muted">{t('partner.address.empty')}</p>
      ) : (
        <GridComponent dataSource={rows} rowSelected={onRowSelected} allowSorting height="auto">
          <ColumnsDirective>
            <ColumnDirective field="typeName" headerText={t('field.addressType')} width="140" />
            <ColumnDirective field="name" headerText={t('field.addressName')} width="150" />
            <ColumnDirective field="address" headerText={t('field.address')} />
            <ColumnDirective field="city" headerText={t('field.city')} width="140" />
            <ColumnDirective field="isDefault" headerText={t('field.isDefaultAddress')} width="140" displayAsCheckBox type="boolean" />
            <ColumnDirective field="isActive" headerText={t('common.active')} width="90" displayAsCheckBox type="boolean" />
          </ColumnsDirective>
        </GridComponent>
      )}
      {selected !== undefined && (
        <FieldIdPrefix.Provider value="paddr">
          <form className="subform" noValidate onSubmit={(e) => void submit(e)} aria-label={t('address.edit')}>
            <ErrorBanner message={save.error} />
            <div className="grid g3">
              <SelectField control={form.control} name="addressType" label={t('field.addressType')} options={typeOptions} numeric />
              <TextField control={form.control} name="name" label={t('field.addressName')} className="span2" />
              <TextField control={form.control} name="address" label={t('field.address')} className="span2" />
              <SelectField control={form.control} name="countryCode" label={t('field.country')} options={countryOptions(orgLookups)} />
              <SelectField control={form.control} name="countyCode" label={t('field.county')} options={countyOptions(orgLookups, country)} allowEmpty={t('common.none')} />
              <CityField form={form} label={t('field.city')} />
              <TextField control={form.control} name="postalCode" label={t('field.postalCode')} />
              <TextField control={form.control} name="phone" label={t('field.phone')} />
              <TextField control={form.control} name="email" label={t('field.email')} />
              <SwitchField control={form.control} name="isDefault" label={t('field.isDefaultAddress')} />
              <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
            </div>
            <SubformActions onCancel={() => setSelected(undefined)} submitting={form.formState.isSubmitting} />
          </form>
        </FieldIdPrefix.Provider>
      )}
    </div>
  );
}

const bankSchema = z.object({
  iban: required.max(42),
  bankName: required.max(100),
  currency: required.regex(/^[A-Z]{3}$/, t('validation.currency')),
  isDefault: z.boolean(),
  isActive: z.boolean(),
});
type BankValues = z.infer<typeof bankSchema>;

/** Conturile bancare ale partenerului: un singur cont implicit pe moneda, cel copiat pe documente. */
export function PartnerBankAccountsTab({ partner, canManage }: { readonly partner: PartnerDetail; readonly canManage: boolean }) {
  const [selected, setSelected] = useState<PartnerBankAccount | 'new' | undefined>();
  const account = selected === 'new' ? undefined : selected;
  const save = useChildSave(partner.id);
  const defaults: BankValues = account === undefined
    ? { iban: '', bankName: '', currency: partner.countryCode === ROMANIA ? 'RON' : 'EUR', isDefault: partner.bankAccounts.length === 0, isActive: true }
    : { iban: account.iban, bankName: account.bankName, currency: account.currency, isDefault: account.isDefault, isActive: account.isActive };
  const form = useForm<BankValues>({ resolver: zodResolver(bankSchema) as Resolver<BankValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [selected]); // eslint-disable-line react-hooks/exhaustive-deps
  // Moneda din nomenclatorul de valute (02.01, 3.5): cele active, plus cea a contului.
  const currencies = useCurrencies();
  const currencyChoices = useMemo(() => currencyOptions(currencies.data, defaults.currency), [currencies.data, defaults.currency]);
  const guard = useUnsavedChanges(selected !== undefined && form.formState.isDirty);
  const onRowSelected = useCallback((args: { data: PartnerBankAccount }) => {
    if (canManage) {
      setSelected(args.data);
    }
  }, [canManage]);

  const submit = form.handleSubmit(async (values) => {
    save.setError(undefined);
    try {
      await partnerApi.saveBankAccount({ ...values, id: account?.id ?? null, partnerId: partner.id, rowVersion: account?.rowVersion ?? null });
      form.reset(form.getValues());
      guard.markClean();
      await save.refresh();
      save.notify(t('common.saved'));
      setSelected(undefined);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      save.setError(message);
      save.notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => save.notify(t('common.notSaved'), 'error'));

  return (
    <div className="tab-body">
      <div className="toolbar">
        {canManage && (
          <button type="button" className="btn ghost" onClick={() => setSelected('new')}>
            {t('bank.add')}
          </button>
        )}
      </div>
      {partner.bankAccounts.length === 0 ? (
        <p className="muted">{t('partner.bank.empty')}</p>
      ) : (
        <GridComponent dataSource={partner.bankAccounts} rowSelected={onRowSelected} allowSorting height="auto">
          <ColumnsDirective>
            <ColumnDirective field="iban" headerText={t('field.iban')} width="260" />
            <ColumnDirective field="bankName" headerText={t('field.bank')} />
            <ColumnDirective field="currency" headerText={t('field.currency')} width="90" />
            <ColumnDirective field="isDefault" headerText={t('partner.bank.isDefault')} width="160" displayAsCheckBox type="boolean" />
            <ColumnDirective field="isActive" headerText={t('common.active')} width="90" displayAsCheckBox type="boolean" />
          </ColumnsDirective>
        </GridComponent>
      )}
      {selected !== undefined && (
        <FieldIdPrefix.Provider value="pbank">
          <form className="subform" noValidate onSubmit={(e) => void submit(e)} aria-label={t('bank.edit')}>
            <ErrorBanner message={save.error} />
            <div className="grid g3">
              <TextField control={form.control} name="iban" label={t('field.iban')} upper className="span2" />
              <SelectField control={form.control} name="currency" label={t('field.currency')} options={currencyChoices} />
              <TextField control={form.control} name="bankName" label={t('field.bank')} className="span2" />
              <SwitchField control={form.control} name="isDefault" label={t('partner.bank.isDefault')} />
              <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
            </div>
            <SubformActions onCancel={() => setSelected(undefined)} submitting={form.formState.isSubmitting} />
          </form>
        </FieldIdPrefix.Provider>
      )}
    </div>
  );
}

const contactSchema = z
  .object({
    name: required.max(150),
    role: optionalText,
    phone: optionalPhone,
    email: optionalEmail,
    notes: optionalText,
    isActive: z.boolean(),
  })
  .refine((v) => v.phone !== null || v.email !== null, { message: t('validation.contactReach'), path: ['phone'] });
type ContactInput = z.input<typeof contactSchema>;
type ContactValues = z.output<typeof contactSchema>;

/** Persoanele de contact ale partenerului: cel putin un telefon sau un e-mail. */
export function PartnerContactsTab({ partner, canManage }: { readonly partner: PartnerDetail; readonly canManage: boolean }) {
  const [selected, setSelected] = useState<PartnerContact | 'new' | undefined>();
  const contact = selected === 'new' ? undefined : selected;
  const save = useChildSave(partner.id);
  const defaults: ContactInput = contact === undefined
    ? { name: '', role: null, phone: null, email: null, notes: null, isActive: true }
    : { name: contact.name, role: contact.role ?? null, phone: contact.phone ?? null, email: contact.email ?? null, notes: contact.notes ?? null,
        isActive: contact.isActive };
  const form = useForm<ContactInput, unknown, ContactValues>({
    resolver: zodResolver(contactSchema) as Resolver<ContactInput, unknown, ContactValues>,
    defaultValues: defaults,
  });
  useEffect(() => form.reset(defaults), [selected]); // eslint-disable-line react-hooks/exhaustive-deps
  const guard = useUnsavedChanges(selected !== undefined && form.formState.isDirty);
  const onRowSelected = useCallback((args: { data: PartnerContact }) => {
    if (canManage) {
      setSelected(args.data);
    }
  }, [canManage]);

  const submit = form.handleSubmit(async (values) => {
    save.setError(undefined);
    try {
      await partnerApi.saveContact({ ...values, id: contact?.id ?? null, partnerId: partner.id, rowVersion: contact?.rowVersion ?? null });
      form.reset(form.getValues());
      guard.markClean();
      await save.refresh();
      save.notify(t('common.saved'));
      setSelected(undefined);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      save.setError(message);
      save.notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => save.notify(t('common.notSaved'), 'error'));

  return (
    <div className="tab-body">
      <div className="toolbar">
        {canManage && (
          <button type="button" className="btn ghost" onClick={() => setSelected('new')}>
            {t('contact.add')}
          </button>
        )}
      </div>
      {partner.contacts.length === 0 ? (
        <p className="muted">{t('partner.contact.empty')}</p>
      ) : (
        <GridComponent dataSource={partner.contacts} rowSelected={onRowSelected} allowSorting height="auto">
          <ColumnsDirective>
            <ColumnDirective field="name" headerText={t('field.contactName')} width="200" />
            <ColumnDirective field="role" headerText={t('field.contactRole')} width="160" />
            <ColumnDirective field="phone" headerText={t('field.phone')} width="130" />
            <ColumnDirective field="email" headerText={t('field.email')} />
            <ColumnDirective field="isActive" headerText={t('common.active')} width="90" displayAsCheckBox type="boolean" />
          </ColumnsDirective>
        </GridComponent>
      )}
      {selected !== undefined && (
        <FieldIdPrefix.Provider value="pcontact">
          <form className="subform" noValidate onSubmit={(e) => void submit(e)} aria-label={t('contact.edit')}>
            <ErrorBanner message={save.error} />
            <div className="grid g3">
              <TextField control={form.control} name="name" label={t('field.contactName')} className="span2" />
              <TextField control={form.control} name="role" label={t('field.contactRole')} />
              <TextField control={form.control} name="phone" label={t('field.phone')} />
              <TextField control={form.control} name="email" label={t('field.email')} className="span2" />
              <TextField control={form.control} name="notes" label={t('field.notes')} className="span3" />
              <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
            </div>
            <SubformActions onCancel={() => setSelected(undefined)} submitting={form.formState.isSubmitting} />
          </form>
        </FieldIdPrefix.Provider>
      )}
    </div>
  );
}

const dateTime = new Intl.DateTimeFormat('ro-RO', { timeZone: 'Europe/Bucharest', hour: '2-digit', minute: '2-digit' });

/** Textul actiunii din audit; o actiune necunoscuta ramane cu codul ei. */
export function actionLabel(action: string): string {
  const key = `partner.action.${action}`;
  return key in ro ? t(key as TextKey) : actionOption(action);
}

/** Detaliile unei intrari: campurile JSON ca „camp: valoare”, fara campurile goale. */
export function changeSummary(changes: string | null | undefined): string {
  if (changes == null) {
    return '';
  }
  try {
    const parsed = JSON.parse(changes) as Record<string, unknown>;
    return Object.entries(parsed)
      .filter(([, value]) => value !== null && value !== undefined)
      .map(([key, value]) => `${fieldLabel(key)}: ${typeof value === 'boolean' ? t(value ? 'common.yes' : 'common.no') : typeof value === 'object' ? JSON.stringify(value) : String(value)}`)
      .join(' · ');
  } catch {
    return changes;
  }
}

/** Istoricul partenerului (ultimul tab, 02.01, 11): auditul partenerului, al adreselor, conturilor si contactelor lui. */
export function PartnerHistoryTab({ partnerId, active }: { readonly partnerId: number; readonly active: boolean }) {
  return <HistoryView history={usePartnerHistory(partnerId, active)} />;
}

/** Tabelul istoricului (auditul unei fise), comun partenerilor si articolelor. */
export function HistoryView({ history }: { readonly history: UseQueryResult<HistoryEntry[]> }) {
  if (history.data === undefined) {
    return history.isError ? (
      <div className="banner error" role="alert">
        {t('app.loadError')}{' '}
        <button type="button" className="btn ghost" onClick={() => void history.refetch()}>
          {t('app.retry')}
        </button>
      </div>
    ) : (
      <p className="muted" role="status">{t('app.loading')}</p>
    );
  }
  if (history.data.length === 0) {
    return <p className="muted">{t('partner.history.empty')}</p>;
  }
  return (
    <div className="tab-body">
      <table className="simple history">
        <thead>
          <tr>
            <th scope="col">{t('partner.history.when')}</th>
            <th scope="col">{t('partner.history.who')}</th>
            <th scope="col">{t('partner.history.what')}</th>
            <th scope="col">{t('partner.history.details')}</th>
          </tr>
        </thead>
        <tbody>
          {history.data.map((entry: HistoryEntry) => {
            const when = new Date(entry.occurredAt);
            return (
              <tr key={entry.id}>
                <td>{`${formatDate(when)} ${dateTime.format(when)}`}</td>
                <td>{entry.userName}</td>
                <td>{actionLabel(entry.action)}</td>
                <td className="muted">{changeSummary(entry.changes)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
