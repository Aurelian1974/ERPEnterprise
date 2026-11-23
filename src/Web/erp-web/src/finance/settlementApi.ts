import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

export type PartnerBalanceList = components['schemas']['PartnerBalanceListDto'];
export type PartnerBalanceRow = components['schemas']['PartnerBalanceRowDto'];
export type PartnerBalance = components['schemas']['PartnerBalanceDto'];
export type OpenItem = components['schemas']['OpenItemListDto'];
export type PartnerAllocation = components['schemas']['PartnerAllocationDto'];
export type FinanceProfile = components['schemas']['PartnerFinanceProfileDto'];

/** Rolul partenerului in decontari (F3 3.1). */
export const Role = { Customer: 1, Supplier: 2 } as const;

/** Tipul partidei (F3 3.1); aceleasi valori ca OpenItemKind pe server. */
export const Kind = { Invoice: 1, CreditNote: 2, AdvanceInvoice: 3, DebitNote: 4, Payment: 5, OpeningBalance: 6, Compensation: 7, CommercialPaper: 8,
  WriteOff: 9, EmployeePayment: 10 } as const;

/** Filtrul listei de solduri (macheta f3-01). */
export type BalanceFilter = 'customers' | 'suppliers' | 'overdue' | 'unallocated' | 'both';

export interface BalanceQuery {
  readonly asOf: string;
  readonly filter: BalanceFilter;
  readonly search: string;
}

export const settlementKeys = {
  all: ['finance', 'settlement'] as const,
  balances: (q: BalanceQuery) => ['finance', 'settlement', 'balances', q] as const,
  partner: (id: number) => ['finance', 'settlement', 'partner', id] as const,
};

/** Parametrii API ai filtrului: rolul si comutatoarele. */
export function balanceParams(q: BalanceQuery) {
  return {
    asOf: q.asOf,
    ...(q.search.trim() === '' ? {} : { search: q.search.trim() }),
    ...(q.filter === 'customers' || q.filter === 'overdue' ? { role: Role.Customer } : {}),
    ...(q.filter === 'suppliers' ? { role: Role.Supplier } : {}),
    ...(q.filter === 'overdue' ? { onlyOverdue: true } : {}),
    ...(q.filter === 'unallocated' ? { onlyUnallocated: true } : {}),
    ...(q.filter === 'both' ? { bothRoles: true } : {}),
  };
}

export function usePartnerBalances(q: BalanceQuery) {
  return useQuery({
    queryKey: settlementKeys.balances(q),
    queryFn: async () => unwrap(await api.GET('/api/finance/partner-balances', { params: { query: balanceParams(q) } })),
    placeholderData: keepPreviousData,
  });
}

export function usePartnerBalance(partnerId: number | undefined) {
  return useQuery({
    queryKey: [...settlementKeys.partner(partnerId ?? 0), 'balance'],
    queryFn: async () => unwrap(await api.GET('/api/finance/partners/{partnerId}/balance', { params: { path: { partnerId: partnerId ?? 0 } } })),
    enabled: partnerId !== undefined,
  });
}

export function useOpenItems(partnerId: number | undefined, onlyOpen = true, role?: number) {
  return useQuery({
    queryKey: [...settlementKeys.partner(partnerId ?? 0), 'items', onlyOpen, role ?? 0],
    queryFn: async () => unwrap(await api.GET('/api/finance/partners/{partnerId}/open-items', {
      params: { path: { partnerId: partnerId ?? 0 }, query: { onlyOpen, ...(role === undefined ? {} : { role }) } },
    })),
    enabled: partnerId !== undefined,
  });
}

export function usePartnerAllocations(partnerId: number | undefined, role?: number) {
  return useQuery({
    queryKey: [...settlementKeys.partner(partnerId ?? 0), 'allocations', role ?? 0],
    queryFn: async () => unwrap(await api.GET('/api/finance/partners/{partnerId}/allocations', {
      params: { path: { partnerId: partnerId ?? 0 }, query: role === undefined ? {} : { role } },
    })),
    enabled: partnerId !== undefined,
  });
}

export function useFinanceProfile(partnerId: number, role: number) {
  return useQuery({
    queryKey: [...settlementKeys.partner(partnerId), 'profile', role],
    queryFn: async () => unwrap(await api.GET('/api/finance/partners/{partnerId}/finance-profile/{role}', { params: { path: { partnerId, role } } })),
  });
}

/** Intarzierea unei partide la o data, in zile (0 = in termen sau scadenta azi). */
export function overdueDays(dueDate: string, today: string): number {
  const days = Math.round((Date.parse(today) - Date.parse(dueDate)) / 86_400_000);
  return days > 0 ? days : 0;
}

const kindLabels: Readonly<Record<number, TextKey>> = {
  1: 'settle.kind.invoice', 2: 'settle.kind.creditNote', 3: 'settle.kind.advanceInvoice', 4: 'settle.kind.debitNote', 5: 'settle.kind.payment',
  6: 'settle.kind.opening', 7: 'settle.kind.compensation', 8: 'settle.kind.paper', 9: 'settle.kind.writeOff', 10: 'settle.kind.employeePayment',
};
export const kindLabel = (kind: number): TextKey => kindLabels[kind] ?? 'settle.kind.invoice';

const sourceLabels: Readonly<Record<number, TextKey>> = {
  1: 'settle.source.auto', 2: 'settle.source.manual', 3: 'settle.source.compensation', 4: 'settle.source.reversal', 5: 'settle.source.paper',
  6: 'settle.source.writeOff', 7: 'settle.source.import',
};
export const allocationSourceLabel = (source: number): TextKey => sourceLabels[source] ?? 'settle.source.manual';

const eInvoiceLabels: Readonly<Record<number, TextKey | undefined>> = {
  1: 'settle.einv.sent', 2: 'settle.einv.accepted', 3: 'settle.einv.rejected', 4: 'settle.einv.received', 5: 'settle.einv.missing',
};
export const eInvoiceLabel = (status: number): TextKey | undefined => eInvoiceLabels[status];
const eInvoicePills: Readonly<Record<number, string>> = { 1: 'pill info', 2: 'pill ok', 3: 'pill bad', 4: 'pill ok', 5: 'pill warn' };
export const eInvoicePill = (status: number): string => eInvoicePills[status] ?? 'pill';

/** Partile vechimii pentru bara din lista de solduri: procente din soldul de sensul rolului (in termen, 1–30, 31–60, peste 60). */
export function agingShares(row: Pick<PartnerBalanceRow, 'currentRon' | 'days30Ron' | 'days60Ron' | 'over60Ron'>): number[] {
  const values = [row.currentRon, row.days30Ron, row.days60Ron, row.over60Ron].map((v) => Math.abs(Number(v)));
  const total = values.reduce((a, b) => a + b, 0);
  return total === 0 ? [0, 0, 0, 0] : values.map((v) => Math.round((v * 1000) / total) / 10);
}
