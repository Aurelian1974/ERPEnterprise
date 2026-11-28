import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

type Schemas = components['schemas'];
export type LandedCost = Schemas['LandedCostDto'];
export type LandedCostItem = Schemas['LandedCostItemDto'];
export type RetailPriceChange = Schemas['RetailPriceChangeDto'];
export type RetailPriceChangeItem = Schemas['RetailPriceChangeItemDto'];
export type RetailDischarge = Schemas['RetailDischargeDto'];
export type ValuationChange = Schemas['ValuationChangeDto'];
export type ValuationChangeItem = Schemas['ValuationChangeListItemDto'];
export type CostLayers = Schemas['CostLayersDto'];
export type CostRecalcQueueItem = Schemas['CostRecalcQueueItemDto'];
export type CostAdjustment = Schemas['CostAdjustmentDto'];

/** Starea documentelor de evaluare (CA, MP, SM): 1 ciorna, 2 validat, 4 anulat. */
export const ValuationStatus = { Draft: 1, Validated: 2, Cancelled: 4 } as const;

export const valuationStatusLabel = (status: number): TextKey =>
  (({ 1: 'inv.status.draft', 2: 'inv.status.validated', 4: 'inv.status.cancelled' }) as Record<number, TextKey>)[status] ?? 'inv.status.draft';

export const landedSourceLabel = (kind: number): TextKey =>
  (({ 1: 'inv.landed.kind.service', 2: 'inv.landed.kind.priceDifference', 3: 'inv.landed.kind.rebate' }) as Record<number, TextKey>)[kind]
  ?? 'inv.landed.kind.service';

export const allocationBasisLabel = (basis: number): TextKey =>
  (({ 1: 'inv.landed.basis.value', 2: 'inv.landed.basis.quantity', 3: 'inv.landed.basis.weight', 4: 'inv.landed.basis.volume', 5: 'inv.landed.basis.manual' }) as Record<number, TextKey>)[basis]
  ?? 'inv.landed.basis.value';

export const adjustmentKindLabel = (kind: number): TextKey =>
  (({ 1: 'inv.adjustment.recalculation', 2: 'inv.adjustment.negative', 3: 'inv.adjustment.landed', 4: 'inv.adjustment.priceDifference', 5: 'inv.adjustment.retail' }) as Record<number, TextKey>)[kind]
  ?? 'inv.adjustment.recalculation';

/** Prima zi a lunii unei date ISO (AAAA-LL-ZZ sau AAAA-LL): documentele SM si DA privesc o luna intreaga. */
export function monthStart(iso: string): string {
  const match = /^(\d{4})-(\d{2})/.exec(iso);
  return match === null ? iso : `${match[1]}-${match[2]}-01`;
}

/** Suma cu doua zecimale scrisa de utilizator (virgula sau punct, cu semn) → text invariant pentru API; null daca nu e valida. */
export function moneyForApi(value: string): string | null {
  const text = value.trim().replace(/\s/g, '').replace(',', '.');
  return /^-?\d{1,15}(\.\d{1,2})?$/.test(text) ? text : null;
}

/** Valoare invarianta → afisare romaneasca (virgula zecimala, separator de mii). */
export function moneyToRo(value: string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  const negative = value.startsWith('-');
  const [whole = '0', fraction = ''] = value.replace('-', '').split('.');
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return `${negative ? '−' : ''}${grouped}${fraction === '' ? '' : `,${fraction}`}`;
}

export const valuationKeys = {
  all: ['inventory', 'valuation'] as const,
  landed: (status: number | undefined) => ['inventory', 'valuation', 'landed', status ?? 0] as const,
  landedDetail: (id: number) => ['inventory', 'valuation', 'landed', 'detail', id] as const,
  priceChanges: (warehouseId: number | undefined) => ['inventory', 'valuation', 'price-changes', warehouseId ?? 0] as const,
  priceChange: (id: number) => ['inventory', 'valuation', 'price-changes', 'detail', id] as const,
  discharges: (warehouseId: number | undefined) => ['inventory', 'valuation', 'discharges', warehouseId ?? 0] as const,
  simulation: (warehouseId: number, periodStart: string) => ['inventory', 'valuation', 'discharge-simulation', warehouseId, periodStart] as const,
  changes: ['inventory', 'valuation', 'changes'] as const,
  change: (id: number) => ['inventory', 'valuation', 'changes', 'detail', id] as const,
  layers: (variantId: number, warehouseId: number | undefined, openOnly: boolean) => ['inventory', 'valuation', 'layers', variantId, warehouseId ?? 0, openOnly] as const,
  queue: ['inventory', 'valuation', 'queue'] as const,
  adjustments: (kind: number | undefined) => ['inventory', 'valuation', 'adjustments', kind ?? 0] as const,
};

