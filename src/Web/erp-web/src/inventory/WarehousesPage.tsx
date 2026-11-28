import { useEffect, useMemo, useState } from 'react';
import { useOutletContext, useSearchParams } from 'react-router';
import { Controller, useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { TextBoxComponent } from '@syncfusion/ej2-react-inputs';
import { z } from 'zod';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, SelectField, TextField, type Option } from '../components/Field';
import { SearchSelectField } from '../components/SearchSelectField';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, required } from '../components/forms';
import { formatDate } from '../format/date';
import { t } from '../i18n';
import { HistoryView } from '../partners/PartnerTabs';
import { useDebounced } from '../partners/PartnersPage';
import { KeepersTab } from './KeepersTab';
import {
  allowsNegativeStock, creatableKinds, filterWarehouses, inventoryApi, inventoryKeys, kindLabel, NegativeStock, Purpose, usesLocation, usesThirdParty,
  useWarehouse, useWarehouseHistory, useWarehouses, ValuationBasis, WarehouseKind, type WarehouseDetail, type WarehouseFilter, type WarehouseLookups,
} from './inventoryApi';

const ManagePermission = 'Inv.Warehouse.Manage';
const BlockPermission = 'Inv.Warehouse.Block';

async function searchPartners(text: string): Promise<Option[]> {
  const page = unwrap(await api.GET('/api/core/partners', { params: { query: { ...(text === '' ? {} : { Search: text }), IsActive: true, PageSize: 30 } } }));
  return page.items.map((p) => ({ value: String(p.id), text: p.fiscalCode == null ? p.name : `${p.name} · ${p.fiscalCode}` }));
}

function useSelection() {
  const [params, setParams] = useSearchParams();
  const id = Number(params.get('id'));
  return {
    id: Number.isInteger(id) && id > 0 ? id : undefined,
    creating: params.get('nou') === '1',
    select: (warehouseId: number) => setParams({ id: String(warehouseId) }),
    startCreate: () => setParams({ nou: '1' }),
    clear: () => setParams({}),
  };
}

const filters: readonly { readonly value: WarehouseFilter; readonly label: Parameters<typeof t>[0] }[] = [
  { value: 'all', label: 'inv.filter.all' },
  { value: 'active', label: 'inv.filter.active' },
  { value: 'blocked', label: 'inv.filter.blocked' },
  { value: 'inactive', label: 'inv.filter.inactive' },
];

/**
 * Stocuri › Gestiuni (02.05, S1 3.1, 3.2 si 11; machetele s1-01, s1-02): lista gestiunilor firmei curente (doar cele permise utilizatorului)
 * si fisa gestiunii cu datele generale, gestionarii cu predarea-primirea si istoricul; blocarea si dezactivarea.
 */
