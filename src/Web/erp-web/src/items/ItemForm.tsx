import { useEffect, useMemo, useState } from 'react';
import { useFieldArray, useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { ApiError } from '../api/problem';
import { ErrorBanner, MultiSelectField, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, optionalText, required } from '../components/forms';
import { formatDate } from '../format/date';
import { t, type TextKey } from '../i18n';
import { DataType, type PropertyDefinition } from '../partners/partnerApi';
import { fromFormProps, propError, propKey, toFormProps, type PropInput } from '../partners/partnerModel';
import { fromRo, groupPath, ITEM_TYPES, itemApi, itemKeys, ItemType, percentText, toRo, usePackagingItems, type ItemDetail, type ItemLookups, type Saved } from './itemApi';

const measure = z.string().trim().nullable().refine((v) => v === null || v === '' || /^\d{1,13}([.,]\d{1,6})?$/.test(v), t('validation.number'));
const propValue = z.union([z.string(), z.boolean(), z.array(z.string()), z.null()]);

function itemSchema(definitions: readonly PropertyDefinition[], isNew: boolean) {
  return z
    .object({
      code: optionalText,
      name: required.max(250),
      itemType: z.number().int().min(1).max(8),
      itemGroupId: z.number().nullable(),
      isStockable: z.boolean(),
      defaultVatRateId: z.number().int().min(1, t('common.required')),
      cnCode: optionalText,
      depositItemId: z.number().nullable(),
      uomSetId: z.number().int().min(1, t('common.required')),
      pricePrecision: z.number().nullable(),
      quantityPrecision: z.number().nullable(),
      netWeightKg: measure,
      grossWeightKg: measure,
      volumeM3: measure,
      isActive: z.boolean(),
      uoms: z.array(z.object({
        uomId: z.number().int().min(1, t('common.required')),
        factorToBase: z.string().trim().regex(/^\d{1,13}([.,]\d{1,6})?$/, t('validation.number')),
        isDefaultSales: z.boolean(),
        isDefaultPurchase: z.boolean(),
        isDefaultPos: z.boolean(),
      })),
      props: z.record(z.string(), propValue),
    })
    .superRefine((v, ctx) => {
      if (v.code === null ? !isNew : !/^[A-Za-z0-9][A-Za-z0-9._/-]{0,49}$/.test(v.code)) {
        ctx.addIssue({ code: 'custom', path: ['code'], message: v.code === null ? t('common.required') : t('validation.partnerCode') });
      }
      if (v.cnCode !== null && !/^\d{4,10}$/.test(v.cnCode.replace(/ /g, ''))) {
        ctx.addIssue({ code: 'custom', path: ['cnCode'], message: 'Codul NC are 4–10 cifre (de exemplu 2201 10 11).' });
      }
      for (const definition of definitions) {
        const message = propError(definition, v.props[propKey(definition.id)], true);
        if (message !== undefined) {
          ctx.addIssue({ code: 'custom', path: ['props', propKey(definition.id)], message });
        }
      }
    });
}
type Schema = ReturnType<typeof itemSchema>;
type ItemInput = z.input<Schema>;
type ItemValues = z.output<Schema>;

const fields = ['code', 'name', 'itemType', 'itemGroupId', 'isStockable', 'defaultVatRateId', 'cnCode', 'depositItemId', 'uomSetId', 'pricePrecision',
  'quantityPrecision', 'netWeightKg', 'grossWeightKg', 'volumeM3', 'isActive'];

function defaultsFor(item: ItemDetail | undefined, lookups: ItemLookups): ItemInput {
  const definitions = lookups.properties;
  if (item === undefined) {
    const vat = lookups.vatRates.find((v) => v.code === 'S21' && v.validTo == null) ?? lookups.vatRates.find((v) => v.validTo == null);
    const set = lookups.uomSets.find((s) => s.code === 'BUC') ?? lookups.uomSets.find((s) => s.isActive);
    return {
      code: null, name: '', itemType: ItemType.Merchandise, itemGroupId: null, isStockable: true, defaultVatRateId: vat?.id ?? 0, cnCode: null,
      depositItemId: null, uomSetId: set?.id ?? 0, pricePrecision: null, quantityPrecision: null, netWeightKg: null, grossWeightKg: null, volumeM3: null,
      isActive: true, uoms: [], props: toFormProps(definitions, []),
    };
  }
  return {
    code: item.code, name: item.name, itemType: item.itemType, itemGroupId: item.itemGroupId ?? null, isStockable: item.isStockable,
    defaultVatRateId: item.defaultVatRateId, cnCode: item.cnCode ?? null, depositItemId: item.depositItemId ?? null, uomSetId: item.uomSetId,
    pricePrecision: item.pricePrecision ?? null, quantityPrecision: item.quantityPrecision ?? null, netWeightKg: toRo(item.netWeightKg),
    grossWeightKg: toRo(item.grossWeightKg), volumeM3: toRo(item.volumeM3), isActive: item.isActive,
    uoms: item.uoms.map((u) => ({ uomId: u.uomId, factorToBase: toRo(u.factorToBase) ?? '', isDefaultSales: u.isDefaultSales,
      isDefaultPurchase: u.isDefaultPurchase, isDefaultPos: u.isDefaultPos })),
    props: toFormProps(definitions, item.properties),
  };
}

/** Erorile serverului pe proprietati vin ca „properties.12”; in formular campul e „props.p12”. */
function remapPropertyErrors(error: unknown): unknown {
  if (!(error instanceof ApiError)) {
    return error;
  }
  const remapped: Record<string, readonly string[]> = {};
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const match = /^properties\.(\d+)$/.exec(field);
    remapped[match === null ? field : `props.${propKey(Number(match[1]))}`] = messages;
  }
  return new ApiError(error.status, { title: error.message, code: error.code, errors: remapped });
}

