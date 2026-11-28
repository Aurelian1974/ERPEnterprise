import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

type Schemas = components['schemas'];
export type LotItem = Schemas['LotItemDto'];
export type Lot = Schemas['LotDto'];
export type LotAllocation = Schemas['LotAllocationDto'];
export type SerialItem = Schemas['SerialItemDto'];
export type Serial = Schemas['SerialDto'];
export type LotTrace = Schemas['LotTraceDto'];
export type LotTraceMovement = Schemas['LotTraceMovementDto'];
export type Recall = Schemas['RecallDto'];
export type RecallItem = Schemas['RecallItemDto'];
export type RecallCustomer = Schemas['RecallCustomerDto'];
export type LotChange = Schemas['LotChangeDto'];
export type LotChangeItem = Schemas['LotChangeItemDto'];
export type PartnerOption = Schemas['PartnerListItemDto'];

/** Starea lotului (S4 3.2): 1 disponibil, 2 carantina, 3 blocat, 4 expirat, 5 retras. */
export const LotStatus = { Available: 1, Quarantine: 2, Blocked: 3, Expired: 4, Recalled: 5 } as const;

/** Starea seriei (S4 3.4): 1 in stoc, 2 tranzit, 3 vanduta, 4 returnata, 5 casata, 6 blocata. */
export const SerialStatus = { InStock: 1, InTransit: 2, Sold: 3, Returned: 4, Scrapped: 5, Blocked: 6 } as const;

/** Starea retragerii: 1 ciorna, 2 deschisa, 3 inchisa, 4 anulata. */
export const RecallStatus = { Draft: 1, Open: 2, Closed: 3, Cancelled: 4 } as const;

export const lotStatusLabel = (status: number): TextKey =>
  (({ 1: 'inv.lot.status.available', 2: 'inv.lot.status.quarantine', 3: 'inv.lot.status.blocked', 4: 'inv.lot.status.expired', 5: 'inv.lot.status.recalled' }) as Record<number, TextKey>)[status]
  ?? 'inv.lot.status.available';

/** Clasa pastilei dupa starea lotului: verde disponibil, violet carantina, rosu blocat/expirat/retras. */
export const lotStatusTone = (status: number): string =>
  (({ 1: 'ok', 2: 'violet', 3: 'bad', 4: 'bad', 5: 'bad' }) as Record<number, string>)[status] ?? 'ok';

export const serialStatusLabel = (status: number): TextKey =>
  (({ 1: 'inv.serial.status.inStock', 2: 'inv.serial.status.inTransit', 3: 'inv.serial.status.sold', 4: 'inv.serial.status.returned', 5: 'inv.serial.status.scrapped', 6: 'inv.serial.status.blocked' }) as Record<number, TextKey>)[status]
  ?? 'inv.serial.status.inStock';

export const recallStatusLabel = (status: number): TextKey =>
  (({ 1: 'inv.status.draft', 2: 'inv.recall.status.open', 3: 'inv.recall.status.closed', 4: 'inv.status.cancelled' }) as Record<number, TextKey>)[status] ?? 'inv.status.draft';

export const expiryKindLabel = (kind: number | null | undefined): TextKey =>
  kind === 1 ? 'inv.lot.expiry.useBy' : kind === 2 ? 'inv.lot.expiry.bestBefore' : 'inv.lot.expiry.none';

export const recallOutcomeLabel = (outcome: number | null | undefined): TextKey =>
  (({ 1: 'inv.recall.outcome.returned', 2: 'inv.recall.outcome.destroyed', 3: 'inv.recall.outcome.none' }) as Record<number, TextKey>)[outcome ?? 0] ?? 'inv.recall.outcome.pending';

/**
 * Pastila termenului (macheta s4-01, s4-02): expirat (rosu), expira curand (galben, in pragul de avertizare) sau nimic. Lotul fara termen nu are pastila.
 */
export function expiryTone(daysLeft: number | null | undefined, warningDays = 30): 'bad' | 'warn' | undefined {
  if (daysLeft === null || daysLeft === undefined) {
    return undefined;
  }
  if (daysLeft < 0) {
    return 'bad';
  }
  return daysLeft <= warningDays ? 'warn' : undefined;
}

/** Cantitate invarianta (12000.000000) → afisare romaneasca fara zecimalele nule (12.000, 2,5). */
export function qtyToRo(value: string | number | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  const text = typeof value === 'number' ? String(value) : value;
  const negative = text.startsWith('-');
  const [whole = '0', fraction = ''] = text.replace('-', '').split('.');
  const trimmed = fraction.replace(/0+$/, '');
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return `${negative ? '−' : ''}${grouped}${trimmed === '' ? '' : `,${trimmed}`}`;
}

