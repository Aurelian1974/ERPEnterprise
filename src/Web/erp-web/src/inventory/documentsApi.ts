import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

type Schemas = components['schemas'];
export type StockDocument = Schemas['StockDocumentDto'];
export type StockDocumentItem = Schemas['StockDocumentItemDto'];
export type StockDocumentLine = Schemas['StockDocumentLineDto'];
export type SaveStockDocument = Schemas['SaveStockDocument'];
export type MovementReason = Schemas['MovementReasonDto'];
export type TransitShipment = Schemas['TransitShipmentDto'];
export type StockMovement = Schemas['StockMovementDto'];
export type VariantOption = Schemas['StockVariantOptionDto'];

/** Un tip de document de stoc (02.05, S2 3.3): de unde iese si unde intra marfa, ce cere antetul si linia. */
export interface DocumentTypeInfo {
  readonly code: string;
  readonly label: TextKey;
  readonly from: boolean;
  readonly to: boolean;
  readonly reason?: 1 | -1;
  readonly costCenter?: boolean;
  readonly unitCost?: 'required' | 'optional';
  readonly custody?: boolean;
}

export const documentTypes: Readonly<Record<string, DocumentTypeInfo>> = {
  NP: { code: 'NP', label: 'inv.doc.NP', from: true, to: true },
  NR: { code: 'NR', label: 'inv.doc.NR', from: true, to: true },
  NI: { code: 'NI', label: 'inv.doc.NI', from: false, to: true, reason: 1, unitCost: 'optional' },
  ND: { code: 'ND', label: 'inv.doc.ND', from: true, to: false, reason: -1 },
  BC: { code: 'BC', label: 'inv.doc.BC', from: true, to: false, costCenter: true },
  PF: { code: 'PF', label: 'inv.doc.PF', from: false, to: true, unitCost: 'required' },
  CI: { code: 'CI', label: 'inv.doc.CI', from: false, to: true, unitCost: 'required', custody: true },
  CE: { code: 'CE', label: 'inv.doc.CE', from: true, to: false, custody: true },
};

export const DocumentStatus = { Draft: 1, Validated: 2, Reversed: 3, Cancelled: 4 } as const;
export const ApprovalStatus = { None: 0, Pending: 1, Approved: 2, Rejected: 3 } as const;

export const statusLabel = (status: number): TextKey =>
  (({ 1: 'inv.status.draft', 2: 'inv.status.validated', 3: 'inv.status.reversed', 4: 'inv.status.cancelled' }) as Record<number, TextKey>)[status]
  ?? 'inv.status.draft';

export const approvalLabel = (status: number): TextKey | undefined =>
  (({ 1: 'inv.approval.pending', 2: 'inv.approval.approved', 3: 'inv.approval.rejected' }) as Record<number, TextKey>)[status];

export const shipmentStatusLabel = (status: number): TextKey =>
  (({ 1: 'inv.shipment.sent', 2: 'inv.shipment.partial', 3: 'inv.shipment.received', 4: 'inv.shipment.differences', 5: 'inv.shipment.closed' }) as Record<number, TextKey>)[status]
  ?? 'inv.shipment.sent';

/** Rezolvarile unei diferente la primire (S2 3.4): lipsa (1–4) sau plus (5). */
export const resolutions: readonly { readonly value: number; readonly label: TextKey; readonly forShortage: boolean }[] = [
  { value: 1, label: 'inv.resolution.stay', forShortage: true },
  { value: 2, label: 'inv.resolution.return', forShortage: true },
  { value: 3, label: 'inv.resolution.charged', forShortage: true },
  { value: 4, label: 'inv.resolution.transit', forShortage: true },
  { value: 5, label: 'inv.resolution.surplus', forShortage: false },
];

/** Diferenta unei linii primite (text invariant): pozitiva = lipsa, negativa = plus, zero = fara diferenta. Fara calcule cu number pe bani. */
export function receiptDifference(sent: string, received: string): 'none' | 'shortage' | 'surplus' | 'invalid' {
  const pattern = /^\d{1,13}([.,]\d{1,6})?$/;
  if (!pattern.test(received.trim())) {
    return 'invalid';
  }
  const scale = (value: string) => {
    const [whole, fraction = ''] = value.trim().replace(',', '.').split('.');
    return BigInt(`${whole}${fraction.padEnd(6, '0')}`);
  };
  const delta = scale(sent) - scale(received);
  return delta === 0n ? 'none' : delta > 0n ? 'shortage' : 'surplus';
}

/** Liniile din editor -> corpul cererii (cantitati si costuri ca text invariant). */
export interface EditorLine {
  readonly itemVariantId: number;
  readonly label: string;
  readonly qty: string;
  readonly unitCost: string;
}

