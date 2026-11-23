import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useOutletContext, useSearchParams } from 'react-router';
import { Controller, useFieldArray, useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, SelectField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { SearchSelectField } from '../components/SearchSelectField';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors } from '../components/forms';
import { t } from '../i18n';
import { isoToRo, roToIso } from '../partners/partnerModel';
import { documentPath } from './CashDeskPage';
import { InvoicePicker } from './InvoicePicker';
import {
  CashKind, cashApi, cashKeys, cashKindLabel, cashKinds, CashStatus, formatAmount, operationsFor, statusLabel, useCashDesk, useCashDocument,
  type CashDeskLookups, type CashDocumentDetail,
} from './cashApi';

/** Plătitorul / beneficiarul documentului: partener, angajat, persoană fără fișă sau niciunul. */
type PayerMode = 'partner' | 'employee' | 'person' | 'none';

async function searchPartners(text: string): Promise<Option[]> {
  const page = unwrap(await api.GET('/api/core/partners', { params: { query: { ...(text === '' ? {} : { Search: text }), IsActive: true, PageSize: 30 } } }));
  return page.items.map((p) => ({ value: String(p.id), text: p.fiscalCode == null ? p.name : `${p.name} · ${p.fiscalCode}` }));
}

const money = z.string().trim().refine((v) => /^\d{1,15}([.,]\d{1,2})?$/.test(v), t('validation.money')).transform((v) => v.replace(',', '.'));

const lineSchema = z.object({
  operationTypeId: z.number().nullable().refine((v) => v !== null, t('cash.validation.operation')),
  amount: money,
  counterTreasuryAccountId: z.number().nullable(),
  settlementRef: z.string().trim(),
  note: z.string().trim(),
});

const documentSchema = z
  .object({
    kind: z.number(),
    treasuryAccountId: z.number(),
    documentDate: z.string().trim().refine((v) => roToIso(v) !== undefined, t('validation.date')),
    payer: z.enum(['partner', 'employee', 'person', 'none']),
    partnerId: z.number().nullable(),
    employeeId: z.number().nullable(),
    personName: z.string().trim(),
    personIdDocument: z.string().trim(),
    description: z.string().trim(),
    openingAmount: z.string().trim(),
    lines: z.array(lineSchema),
  })
  .superRefine((v, ctx) => {
    if (v.payer === 'partner' && v.partnerId === null) ctx.addIssue({ code: 'custom', path: ['partnerId'], message: t('common.required') });
    if (v.payer === 'employee' && v.employeeId === null) ctx.addIssue({ code: 'custom', path: ['employeeId'], message: t('common.required') });
    if (v.payer === 'person' && v.personName === '') ctx.addIssue({ code: 'custom', path: ['personName'], message: t('common.required') });
    if (v.kind === CashKind.Receipt && v.payer !== 'partner' && v.payer !== 'person') {
      ctx.addIssue({ code: 'custom', path: ['payer'], message: t('cash.validation.receiptPayer') });
    }
    if (v.kind === CashKind.OpeningBalance && !/^\d{1,15}([.,]\d{1,2})?$/.test(v.openingAmount)) {
      ctx.addIssue({ code: 'custom', path: ['openingAmount'], message: t('validation.money') });
    }
    if (v.kind !== CashKind.OpeningBalance && v.lines.length === 0) {
      ctx.addIssue({ code: 'custom', path: ['description'], message: t('cash.validation.lines') });
    }
  });
type DocumentInput = z.input<typeof documentSchema>;
type DocumentValues = z.output<typeof documentSchema>;

const fields = ['kind', 'documentDate', 'partnerId', 'employeeId', 'personName', 'personIdDocument', 'description', 'openingAmount'];

const emptyLine = { operationTypeId: null, amount: '', counterTreasuryAccountId: null, settlementRef: '', note: '' };

