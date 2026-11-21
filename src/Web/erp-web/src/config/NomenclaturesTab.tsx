import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useFieldArray, useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import { ErrorBanner, NumberField, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, code as codeRule, required } from '../components/forms';
import { t } from '../i18n';
import { groupPath, itemKeys, toRo, useItemLookups, type ItemGroup, type ItemLookups, type UnitOfMeasure, type UomSet, type VatRate } from '../items/itemApi';
import { usePartnerLookups } from '../partners/partnerApi';
import { isoToRo, roToIso } from '../partners/partnerModel';

type List = 'vat' | 'units' | 'sets' | 'groups';

/**
 * Configurare › Nomenclatoare (macheta 07): cotele TVA si unitatile de masura editate direct in tabel, seturile de UM si grupele
 * de articole cu fisa lor. Tarile si judetele vin cu kitul; grupele de parteneri au tab-ul lor.
 */
export function NomenclaturesTab() {
  const lookups = useItemLookups();
  const partnerLookups = usePartnerLookups();
  const [params, setParams] = useSearchParams();
  const list = (params.get('lista') as List | null) ?? 'vat';
  const data = lookups.data;
  const counts: Record<List, number> = {
    vat: data?.vatRates.filter((v) => v.validTo == null).length ?? 0,
    units: data?.units.length ?? 0,
    sets: data?.uomSets.length ?? 0,
    groups: data?.groups.length ?? 0,
  };
  const entry = (key: List, label: string) => (
    <button type="button" className={list === key ? 'nom-entry on' : 'nom-entry'} aria-current={list === key ? 'page' : undefined}
      onClick={() => setParams({ lista: key })}>
      <span>{label}</span>
      <span className="muted small">{counts[key]}</span>
    </button>
  );

  return (
    <div className="nom-layout">
      <nav className="card nom-nav" aria-label={t('config.tab.nomenclatures')}>
        <div className="family-name">{t('nom.section.fiscal')}</div>
        {entry('vat', t('nom.vatRates'))}
        {entry('units', t('nom.units'))}
        <div className="family-name">{t('nom.section.classifications')}</div>
        {entry('sets', t('nom.uomSets'))}
        {entry('groups', t('nom.itemGroups'))}
        <Link className="nom-entry" to="/admin/config/grupe-parteneri">
          <span>{t('nom.partnerGroups')}</span>
          <span className="muted small">{partnerLookups.data?.groups.length ?? ''}</span>
        </Link>
        <p className="muted small nom-foot">{t('nom.footer')}</p>
      </nav>
      <section className="card nom-body">
        {data === undefined ? (
          <p className="muted" role="status">{lookups.isError ? t('app.loadError') : t('app.loading')}</p>
        ) : list === 'vat' ? (
          <VatRatesEditor key={JSON.stringify(data.vatRates.map((v) => v.rowVersion))} rates={data.vatRates} />
        ) : list === 'units' ? (
          <UnitsEditor key={JSON.stringify(data.units.map((u) => u.rowVersion))} units={data.units} />
        ) : list === 'sets' ? (
          <UomSetsEditor lookups={data} />
        ) : (
          <ItemGroupsEditor groups={data.groups} />
        )}
      </section>
    </div>
  );
}

/** Erorile pe randuri ale salvarii in tabel („rows[2].percent”) ca mesaje „Randul 3: …”. */
function rowErrors(error: unknown): string | undefined {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.entries(error.fieldErrors).map(([field, list]) => {
    const match = /^rows\[(\d+)\]/.exec(field);
    return match === null ? list.join(' ') : t('nom.invalidRow', { row: Number(match[1]) + 1, message: list.join(' ') });
  });
  return messages.length === 0 ? error.message : messages.join(' · ');
}

const today = () => new Date().toISOString().slice(0, 10);

interface VatRow {
  readonly key: string;
  readonly id: number | null;
  readonly code: string;
  readonly name: string;
  readonly percent: string;
  readonly category: string;
  readonly validFrom: string;
  readonly validTo: string;
  readonly usageCount: number;
  readonly rowVersion: string | null;
}

