import { useEffect, useMemo, useState } from 'react';
import { Link, useOutletContext, useSearchParams } from 'react-router';
import { useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { TextBoxComponent } from '@syncfusion/ej2-react-inputs';
import { z } from 'zod';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, optionalEmail, optionalPhone, optionalText, required } from '../components/forms';
import { formatDate } from '../format/date';
import { t } from '../i18n';
import { HistoryView } from '../partners/PartnerTabs';
import { isoToRo, roToIso } from '../partners/partnerModel';
import { Pager, useDebounced } from '../partners/PartnersPage';
import { employeeApi, employeeKeys, useEmployee, useEmployeeHistory, useEmployeeLookups, useEmployees, type EmployeeCriteria, type EmployeeDetail,
  type EmployeeLookups } from './employeeApi';

function useSelection() {
  const [params, setParams] = useSearchParams();
  const id = Number(params.get('id'));
  return {
    id: Number.isInteger(id) && id > 0 ? id : undefined,
    creating: params.get('nou') === '1',
    select: (employeeId: number) => setParams({ id: String(employeeId) }),
    startCreate: () => setParams({ nou: '1' }),
    clear: () => setParams({}),
  };
}

/**
 * Angajati: fisa minima din Nucleu (02.01, 3.4), de care se leaga utilizatorii angajati. Fara macheta aprobata: lista si fisa
 * urmeaza tiparul celorlalte nomenclatoare; modulul HR va extinde fisa (contract, salarizare).
 */
