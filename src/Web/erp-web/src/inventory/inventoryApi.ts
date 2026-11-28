import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

type Schemas = components['schemas'];
export type WarehousePage = Schemas['WarehousePageDto'];
export type WarehouseItem = Schemas['WarehouseItemDto'];
export type WarehouseDetail = Schemas['WarehouseDetailDto'];
export type WarehouseLookups = Schemas['WarehouseLookupsDto'];
export type SaveWarehouse = Schemas['SaveWarehouse'];
export type WarehouseKeepers = Schemas['WarehouseKeepersDto'];
export type SaveWarehouseKeeper = Schemas['SaveWarehouseKeeper'];
export type DepotPage = Schemas['DepotPageDto'];
export type DepotItem = Schemas['DepotItemDto'];
export type ItemStockTab = Schemas['ItemStockTabDto'];
export type SaveItemStockSettings = Schemas['SaveItemStockSettings'];
export type SaveItemWarehouseSettings = Schemas['SaveItemWarehouseSettings'];

/** Tipul gestiunii (02.05, S1 3.1); aceleasi valori ca WarehouseKind pe server. */
export const WarehouseKind = { Depot: 1, Store: 2, Transit: 3, Custody: 4, AtThirdParty: 5 } as const;
export type WarehouseKindValue = (typeof WarehouseKind)[keyof typeof WarehouseKind];

/** Tipurile pe care utilizatorul le poate crea: tranzitul se creeaza automat, unul pe firma. */
export const creatableKinds: readonly WarehouseKindValue[] = [WarehouseKind.Depot, WarehouseKind.Store, WarehouseKind.Custody, WarehouseKind.AtThirdParty];

export const ValuationBasis = { Cost: 1, RetailPrice: 2 } as const;
export const NegativeStock = { Blocked: 1, AllowedWithWarning: 2 } as const;
export const Purpose = { Regular: 1, CustomerReturns: 2, DepositPackaging: 3 } as const;
export const StockLevel = { Warehouse: 1, Depot: 2 } as const;
export const KeeperRole = { Holder: 1, Substitute: 2 } as const;
export const HandoverStatus = { Draft: 1, Counting: 2, Completed: 3, Cancelled: 4 } as const;
export const ValuationMethod = { Fifo: 1, WeightedAverage: 2, Lifo: 3, SpecificIdentification: 4 } as const;

const kindLabels: Readonly<Record<WarehouseKindValue, TextKey>> = {
  1: 'inv.kind.depot',
  2: 'inv.kind.store',
  3: 'inv.kind.transit',
  4: 'inv.kind.custody',
  5: 'inv.kind.atThirdParty',
};

export const kindLabel = (kind: number): TextKey => kindLabels[kind as WarehouseKindValue];

/** Tipurile cu locatie (la nivelul „gestiune”) si cele cu partener (S1 3.1). */
export const usesLocation = (kind: number) => kind === WarehouseKind.Depot || kind === WarehouseKind.Store || kind === WarehouseKind.Custody;
export const usesThirdParty = (kind: number) => kind === WarehouseKind.Custody || kind === WarehouseKind.AtThirdParty;
/** Stocul negativ permis doar pe depozit, magazin si la terti (S1 3.6). */
export const allowsNegativeStock = (kind: number) => kind === WarehouseKind.Depot || kind === WarehouseKind.Store || kind === WarehouseKind.AtThirdParty;

export const methodLabel = (method: number): TextKey =>
  (({ 1: 'inv.method.fifo', 2: 'inv.method.average', 3: 'inv.method.lifo', 4: 'inv.method.specific' }) as Record<number, TextKey>)[method]
  ?? 'inv.method.average';

export const trackingLabel = (mode: number): TextKey =>
  (({ 0: 'inv.tracking.none', 1: 'inv.tracking.lot', 2: 'inv.tracking.lotExpiry', 3: 'inv.tracking.serial', 4: 'inv.tracking.serialLot' }) as Record<number, TextKey>)[mode]
  ?? 'inv.tracking.none';

export const handoverStatusLabel = (status: number): TextKey =>
  (({ 1: 'inv.handover.draft', 2: 'inv.handover.counting', 3: 'inv.handover.completed', 4: 'inv.handover.cancelled' }) as Record<number, TextKey>)[status]
  ?? 'inv.handover.draft';

/** Filtrul listei de gestiuni (chip-urile machetei s1-01). */
export type WarehouseFilter = 'all' | 'active' | 'blocked' | 'inactive';

export function filterWarehouses(rows: readonly WarehouseItem[], filter: WarehouseFilter): WarehouseItem[] {
  switch (filter) {
    case 'active':
      return rows.filter((w) => w.isActive);
    case 'blocked':
      return rows.filter((w) => w.isBlocked);
    case 'inactive':
      return rows.filter((w) => !w.isActive);
    default:
      return [...rows];
  }
}

/** Titularul gestiunii la o data ISO (aaaa-ll-zz), din perioadele gestionarilor (S1 3.2). */
export function holderOn(keepers: WarehouseKeepers['keepers'], isoDate: string): WarehouseKeepers['keepers'][number] | undefined {
  return keepers.find((k) => k.role === KeeperRole.Holder && k.validFrom <= isoDate && (k.validTo == null || k.validTo >= isoDate));
}

