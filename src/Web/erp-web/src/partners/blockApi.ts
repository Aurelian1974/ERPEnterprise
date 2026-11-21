import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';

export type PartnerBlock = components['schemas']['PartnerBlockDto'];
export type ItemBlock = components['schemas']['ItemBlockDto'];

/** Masca blocarii (02.01, 3.6 punctul 6): partener 1 vanzare, 2 achizitie, 4 plati; articol 1 vanzare, 2 achizitie. */
export const PARTNER_BLOCK_FLAGS = [1, 2, 4] as const;
export const ITEM_BLOCK_FLAGS = [1, 2] as const;

export const blockKeys = {
  partner: (id: number) => ['core', 'partners', id, 'block'] as const,
  item: (id: number) => ['core', 'items', id, 'block'] as const,
};

export function usePartnerBlock(id: number, enabled: boolean) {
  return useQuery({
    queryKey: blockKeys.partner(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/partners/{id}/block', { params: { path: { id } }, signal })),
    enabled,
  });
}

export function useItemBlock(id: number, enabled: boolean) {
  return useQuery({
    queryKey: blockKeys.item(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/items/{id}/block', { params: { path: { id } }, signal })),
    enabled,
  });
}

export const blockApi = {
  async setPartner(id: number, blockedFor: number, reason: string | null, rowVersion: string | null) {
    return unwrap(await api.PUT('/api/core/partners/{id}/block', {
      params: { path: { id } }, body: { partnerId: id, blockedFor, reason, rowVersion },
    }));
  },
  async setItem(id: number, blockedFor: number, reason: string | null, rowVersion: string | null) {
    return unwrap(await api.PUT('/api/core/items/{id}/block', { params: { path: { id } }, body: { itemId: id, blockedFor, reason, rowVersion } }));
  },
  async setCredit(id: number, creditLimit: string | null, overLimitAction: number, rowVersion: string | null) {
    return unwrap(await api.PUT('/api/core/partners/{id}/credit-limit', {
      params: { path: { id } }, body: { partnerId: id, creditLimit, overLimitAction, rowVersion },
    }));
  },
};

/** Masca din bifele alese. */
export function maskOf(flags: readonly number[]): number {
  return flags.reduce((mask, flag) => mask | flag, 0);
}

/** Textul blocarii din lista („vânzare, plăți”); 7 la partener = „tot”. */
export function blockedText(mask: number, all: number, label: (flag: number) => string, allText: string): string {
  if (mask === 0) {
    return '';
  }
  return mask === all ? allText : [1, 2, 4].filter((f) => (mask & f) !== 0).map(label).join(', ');
}

/** Limita din campul text („10.000,50” sau „10000.5”) ca sir zecimal invariant cu 2 zecimale; gol = fara limita; invalid = undefined. */
export function parseLimit(text: string): string | null | undefined {
  const clean = text.trim().replace(/\s/g, '');
  if (clean === '') {
    return null;
  }
  const normalized = clean.includes(',') ? clean.replace(/\./g, '').replace(',', '.') : clean;
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) {
    return undefined;
  }
  return Number(normalized).toFixed(2);
}