/** Totalurile listei de loturi pe gestiune (subsolul machetei s4-01): bucatile, numarul loturilor si disponibilul (doar starea Disponibil). */
export function lotTotals(rows: readonly LotItem[]): { qty: number; lots: number; available: number } {
  const lots = new Set(rows.map((r) => r.lotId));
  return {
    qty: rows.reduce((sum, r) => sum + Number(r.qtyOnHand), 0),
    lots: lots.size,
    available: rows.filter((r) => r.status === LotStatus.Available && (r.daysLeft ?? 0) >= 0).reduce((sum, r) => sum + Number(r.qtyOnHand), 0),
  };
}

/** Clientii retragerii grupati (macheta s4-05): bucatile, documentele distincte si ultima livrare, cei mai mari intai. */
export function groupCustomers(rows: readonly RecallCustomer[]): { partnerId: number | null; name: string; documents: number; qty: number; last: string }[] {
  const map = new Map<string, { partnerId: number | null; name: string; docs: Set<string>; qty: number; last: string }>();
  for (const r of rows) {
    const key = String(r.partnerId ?? 0);
    const entry = map.get(key) ?? { partnerId: r.partnerId ?? null, name: r.partnerName ?? '—', docs: new Set<string>(), qty: 0, last: r.movementDate };
    entry.docs.add(`${r.sourceDocumentType}:${r.sourceDocumentId}`);
    entry.qty += Number(r.qtyBase);
    entry.last = r.movementDate > entry.last ? r.movementDate : entry.last;
    map.set(key, entry);
  }
  return [...map.values()].map((e) => ({ partnerId: e.partnerId, name: e.name, documents: e.docs.size, qty: e.qty, last: e.last }))
    .sort((a, b) => b.qty - a.qty);
}

/** Miscarile de iesire spre clienti din trasabilitate (inainte), pentru rezumatul „livrate la N clienti”. */
export function deliveredSummary(forward: readonly LotTraceMovement[]): { received: number; delivered: number; customers: number } {
  const salesTypes = ['ISSUE_SALE', 'POS_SALE', 'RETURN_FROM_CUSTOMER'];
  const received = forward.filter((m) => Number(m.qtyBase) > 0 && m.depth === 0 && !m.movementTypeCode.startsWith('TRANSFER') && !salesTypes.includes(m.movementTypeCode))
    .reduce((s, m) => s + Number(m.qtyBase), 0);
  const sales = forward.filter((m) => m.partnerId != null && salesTypes.includes(m.movementTypeCode));
  return {
    received,
    delivered: -sales.reduce((s, m) => s + Number(m.qtyBase), 0),
    customers: new Set(sales.filter((m) => Number(m.qtyBase) < 0).map((m) => m.partnerId)).size,
  };
}

export const lotKeys = {
  all: ['inventory', 'lots'] as const,
  list: (variant: number | undefined, warehouse: number | undefined, search: string, expiring: number | undefined) =>
    ['inventory', 'lots', 'list', variant ?? 0, warehouse ?? 0, search, expiring ?? -1] as const,
  detail: (id: number) => ['inventory', 'lots', 'detail', id] as const,
  propose: (warehouse: number, variant: number, qty: string, partner: number | undefined, date: string) =>
    ['inventory', 'lots', 'propose', warehouse, variant, qty, partner ?? 0, date] as const,
  serials: (variant: number | undefined, warehouse: number | undefined, status: number | undefined, search: string) =>
    ['inventory', 'lots', 'serials', variant ?? 0, warehouse ?? 0, status ?? 0, search] as const,
  serial: (id: number) => ['inventory', 'lots', 'serial', id] as const,
  trace: (id: number) => ['inventory', 'lots', 'trace', id] as const,
  recalls: ['inventory', 'lots', 'recalls'] as const,
  recall: (id: number) => ['inventory', 'lots', 'recalls', 'detail', id] as const,
  changes: ['inventory', 'lots', 'changes'] as const,
  change: (id: number) => ['inventory', 'lots', 'changes', 'detail', id] as const,
};

export const useLots = (variant: number | undefined, warehouse: number | undefined, search: string, expiring?: number) => useQuery({
  queryKey: lotKeys.list(variant, warehouse, search.trim(), expiring),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/lots', {
    params: {
      query: {
        ...(variant === undefined ? {} : { ItemVariantId: variant }),
        ...(warehouse === undefined ? {} : { WarehouseId: warehouse }),
        ...(search.trim() === '' ? {} : { Search: search.trim() }),
        ...(expiring === undefined ? {} : { ExpiringWithinDays: expiring }),
      },
    },
    signal,
  })),
  placeholderData: keepPreviousData,
});

