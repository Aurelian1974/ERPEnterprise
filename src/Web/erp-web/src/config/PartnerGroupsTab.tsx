import { useEffect, useState } from 'react';
import { useForm, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { ColumnDirective, ColumnsDirective, GridComponent, Inject, Sort } from '@syncfusion/ej2-react-grids';
import { z } from 'zod';
import { ErrorBanner, NumberField, SwitchField, TextField } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { applyServerErrors, code, required } from '../components/forms';
import { t } from '../i18n';
import { partnerApi, partnerKeys, usePartnerLookups, type PartnerGroup } from '../partners/partnerApi';

const groupSchema = z.object({
  code,
  name: required.max(100),
  sortOrder: z.number().int().min(0).max(9999),
  isActive: z.boolean(),
});
type GroupValues = z.infer<typeof groupSchema>;

/** Configurare › Grupe de parteneri: nomenclatorul grupelor (distribuitori, retail, servicii; 02.01, 3.2). */
export function PartnerGroupsTab() {
  const lookups = usePartnerLookups();
  const [selected, setSelected] = useState<PartnerGroup | 'new' | undefined>();
  const rows = lookups.data?.groups ?? [];

  return (
    <div className="split series">
      <section className="card" aria-labelledby="partner-groups-title">
        <div className="card-head">
          <h2 id="partner-groups-title">{t('partnerGroup.title')}</h2>
          <button type="button" className="btn primary" onClick={() => setSelected('new')}>
            {t('partnerGroup.new')}
          </button>
        </div>
        <p className="muted">{t('partnerGroup.hint')}</p>
        {rows.length === 0 ? (
          <p className="muted">{lookups.isPending ? t('app.loading') : t('partnerGroup.empty')}</p>
        ) : (
          <GridComponent dataSource={rows} allowSorting height="auto" rowSelected={(args: { data: PartnerGroup }) => setSelected(args.data)}>
            <ColumnsDirective>
              <ColumnDirective field="name" headerText={t('field.name')} />
              <ColumnDirective field="code" headerText={t('field.code')} width="160" />
              <ColumnDirective field="sortOrder" headerText={t('addressType.order')} width="90" textAlign="Right" />
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
          <GroupForm key={selected === 'new' ? 'new' : `${selected.id}-${selected.rowVersion}`} group={selected === 'new' ? undefined : selected}
            onDone={() => setSelected(undefined)} />
        )}
      </section>
    </div>
  );
}

function GroupForm({ group, onDone }: { readonly group: PartnerGroup | undefined; readonly onDone: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const defaults: GroupValues = group === undefined
    ? { code: '', name: '', sortOrder: 100, isActive: true }
    : { code: group.code, name: group.name, sortOrder: group.sortOrder, isActive: group.isActive };
  const form = useForm<GroupValues>({ resolver: zodResolver(groupSchema) as Resolver<GroupValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [group?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      await partnerApi.saveGroup({ ...values, id: group?.id ?? null, rowVersion: group?.rowVersion ?? null });
      await queryClient.invalidateQueries({ queryKey: partnerKeys.lookups });
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
      <h2 className="form-title">{group === undefined ? t('partnerGroup.new').replace('+ ', '') : group.name}</h2>
      <ErrorBanner message={error} />
      <div className="form-body">
        <div className="grid g2">
          <TextField control={form.control} name="code" label={t('field.code')} upper />
          <TextField control={form.control} name="name" label={t('field.name')} />
          <NumberField control={form.control} name="sortOrder" label={t('addressType.order')} min={0} max={9999} grouping={false} />
        </div>
        <SwitchField control={form.control} name="isActive" label={t('field.isActive')} onText={t('common.activeF')} />
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