function defaultsFor(document: CashDocumentDetail | undefined, kind: number, accountId: number, day: string): DocumentInput {
  if (document === undefined) {
    return { kind, treasuryAccountId: accountId, documentDate: isoToRo(day), payer: kind === CashKind.Receipt ? 'partner' : 'none', partnerId: null,
      employeeId: null, personName: '', personIdDocument: '', description: '', openingAmount: '',
      lines: kind === CashKind.OpeningBalance ? [] : [emptyLine] };
  }
  const payer: PayerMode = document.partnerId != null ? 'partner' : document.employeeId != null ? 'employee' : document.personName != null ? 'person' : 'none';
  return {
    kind: document.kind, treasuryAccountId: document.treasuryAccountId, documentDate: isoToRo(document.documentDate), payer,
    partnerId: document.partnerId ?? null, employeeId: document.employeeId ?? null, personName: document.personName ?? '', personIdDocument: '',
    description: document.description ?? '', openingAmount: document.kind === CashKind.OpeningBalance ? document.totalAmount.replace('.', ',') : '',
    lines: document.lines.map((l) => ({ operationTypeId: l.operationTypeId, amount: l.amount.replace('.', ','),
      counterTreasuryAccountId: l.counterTreasuryAccountId ?? null, settlementRef: l.settlementRef ?? '', note: l.note ?? '' })),
  };
}

/** Corpul cererii de salvare: doar plătitorul ales, datele ISO, liniile doar pentru documentele cu linii. */
export function toCashBody(values: DocumentValues, document: CashDocumentDetail | undefined) {
  const opening = values.kind === CashKind.OpeningBalance;
  return {
    id: document?.id ?? null,
    rowVersion: document?.rowVersion ?? null,
    treasuryAccountId: values.treasuryAccountId,
    kind: values.kind,
    documentDate: roToIso(values.documentDate) ?? '',
    partnerId: values.payer === 'partner' ? values.partnerId : null,
    employeeId: values.payer === 'employee' ? values.employeeId : null,
    personName: values.payer === 'person' ? values.personName : null,
    personIdDocument: values.payer === 'person' && values.personIdDocument !== '' ? values.personIdDocument : null,
    description: values.description === '' ? null : values.description,
    openingAmount: opening ? values.openingAmount.replace(',', '.') : null,
    lines: opening ? [] : values.lines.map((l) => ({ operationTypeId: l.operationTypeId ?? 0, amount: l.amount,
      counterTreasuryAccountId: l.counterTreasuryAccountId, settlementRef: l.settlementRef === '' ? null : l.settlementRef, costCenterId: null,
      projectId: null, note: l.note === '' ? null : l.note })),
  };
}

/**
 * Trezorerie › Casa › Document de casă (02.02, F1 3.3 și 4.1; macheta f1-03): chitanța, dispozițiile de încasare și de plată, soldul
 * inițial. Ciorna se editează, se salvează sau se șterge; validarea alocă numărul din serie și cere confirmarea avertizărilor de plafon;
 * un document validat e doar citire și se corectează prin storno.
 */
export function CashDocumentPage() {
  const [params] = useSearchParams();
  const idParam = Number(params.get('id'));
  const id = Number.isInteger(idParam) && idParam > 0 ? idParam : undefined;
  const document = useCashDocument(id);
  const accountParam = Number(params.get('casa'));
  const accountId = document.data?.treasuryAccountId ?? (Number.isInteger(accountParam) && accountParam > 0 ? accountParam : undefined);
  const day = document.data?.documentDate ?? params.get('zi') ?? undefined;
  const desk = useCashDesk(accountId, day);
  const kindParam = Number(params.get('fel'));

  if (desk.data === undefined || (id !== undefined && document.data === undefined)) {
    const error = desk.error ?? document.error;
    return <div className="page" role="status">{error == null ? t('app.loading') : error.message}</div>;
  }
  const deskDay = desk.data.day;
  if (deskDay == null) {
    return <div className="page"><div className="banner info">{t('cash.noAccounts')}</div></div>;
  }
  return (
    <CashDocumentForm key={`${id ?? 'nou'}-${document.data?.rowVersion ?? ''}`} document={document.data} lookups={desk.data.lookups}
      kind={cashKinds.includes(kindParam as never) ? kindParam : CashKind.Receipt} accountId={deskDay.treasuryAccountId} day={deskDay.day}
      accountLabel={`${deskDay.code} · ${deskDay.currencyCode}`} />
  );
}

