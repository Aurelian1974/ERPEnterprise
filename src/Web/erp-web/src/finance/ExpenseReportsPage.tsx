import { useMemo, useState } from 'react';
import { Link, useOutletContext, useSearchParams } from 'react-router';
import { useFieldArray, useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, SelectField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors } from '../components/forms';
import { t } from '../i18n';
import { isoToRo, roToIso } from '../partners/partnerModel';
import {
  advanceApi, advanceKeys, Justification, justificationLabel, ReportStatus, reportStatusLabel, useExpenseReport, useExpenseReports,
  type ExpenseReportDetail, type ExpenseReportPage,
} from './advanceApi';
import { cashKeys, formatAmount } from './cashApi';
import { documentPath } from './CashDeskPage';

const amount = z.string().trim().refine((v) => v === '' || /^\d{1,15}([.,]\d{1,2})?$/.test(v), t('validation.money')).transform((v) => v.replace(',', '.'));
const lineSchema = z.object({
  expenseTypeId: z.number().nullable().refine((v) => v !== null, t('common.required')),
  justificationKind: z.number(),
  documentNo: z.string().trim(),
  amount,
  vatAmount: amount,
  days: z.string().trim(),
});
const schema = z.object({
  employeeId: z.number().nullable().refine((v) => v !== null, t('common.required')),
  cashAdvanceId: z.number().nullable(),
  documentDate: z.string().trim().refine((v) => roToIso(v) !== undefined, t('validation.date')),
  description: z.string().trim(),
  lines: z.array(lineSchema).min(1, t('cash.validation.lines')),
});
type Input = z.input<typeof schema>;
type Values = z.output<typeof schema>;
const emptyLine = { expenseTypeId: null, justificationKind: Justification.Receipt, documentNo: '', amount: '', vatAmount: '', days: '' };

/** Corpul salvării: diurna trimite zilele (suma o calculează serverul), celelalte linii suma și TVA-ul. */
export function toReportBody(values: Values, report: ExpenseReportDetail | undefined, perDiemTypes: ReadonlySet<number>) {
  return {
    id: report?.id ?? null,
    rowVersion: report?.rowVersion ?? null,
    employeeId: values.employeeId ?? 0,
    cashAdvanceId: values.cashAdvanceId,
    documentDate: roToIso(values.documentDate) ?? '',
    description: values.description === '' ? null : values.description,
    lines: values.lines.map((l) => {
      const perDiem = l.expenseTypeId !== null && perDiemTypes.has(l.expenseTypeId);
      return {
        expenseTypeId: l.expenseTypeId ?? 0, justificationKind: perDiem ? Justification.PerDiem : l.justificationKind,
        documentNo: l.documentNo === '' ? null : l.documentNo, documentDate: null, supplierFiscalCode: null, supplierInvoiceId: null,
        amount: perDiem ? null : l.amount, vatAmount: perDiem || l.vatAmount === '' ? null : l.vatAmount, costCenterId: null, projectId: null,
        days: perDiem ? l.days.replace(',', '.') : null, note: null,
      };
    }),
  };
}

/**
 * Trezorerie › Deconturi de cheltuieli (02.02, F1 3.7 și 4.4; macheta f1-06): lista deconturilor și fișa decontului, introdusă de
 * contabil: angajatul și avansul, cheltuielile justificate (diurna calculată pe server), apoi Trimite, Aprobă (alt utilizator decât cel
 * care l-a întocmit) și Validează, cu dispoziția (ciornă) care închide diferența față de avans.
 */
