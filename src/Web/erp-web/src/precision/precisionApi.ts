import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';

export type PrecisionPage = components['schemas']['PrecisionPageDto'];
export type PrecisionPolicy = components['schemas']['PrecisionPolicyItemDto'];
type SavePolicy = components['schemas']['SavePrecisionPolicy'];

export const precisionKeys = { all: ['core', 'precision-policies'] as const };

export function usePrecisionPolicies() {
  return useQuery({
    queryKey: precisionKeys.all,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/precision-policies', { signal })),
  });
}

export const precisionApi = {
  async save(body: SavePolicy) {
    return body.id === null
      ? unwrap(await api.POST('/api/core/precision-policies', { body }))
      : unwrap(await api.PUT('/api/core/precision-policies/{id}', { params: { path: { id: body.id } }, body }));
  },
};

/** Modurile de rotunjire (System.MidpointRounding), cu textul din macheta 07d. */
export const ROUNDING_MODES = [1, 0, 2, 3, 4] as const;

/**
 * Previzualizarea din macheta 07d (doar ilustrativa; documentele rotunjesc pe server, IPrecisionService): pretul si cantitatea rotunjite
 * la zecimalele lor, apoi valoarea la 2 zecimale, „jumatate departe de zero”.
 */
export function previewLine(quantity: number, price: number, priceDecimals: number, quantityDecimals: number) {
  const round = (value: number, decimals: number) => {
    const factor = 10 ** decimals;
    return Math.sign(value) * Math.round(Math.abs(value) * factor + Number.EPSILON) / factor;
  };
  const p = round(price, priceDecimals);
  const q = round(quantity, quantityDecimals);
  return { price: p, quantity: q, amount: round(p * q, 2) };
}

export const formatNumber = (value: number, decimals: number) =>
  value.toLocaleString('ro-RO', { minimumFractionDigits: decimals, maximumFractionDigits: decimals, useGrouping: false });