export function WarehousesPage() {
  const user = useOutletContext<CurrentUser>();
  const selection = useSelection();
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<WarehouseFilter>('all');
  const debounced = useDebounced(search);
  const list = useWarehouses(debounced);
  const canManage = user.permissions.includes(ManagePermission);
  const data = list.data;
  const rows = useMemo(() => filterWarehouses(data?.warehouses ?? [], filter), [data?.warehouses, filter]);

  if (data === undefined) {
    return <div className="page" role="status">{list.isError ? t('app.loadError') : t('app.loading')}</div>;
  }

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.warehouses')}</div>
      <div className="head">
        <div>
          <h1>{t('inv.warehouses.title')}</h1>
          <div className="sub2">{t('inv.warehouses.subtitle')}</div>
        </div>
        <div className="actions">
          {canManage && (
            <button type="button" className="btn primary" onClick={selection.startCreate}>
              {t('inv.warehouses.new')}
            </button>
          )}
        </div>
      </div>

      <div className="split partners">
        <section className="card list-card" aria-label={t('inv.warehouses.title')}>
          <TextBoxComponent placeholder={t('inv.warehouses.search')} value={search} input={(args: { value: string }) => setSearch(args.value)}
            showClearButton htmlAttributes={{ 'aria-label': t('inv.warehouses.search') }} />
          <div className="chips-row" role="group" aria-label={t('inv.filter.label')}>
            {filters.map((f) => (
              <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                onClick={() => setFilter(f.value)}>
                {t(f.label)}
              </button>
            ))}
          </div>
          {rows.length === 0 && <p className="muted">{t('inv.warehouses.empty')}</p>}
          {rows.length > 0 && (
            <table className="partner-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('inv.col.warehouse')}</th>
                  <th scope="col">{t('inv.col.kind')}</th>
                  <th scope="col">{t('inv.col.status')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((item) => {
                  const current = item.id === selection.id;
                  return (
                    <tr key={item.id} className={[current ? 'current' : '', item.isActive ? '' : 'inactive'].join(' ').trim()}
                      onClick={() => selection.select(item.id)}>
                      <td>
                        <button type="button" className="row-link" aria-current={current ? 'true' : undefined} onClick={(e) => {
                          e.stopPropagation();
                          selection.select(item.id);
                        }}>
                          {item.code}
                        </button>
                        <div className="muted small">{[item.name, item.locationName ?? item.thirdPartyName].filter(Boolean).join(' · ')}</div>
                      </td>
                      <td className="small">
                        {t(kindLabel(item.kind))}
                        {item.valuationBasis === ValuationBasis.RetailPrice ? ` · ${t('inv.basis.retailShort')}` : ''}
                      </td>
                      <td>
                        {item.isBlocked && <span className="pill warn">{t('inv.blocked')}</span>}{' '}
                        <span className={item.isActive ? 'pill ok' : 'pill off'}>{item.isActive ? t('common.active') : t('common.inactive')}</span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
          <div className="muted small list-foot">{t('inv.warehouses.count', { count: data.warehouses.length })}</div>
        </section>

        <section className="card detail-card">
          {selection.creating ? (
            <WarehouseForm key="new" warehouse={undefined} lookups={data.lookups} canManage={canManage} onSaved={(id) => selection.select(id)}
              onCancel={selection.clear} />
          ) : selection.id === undefined ? (
            <p className="muted empty-detail">{t('inv.warehouses.select')}</p>
          ) : (
            <WarehousePanel key={selection.id} warehouseId={selection.id} lookups={data.lookups} user={user} />
          )}
        </section>
      </div>
    </div>
  );
}

type PanelTab = 'general' | 'keepers' | 'history';

function WarehousePanel({ warehouseId, lookups, user }: { readonly warehouseId: number; readonly lookups: WarehouseLookups; readonly user: CurrentUser }) {
  const warehouse = useWarehouse(warehouseId);
  const [tab, setTab] = useState<PanelTab>('general');
  const history = useWarehouseHistory(warehouseId, tab === 'history');
  if (warehouse.data === undefined) {
    return <p role="status">{warehouse.isError ? warehouse.error.message : t('app.loading')}</p>;
  }
  const data = warehouse.data;
  const canManage = user.permissions.includes(ManagePermission);
  const tabs: readonly [PanelTab, string][] = [['general', t('inv.tab.general')], ['keepers', t('inv.tab.keepers')], ['history', t('partner.tab.history')]];
  return (
    <div className="company-panel">
      <div className="panel-head">
        <div>
          <h2>
            {`${data.code} · ${data.name}`}{' '}
            {data.isBlocked && <span className="pill warn">{t('inv.blocked')}</span>}{' '}
            <span className={data.isActive ? 'pill ok' : 'pill off'}>{data.isActive ? t('common.active') : t('common.inactive')}</span>
          </h2>
          <div className="muted">
            {[t(kindLabel(data.kind)), data.locationName ?? data.thirdPartyName, data.hasMovements ? t('inv.hasMovements') : undefined]
              .filter(Boolean).join(' · ')}
          </div>
          {data.isBlocked && data.blockReason != null && <div className="banner warn">{t('inv.blockedReason', { reason: data.blockReason })}</div>}
        </div>
        <WarehouseActions data={data} canBlock={user.permissions.includes(BlockPermission)} canManage={canManage} />
      </div>
      <div className="tabs" role="tablist" aria-label={data.name}>
        {tabs.map(([key, label]) => (
          <button key={key} type="button" role="tab" id={`wh-tab-${key}`} aria-selected={tab === key} tabIndex={tab === key ? 0 : -1}
            className={tab === key ? 'tab on' : 'tab'} onClick={() => setTab(key)}>
            {label}
          </button>
        ))}
      </div>
      <div role="tabpanel" aria-labelledby="wh-tab-general" hidden={tab !== 'general'}>
        <WarehouseForm warehouse={data} lookups={lookups} canManage={canManage} onSaved={() => undefined} onCancel={() => undefined} />
      </div>
      {tab === 'keepers' && (
        <div role="tabpanel" aria-labelledby="wh-tab-keepers">
          <KeepersTab warehouseId={warehouseId} canManage={user.permissions.includes('Inv.Warehouse.Keeper')} />
        </div>
      )}
      {tab === 'history' && <div role="tabpanel" aria-labelledby="wh-tab-history"><HistoryView history={history} /></div>}
    </div>
  );
}

/** Blocarea cu motiv, deblocarea, dezactivarea si reactivarea (S1 4.3, 4.4); tranzitul nu se dezactiveaza. */
function WarehouseActions({ data, canBlock, canManage }: { readonly data: WarehouseDetail; readonly canBlock: boolean; readonly canManage: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [reason, setReason] = useState('');
  const [asking, setAsking] = useState(false);
  const run = async (action: () => Promise<unknown>) => {
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: inventoryKeys.all });
      notify(t('common.saved'));
      setAsking(false);
      setReason('');
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };
  return (
    <div className="actions">
      {canBlock && !data.isBlocked && !asking && (
        <button type="button" className="btn ghost" onClick={() => setAsking(true)}>{t('inv.block')}</button>
      )}
      {canBlock && asking && (
        <>
          <input className="cell-input" value={reason} aria-label={t('inv.blockReason')} placeholder={t('inv.blockReason')}
            onChange={(e) => setReason(e.target.value)} />
          <button type="button" className="btn primary" disabled={reason.trim() === ''}
            onClick={() => void run(() => inventoryApi.block(data.id, reason.trim(), data.rowVersion))}>
            {t('inv.block')}
          </button>
          <button type="button" className="btn ghost" onClick={() => setAsking(false)}>{t('common.cancel')}</button>
        </>
      )}
      {canBlock && data.isBlocked && (
        <button type="button" className="btn ghost" onClick={() => void run(() => inventoryApi.unblock(data.id, data.rowVersion))}>{t('inv.unblock')}</button>
      )}
      {canManage && data.kind !== WarehouseKind.Transit && (
        <button type="button" className="btn ghost" onClick={() => void run(() => inventoryApi.setActive(data.id, !data.isActive, data.rowVersion))}>
          {data.isActive ? t('inv.deactivate') : t('inv.reactivate')}
        </button>
      )}
    </div>
  );
}

const warehouseSchema = z
  .object({
    kind: z.number(),
    code: required.max(20).regex(/^[A-Za-z0-9][A-Za-z0-9._/-]{0,19}$/, t('inv.validation.code')),
    name: required.max(200),
    locationId: z.number().nullable(),
    valuationBasis: z.number(),
    retailPriceListId: z.number().nullable(),
    thirdPartyId: z.number().nullable(),
    negativeStockPolicy: z.number(),
    defaultCostCenterId: z.number().nullable(),
    deliveryOrderMode: z.number(),
    returnWarehouseId: z.number().nullable(),
    purpose: z.number(),
  })
  .superRefine((v, ctx) => {
    if (usesThirdParty(v.kind) && v.thirdPartyId === null) {
      ctx.addIssue({ code: 'custom', path: ['thirdPartyId'], message: t('inv.validation.thirdParty') });
    }
    if (v.valuationBasis === ValuationBasis.RetailPrice && v.retailPriceListId === null) {
      ctx.addIssue({ code: 'custom', path: ['retailPriceListId'], message: t('inv.validation.priceList') });
    }
  });
type WarehouseInput = z.input<typeof warehouseSchema>;
type WarehouseValues = z.output<typeof warehouseSchema>;

const fields = ['kind', 'code', 'name', 'locationId', 'valuationBasis', 'retailPriceListId', 'thirdPartyId', 'negativeStockPolicy',
  'defaultCostCenterId', 'deliveryOrderMode', 'returnWarehouseId', 'purpose'];

function defaultsFor(w: WarehouseDetail | undefined): WarehouseInput {
  return w === undefined
    ? { kind: WarehouseKind.Depot, code: '', name: '', locationId: null, valuationBasis: ValuationBasis.Cost, retailPriceListId: null, thirdPartyId: null,
        negativeStockPolicy: NegativeStock.Blocked, defaultCostCenterId: null, deliveryOrderMode: 1, returnWarehouseId: null, purpose: Purpose.Regular }
    : { kind: w.kind, code: w.code, name: w.name, locationId: w.locationId ?? null, valuationBasis: w.valuationBasis,
        retailPriceListId: w.retailPriceListId ?? null, thirdPartyId: w.thirdPartyId ?? null, negativeStockPolicy: w.negativeStockPolicy,
        defaultCostCenterId: w.defaultCostCenterId ?? null, deliveryOrderMode: w.deliveryOrderMode, returnWarehouseId: w.returnWarehouseId ?? null,
        purpose: w.purpose };
}

/** Corpul cererii de salvare: partenerul doar la custodie si la terti, locatia doar la tipurile cu locatie, lista de preturi doar la amanunt. */
export function toWarehouseBody(values: WarehouseValues, w: WarehouseDetail | undefined) {
  return {
    id: w?.id ?? null,
    rowVersion: w?.rowVersion ?? null,
    code: values.code,
    name: values.name,
    kind: values.kind,
    locationId: usesLocation(values.kind) ? values.locationId : null,
    valuationBasis: values.kind === WarehouseKind.Store ? values.valuationBasis : ValuationBasis.Cost,
    retailPriceListId: values.kind === WarehouseKind.Store && values.valuationBasis === ValuationBasis.RetailPrice ? values.retailPriceListId : null,
    thirdPartyId: usesThirdParty(values.kind) ? values.thirdPartyId : null,
    negativeStockPolicy: allowsNegativeStock(values.kind) ? values.negativeStockPolicy : NegativeStock.Blocked,
    defaultCostCenterId: values.defaultCostCenterId,
    deliveryOrderMode: values.deliveryOrderMode,
    returnWarehouseId: values.returnWarehouseId,
    purpose: values.kind === WarehouseKind.Depot ? values.purpose : Purpose.Regular,
  };
}

function WarehouseForm({ warehouse, lookups, canManage, onSaved, onCancel }: {
  readonly warehouse: WarehouseDetail | undefined;
  readonly lookups: WarehouseLookups;
  readonly canManage: boolean;
  readonly onSaved: (id: number) => void;
  readonly onCancel: () => void;
}) {
  const isNew = warehouse === undefined;
  const locked = warehouse?.hasMovements === true;
  const defaults = useMemo(() => defaultsFor(warehouse), [warehouse]);
  const form = useForm<WarehouseInput, unknown, WarehouseValues>({
    resolver: zodResolver(warehouseSchema) as Resolver<WarehouseInput, unknown, WarehouseValues>,
    defaultValues: defaults,
  });
  useEffect(() => form.reset(defaults), [warehouse?.id, warehouse?.rowVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const guard = useUnsavedChanges(form.formState.isDirty);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const kind = useWatch({ control: form.control, name: 'kind' });
  const basis = useWatch({ control: form.control, name: 'valuationBasis' });
  const transit = kind === WarehouseKind.Transit;

  const locations = useMemo<Option[]>(() => lookups.locations.map((l) => ({ value: String(l.id), text: `${l.code} · ${l.name}` })), [lookups.locations]);
  const costCenters = useMemo<Option[]>(() => lookups.costCenters.map((c) => ({ value: String(c.id), text: `${c.code} · ${c.name}` })), [lookups.costCenters]);
  const returns = useMemo<Option[]>(
    () => lookups.returnWarehouses.filter((r) => r.id !== warehouse?.id).map((r) => ({ value: String(r.id), text: `${r.code} · ${r.name}` })),
    [lookups.returnWarehouses, warehouse?.id],
  );
  const thirdPartyId = warehouse?.thirdPartyId;
  const thirdPartyName = warehouse?.thirdPartyName;
  const thirdParty = useMemo<Option | undefined>(
    () => (thirdPartyId == null ? undefined : { value: String(thirdPartyId), text: thirdPartyName ?? '' }),
    [thirdPartyId, thirdPartyName],
  );
  const deliveryModes = useMemo<Option[]>(() => [
    { value: '0', text: t('inv.delivery.direct') }, { value: '1', text: t('inv.delivery.both') }, { value: '2', text: t('inv.delivery.order') },
  ], []);
  const purposes = useMemo<Option[]>(() => [
    { value: String(Purpose.Regular), text: t('inv.purpose.regular') }, { value: String(Purpose.CustomerReturns), text: t('inv.purpose.returns') },
    { value: String(Purpose.DepositPackaging), text: t('inv.purpose.packaging') },
  ], []);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      const saved = await inventoryApi.saveWarehouse(toWarehouseBody(values, warehouse));
      form.reset(form.getValues());
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: inventoryKeys.all });
      notify(t('common.saved'));
      onSaved(saved.id);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, fields);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  const info = warehouse === undefined ? undefined
    : t('common.lastChanged', { name: warehouse.modifiedByName ?? '', date: formatDate(new Date(warehouse.modifiedAt ?? warehouse.createdAt)) });
  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(e)} aria-label={isNew ? t('inv.warehouses.newTitle') : warehouse.code}>
      {isNew && <h2 className="form-title">{t('inv.warehouses.newTitle')}</h2>}
      <ErrorBanner message={error} />
      <fieldset className="form-body plain" disabled={!canManage}>
        <Controller
          control={form.control}
          name="kind"
          render={({ field }) => (
            <div className="field">
              <span className="label" id="wh-kind-label">{t('inv.field.kind')} *</span>
              <div className="segmented four" role="radiogroup" aria-labelledby="wh-kind-label">
                {(transit ? [WarehouseKind.Transit] : creatableKinds).map((value) => (
                  <button key={value} type="button" role="radio" aria-checked={field.value === value} className={field.value === value ? 'on' : ''}
                    disabled={locked || transit} onClick={() => field.onChange(value)}>
                    {t(kindLabel(value))}
                  </button>
                ))}
              </div>
            </div>
          )}
        />
        <div className="grid g3">
          <TextField control={form.control} name="code" label={`${t('inv.field.code')} *`} upper disabled={locked} />
          <TextField control={form.control} name="name" label={`${t('inv.field.name')} *`} className="span2" />
          {usesLocation(kind) && (
            <SelectField control={form.control} name="locationId" label={t('inv.field.location')} options={locations}
              allowEmpty={t('inv.field.locationInDepots')} numeric className="span2" />
          )}
          {usesThirdParty(kind) && (
            <SearchSelectField control={form.control} name="thirdPartyId" label={`${t('inv.field.thirdParty')} *`} className="span2" current={thirdParty}
              search={searchPartners} />
          )}
          {kind === WarehouseKind.Store && (
            <Controller
              control={form.control}
              name="valuationBasis"
              render={({ field }) => (
                <div className="field">
                  <span className="label" id="wh-basis-label">{t('inv.field.basis')}</span>
                  <div className="segmented two" role="radiogroup" aria-labelledby="wh-basis-label">
                    {[ValuationBasis.Cost, ValuationBasis.RetailPrice].map((value) => (
                      <button key={value} type="button" role="radio" aria-checked={field.value === value} className={field.value === value ? 'on' : ''}
                        disabled={locked} onClick={() => field.onChange(value)}>
                        {t(value === ValuationBasis.Cost ? 'inv.basis.cost' : 'inv.basis.retail')}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            />
          )}
          {kind === WarehouseKind.Store && basis === ValuationBasis.RetailPrice && (
            <Controller control={form.control} name="retailPriceListId" render={({ field, fieldState }) => (
              <label className="field">
                <span className="label">{t('inv.field.priceList')} *</span>
                <input className="cell-input" inputMode="numeric" value={field.value ?? ''} disabled={locked} aria-invalid={fieldState.error !== undefined}
                  onChange={(e) => field.onChange(e.target.value === '' ? null : Number(e.target.value))} />
                {fieldState.error !== undefined && <span className="error">{fieldState.error.message}</span>}
              </label>
            )} />
          )}
          {allowsNegativeStock(kind) && (
            <Controller
              control={form.control}
              name="negativeStockPolicy"
              render={({ field }) => (
                <div className="field">
                  <span className="label" id="wh-neg-label">{t('inv.field.negative')}</span>
                  <div className="segmented two" role="radiogroup" aria-labelledby="wh-neg-label">
                    {[NegativeStock.Blocked, NegativeStock.AllowedWithWarning].map((value) => (
                      <button key={value} type="button" role="radio" aria-checked={field.value === value} className={field.value === value ? 'on' : ''}
                        onClick={() => field.onChange(value)}>
                        {t(value === NegativeStock.Blocked ? 'inv.negative.blocked' : 'inv.negative.allowed')}
                      </button>
                    ))}
                  </div>
                  <span className="muted small">{t('inv.hint.negative')}</span>
                </div>
              )}
            />
          )}
          <SelectField control={form.control} name="defaultCostCenterId" label={t('inv.field.costCenter')} options={costCenters}
            allowEmpty={t('common.none')} numeric />
          <SelectField control={form.control} name="deliveryOrderMode" label={t('inv.field.delivery')} options={deliveryModes} numeric />
          {kind === WarehouseKind.Depot && (
            <SelectField control={form.control} name="purpose" label={t('inv.field.purpose')} options={purposes} numeric />
          )}
          <SelectField control={form.control} name="returnWarehouseId" label={t('inv.field.returnWarehouse')} options={returns}
            allowEmpty={t('common.none')} numeric />
        </div>
        <div className="banner dashed">{t('inv.hint.locked')}</div>
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