export const useLot = (id: number | undefined) => useQuery({
  queryKey: lotKeys.detail(id ?? 0),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/lots/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  enabled: id !== undefined,
});

export const useLotProposal = (warehouse: number | undefined, variant: number | undefined, qty: string, partner: number | undefined, date: string) => useQuery({
  queryKey: lotKeys.propose(warehouse ?? 0, variant ?? 0, qty, partner, date),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/lots/propose', {
    params: {
      query: {
        WarehouseId: warehouse ?? 0, ItemVariantId: variant ?? 0, QtyBase: qty, DeliveryDate: date,
        ...(partner === undefined ? {} : { CustomerPartnerId: partner }),
      },
    },
    signal,
  })),
  enabled: warehouse !== undefined && variant !== undefined && Number(qty) > 0,
});

export const useSerials = (variant: number | undefined, warehouse: number | undefined, status: number | undefined, search: string) => useQuery({
  queryKey: lotKeys.serials(variant, warehouse, status, search.trim()),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/serials', {
    params: {
      query: {
        ...(variant === undefined ? {} : { ItemVariantId: variant }),
        ...(warehouse === undefined ? {} : { WarehouseId: warehouse }),
        ...(status === undefined ? {} : { Status: status }),
        ...(search.trim() === '' ? {} : { Search: search.trim() }),
      },
    },
    signal,
  })),
  placeholderData: keepPreviousData,
});

export const useSerial = (id: number | undefined) => useQuery({
  queryKey: lotKeys.serial(id ?? 0),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/serials/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  enabled: id !== undefined,
});

export const useLotTrace = (id: number | undefined) => useQuery({
  queryKey: lotKeys.trace(id ?? 0),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/lots/{id}/trace', { params: { path: { id: id ?? 0 }, query: {} }, signal })),
  enabled: id !== undefined,
});

export const useRecalls = () => useQuery({
  queryKey: lotKeys.recalls,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/recalls', { signal })),
});

export const useRecall = (id: number | undefined) => useQuery({
  queryKey: lotKeys.recall(id ?? 0),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/recalls/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  enabled: id !== undefined,
});

export const useLotChanges = () => useQuery({
  queryKey: lotKeys.changes,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/lot-changes', { signal })),
});

export const useLotChange = (id: number | undefined) => useQuery({
  queryKey: lotKeys.change(id ?? 0),
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/lot-changes/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  enabled: id !== undefined,
});

/** Cautarea clientilor sau furnizorilor activi (primii 10), pentru alegerea partenerului pe ecranele de lot. */
export async function searchPartners(search: string): Promise<PartnerOption[]> {
  const page = unwrap(await api.GET('/api/core/partners', {
    params: { query: { ...(search.trim() === '' ? {} : { Search: search.trim() }), IsActive: true, Page: 1, PageSize: 10 } },
  }));
  return [...page.items];
}

export const lotsApi = {
  async changeStatus(id: number, toStatus: number, reason: string | null, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/lots/{id}/status', { params: { path: { id } }, body: { id, toStatus, reason, rowVersion } }));
  },
  async update(body: Schemas['UpdateLot']) {
    return unwrap(await api.PUT('/api/inventory/lots/{id}', { params: { path: { id: body.id } }, body }));
  },
  async parseGs1(raw: string) {
    return unwrap(await api.GET('/api/inventory/gs1/parse', { params: { query: { raw } } }));
  },
  async saveLotChange(body: Schemas['SaveLotChange']) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/lot-changes', { body }))
      : unwrap(await api.PUT('/api/inventory/lot-changes/{id}', { params: { path: { id: body.id } }, body }));
  },
  async validateLotChange(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/lot-changes/{id}/validate', { params: { path: { id } }, body: { rowVersion } }));
  },
  async cancelLotChange(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/lot-changes/{id}/cancel', { params: { path: { id } }, body: { rowVersion } }));
  },
  async saveRecall(body: Schemas['SaveRecall']) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/recalls', { body }))
      : unwrap(await api.PUT('/api/inventory/recalls/{id}', { params: { path: { id: body.id } }, body }));
  },
  async validateRecall(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/recalls/{id}/validate', { params: { path: { id } }, body: { rowVersion } }));
  },
  async closeRecall(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/recalls/{id}/close', { params: { path: { id } }, body: { rowVersion } }));
  },
  async setOutcome(customerLineId: number, outcome: number, note: string | null) {
    return unwrap(await api.PUT('/api/inventory/recall-customers/{id}/outcome', { params: { path: { id: customerLineId } }, body: { customerLineId, outcome, note } }));
  },
};
