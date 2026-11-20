import { useEffect, useMemo, useState } from 'react';
import { useFieldArray, useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner, NumberField, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, required } from '../components/forms';
import { t, type TextKey } from '../i18n';
import { partnerKeys, usePartnerLookups } from '../partners/partnerApi';

type Definition = components['schemas']['PropertyDefinitionAdminDto'];

const ENTITY = 'Partner';
const definitionsKey = ['core', 'property-definitions', ENTITY] as const;

/** Tipurile care pot fi filtru in lista de parteneri (o optiune sau da/nu, ca pe server). */
export const filterableTypes: readonly number[] = [4, 5, 6];
export const listTypes: readonly number[] = [5, 6];

const optionSchema = z.object({
  id: z.number().nullable(),
  code: z.string().trim().regex(/^[A-Za-z0-9][A-Za-z0-9_-]{0,29}$/, t('property.option.required')),
  label: required.max(100),
  isActive: z.boolean(),
});

const schema = z
  .object({
    code: required.regex(/^[A-Za-z][A-Za-z0-9_]{0,29}$/, 'Codul are 1–30 caractere: litere, cifre sau „_”, începând cu literă.'),
    name: required.max(100),
    dataType: z.number().int().min(1).max(7),
    appliesTo: z.number().int().min(1).max(3),
    section: required.max(50),
    sortOrder: z.number().int().min(0).max(9999),
    isRequired: z.boolean(),
    isFilterable: z.boolean(),
    companyId: z.number().nullable(),
    isActive: z.boolean(),
    options: z.array(optionSchema),
  })
  .superRefine((v, ctx) => {
    if (listTypes.includes(v.dataType) && !v.options.some((o) => o.isActive)) {
      ctx.addIssue({ code: 'custom', path: ['options'], message: 'O listă are cel puțin o opțiune activă.' });
    }
  });
type Values = z.infer<typeof schema>;

const typeKey = (type: number) => `property.type.${type}` as TextKey;
const appliesKey = (applies: number) => `property.applies.${applies}` as TextKey;

/**
 * Configurare › Proprietati (macheta 07l; 02.01, 3.2): definitiile campurilor suplimentare ale partenerilor, grupate pe sectiuni,
 * cu optiunile listelor. Articolele vin cu nomenclatorul lor.
 */
export function PropertiesTab() {
  const definitions = useQuery({
    queryKey: definitionsKey,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/property-definitions', { params: { query: { entityType: ENTITY } }, signal })),
  });
  const lookups = usePartnerLookups();
  const [selected, setSelected] = useState<Definition | 'new' | undefined>();
  const [search, setSearch] = useState('');
  const [showInactive, setShowInactive] = useState(false);
  const companies = useMemo(() => new Map((lookups.data?.companies ?? []).map((c) => [c.id, c.name])), [lookups.data]);

  const visible = (definitions.data ?? []).filter((d) => (showInactive || d.isActive)
    && (search.trim() === '' || `${d.code} ${d.name}`.toLocaleLowerCase('ro-RO').includes(search.trim().toLocaleLowerCase('ro-RO'))));
  const sections = [...new Set(visible.map((d) => d.section))];

  return (
    <div className="split series">
      <section className="card" aria-labelledby="properties-title">
        <div className="card-head">
          <div className="segmented small" role="tablist" aria-label={t('property.title')}>
            <button type="button" role="tab" aria-selected className="on">
              {t('property.entity.Partner', { count: definitions.data?.length ?? 0 })}
            </button>
          </div>
          <div className="chips-row">
            <input className="search" type="search" placeholder={t('property.search')} aria-label={t('property.search')} value={search}
              onChange={(e) => setSearch(e.target.value)} />
            <button type="button" className={showInactive ? 'chip on' : 'chip'} aria-pressed={showInactive} onClick={() => setShowInactive(!showInactive)}>
              {t('property.showInactive')}
            </button>
            <button type="button" className="btn primary" onClick={() => setSelected('new')}>
              {t('property.new')}
            </button>
          </div>
        </div>
        <h2 id="properties-title" className="sr-only">{t('property.title')}</h2>
        {visible.length === 0 ? (
          <p className="muted">{definitions.isPending ? t('app.loading') : t('property.empty')}</p>
        ) : (
          <table className="simple properties-table">
            <thead>
              <tr>
                <th scope="col">{t('property.col.code')}</th>
                <th scope="col">{t('property.col.name')}</th>
                <th scope="col">{t('property.col.type')}</th>
                <th scope="col">{t('property.col.appliesTo')}</th>
                <th scope="col">{t('property.col.rule')}</th>
                <th scope="col">{t('property.col.filter')}</th>
                <th scope="col">{t('property.col.scope')}</th>
              </tr>
            </thead>
            {sections.map((section) => (
              <tbody key={section}>
                <tr className="section-row">
                  <th colSpan={7} scope="colgroup">{section}</th>
                </tr>
                {visible.filter((d) => d.section === section).map((d) => {
                  const current = selected !== 'new' && selected?.id === d.id;
                  return (
                    <tr key={d.id} className={[current ? 'current' : '', d.isActive ? '' : 'inactive'].join(' ').trim()} onClick={() => setSelected(d)}>
                      <td className="mono">
                        <button type="button" className="row-link" aria-current={current ? 'true' : undefined} onClick={(e) => {
                          e.stopPropagation();
                          setSelected(d);
                        }}>
                          {d.code}
                        </button>
                      </td>
                      <td>{d.name}</td>
                      <td><span className="tag type">{t(typeKey(d.dataType))}</span></td>
                      <td>{t(appliesKey(d.appliesTo))}</td>
                      <td>{d.isRequired && <span className="pill warn">{t('property.required')}</span>}</td>
                      <td>{d.isFilterable ? '✓' : ''}</td>
                      <td>{d.companyId == null ? t('property.scope.groupShort') : (companies.get(d.companyId) ?? String(d.companyId))}</td>
                    </tr>
                  );
                })}
              </tbody>
            ))}
          </table>
        )}
        <p className="muted small">{t('property.footer')}</p>
      </section>
      <section className="card detail-card">
        {selected === undefined ? (
          <p className="muted empty-detail">{t('org.selectNode')}</p>
        ) : (
          <DefinitionForm key={selected === 'new' ? 'new' : `${selected.id}-${selected.rowVersion}`} definition={selected === 'new' ? undefined : selected}
            sections={[...new Set((definitions.data ?? []).map((d) => d.section))]}
            companies={(lookups.data?.companies ?? []).map((c) => ({ value: String(c.id), text: c.name }))}
            onDone={(next) => setSelected(next)} />
        )}
      </section>
    </div>
  );
}