// Datele in tabel sunt zz.ll.aaaa (UI-04), nu campuri native de data, care urmeaza limba browserului.
const toVatRow = (v: VatRate): VatRow => ({
  key: `v${v.id}`, id: v.id, code: v.code, name: v.name, percent: toRo(v.percent) ?? '', category: v.category, validFrom: isoToRo(v.validFrom),
  validTo: v.validTo == null ? '' : isoToRo(v.validTo), usageCount: v.usageCount, rowVersion: v.rowVersion,
});

const categories = ['S', 'Z', 'E', 'AE', 'K', 'G', 'O'];

/** Cotele TVA editate in tabel (P6): o cota folosita nu se modifica, doar primeste „valabil pana la”. */
function VatRatesEditor({ rates }: { readonly rates: readonly VatRate[] }) {
  const original = useMemo(() => rates.map(toVatRow), [rates]);
  const [rows, setRows] = useState<VatRow[]>(original);
  const [filter, setFilter] = useState<'current' | 'historic' | 'all'>('all');
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const changed = rows.filter((r) => r.id === null || JSON.stringify(r) !== JSON.stringify(original.find((o) => o.key === r.key)));
  const guard = useUnsavedChanges(changed.length > 0);
  const now = today();
  const iso = (value: string) => roToIso(value) ?? value;
  const state = (r: VatRow) => (r.validTo !== '' && iso(r.validTo) < now ? 'closed' : iso(r.validFrom) > now ? 'future' : 'active');
  const visible = rows.filter((r) => (filter === 'all' || r.id === null || (filter === 'current' ? state(r) !== 'closed' : state(r) === 'closed'))
    && (search.trim() === '' || `${r.code} ${r.name}`.toLocaleLowerCase('ro-RO').includes(search.trim().toLocaleLowerCase('ro-RO'))));
  const update = (key: string, patch: Partial<VatRow>) => setRows((current) => current.map((r) => (r.key === key ? { ...r, ...patch } : r)));

  async function save() {
    const badDate = changed.find((r) => roToIso(r.validFrom) === undefined || (r.validTo !== '' && roToIso(r.validTo) === undefined));
    if (badDate !== undefined) {
      const message = t('nom.invalidRow', { row: rows.indexOf(badDate) + 1, message: t('validation.date') });
      setError(message);
      notify(message, 'error');
      return;
    }
    setBusy(true);
    setError(undefined);
    try {
      unwrap(await api.PUT('/api/core/vat-rates', {
        body: {
          rows: changed.map((r) => ({
            id: r.id, code: r.code.trim(), name: r.name.trim(), percent: r.percent.trim().replace(',', '.'), category: r.category, validFrom: roToIso(r.validFrom) ?? '',
            validTo: r.validTo === '' ? null : (roToIso(r.validTo) ?? null), rowVersion: r.rowVersion,
          })),
        },
      }));
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: itemKeys.lookups });
      notify(t('common.saved'));
    } catch (e) {
      const message = rowErrors(e);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="nom-editor">
      <div className="card-head">
        <h2>{t('nom.vatRates')} <span className="muted small">{t('nom.inlineHint')}</span></h2>
        <div className="chips-row">
          <input className="search" type="search" placeholder={t('nom.search')} aria-label={t('nom.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
          <button type="button" className={filter === 'current' ? 'chip on' : 'chip'} aria-pressed={filter === 'current'}
            onClick={() => setFilter(filter === 'current' ? 'all' : 'current')}>{t('nom.current')}</button>
          <button type="button" className={filter === 'historic' ? 'chip on' : 'chip'} aria-pressed={filter === 'historic'}
            onClick={() => setFilter(filter === 'historic' ? 'all' : 'historic')}>{t('nom.historic')}</button>
        </div>
      </div>
      <p className="banner warn small">{t('nom.vat.hint')}</p>
      <ErrorBanner message={error} />
      <table className="simple inline-table">
        <thead>
          <tr>
            <th scope="col">{t('nom.vat.code')}</th>
            <th scope="col">{t('nom.vat.name')}</th>
            <th scope="col" className="num">{t('nom.vat.percent')}</th>
            <th scope="col">{t('nom.vat.category')}</th>
            <th scope="col">{t('nom.vat.validFrom')}</th>
            <th scope="col">{t('nom.vat.validTo')}</th>
            <th scope="col">{t('nom.vat.state')}</th>
          </tr>
        </thead>
        <tbody>
          {visible.map((r) => {
            const locked = r.usageCount > 0;
            const isNew = r.id === null;
            const dirty = !isNew && changed.includes(r);
            return (
              <tr key={r.key} className={isNew || dirty ? 'pending' : undefined}>
                <td><input className="cell-input mono" aria-label={`${t('nom.vat.code')} ${r.code}`} value={r.code} disabled={locked}
                  onChange={(e) => update(r.key, { code: e.target.value.toUpperCase() })} /></td>
                <td><input className="cell-input" aria-label={`${t('nom.vat.name')} ${r.code}`} value={r.name}
                  onChange={(e) => update(r.key, { name: e.target.value })} /></td>
                <td><input className="cell-input num" aria-label={`${t('nom.vat.percent')} ${r.code}`} value={r.percent} disabled={locked}
                  onChange={(e) => update(r.key, { percent: e.target.value })} /></td>
                <td>
                  <select className="cell-input" aria-label={`${t('nom.vat.category')} ${r.code}`} value={r.category} disabled={locked}
                    onChange={(e) => update(r.key, { category: e.target.value })}>
                    <option value="">{t('common.select')}</option>
                    {categories.map((c) => <option key={c} value={c}>{c}</option>)}
                  </select>
                </td>
                <td><input className="cell-input date" placeholder="zz.ll.aaaa" aria-label={`${t('nom.vat.validFrom')} ${r.code}`} value={r.validFrom} disabled={locked}
                  onChange={(e) => update(r.key, { validFrom: e.target.value })} /></td>
                <td><input className="cell-input date" placeholder="zz.ll.aaaa" aria-label={`${t('nom.vat.validTo')} ${r.code}`} value={r.validTo}
                  onChange={(e) => update(r.key, { validTo: e.target.value })} /></td>
                <td className="nowrap">
                  {isNew ? <span className="pill warn">{t('nom.newRow')}</span>
                    : dirty ? <span className="pill warn">{t('nom.changedRow')}</span>
                      : <span className={state(r) === 'closed' ? 'pill off' : 'pill ok'}>{t(state(r) === 'closed' ? 'nom.vat.closed' : state(r) === 'future' ? 'nom.vat.future' : 'nom.vat.active')}</span>}
                  {locked && <span className="muted small"> 🔒 {t('nom.used')}</span>}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <div className="form-footer">
        <span className="muted">{t('nom.rowsInfo', { count: rows.length, changed: changed.length })}</span>
        <div className="actions">
          <button type="button" className="btn ghost" onClick={() => setRows((current) => [...current, {
            key: `n${Date.now()}`, id: null, code: '', name: '', percent: '', category: '', validFrom: isoToRo(now), validTo: '', usageCount: 0, rowVersion: null,
          }])}>{t('nom.addRow')}</button>
          <button type="button" className="btn ghost" disabled={changed.length === 0} onClick={() => setRows(original)}>{t('nom.discard')}</button>
          <button type="button" className="btn primary" disabled={changed.length === 0 || busy} onClick={() => void save()}>
            {t('nom.saveChanges', { count: changed.length })}
          </button>
        </div>
      </div>
    </div>
  );
}

interface UnitRow {
  readonly key: string;
  readonly id: number | null;
  readonly code: string;
  readonly name: string;
  readonly uneceCode: string;
  readonly isActive: boolean;
  readonly usageCount: number;
  readonly rowVersion: string | null;
}

/** Unitatile de masura editate in tabel; codul UN/ECE e obligatoriu (e-Factura), iar o UM folosita nu-si schimba codurile. */
function UnitsEditor({ units }: { readonly units: readonly UnitOfMeasure[] }) {
  const original = useMemo<UnitRow[]>(() => units.map((u) => ({ key: `u${u.id}`, id: u.id, code: u.code, name: u.name, uneceCode: u.uneceCode,
    isActive: u.isActive, usageCount: u.usageCount, rowVersion: u.rowVersion })), [units]);
  const [rows, setRows] = useState<UnitRow[]>(original);
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const changed = rows.filter((r) => r.id === null || JSON.stringify(r) !== JSON.stringify(original.find((o) => o.key === r.key)));
  const guard = useUnsavedChanges(changed.length > 0);
  const update = (key: string, patch: Partial<UnitRow>) => setRows((current) => current.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  const visible = rows.filter((r) => r.id === null || search.trim() === ''
    || `${r.code} ${r.name} ${r.uneceCode}`.toLocaleLowerCase('ro-RO').includes(search.trim().toLocaleLowerCase('ro-RO')));

  async function save() {
    setBusy(true);
    setError(undefined);
    try {
      unwrap(await api.PUT('/api/core/units-of-measure', {
        body: { rows: changed.map((r) => ({ id: r.id, code: r.code.trim(), name: r.name.trim(), uneceCode: r.uneceCode.trim(), isActive: r.isActive, rowVersion: r.rowVersion })) },
      }));
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: itemKeys.lookups });
      notify(t('common.saved'));
    } catch (e) {
      const message = rowErrors(e);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="nom-editor">
      <div className="card-head">
        <h2>{t('nom.units')} <span className="muted small">{t('nom.inlineHint')}</span></h2>
        <input className="search" type="search" placeholder={t('nom.search')} aria-label={t('nom.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
      </div>
      <p className="banner info small">{t('nom.unit.hint')}</p>
      <ErrorBanner message={error} />
      <table className="simple inline-table">
        <thead>
          <tr>
            <th scope="col">{t('nom.unit.code')}</th>
            <th scope="col">{t('nom.unit.name')}</th>
            <th scope="col">{t('nom.unit.unece')}</th>
            <th scope="col">{t('common.active')}</th>
            <th scope="col"><span className="sr-only">{t('nom.vat.state')}</span></th>
          </tr>
        </thead>
        <tbody>
          {visible.map((r) => {
            const locked = r.usageCount > 0;
            return (
              <tr key={r.key} className={changed.includes(r) ? 'pending' : undefined}>
                <td><input className="cell-input mono" aria-label={`${t('nom.unit.code')} ${r.code}`} value={r.code} disabled={locked}
                  onChange={(e) => update(r.key, { code: e.target.value })} /></td>
                <td><input className="cell-input" aria-label={`${t('nom.unit.name')} ${r.code}`} value={r.name} onChange={(e) => update(r.key, { name: e.target.value })} /></td>
                <td><input className="cell-input mono" aria-label={`${t('nom.unit.unece')} ${r.code}`} value={r.uneceCode} disabled={locked} maxLength={3}
                  onChange={(e) => update(r.key, { uneceCode: e.target.value.toUpperCase() })} /></td>
                <td><input type="checkbox" aria-label={`${t('common.active')} ${r.code}`} checked={r.isActive} onChange={(e) => update(r.key, { isActive: e.target.checked })} /></td>
                <td className="nowrap">{locked && <span className="muted small">🔒 {t('nom.usedM')}</span>}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <div className="form-footer">
        <span className="muted">{t('nom.rowsInfo', { count: rows.length, changed: changed.length })}</span>
        <div className="actions">
          <button type="button" className="btn ghost" onClick={() => setRows((current) => [...current, {
            key: `n${Date.now()}`, id: null, code: '', name: '', uneceCode: '', isActive: true, usageCount: 0, rowVersion: null,
          }])}>{t('nom.addRow')}</button>
          <button type="button" className="btn ghost" disabled={changed.length === 0} onClick={() => setRows(original)}>{t('nom.discard')}</button>
          <button type="button" className="btn primary" disabled={changed.length === 0 || busy} onClick={() => void save()}>
            {t('nom.saveChanges', { count: changed.length })}
          </button>
        </div>
      </div>
    </div>
  );
}

const setSchema = z.object({
  code: required.regex(/^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$/, t('validation.code')),
  name: required.max(100),
  isActive: z.boolean(),
  lines: z.array(z.object({
    uomId: z.number().int().min(1, t('common.required')),
    factorToBase: z.string().trim().regex(/^\d{1,13}([.,]\d{1,6})?$/, t('validation.number')),
    isBase: z.boolean(),
    isDefaultSales: z.boolean(),
    isDefaultPurchase: z.boolean(),
    isDefaultPos: z.boolean(),
  })).min(1),
}).superRefine((v, ctx) => {
  if (v.lines.filter((l) => l.isBase).length !== 1) {
    ctx.addIssue({ code: 'custom', path: ['lines'], message: 'Setul are exact o UM de bază, cu factorul 1.' });
  }
  v.lines.forEach((l, i) => {
    const factor = Number(l.factorToBase.trim().replace(',', '.'));
    if (l.isBase && factor !== 1) {
      ctx.addIssue({ code: 'custom', path: ['lines', i, 'factorToBase'], message: t('nom.invalidRow', { row: i + 1, message: t('nom.set.baseFactor') }) });
    } else if (!l.isBase && /^\d{1,13}([.,]\d{1,6})?$/.test(l.factorToBase.trim()) && !(factor > 0)) {
      ctx.addIssue({ code: 'custom', path: ['lines', i, 'factorToBase'], message: t('nom.invalidRow', { row: i + 1, message: t('nom.set.positiveFactor') }) });
    }
  });
});
type SetValues = z.infer<typeof setSchema>;

/** Seturile de UM: lista si fisa cu liniile (o singura UM de baza cu factor 1, cel mult o implicita pe circuit). */
function UomSetsEditor({ lookups }: { readonly lookups: ItemLookups }) {
  const [selected, setSelected] = useState<UomSet | 'new' | undefined>();
  const units = useMemo(() => new Map(lookups.units.map((u) => [u.id, u])), [lookups.units]);
  const current = selected === 'new' || selected === undefined ? selected : lookups.uomSets.find((s) => s.id === selected.id);
  return (
    <div className="nom-split">
      <div>
        <div className="card-head">
          <h2>{t('nom.uomSets')}</h2>
          <button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('nom.set.new')}</button>
        </div>
        <p className="muted small">{t('nom.set.hint')}</p>
        <table className="simple properties-table">
          <thead>
            <tr>
              <th scope="col">{t('field.code')}</th>
              <th scope="col">{t('field.name')}</th>
              <th scope="col">{t('nom.set.lines')}</th>
              <th scope="col" className="num">{t('nom.set.items')}</th>
            </tr>
          </thead>
          <tbody>
            {lookups.uomSets.map((s) => (
              <tr key={s.id} className={[current !== 'new' && current?.id === s.id ? 'current' : '', s.isActive ? '' : 'inactive'].join(' ').trim()} onClick={() => setSelected(s)}>
                <td className="mono"><button type="button" className="row-link" onClick={(e) => { e.stopPropagation(); setSelected(s); }}>{s.code}</button></td>
                <td>{s.name}</td>
                <td className="small">{s.lines.map((l) => `${units.get(l.uomId)?.code ?? ''}${l.isBase ? '' : ` ×${toRo(l.factorToBase) ?? ''}`}`).join(' · ')}</td>
                <td className="num">{s.usageCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div>
        {current === undefined ? <p className="muted empty-detail">{t('org.selectNode')}</p> : (
          <UomSetForm key={current === 'new' ? 'new' : `${current.id}-${current.rowVersion}`} set={current === 'new' ? undefined : current} units={lookups.units}
            onDone={() => setSelected(undefined)} />
        )}
      </div>
    </div>
  );
}

const flagLabel = { isBase: 'nom.set.base', isDefaultSales: 'item.uom.sales', isDefaultPurchase: 'item.uom.purchase', isDefaultPos: 'item.uom.pos' } as const;

function UomSetForm({ set, units, onDone }: { readonly set: UomSet | undefined; readonly units: readonly UnitOfMeasure[]; readonly onDone: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const defaults: SetValues = set === undefined
    ? { code: '', name: '', isActive: true, lines: [{ uomId: 0, factorToBase: '1', isBase: true, isDefaultSales: true, isDefaultPurchase: true, isDefaultPos: true }] }
    : { code: set.code, name: set.name, isActive: set.isActive, lines: set.lines.map((l) => ({ ...l, factorToBase: toRo(l.factorToBase) ?? '' })) };
  const form = useForm<SetValues>({ resolver: zodResolver(setSchema) as Resolver<SetValues>, defaultValues: defaults });
  const lines = useFieldArray({ control: form.control, name: 'lines' });
  const watchedLines = useWatch({ control: form.control, name: 'lines' });
  const guard = useUnsavedChanges(form.formState.isDirty);
  const unitOptions = units.filter((u) => u.isActive || set?.lines.some((l) => l.uomId === u.id)).map((u) => ({ value: u.id, text: `${u.code} · ${u.name} (${u.uneceCode})` }));

  // Bifele „baza” si cele implicite sunt exclusive pe set: alegerea uneia o scoate pe cea veche.
  const exclusive = (index: number, flag: 'isBase' | 'isDefaultSales' | 'isDefaultPurchase' | 'isDefaultPos', checked: boolean) => {
    if (checked) {
      form.getValues('lines').forEach((_, i) => {
        if (i !== index) {
          form.setValue(`lines.${i}.${flag}`, false, { shouldDirty: true });
        }
      });
      if (flag === 'isBase') {
        form.setValue(`lines.${index}.factorToBase`, '1', { shouldDirty: true });
      }
    }
    form.setValue(`lines.${index}.${flag}`, checked, { shouldDirty: true });
  };

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    const body = {
      ...values,
      id: set?.id ?? null,
      rowVersion: set?.rowVersion ?? null,
      lines: values.lines.map((l) => ({ ...l, factorToBase: l.factorToBase.replace(',', '.') })),
    };
    try {
      if (set === undefined) {
        unwrap(await api.POST('/api/core/uom-sets', { body }));
      } else {
        unwrap(await api.PUT('/api/core/uom-sets/{id}', { params: { path: { id: set.id } }, body }));
      }
      form.reset(form.getValues());
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: itemKeys.all });
      notify(t('common.saved'));
      onDone();
    } catch (e) {
      const message = applyServerErrors(e, form.setError, ['code', 'name', 'isActive']);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(e)}>
      <h2 className="form-title">{set === undefined ? t('nom.set.newTitle') : set.name}</h2>
      <ErrorBanner message={error ?? form.formState.errors.lines?.message
        ?? (Array.isArray(form.formState.errors.lines) ? form.formState.errors.lines.find((l) => l?.factorToBase?.message !== undefined)?.factorToBase?.message : undefined)} />
      <div className="form-body">
        <div className="grid g2">
          <TextField control={form.control} name="code" label={t('field.code')} upper />
          <TextField control={form.control} name="name" label={t('field.name')} />
        </div>
        <h3 className="section">{t('nom.set.lines')}</h3>
        <table className="simple options-table">
          <thead>
            <tr>
              <th scope="col">{t('item.uom.col.uom')}</th>
              <th scope="col">{t('nom.set.factor')}</th>
              <th scope="col">{t('nom.set.base')}</th>
              <th scope="col">{t('item.uom.sales')}</th>
              <th scope="col">{t('item.uom.purchase')}</th>
              <th scope="col">{t('item.uom.pos')}</th>
              <th scope="col"><span className="sr-only">{t('item.uom.remove')}</span></th>
            </tr>
          </thead>
          <tbody>
            {lines.fields.map((field, index) => (
              <tr key={field.id}>
                <td>
                  <select className="cell-input" aria-label={`${t('item.uom.col.uom')} ${index + 1}`} {...form.register(`lines.${index}.uomId`, { valueAsNumber: true })}>
                    <option value={0}>{t('common.select')}</option>
                    {unitOptions.map((o) => <option key={o.value} value={o.value}>{o.text}</option>)}
                  </select>
                </td>
                <td><input className="cell-input num" aria-label={`${t('nom.set.factor')} ${index + 1}`}
                  aria-invalid={form.formState.errors.lines?.[index]?.factorToBase !== undefined} {...form.register(`lines.${index}.factorToBase`)} /></td>
                {(['isBase', 'isDefaultSales', 'isDefaultPurchase', 'isDefaultPos'] as const).map((flag) => (
                  <td key={flag}>
                    <input type="checkbox" aria-label={`${t(flagLabel[flag])} ${index + 1}`} checked={watchedLines[index]?.[flag] === true}
                      onChange={(e) => exclusive(index, flag, e.target.checked)} />
                  </td>
                ))}
                <td><button type="button" className="icon-link" aria-label={`${t('item.uom.remove')} ${index + 1}`} onClick={() => lines.remove(index)}>✕</button></td>
              </tr>
            ))}
          </tbody>
        </table>
        <div>
          <button type="button" className="btn ghost small" onClick={() => lines.append({ uomId: 0, factorToBase: '', isBase: false, isDefaultSales: false,
            isDefaultPurchase: false, isDefaultPos: false })}>{t('nom.set.addLine')}</button>
        </div>
        <SwitchField control={form.control} name="isActive" label={t('field.isActive')} />
        {set !== undefined && set.usageCount > 0 && <p className="banner dashed small">{t('nom.set.usage', { count: set.usageCount })}</p>}
      </div>
      <div className="form-footer">
        <span className="muted" />
        <div className="actions">
          <button type="button" className="btn ghost" onClick={onDone}>{t('common.cancel')}</button>
          <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>{t('common.save')}</button>
        </div>
      </div>
    </form>
  );
}

const groupSchema = z.object({
  code: codeRule,
  name: required.max(100),
  parentId: z.number().nullable(),
  sortOrder: z.number().int().min(0).max(9999),
  isActive: z.boolean(),
});
type GroupValues = z.infer<typeof groupSchema>;

/** Arborele grupelor de articole, aplatizat cu indentare: parintele inaintea copiilor, in ordinea lor. */
export function flattenGroups(groups: readonly ItemGroup[]): { group: ItemGroup; depth: number }[] {
  const children = new Map<number | null, ItemGroup[]>();
  for (const g of groups) {
    const key = g.parentId ?? null;
    children.set(key, [...(children.get(key) ?? []), g]);
  }
  const result: { group: ItemGroup; depth: number }[] = [];
  const visit = (parent: number | null, depth: number) => {
    for (const g of (children.get(parent) ?? []).sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'ro'))) {
      result.push({ group: g, depth });
      if (depth < 20) {
        visit(g.id, depth + 1);
      }
    }
  };
  visit(null, 0);
  return result;
}

function ItemGroupsEditor({ groups }: { readonly groups: readonly ItemGroup[] }) {
  const [selected, setSelected] = useState<ItemGroup | 'new' | undefined>();
  const tree = useMemo(() => flattenGroups(groups), [groups]);
  return (
    <div className="nom-split">
      <div>
        <div className="card-head">
          <h2>{t('nom.itemGroups')}</h2>
          <button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('nom.group.new')}</button>
        </div>
        <p className="muted small">{t('nom.group.hint')}</p>
        <table className="simple properties-table">
          <thead>
            <tr>
              <th scope="col">{t('field.name')}</th>
              <th scope="col">{t('field.code')}</th>
              <th scope="col" className="num">{t('nom.group.items')}</th>
            </tr>
          </thead>
          <tbody>
            {tree.map(({ group, depth }) => (
              <tr key={group.id} className={[selected !== 'new' && selected?.id === group.id ? 'current' : '', group.isActive ? '' : 'inactive'].join(' ').trim()}
                onClick={() => setSelected(group)}>
                <td style={{ paddingLeft: `${8 + depth * 18}px` }}>
                  <button type="button" className="row-link" onClick={(e) => { e.stopPropagation(); setSelected(group); }}>{depth > 0 ? '› ' : ''}{group.name}</button>
                </td>
                <td className="mono">{group.code}</td>
                <td className="num">{group.usageCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div>
        {selected === undefined ? <p className="muted empty-detail">{t('org.selectNode')}</p> : (
          <ItemGroupForm key={selected === 'new' ? 'new' : `${selected.id}-${selected.rowVersion}`} group={selected === 'new' ? undefined : selected} groups={groups}
            onDone={() => setSelected(undefined)} />
        )}
      </div>
    </div>
  );
}

function ItemGroupForm({ group, groups, onDone }: { readonly group: ItemGroup | undefined; readonly groups: readonly ItemGroup[]; readonly onDone: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const defaults: GroupValues = group === undefined
    ? { code: '', name: '', parentId: null, sortOrder: 100, isActive: true }
    : { code: group.code, name: group.name, parentId: group.parentId ?? null, sortOrder: group.sortOrder, isActive: group.isActive };
  const form = useForm<GroupValues>({ resolver: zodResolver(groupSchema) as Resolver<GroupValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [group?.id]); // eslint-disable-line react-hooks/exhaustive-deps
  const guard = useUnsavedChanges(form.formState.isDirty);
  // Parintele nu poate fi grupa insasi sau un descendent al ei (serverul refuza si el).
  const parentOptions = useMemo<Option[]>(() => {
    const descendants = new Set<number>(group === undefined ? [] : [group.id]);
    let added = true;
    while (added) {
      added = false;
      for (const g of groups) {
        if (g.parentId != null && descendants.has(g.parentId) && !descendants.has(g.id)) {
          descendants.add(g.id);
          added = true;
        }
      }
    }
    return groups.filter((g) => g.isActive && !descendants.has(g.id)).map((g) => ({ value: String(g.id), text: groupPath(groups, g.id) }))
      .sort((a, b) => a.text.localeCompare(b.text, 'ro'));
  }, [groups, group]);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    const body = { ...values, id: group?.id ?? null, rowVersion: group?.rowVersion ?? null };
    try {
      if (group === undefined) {
        unwrap(await api.POST('/api/core/item-groups', { body }));
      } else {
        unwrap(await api.PUT('/api/core/item-groups/{id}', { params: { path: { id: group.id } }, body }));
      }
      form.reset(form.getValues());
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: itemKeys.lookups });
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
      <h2 className="form-title">{group === undefined ? t('nom.group.newTitle') : group.name}</h2>
      <ErrorBanner message={error} />
      <div className="form-body">
        <div className="grid g2">
          <TextField control={form.control} name="code" label={t('field.code')} upper />
          <TextField control={form.control} name="name" label={t('field.name')} />
          <SelectField control={form.control} name="parentId" label={t('nom.group.parent')} options={parentOptions} allowEmpty={t('nom.group.root')} numeric />
          <NumberField control={form.control} name="sortOrder" label={t('addressType.order')} min={0} max={9999} grouping={false} />
        </div>
        <SwitchField control={form.control} name="isActive" label={t('field.isActive')} onText={t('common.activeF')} />
      </div>
      <div className="form-footer">
        <span className="muted" />
        <div className="actions">
          <button type="button" className="btn ghost" onClick={onDone}>{t('common.cancel')}</button>
          <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>{t('common.save')}</button>
        </div>
      </div>
    </form>
  );
}