export function ExpenseReportsPage() {
  const [params, setParams] = useSearchParams();
  const idParam = Number(params.get('id'));
  const id = Number.isInteger(idParam) && idParam > 0 ? idParam : undefined;
  const creating = params.get('nou') === '1';
  const page = useExpenseReports();
  const report = useExpenseReport(id);
  const data = page.data;
  if (data === undefined) {
    return <div className="page" role="status">{page.isError ? page.error.message : t('app.loading')}</div>;
  }
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.treasury')} › {t('menu.expenseReports')}</div>
      <div className="head">
        <div>
          <h1>{t('report.title')}</h1>
          <div className="sub2">{t('report.subtitle')}</div>
        </div>
        <div className="actions">
          <button type="button" className="btn primary" onClick={() => setParams({ nou: '1' })}>{t('report.new')}</button>
        </div>
      </div>
      <div className="split cash-register">
        <section className="card" aria-label={t('report.title')}>
          {data.reports.length === 0 ? <p className="muted">{t('report.empty')}</p> : (
            <table className="partner-list cash-list">
              <thead>
                <tr>
                  <th scope="col">{t('cash.col.document')}</th>
                  <th scope="col" className="num">{t('report.col.total')}</th>
                  <th scope="col" className="num">{t('report.col.difference')}</th>
                  <th scope="col">{t('cash.col.status')}</th>
                </tr>
              </thead>
              <tbody>
                {data.reports.map((r) => (
                  <tr key={r.id} className={r.id === id ? 'current' : ''} onClick={() => setParams({ id: String(r.id) })}>
                    <td>
                      <button type="button" className="row-link">{r.number ?? t('cash.noNumber')}</button>
                      <div className="muted small">{`${r.employeeName} · ${isoToRo(r.documentDate)}`}</div>
                    </td>
                    <td className="num">{formatAmount(r.totalJustified)}</td>
                    <td className="num">{formatAmount(r.difference)}</td>
                    <td><span className={r.status === ReportStatus.Validated ? 'pill ok' : 'pill info'}>{t(reportStatusLabel(r.status))}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <section className="card">
          {creating ? <ReportForm key="new" report={undefined} page={data} onSaved={(saved) => setParams({ id: String(saved) })} />
            : id === undefined ? <p className="muted empty-detail">{t('report.select')}</p>
              : report.data === undefined ? <p role="status">{report.isError ? report.error.message : t('app.loading')}</p>
                : <ReportForm key={`${id}-${report.data.rowVersion}`} report={report.data} page={data} onSaved={() => undefined} />}
        </section>
      </div>
    </div>
  );
}

function ReportForm({ report, page, onSaved }: { readonly report: ExpenseReportDetail | undefined; readonly page: ExpenseReportPage; readonly onSaved: (id: number) => void }) {
  const user = useOutletContext<CurrentUser>();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const [settlementCash, setSettlementCash] = useState<number | undefined>(page.lookups.cashAccounts[0]?.id);
  const draft = report === undefined || report.status === ReportStatus.Draft;
  const defaults = useMemo<Input>(() => report === undefined
    ? { employeeId: null, cashAdvanceId: null, documentDate: isoToRo(new Date().toISOString().slice(0, 10)), description: '', lines: [emptyLine] }
    : { employeeId: report.employeeId, cashAdvanceId: report.cashAdvanceId ?? null, documentDate: isoToRo(report.documentDate), description: report.description ?? '',
        lines: report.lines.map((l) => ({ expenseTypeId: l.expenseTypeId, justificationKind: l.justificationKind, documentNo: l.documentNo ?? '',
          amount: l.isPerDiem ? '' : l.amount.replace('.', ','), vatAmount: l.isPerDiem ? '' : l.vatAmount.replace('.', ','), days: l.days ?? '' })) },
  [report]);
  const form = useForm<Input, unknown, Values>({ resolver: zodResolver(schema) as Resolver<Input, unknown, Values>, defaultValues: defaults });
  const guard = useUnsavedChanges(form.formState.isDirty);
  const { fields: lines, append, remove } = useFieldArray({ control: form.control, name: 'lines' });
  const employeeId = useWatch({ control: form.control, name: 'employeeId' });
  const watched = useWatch({ control: form.control, name: 'lines' });
  const perDiemTypes = useMemo(() => new Set(page.lookups.expenseTypes.filter((e) => e.isPerDiem).map((e) => e.id)), [page.lookups.expenseTypes]);
  const employees = useMemo<Option[]>(() => page.lookups.employees.map((e) => ({ value: String(e.id), text: `${e.name} · ${e.employeeNo}` })),
    [page.lookups.employees]);
  const advances = useMemo<Option[]>(() => page.openAdvances.filter((a) => a.employeeId === employeeId || a.id === report?.cashAdvanceId)
    .map((a) => ({ value: String(a.id), text: `AV ${a.id} · ${formatAmount(a.amount)} ${a.currencyCode}` })), [page.openAdvances, employeeId, report]);
  const types = useMemo<Option[]>(() => page.lookups.expenseTypes.map((e) => ({ value: String(e.id), text: e.name })), [page.lookups.expenseTypes]);
  const kinds = useMemo<Option[]>(() => [Justification.CompanyInvoice, Justification.Receipt, Justification.Other]
    .map((k) => ({ value: String(k), text: t(justificationLabel(k)) })), []);

  const refresh = () => Promise.all([queryClient.invalidateQueries({ queryKey: advanceKeys.all }), queryClient.invalidateQueries({ queryKey: cashKeys.all })]);
  const fail = (e: unknown) => {
    const message = applyServerErrors(e, form.setError, ['employeeId', 'cashAdvanceId', 'documentDate', 'description']);
    setError(message);
    notify(message ?? t('common.notSaved'), 'error');
  };
  const save = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      const saved = await advanceApi.saveReport(toReportBody(values, report, perDiemTypes));
      form.reset(form.getValues());
      guard.markClean();
      await refresh();
      notify(t('common.saved'));
      onSaved(saved.id);
    } catch (e) {
      fail(e);
    }
  }, () => notify(t('common.notSaved'), 'error'));
  const step = async (action: () => Promise<unknown>, message: string) => {
    setError(undefined);
    try {
      await action();
      await refresh();
      notify(message);
    } catch (e) {
      fail(e);
    }
  };

  const canCreate = user.permissions.includes('Fin.ExpenseReport.Create');
  return (
    <form className="node-form" noValidate onSubmit={(e) => void save(e)} aria-label={report?.number ?? t('report.newTitle')}>
      <div className="panel-head">
        <div>
          <h2>
            {report === undefined ? t('report.newTitle') : `${report.number ?? t('cash.noNumber')} · ${report.employeeName}`}{' '}
            {report !== undefined && <span className="pill info">{t(reportStatusLabel(report.status))}</span>}
          </h2>
          {report !== undefined && (
            <div className="chips-row">
              <span className="chip static">{t('report.advance', { amount: formatAmount(report.advanceAmount) })}</span>
              <span className="chip static">{t('report.justified', { amount: formatAmount(report.totalJustified) })}</span>
              <span className="chip static"><strong>{t('report.difference', { amount: formatAmount(report.difference) })}</strong></span>
            </div>
          )}
        </div>
      </div>
      <ErrorBanner message={error} />
      <fieldset className="form-body plain" disabled={!draft || !canCreate}>
        <div className="grid g3">
          <SelectField control={form.control} name="employeeId" label={`${t('cash.field.employee')} *`} options={employees} numeric />
          <SelectField control={form.control} name="cashAdvanceId" label={t('report.field.advance')} options={advances} allowEmpty={t('report.noAdvance')} numeric />
          <TextField control={form.control} name="documentDate" label={`${t('cash.field.date')} *`} />
          <TextField control={form.control} name="description" label={t('cash.field.description')} className="span3" />
        </div>
        <h3 className="section">{t('report.lines')}</h3>
        <table className="inline-table cash-lines">
          <thead>
            <tr>
              <th scope="col">{t('report.col.type')}</th>
              <th scope="col">{t('report.col.justification')}</th>
              <th scope="col">{t('report.col.documentNo')}</th>
              <th scope="col" className="num">{t('report.col.amountOrDays')}</th>
              <th scope="col" className="num">{t('report.col.vat')}</th>
              <th scope="col"><span className="sr-only">{t('treasury.cashiers.remove')}</span></th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line, index) => {
              const perDiem = watched[index]?.expenseTypeId != null && perDiemTypes.has(watched[index].expenseTypeId);
              const saved = report?.lines[index];
              return (
                <tr key={line.id}>
                  <td><SelectField control={form.control} name={`lines.${index}.expenseTypeId`} label={t('report.col.type')} options={types} numeric className="compact" /></td>
                  <td>{perDiem ? t('report.justification.perDiem')
                    : <SelectField control={form.control} name={`lines.${index}.justificationKind`} label={t('report.col.justification')} options={kinds} numeric className="compact" />}</td>
                  <td><TextField control={form.control} name={`lines.${index}.documentNo`} label={t('report.col.documentNo')} className="compact" /></td>
                  <td>
                    {perDiem ? <TextField control={form.control} name={`lines.${index}.days`} label={t('report.col.days')} className="compact num" />
                      : <TextField control={form.control} name={`lines.${index}.amount`} label={t('report.col.amountOrDays')} className="compact num" />}
                    {perDiem && saved?.isPerDiem === true && <div className="muted small">{t('report.perDiemSplit', { amount: formatAmount(saved.amount),
                      nonTaxable: formatAmount(saved.nonTaxableAmount), taxable: formatAmount(saved.taxableAmount) })}</div>}
                  </td>
                  <td>{perDiem ? t('common.none') : <TextField control={form.control} name={`lines.${index}.vatAmount`} label={t('report.col.vat')} className="compact num" />}</td>
                  <td>{draft && lines.length > 1 && <button type="button" className="btn ghost small" onClick={() => remove(index)}>{t('treasury.cashiers.remove')}</button>}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
        {draft && canCreate && <button type="button" className="btn ghost" onClick={() => append(emptyLine)}>{t('cash.addLine')}</button>}
      </fieldset>
      {report?.status === ReportStatus.Approved && report.difference !== '0.00' && (
        <div className="field">
          <label className="label" htmlFor="settlement-cash">{t('report.settlementCash')}</label>
          <select id="settlement-cash" className="chip" value={settlementCash ?? ''} onChange={(e) => setSettlementCash(Number(e.target.value))}>
            {page.lookups.cashAccounts.map((a) => <option key={a.id} value={a.id}>{`${a.code} · ${a.currencyCode}`}</option>)}
          </select>
        </div>
      )}
      {report?.settlementCashDocumentId != null && (
        <div className="banner info">
          {t('report.settlement', { number: report.settlementNumber ?? t('cash.noNumber') })}{' '}
          <Link to={documentPath({ id: report.settlementCashDocumentId })}>{t('report.openSettlement')}</Link>
        </div>
      )}
      <div className="form-footer">
        <span className="muted small">{report === undefined ? t('report.hint') : [report.createdByName == null ? undefined : t('report.createdBy', { name: report.createdByName }),
          report.approvedByName == null ? undefined : t('report.approvedBy', { name: report.approvedByName })].filter(Boolean).join(' · ')}</span>
        <div className="actions">
          {draft && canCreate && <button type="submit" className="btn ghost" disabled={form.formState.isSubmitting}>{t('cash.saveDraft')}</button>}
          {report?.status === ReportStatus.Draft && canCreate && !form.formState.isDirty && (
            <button type="button" className="btn primary" onClick={() => void step(() => advanceApi.submit(report.id, report.rowVersion), t('report.submitted'))}>
              {t('report.submit')}</button>)}
          {report?.status === ReportStatus.Submitted && user.permissions.includes('Fin.ExpenseReport.Approve') && (
            <button type="button" className="btn primary" onClick={() => void step(() => advanceApi.approve(report.id, report.rowVersion), t('report.approved'))}>
              {t('report.approve')}</button>)}
          {report?.status === ReportStatus.Approved && user.permissions.includes('Fin.ExpenseReport.Validate') && (
            <button type="button" className="btn primary" onClick={() => void step(() => advanceApi.validate(report.id, report.rowVersion,
              report.difference === '0.00' ? null : settlementCash ?? null), t('report.validated'))}>{t('report.validate')}</button>)}
        </div>
      </div>
    </form>
  );
}