function DefinitionForm({ definition, sections, companies, onDone }: {
  readonly definition: Definition | undefined;
  readonly sections: readonly string[];
  readonly companies: readonly Option[];
  readonly onDone: (next: Definition | undefined) => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const defaults: Values = definition === undefined
    ? { code: '', name: '', dataType: 1, appliesTo: 3, section: sections[0] ?? '', sortOrder: 100, isRequired: false, isFilterable: false,
        companyId: null, isActive: true, options: [] }
    : { code: definition.code, name: definition.name, dataType: definition.dataType, appliesTo: definition.appliesTo, section: definition.section,
        sortOrder: definition.sortOrder, isRequired: definition.isRequired, isFilterable: definition.isFilterable, companyId: definition.companyId ?? null,
        isActive: definition.isActive, options: definition.options.map((o) => ({ id: o.id, code: o.code, label: o.label, isActive: o.isActive })) };
  const form = useForm<Values>({ resolver: zodResolver(schema) as Resolver<Values>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [definition?.id]); // eslint-disable-line react-hooks/exhaustive-deps
  const guard = useUnsavedChanges(form.formState.isDirty);
  const options = useFieldArray({ control: form.control, name: 'options' });
  const dataType = useWatch({ control: form.control, name: 'dataType' });
  const used = (definition?.usageCount ?? 0) > 0;
  const isList = listTypes.includes(dataType);
  const canFilter = filterableTypes.includes(dataType);

  // Filtrul si optiunile urmeaza tipul: o proprietate care nu poate fi filtru nu ramane marcata, una care nu e lista nu are optiuni.
  useEffect(() => {
    if (!canFilter && form.getValues('isFilterable')) {
      form.setValue('isFilterable', false, { shouldDirty: true });
    }
  }, [form, canFilter]);

  const typeOptions = useMemo<Option[]>(() => [1, 2, 3, 4, 5, 6, 7].map((type) => ({ value: String(type), text: t(typeKey(type)) })), []);
  const appliesOptions = useMemo<Option[]>(() => [1, 2, 3].map((value) => ({ value: String(value), text: t(appliesKey(value)) })), []);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    const body = {
      ...values,
      id: definition?.id ?? null,
      entityType: ENTITY,
      rowVersion: definition?.rowVersion ?? null,
      options: isList ? values.options.map((o, index) => ({ ...o, sortOrder: (index + 1) * 10 })) : [],
    };
    try {
      const saved = definition === undefined
        ? unwrap(await api.POST('/api/core/property-definitions', { body }))
        : unwrap(await api.PUT('/api/core/property-definitions/{id}', { params: { path: { id: definition.id } }, body }));
      form.reset(form.getValues());
      guard.markClean();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: definitionsKey }),
        queryClient.invalidateQueries({ queryKey: partnerKeys.lookups }),
      ]);
      notify(t('common.saved'));
      const fresh = queryClient.getQueryData<Definition[]>(definitionsKey)?.find((d) => d.id === saved.id);
      onDone(fresh);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(e)}>
      <h2 className="form-title">
        {definition === undefined ? t('property.newTitle') : definition.name}{' '}
        {definition !== undefined && (
          <>
            <span className="tag type">{t(typeKey(definition.dataType))}</span>{' '}
            <span className={definition.isActive ? 'pill ok' : 'pill off'}>{definition.isActive ? t('common.activeF') : t('common.inactiveF')}</span>
          </>
        )}
      </h2>
      <ErrorBanner message={error} />
      <div className="form-body">
        <div className="grid g2">
          <TextField control={form.control} name="code" label={definition === undefined ? t('property.field.code') : `${t('property.field.code')} 🔒`}
            upper disabled={definition !== undefined} />
          <SelectField control={form.control} name="dataType" label={used ? `${t('property.field.dataType')} 🔒 ${t('property.locked')}` : t('property.field.dataType')}
            options={typeOptions} numeric disabled={used} />
          <TextField control={form.control} name="name" label={t('property.field.name')} className="span2" />
          <TextField control={form.control} name="section" label={t('property.field.section')} />
          <SelectField control={form.control} name="appliesTo" label={t('property.field.appliesTo')} options={appliesOptions} numeric />
          <NumberField control={form.control} name="sortOrder" label={t('property.field.sortOrder')} min={0} max={9999} grouping={false} />
          <SelectField control={form.control} name="companyId" label={t('property.field.scope')} options={companies} allowEmpty={t('property.scope.group')} numeric />
        </div>
        <SwitchField control={form.control} name="isRequired" label={t('property.field.required')} />
        <SwitchField control={form.control} name="isFilterable" label={t('property.field.filterable')} disabled={!canFilter} />
        <SwitchField control={form.control} name="isActive" label={t('field.isActive')} onText={t('common.activeF')} />
        {isList && (
          <section aria-labelledby="options-title">
            <h3 id="options-title" className="section">{t('property.options')}</h3>
            {form.formState.errors.options?.message !== undefined && (
              <span className="field-error" role="alert">{form.formState.errors.options.message}</span>
            )}
            <table className="simple options-table">
              <thead>
                <tr>
                  <th scope="col"><span className="sr-only">{t('property.option.up')}</span></th>
                  <th scope="col">{t('property.option.code')}</th>
                  <th scope="col">{t('property.option.label')}</th>
                  <th scope="col">{t('property.option.active')}</th>
                </tr>
              </thead>
              <tbody>
                {options.fields.map((field, index) => {
                  const optionErrors = form.formState.errors.options?.[index];
                  return (
                    <tr key={field.id}>
                      <td className="order">
                        <button type="button" className="icon-link" aria-label={`${t('property.option.up')} (${index + 1})`} disabled={index === 0}
                          onClick={() => options.move(index, index - 1)}>▲</button>
                        <button type="button" className="icon-link" aria-label={`${t('property.option.down')} (${index + 1})`}
                          disabled={index === options.fields.length - 1} onClick={() => options.move(index, index + 1)}>▼</button>
                      </td>
                      <td>
                        <input className="cell-input mono" aria-label={`${t('property.option.code')} ${index + 1}`} {...form.register(`options.${index}.code`)}
                          readOnly={form.getValues(`options.${index}.id`) !== null}
                          aria-invalid={optionErrors?.code !== undefined} />
                      </td>
                      <td>
                        <input className="cell-input" aria-label={`${t('property.option.label')} ${index + 1}`} {...form.register(`options.${index}.label`)}
                          aria-invalid={optionErrors?.label !== undefined} />
                      </td>
                      <td>
                        <input type="checkbox" aria-label={`${t('property.option.active')} ${index + 1}`} {...form.register(`options.${index}.isActive`)} />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            {Array.isArray(form.formState.errors.options) && form.formState.errors.options.some((o) => o !== undefined) && (
              <span className="field-error" role="alert">{t('property.option.required')}</span>
            )}
          </section>
        )}
        {definition !== undefined && (
          <p className="banner dashed small">{used ? t('property.usage', { count: definition.usageCount.toLocaleString('ro-RO') }) : t('property.unused')}</p>
        )}
      </div>
      <div className="form-footer">
        <span className="muted" />
        <div className="actions">
          {isList && (
            <button type="button" className="btn ghost" onClick={() => options.append({ id: null, code: '', label: '', isActive: true })}>
              {t('property.option.add')}
            </button>
          )}
          <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>
            {t('common.save')}
          </button>
        </div>
      </div>
    </form>
  );
}
