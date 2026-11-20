import { useCallback, useEffect, useMemo, useState } from 'react';
import { useForm, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { ColumnDirective, ColumnsDirective, GridComponent } from '@syncfusion/ej2-react-grids';
import { Link } from 'react-router';
import { z } from 'zod';
import { ErrorBanner, FieldIdPrefix, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, optionalEmail, optionalPhone, optionalText, required, requireCountyForRomania, ROMANIA } from '../components/forms';
import { t } from '../i18n';
import { countryOptions, countyOptions, useCountyFollowsCountry } from './OrgForms';
import { orgApi, orgKeys, useNumberSeries, type BankAccount, type CompanyAddress, type CompanyContact, type CompanyDetail, type OrgLookups } from './orgApi';

const bankSchema = z.object({
  iban: required.max(42),
  bankName: required.max(100),
  currency: required.regex(/^[A-Z]{3}$/, t('validation.currency')),
  isDefault: z.boolean(),
  isActive: z.boolean(),
});
type BankValues = z.infer<typeof bankSchema>;

/** Conturile bancare ale firmei (macheta 03): un singur cont implicit pe moneda, cel tiparit pe facturi. */
export function BankAccountsTab({ company, canManage }: { readonly company: CompanyDetail; readonly canManage: boolean }) {
  const [selected, setSelected] = useState<BankAccount | 'new' | undefined>();
  const account = selected === 'new' ? undefined : selected;
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const defaults: BankValues = account === undefined
    ? { iban: '', bankName: '', currency: 'RON', isDefault: company.bankAccounts.length === 0, isActive: true }
    : { iban: account.iban, bankName: account.bankName, currency: account.currency, isDefault: account.isDefault, isActive: account.isActive };
  const form = useForm<BankValues>({ resolver: zodResolver(bankSchema) as Resolver<BankValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [selected]); // eslint-disable-line react-hooks/exhaustive-deps
  // Contul deschis in editare intra in garda modificarilor nesalvate (UI-18) cat timp formularul exista.
  const guard = useUnsavedChanges(selected !== undefined && form.formState.isDirty);
  // Handler stabil (UI-12): o functie noua la fiecare randare face grila sa se relege.
  const onRowSelected = useCallback((args: { data: BankAccount }) => {
    if (canManage) {
      setSelected(args.data);
    }
  }, [canManage]);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      await orgApi.saveBankAccount({ ...values, id: account?.id ?? null, companyId: company.id, rowVersion: account?.rowVersion ?? null });
      form.reset(form.getValues());
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: orgKeys.company(company.id) });
      notify(t('common.saved'));
      setSelected(undefined);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  return (
    <div className="tab-body">
      <div className="toolbar">
        {canManage && (
          <button type="button" className="btn ghost" onClick={() => setSelected('new')}>
            {t('bank.add')}
          </button>
        )}
      </div>
      {company.bankAccounts.length === 0 ? (
        <p className="muted">{t('bank.empty')}</p>
      ) : (
        <GridComponent
          dataSource={company.bankAccounts}
          rowSelected={onRowSelected}
          allowSorting
          height="auto"
        >
          <ColumnsDirective>
            <ColumnDirective field="iban" headerText={t('field.iban')} width="260" />
            <ColumnDirective field="bankName" headerText={t('field.bank')} />
            <ColumnDirective field="currency" headerText={t('field.currency')} width="90" />
            <ColumnDirective field="isDefault" headerText={t('field.isDefault')} width="150" displayAsCheckBox type="boolean" />
            <ColumnDirective field="isActive" headerText={t('common.active')} width="90" displayAsCheckBox type="boolean" />
          </ColumnsDirective>
        </GridComponent>
      )}
      {selected !== undefined && (
        // Prefix propriu de id-uri: fisa firmei ramane montata alaturi si are si ea un camp isActive.
        <FieldIdPrefix.Provider value="bank">
          <form className="subform" noValidate onSubmit={(e) => void submit(e)} aria-label={t('bank.edit')}>
            <ErrorBanner message={error} />
            <div className="grid g3">
              <TextField control={form.control} name="iban" label={t('field.iban')} upper className="span2" />
              <TextField control={form.control} name="currency" label={t('field.currency')} upper />
              <TextField control={form.control} name="bankName" label={t('field.bank')} className="span2" />
              <SwitchField control={form.control} name="isDefault" label={t('field.isDefault')} />
              <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
            </div>
            <div className="actions end">
              <button type="button" className="btn ghost" onClick={() => setSelected(undefined)}>
                {t('common.cancel')}
              </button>
              <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>
                {t('common.save')}
              </button>
            </div>
          </form>
        </FieldIdPrefix.Provider>
      )}
    </div>
  );
}

