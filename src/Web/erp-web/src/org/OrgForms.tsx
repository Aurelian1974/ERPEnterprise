import { createContext, useContext, useEffect, useId, useState, type ReactNode } from 'react';
import { useForm, useWatch, type DefaultValues, type FieldValues, type Path, type PathValue, type Resolver, type UseFormReturn } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { ErrorBanner, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, code, money, moneyForInput, optionalEmail, optionalPhone, optionalText, required, requireCountyForRomania, ROMANIA } from '../components/forms';
import { formatDate } from '../format/date';
import { t } from '../i18n';
import { orgApi, orgKeys, type CompanyDetail, type OrgLookups, type Saved } from './orgApi';
import type { OrgTree } from './orgTree';

type Lookups = OrgLookups;

export const countryOptions = (lookups: Lookups): Option[] => lookups.countries.map((c) => ({ value: c.code, text: c.name }));
export const countyOptions = (lookups: Lookups, country: string | null): Option[] =>
  lookups.counties.filter((c) => c.countryCode === country).map((c) => ({ value: c.code, text: c.name }));

/** Judetul ales apartine tarii? Un judet gol e mereu in regula (obligativitatea e regula separata, A3). */
export function countyBelongsTo(lookups: Pick<Lookups, 'counties'>, country: string | null, county: string | null): boolean {
  return county === null || county === '' || lookups.counties.some((c) => c.code === county && c.countryCode === country);
}

/**
 * Judetul urmeaza tara (A1): la schimbarea tarii, un judet care nu mai e in lista ei se goleste. Lista de selectie isi
 * schimba doar valoarea la alegerea utilizatorului, deci altfel judetul vechi ramanea ascuns in formular si salvarea
 * pica pe server cu un mesaj fara legatura cu ce facuse utilizatorul.
 */
export function useCountyFollowsCountry<T extends FieldValues & { countryCode: string | null; countyCode: string | null }>(form: UseFormReturn<T, unknown, unknown>, lookups: Lookups) {
  const country = useWatch({ control: form.control, name: 'countryCode' as Path<T> }) as string | null;
  const county = useWatch({ control: form.control, name: 'countyCode' as Path<T> }) as string | null;
  useEffect(() => {
    if (!countyBelongsTo(lookups, country, county)) {
      form.setValue('countyCode' as Path<T>, null as PathValue<T, Path<T>>, { shouldDirty: true, shouldValidate: form.formState.isSubmitted });
    }
  }, [form, lookups, country, county]);
  return country;
}

/**
 * Salvarea comuna: erorile pe camp merg la campuri, restul in banner; dupa salvare se reincarca arborele si fisa.
 * Dupa o salvare reusita formularul se marcheaza curat inainte de onSaved, care navigheaza spre inregistrarea salvata:
 * altfel garda modificarilor nesalvate (UI-18) ar opri chiar navigarea de dupa salvare.
 */
function useSave<TInput extends FieldValues, TOutput>(
  form: UseFormReturn<TInput, unknown, TOutput>,
  fields: readonly string[],
  save: (values: TOutput) => Promise<Saved>,
  onSaved: (saved: Saved) => void,
  companyId?: number,
) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const guard = useUnsavedChanges(form.formState.isDirty);
  return {
    error,
    /** Validarea din browser a esuat: campurile sunt marcate, iar mesajul confirma ca nu s-a salvat nimic. */
    invalid: () => notify(t('common.notSaved'), 'error'),
    async submit(values: TOutput) {
      setError(undefined);
      try {
        const saved = await save(values);
        form.reset(form.getValues());
        guard.markClean();
        await queryClient.invalidateQueries({ queryKey: orgKeys.tree });
        await queryClient.invalidateQueries({ queryKey: ['core', 'me'] });
        if (companyId !== undefined) {
          await queryClient.invalidateQueries({ queryKey: orgKeys.company(companyId) });
        }
        notify(t('common.saved'));
        onSaved(saved);
      } catch (e) {
        const message = applyServerErrors(e, form.setError, fields);
        setError(message);
        notify(message ?? t('common.notSaved'), 'error');
      }
    },
  };
}

const FormIdContext = createContext<string | undefined>(undefined);

