import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

export type PaymentOrderItem = components['schemas']['PaymentOrderItemDto'];
export type PaymentOrderDetail = components['schemas']['PaymentOrderDetailDto'];
export type SavePaymentOrder = components['schemas']['SavePaymentOrder'];
export type PaymentBatchItem = components['schemas']['PaymentBatchItemDto'];

/** Starea documentului OP (F2 3.6): 1 ciorna, 2 validat, 3 stornat. */
export const OrderStatus = { Draft: 1, Validated: 2, Reversed: 3 } as const;

/** Starea confirmarii pe extras: 1 neconfirmat, 2 confirmat, 3 respins. */
export const Confirmation = { Unconfirmed: 1, Confirmed: 2, Rejected: 3 } as const;

/** Originea: 2 = OP nou din extras (plata deja executata, fara aprobare). */
export const Origin = { PaymentOrders: 1, BankStatement: 2 } as const;

export const BatchStatus = { Open: 1, Exported: 2, Cancelled: 3 } as const;

export const purposes = [1, 2, 3, 4] as const;
const purposeLabels: Readonly<Record<number, TextKey>> = { 1: 'order.purpose.suppliers', 2: 'order.purpose.salaries', 3: 'order.purpose.budget', 4: 'order.purpose.other' };
export const purposeLabel = (purpose: number): TextKey => purposeLabels[purpose] ?? 'order.purpose.other';

/** Filtrul listei de OP-uri (macheta f2-03). */
export type OrderFilter = 'all' | 'drafts' | 'toApprove' | 'unconfirmed' | 'rejected';

/** Un OP asteapta aprobarea cand e validat, nestornat, neconfirmat si nu vine din extras (acolo plata e deja facuta). */
export const awaitsApproval = (o: Pick<PaymentOrderItem, 'status' | 'isApproved' | 'origin' | 'confirmationStatus' | 'reversalOfId'>) =>
  o.status === OrderStatus.Validated && !o.isApproved && o.origin !== Origin.BankStatement && o.confirmationStatus === Confirmation.Unconfirmed
  && o.reversalOfId == null;

export function filterOrders(orders: readonly PaymentOrderItem[], filter: OrderFilter): PaymentOrderItem[] {
  return orders.filter((o) => {
    switch (filter) {
      case 'drafts':
        return o.status === OrderStatus.Draft;
      case 'toApprove':
        return awaitsApproval(o);
      case 'unconfirmed':
        return o.status === OrderStatus.Validated && o.confirmationStatus === Confirmation.Unconfirmed;
      case 'rejected':
        return o.confirmationStatus === Confirmation.Rejected;
      default:
        return true;
    }
  });
}

export function documentLabel(o: Pick<PaymentOrderItem, 'status' | 'reversalOfId'>): { readonly label: TextKey; readonly pill: string } {
  if (o.status === OrderStatus.Draft) return { label: 'order.status.draft', pill: 'pill' };
  if (o.status === OrderStatus.Reversed) return { label: 'order.status.reversed', pill: 'pill bad' };
  return o.reversalOfId == null ? { label: 'order.status.validated', pill: 'pill ok' } : { label: 'order.status.reversal', pill: 'pill warn' };
}

export function confirmationLabel(status: number | null | undefined): { readonly label: TextKey; readonly pill: string } | undefined {
  switch (status) {
    case Confirmation.Unconfirmed:
      return { label: 'order.confirmation.unconfirmed', pill: 'pill' };
    case Confirmation.Confirmed:
      return { label: 'order.confirmation.confirmed', pill: 'pill ok' };
    case Confirmation.Rejected:
      return { label: 'order.confirmation.rejected', pill: 'pill bad' };
    default:
      return undefined;
  }
}

export const orderKeys = {
  all: ['finance', 'orders'] as const,
  list: (accountId: number | undefined) => ['finance', 'orders', 'list', accountId ?? 0] as const,
  detail: (id: number) => ['finance', 'orders', 'detail', id] as const,
  batches: (accountId: number | undefined) => ['finance', 'orders', 'batches', accountId ?? 0] as const,
};

export function usePaymentOrders(accountId: number | undefined) {
  return useQuery({
    queryKey: orderKeys.list(accountId),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/payment-orders', {
      params: { query: accountId === undefined ? {} : { TreasuryAccountId: accountId } },
      signal,
    })),
    placeholderData: keepPreviousData,
  });
}

export function usePaymentOrder(id: number | undefined) {
  return useQuery({
    queryKey: orderKeys.detail(id ?? 0),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/payment-orders/{id}', { params: { path: { id: id ?? 0 } }, signal })),
    enabled: id !== undefined,
  });
}

export function usePaymentBatches(accountId: number | undefined) {
  return useQuery({
    queryKey: orderKeys.batches(accountId),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/payment-batches', {
      params: { query: accountId === undefined ? {} : { TreasuryAccountId: accountId } },
      signal,
    })),
  });
}

/** Descarca fisierul exportat al lotului (continutul vine Base64 din API). */
export function downloadFile(file: { readonly fileName: string; readonly contentType: string; readonly contentBase64: string }) {
  const bytes = Uint8Array.from(atob(file.contentBase64), (c) => c.charCodeAt(0));
  const url = URL.createObjectURL(new Blob([bytes], { type: file.contentType }));
  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  link.click();
  URL.revokeObjectURL(url);
}

export const orderApi = {
  async save(body: SavePaymentOrder) {
    return body.id === null
      ? unwrap(await api.POST('/api/finance/payment-orders', { body }))
      : unwrap(await api.PUT('/api/finance/payment-orders/{id}', { params: { path: { id: body.id } }, body }));
  },
  async remove(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/finance/payment-orders/{id}/delete', { params: { path: { id } }, body: { rowVersion } }));
  },
  async validate(id: number, rowVersion: string, confirmAnafInactive: boolean) {
    return unwrap(await api.POST('/api/finance/payment-orders/{id}/validate', { params: { path: { id } }, body: { rowVersion, confirmAnafInactive } }));
  },
  async approve(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/finance/payment-orders/approve', { body: { orders: [{ id, rowVersion }] } }));
  },
  async reverse(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/finance/payment-orders/{id}/reverse', { params: { path: { id } }, body: { rowVersion } }));
  },
  /** Pune OP-ul in lotul deschis al contului (sau intr-unul nou) ori il scoate din lot. */
  async setBatch(batch: { id: number; rowVersion: string } | undefined, accountId: number, orderIds: number[]) {
    return batch === undefined
      ? unwrap(await api.POST('/api/finance/payment-batches', { body: { id: null, treasuryAccountId: accountId, orderIds, rowVersion: null } }))
      : unwrap(await api.PUT('/api/finance/payment-batches/{id}', { params: { path: { id: batch.id } },
        body: { id: batch.id, treasuryAccountId: accountId, orderIds, rowVersion: batch.rowVersion } }));
  },
  async batchOrders(id: number) {
    return unwrap(await api.GET('/api/finance/payment-batches/{id}', { params: { path: { id } } })).orders.map((o) => o.id);
  },
  async exportBatch(id: number, rowVersion: string, reExportReason: string | null) {
    return unwrap(await api.POST('/api/finance/payment-batches/{id}/export', { params: { path: { id } }, body: { rowVersion, reExportReason } }));
  },
};
