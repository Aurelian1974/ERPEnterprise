import { useEffect, useMemo, useState } from 'react';
import { Controller, useFieldArray, useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import { ErrorBanner, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { SearchSelectField } from '../components/SearchSelectField';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, optionalEmail, optionalPhone, optionalText, required } from '../components/forms';
import { formatDate } from '../format/date';
import { t, type TextKey } from '../i18n';
import { isoToRo, roToIso } from '../partners/partnerModel';
import { UserKind, externalAccessTypes, userApi, userKeys, type UserDetail, type UserLookups, type UserSaved } from './userApi';
import { accessTypeLabel, isoToday, kindOf } from './userModel';

const roDate = z.string().trim().refine((v) => v === '' || roToIso(v) !== undefined, t('validation.date'));

function userSchema(maxMonths: number) {
  return z
    .object({
      kind: z.number().int().min(1).max(3),
      userName: required.max(50).regex(/^[A-Za-z0-9][A-Za-z0-9._@-]{1,49}$/, t('user.validation.userName')),
      employeeId: z.number().nullable(),
      firstName: optionalText,
      lastName: optionalText,
      email: optionalEmail,
      phone: optionalPhone,
      externalAccessType: z.string().nullable(),
      externalPartnerId: z.number().nullable(),
      sponsorUserId: z.number().nullable(),
      accessReason: optionalText,
      validFrom: roDate,
      validUntil: roDate,
      language: z.string(),
      lastCompanyId: z.number().nullable(),
      isActive: z.boolean(),
      roles: z.array(z.object({ roleId: z.number().nullable(), companyId: z.string() })),
      notificationPreferences: z.array(z.object({ code: z.string(), inApp: z.boolean(), email: z.boolean(), emailMode: z.string() })),
    })
    .superRefine((v, ctx) => {
      const need = (path: string, ok: boolean) => {
        if (!ok) {
          ctx.addIssue({ code: 'custom', path: [path], message: t('common.required') });
        }
      };
      if (v.kind === UserKind.Employee) {
        need('employeeId', v.employeeId !== null);
      }
      if (v.kind === UserKind.External) {
        need('firstName', v.firstName !== null);
        need('lastName', v.lastName !== null);
        need('externalAccessType', v.externalAccessType !== null);
        need('externalPartnerId', v.externalPartnerId !== null);
        need('sponsorUserId', v.sponsorUserId !== null);
        need('accessReason', v.accessReason !== null);
        need('validFrom', v.validFrom !== '');
        need('validUntil', v.validUntil !== '');
        if (v.externalAccessType !== 'TECHNICAL') {
          need('email', v.email !== null);
        }
        const from = roToIso(v.validFrom);
        const until = roToIso(v.validUntil);
        if (from !== undefined && until !== undefined) {
          if (until < from) {
            ctx.addIssue({ code: 'custom', path: ['validUntil'], message: t('user.validation.untilBeforeFrom') });
          } else if (until > addMonths(from, maxMonths)) {
            ctx.addIssue({ code: 'custom', path: ['validUntil'], message: t('user.validation.tooLong', { months: maxMonths }) });
          }
        }
      }
      v.roles.forEach((role, index) => {
        if (role.roleId === null) {
          ctx.addIssue({ code: 'custom', path: ['roles', index, 'roleId'], message: t('common.required') });
        }
      });
      const keys = v.roles.map((r) => `${r.roleId}|${r.companyId}`);
      keys.forEach((key, index) => {
        if (keys.indexOf(key) !== index) {
          ctx.addIssue({ code: 'custom', path: ['roles', index, 'roleId'], message: t('user.validation.duplicateRole') });
        }
      });
    });
}
type Schema = ReturnType<typeof userSchema>;
type UserInput = z.input<Schema>;
type UserValues = z.output<Schema>;

/** „aaaa-ll-zz” plus n luni (ziua limitata la sfarsitul lunii), ca DateOnly.AddMonths pe server. */
export function addMonths(iso: string, months: number): string {
  const [y, m, d] = iso.split('-').map(Number) as [number, number, number];
  const total = y * 12 + (m - 1) + months;
  const year = Math.floor(total / 12);
  const month = (total % 12) + 1;
  const last = new Date(Date.UTC(year, month, 0)).getUTCDate();
  return `${year}-${String(month).padStart(2, '0')}-${String(Math.min(d, last)).padStart(2, '0')}`;
}

const fields = ['kind', 'userName', 'employeeId', 'firstName', 'lastName', 'email', 'phone', 'externalAccessType', 'externalPartnerId', 'sponsorUserId',
  'accessReason', 'validFrom', 'validUntil', 'language', 'lastCompanyId', 'isActive'];

/** Valoarea firmei in randul de rol: id-ul ca text, sau „” pentru toata instalarea (doar Administrator). */
const ALL_COMPANIES = '';

function defaultsFor(user: UserDetail | undefined, lookups: UserLookups): UserInput {
  if (user === undefined) {
    return {
      kind: UserKind.Employee, userName: '', employeeId: null, firstName: null, lastName: null, email: null, phone: null, externalAccessType: null,
      externalPartnerId: null, sponsorUserId: null, accessReason: null, validFrom: isoToRo(isoToday()), validUntil: '', language: 'ro',
      lastCompanyId: null, isActive: true, roles: [],
      notificationPreferences: lookups.notificationTypes.map((type) => ({ code: type.code, inApp: type.defaultInApp, email: type.defaultEmail,
        emailMode: type.defaultEmailMode })),
    };
  }
  return {
    kind: kindOf(user), userName: user.userName, employeeId: user.employeeId ?? null, firstName: user.firstName ?? null, lastName: user.lastName ?? null,
    email: user.email ?? null, phone: user.phone ?? null, externalAccessType: user.externalAccessType ?? null,
    externalPartnerId: user.externalPartnerId ?? null, sponsorUserId: user.sponsorUserId ?? null, accessReason: user.accessReason ?? null,
    validFrom: user.validFrom == null ? '' : isoToRo(user.validFrom), validUntil: user.validUntil == null ? '' : isoToRo(user.validUntil),
    language: user.language, lastCompanyId: user.lastCompanyId ?? null, isActive: user.isActive,
    roles: user.roles.map((r) => ({ roleId: r.roleId, companyId: r.companyId == null ? ALL_COMPANIES : String(r.companyId) })),
    notificationPreferences: user.notificationPreferences.map((p) => ({ code: p.code, inApp: p.inApp, email: p.email, emailMode: p.emailMode })),
  };
}

export type UserSection = 'account' | 'roles' | 'preferences';

async function searchPartners(text: string): Promise<Option[]> {
  const page = unwrap(await api.GET('/api/core/partners', { params: { query: { ...(text === '' ? {} : { Search: text }), IsActive: true, PageSize: 30 } } }));
  return page.items.map((p) => ({ value: String(p.id), text: p.fiscalCode == null ? p.name : `${p.name} · ${p.fiscalCode}` }));
}

async function searchEmployees(text: string): Promise<Option[]> {
  const page = unwrap(await api.GET('/api/core/employees', {
    params: { query: { ...(text === '' ? {} : { Search: text }), IsActive: true, WithoutUser: true, PageSize: 30 } },
  }));
  return page.items.map((e) => ({ value: String(e.id), text: `${e.firstName} ${e.lastName} · ${t('user.employeeNo', { no: e.employeeNo })} · ${e.companyName}` }));
}

/**
 * Fisa utilizatorului, tab-urile Cont si „Roluri și firme” (machetele 06b, 06c), intr-un singur formular cu o singura salvare.
 * `section` alege partea vizibila; undefined = formularul ramane montat, ascuns, pe celelalte tab-uri (UI-18).
 */
export function UserForm({ user, lookups, section, currentUserId, onSaved, onCancel }: {
  readonly user: UserDetail | undefined;
  readonly lookups: UserLookups;
  readonly section: UserSection | undefined;
  readonly currentUserId: number;
  readonly onSaved: (saved: UserSaved, userName: string) => void;
  readonly onCancel: () => void;
}) {
  const isNew = user === undefined;
  const schema = useMemo(() => userSchema(lookups.externalAccessMaxMonths), [lookups.externalAccessMaxMonths]);
  const defaults = useMemo(() => defaultsFor(user, lookups), [user, lookups]);
  const form = useForm<UserInput, unknown, UserValues>({ resolver: zodResolver(schema) as Resolver<UserInput, unknown, UserValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [user?.id, user?.rowVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const roles = useFieldArray({ control: form.control, name: 'roles' });
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const guard = useUnsavedChanges(form.formState.isDirty);
  const [kind, accessType] = useWatch({ control: form.control, name: ['kind', 'externalAccessType'] });
  const self = user?.id === currentUserId;

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    const external = values.kind === UserKind.External;
    try {
      const saved = await userApi.save({
        id: user?.id ?? null,
        rowVersion: user?.rowVersion ?? null,
        userName: values.userName,
        kind: values.kind,
        employeeId: values.kind === UserKind.Employee ? values.employeeId : null,
        firstName: values.kind === UserKind.Employee ? null : values.firstName,
        lastName: values.kind === UserKind.Employee ? null : values.lastName,
        email: values.kind === UserKind.Employee ? null : values.email,
        phone: external ? values.phone : null,
        externalAccessType: external ? values.externalAccessType : null,
        externalPartnerId: external ? values.externalPartnerId : null,
        sponsorUserId: external ? values.sponsorUserId : null,
        accessReason: external ? values.accessReason : null,
        validFrom: external ? (roToIso(values.validFrom) ?? null) : null,
        validUntil: external ? (roToIso(values.validUntil) ?? null) : null,
        language: values.language,
        lastCompanyId: values.lastCompanyId,
        isActive: values.isActive,
        // Propriile roluri nu se trimit: serverul refuza schimbarea lor (separarea drepturilor, 02.01, 8).
        roles: self ? null : values.roles.map((r) => ({ roleId: r.roleId ?? 0, companyId: r.companyId === ALL_COMPANIES ? null : Number(r.companyId) })),
        // Preferintele se trimit doar daca s-au schimbat: tipurile neatinse raman pe implicitele catalogului.
        notificationPreferences: form.formState.dirtyFields.notificationPreferences === undefined ? null : values.notificationPreferences,
      });
      form.reset(form.getValues());
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: userKeys.all });
      notify(t('common.saved'));
      onSaved(saved, values.userName.trim().toLowerCase());
    } catch (e) {
      const message = applyServerErrors(e, form.setError, fields);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  const companies = useMemo<Option[]>(() => lookups.companies.map((c) => ({ value: String(c.id), text: c.name })), [lookups.companies]);
  const sponsors = useMemo<Option[]>(
    () => lookups.sponsors.filter((s) => s.id !== user?.id).map((s) => ({ value: String(s.id), text: s.displayName })),
    [lookups.sponsors, user?.id],
  );
  const accessTypes = useMemo<Option[]>(() => externalAccessTypes.map((type) => ({ value: type, text: accessTypeLabel(type) })), []);
  const languages = useMemo<Option[]>(() => [{ value: 'ro', text: t('user.language.ro') }, { value: 'en', text: t('user.language.en') }], []);
  const roleOptions = useMemo<Option[]>(() => lookups.roles.map((r) => ({ value: String(r.id), text: r.name })), [lookups.roles]);
  const adminRoleId = lookups.roles.find((r) => r.code === 'ADMIN' && r.isSystem)?.id;
  const companyOptions = (roleId: number | null): Option[] =>
    roleId !== null && roleId === adminRoleId ? [{ value: ALL_COMPANIES, text: t('user.roles.allCompanies') }, ...companies] : companies;
  const employeeCurrent = user?.employeeId == null ? undefined
    : { value: String(user.employeeId), text: `${user.displayName} · ${t('user.employeeNo', { no: user.employeeNo ?? '' })} · ${user.employeeCompanyName ?? ''}` };
  const partnerCurrent = user?.externalPartnerId == null ? undefined
    : { value: String(user.externalPartnerId), text: user.externalPartnerFiscalCode == null ? (user.externalPartnerName ?? '') : `${user.externalPartnerName ?? ''} · ${user.externalPartnerFiscalCode}` };
  const info = user === undefined ? undefined : t('common.lastChanged', { name: user.modifiedByName, date: formatDate(new Date(user.lastChangedAt)) });

  return (
    <form className="node-form user-form" noValidate hidden={section === undefined} onSubmit={(e) => void submit(e)} aria-label={user?.displayName ?? t('user.newTitle')}>
      {isNew && <h2 className="form-title">{t('user.newTitle')}</h2>}
      <ErrorBanner message={error} />
      <fieldset className="form-body plain" hidden={section !== 'account'}>
        {kind !== UserKind.System && (
          <Controller
            control={form.control}
            name="kind"
            render={({ field }) => (
              <div className="field">
                <span className="label" id="user-kind-label">{t('user.field.kind')} *</span>
                <div className="segmented two" role="radiogroup" aria-labelledby="user-kind-label">
                  {([UserKind.Employee, UserKind.External] as const).map((value) => (
                    <button key={value} type="button" role="radio" aria-checked={field.value === value} className={field.value === value ? 'on' : ''}
                      onClick={() => field.onChange(value)}>
                      {t(value === UserKind.Employee ? 'user.kind.isEmployee' : 'user.kind.isExternal')}
                    </button>
                  ))}
                </div>
                <span className="muted small">{t(field.value === UserKind.Employee ? 'user.kind.employeeHint' : 'user.kind.externalHint')}</span>
              </div>
            )}
          />
        )}
        {kind === UserKind.System && <div className="banner info">{t('user.systemHint')}</div>}

        {kind === UserKind.Employee && (
          <>
            <div className="grid g3">
              <SearchSelectField control={form.control} name="employeeId" label={`${t('user.field.employee')} *`} className="span2"
                current={employeeCurrent} search={searchEmployees} />
              <TextField control={form.control} name="userName" label={`${t('user.field.userName')} *`} />
            </div>
            {user?.isEmployee === true && user.employeeId === form.getValues('employeeId') && <EmployeeFacts user={user} />}
          </>
        )}

        {kind === UserKind.External && (
          <>
            <div className="grid g3">
              <TextField control={form.control} name="lastName" label={`${t('user.field.lastName')} *`} />
              <TextField control={form.control} name="firstName" label={`${t('user.field.firstName')} *`} />
              <TextField control={form.control} name="userName" label={`${t('user.field.userName')} *`} />
              <TextField control={form.control} name="email" label={accessType === 'TECHNICAL' ? t('field.email') : `${t('field.email')} *`} />
              <TextField control={form.control} name="phone" label={t('field.phone')} />
              <SelectField control={form.control} name="externalAccessType" label={`${t('user.field.accessType')} *`} options={accessTypes} />
              <SearchSelectField control={form.control} name="externalPartnerId" label={`${t('user.field.organization')} *`} className="span2"
                current={partnerCurrent} search={searchPartners} />
              <SelectField control={form.control} name="sponsorUserId" label={`${t('user.field.sponsor')} *`} options={sponsors} numeric />
              <TextField control={form.control} name="validFrom" label={`${t('user.field.validFrom')} *`} />
              <TextField control={form.control} name="validUntil" label={`${t('user.field.validUntil')} *`} />
              <div className="field">
                <span className="label">{t('user.field.twoFactor')}</span>
                <span className="readonly">{t(accessType === 'TECHNICAL' ? 'user.twoFactor.technical' : 'user.twoFactor.requiredExternal')}</span>
              </div>
              <TextField control={form.control} name="accessReason" label={`${t('user.field.accessReason')} *`} className="span3" />
            </div>
            <div className="banner warn">{t('user.externalHint', { months: lookups.externalAccessMaxMonths })}</div>
          </>
        )}

        {kind === UserKind.System && (
          <div className="grid g3">
            <TextField control={form.control} name="userName" label={t('user.field.userName')} disabled />
            <TextField control={form.control} name="lastName" label={t('user.field.lastName')} />
            <TextField control={form.control} name="firstName" label={t('user.field.firstName')} />
            <TextField control={form.control} name="email" label={t('field.email')} />
          </div>
        )}

        <h3 className="section">{t('user.section.preferences')}</h3>
        <div className="grid g3">
          <SelectField control={form.control} name="language" label={t('user.field.language')} options={languages} />
          <SelectField control={form.control} name="lastCompanyId" label={t('user.field.defaultCompany')} options={companies} allowEmpty={t('common.none')} numeric />
          <SwitchField control={form.control} name="isActive" label={t('field.isActive')} disabled={self} />
        </div>
      </fieldset>

      <fieldset className="form-body plain" hidden={section !== 'roles'} disabled={self}>
        {self && <div className="banner info">{t('user.roles.selfHint')}</div>}
        <table className="simple roles-table">
          <thead>
            <tr>
              <th scope="col">{t('user.roles.company')}</th>
              <th scope="col">{t('user.roles.role')}</th>
              <th scope="col">{t('user.roles.scope')}</th>
              <th scope="col"><span className="sr-only">{t('common.actions')}</span></th>
            </tr>
          </thead>
          <tbody>
            {roles.fields.length === 0 && (
              <tr>
                <td colSpan={4} className="muted">{t('user.roles.empty')}</td>
              </tr>
            )}
            {roles.fields.map((row, index) => (
              <RoleRow key={row.id} index={index} form={form} roleOptions={roleOptions} companyOptions={companyOptions} onRemove={() => roles.remove(index)} />
            ))}
          </tbody>
        </table>
        {!self && (
          <button type="button" className="btn ghost" onClick={() => roles.append({ roleId: null, companyId: companies[0]?.value ?? ALL_COMPANIES })}>
            {t('user.roles.add')}
          </button>
        )}
        <p className="muted small">{t('user.roles.hint')}</p>
      </fieldset>

      <fieldset className="form-body plain" hidden={section !== 'preferences'}>
        <NotificationPreferences form={form} lookups={lookups} />
      </fieldset>

      <div className="form-footer">
        <span className="muted">{info}</span>
        <div className="actions">
          <button type="button" className="btn ghost" onClick={() => (isNew ? onCancel() : form.reset(defaults))}>
            {t('common.cancel')}
          </button>
          <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>
            {t('common.save')}
          </button>
        </div>
      </div>
    </form>
  );
}

type UserFormApi = ReturnType<typeof useForm<UserInput, unknown, UserValues>>;

function RoleRow({ index, form, roleOptions, companyOptions, onRemove }: {
  readonly index: number;
  readonly form: UserFormApi;
  readonly roleOptions: readonly Option[];
  readonly companyOptions: (roleId: number | null) => Option[];
  readonly onRemove: () => void;
}) {
  const roleId = useWatch({ control: form.control, name: `roles.${index}.roleId` });
  const companyId = useWatch({ control: form.control, name: `roles.${index}.companyId` });
  const options = companyOptions(roleId);
  // „Toate firmele” e permis doar Administratorului; la schimbarea rolului se alege o firma.
  useEffect(() => {
    if (companyId === ALL_COMPANIES && !options.some((o) => o.value === ALL_COMPANIES) && options[0] !== undefined) {
      form.setValue(`roles.${index}.companyId`, options[0].value, { shouldDirty: true });
    }
  }, [companyId, options, form, index]);
  const error = form.formState.errors.roles?.[index]?.roleId?.message;
  return (
    <tr>
      <td>
        <select className="cell-input" aria-label={t('user.roles.company')} {...form.register(`roles.${index}.companyId`)}>
          {options.map((o) => (
            <option key={o.value} value={o.value}>{o.text}</option>
          ))}
        </select>
      </td>
      <td>
        <Controller
          control={form.control}
          name={`roles.${index}.roleId`}
          render={({ field }) => (
            <select className="cell-input" aria-label={t('user.roles.role')} aria-invalid={error === undefined ? 'false' : 'true'}
              value={field.value === null ? '' : String(field.value)} onChange={(e) => field.onChange(e.target.value === '' ? null : Number(e.target.value))}>
              <option value="">{t('common.select')}</option>
              {roleOptions.map((o) => (
                <option key={o.value} value={o.value}>{o.text}</option>
              ))}
            </select>
          )}
        />
        {error !== undefined && <span className="field-error" role="alert">{error}</span>}
      </td>
      <td className="muted">{t('user.roles.wholeCompany')}</td>
      <td>
        <button type="button" className="btn ghost small" onClick={onRemove} aria-label={t('user.roles.remove')}>
          ✕
        </button>
      </td>
    </tr>
  );
}

/**
 * Tab-ul Preferinte: matricea tip × canal (in aplicatie, email) si momentul emailului (02.01, 11.2). Tipurile critice nu se
 * opresc in aplicatie. Fara alegere, utilizatorul primeste implicitele catalogului.
 */
function NotificationPreferences({ form, lookups }: { readonly form: UserFormApi; readonly lookups: UserLookups }) {
  const critical = new Map(lookups.notificationTypes.map((type) => [type.code, type.critical]));
  const rows = useWatch({ control: form.control, name: 'notificationPreferences' });
  return (
    <>
      <table className="simple preferences-table">
        <thead>
          <tr>
            <th scope="col">{t('user.preferences.type')}</th>
            <th scope="col">{t('user.preferences.inApp')}</th>
            <th scope="col">{t('user.preferences.email')}</th>
            <th scope="col">{t('user.preferences.emailMode')}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => {
            const label = t(`notification.type.${row.code}` as TextKey);
            const isCritical = critical.get(row.code) === true;
            return (
              <tr key={row.code}>
                <td>
                  {label}
                  {isCritical && <span className="pill warn small-pill">{t('user.preferences.critical')}</span>}
                </td>
                <td>
                  <input type="checkbox" aria-label={`${label}: ${t('user.preferences.inApp')}`} disabled={isCritical}
                    {...form.register(`notificationPreferences.${index}.inApp`)} />
                </td>
                <td>
                  <input type="checkbox" aria-label={`${label}: ${t('user.preferences.email')}`} {...form.register(`notificationPreferences.${index}.email`)} />
                </td>
                <td>
                  <select className="cell-input" aria-label={`${label}: ${t('user.preferences.emailMode')}`} disabled={!row.email}
                    {...form.register(`notificationPreferences.${index}.emailMode`)}>
                    <option value="Immediate">{t('user.preferences.immediate')}</option>
                    <option value="DailyDigest">{t('user.preferences.dailyDigest')}</option>
                  </select>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <p className="muted small">{t('user.preferences.hint')}</p>
    </>
  );
}

/** Datele din fisa angajatului (doar citire, 06b): numele, emailul si functia nu se editeaza pe utilizator. */
function EmployeeFacts({ user }: { readonly user: UserDetail }) {
  const facts: [string, string | null | undefined][] = [
    [t('user.employee.company'), user.employeeCompanyName],
    [t('field.branch'), user.employeeBranchName],
    [t('employee.field.department'), user.employeeDepartment],
    [t('employee.field.jobTitle'), user.employeeJobTitle],
    [t('employee.field.workEmail'), user.email],
    [t('field.phone'), user.employeePhone],
    [t('employee.field.hireDate'), user.employeeHireDate == null ? null : isoToRo(user.employeeHireDate)],
    [t('employee.field.leaveDate'), user.employeeLeaveDate == null ? null : isoToRo(user.employeeLeaveDate)],
  ];
  return (
    <>
      <dl className="facts">
        {facts.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{value == null || value === '' ? t('common.none') : value}</dd>
          </div>
        ))}
      </dl>
      <p className="muted small">{t('user.employeeHint')}</p>
    </>
  );
}