function CashDocumentForm({ document, lookups, kind: initialKind, accountId, day, accountLabel }: {
  readonly document: CashDocumentDetail | undefined;
  readonly lookups: CashDeskLookups;
  readonly kind: number;
  readonly accountId: number;
  readonly day: string;
  readonly accountLabel: string;
}) {
  const user = useOutletContext<CurrentUser>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const defaults = useMemo(() => defaultsFor(document, initialKind, accountId, day), [document, initialKind, accountId, day]);
  const form = useForm<DocumentInput, unknown, DocumentValues>({
    resolver: zodResolver(documentSchema) as Resolver<DocumentInput, unknown, DocumentValues>,
    defaultValues: defaults,
  });
  const guard = useUnsavedChanges(form.formState.isDirty);
  const { fields: lines, append, remove } = useFieldArray({ control: form.control, name: 'lines' });
  const kind = useWatch({ control: form.control, name: 'kind' });
  const payer = useWatch({ control: form.control, name: 'payer' });
  const watchedLines = useWatch({ control: form.control, name: 'lines' });
  const watchedPartner = useWatch({ control: form.control, name: 'partnerId' });
  const draft = document === undefined || document.status === CashStatus.Draft;
  const canEdit = draft && user.permissions.includes('Fin.Cash.Create');
  const canValidate = draft && document !== undefined && user.permissions.includes('Fin.Cash.Validate');
  const canReverse = document?.status === CashStatus.Validated && document.reversalOfId == null && user.permissions.includes('Fin.Cash.Reverse');

  const operations = useMemo<Option[]>(() => operationsFor(lookups.operationTypes, kind).map((o) => ({ value: String(o.id), text: `${o.code} · ${o.name}` })),
    [lookups.operationTypes, kind]);
  const counterAccounts = useMemo<Option[]>(() => lookups.counterAccounts.filter((a) => a.id !== accountId)
    .map((a) => ({ value: String(a.id), text: `${a.code} · ${a.name}` })), [lookups.counterAccounts, accountId]);
  const employees = useMemo<Option[]>(() => lookups.employees.map((e) => ({ value: String(e.id), text: `${e.name} · ${e.employeeNo}` })),
    [lookups.employees]);
  const partnerCurrent = useMemo<Option | undefined>(() => document?.partnerId == null ? undefined
    : { value: String(document.partnerId), text: document.partnerName ?? '' }, [document]);
  // Facturile deschise ale partenerului, la tipurile cu decontare pe facturi (1 client, 2 furnizor; F3 4.2).
  const invoiceRole = (index: number) => {
    const kindOf = lookups.operationTypes.find((o) => o.id === watchedLines[index]?.operationTypeId)?.settlementKind;
    return payer === 'partner' && watchedPartner != null && (kindOf === 1 || kindOf === 2) ? kindOf : undefined;
  };
  const needsCounter = (index: number) => lookups.operationTypes.find((o) => o.id === watchedLines[index]?.operationTypeId)?.requiresCounterAccount === true;

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: cashKeys.all });
  };

  const save = async (values: DocumentValues) => {
    const saved = await cashApi.save(toCashBody(values, document));
    form.reset(form.getValues());
    guard.markClean();
    return saved;
  };

  const fail = (e: unknown) => {
    const message = applyServerErrors(e, form.setError, fields);
    setError(message);
    notify(message ?? t('common.notSaved'), 'error');
  };

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      const saved = await save(values);
      await refresh();
      notify(t('common.saved'));
      if (document === undefined) void navigate(documentPath({ id: saved.id }), { replace: true });
    } catch (e) {
      fail(e);
    }
  }, () => notify(t('common.notSaved'), 'error'));

  const validate = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      const saved = form.formState.isDirty || document === undefined ? await save(values) : { id: document.id, rowVersion: document.rowVersion };
      let result;
      try {
        result = await cashApi.validate(saved.id, saved.rowVersion, false);
      } catch (e) {
        if (!(e instanceof ApiError && e.code === 'FIN_CASH_LIMIT_WARNING_UNCONFIRMED' && window.confirm(`${e.message}\n\n${t('cash.confirmWarning')}`))) {
          throw e;
        }
        result = await cashApi.validate(saved.id, saved.rowVersion, true);
      }
      await refresh();
      notify(t('cash.validated', { number: result.number ?? t('cash.noNumber'), balance: formatAmount(result.dayClosingBalance) }));
      void navigate(documentPath({ id: result.cashDocumentId }), { replace: true });
    } catch (e) {
      await refresh();
      fail(e);
    }
  }, () => notify(t('common.notSaved'), 'error'));

  const removeDraft = async () => {
    if (document === undefined || !window.confirm(t('cash.confirmDelete'))) return;
    try {
      await cashApi.remove(document.id, document.rowVersion);
      guard.markClean();
      await refresh();
      notify(t('cash.deleted'));
      void navigate(`/finante/trezorerie/casa?casa=${accountId}&zi=${day}`);
    } catch (e) {
      fail(e);
    }
  };

  const reverse = async () => {
    if (document === undefined || !window.confirm(t('cash.confirmReverse'))) return;
    try {
      const result = await cashApi.reverse(document.id, document.rowVersion);
      await refresh();
      notify(t('cash.reversed', { number: result.number ?? t('cash.noNumber') }));
      void navigate(documentPath({ id: result.cashDocumentId }));
    } catch (e) {
      fail(e);
    }
  };

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'F9' && canEdit) {
        event.preventDefault();
        void validate();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [canEdit, validate]);

  const title = document === undefined ? t('cash.newTitle', { kind: t(cashKindLabel(kind)) }) : `${t(cashKindLabel(document.kind))} ${document.number ?? ''}`.trim();
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.treasury')} › {t('menu.cashDesk')} › {title}</div>
      <div className="head">
        <div>
          <h1>{title}</h1>
          <div className="sub2">{t('cash.documentSubtitle', { account: accountLabel })}</div>
        </div>
        <div className="actions">
          <Link to={`/finante/trezorerie/casa?casa=${accountId}&zi=${document?.documentDate ?? day}`} className="btn ghost">{t('cash.back')}</Link>
        </div>
      </div>

      <div className="split cash-document">
        <form className="card node-form" noValidate onSubmit={(e) => void submit(e)} aria-label={title}>
          <div className="panel-head">
            <div>
              <h2>
                {document === undefined ? t('cash.status.draft') : t(statusLabel(document))}{' '}
                <span className="pill off">{document?.number ?? t('cash.noNumber')}</span>
              </h2>
              {document?.reversalOfNumber != null && <div className="muted">{t('cash.reversalOf', { number: document.reversalOfNumber })}</div>}
              {document?.reversedByNumber != null && <div className="muted">{t('cash.reversedBy', { number: document.reversedByNumber })}</div>}
            </div>
          </div>
          <ErrorBanner message={error} />
          <fieldset className="form-body plain" disabled={!canEdit}>
            <Controller control={form.control} name="kind" render={({ field }) => (
              <div className="field">
                <span className="label" id="cash-kind-label">{t('cash.field.kind')}</span>
                <div className="segmented" role="radiogroup" aria-labelledby="cash-kind-label">
                  {cashKinds.map((value) => (
                    <button key={value} type="button" role="radio" aria-checked={field.value === value} className={field.value === value ? 'on' : ''}
                      disabled={document !== undefined} onClick={() => {
                        field.onChange(value);
                        form.setValue('payer', value === CashKind.Receipt ? 'partner' : 'none');
                        form.setValue('lines', value === CashKind.OpeningBalance ? [] : [emptyLine]);
                      }}>
                      {t(cashKindLabel(value))}
                    </button>
                  ))}
                </div>
              </div>
            )} />
            {kind !== CashKind.OpeningBalance && (
              <Controller control={form.control} name="payer" render={({ field, fieldState }) => (
                <div className="field">
                  <span className="label" id="cash-payer-label">{t(kind === CashKind.CashOut ? 'cash.field.beneficiary' : 'cash.field.payer')}</span>
                  <div className="segmented" role="radiogroup" aria-labelledby="cash-payer-label">
                    {(['partner', 'employee', 'person', 'none'] as const).map((value) => (
                      <button key={value} type="button" role="radio" aria-checked={field.value === value} className={field.value === value ? 'on' : ''}
                        onClick={() => field.onChange(value)}>
                        {t(`cash.payer.${value}`)}
                      </button>
                    ))}
                  </div>
                  {fieldState.error !== undefined && <span className="field-error" role="alert">{fieldState.error.message}</span>}
                </div>
              )} />
            )}
            <div className="grid g3">
              {payer === 'partner' && kind !== CashKind.OpeningBalance && (
                <SearchSelectField control={form.control} name="partnerId" label={`${t('cash.field.partner')} *`} className="span2" current={partnerCurrent}
                  search={searchPartners} />
              )}
              {payer === 'employee' && kind !== CashKind.OpeningBalance && (
                <SelectField control={form.control} name="employeeId" label={`${t('cash.field.employee')} *`} options={employees} numeric className="span2" />
              )}
              {payer === 'person' && kind !== CashKind.OpeningBalance && (
                <>
                  <TextField control={form.control} name="personName" label={`${t('cash.field.personName')} *`} />
                  <TextField control={form.control} name="personIdDocument"
                    label={document?.hasPersonId === true ? t('cash.field.personIdKept') : t('cash.field.personId')} />
                </>
              )}
              <TextField control={form.control} name="documentDate" label={`${t('cash.field.date')} *`} />
              {kind === CashKind.OpeningBalance && (
                <TextField control={form.control} name="openingAmount" label={`${t('cash.field.openingAmount')} *`} />
              )}
              <TextField control={form.control} name="description" label={t('cash.field.description')} className="span3" />
            </div>

            {kind !== CashKind.OpeningBalance && (
              <>
                <h3 className="section">{t('cash.lines')}</h3>
                <table className="inline-table cash-lines">
                  <thead>
                    <tr>
                      <th scope="col">{t('cash.line.operation')}</th>
                      <th scope="col">{t('cash.line.counter')}</th>
                      <th scope="col">{t('cash.line.settlement')}</th>
                      <th scope="col" className="num">{t('cash.line.amount')}</th>
                      <th scope="col"><span className="sr-only">{t('treasury.cashiers.remove')}</span></th>
                    </tr>
                  </thead>
                  <tbody>
                    {lines.map((line, index) => (
                      <tr key={line.id}>
                        <td>
                          <SelectField control={form.control} name={`lines.${index}.operationTypeId`} label={t('cash.line.operation')} options={operations}
                            numeric className="compact" />
                        </td>
                        <td>
                          {needsCounter(index) ? (
                            <SelectField control={form.control} name={`lines.${index}.counterTreasuryAccountId`} label={t('cash.line.counter')}
                              options={counterAccounts} numeric className="compact" />
                          ) : <span className="muted">{t('common.none')}</span>}
                        </td>
                        <td>{invoiceRole(index) !== undefined && watchedPartner != null
                          ? <InvoicePicker control={form.control} name={`lines.${index}.settlementRef`} partnerId={watchedPartner} role={invoiceRole(index) ?? 1}
                            label={t('cash.line.settlement')} className="compact" />
                          : <TextField control={form.control} name={`lines.${index}.settlementRef`} label={t('cash.line.settlement')} className="compact" />}</td>
                        <td><TextField control={form.control} name={`lines.${index}.amount`} label={t('cash.line.amount')} className="compact num" /></td>
                        <td>{canEdit && lines.length > 1 && (
                          <button type="button" className="btn ghost small" onClick={() => remove(index)}>{t('treasury.cashiers.remove')}</button>)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {canEdit && (
                  <button type="button" className="btn ghost" onClick={() => append(emptyLine)}>{t('cash.addLine')}</button>
                )}
              </>
            )}
          </fieldset>
          <div className="form-footer">
            <span className="muted">{document === undefined ? t('cash.numberOnValidation') : t('cash.total', {
              amount: formatAmount(document.totalAmount), currency: document.currencyCode, ron: formatAmount(document.totalAmountRon) })}</span>
            <div className="actions">
              {canEdit && document !== undefined && (
                <button type="button" className="btn ghost" onClick={() => void removeDraft()}>{t('cash.delete')}</button>
              )}
              {canEdit && (
                <button type="submit" className="btn ghost" disabled={form.formState.isSubmitting}>{t('cash.saveDraft')}</button>
              )}
              {(canValidate || (canEdit && document === undefined && user.permissions.includes('Fin.Cash.Validate'))) && (
                <button type="button" className="btn primary" disabled={form.formState.isSubmitting} onClick={() => void validate()}>
                  {t('cash.validate')} <kbd>F9</kbd>
                </button>
              )}
              {canReverse && (
                <button type="button" className="btn danger" onClick={() => void reverse()}>{t('cash.reverse')}</button>
              )}
            </div>
          </div>
        </form>

        <aside className="card cash-side">
          <span className="label">{t('cash.totalLabel')}</span>
          <strong className="cash-total">{document === undefined ? t('common.none') : `${formatAmount(document.totalAmount)} ${document.currencyCode}`}</strong>
          {document !== undefined && document.currencyCode !== 'RON' && (
            <div className="muted small">{t('cash.ronEquivalent', { ron: formatAmount(document.totalAmountRon), rate: document.exchangeRate,
              date: isoToRo(document.rateDate) })}</div>
          )}
          <div className="banner dashed">{t('cash.sideHint')}</div>
          <p className="muted small">{t('cash.keys')}</p>
        </aside>
      </div>
    </div>
  );
}
