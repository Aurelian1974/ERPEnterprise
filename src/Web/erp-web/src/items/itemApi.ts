import { ownerQuery } from '../org/owner';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components, paths } from '../api/schema';

type Schemas = components['schemas'];
export type ItemPage = Schemas['ItemPageDto'];
export type ItemListItem = Schemas['ItemListItemDto'];
export type ItemDetail = Schemas['ItemDetailDto'];
export type ItemLookups = Schemas['ItemLookupsDto'];
export type ItemBarcode = Schemas['ItemBarcodeDto'];
export type UomLine = Schemas['UomLineDto'];
export type UomSet = Schemas['UomSetDto'];
export type VatRate = Schemas['VatRateDto'];
export type UnitOfMeasure = Schemas['UnitOfMeasureDto'];
export type ItemGroup = Schemas['ItemGroupDto'];
export type SaveItem = Schemas['SaveItem'];
export type SaveItemBarcode = Schemas['SaveItemBarcode'];
export type Saved = Schemas['SavedEntity'];

/** Tipul articolului (02.01, 3.3). */
export const ItemType = { Merchandise: 1, Service: 6, Packaging: 7 } as const;
export const ITEM_TYPES = [1, 2, 3, 4, 5, 6, 7, 8] as const;

export interface ItemCriteria {
  readonly search: string;
  readonly isStockable?: boolean | undefined;
  readonly itemType?: number | undefined;
  readonly itemGroupId?: number | undefined;
  readonly showInactive: boolean;
  readonly propertyDefinitionId?: number | undefined;
  readonly propertyOptionId?: number | undefined;
  readonly propertyBool?: boolean | undefined;
  /** Proprietarul ales in filtru (cheile din org/owner); gol = toate cele vizibile. */
  readonly owner?: string | undefined;
  readonly page: number;
}

export const PAGE_SIZE = 50;

export const itemKeys = {
  all: ['core', 'items'] as const,
  list: (criteria: ItemCriteria) => ['core', 'items', 'list', criteria] as const,
  detail: (id: number) => ['core', 'items', 'detail', id] as const,
  history: (id: number) => ['core', 'items', 'history', id] as const,
  lookups: ['core', 'items', 'lookups'] as const,
};

type SearchQuery = NonNullable<NonNullable<paths['/api/core/items']['get']['parameters']['query']>>;

/** Parametrii cautarii fara filtrele necompletate; implicit doar articolele active. */
export function itemSearchQuery(criteria: ItemCriteria): SearchQuery {
  const search = criteria.search.trim();
  const entries: [keyof SearchQuery, string | number | boolean | undefined][] = [
    ['Search', search === '' ? undefined : search],
    ['IsStockable', criteria.isStockable],
    ['ItemType', criteria.itemType],
    ['ItemGroupId', criteria.itemGroupId],
    ['IsActive', criteria.showInactive ? undefined : true],
    ['PropertyDefinitionId', criteria.propertyDefinitionId],
    ['PropertyOptionId', criteria.propertyOptionId],
    ['PropertyBool', criteria.propertyBool],
    ['Page', criteria.page],
    ['PageSize', PAGE_SIZE],
  ];
  return { ...Object.fromEntries(entries.filter(([, value]) => value !== undefined)), ...ownerQuery(criteria.owner) } as SearchQuery;
}

export function useItems(criteria: ItemCriteria) {
  return useQuery({
    queryKey: itemKeys.list(criteria),
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/items', { params: { query: itemSearchQuery(criteria) }, signal })),
  });
}

export function useItem(id: number | undefined) {
  return useQuery({
    queryKey: itemKeys.detail(id ?? 0),
    enabled: id !== undefined,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/items/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  });
}

export function useItemHistory(id: number, enabled: boolean) {
  return useQuery({
    queryKey: itemKeys.history(id),
    enabled,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/items/{id}/history', { params: { path: { id } }, signal })),
  });
}

export function useItemLookups() {
  return useQuery({
    queryKey: itemKeys.lookups,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/items/lookups', { signal })),
    staleTime: 10 * 60_000,
  });
}

/** Ambalajele active (tip 7), pentru garantia SGR. */
export function usePackagingItems() {
  return useQuery({
    queryKey: ['core', 'items', 'packaging'] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/core/items', { params: { query: { ItemType: ItemType.Packaging, IsActive: true, PageSize: 200 } }, signal })),
    staleTime: 5 * 60_000,
  });
}

export const itemApi = {
  async save(body: SaveItem): Promise<Saved> {
    return body.id === null
      ? unwrap(await api.POST('/api/core/items', { body }))
      : unwrap(await api.PUT('/api/core/items/{id}', { params: { path: { id: body.id } }, body }));
  },
  async saveBarcode(body: SaveItemBarcode): Promise<Saved> {
    const itemId = body.itemId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/items/{itemId}/barcodes', { params: { path: { itemId } }, body }))
      : unwrap(await api.PUT('/api/core/items/{itemId}/barcodes/{id}', { params: { path: { itemId, id: body.id } }, body }));
  },
};

/** Numele grupei cu calea parintilor („Băuturi › Ape minerale”), pentru liste si antetul fisei. */
export function groupPath(groups: readonly ItemGroup[], id: number | null | undefined): string {
  const byId = new Map(groups.map((g) => [g.id, g]));
  const names: string[] = [];
  let current = id == null ? undefined : byId.get(id);
  for (let depth = 0; current !== undefined && depth < 20; depth++) {
    names.unshift(current.name);
    current = current.parentId == null ? undefined : byId.get(current.parentId);
  }
  return names.join(' › ');
}

/** Procentul cotei in format romanesc („11%”, „5,5%”), fara zecimale inutile. */
export function percentText(percent: string): string {
  return `${Number(percent).toLocaleString('ro-RO', { maximumFractionDigits: 2 })}%`;
}

/** Numar invariant („0.523”) in format romanesc pentru campuri („0,523”) si inapoi. */
export const toRo = (value: string | null | undefined) => (value == null ? null : value.replace('.', ','));
export const fromRo = (value: string | null) => (value === null || value.trim() === '' ? null : value.trim().replace(',', '.'));