export function EmployeesPage() {
  const user = useOutletContext<CurrentUser>();
  const lookups = useEmployeeLookups();
  const selection = useSelection();
  const [search, setSearch] = useState('');
  const [companyId, setCompanyId] = useState<number | undefined>();
  const [showInactive, setShowInactive] = useState(false);
  const [withoutUser, setWithoutUser] = useState(false);
  const debounced = useDebounced(search);
  const filterKey = JSON.stringify([debounced, companyId, showInactive, withoutUser]);
  const [paging, setPaging] = useState({ key: filterKey, page: 1 });
  const page = paging.key === filterKey ? paging.page : 1;
  const criteria: EmployeeCriteria = { search: debounced, companyId, showInactive, withoutUser: withoutUser ? true : undefined, page };
  const list = useEmployees(criteria);
  const canManage = user.permissions.includes('Core.Employee.Manage');
  const canManageUsers = user.permissions.includes('Core.User.Manage');

  if (lookups.data === undefined) {
    return <div className="page" role="status">{lookups.isError ? t('app.loadError') : t('app.loading')}</div>;
  }
  const data = list.data;

  return (
    <div className="page wide">
      <div className="crumb">{t('menu.admin')} › {t('menu.employees')}</div>
      <div className="head">
        <div>
          <h1>{t('employee.title')}</h1>
          <div className="sub2">{t('employee.subtitle', { count: (data?.activeCount ?? 0).toLocaleString('ro-RO') })}</div>
        </div>
        <div className="actions">
          {canManageUsers && (
            <Link to="/admin/utilizatori" className="btn ghost">
              {t('menu.users')}
            </Link>
          )}
          {canManage && (
            <button type="button" className="btn primary" onClick={selection.startCreate}>
              {t('employee.new')}
            </button>
          )}
        </div>
      </div>

      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.employees')}>
          <TextBoxComponent placeholder={t('employee.searchPlaceholder')} value={search} input={(args: { value: string }) => setSearch(args.value)}
            showClearButton htmlAttributes={{ 'aria-label': t('employee.searchPlaceholder') }} />
          <div className="chips-row">
            <select className="chip" aria-label={t('employee.filter.company')} value={companyId ?? ''}
              onChange={(e) => setCompanyId(e.target.value === '' ? undefined : Number(e.target.value))}>
              <option value="">{t('user.filter.anyCompany')}</option>
              {lookups.data.companies.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
            <button type="button" className={withoutUser ? 'chip on' : 'chip'} aria-pressed={withoutUser} onClick={() => setWithoutUser(!withoutUser)}>
              {t('employee.filter.withoutUser')}
            </button>
            <button type="button" className={showInactive ? 'chip on' : 'chip'} aria-pressed={showInactive} onClick={() => setShowInactive(!showInactive)}>
              {t('partner.filter.showInactive')}
            </button>
          </div>
          {list.isError && data === undefined && (
            <div className="banner error" role="alert">
              {t('app.loadError')}{' '}
              <button type="button" className="btn ghost" onClick={() => void list.refetch()}>
                {t('app.retry')}
              </button>
            </div>
          )}
          {data !== undefined && data.items.length === 0 && <p className="muted">{t('employee.empty')}</p>}
          {data !== undefined && data.items.length > 0 && (
            <table className="partner-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('employee.col.employee')}</th>
                  <th scope="col">{t('employee.col.company')}</th>
                  <th scope="col">{t('employee.col.job')}</th>
                  <th scope="col">{t('employee.col.user')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((item) => {
                  const current = item.id === selection.id;
                  return (
                    <tr key={item.id} className={[current ? 'current' : '', item.isActive ? '' : 'inactive'].join(' ').trim()} onClick={() => selection.select(item.id)}>
                      <td>
                        <button type="button" className="row-link" aria-current={current ? 'true' : undefined} onClick={(e) => {
                          e.stopPropagation();
                          selection.select(item.id);
                        }}>
                          {`${item.lastName} ${item.firstName}`}
                        </button>
                        <div className="muted small">
                          {t('user.employeeNo', { no: item.employeeNo })}
                          {item.leaveDate == null ? '' : ` · ${t('employee.leftOn', { date: isoToRo(item.leaveDate) })}`}
                        </div>
                      </td>
                      <td className="small">{item.companyName}{item.branchName == null ? '' : <div className="muted">{item.branchName}</div>}</td>
                      <td className="small">{[item.department, item.jobTitle].filter(Boolean).join(' · ') || t('common.none')}</td>
                      <td className="small">{item.userName ?? <span className="muted">{t('employee.noUser')}</span>}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
          {data !== undefined && data.total > 0 && <Pager page={page} total={data.total} onPage={(next) => setPaging({ key: filterKey, page: next })} />}
        </section>

        <section className="card detail-card">
          {selection.creating ? (
            <EmployeeForm key="new" employee={undefined} lookups={lookups.data} canManage={canManage} onSaved={(id) => selection.select(id)} onCancel={selection.clear} />
          ) : selection.id === undefined ? (
            <p className="muted empty-detail">{t('employee.select')}</p>
          ) : (
            <EmployeePanel key={selection.id} employeeId={selection.id} lookups={lookups.data} canManage={canManage} canManageUsers={canManageUsers} />
          )}
        </section>
      </div>
    </div>
  );
}

function EmployeePanel({ employeeId, lookups, canManage, canManageUsers }: {
  readonly employeeId: number;
  readonly lookups: EmployeeLookups;
  readonly canManage: boolean;
  readonly canManageUsers: boolean;
}) {
  const employee = useEmployee(employeeId);
  const [tab, setTab] = useState<'general' | 'history'>('general');
  const history = useEmployeeHistory(employeeId, tab === 'history');
  if (employee.data === undefined) {
    return <p role="status">{employee.isError ? employee.error.message : t('app.loading')}</p>;
  }
  const data = employee.data;
  return (
    <div className="company-panel">
      <div className="panel-head">
        <div>
          <h2>
            {`${data.firstName} ${data.lastName}`}{' '}
            <span className={data.isActive ? 'pill ok' : 'pill off'}>{data.isActive ? t('common.active') : t('common.inactive')}</span>
          </h2>
          <div className="muted">{`${t('user.employeeNo', { no: data.employeeNo })} · ${data.companyName}`}</div>
          <div className="chips-row">
            {data.userId == null ? (
              <span className="chip static">{t('employee.noUser')}</span>
            ) : canManageUsers ? (
              <Link className="chip static" to={`/admin/utilizatori?id=${data.userId}`}>{t('employee.userChip', { userName: data.userName ?? '' })}</Link>
            ) : (
              <span className="chip static">{t('employee.userChip', { userName: data.userName ?? '' })}</span>
            )}
          </div>
        </div>
      </div>
      <div className="tabs" role="tablist" aria-label={`${data.firstName} ${data.lastName}`}>
        {(['general', 'history'] as const).map((key) => (
          <button key={key} type="button" role="tab" id={`tab-${key}`} aria-selected={tab === key} tabIndex={tab === key ? 0 : -1}
            className={tab === key ? 'tab on' : 'tab'} onClick={() => setTab(key)}
            onKeyDown={(event) => {
              if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
                event.preventDefault();
                setTab(tab === 'general' ? 'history' : 'general');
              }
            }}>
            {t(key === 'general' ? 'employee.tab.general' : 'partner.tab.history')}
          </button>
        ))}
      </div>
      <div role="tabpanel" aria-labelledby="tab-general" hidden={tab !== 'general'}>
        <EmployeeForm employee={data} lookups={lookups} canManage={canManage} onSaved={() => undefined} onCancel={() => undefined} />
      </div>
      {tab === 'history' && (
        <div role="tabpanel" aria-labelledby="tab-history">
          <HistoryView history={history} />
        </div>
      )}
    </div>
  );
}

const roDate = z.string().trim().refine((v) => v === '' || roToIso(v) !== undefined, t('validation.date'));

const employeeSchema = z
  .object({
    companyId: z.number().nullable(),
    branchId: z.number().nullable(),
    employeeNo: required.max(20).regex(/^[A-Za-z0-9][A-Za-z0-9._/-]{0,19}$/, t('employee.validation.employeeNo')),
    firstName: required.max(100),
    lastName: required.max(100),
    department: optionalText,
    jobTitle: optionalText,
    workEmail: optionalEmail,
    phone: optionalPhone,
    hireDate: roDate,
    leaveDate: roDate,
    isActive: z.boolean(),
  })
  .superRefine((v, ctx) => {
    if (v.companyId === null) {
      ctx.addIssue({ code: 'custom', path: ['companyId'], message: t('common.required') });
    }
    const hire = roToIso(v.hireDate);
    const leave = roToIso(v.leaveDate);
    if (hire !== undefined && leave !== undefined && leave < hire) {
      ctx.addIssue({ code: 'custom', path: ['leaveDate'], message: t('employee.validation.leaveBeforeHire') });
    }
  });
type EmployeeInput = z.input<typeof employeeSchema>;
type EmployeeValues = z.output<typeof employeeSchema>;

const fields = ['companyId', 'branchId', 'employeeNo', 'firstName', 'lastName', 'department', 'jobTitle', 'workEmail', 'phone', 'hireDate', 'leaveDate', 'isActive'];

function defaultsFor(employee: EmployeeDetail | undefined, lookups: EmployeeLookups): EmployeeInput {
  return employee === undefined
    ? { companyId: lookups.companies[0]?.id ?? null, branchId: null, employeeNo: '', firstName: '', lastName: '', department: null, jobTitle: null,
        workEmail: null, phone: null, hireDate: '', leaveDate: '', isActive: true }
    : { companyId: employee.companyId, branchId: employee.branchId ?? null, employeeNo: employee.employeeNo, firstName: employee.firstName,
        lastName: employee.lastName, department: employee.department ?? null, jobTitle: employee.jobTitle ?? null, workEmail: employee.workEmail ?? null,
        phone: employee.phone ?? null, hireDate: employee.hireDate == null ? '' : isoToRo(employee.hireDate),
        leaveDate: employee.leaveDate == null ? '' : isoToRo(employee.leaveDate), isActive: employee.isActive };
}

function EmployeeForm({ employee, lookups, canManage, onSaved, onCancel }: {
  readonly employee: EmployeeDetail | undefined;
  readonly lookups: EmployeeLookups;
  readonly canManage: boolean;
  readonly onSaved: (id: number) => void;
  readonly onCancel: () => void;
}) {
  const isNew = employee === undefined;
  const defaults = useMemo(() => defaultsFor(employee, lookups), [employee, lookups]);
  const form = useForm<EmployeeInput, unknown, EmployeeValues>({
    resolver: zodResolver(employeeSchema) as Resolver<EmployeeInput, unknown, EmployeeValues>,
    defaultValues: defaults,
  });
  useEffect(() => form.reset(defaults), [employee?.id, employee?.rowVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const guard = useUnsavedChanges(form.formState.isDirty);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const companyId = useWatch({ control: form.control, name: 'companyId' });
  const companies = useMemo<Option[]>(() => lookups.companies.map((c) => ({ value: String(c.id), text: c.name })), [lookups.companies]);
  const branches = useMemo<Option[]>(
    () => lookups.branches.filter((b) => b.companyId === companyId).map((b) => ({ value: String(b.id), text: b.name })),
    [lookups.branches, companyId],
  );
  // Punctul de lucru apartine firmei: la schimbarea firmei se goleste.
  useEffect(() => {
    const branchId = form.getValues('branchId');
    if (branchId !== null && !lookups.branches.some((b) => b.id === branchId && b.companyId === companyId)) {
      form.setValue('branchId', null, { shouldDirty: true });
    }
  }, [companyId, form, lookups.branches]);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      const saved = await employeeApi.save({
        ...values,
        id: employee?.id ?? null,
        rowVersion: employee?.rowVersion ?? null,
        companyId: values.companyId ?? 0,
        hireDate: roToIso(values.hireDate) ?? null,
        leaveDate: roToIso(values.leaveDate) ?? null,
      });
      form.reset(form.getValues());
      guard.markClean();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: employeeKeys.all }),
        queryClient.invalidateQueries({ queryKey: ['core', 'users'] }),
      ]);
      notify(t('common.saved'));
      onSaved(saved.id);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, fields);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  const info = employee === undefined ? undefined : t('common.lastChanged', { name: employee.modifiedByName, date: formatDate(new Date(employee.lastChangedAt)) });
  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(e)} aria-label={employee === undefined ? t('employee.newTitle') : employee.employeeNo}>
      {isNew && <h2 className="form-title">{t('employee.newTitle')}</h2>}
      <ErrorBanner message={error} />
      <fieldset className="form-body plain" disabled={!canManage}>
        <div className="grid g3">
          <SelectField control={form.control} name="companyId" label={`${t('employee.field.company')} *`} options={companies} numeric
            disabled={employee?.userId != null} />
          <SelectField control={form.control} name="branchId" label={t('field.branch')} options={branches} allowEmpty={t('common.none')} numeric />
          <TextField control={form.control} name="employeeNo" label={`${t('employee.field.employeeNo')} *`} upper />
          <TextField control={form.control} name="lastName" label={`${t('user.field.lastName')} *`} />
          <TextField control={form.control} name="firstName" label={`${t('user.field.firstName')} *`} />
          <TextField control={form.control} name="department" label={t('employee.field.department')} />
          <TextField control={form.control} name="jobTitle" label={t('employee.field.jobTitle')} />
          <TextField control={form.control} name="workEmail" label={t('employee.field.workEmail')} />
          <TextField control={form.control} name="phone" label={t('field.phone')} />
          <TextField control={form.control} name="hireDate" label={t('employee.field.hireDate')} />
          <TextField control={form.control} name="leaveDate" label={t('employee.field.leaveDate')} />
          <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
        </div>
        <p className="muted small">{t('employee.hint')}</p>
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