export const useLandedCosts = (status: number | undefined) => useQuery({
  queryKey: valuationKeys.landed(status),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/landed-costs', { params: { query: status === undefined ? {} : { Status: status } }, signal })),
  placeholderData: keepPreviousData,
});

export const useLandedCost = (id: number | undefined) => useQuery({
  queryKey: valuationKeys.landedDetail(id ?? 0),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/landed-costs/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  enabled: id !== undefined,
});

export const useRetailPriceChanges = (warehouseId: number | undefined) => useQuery({
  queryKey: valuationKeys.priceChanges(warehouseId),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/retail-price-changes', {
    params: { query: warehouseId === undefined ? {} : { WarehouseId: warehouseId } }, signal,
  })),
  placeholderData: keepPreviousData,
});

export const useRetailPriceChange = (id: number | undefined) => useQuery({
  queryKey: valuationKeys.priceChange(id ?? 0),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/retail-price-changes/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  enabled: id !== undefined,
});

export const useRetailDischarges = (warehouseId: number | undefined) => useQuery({
  queryKey: valuationKeys.discharges(warehouseId),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/retail-discharges', {
    params: { query: warehouseId === undefined ? {} : { WarehouseId: warehouseId } }, signal,
  })),
});

export const useDischargeSimulation = (warehouseId: number | undefined, periodStart: string) => useQuery({
  queryKey: valuationKeys.simulation(warehouseId ?? 0, periodStart),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/retail-discharges/simulate', {
    params: { query: { WarehouseId: warehouseId ?? 0, PeriodStart: periodStart } }, signal,
  })),
  enabled: warehouseId !== undefined,
  retry: false,
});

export const useValuationChanges = () => useQuery({
  queryKey: valuationKeys.changes,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/valuation-changes', { signal })),
});

export const useValuationChange = (id: number | undefined) => useQuery({
  queryKey: valuationKeys.change(id ?? 0),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/valuation-changes/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  enabled: id !== undefined,
});

export const useCostLayers = (variantId: number | undefined, warehouseId: number | undefined, openOnly: boolean) => useQuery({
  queryKey: valuationKeys.layers(variantId ?? 0, warehouseId, openOnly),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/cost-layers', {
    params: { query: { ItemVariantId: variantId ?? 0, OpenOnly: openOnly, ...(warehouseId === undefined ? {} : { WarehouseId: warehouseId }) } }, signal,
  })),
  enabled: variantId !== undefined,
});

export const useRecalcQueue = () => useQuery({
  queryKey: valuationKeys.queue,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/cost-recalc-queue', { signal })),
});

export const useCostAdjustments = (kind: number | undefined) => useQuery({
  queryKey: valuationKeys.adjustments(kind),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/cost-adjustments', { params: { query: kind === undefined ? {} : { Kind: kind } }, signal })),
  placeholderData: keepPreviousData,
});

export const valuationApi = {
  async saveLandedCost(body: Schemas['SaveLandedCost']) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/landed-costs', { body }))
      : unwrap(await api.PUT('/api/inventory/landed-costs/{id}', { params: { path: { id: body.id } }, body }));
  },
  async validateLandedCost(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/landed-costs/{id}/validate', { params: { path: { id } }, body: { rowVersion } }));
  },
  async cancelLandedCost(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/landed-costs/{id}/cancel', { params: { path: { id } }, body: { rowVersion } }));
  },
  async savePriceChange(body: Schemas['SaveRetailPriceChange']) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/retail-price-changes', { body }))
      : unwrap(await api.PUT('/api/inventory/retail-price-changes/{id}', { params: { path: { id: body.id } }, body }));
  },
  async validatePriceChange(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/retail-price-changes/{id}/validate', { params: { path: { id } }, body: { rowVersion } }));
  },
  async cancelPriceChange(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/retail-price-changes/{id}/cancel', { params: { path: { id } }, body: { rowVersion } }));
  },
  async validateDischarge(warehouseId: number, periodStart: string) {
    return unwrap(await api.POST('/api/inventory/retail-discharges', { body: { warehouseId, periodStart } }));
  },
  async saveValuationChange(body: Schemas['SaveValuationChange']) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/valuation-changes', { body }))
      : unwrap(await api.PUT('/api/inventory/valuation-changes/{id}', { params: { path: { id: body.id } }, body }));
  },
  async validateValuationChange(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/valuation-changes/{id}/validate', { params: { path: { id } }, body: { rowVersion } }));
  },
  async cancelValuationChange(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/valuation-changes/{id}/cancel', { params: { path: { id } }, body: { rowVersion } }));
  },
  async recalculate(item: CostRecalcQueueItem) {
    return unwrap(await api.POST('/api/inventory/cost-recalc', {
      body: { warehouseId: item.warehouseId, depotId: item.depotId ?? null, itemVariantId: item.itemVariantId, lotId: item.lotId ?? null },
    }));
  },
};
