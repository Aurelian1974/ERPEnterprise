import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';

export type ExchangeRates = components['schemas']['ExchangeRatesDto'];
export type Currency = components['schemas']['CurrencyDto'];
export type BnrRate = components['schemas']['BnrRateDto'];
export type ManualRate = components['schemas']['ManualRateDto'];
type SaveManual = components['schemas']['SaveManualExchangeRate'];

export const rateKeys = {
  all: ['core', 'exchange-rates'] as const,
  list: (from: string, to: string) => ['core', 'exchange-rates', from, to] as const,
};

/** Configurare › Cursuri valutare: valutele, cursurile BNR si manuale din interval (zile „aaaa-ll-zz”). */
export function useExchangeRates(from: string, to: string) {
  return useQuery({
    queryKey: rateKeys.list(from, to),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/exchange-rates', { params: { query: { From: from, To: to } }, signal })),
  });
}

/** Valutele din nomenclator (RON intai, apoi cele active), pentru listele de alegere. */
export function useCurrencies() {
  return useQuery({
    queryKey: [...rateKeys.all, 'currencies'] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/currencies', { signal })),
    staleTime: 10 * 60_000,
  });
}

/** Monedele de ales pe un cont bancar: cele active, plus cea curenta a contului (chiar daca s-a dezactivat intre timp). */
export function currencyOptions(currencies: readonly Currency[] | undefined, current: string): { value: string; text: string }[] {
  const list = (currencies ?? []).filter((c) => c.isActive || c.code === current).map((c) => ({ value: c.code, text: `${c.code} · ${c.name}` }));
  return list.some((o) => o.value === current) ? list : [{ value: current, text: current }, ...list];
}

export const ratesApi = {
  async saveManual(body: SaveManual) {
    return body.id === null
      ? unwrap(await api.POST('/api/core/exchange-rates/manual', { body }))
      : unwrap(await api.PUT('/api/core/exchange-rates/manual/{id}', { params: { path: { id: body.id } }, body }));
  },
  async importBnr() {
    return unwrap(await api.POST('/api/core/exchange-rates/import-bnr'));
  },
  async setActive(code: string, isActive: boolean, rowVersion: string) {
    return unwrap(await api.PUT('/api/core/currencies/{code}/active', { params: { path: { code } }, body: { isActive, rowVersion } }));
  },
};
