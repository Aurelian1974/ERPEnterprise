import { useEffect, useMemo, useState } from 'react';
import { OWNER_COMMON, ownerChoices, ownerFields, ownerKey, useOwnerOptions } from '../org/owner';
import { Controller, useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { ApiError } from '../api/problem';
import { ErrorBanner, MultiSelectField, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, optionalEmail, optionalPhone, optionalText, required, ROMANIA } from '../components/forms';
import { formatDate } from '../format/date';
import { t, type TextKey } from '../i18n';
import { countryOptions, countyOptions, useCountyFollowsCountry } from '../org/OrgForms';
import type { OrgLookups } from '../org/orgApi';
import { DataType, PartnerType, partnerApi, partnerKeys, type AnafCompany, type PartnerDetail, type PartnerLookups, type PropertyDefinition, type Saved } from './partnerApi';
import { anafToForm, applies, fromFormProps, propError, propKey, toFormProps, type PropInput } from './partnerModel';

const propValue = z.union([z.string(), z.boolean(), z.array(z.string()), z.null()]);

function partnerSchema(definitions: readonly PropertyDefinition[], isNew: boolean) {
  return z
    .object({
      code: optionalText,
      name: required.max(200),
      partnerType: z.number().int().min(1).max(4),
      fiscalCode: optionalText,
      isCustomer: z.boolean(),
      isSupplier: z.boolean(),
      partnerGroupId: z.number().nullable(),
      vatPayer: z.boolean(),
      vatOnCollection: z.boolean(),
      anafInactive: z.boolean(),
      tradeRegisterNo: optionalText,
      address: required.max(300),
      countryCode: required,
      countyCode: optionalText,
      city: required.max(100),
      postalCode: optionalText,
      phone: optionalPhone,
      email: optionalEmail,
      intraGroupCompanyId: z.number().nullable(),
      isActive: z.boolean(),
      owner: z.string(),
      changePersonalId: z.boolean(),
      personalId: optionalText,
      anafCheckedAt: z.string().nullable(),
      props: z.record(z.string(), propValue),
    })
    .superRefine((v, ctx) => {
      if (v.code === null ? !isNew : !/^[A-Za-z0-9][A-Za-z0-9._/-]{0,29}$/.test(v.code)) {
        ctx.addIssue({ code: 'custom', path: ['code'], message: v.code === null ? t('common.required') : t('validation.partnerCode') });
      }
      if (!v.isCustomer && !v.isSupplier) {
        ctx.addIssue({ code: 'custom', path: ['isCustomer'], message: t('validation.role') });
      }
      if (v.partnerType !== PartnerType.Individual && v.fiscalCode === null) {
        ctx.addIssue({ code: 'custom', path: ['fiscalCode'], message: t('common.required') });
      }
      if (v.partnerType === PartnerType.Individual && v.changePersonalId && v.personalId !== null && !/^\d{13}$/.test(v.personalId.replace(/ /g, ''))) {
        ctx.addIssue({ code: 'custom', path: ['personalId'], message: t('validation.personalId') });
      }
      if (v.countryCode === ROMANIA && v.countyCode === null) {
        ctx.addIssue({ code: 'custom', path: ['countyCode'], message: t('validation.countyRequired') });
      }
      for (const definition of definitions) {
        const message = propError(definition, v.props[propKey(definition.id)], applies(definition, v.isCustomer, v.isSupplier));
        if (message !== undefined) {
          ctx.addIssue({ code: 'custom', path: ['props', propKey(definition.id)], message });
        }
      }
    });
}
type Schema = ReturnType<typeof partnerSchema>;
type PartnerInput = z.input<Schema>;
type PartnerValues = z.output<Schema>;

const fields = ['code', 'name', 'partnerType', 'fiscalCode', 'isCustomer', 'isSupplier', 'partnerGroupId', 'vatPayer', 'vatOnCollection', 'anafInactive',
  'tradeRegisterNo', 'address', 'countryCode', 'countyCode', 'city', 'postalCode', 'phone', 'email', 'intraGroupCompanyId', 'isActive', 'personalId'];

function defaultsFor(partner: PartnerDetail | undefined, definitions: readonly PropertyDefinition[]): PartnerInput {
  if (partner === undefined) {
    return {
      code: null, name: '', partnerType: PartnerType.LegalRomania, fiscalCode: null, isCustomer: true, isSupplier: false, partnerGroupId: null,
      vatPayer: false, vatOnCollection: false, anafInactive: false, tradeRegisterNo: null, address: '', countryCode: ROMANIA, countyCode: null,
      city: '', postalCode: null, phone: null, email: null, intraGroupCompanyId: null, isActive: true, owner: OWNER_COMMON, changePersonalId: true, personalId: null,
      anafCheckedAt: null, props: toFormProps(definitions, []),
    };
  }
  return {
    code: partner.code, name: partner.name, partnerType: partner.partnerType, fiscalCode: partner.fiscalCode ?? null, isCustomer: partner.isCustomer,
    isSupplier: partner.isSupplier, partnerGroupId: partner.partnerGroupId ?? null, vatPayer: partner.vatPayer, vatOnCollection: partner.vatOnCollection,
    anafInactive: partner.anafInactive, tradeRegisterNo: partner.tradeRegisterNo ?? null, address: partner.address, countryCode: partner.countryCode,
    countyCode: partner.countyCode ?? null, city: partner.city, postalCode: partner.postalCode ?? null, phone: partner.phone ?? null,
    email: partner.email ?? null, intraGroupCompanyId: partner.intraGroupCompanyId ?? null, isActive: partner.isActive,
    owner: ownerKey(partner.ownerLevel, partner.ownerNodeId),
    changePersonalId: !partner.hasPersonalId, personalId: null, anafCheckedAt: null, props: toFormProps(definitions, partner.properties),
  };
}

/** Erorile serverului pe proprietati vin ca „properties.12”; in formular campul e „props.p12”. */
function remapPropertyErrors(error: unknown): unknown {
  if (!(error instanceof ApiError)) {
    return error;
  }
  const remapped: Record<string, readonly string[]> = {};
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const match = /^properties\.(\d+)$/.exec(field);
    remapped[match === null ? field : `props.${propKey(Number(match[1]))}`] = messages;
  }
  return new ApiError(error.status, { title: error.message, code: error.code, errors: remapped });
}

