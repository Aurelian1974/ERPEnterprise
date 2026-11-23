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
export type DueItem = components['schemas']['DueItemDto'];

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

/** O partida aleasa in ecranul Alocare, cu suma de alocat (pozitiva, in moneda partidei). */
export interface AllocationPick {
  readonly id: number;
  readonly amount: number;
}

/**
 * Perechile de alocare din partidele alese (F3 4.3): debitele si creditele se potrivesc in ordinea data (doi indici), fiecare pereche cu
 * minimul dintre ce mai e de alocat pe cele doua; ce depaseste pe o parte ramane nealocat.
 */
export function pairAllocations(debits: readonly AllocationPick[], credits: readonly AllocationPick[]): { debitId: number; creditId: number; amount: number }[] {
  const pairs: { debitId: number; creditId: number; amount: number }[] = [];
  const d = debits.map((x) => ({ ...x, left: Math.round(x.amount * 100) }));
  const c = credits.map((x) => ({ ...x, left: Math.round(x.amount * 100) }));
  let i = 0;
  let j = 0;
  while (i < d.length && j < c.length) {
    const debit = d[i];
    const credit = c[j];
    if (debit === undefined || credit === undefined) break;
    const cents = Math.min(debit.left, credit.left);
    if (cents > 0) pairs.push({ debitId: debit.id, creditId: credit.id, amount: cents / 100 });
    debit.left -= cents;
    credit.left -= cents;
    if (debit.left === 0) i += 1;
    if (credit.left === 0) j += 1;
  }
  return pairs;
}

/**
 * Propunerea FIFO (F3 4.3 pasul 3): toate creditele deschise (in ordinea lor) pe cele mai vechi debite, fara partidele in litigiu; intoarce
 * sumele propuse pe fiecare parte (doar cele cu suma > 0).
 */
export function proposeFifo(items: readonly OpenItem[]): { debits: Map<number, number>; credits: Map<number, number> } {
  const open = items.filter((i) => !i.isDisputed && i.currencyCode === (items[0]?.currencyCode ?? 'RON'));
  const debits = open.filter((i) => Number(i.openAmount) > 0).map((i) => ({ id: i.openItemId, amount: Number(i.openAmount) }));
  const credits = open.filter((i) => Number(i.openAmount) < 0).map((i) => ({ id: i.openItemId, amount: -Number(i.openAmount) }));
  const result = { debits: new Map<number, number>(), credits: new Map<number, number>() };
  for (const p of pairAllocations(debits, credits)) {
    result.debits.set(p.debitId, Math.round(((result.debits.get(p.debitId) ?? 0) + p.amount) * 100) / 100);
    result.credits.set(p.creditId, Math.round(((result.credits.get(p.creditId) ?? 0) + p.amount) * 100) / 100);
  }
  return result;
}
