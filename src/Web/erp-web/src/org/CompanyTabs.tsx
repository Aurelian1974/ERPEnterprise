import { useEffect, useState } from 'react';
import { useForm, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { ColumnDirective, ColumnsDirective, GridComponent } from '@syncfusion/ej2-react-grids';
import { Link } from 'react-router';
import { z } from 'zod';
import { ErrorBanner, SwitchField, TextField } from '../components/Field';
import { applyServerErrors, required } from '../components/forms';
import { t } from '../i18n';
import { orgApi, orgKeys, useNumberSeries, type BankAccount, type CompanyDetail } from './orgApi';

const bankSchema = z.object({
  iban: required.max(42),
  bankName: required.max(100),
  currency: required.regex(/^[A-Z]{3}$/, 'Moneda are 3 litere, de exemplu RON.'),
  isDefault: z.boolean(),
  isActive: z.boolean(),
});
type BankValues = z.infer<typeof bankSchema>;

/** Conturile bancare ale firmei (macheta 03): un singur cont implicit pe moneda, cel tiparit pe facturi. */
export function BankAccountsTab({ company, canManage }: { readonly company: CompanyDetail; readonly canManage: boolean }) {
  const [selected, setSelected] = useState<BankAccount | 'new' | undefined>();
  const account = selected === 'new' ? undefined : selected;
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | undefined>();
  const defaults: BankValues = account === undefined
    ? { iban: '', bankName: '', currency: 'RON', isDefault: company.bankAccounts.length === 0, isActive: true }
    : { iban: account.iban, bankName: account.bankName, currency: account.currency, isDefault: account.isDefault, isActive: account.isActive };
  const form = useForm<BankValues>({ resolver: zodResolver(bankSchema) as Resolver<BankValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [selected]); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      await orgApi.saveBankAccount({ ...values, id: account?.id ?? null, companyId: company.id, rowVersion: account?.rowVersion ?? null });
      await queryClient.invalidateQueries({ queryKey: orgKeys.company(company.id) });
      setSelected(undefined);
    } catch (e) {
      setError(applyServerErrors(e, form.setError, Object.keys(defaults)));
    }
  });

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
          rowSelected={(args: { data: BankAccount }) => canManage && setSelected(args.data)}
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
      )}
    </div>
  );
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