export type PartnerSection = 'general' | 'properties';

/**
 * Fisa partenerului, tab-urile Date generale si Proprietati (macheta 04/04b), intr-un singur formular cu o singura salvare:
 * proprietatile se salveaza impreuna cu partenerul, cu aceeasi versiune (RowVersion). `section` alege partea vizibila;
 * undefined = formularul ramane montat, ascuns, cat utilizatorul e pe alt tab (UI-18).
 */
export function PartnerForm({ partner, lookups, orgLookups, section, canManage, canViewPersonalData, onSaved, onCancel }: {
  readonly partner: PartnerDetail | undefined;
  readonly lookups: PartnerLookups;
  readonly orgLookups: OrgLookups;
  readonly section: PartnerSection | undefined;
  readonly canManage: boolean;
  readonly canViewPersonalData: boolean;
  readonly onSaved: (saved: Saved) => void;
  readonly onCancel: () => void;
}) {
  const definitions = lookups.properties;
  const isNew = partner === undefined;
  const schema = useMemo(() => partnerSchema(definitions, isNew), [definitions, isNew]);
  const defaults = useMemo(() => defaultsFor(partner, definitions), [partner, definitions]);
  const form = useForm<PartnerInput, unknown, PartnerValues>({ resolver: zodResolver(schema) as Resolver<PartnerInput, unknown, PartnerValues>, defaultValues: defaults });
  const ownerOptions = useOwnerOptions('Partner');
  const owners = useMemo(
    () => ownerChoices(ownerOptions.data, partner === undefined ? undefined
      : { key: ownerKey(partner.ownerLevel, partner.ownerNodeId), name: partner.ownerName ?? null }),
    [ownerOptions.data, partner],
  );
  // Un partener nou primeste primul proprietar permis (comun doar cu dreptul pe toata firma).
  useEffect(() => {
    if (partner === undefined && owners.length > 0 && !owners.some((o) => o.value === form.getValues('owner'))) {
      form.setValue('owner', owners[0]?.value ?? OWNER_COMMON);
    }
  }, [form, partner, owners]);
  // Dupa salvare sau reincarcare (alt RowVersion) formularul preia datele serverului.
  useEffect(() => form.reset(defaults), [partner?.id, partner?.rowVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const [revealed, setRevealed] = useState<string | null | undefined>();
  const [anaf, setAnaf] = useState<AnafCompany | undefined>();
  const [anafBusy, setAnafBusy] = useState(false);
  const guard = useUnsavedChanges(form.formState.isDirty);
  const country = useCountyFollowsCountry(form as never, orgLookups);
  const [partnerType, vatPayer, isCustomer, isSupplier, changePersonalId] = useWatch({
    control: form.control,
    name: ['partnerType', 'vatPayer', 'isCustomer', 'isSupplier', 'changePersonalId'],
  });

  // Tipul conduce tara si codurile: juridica RO are sediul in Romania; persoana fizica nu are CUI; TVA la incasare cere platitor.
  useEffect(() => {
    const options = { shouldDirty: true } as const;
    if (partnerType === PartnerType.LegalRomania && form.getValues('countryCode') !== ROMANIA) {
      form.setValue('countryCode', ROMANIA, options);
    }
    if ((partnerType === PartnerType.LegalEu || partnerType === PartnerType.LegalNonEu) && form.getValues('countryCode') === ROMANIA) {
      form.setValue('countryCode', '', options);
    }
    if (partnerType === PartnerType.Individual && form.getValues('fiscalCode') !== null) {
      form.setValue('fiscalCode', null, options);
    }
  }, [form, partnerType]);
  useEffect(() => {
    if (!vatPayer && form.getValues('vatOnCollection')) {
      form.setValue('vatOnCollection', false, { shouldDirty: true });
    }
  }, [form, vatPayer]);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    const { props, owner, ...rest } = values;
    const individual = rest.partnerType === PartnerType.Individual;
    try {
      const saved = await partnerApi.save({
        ...rest,
        id: partner?.id ?? null,
        rowVersion: partner?.rowVersion ?? null,
        ...ownerFields(owner),
        fiscalCode: individual ? null : rest.fiscalCode,
        changePersonalId: individual && rest.changePersonalId,
        personalId: individual && rest.changePersonalId ? rest.personalId : null,
        properties: fromFormProps(definitions, props as Record<string, PropInput>),
      });
      form.reset(form.getValues());
      guard.markClean();
      setRevealed(undefined);
      setAnaf(undefined);
      await queryClient.invalidateQueries({ queryKey: partnerKeys.all });
      notify(t('common.saved'));
      onSaved(saved);
    } catch (e) {
      const propFields = definitions.map((d) => `props.${propKey(d.id)}`);
      const message = applyServerErrors(remapPropertyErrors(e), form.setError, [...fields, ...propFields]);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  /** „Preia de la ANAF” (02.01, 11): completeaza formularul cu datele ANAF; utilizatorul le verifica si salveaza fisa. */
  async function fetchAnaf() {
    const fiscalCode = form.getValues('fiscalCode');
    if (fiscalCode === null || fiscalCode.trim() === '') {
      form.setError('fiscalCode', { type: 'manual', message: t('partner.anaf.needCui') });
      return;
    }
    setAnafBusy(true);
    setError(undefined);
    try {
      const company = await partnerApi.lookupAnaf(fiscalCode.trim());
      for (const [field, value] of Object.entries(anafToForm(company, orgLookups.counties))) {
        form.setValue(field as keyof PartnerInput, value as never, { shouldDirty: true, shouldValidate: form.formState.isSubmitted });
      }
      form.clearErrors('fiscalCode');
      setAnaf(company);
      notify(t('partner.anaf.filled'));
    } catch (e) {
      const message = applyServerErrors(e, form.setError, ['fiscalCode']);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    } finally {
      setAnafBusy(false);
    }
  }

  async function reveal() {
    if (partner === undefined) {
      return;
    }
    try {
      setRevealed(await partnerApi.revealPersonalId(partner.id));
      await queryClient.invalidateQueries({ queryKey: partnerKeys.history(partner.id) });
    } catch (e) {
      notify(e instanceof Error ? e.message : t('app.loadError'), 'error');
    }
  }

  const readOnly = !canManage;
  const groups = useMemo<Option[]>(
    () => lookups.groups.filter((g) => g.isActive || g.id === partner?.partnerGroupId).map((g) => ({ value: String(g.id), text: g.name })),
    [lookups.groups, partner?.partnerGroupId],
  );
  const companies = useMemo<Option[]>(() => lookups.companies.map((c) => ({ value: String(c.id), text: c.name })), [lookups.companies]);
  const individual = partnerType === PartnerType.Individual;
  const info = partner === undefined ? undefined : t('common.lastChanged', { name: partner.modifiedByName, date: formatDate(new Date(partner.lastChangedAt)) });

  return (
    <form className="node-form partner-form" noValidate hidden={section === undefined} onSubmit={(e) => void submit(e)}
      aria-label={partner?.name ?? t('partner.newTitle')}>
      {isNew && <h2 className="form-title">{t('partner.newTitle')}</h2>}
      <ErrorBanner message={error} />
      <fieldset className="form-body plain" disabled={readOnly} hidden={section !== 'general'}>
        <Controller
          control={form.control}
          name="partnerType"
          render={({ field }) => (
            <div className="field">
              <span className="label" id="partner-type-label">{t('partner.field.type')} *</span>
              <div className="segmented" role="radiogroup" aria-labelledby="partner-type-label">
                {([1, 2, 3, 4] as const).map((type) => (
                  <button key={type} type="button" role="radio" aria-checked={field.value === type} className={field.value === type ? 'on' : ''}
                    disabled={readOnly} onClick={() => field.onChange(type)}>
                    {t(`partner.type.${type}`)}
                  </button>
                ))}
              </div>
            </div>
          )}
        />
        <RoleField form={form} readOnly={readOnly} />
        <div className="grid g3">
          {individual ? (
            <PersonalIdField form={form} partner={partner} change={changePersonalId} revealed={revealed} readOnly={readOnly}
              canView={canViewPersonalData} onReveal={() => void reveal()} />
          ) : (
            <TextField control={form.control} name="fiscalCode" label={`${t(`partner.field.fiscalCode.${partnerType as 1 | 2 | 3}`)} *`} upper
              hint={partnerType === PartnerType.LegalRomania && canManage ? (
                <button type="button" className="btn ghost small anaf-fetch" disabled={anafBusy} onClick={() => void fetchAnaf()}
                  title={t('partner.anaf.fetch')} aria-label={t('partner.anaf.fetch')}>
                  {anafBusy ? '…' : 'ANAF ⟳'}
                </button>
              ) : undefined} />
          )}
          <TextField control={form.control} name="name" label={`${t(individual ? 'partner.field.individualName' : 'partner.field.name')} *`} className="span2" />
          <TextField control={form.control} name="code" label={isNew ? t('partner.field.codeAuto') : `${t('partner.field.code')} *`} upper />
          {!individual && <TextField control={form.control} name="tradeRegisterNo" label={t('field.tradeRegisterNo')} upper />}
          <SelectField control={form.control} name="partnerGroupId" label={t('partner.field.group')} options={groups} allowEmpty={t('partner.field.noGroup')} numeric />
        </div>
        {!individual && (
          <div className="anaf-box">
            <div className="switches">
              <SwitchField control={form.control} name="vatPayer" label={t('field.vatPayer')} />
              <SwitchField control={form.control} name="vatOnCollection" label={t('field.vatOnCollection')} disabled={!vatPayer} />
              <SwitchField control={form.control} name="anafInactive" label={t('partner.field.anafInactive')} />
            </div>
            {anaf !== undefined && (
              <div className="chips-row" aria-live="polite">
                <span className={anaf.eInvoiceRegistered ? 'pill ok' : 'pill off'}>{t(anaf.eInvoiceRegistered ? 'partner.anaf.eInvoice' : 'partner.anaf.noEInvoice')}</span>
                {anaf.splitVat && <span className="pill warn">{t('partner.anaf.splitVat')}</span>}
                {anaf.registrationState != null && <span className="pill off">{t('partner.anaf.state', { state: anaf.registrationState })}</span>}
              </div>
            )}
            <p className="muted small">
              {anaf !== undefined ? t('partner.anaf.pendingCheck')
                : partner?.anafCheckedAt == null ? t('partner.section.anafHint')
                  : t('partner.section.anafChecked', { date: formatDate(new Date(partner.anafCheckedAt)) })}
            </p>
          </div>
        )}
        <h3 className="section">{t('section.registeredOffice')}</h3>
        <div className="grid g3">
          <TextField control={form.control} name="address" label={`${t('field.address')} *`} className="span2" />
          <TextField control={form.control} name="postalCode" label={t('field.postalCode')} />
          <SelectField control={form.control} name="countryCode" label={`${t('field.country')} *`} options={countryOptions(orgLookups)}
            disabled={partnerType === PartnerType.LegalRomania} />
          <SelectField control={form.control} name="countyCode" label={t('field.county')} options={countyOptions(orgLookups, country)} allowEmpty={t('common.none')} />
          <TextField control={form.control} name="city" label={`${t('field.city')} *`} />
        </div>
        <h3 className="section">{t('partner.section.other')}</h3>
        <div className="grid g3">
          <TextField control={form.control} name="phone" label={t('field.phone')} />
          <TextField control={form.control} name="email" label={t('field.email')} />
          <SelectField control={form.control} name="intraGroupCompanyId" label={t('partner.field.intraGroup')} options={companies}
            allowEmpty={t('partner.field.noIntraGroup')} numeric />
          <SelectField control={form.control} name="owner" label={t('owner.field')} options={owners} />
          <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
        </div>
        <p className="muted small">{t('owner.hint')}</p>
      </fieldset>
      <fieldset className="form-body plain" disabled={readOnly} hidden={section !== 'properties'}>
        <PropertiesSection form={form} definitions={definitions} lookups={lookups} isCustomer={isCustomer} isSupplier={isSupplier} />
      </fieldset>
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

type PartnerFormApi = ReturnType<typeof useForm<PartnerInput, unknown, PartnerValues>>;

/** Rolul de baza (client, furnizor sau ambele; 02.01, 3.2): cel putin unul, verificat si in baza (CK_Partner_Role). */
function RoleField({ form, readOnly }: { readonly form: PartnerFormApi; readonly readOnly: boolean }) {
  const roleError = form.formState.errors.isCustomer?.message;
  return (
    <div className="field">
      <span className="label" id="partner-role-label">{t('partner.field.role')} *</span>
      <div className="role-row" role="group" aria-labelledby="partner-role-label" aria-describedby={roleError === undefined ? undefined : 'partner-role-error'}>
        {(['isCustomer', 'isSupplier'] as const).map((name) => (
          <Controller
            key={name}
            control={form.control}
            name={name}
            render={({ field }) => (
              <label className={field.value === true ? 'role-toggle on' : 'role-toggle'}>
                <input type="checkbox" checked={field.value === true} disabled={readOnly}
                  onChange={(e) => {
                    field.onChange(e.target.checked);
                    if (form.formState.isSubmitted) {
                      void form.trigger('isCustomer');
                    }
                  }} />
                {t(name === 'isCustomer' ? 'partner.role.customer' : 'partner.role.supplier')}
              </label>
            )}
          />
        ))}
      </div>
      {roleError !== undefined && (
        <span className="field-error" id="partner-role-error" role="alert">
          {roleError}
        </span>
      )}
    </div>
  );
}

/**
 * CNP-ul persoanei fizice (GDPR, A13): nu vine cu fisa; se afiseaza la cerere, doar cu Core.Partner.ViewPersonalData, iar
 * afisarea se jurnalizeaza. La modificare se scrie unul nou (criptat pe server); altfel ramane cel existent.
 */
function PersonalIdField({ form, partner, change, revealed, readOnly, canView, onReveal }: {
  readonly form: PartnerFormApi;
  readonly partner: PartnerDetail | undefined;
  readonly change: boolean;
  readonly revealed: string | null | undefined;
  readonly readOnly: boolean;
  readonly canView: boolean;
  readonly onReveal: () => void;
}) {
  const existing = partner?.hasPersonalId === true;
  if (change || !existing) {
    return (
      <TextField control={form.control} name="personalId" label={t('partner.field.personalId')}
        hint={existing && !readOnly ? (
          <button type="button" className="btn ghost small" onClick={() => {
            form.setValue('changePersonalId', false, { shouldDirty: true });
            form.setValue('personalId', null, { shouldDirty: true });
          }}>
            {t('partner.personalId.keep')}
          </button>
        ) : undefined} />
    );
  }
  return (
    <div className="field">
      <span className="label">{t('partner.field.personalId')}</span>
      <div className="field-row">
        <span className="readonly">{revealed ?? t('partner.personalId.hidden')}</span>
        {canView && revealed === undefined && (
          <button type="button" className="btn ghost small" onClick={onReveal} title={t('partner.personalId.hint')}>
            {t('partner.personalId.show')}
          </button>
        )}
        {!readOnly && (
          <button type="button" className="btn ghost small" onClick={() => form.setValue('changePersonalId', true, { shouldDirty: true })}>
            {t('partner.personalId.change')}
          </button>
        )}
      </div>
    </div>
  );
}

const roleKey: Record<number, TextKey> = { 1: 'partner.properties.forCustomer', 2: 'partner.properties.forSupplier', 3: 'partner.properties.forBoth' };

/** Proprietatile configurabile (macheta 04b), grupate pe sectiuni; apar doar cele valabile pentru rolul partenerului. */
function PropertiesSection({ form, definitions, lookups, isCustomer, isSupplier }: {
  readonly form: PartnerFormApi;
  readonly definitions: readonly PropertyDefinition[];
  readonly lookups: PartnerLookups;
  readonly isCustomer: boolean;
  readonly isSupplier: boolean;
}) {
  const visible = definitions.filter((d) => applies(d, isCustomer, isSupplier));
  const sections = [...new Set(visible.map((d) => d.section))];
  const users = useMemo<Option[]>(() => lookups.users.map((u) => ({ value: String(u.id), text: u.displayName })), [lookups.users]);
  return (
    <>
      <div className="banner info">{t('partner.properties.hint')}</div>
      {visible.length === 0 && <p className="muted">{t('partner.properties.empty')}</p>}
      {sections.map((section) => {
        const items = visible.filter((d) => d.section === section);
        const roles = new Set(items.map((d) => d.appliesTo));
        const role = roles.size === 1 ? roleKey[items[0]?.appliesTo ?? 3] : undefined;
        return (
          <section key={section} aria-label={section}>
            <h3 className="section">{role === undefined ? section : t('partner.properties.section', { section, role: t(role) })}</h3>
            <div className="grid g3">
              {items.map((definition) => (
                <PropertyField key={definition.id} form={form} definition={definition} lookups={lookups} users={users} />
              ))}
            </div>
          </section>
        );
      })}
    </>
  );
}

function PropertyField({ form, definition, lookups, users }: {
  readonly form: PartnerFormApi;
  readonly definition: PropertyDefinition;
  readonly lookups: PartnerLookups;
  readonly users: readonly Option[];
}) {
  const name = `props.${propKey(definition.id)}` as const;
  const label = definition.isRequired ? `${definition.name} *` : definition.name;
  const control = form.control;
  const options = useMemo<Option[]>(
    () => lookups.options.filter((o) => o.propertyDefinitionId === definition.id && o.isActive).map((o) => ({ value: String(o.id), text: o.label })),
    [lookups.options, definition.id],
  );
  switch (definition.dataType) {
    case DataType.Bool:
      return <SwitchField control={control} name={name} label={label} />;
    case DataType.List:
      return <SelectField control={control} name={name} label={label} options={options} allowEmpty={t('common.none')} />;
    case DataType.MultiList:
      return <MultiSelectField control={control} name={name} label={label} options={options} />;
    case DataType.User:
      return <SelectField control={control} name={name} label={label} options={users} allowEmpty={t('common.none')} />;
    case DataType.Date:
      return <TextField control={control} name={name} label={`${label} (zz.ll.aaaa)`} />;
    default:
      return <TextField control={control} name={name} label={label} />;
  }
}