export const addressSchema = z
  .object({
    addressType: z.number().int().min(1),
    name: optionalText,
    address: required.max(300),
    countryCode: required,
    countyCode: optionalText,
    city: required.max(100),
    postalCode: optionalText,
    phone: optionalPhone,
    email: optionalEmail,
    isDefault: z.boolean(),
    isActive: z.boolean(),
  })
  .superRefine(requireCountyForRomania);
type AddressInput = z.input<typeof addressSchema>;
type AddressValues = z.output<typeof addressSchema>;

/** Adresele firmei pe tip (macheta 03): livrare, facturare, corespondenta; sediul social ramane unic, in Date generale. */
export function AddressesTab({ company, lookups, canManage }: { readonly company: CompanyDetail; readonly lookups: OrgLookups; readonly canManage: boolean }) {
  const [selected, setSelected] = useState<CompanyAddress | 'new' | undefined>();
  const address = selected === 'new' ? undefined : selected;
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const types = lookups.addressTypes;
  const defaultType = types.find((x) => x.isActive)?.id ?? 0;
  const defaults: AddressInput = address === undefined
    ? { addressType: defaultType, name: null, address: '', countryCode: ROMANIA, countyCode: null, city: '', postalCode: null, phone: null, email: null, isDefault: false, isActive: true }
    : { addressType: address.addressType, name: address.name ?? null, address: address.address, countryCode: address.countryCode,
        countyCode: address.countyCode ?? null, city: address.city, postalCode: address.postalCode ?? null,
        phone: address.phone ?? null, email: address.email ?? null, isDefault: address.isDefault, isActive: address.isActive };
  const form = useForm<AddressInput, unknown, AddressValues>({
    resolver: zodResolver(addressSchema) as Resolver<AddressInput, unknown, AddressValues>,
    defaultValues: defaults,
  });
  useEffect(() => form.reset(defaults), [selected]); // eslint-disable-line react-hooks/exhaustive-deps
  const country = useCountyFollowsCountry(form as never, lookups);
  const guard = useUnsavedChanges(selected !== undefined && form.formState.isDirty);
  // Randurile cu tipul afisat ca text, memoizate (UI-12): o lista noua la fiecare randare face grila sa se relege.
  const rows = useMemo(() => {
    const names = new Map(types.map((x) => [x.id, x.name]));
    return company.addresses.map((a) => ({ ...a, typeName: names.get(a.addressType) ?? String(a.addressType) }));
  }, [company.addresses, types]);
  // Tipurile inactive nu se mai ofera pentru adrese noi; tipul curent al unei adrese existente ramane in lista.
  const addressTypeOptions = useMemo<Option[]>(
    () => types.filter((x) => x.isActive || x.id === address?.addressType).map((x) => ({ value: String(x.id), text: x.name })),
    [types, address?.addressType],
  );
  const onRowSelected = useCallback((args: { data: CompanyAddress }) => {
    if (canManage) {
      setSelected(args.data);
    }
  }, [canManage]);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      await orgApi.saveAddress({ ...values, id: address?.id ?? null, companyId: company.id, rowVersion: address?.rowVersion ?? null });
      form.reset(form.getValues());
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: orgKeys.company(company.id) });
      notify(t('common.saved'));
      setSelected(undefined);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  return (
    <div className="tab-body">
      <div className="toolbar">
        <span className="muted">{t('address.hint')}</span>
        {canManage && (
          <button type="button" className="btn ghost" onClick={() => setSelected('new')}>
            {t('address.add')}
          </button>
        )}
      </div>
      {rows.length === 0 ? (
        <p className="muted">{t('address.empty')}</p>
      ) : (
        <GridComponent dataSource={rows} rowSelected={onRowSelected} allowSorting height="auto">
          <ColumnsDirective>
            <ColumnDirective field="typeName" headerText={t('field.addressType')} width="150" />
            <ColumnDirective field="name" headerText={t('field.addressName')} width="160" />
            <ColumnDirective field="address" headerText={t('field.address')} />
            <ColumnDirective field="city" headerText={t('field.city')} width="150" />
            <ColumnDirective field="phone" headerText={t('field.phone')} width="130" />
            <ColumnDirective field="email" headerText={t('field.email')} width="180" />
            <ColumnDirective field="isDefault" headerText={t('field.isDefaultAddress')} width="150" displayAsCheckBox type="boolean" />
            <ColumnDirective field="isActive" headerText={t('common.active')} width="90" displayAsCheckBox type="boolean" />
          </ColumnsDirective>
        </GridComponent>
      )}
      {selected !== undefined && (
        <FieldIdPrefix.Provider value="addr">
          <form className="subform" noValidate onSubmit={(e) => void submit(e)} aria-label={t('address.edit')}>
            <ErrorBanner message={error} />
            <div className="grid g3">
              <SelectField control={form.control} name="addressType" label={t('field.addressType')} options={addressTypeOptions} numeric />
              <TextField control={form.control} name="name" label={t('field.addressName')} className="span2" />
              <TextField control={form.control} name="address" label={t('field.address')} className="span2" />
              <SelectField control={form.control} name="countryCode" label={t('field.country')} options={countryOptions(lookups)} />
              <SelectField control={form.control} name="countyCode" label={t('field.county')} options={countyOptions(lookups, country)} allowEmpty={t('common.none')} />
              <TextField control={form.control} name="city" label={t('field.city')} />
              <TextField control={form.control} name="postalCode" label={t('field.postalCode')} />
              <TextField control={form.control} name="phone" label={t('field.phone')} />
              <TextField control={form.control} name="email" label={t('field.email')} />
              <SwitchField control={form.control} name="isDefault" label={t('field.isDefaultAddress')} />
              <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
            </div>
            <div className="actions end">
              <button type="button" className="btn ghost" onClick={() => setSelected(undefined)}>
                {t('common.cancel')}
              </button>
              <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>
                {t('common.save')}
              </button>
            </div>
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

/** Persoanele de contact (ale firmei, ale unui punct de lucru sau ale unei locații): cel putin un telefon sau un e-mail. */
export function ContactsSection({ companyId, contacts, canManage, branchId, locationId }: {
  readonly companyId: number;
  readonly contacts: readonly CompanyContact[];
  readonly canManage: boolean;
  readonly branchId?: number | undefined;
  readonly locationId?: number | undefined;
}) {
  const [selected, setSelected] = useState<CompanyContact | 'new' | undefined>();
  const contact = selected === 'new' ? undefined : selected;
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
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
  const onRowSelected = useCallback((args: { data: CompanyContact }) => {
    if (canManage) {
      setSelected(args.data);
    }
  }, [canManage]);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      await orgApi.saveContact({ ...values, id: contact?.id ?? null, companyId, branchId: branchId ?? null, locationId: locationId ?? null,
        rowVersion: contact?.rowVersion ?? null });
      form.reset(form.getValues());
      guard.markClean();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: orgKeys.company(companyId) }),
        queryClient.invalidateQueries({ queryKey: orgKeys.tree }),
      ]);
      notify(t('common.saved'));
      setSelected(undefined);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  return (
    <div className="tab-body">
      <div className="toolbar">
        {canManage && (
          <button type="button" className="btn ghost" onClick={() => setSelected('new')}>
            {t('contact.add')}
          </button>
        )}
      </div>
      {contacts.length === 0 ? (
        <p className="muted">{t('contact.empty')}</p>
      ) : (
        <GridComponent dataSource={contacts as CompanyContact[]} rowSelected={onRowSelected} allowSorting height="auto">
          <ColumnsDirective>
            <ColumnDirective field="name" headerText={t('field.contactName')} width="200" />
            <ColumnDirective field="role" headerText={t('field.contactRole')} width="160" />
            <ColumnDirective field="phone" headerText={t('field.phone')} width="130" />
            <ColumnDirective field="email" headerText={t('field.email')} width="240" />
            <ColumnDirective field="isActive" headerText={t('common.active')} width="90" displayAsCheckBox type="boolean" />
          </ColumnsDirective>
        </GridComponent>
      )}
      {selected !== undefined && (
        <FieldIdPrefix.Provider value="contact">
          <form className="subform" noValidate onSubmit={(e) => void submit(e)} aria-label={t('contact.edit')}>
            <ErrorBanner message={error} />
            <div className="grid g3">
              <TextField control={form.control} name="name" label={t('field.contactName')} className="span2" />
              <TextField control={form.control} name="role" label={t('field.contactRole')} />
              <TextField control={form.control} name="phone" label={t('field.phone')} />
              <TextField control={form.control} name="email" label={t('field.email')} className="span2" />
              <TextField control={form.control} name="notes" label={t('field.notes')} className="span3" />
              <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
            </div>
            <div className="actions end">
              <button type="button" className="btn ghost" onClick={() => setSelected(undefined)}>
                {t('common.cancel')}
              </button>
              <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>
                {t('common.save')}
              </button>
            </div>
          </form>
        </FieldIdPrefix.Provider>
      )}
    </div>
  );
}