export function toLinesBody(lines: readonly EditorLine[]): SaveStockDocument['lines'] {
  return lines.map((l) => ({
    itemVariantId: l.itemVariantId, uomId: null, qty: l.qty.trim().replace(',', '.'), uomFactor: '1',
    unitCost: l.unitCost.trim() === '' ? null : l.unitCost.trim().replace(',', '.'), fromDepotId: null, fromWarehouseId: null, toDepotId: null,
    toWarehouseId: null, costCenterId: null, projectId: null,
  }));
}

export const documentKeys = {
  all: ['inventory', 'documents'] as const,
  list: (types: string, status: number | undefined) => ['inventory', 'documents', 'list', types, status ?? 0] as const,
  detail: (id: number) => ['inventory', 'documents', 'detail', id] as const,
};

export function useStockDocuments(types: readonly string[], status: number | undefined) {
  return useQuery({
    queryKey: documentKeys.list(types.join(','), status),
    queryFn: async ({ signal }) => {
      const pages = await Promise.all(types.map(async (type) => unwrap(await api.GET('/api/inventory/documents', {
        params: { query: { DocumentType: type, ...(status === undefined ? {} : { Status: status }) } }, signal,
      }))));
      return pages.flat().sort((a, b) => (a.documentDate === b.documentDate ? b.id - a.id : a.documentDate < b.documentDate ? 1 : -1));
    },
    placeholderData: keepPreviousData,
  });
}

export function useStockDocument(id: number | undefined) {
  return useQuery({
    queryKey: documentKeys.detail(id ?? 0),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/documents/{id}', { params: { path: { id: id ?? 0 } }, signal })),
    enabled: id !== undefined,
  });
}

export function useMovementReasons() {
  return useQuery({
    queryKey: ['inventory', 'movement-reasons'] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/movement-reasons', { signal })),
    staleTime: 5 * 60_000,
  });
}

export function useTransitShipments(openOnly: boolean) {
  return useQuery({
    queryKey: ['inventory', 'transit-shipments', openOnly] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/transit-shipments', { params: { query: { OpenOnly: openOnly } }, signal })),
  });
}

export function useStockMovements(warehouseId: number | undefined, itemVariantId: number | undefined, afterSeq: number | undefined) {
  return useQuery({
    queryKey: ['inventory', 'movements', warehouseId ?? 0, itemVariantId ?? 0, afterSeq ?? 0] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/inventory/movements', {
      params: {
        query: {
          ...(warehouseId === undefined ? {} : { WarehouseId: warehouseId }), ...(itemVariantId === undefined ? {} : { ItemVariantId: itemVariantId }),
          ...(afterSeq === undefined ? {} : { AfterSeq: afterSeq }), Top: 200,
        },
      },
      signal,
    })),
    placeholderData: keepPreviousData,
  });
}

export async function searchVariants(search: string, warehouseId: number | undefined): Promise<VariantOption[]> {
  return unwrap(await api.GET('/api/inventory/variants', {
    params: { query: { ...(search.trim() === '' ? {} : { Search: search.trim() }), ...(warehouseId === undefined ? {} : { WarehouseId: warehouseId }) } },
  }));
}

export const documentsApi = {
  async save(body: SaveStockDocument) {
    return body.id == null
      ? unwrap(await api.POST('/api/inventory/documents', { body }))
      : unwrap(await api.PUT('/api/inventory/documents/{id}', { params: { path: { id: body.id } }, body }));
  },
  async validate(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/documents/{id}/validate', { params: { path: { id } }, body: { rowVersion } }));
  },
  async reverse(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/documents/{id}/reverse', { params: { path: { id } }, body: { id, documentDate: null, rowVersion } }));
  },
  async cancel(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/documents/{id}/cancel', { params: { path: { id } }, body: { rowVersion } }));
  },
  async approve(id: number, approve: boolean, note: string | null, rowVersion: string) {
    return unwrap(await api.POST('/api/inventory/documents/{id}/approve', { params: { path: { id } }, body: { id, approve, note, rowVersion } }));
  },
  async createReceipt(shipmentId: number, documentDate: string) {
    return unwrap(await api.POST('/api/inventory/transit-shipments/{id}/receipt', { params: { path: { id: shipmentId } }, body: { shipmentId, documentDate } }));
  },
  async saveReceipt(id: number, lines: { lineId: number; receivedQtyBase: string; resolution: number | null }[], rowVersion: string) {
    return unwrap(await api.PUT('/api/inventory/documents/{id}/receipt', { params: { path: { id } }, body: { id, notes: null, lines, rowVersion } }));
  },
};