/** `after` (ex. sectiunea de contacte, cu propriile formulare) sta in afara <form>, deasupra butoanelor Anuleaza/Salveaza. */
function FormShell({ title, error, footer, onSubmit, after, children }: { title?: ReactNode; error: string | undefined; footer: ReactNode; onSubmit: () => void; after?: ReactNode; children: ReactNode }) {
  const formId = useId();
  return (
    <FormIdContext.Provider value={formId}>
      <div className="node-form">
        <form
          id={formId}
          className="node-form"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            onSubmit();
          }}
        >
          {title}
          <ErrorBanner message={error} />
          <div className="form-body">{children}</div>
        </form>
        {after}
        {footer}
      </div>
    </FormIdContext.Provider>
  );
}

function Footer({ info, onCancel, submitting }: { info?: string | undefined; onCancel: () => void; submitting: boolean }) {
  const readOnly = useContext(OrgReadOnly);
  const formId = useContext(FormIdContext);
  return (
    <div className="form-footer">
      <span className="muted">{info}</span>
      {!readOnly && (
        <div className="actions">
          <button type="button" className="btn ghost" onClick={onCancel}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn primary" disabled={submitting}>
            {t('common.save')}
          </button>
        </div>
      )}
    </div>
  );
}

/** Fara Core.Company.Manage fisele sunt doar pentru citire (serverul refuza oricum salvarea, 4.1). */
export const OrgReadOnly = createContext(false);

// ---------- Grup ----------

const groupSchema = z.object({
  code,
  name: required.max(200),
  parentGroupId: z.number().nullable(),
  parentCompanyId: z.number().nullable(),
  isActive: z.boolean(),
});
type GroupValues = z.infer<typeof groupSchema>;

export function GroupForm({ tree, groupId, parentGroupId, onSaved, onCancel }: {
  readonly tree: OrgTree;
  readonly groupId: number | undefined;
  readonly parentGroupId?: number | undefined;
  readonly onSaved: (saved: Saved) => void;
  readonly onCancel: () => void;
}) {
  const group = tree.groups.find((g) => g.id === groupId);
  const defaults: GroupValues = group === undefined
    ? { code: '', name: '', parentGroupId: parentGroupId ?? null, parentCompanyId: null, isActive: true }
    : { code: group.code, name: group.name, parentGroupId: group.parentGroupId, parentCompanyId: group.parentCompanyId, isActive: group.isActive };
  const form = useForm<GroupValues>({ resolver: zodResolver(groupSchema), defaultValues: defaults });
  useEffect(() => form.reset(defaults), [groupId]); // eslint-disable-line react-hooks/exhaustive-deps
  const save = useSave(form, Object.keys(defaults), (v) => orgApi.saveGroup({ ...v, id: group?.id ?? null, rowVersion: group?.rowVersion ?? null }), onSaved);

  const groups = tree.groups.filter((g) => g.id !== groupId).map((g) => ({ value: String(g.id), text: g.name }));
  const companies = tree.companies.filter((c) => c.groupId === groupId).map((c) => ({ value: String(c.id), text: c.name }));

  return (
    <FormShell
      title={<h2 className="form-title">{group?.name ?? t('org.newGroup')}</h2>}
      error={save.error}
      onSubmit={form.handleSubmit((v) => save.submit(v), save.invalid)}
      footer={<Footer onCancel={() => (group === undefined ? onCancel() : form.reset(defaults))} submitting={form.formState.isSubmitting} />}
    >
      <div className="grid g3">
        <TextField control={form.control} name="code" label={t('field.code')} upper />
        <TextField control={form.control} name="name" label={t('field.name')} className="span2" />
        <SelectField control={form.control} name="parentGroupId" label={t('field.parentGroup')} options={groups} allowEmpty={t('common.none')} numeric />
        <SelectField control={form.control} name="parentCompanyId" label={t('field.parentCompany')} options={companies} allowEmpty={t('common.none')} numeric
          disabled={group === undefined} />
        <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
      </div>
    </FormShell>
  );
}

// ---------- Firma ----------

export const companySchema = z
  .object({
    code,
    name: required.max(200),
    fiscalCode: required.max(20),
    tradeRegisterNo: required.max(30),
    shareCapital: money,
    groupId: z.number().nullable(),
    vatPayer: z.boolean(),
    vatOnCollection: z.boolean(),
    address: required.max(300),
    countryCode: required,
    countyCode: optionalText,
    city: required.max(100),
    postalCode: optionalText,
    phone: optionalPhone,
    email: optionalEmail,
    defaultPrecisionPolicyId: z.number({ message: t('common.required') }),
    includeInConsolidation: z.boolean(),
    isActive: z.boolean(),
  })
  .superRefine(requireCountyForRomania);
type CompanyInput = z.input<typeof companySchema>;
type CompanyValues = z.output<typeof companySchema>;

