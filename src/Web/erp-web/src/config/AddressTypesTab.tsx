import { useEffect, useMemo, useState } from 'react';
import { useForm, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { ColumnDirective, ColumnsDirective, GridComponent, Inject, Sort } from '@syncfusion/ej2-react-grids';
import { z } from 'zod';
import { ErrorBanner, NumberField, SwitchField, TextField } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { applyServerErrors, required } from '../components/forms';
import { t } from '../i18n';
import { orgApi, orgKeys, useOrgLookups, type AddressType } from '../org/orgApi';

const addressTypeSchema = z.object({
  code: required.regex(/^[A-Z][A-Z0-9_]{0,29}$/, 'Codul are litere mari, cifre sau „_”, începând cu literă (cel mult 30).'),
  name: required.max(100),
  sortOrder: z.number().int().min(0).max(9999),
  isActive: z.boolean(),
});
type AddressTypeValues = z.infer<typeof addressTypeSchema>;

/** Configurare › Tipuri de adrese: nomenclatorul folosit de adresele firmelor (02.01, 3.1). */
export function AddressTypesTab() {
  const lookups = useOrgLookups();
  const [selected, setSelected] = useState<AddressType | 'new' | undefined>();
  const rows = useMemo(
    () => (lookups.data?.addressTypes ?? []).map((x) => ({ ...x, systemText: x.isSystem ? t('addressType.system') : t('common.none') })),
    [lookups.data],
  );

  return (
    <div className="split series">
      <section className="card" aria-labelledby="address-types-title">
        <div className="card-head">
          <h2 id="address-types-title">{t('addressType.title')}</h2>
          <button type="button" className="btn primary" onClick={() => setSelected('new')}>
            {t('addressType.new')}
          </button>
        </div>
        <p className="muted">{t('addressType.hint')}</p>
        {rows.length === 0 ? (
          <p className="muted">{lookups.isPending ? t('app.loading') : t('addressType.empty')}</p>
        ) : (
          <GridComponent dataSource={rows} allowSorting height="auto" rowSelected={(args: { data: AddressType }) => setSelected(args.data)}>
            <ColumnsDirective>
              <ColumnDirective field="name" headerText={t('addressType.name')} />
              <ColumnDirective field="code" headerText={t('addressType.code')} width="180" />
              <ColumnDirective field="sortOrder" headerText={t('addressType.order')} width="90" textAlign="Right" />
              <ColumnDirective field="systemText" headerText={t('addressType.system')} width="90" />
              <ColumnDirective field="isActive" headerText={t('common.active')} width="90" displayAsCheckBox type="boolean" />
            </ColumnsDirective>
            <Inject services={[Sort]} />
          </GridComponent>
        )}
      </section>
      <section className="card detail-card">
        {selected === undefined ? (
          <p className="muted empty-detail">{t('org.selectNode')}</p>
        ) : (
          <AddressTypeForm
            key={selected === 'new' ? 'new' : `${selected.id}-${selected.rowVersion}`}
            type={selected === 'new' ? undefined : selected}
            onDone={() => setSelected(undefined)}
          />
        )}
      </section>
    </div>
  );
}

function AddressTypeForm({ type, onDone }: { readonly type: AddressType | undefined; readonly onDone: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const defaults: AddressTypeValues = type === undefined
    ? { code: '', name: '', sortOrder: 100, isActive: true }
    : { code: type.code, name: type.name, sortOrder: type.sortOrder, isActive: type.isActive };
  const form = useForm<AddressTypeValues>({ resolver: zodResolver(addressTypeSchema) as Resolver<AddressTypeValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [type?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      await orgApi.saveAddressType({ ...values, id: type?.id ?? null, rowVersion: type?.rowVersion ?? null });
      await queryClient.invalidateQueries({ queryKey: orgKeys.lookups });
      notify(t('common.saved'));
      onDone();
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(e)}>
      <h2 className="form-title">{type === undefined ? t('addressType.new').replace('+ ', '') : type.name}</h2>
      <ErrorBanner message={error} />
      <div className="form-body">
        <div className="grid g2">
          <TextField control={form.control} name="code" label={t('addressType.code')} upper disabled={type !== undefined} />
          <TextField control={form.control} name="name" label={t('addressType.name')} />
          <NumberField control={form.control} name="sortOrder" label={t('addressType.order')} min={0} max={9999} grouping={false} />
        </div>
        <SwitchField control={form.control} name="isActive" label={t('field.isActive')} disabled={type?.isSystem === true} />
      </div>
      <div className="form-footer">
        <span className="muted" />
        <div className="actions">
          <button type="button" className="btn ghost" onClick={onDone}>
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
