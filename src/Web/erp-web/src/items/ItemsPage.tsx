import { OwnerFilter } from '../org/OwnerFilter';
import { ownerLabel, useOwnerOptions } from '../org/owner';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useOutletContext, useSearchParams } from 'react-router';
import { useForm, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { TextBoxComponent } from '@syncfusion/ej2-react-inputs';
import { ColumnDirective, ColumnsDirective, GridComponent } from '@syncfusion/ej2-react-grids';
import { z } from 'zod';
import { downloadFile } from '../api/download';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, FieldIdPrefix, SelectField, SwitchField, TextField, type Option } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors, required } from '../components/forms';
import { tabKeyTarget } from '../components/tabs';
import { t, type TextKey } from '../i18n';
import { DataType } from '../partners/partnerApi';
import { HistoryView } from '../partners/PartnerTabs';
import { ItemForm, effectiveLines, type ItemSection } from './ItemForm';
import {
  groupPath, ITEM_TYPES, itemApi, itemKeys, itemSearchQuery, PAGE_SIZE, percentText, useItem, useItemHistory, useItemLookups, useItems,
  type ItemBarcode, type ItemCriteria, type ItemDetail, type ItemLookups,
} from './itemApi';

type Chip = 'all' | 'stockable' | 'nonStockable';