/** Cantitate introdusa romaneste (12,5 sau 12.5), cel mult 6 zecimale; gol = fara valoare. */
export const quantityPattern = /^\d{1,13}([.,]\d{1,6})?$/;
export const quantityForApi = (value: string): string | null => (value.trim() === '' ? null : value.trim().replace(',', '.'));
export const quantityForInput = (value: string | null | undefined): string => (value == null ? '' : value.replace('.', ','));

export const inventoryKeys = {
  all: ['inventory'] as const,
  warehouses: (search: string) => ['inventory', 'warehouses', search] as const,
  warehouse: (id: number) => ['inventory', 'warehouse', id] as const,
  keepers: (id: number) => ['inventory', 'keepers', id] as const,
  depots: ['inventory', 'depots'] as const,
  itemStock: (itemId: number) => ['inventory', 'item-stock', itemId] as const,
};

export function useWarehouses(search: string) {
  return useQuery({
    queryKey: inventoryKeys.warehouses(search),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/warehouses', {
      params: { query: { IncludeInactive: true, ...(search.trim() === '' ? {} : { Search: search.trim() }) } },
      signal,
    })),
    placeholderData: keepPreviousData,
  });
}

export function useWarehouse(id: number) {
  return useQuery({
    queryKey: inventoryKeys.warehouse(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/warehouses/{id}', { params: { path: { id } }, signal })),
  });
}

export function useWarehouseHistory(id: number | undefined, enabled: boolean) {
  return useQuery({
    queryKey: ['inventory', 'warehouse-history', id ?? 0] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/warehouses/{id}/history', { params: { path: { id: id ?? 0 } }, signal })),
    enabled: enabled && id !== undefined,
  });
}

export function useWarehouseKeepers(id: number, enabled: boolean) {
  return useQuery({
    queryKey: inventoryKeys.keepers(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/warehouses/{id}/keepers', { params: { path: { id } }, signal })),
    enabled,
  });
}

export function useDepots() {
  return useQuery({
    queryKey: inventoryKeys.depots,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/depots', { signal })),
  });
}

export function useItemStock(itemId: number, enabled: boolean) {
  return useQuery({
    queryKey: inventoryKeys.itemStock(itemId),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/items/{itemId}/stock-settings', { params: { path: { itemId } }, signal })),
    enabled,
  });
}

export const inventoryApi = {
  async saveWarehouse(body: SaveWarehouse) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/warehouses', { body }))
      : unwrap(await api.PUT('/api/inventory/warehouses/{id}', { params: { path: { id: body.id } }, body }));
  },
  async block(id: number, reason: string, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/warehouses/{id}/block', { params: { path: { id } }, body: { id, reason, rowVersion } }));
  },
  async unblock(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/warehouses/{id}/unblock', { params: { path: { id } }, body: { rowVersion } }));
  },
  async setActive(id: number, active: boolean, rowVersion: string) {
    return active
      ? unwrap(await api.POST('/api/inventory/warehouses/{id}/reactivate', { params: { path: { id } }, body: { rowVersion } }))
      : unwrap(await api.POST('/api/inventory/warehouses/{id}/deactivate', { params: { path: { id } }, body: { rowVersion } }));
  },
  async saveKeeper(warehouseId: number, body: SaveWarehouseKeeper) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/warehouses/{id}/keepers', { params: { path: { id: warehouseId } }, body }))
      : unwrap(await api.PUT('/api/inventory/warehouses/{id}/keepers/{keeperId}', { params: { path: { id: warehouseId, keeperId: body.id } }, body }));
  },
  async startHandover(warehouseId: number, handoverDate: string, toEmployeeId: number, notes: string | null) {
    return unwrap(await api.POST('/api/inventory/warehouses/{id}/handovers', {
      params: { path: { id: warehouseId } }, body: { warehouseId, handoverDate, toEmployeeId, notes },
    }));
  },
  async completeHandover(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/handovers/{id}/complete', { params: { path: { id } }, body: { rowVersion } }));
  },
  async cancelHandover(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/handovers/{id}/cancel', { params: { path: { id } }, body: { rowVersion } }));
  },
  async saveDepot(body: Schemas['SaveDepot']) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/depots', { body }))
      : unwrap(await api.PUT('/api/inventory/depots/{id}', { params: { path: { id: body.id } }, body }));
  },
  async setDepotWarehouse(depotId: number, warehouseId: number, isActive: boolean) {
    return unwrap(await api.POST('/api/inventory/depots/{id}/warehouses', { params: { path: { id: depotId } }, body: { depotId, warehouseId, isActive } }));
  },
  async setStockLevel(body: Schemas['SetStockLevel']) {
    return unwrap(await api.PUT('/api/inventory/stock-level', { body }));
  },
  async saveItemStock(body: SaveItemStockSettings) {
    return unwrap(await api.PUT('/api/inventory/items/{itemId}/stock-settings', { params: { path: { itemId: body.itemId } }, body }));
  },
  async saveItemWarehouse(body: SaveItemWarehouseSettings) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/items/{itemId}/warehouse-settings', { params: { path: { itemId: body.itemId } }, body }))
      : unwrap(await api.PUT('/api/inventory/items/{itemId}/warehouse-settings/{id}', { params: { path: { itemId: body.itemId, id: body.id } }, body }));
  },
  async deleteItemWarehouse(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/item-warehouse-settings/{id}/delete', { params: { path: { id } }, body: { rowVersion } }));
  },
};
