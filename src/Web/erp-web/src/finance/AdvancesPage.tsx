import { useMemo, useState } from 'react';
import { Link, useOutletContext } from 'react-router';
import { useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { ApiError } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, SelectField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { applyServerErrors, money } from '../components/forms';
import { t } from '../i18n';
import { isoToRo, roToIso } from '../partners/partnerModel';
import { advanceApi, advanceKeys, advancePill, advanceStatusLabel, isTrip, Purpose, purposeLabel, purposes, useAdvances, type AdvanceLookups } from './advanceApi';
import { cashKeys, formatAmount } from './cashApi';
import { documentPath } from './CashDeskPage';

const filters = [
  { value: 1, label: 'advance.filter.open' },
  { value: 2, label: 'advance.filter.overdue' },
  { value: 3, label: 'advance.filter.settled' },
  { value: 0, label: 'advance.filter.all' },
] as const;

/**
 * Trezorerie › Avansuri spre decontare (02.02, F1 3.5 și 4.4; macheta f1-05): lista cu filtrele Deschise / Depășite / Decontate, cu
 * soldul, termenul și întârzierea, și acordarea unui avans nou: avansul și dispoziția de plată (ciornă) pe casa aleasă, validată de casier.
 */
export function AdvancesPage() {
  const user = useOutletContext<CurrentUser>();
  const [filter, setFilter] = useState<number>(1);
  const list = useAdvances(filter);
  const [creating, setCreating] = useState(false);
  const data = list.data;
  if (data === undefined) {
    return <div className="page" role="status">{list.isError ? list.error.message : t('app.loading')}</div>;
  }
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.treasury')} › {t('menu.advances')}</div>
      <div className="head">
        <div>
          <h1>{t('advance.title')}</h1>
          <div className="sub2">{t('advance.subtitle')}</div>
        </div>
        <div className="actions">
          {user.permissions.includes('Fin.ExpenseReport.Create') && <Link to="/finante/trezorerie/deconturi" className="btn ghost">{t('menu.expenseReports')}</Link>}
          <button type="button" className="btn primary" onClick={() => setCreating(true)}>{t('advance.new')}</button>
        </div>
      </div>
      <div className="split cash-document">
        <section className="card" aria-label={t('advance.title')}>
          <div className="chips-row" role="group" aria-label={t('advance.filter.label')}>
            {filters.map((f) => (
              <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                onClick={() => setFilter(f.value)}>{t(f.label)}</button>
            ))}
          </div>
          {data.advances.length === 0 ? <p className="muted">{t('advance.empty')}</p> : (
            <table className="partner-list cash-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('advance.col.employee')}</th>
                  <th scope="col">{t('advance.col.purpose')}</th>
                  <th scope="col" className="num">{t('advance.col.amount')}</th>
                  <th scope="col" className="num">{t('advance.col.balance')}</th>
                  <th scope="col">{t('advance.col.due')}</th>
                  <th scope="col">{t('cash.col.status')}</th>
                </tr>
              </thead>
              <tbody>
                {data.advances.map((a) => (
                  <tr key={a.id}>
                    <td>
                      <strong>{a.employeeName}</strong>
                      <div className="muted small">{[`AV ${a.id}`, a.grantedByNumber, a.expenseReportNumber].filter(Boolean).join(' · ')}</div>
                    </td>
                    <td className="small">{t(purposeLabel(a.purpose))}{a.description == null ? '' : <div className="muted">{a.description}</div>}</td>
                    <td className="num">{`${formatAmount(a.amount)} ${a.currencyCode}`}</td>
                    <td className="num"><strong>{formatAmount(a.balance)}</strong></td>
                    <td className="small">{isoToRo(a.settlementDueDate)}{a.daysOverdue > 0 && (
                      <div><span className="pill bad">{t('advance.daysOverdue', { days: a.daysOverdue })}</span></div>)}</td>
                    <td><span className={advancePill(a.status)}>{t(advanceStatusLabel(a.status))}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <aside className="card cash-side">
          {creating ? <NewAdvance lookups={data.lookups} onDone={() => setCreating(false)} />
            : <><p className="muted">{t('advance.sideHint')}</p><div className="banner dashed">{t('advance.rule')}</div></>}
        </aside>
      </div>
    </div>
  );
}

const roDate = z.string().trim().refine((v) => v === '' || roToIso(v) !== undefined, t('validation.date'));
const schema = z.object({
  employeeId: z.number().nullable().refine((v) => v !== null, t('common.required')),
  purpose: z.number(),
  amount: money.refine((v) => v !== null, t('common.required')),
  treasuryAccountId: z.number().nullable().refine((v) => v !== null, t('common.required')),
  documentDate: roDate.refine((v) => v !== '', t('common.required')),
  tripStartDate: roDate,
  tripEndDate: roDate,
  description: z.string().trim(),
});
type Input = z.input<typeof schema>;
type Values = z.output<typeof schema>;

function NewAdvance({ lookups, onDone }: { readonly lookups: AdvanceLookups; readonly onDone: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const [created, setCreated] = useState<number | undefined>();
  const today = isoToRo(new Date().toISOString().slice(0, 10));
  const form = useForm<Input, unknown, Values>({
    resolver: zodResolver(schema) as Resolver<Input, unknown, Values>,
    defaultValues: { employeeId: null, purpose: Purpose.CurrentExpenses, amount: '', treasuryAccountId: lookups.cashAccounts[0]?.id ?? null,
      documentDate: today, tripStartDate: '', tripEndDate: '', description: '' },
  });
  const purpose = useWatch({ control: form.control, name: 'purpose' });
  const employees = useMemo<Option[]>(() => lookups.employees.map((e) => ({ value: String(e.id), text: `${e.name} · ${e.employeeNo}` })), [lookups.employees]);
  const accounts = useMemo<Option[]>(() => lookups.cashAccounts.map((a) => ({ value: String(a.id), text: `${a.code} · ${a.currencyCode}` })),
    [lookups.cashAccounts]);
  const purposeOptions = useMemo<Option[]>(() => purposes.map((p) => ({ value: String(p), text: t(purposeLabel(p)) })), []);

  const submit = (confirmOverdue: boolean) => form.handleSubmit(async (v) => {
    setError(undefined);
    try {
      const result = await advanceApi.create({
        employeeId: v.employeeId ?? 0, purpose: v.purpose, amount: v.amount ?? '0', treasuryAccountId: v.treasuryAccountId ?? 0,
        documentDate: roToIso(v.documentDate) ?? '', tripStartDate: roToIso(v.tripStartDate) ?? null, tripEndDate: roToIso(v.tripEndDate) ?? null,
        description: v.description === '' ? null : v.description, confirmOverdue,
      });
      await Promise.all([queryClient.invalidateQueries({ queryKey: advanceKeys.all }), queryClient.invalidateQueries({ queryKey: cashKeys.all })]);
      notify(t('advance.created', { due: isoToRo(result.settlementDueDate) }));
      setCreated(result.cashDocumentId);
    } catch (e) {
      if (!confirmOverdue && e instanceof ApiError && e.code === 'FIN_ADVANCE_OVERDUE_UNCONFIRMED' && window.confirm(`${e.message}\n\n${t('cash.confirmWarning')}`)) {
        await submit(true)();
        return;
      }
      const message = applyServerErrors(e, form.setError, ['employeeId', 'purpose', 'amount', 'treasuryAccountId', 'documentDate', 'tripStartDate',
        'tripEndDate', 'description']);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  });

  if (created !== undefined) {
    return (
      <div className="cash-side">
        <div className="banner info">{t('advance.createdHint')}</div>
        <Link className="btn primary" to={documentPath({ id: created })}>{t('advance.openPayment')}</Link>
        <button type="button" className="btn ghost" onClick={onDone}>{t('common.cancel')}</button>
      </div>
    );
  }
  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(false)(e)} aria-label={t('advance.new')}>
      <h2 className="form-title">{t('advance.newTitle')}</h2>
      <ErrorBanner message={error} />
      <div className="form-body plain">
        <SelectField control={form.control} name="employeeId" label={`${t('cash.field.employee')} *`} options={employees} numeric />
        <SelectField control={form.control} name="purpose" label={t('advance.col.purpose')} options={purposeOptions} numeric />
        <TextField control={form.control} name="amount" label={`${t('advance.col.amount')} *`} />
        <SelectField control={form.control} name="treasuryAccountId" label={`${t('advance.field.cash')} *`} options={accounts} numeric />
        <TextField control={form.control} name="documentDate" label={`${t('cash.field.date')} *`} />
        {isTrip(purpose) && (
          <>
            <TextField control={form.control} name="tripStartDate" label={t('advance.field.tripStart')} />
            <TextField control={form.control} name="tripEndDate" label={`${t('advance.field.tripEnd')} *`} />
          </>
        )}
        <TextField control={form.control} name="description" label={t('cash.field.description')} />
        <p className="muted small">{t('advance.dueHint')}</p>
      </div>
      <div className="form-footer">
        <span />
        <div className="actions">
          <button type="button" className="btn ghost" onClick={onDone}>{t('common.cancel')}</button>
          <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>{t('advance.grant')}</button>
        </div>
      </div>
    </form>
  );
}