function useDebounced(value: string, delay = 250): string {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

/** Selectia din URL (?id=5 sau ?nou=1), ca fisa sa poata fi deschisa dintr-un link. */
function useSelection() {
  const [params, setParams] = useSearchParams();
  const id = Number(params.get('id'));
  return {
    id: Number.isInteger(id) && id > 0 ? id : undefined,
    creating: params.get('nou') === '1',
    select: (itemId: number) => setParams({ id: String(itemId) }),
    startCreate: () => setParams({ nou: '1' }),
    clear: () => setParams({}),
  };
}

const typeKey = (type: number) => `item.type.${type}` as TextKey;

/** Articole (macheta 05): lista cu cautare si filtre in stanga, fisa articolului ales in dreapta. */
export function ItemsPage() {
  const user = useOutletContext<CurrentUser>();
  const lookups = useItemLookups();
  const selection = useSelection();
  const [search, setSearch] = useState('');
  const [chip, setChip] = useState<Chip>('all');
  const [groupId, setGroupId] = useState<number | undefined>();
  const [itemType, setItemType] = useState<number | undefined>();
  const [showInactive, setShowInactive] = useState(false);
  const [propertyId, setPropertyId] = useState<number | undefined>();
  const [propertyValue, setPropertyValue] = useState<string | undefined>();
  const [owner, setOwner] = useState('');
  const ownerOptions = useOwnerOptions('Item');
  const debounced = useDebounced(search);
  const filterKey = JSON.stringify([debounced, chip, groupId, itemType, showInactive, propertyId, propertyValue, owner]);
  const [paging, setPaging] = useState({ key: filterKey, page: 1 });
  const page = paging.key === filterKey ? paging.page : 1;
  const canManage = user.permissions.includes('Core.Item.Manage');
  const canImport = user.permissions.includes('Core.Import.Run');
  const { notify } = useNotifier();
  const [exporting, setExporting] = useState(false);

  const property = lookups.data?.properties.find((p) => p.id === propertyId);
  const criteria: ItemCriteria = {
    search: debounced,
    isStockable: chip === 'stockable' ? true : chip === 'nonStockable' ? false : undefined,
    itemType,
    itemGroupId: groupId,
    showInactive,
    propertyDefinitionId: property !== undefined && propertyValue !== undefined ? property.id : undefined,
    propertyOptionId: property !== undefined && property.dataType !== DataType.Bool && propertyValue !== undefined ? Number(propertyValue) : undefined,
    propertyBool: property?.dataType === DataType.Bool && propertyValue !== undefined ? propertyValue === 'true' : undefined,
    owner,
    page,
  };
  const list = useItems(criteria);

  async function exportExcel() {
    setExporting(true);
    try {
      const query = Object.fromEntries(Object.entries(itemSearchQuery(criteria)).filter(([key]) => key !== 'Page' && key !== 'PageSize'));
      await downloadFile('/api/core/items/export', query, 'articole.xlsx');
      notify(t('item.exportDone'));
    } catch (e) {
      notify(e instanceof Error ? e.message : t('app.loadError'), 'error');
    } finally {
      setExporting(false);
    }
  }

  if (lookups.data === undefined) {
    return <div className="page" role="status">{lookups.isError ? t('app.loadError') : t('app.loading')}</div>;
  }
  const data = list.data;
  const groups = lookups.data.groups;
  const filterable = lookups.data.properties.filter((p) => p.isFilterable && [DataType.Bool, DataType.List, DataType.MultiList].includes(p.dataType as 4));
  const chips: [Chip, string][] = [['all', t('item.filter.all')], ['stockable', t('item.filter.stockable')], ['nonStockable', t('item.filter.nonStockable')]];

  return (
    <div className="page wide">
      <div className="crumb">{t('menu.nomenclatures')} › {t('menu.items')}</div>
      <div className="head">
        <div>
          <h1>{t('item.title')}</h1>
          <div className="sub2">{t('item.subtitle', { count: (data?.activeCount ?? 0).toLocaleString('ro-RO') })}</div>
        </div>
        <div className="actions">
          <button type="button" className="btn ghost" disabled={exporting} onClick={() => void exportExcel()}>
            {t('partner.export')}
          </button>
          {canImport && (
            <Link to="/admin/config/import?entitate=Item" className="btn ghost">
              {t('partner.import')}
            </Link>
          )}
          {canManage && (
            <button type="button" className="btn primary" onClick={selection.startCreate}>
              {t('item.new')}
            </button>
          )}
        </div>
      </div>

      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.items')}>
          <TextBoxComponent
            placeholder={t('item.searchPlaceholder')}
            value={search}
            input={(args: { value: string }) => setSearch(args.value)}
            showClearButton
            htmlAttributes={{ 'aria-label': t('item.searchPlaceholder') }}
          />
          <div className="chips-row">
            {chips.map(([key, label]) => (
              <button key={key} type="button" className={chip === key ? 'chip on' : 'chip'} aria-pressed={chip === key} onClick={() => setChip(key)}>
                {label}
              </button>
            ))}
            <select className="chip" aria-label={t('item.filter.group')} value={groupId ?? ''} onChange={(e) => setGroupId(e.target.value === '' ? undefined : Number(e.target.value))}>
              <option value="">{t('item.filter.anyGroup')}</option>
              {groups.filter((g) => g.isActive).map((g) => ({ id: g.id, text: groupPath(groups, g.id) })).sort((a, b) => a.text.localeCompare(b.text, 'ro'))
                .map((g) => <option key={g.id} value={g.id}>{g.text}</option>)}
            </select>
            <select className="chip" aria-label={t('item.filter.type')} value={itemType ?? ''} onChange={(e) => setItemType(e.target.value === '' ? undefined : Number(e.target.value))}>
              <option value="">{t('item.filter.anyType')}</option>
              {ITEM_TYPES.map((type) => <option key={type} value={type}>{t(typeKey(type))}</option>)}
            </select>
            <OwnerFilter options={ownerOptions.data} value={owner} onChange={setOwner} />
            <button type="button" className={showInactive ? 'chip on' : 'chip'} aria-pressed={showInactive} onClick={() => setShowInactive(!showInactive)}>
              {t('partner.filter.showInactive')}
            </button>
          </div>
          {filterable.length > 0 && (
            <div className="chips-row property-filter">
              <select className="chip" aria-label={t('partner.filter.property')} value={propertyId ?? ''}
                onChange={(e) => {
                  setPropertyId(e.target.value === '' ? undefined : Number(e.target.value));
                  setPropertyValue(undefined);
                }}>
                <option value="">{t('partner.filter.noProperty')}</option>
                {filterable.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
              {property !== undefined && (
                <select className="chip" aria-label={t('partner.filter.value')} value={propertyValue ?? ''}
                  onChange={(e) => setPropertyValue(e.target.value === '' ? undefined : e.target.value)}>
                  <option value="">{t('common.select')}</option>
                  {(property.dataType === DataType.Bool
                    ? [{ value: 'true', text: t('common.yes') }, { value: 'false', text: t('common.no') }]
                    : lookups.data.options.filter((o) => o.propertyDefinitionId === property.id && o.isActive).map((o) => ({ value: String(o.id), text: o.label })))
                    .map((v) => <option key={v.value} value={v.value}>{v.text}</option>)}
                </select>
              )}
            </div>
          )}
          {list.isError && data === undefined && (
            <div className="banner error" role="alert">
              {t('app.loadError')}{' '}
              <button type="button" className="btn ghost" onClick={() => void list.refetch()}>
                {t('app.retry')}
              </button>
            </div>
          )}
          {data !== undefined && data.items.length === 0 && <p className="muted">{t('item.empty')}</p>}
          {data !== undefined && data.items.length > 0 && (
            <table className="partner-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('item.col.item')}</th>
                  <th scope="col">{t('item.col.type')}</th>
                  <th scope="col">{t('item.col.stock')}</th>
                  <th scope="col">{t('item.col.vat')}</th>
                  <th scope="col">{t('owner.field')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((item) => {
                  const current = item.id === selection.id;
                  return (
                    <tr key={item.id} className={[current ? 'current' : '', item.isActive ? '' : 'inactive'].join(' ').trim()} onClick={() => selection.select(item.id)}>
                      <td>
                        <button type="button" className="row-link" aria-current={current ? 'true' : undefined} onClick={(e) => {
                          e.stopPropagation();
                          selection.select(item.id);
                        }}>
                          {item.name}
                        </button>
                        <div className="muted small">{item.barcode == null ? item.code : `${item.code} · ${item.barcode}`}</div>
                      </td>
                      <td><span className="tag type">{t(typeKey(item.itemType))}</span></td>
                      <td><span className={item.isStockable ? 'pill ok' : 'pill violet'}>{t(item.isStockable ? 'item.stockable' : 'item.nonStockable')}</span></td>
                      <td>{percentText(item.vatPercent)}</td>
                      <td className="small">{ownerLabel(item.ownerLevel, item.ownerName)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
          {data !== undefined && data.total > 0 && (
            <Pager page={page} total={data.total} onPage={(next) => setPaging({ key: filterKey, page: next })} />
          )}
        </section>

        <section className="card detail-card">
          {selection.creating ? (
            <NewItemPanel lookups={lookups.data} canManage={canManage} onSaved={(id) => selection.select(id)} onCancel={selection.clear} />
          ) : selection.id === undefined ? (
            <p className="muted empty-detail">{t('item.select')}</p>
          ) : (
            <ItemPanel key={selection.id} itemId={selection.id} lookups={lookups.data} canManage={canManage} />
          )}
        </section>
      </div>
    </div>
  );
}

function Pager({ page, total, onPage }: { readonly page: number; readonly total: number; readonly onPage: (page: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  return (
    <div className="pager">
      <span className="muted">{t('partner.pageInfo', { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, total), total: total.toLocaleString('ro-RO') })}</span>
      <div className="actions">
        <button type="button" className="btn ghost" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label={t('partner.prevPage')}>‹</button>
        <span>{`${page} / ${pages}`}</span>
        <button type="button" className="btn ghost" disabled={page >= pages} onClick={() => onPage(page + 1)} aria-label={t('partner.nextPage')}>›</button>
      </div>
    </div>
  );
}

const formTabs: readonly ItemSection[] = ['general', 'uom', 'properties'];
const formTabLabel: Record<ItemSection, TextKey> = { general: 'item.tab.general', uom: 'item.tab.uom', properties: 'item.tab.properties' };

function NewItemPanel({ lookups, canManage, onSaved, onCancel }: {
  readonly lookups: ItemLookups;
  readonly canManage: boolean;
  readonly onSaved: (id: number) => void;
  readonly onCancel: () => void;
}) {
  const [section, setSection] = useState<ItemSection>('general');
  return (
    <div className="company-panel">
      <div className="tabs" role="tablist" aria-label={t('item.newTitle')}>
        {formTabs.map((key, index) => (
          <button key={key} type="button" role="tab" id={`tab-${key}`} aria-selected={section === key} aria-controls="item-form"
            tabIndex={section === key ? 0 : -1} className={section === key ? 'tab on' : 'tab'} onClick={() => setSection(key)}
            onKeyDown={(event) => {
              const target = tabKeyTarget(index, event.key, formTabs.length);
              if (target !== undefined) {
                event.preventDefault();
                setSection(formTabs[target] ?? 'general');
              }
            }}>
            {t(formTabLabel[key])}
          </button>
        ))}
      </div>
      <div id="item-form" role="tabpanel" aria-labelledby={`tab-${section}`}>
        <ItemForm key="new" item={undefined} lookups={lookups} section={section} canManage={canManage} onSaved={(saved) => onSaved(saved.id)} onCancel={onCancel} />
      </div>
      <p className="muted small">{t('item.saveFirst')}</p>
    </div>
  );
}

type ItemTab = ItemSection | 'barcodes' | 'history';

function ItemPanel({ itemId, lookups, canManage }: { readonly itemId: number; readonly lookups: ItemLookups; readonly canManage: boolean }) {
  const item = useItem(itemId);
  const [tab, setTab] = useState<ItemTab>('general');
  const [visited, setVisited] = useState<ReadonlySet<ItemTab>>(() => new Set<ItemTab>(['general']));
  const tabButtons = useRef(new Map<ItemTab, HTMLButtonElement>());
  const history = useItemHistory(itemId, tab === 'history');

  if (item.data === undefined) {
    return <p role="status">{item.isError ? item.error.message : t('app.loading')}</p>;
  }
  const data = item.data;
  const tabs: [ItemTab, string][] = [
    ['general', t('item.tab.general')],
    ['uom', t('item.tab.uom')],
    ['properties', t('item.tab.properties')],
    ['barcodes', t('item.tab.barcodes')],
    ['history', t('item.tab.history')],
  ];
  const open = (next: ItemTab, focus = false) => {
    setTab(next);
    setVisited((current) => (current.has(next) ? current : new Set([...current, next])));
    if (focus) {
      tabButtons.current.get(next)?.focus();
    }
  };
  const panel = (key: ItemTab, content: ReactNode) =>
    visited.has(key) && (
      <div role="tabpanel" id={`panel-${key}`} aria-labelledby={`tab-${key}`} hidden={tab !== key}>
        {content}
      </div>
    );
  const formSection: ItemSection | undefined = tab === 'general' || tab === 'uom' || tab === 'properties' ? tab : undefined;
  const group = groupPath(lookups.groups, data.itemGroupId);

  return (
    <div className="company-panel">
      <div className="panel-head">
        <div>
          <h2>
            {data.name} <span className={data.isActive ? 'pill ok' : 'pill off'}>{data.isActive ? t('common.active') : t('common.inactive')}</span>
          </h2>
          <div className="muted">
            {t('item.header', { code: data.code })}
            {group === '' ? '' : ` · ${t('item.headerGroup', { group })}`}
          </div>
          <div className="chips-row">
            <span className="chip static">{t('item.barcodeCount', { count: data.barcodes.length })}</span>
          </div>
        </div>
      </div>
      <div className="tabs" role="tablist" aria-label={data.name}>
        {tabs.map(([key, label], index) => (
          <button
            key={key}
            ref={(button) => {
              if (button === null) {
                tabButtons.current.delete(key);
              } else {
                tabButtons.current.set(key, button);
              }
            }}
            type="button"
            role="tab"
            id={`tab-${key}`}
            aria-selected={tab === key}
            aria-controls={formTabs.includes(key as ItemSection) ? 'item-form' : visited.has(key) ? `panel-${key}` : undefined}
            tabIndex={tab === key ? 0 : -1}
            className={tab === key ? 'tab on' : 'tab'}
            onClick={() => open(key)}
            onKeyDown={(event) => {
              const target = tabKeyTarget(index, event.key, tabs.length);
              const next = target === undefined ? undefined : tabs[target]?.[0];
              if (next !== undefined) {
                event.preventDefault();
                open(next, true);
              }
            }}
          >
            {label}
          </button>
        ))}
      </div>
      <div id="item-form" role="tabpanel" aria-labelledby={`tab-${formSection ?? 'general'}`} hidden={formSection === undefined}>
        <ItemForm item={data} lookups={lookups} section={formSection} canManage={canManage} onSaved={() => undefined} onCancel={() => undefined} />
      </div>
      {panel('barcodes', <BarcodesTab item={data} lookups={lookups} canManage={canManage} />)}
      {panel('history', <HistoryView history={history} />)}
    </div>
  );
}

const barcodeSchema = z.object({
  barcode: required.max(50),
  barcodeType: z.number().int().min(1).max(4),
  uomId: z.number().nullable(),
  isActive: z.boolean(),
});
type BarcodeValues = z.infer<typeof barcodeSchema>;

/** Codurile de bare ale articolului; unul legat de o UM aduce la scanare UM respectiva (de exemplu baxul). */
function BarcodesTab({ item, lookups, canManage }: { readonly item: ItemDetail; readonly lookups: ItemLookups; readonly canManage: boolean }) {
  const [selected, setSelected] = useState<ItemBarcode | 'new' | undefined>();
  const barcode = selected === 'new' ? undefined : selected;
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const defaults: BarcodeValues = barcode === undefined
    ? { barcode: '', barcodeType: 1, uomId: null, isActive: true }
    : { barcode: barcode.barcode, barcodeType: barcode.barcodeType, uomId: barcode.uomId ?? null, isActive: barcode.isActive };
  const form = useForm<BarcodeValues>({ resolver: zodResolver(barcodeSchema) as Resolver<BarcodeValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [selected]); // eslint-disable-line react-hooks/exhaustive-deps
  const guard = useUnsavedChanges(selected !== undefined && form.formState.isDirty);
  const units = useMemo(() => new Map(lookups.units.map((u) => [u.id, u])), [lookups.units]);
  const set = lookups.uomSets.find((s) => s.id === item.uomSetId);
  const uomOptions = useMemo<Option[]>(
    () => effectiveLines(set?.lines ?? [], item.uoms).filter((l) => !l.isBase).map((l) => ({ value: String(l.uomId), text: `${units.get(l.uomId)?.code ?? ''} (${l.factorToBase})` })),
    [set, item.uoms, units],
  );
  const typeOptions = useMemo<Option[]>(() => [1, 2, 3, 4].map((type) => ({ value: String(type), text: t(`item.barcode.type.${type}` as TextKey) })), []);
  const rows = useMemo(
    () => item.barcodes.map((b) => ({ ...b, typeText: t(`item.barcode.type.${b.barcodeType}` as TextKey), uomText: b.uomId == null ? t('item.barcode.baseUom') : (units.get(b.uomId)?.code ?? '') })),
    [item.barcodes, units],
  );
  const onRowSelected = useCallback((args: { data: ItemBarcode }) => {
    if (canManage) {
      setSelected(args.data);
    }
  }, [canManage]);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      await itemApi.saveBarcode({ ...values, id: barcode?.id ?? null, itemId: item.id, rowVersion: barcode?.rowVersion ?? null });
      form.reset(form.getValues());
      guard.markClean();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: itemKeys.detail(item.id) }),
        queryClient.invalidateQueries({ queryKey: itemKeys.history(item.id) }),
      ]);
      notify(t('common.saved'));
      setSelected(undefined);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  return (
    <div className="tab-body">
      <div className="toolbar">
        {canManage && (
          <button type="button" className="btn ghost" onClick={() => setSelected('new')}>
            {t('item.barcode.add')}
          </button>
        )}
      </div>
      {rows.length === 0 ? (
        <p className="muted">{t('item.barcode.empty')}</p>
      ) : (
        <GridComponent dataSource={rows} rowSelected={onRowSelected} allowSorting height="auto">
          <ColumnsDirective>
            <ColumnDirective field="barcode" headerText={t('item.barcode.code')} width="200" />
            <ColumnDirective field="typeText" headerText={t('item.barcode.type')} width="110" />
            <ColumnDirective field="uomText" headerText={t('item.barcode.uom')} />
            <ColumnDirective field="isActive" headerText={t('common.active')} width="90" displayAsCheckBox type="boolean" />
          </ColumnsDirective>
        </GridComponent>
      )}
      {selected !== undefined && (
        <FieldIdPrefix.Provider value="barcode">
          <form className="subform" noValidate onSubmit={(e) => void submit(e)} aria-label={t('item.barcode.edit')}>
            <ErrorBanner message={error} />
            <div className="grid g3">
              <TextField control={form.control} name="barcode" label={t('item.barcode.code')} />
              <SelectField control={form.control} name="barcodeType" label={t('item.barcode.type')} options={typeOptions} numeric />
              <SelectField control={form.control} name="uomId" label={t('item.barcode.uom')} options={uomOptions} allowEmpty={t('item.barcode.baseUom')} numeric />
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
        </FieldIdPrefix.Provider>
      )}
    </div>
  );
}
