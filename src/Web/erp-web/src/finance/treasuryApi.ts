import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

export type TreasuryPage = components['schemas']['TreasuryAccountPageDto'];
export type TreasuryItem = components['schemas']['TreasuryAccountItemDto'];
export type TreasuryDetail = components['schemas']['TreasuryAccountDetailDto'];
export type TreasuryLookups = components['schemas']['TreasuryLookupsDto'];
export type SaveTreasuryAccount = components['schemas']['SaveTreasuryAccount'];

/** Tipul contului de trezorerie (02.02, F1 3.1); aceleasi valori ca TreasuryAccountKind pe server. */
export const TreasuryKind = { Bank: 1, Cash: 2, CompanyCard: 3, Transit: 4, Deposit: 5 } as const;
export type TreasuryKindValue = (typeof TreasuryKind)[keyof typeof TreasuryKind];

/** Inchiderea zilei de casa: la cerere sau automat la miezul noptii (F1 3.1). */
export const CloseMode = { OnDemand: 1, Automatic: 2 } as const;

export const treasuryKinds: readonly TreasuryKindValue[] = [TreasuryKind.Bank, TreasuryKind.Cash, TreasuryKind.CompanyCard, TreasuryKind.Transit,
  TreasuryKind.Deposit];

const kindLabels: Readonly<Record<TreasuryKindValue, TextKey>> = {
  1: 'treasury.kind.bank',
  2: 'treasury.kind.cash',
  3: 'treasury.kind.card',
  4: 'treasury.kind.transit',
  5: 'treasury.kind.deposit',
};

const groupLabels: Readonly<Record<TreasuryKindValue, TextKey>> = {
  1: 'treasury.group.bank',
  2: 'treasury.group.cash',
  3: 'treasury.group.card',
  4: 'treasury.group.transit',
  5: 'treasury.group.deposit',
};

export const kindLabel = (kind: number): TextKey => kindLabels[kind as TreasuryKindValue];
export const groupLabel = (kind: number): TextKey => groupLabels[kind as TreasuryKindValue];

export const requiresBankAccount = (kind: number) => kind === TreasuryKind.Bank || kind === TreasuryKind.CompanyCard;
export const isCash = (kind: number) => kind === TreasuryKind.Cash;

/** Simbolul contabil propus de server cand campul ramane gol (TreasuryAccountRules.DefaultGlAccount). */
export function proposedGlAccount(kind: number, currency: string): string | undefined {
  const ron = currency === 'RON';
  switch (kind) {
    case TreasuryKind.Bank:
      return ron ? '5121' : '5124';
    case TreasuryKind.Cash:
      return ron ? '5311' : '5314';
    case TreasuryKind.CompanyCard:
      return '5125';
    case TreasuryKind.Transit:
      return '581';
    default:
      return undefined;
  }
}

/** Filtrul listei (chip-urile din macheta f1-01): toate, un tip sau doar inactivele. */
export type TreasuryFilter = 'all' | 'inactive' | TreasuryKindValue;

export interface TreasuryGroup {
  readonly kind: number;
  readonly accounts: readonly TreasuryItem[];
}

/** Conturile filtrate, grupate pe tip in ordinea tipurilor (banci, case, carduri, tranzit, depozite). */
export function groupAccounts(accounts: readonly TreasuryItem[], filter: TreasuryFilter): TreasuryGroup[] {
  const visible = accounts.filter((a) => (filter === 'all' ? true : filter === 'inactive' ? !a.isActive : a.kind === filter));
  return treasuryKinds
    .map((kind) => ({ kind, accounts: visible.filter((a) => a.kind === kind) }))
    .filter((g) => g.accounts.length > 0);
}

export const treasuryKeys = {
  all: ['finance', 'treasury-accounts'] as const,
  list: (search: string) => ['finance', 'treasury-accounts', 'list', search] as const,
  detail: (id: number) => ['finance', 'treasury-accounts', 'detail', id] as const,
};

/** Lista ecranului Conturi de trezorerie (firma curenta), cu listele fisei. */
export function useTreasuryAccounts(search: string) {
  return useQuery({
    queryKey: treasuryKeys.list(search),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/treasury-accounts', {
      params: { query: { IncludeInactive: true, ...(search.trim() === '' ? {} : { Search: search.trim() }) } },
      signal,
    })),
    placeholderData: keepPreviousData,
  });
}

export function useTreasuryAccount(id: number) {
  return useQuery({
    queryKey: treasuryKeys.detail(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/treasury-accounts/{id}', { params: { path: { id } }, signal })),
  });
}

export const treasuryApi = {
  async save(body: SaveTreasuryAccount) {
    return body.id === null
      ? unwrap(await api.POST('/api/finance/treasury-accounts', { body }))
      : unwrap(await api.PUT('/api/finance/treasury-accounts/{id}', { params: { path: { id: body.id } }, body }));
  },
};