export function CompanyForm({ company, lookups, tree, groupId, onSaved, onCancel }: {
  readonly company: CompanyDetail | undefined;
  readonly lookups: Lookups;
  readonly tree: OrgTree;
  readonly groupId?: number | undefined;
  readonly onSaved: (saved: Saved) => void;
  readonly onCancel: () => void;
}) {
  const defaults: CompanyInput = company === undefined
    ? {
        code: '', name: '', fiscalCode: '', tradeRegisterNo: '', shareCapital: '', groupId: groupId ?? null, vatPayer: true, vatOnCollection: false,
        address: '', countryCode: ROMANIA, countyCode: null, city: '', postalCode: null, phone: null, email: null,
        defaultPrecisionPolicyId: lookups.precisionPolicies[0]?.id ?? 0, includeInConsolidation: true, isActive: true,
      }
    : {
        code: company.code, name: company.name, fiscalCode: (company.vatPayer ? 'RO' : '') + company.fiscalCode, tradeRegisterNo: company.tradeRegisterNo,
        shareCapital: moneyForInput(company.shareCapital), groupId: company.groupId, vatPayer: company.vatPayer, vatOnCollection: company.vatOnCollection,
        address: company.address, countryCode: company.countryCode, countyCode: company.countyCode, city: company.city, postalCode: company.postalCode,
        phone: company.phone ?? null, email: company.email ?? null,
        defaultPrecisionPolicyId: company.defaultPrecisionPolicyId, includeInConsolidation: company.includeInConsolidation, isActive: company.isActive,
      };
  const form = useForm<CompanyInput, unknown, CompanyValues>({
    resolver: zodResolver(companySchema) as Resolver<CompanyInput, unknown, CompanyValues>,
    defaultValues: defaults as DefaultValues<CompanyInput>,
  });
  useEffect(() => form.reset(defaults), [company?.id, company?.rowVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const country = useCountyFollowsCountry(form, lookups);
  const vatPayer = useWatch({ control: form.control, name: 'vatPayer' });
  // TVA la incasare se aplica doar platitorilor (A2): oprirea platitorului il opreste si pe el. Altfel ramanea pornit
  // pe un comutator dezactivat, iar serverul respingea salvarea pe un camp pe care utilizatorul nu-l mai putea schimba.
  useEffect(() => {
    if (!vatPayer && form.getValues('vatOnCollection')) {
      form.setValue('vatOnCollection', false, { shouldDirty: true });
    }
  }, [form, vatPayer]);
  const save = useSave(
    form,
    Object.keys(defaults),
    (v) => orgApi.saveCompany({ ...v, id: company?.id ?? null, rowVersion: company?.rowVersion ?? null }),
    onSaved,
    company?.id,
  );

  const info = company === undefined ? undefined : t('common.lastChanged', { name: company.modifiedByName, date: formatDate(new Date(company.lastChangedAt)) });
  const groups = tree.groups.filter((g) => g.isActive || g.id === company?.groupId).map((g) => ({ value: String(g.id), text: g.name }));

  return (
    <FormShell
      error={save.error}
      onSubmit={form.handleSubmit((v) => save.submit(v), save.invalid)}
      footer={<Footer info={info} onCancel={() => (company === undefined ? onCancel() : form.reset(defaults))} submitting={form.formState.isSubmitting} />}
    >
      {company === undefined && <h2 className="form-title">{t('org.newCompany')}</h2>}
      <div className="grid g3">
        <TextField control={form.control} name="code" label={t('field.code')} upper />
        <TextField control={form.control} name="name" label={t('field.legalName')} className="span2" />
        <TextField control={form.control} name="fiscalCode" label={t('field.fiscalCode')} upper />
        <TextField control={form.control} name="tradeRegisterNo" label={t('field.tradeRegisterNo')} upper />
        <TextField control={form.control} name="shareCapital" label={t('field.shareCapital')} />
        <SelectField control={form.control} name="groupId" label={t('field.group')} options={groups} allowEmpty={t('field.noGroup')} numeric />
        <SwitchField control={form.control} name="vatPayer" label={t('field.vatPayer')} />
        <SwitchField control={form.control} name="vatOnCollection" label={t('field.vatOnCollection')} disabled={!vatPayer} />
      </div>
      <h3 className="section">{t('section.registeredOffice')}</h3>
      <div className="grid g3">
        <TextField control={form.control} name="address" label={t('field.address')} className="span2" />
        <SelectField control={form.control} name="countryCode" label={t('field.country')} options={countryOptions(lookups)} />
        <SelectField control={form.control} name="countyCode" label={t('field.county')} options={countyOptions(lookups, country)} allowEmpty={t('common.none')} />
        <TextField control={form.control} name="city" label={t('field.city')} />
        <TextField control={form.control} name="postalCode" label={t('field.postalCode')} />
      </div>
      <h3 className="section">{t('section.contact')}</h3>
      <div className="grid g3">
        <TextField control={form.control} name="phone" label={t('field.phone')} />
        <TextField control={form.control} name="email" label={t('field.email')} />
      </div>
      <h3 className="section">{t('section.accounting')}</h3>
      <div className="grid g3">
        <div className="field">
          <span className="label">{t('field.functionalCurrency')}</span>
          <span className="readonly">RON</span>
        </div>
        <SelectField control={form.control} name="defaultPrecisionPolicyId" label={t('field.precisionPolicy')} numeric
          options={lookups.precisionPolicies.map((p) => ({ value: String(p.id), text: p.name }))} />
        <SwitchField control={form.control} name="includeInConsolidation" label={t('field.includeInConsolidation')} />
        <SwitchField control={form.control} name="isActive" label={t('field.isActive')} onText={t('common.activeF')} />
      </div>
    </FormShell>
  );
}

// ---------- Punct de lucru ----------

export const branchSchema = z
  .object({
    code,
    name: required.max(200),
    address: required.max(300),
    countryCode: required,
    countyCode: optionalText,
    city: required.max(100),
    postalCode: optionalText,
    phone: optionalPhone,
    email: optionalEmail,
    isActive: z.boolean(),
  })
  .superRefine(requireCountyForRomania);
type BranchInput = z.input<typeof branchSchema>;
type BranchValues = z.output<typeof branchSchema>;

export function BranchForm({ tree, lookups, companyId, branchId, onSaved, onCancel, after }: {
  readonly tree: OrgTree;
  readonly lookups: Lookups;
  readonly companyId: number;
  readonly branchId: number | undefined;
  readonly onSaved: (saved: Saved) => void;
  readonly onCancel: () => void;
  readonly after?: ReactNode;
}) {
  const branch = tree.branches.find((b) => b.id === branchId);
  const defaults: BranchInput = branch === undefined
    ? { code: '', name: '', address: '', countryCode: ROMANIA, countyCode: null,     city: '', postalCode: null, phone: null, email: null, isActive: true }
    : { code: branch.code, name: branch.name, address: branch.address, countryCode: branch.countryCode, countyCode: branch.countyCode, city: branch.city,
            postalCode: branch.postalCode, phone: branch.phone, email: branch.email, isActive: branch.isActive };
  const form = useForm<BranchInput, unknown, BranchValues>({
    resolver: zodResolver(branchSchema) as Resolver<BranchInput, unknown, BranchValues>,
    defaultValues: defaults as DefaultValues<BranchInput>,
  });
  useEffect(() => form.reset(defaults), [branchId, branch?.rowVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const country = useCountyFollowsCountry(form, lookups);
  const save = useSave(
    form,
    Object.keys(defaults),
    (v) => orgApi.saveBranch({ ...v, id: branch?.id ?? null, companyId, rowVersion: branch?.rowVersion ?? null }),
    onSaved,
  );
  const company = tree.companies.find((c) => c.id === companyId);

  return (
    <FormShell
      title={<h2 className="form-title">{branch?.name ?? t('org.newBranch')} <span className="muted">· {company?.name}</span></h2>}
      error={save.error}
      onSubmit={form.handleSubmit((v) => save.submit(v), save.invalid)}
      footer={<Footer onCancel={() => (branch === undefined ? onCancel() : form.reset(defaults))} submitting={form.formState.isSubmitting} />}
      after={after}
    >
      <div className="grid g3">
        <TextField control={form.control} name="code" label={t('field.code')} upper />
        <TextField control={form.control} name="name" label={t('field.name')} className="span2" />
        <TextField control={form.control} name="address" label={t('field.address')} className="span2" />
        <SelectField control={form.control} name="countryCode" label={t('field.country')} options={countryOptions(lookups)} />
        <SelectField control={form.control} name="countyCode" label={t('field.county')} options={countyOptions(lookups, country)} allowEmpty={t('common.none')} />
        <TextField control={form.control} name="city" label={t('field.city')} />
        <TextField control={form.control} name="postalCode" label={t('field.postalCode')} />
        <TextField control={form.control} name="phone" label={t('field.phone')} />
        <TextField control={form.control} name="email" label={t('field.email')} />
        <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
      </div>
    </FormShell>
  );
}

// ---------- Locatie ----------

export const locationSchema = z
  .object({
    code,
    name: required.max(200),
    branchId: z.number().nullable(),
    locationType: z.number({ message: t('common.required') }).int().min(1).max(6),
    address: optionalText,
    countryCode: optionalText,
    countyCode: optionalText,
    city: optionalText,
    postalCode: optionalText,
    phone: optionalPhone,
    email: optionalEmail,
    isActive: z.boolean(),
  })
  // Adresa locatiei e optionala; daca s-a ales Romania, judetul se cere ca la firma si la punctul de lucru.
  .superRefine(requireCountyForRomania);
type LocationInput = z.input<typeof locationSchema>;
type LocationValues = z.output<typeof locationSchema>;

export const locationTypes = [1, 2, 3, 4, 5, 6] as const;

export function LocationForm({ tree, lookups, companyId, locationId, branchId, onSaved, onCancel, after }: {
  readonly tree: OrgTree;
  readonly lookups: Lookups;
  readonly companyId: number;
  readonly locationId: number | undefined;
  readonly branchId?: number | undefined;
  readonly onSaved: (saved: Saved) => void;
  readonly onCancel: () => void;
  readonly after?: ReactNode;
}) {
  const location = tree.locations.find((l) => l.id === locationId);
  const defaults: LocationInput = location === undefined
    ? { code: '', name: '', branchId: branchId ?? null, locationType: 2, address: null, countryCode: null, countyCode: null,     city: null, postalCode: null, phone: null, email: null, isActive: true }
    : { code: location.code, name: location.name, branchId: location.branchId, locationType: location.locationType, address: location.address,
            countryCode: location.countryCode, countyCode: location.countyCode, city: location.city, postalCode: location.postalCode,
            phone: location.phone, email: location.email, isActive: location.isActive };
  const form = useForm<LocationInput, unknown, LocationValues>({
    resolver: zodResolver(locationSchema) as Resolver<LocationInput, unknown, LocationValues>,
    defaultValues: defaults as DefaultValues<LocationInput>,
  });
  useEffect(() => form.reset(defaults), [locationId, location?.rowVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const country = useCountyFollowsCountry(form, lookups);
  const save = useSave(
    form,
    Object.keys(defaults),
    (v) => orgApi.saveLocation({ ...v, id: location?.id ?? null, companyId, rowVersion: location?.rowVersion ?? null }),
    onSaved,
  );
  const branches = tree.branches.filter((b) => b.companyId === companyId && (b.isActive || b.id === location?.branchId)).map((b) => ({ value: String(b.id), text: b.name }));

  return (
    <FormShell
      title={<h2 className="form-title">{location?.name ?? t('org.newLocation')}</h2>}
      error={save.error}
      onSubmit={form.handleSubmit((v) => save.submit(v), save.invalid)}
      footer={<Footer onCancel={() => (location === undefined ? onCancel() : form.reset(defaults))} submitting={form.formState.isSubmitting} />}
      after={after}
    >
      <div className="grid g3">
        <TextField control={form.control} name="code" label={t('field.code')} upper />
        <TextField control={form.control} name="name" label={t('field.name')} className="span2" />
        <SelectField control={form.control} name="branchId" label={t('field.branch')} options={branches} allowEmpty={t('field.directlyUnderCompany')} numeric />
        <SelectField control={form.control} name="locationType" label={t('field.locationType')} numeric
          options={locationTypes.map((type) => ({ value: String(type), text: t(`locationType.${type}`) }))} />
        <SwitchField control={form.control} name="isActive" label={t('field.isActive')} onText={t('common.activeF')} />
      </div>
      <h3 className="section">{t('section.address')}</h3>
      <div className="grid g3">
        <TextField control={form.control} name="address" label={t('field.address')} className="span2" />
        <SelectField control={form.control} name="countryCode" label={t('field.country')} options={countryOptions(lookups)} allowEmpty={t('common.none')} />
        <SelectField control={form.control} name="countyCode" label={t('field.county')} options={countyOptions(lookups, country)} allowEmpty={t('common.none')} />
        <TextField control={form.control} name="city" label={t('field.city')} />
        <TextField control={form.control} name="postalCode" label={t('field.postalCode')} />
      </div>
      <h3 className="section">{t('section.contact')}</h3>
      <div className="grid g3">
        <TextField control={form.control} name="phone" label={t('field.phone')} />
        <TextField control={form.control} name="email" label={t('field.email')} />
      </div>
    </FormShell>
  );
}