export type ItemSection = 'general' | 'uom' | 'properties';

/**
 * Fisa articolului, tab-urile Date generale, UM si conversii si Proprietati (macheta 05/05b), intr-un singur formular cu o
 * singura salvare. `section` alege partea vizibila; undefined = ramane montat, ascuns, pe alte tab-uri (UI-18).
 */
export function ItemForm({ item, lookups, section, canManage, onSaved, onCancel }: {
  readonly item: ItemDetail | undefined;
  readonly lookups: ItemLookups;
  readonly section: ItemSection | undefined;
  readonly canManage: boolean;
  readonly onSaved: (saved: Saved) => void;
  readonly onCancel: () => void;
}) {
  const definitions = lookups.properties;
  const isNew = item === undefined;
  const schema = useMemo(() => itemSchema(definitions, isNew), [definitions, isNew]);
  const defaults = useMemo(() => defaultsFor(item, lookups), [item, lookups]);
  const form = useForm<ItemInput, unknown, ItemValues>({ resolver: zodResolver(schema) as Resolver<ItemInput, unknown, ItemValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [item?.id, item?.rowVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const guard = useUnsavedChanges(form.formState.isDirty);
  const [itemType, uomSetId, uoms] = useWatch({ control: form.control, name: ['itemType', 'uomSetId', 'uoms'] });
  const exceptions = useFieldArray({ control: form.control, name: 'uoms' });
  const packaging = usePackagingItems();

  // Un serviciu nu tine stoc (CK_Item_Service): comutatorul urmeaza tipul.
  useEffect(() => {
    if (itemType === ItemType.Service && form.getValues('isStockable')) {
      form.setValue('isStockable', false, { shouldDirty: true });
    }
  }, [form, itemType]);

  const set = lookups.uomSets.find((s) => s.id === uomSetId);
  const units = useMemo(() => new Map(lookups.units.map((u) => [u.id, u])), [lookups.units]);
  const baseLine = set?.lines.find((l) => l.isBase);
  const baseUom = baseLine === undefined ? undefined : units.get(baseLine.uomId);

  const typeOptions = useMemo<Option[]>(() => ITEM_TYPES.map((type) => ({ value: String(type), text: t(`item.type.${type}` as TextKey) })), []);
  const groupOptions = useMemo<Option[]>(
    () => lookups.groups.filter((g) => g.isActive || g.id === item?.itemGroupId).map((g) => ({ value: String(g.id), text: groupPath(lookups.groups, g.id) }))
      .sort((a, b) => a.text.localeCompare(b.text, 'ro')),
    [lookups.groups, item?.itemGroupId],
  );
  const vatOptions = useMemo<Option[]>(
    () => lookups.vatRates.filter((v) => v.validTo == null || v.id === item?.defaultVatRateId)
      .map((v) => ({ value: String(v.id), text: `${percentText(v.percent)} · ${v.category} · ${v.name}` })),
    [lookups.vatRates, item?.defaultVatRateId],
  );
  const setOptions = useMemo<Option[]>(
    () => lookups.uomSets.filter((s) => s.isActive || s.id === item?.uomSetId).map((s) => ({ value: String(s.id), text: `${s.name} (${s.code})` })),
    [lookups.uomSets, item?.uomSetId],
  );
  const depositOptions = useMemo<Option[]>(
    () => (packaging.data?.items ?? []).filter((p) => p.id !== item?.id).map((p) => ({ value: String(p.id), text: `${p.name} (${p.code})` })),
    [packaging.data, item?.id],
  );
  const precisionOptions = useMemo<Option[]>(() => [0, 1, 2, 3, 4, 5, 6].map((n) => ({ value: String(n), text: String(n) })), []);
  // Lista nativa (<select>), nu componenta Syncfusion: nu are nevoie de referinta stabila.
  const unitOptions: Option[] = lookups.units.filter((u) => u.isActive && u.id !== baseLine?.uomId)
    .map((u) => ({ value: String(u.id), text: `${u.code} · ${u.name}` }));

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    const { props, uoms: exceptionValues, ...rest } = values;
    try {
      const saved = await itemApi.save({
        ...rest,
        id: item?.id ?? null,
        rowVersion: item?.rowVersion ?? null,
        netWeightKg: fromRo(rest.netWeightKg),
        grossWeightKg: fromRo(rest.grossWeightKg),
        volumeM3: fromRo(rest.volumeM3),
        cnCode: rest.cnCode === null ? null : rest.cnCode.replace(/ /g, ''),
        uoms: exceptionValues.map((u) => ({ ...u, factorToBase: fromRo(u.factorToBase) ?? '0', isBase: false })),
        properties: fromFormProps(definitions, props as Record<string, PropInput>),
      });
      form.reset(form.getValues());
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: itemKeys.all });
      notify(t('common.saved'));
      onSaved(saved);
    } catch (e) {
      const propFields = definitions.map((d) => `props.${propKey(d.id)}`);
      const message = applyServerErrors(remapPropertyErrors(e), form.setError, [...fields, ...propFields]);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  const readOnly = !canManage;
  const info = item === undefined ? undefined : t('common.lastChanged', { name: item.modifiedByName, date: formatDate(new Date(item.lastChangedAt)) });
  const uomName = baseUom?.code ?? 'UM';
  const example = conversionExample(set?.lines ?? [], uoms, units);

  return (
    <form className="node-form partner-form" noValidate hidden={section === undefined} onSubmit={(e) => void submit(e)} aria-label={item?.name ?? t('item.newTitle')}>
      {isNew && <h2 className="form-title">{t('item.newTitle')}</h2>}
      <ErrorBanner message={error} />
      <fieldset className="form-body plain" disabled={readOnly} hidden={section !== 'general'}>
        <div className="grid g3">
          <TextField control={form.control} name="code" label={isNew ? t('item.field.codeAuto') : t('item.field.code')} upper />
          <TextField control={form.control} name="name" label={t('item.field.name')} className="span2" />
          <SelectField control={form.control} name="itemType" label={t('item.field.type')} options={typeOptions} numeric />
          <SelectField control={form.control} name="itemGroupId" label={t('item.field.group')} options={groupOptions} allowEmpty={t('item.field.noGroup')} numeric />
          <SwitchField control={form.control} name="isStockable" label={t('item.field.stockable')} onText={t('item.field.stockableOn')}
            disabled={itemType === ItemType.Service} />
          <SelectField control={form.control} name="defaultVatRateId" label={t('item.field.vat')} options={vatOptions} numeric />
          <TextField control={form.control} name="cnCode" label={t('item.field.cnCode')} />
          <SelectField control={form.control} name="depositItemId" label={t('item.field.deposit')} options={depositOptions} allowEmpty={t('item.field.noDeposit')} numeric />
          <SelectField control={form.control} name="uomSetId" label={t('item.field.uomSet')} options={setOptions} numeric />
          <div className="field">
            <span className="label">{t('item.field.baseUom')}</span>
            <span className="readonly">{baseUom === undefined ? t('common.none') : `${baseUom.code} (${baseUom.uneceCode})`}</span>
          </div>
          <div className="grid g2 tight">
            <SelectField control={form.control} name="pricePrecision" label={t('item.field.pricePrecision')} options={precisionOptions}
              allowEmpty={t('item.field.precisionPolicy')} numeric />
            <SelectField control={form.control} name="quantityPrecision" label={t('item.field.quantityPrecision')} options={precisionOptions}
              allowEmpty={t('item.field.precisionPolicy')} numeric />
          </div>
          <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
        </div>
        <p className="banner dashed small">{t('item.accountingHint')}</p>
      </fieldset>
      <fieldset className="form-body plain" disabled={readOnly} hidden={section !== 'uom'}>
        <h3 className="section">{t('item.uom.set', { set: set?.name ?? '' })}</h3>
        <table className="simple uom-table">
          <thead>
            <tr>
              <th scope="col">{t('item.uom.col.uom')}</th>
              <th scope="col">{t('item.uom.col.unece')}</th>
              <th scope="col" className="num">{t('item.uom.col.factor', { base: uomName })}</th>
              <th scope="col">{t('item.uom.col.defaults')}</th>
              <th scope="col">{t('item.uom.col.source')}</th>
            </tr>
          </thead>
          <tbody>
            {effectiveLines(set?.lines ?? [], uoms).map((line) => {
              const unit = units.get(line.uomId);
              return (
                <tr key={line.uomId}>
                  <td><strong>{unit?.code}</strong> · {unit?.name}</td>
                  <td className="mono">{unit?.uneceCode}</td>
                  <td className="num">{toRo(line.factorToBase)}</td>
                  <td>
                    {line.isDefaultPos && <span className="tag customer">{t('item.uom.pos')}</span>}{' '}
                    {line.isDefaultSales && <span className="tag customer">{t('item.uom.sales')}</span>}{' '}
                    {line.isDefaultPurchase && <span className="tag customer">{t('item.uom.purchase')}</span>}
                  </td>
                  <td className="muted">{t(line.exception ? 'item.uom.exception' : 'item.uom.fromSet')}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
        {example !== undefined && <p className="banner dashed small">{t('item.uom.example', example)}</p>}
        <h3 className="section">{t('item.uom.exceptions')}</h3>
        <p className="muted small">{t('item.uom.exceptionsHint')}</p>
        {exceptions.fields.length > 0 && (
          <table className="simple options-table">
            <thead>
              <tr>
                <th scope="col">{t('item.uom.col.uom')}</th>
                <th scope="col">{t('item.uom.col.factor', { base: uomName })}</th>
                <th scope="col">{t('item.uom.sales')}</th>
                <th scope="col">{t('item.uom.purchase')}</th>
                <th scope="col">{t('item.uom.pos')}</th>
                <th scope="col"><span className="sr-only">{t('item.uom.remove')}</span></th>
              </tr>
            </thead>
            <tbody>
              {exceptions.fields.map((field, index) => (
                <tr key={field.id}>
                  <td>
                    <select className="cell-input" aria-label={`${t('item.uom.col.uom')} ${index + 1}`}
                      {...form.register(`uoms.${index}.uomId`, { valueAsNumber: true })}>
                      <option value={0}>{t('common.select')}</option>
                      {unitOptions.map((o) => <option key={o.value} value={o.value}>{o.text}</option>)}
                    </select>
                  </td>
                  <td>
                    <input className="cell-input num" aria-label={`${t('item.uom.col.factor', { base: uomName })} ${index + 1}`}
                      aria-invalid={form.formState.errors.uoms?.[index]?.factorToBase !== undefined} {...form.register(`uoms.${index}.factorToBase`)} />
                  </td>
                  {(['isDefaultSales', 'isDefaultPurchase', 'isDefaultPos'] as const).map((flag) => (
                    <td key={flag}>
                      <input type="checkbox" aria-label={`${t(flag === 'isDefaultSales' ? 'item.uom.sales' : flag === 'isDefaultPurchase' ? 'item.uom.purchase' : 'item.uom.pos')} ${index + 1}`}
                        {...form.register(`uoms.${index}.${flag}`)} />
                    </td>
                  ))}
                  <td>
                    <button type="button" className="icon-link" onClick={() => exceptions.remove(index)} aria-label={`${t('item.uom.remove')} ${index + 1}`}>✕</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {canManage && (
          <div>
            <button type="button" className="btn ghost small" onClick={() => exceptions.append({ uomId: 0, factorToBase: '', isDefaultSales: false,
              isDefaultPurchase: false, isDefaultPos: false })}>
              {t('item.uom.addException')}
            </button>
          </div>
        )}
        <h3 className="section">{t('item.uom.measures')}</h3>
        <div className="grid g3">
          <TextField control={form.control} name="netWeightKg" label={t('item.field.netWeight', { uom: uomName })} />
          <TextField control={form.control} name="grossWeightKg" label={t('item.field.grossWeight', { uom: uomName })} />
          <TextField control={form.control} name="volumeM3" label={t('item.field.volume', { uom: uomName })} />
        </div>
        <p className="muted small">{t('item.uom.rule')}</p>
      </fieldset>
      <fieldset className="form-body plain" disabled={readOnly} hidden={section !== 'properties'}>
        <ItemProperties control={form.control as never} definitions={definitions} lookups={lookups} />
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

interface FormUom {
  readonly uomId: number;
  readonly factorToBase: string;
  readonly isDefaultSales: boolean;
  readonly isDefaultPurchase: boolean;
  readonly isDefaultPos: boolean;
}

/** Liniile efective ale articolului: setul, cu exceptiile articolului in locul liniei din set (sau adaugate). */
export function effectiveLines(setLines: readonly { uomId: number; factorToBase: string; isBase: boolean; isDefaultSales: boolean; isDefaultPurchase: boolean; isDefaultPos: boolean }[],
  exceptions: readonly FormUom[] | undefined) {
  const own = (exceptions ?? []).filter((e) => e.uomId > 0);
  const lines = setLines.map((l) => {
    const exception = own.find((e) => e.uomId === l.uomId);
    return exception === undefined || l.isBase
      ? { ...l, exception: false }
      : { ...exception, factorToBase: fromRo(exception.factorToBase) ?? l.factorToBase, isBase: false, exception: true };
  });
  for (const e of own) {
    if (!setLines.some((l) => l.uomId === e.uomId)) {
      lines.push({ ...e, factorToBase: fromRo(e.factorToBase) ?? '0', isBase: false, exception: true });
    }
  }
  return lines.sort((a, b) => Number(a.factorToBase) - Number(b.factorToBase));
}

/** Exemplul din macheta 05b: cea mai mare UM × 3 + urmatoarea × 2 = total in UM de baza. */
function conversionExample(setLines: Parameters<typeof effectiveLines>[0], exceptions: readonly FormUom[] | undefined, units: Map<number, { code: string }>) {
  const lines = effectiveLines(setLines, exceptions).filter((l) => !l.isBase && Number(l.factorToBase) > 0);
  const base = setLines.find((l) => l.isBase);
  if (lines.length === 0 || base === undefined) {
    return undefined;
  }
  const [largest, second] = [...lines].reverse();
  const parts = [{ count: 3, line: largest }, ...(second === undefined ? [] : [{ count: 2, line: second }])];
  const total = parts.reduce((sum, p) => sum + p.count * Number(p.line?.factorToBase ?? 0), 0);
  return {
    expression: parts.map((p) => `${p.count} ${units.get(p.line?.uomId ?? 0)?.code ?? ''} (${p.count} × ${toRo(p.line?.factorToBase ?? '0')})`).join(' + '),
    total: `${total.toLocaleString('ro-RO')} ${units.get(base.uomId)?.code ?? ''}`,
  };
}

/** Proprietatile articolului (aceleasi tipuri si reguli ca la parteneri; toate se aplica oricarui articol). */
function ItemProperties({ control, definitions, lookups }: {
  readonly control: never;
  readonly definitions: readonly PropertyDefinition[];
  readonly lookups: ItemLookups;
}) {
  const sections = [...new Set(definitions.map((d) => d.section))];
  const users = useMemo<Option[]>(() => lookups.users.map((u) => ({ value: String(u.id), text: u.displayName })), [lookups.users]);
  if (definitions.length === 0) {
    return <p className="muted">{t('partner.properties.empty')}</p>;
  }
  return (
    <>
      {sections.map((section) => (
        <section key={section} aria-label={section}>
          <h3 className="section">{section}</h3>
          <div className="grid g3">
            {definitions.filter((d) => d.section === section).map((definition) => {
              const name = `props.${propKey(definition.id)}`;
              const label = definition.isRequired ? `${definition.name} *` : definition.name;
              const options = lookups.options.filter((o) => o.propertyDefinitionId === definition.id && o.isActive)
                .map((o) => ({ value: String(o.id), text: o.label }));
              switch (definition.dataType) {
                case DataType.Bool:
                  return <SwitchField key={definition.id} control={control} name={name as never} label={label} />;
                case DataType.List:
                  return <SelectField key={definition.id} control={control} name={name as never} label={label} options={options} allowEmpty={t('common.none')} />;
                case DataType.MultiList:
                  return <MultiSelectField key={definition.id} control={control} name={name as never} label={label} options={options} />;
                case DataType.User:
                  return <SelectField key={definition.id} control={control} name={name as never} label={label} options={users} allowEmpty={t('common.none')} />;
                case DataType.Date:
                  return <TextField key={definition.id} control={control} name={name as never} label={`${label} (zz.ll.aaaa)`} />;
                default:
                  return <TextField key={definition.id} control={control} name={name as never} label={label} />;
              }
            })}
          </div>
        </section>
      ))}
    </>
  );
}