export function ContactsTab({ company, canManage }: { readonly company: CompanyDetail; readonly canManage: boolean }) {
  return <ContactsSection companyId={company.id} contacts={company.contacts} canManage={canManage} />;
}

/** Seriile firmei, doar citire; administrarea e in Configurare › Serii (macheta 07c). */
export function CompanySeriesTab({ companyId, canManage }: { readonly companyId: number; readonly canManage: boolean }) {
  const series = useNumberSeries(canManage ? companyId : undefined);
  if (!canManage) {
    return null;
  }
  return (
    <div className="tab-body">
      <div className="toolbar">
        <Link to={`/admin/config/serii?firma=${companyId}`}>{t('series.goToConfig')}</Link>
      </div>
      {/* Eroarea de incarcare se arata, nu se inghite (AGENTS.md). */}
      {series.isError && series.data === undefined && (
        <div className="banner error" role="alert">
          {t('app.loadError')}{' '}
          <button type="button" className="btn ghost" onClick={() => void series.refetch()}>
            {t('app.retry')}
          </button>
        </div>
      )}
      {series.data !== undefined && series.data.length === 0 && <p className="muted">{t('series.empty')}</p>}
      {series.data !== undefined && series.data.length > 0 && (
        <GridComponent dataSource={series.data} allowSorting height="auto">
          <ColumnsDirective>
            <ColumnDirective field="documentTypeName" headerText={t('series.documentType')} />
            <ColumnDirective field="branchName" headerText={t('series.branch')} />
            <ColumnDirective field="prefix" headerText={t('series.prefix')} width="100" />
            <ColumnDirective field="year" headerText={t('series.year')} width="80" />
            <ColumnDirective field="usedCount" headerText={t('series.used')} width="100" textAlign="Right" format="N0" />
          </ColumnsDirective>
        </GridComponent>
      )}
    </div>
  );
}
