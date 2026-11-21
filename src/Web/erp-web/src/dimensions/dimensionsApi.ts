import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';

export type DimensionPage = components['schemas']['DimensionPageDto'];
export type DimensionValue = components['schemas']['DimensionValueItemDto'];
type SaveDimension = components['schemas']['SaveDimensionValue'];

export interface DimensionFilter {
  readonly type: string;
  readonly companyId: number | undefined;
  readonly search: string;
  readonly includeInactive: boolean;
}

export const dimensionKeys = {
  all: ['core', 'dimensions'] as const,
  list: (filter: DimensionFilter) => ['core', 'dimensions', filter] as const,
};

/** Configurare › Dimensiuni: tipurile, valorile tipului ales, firmele si responsabilii posibili. */
export function useDimensions(filter: DimensionFilter) {
  return useQuery({
    queryKey: dimensionKeys.list(filter),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/dimensions', {
      params: {
        query: {
          Type: filter.type, IncludeInactive: filter.includeInactive,
          ...(filter.companyId === undefined ? {} : { CompanyId: filter.companyId }),
          ...(filter.search.trim() === '' ? {} : { Search: filter.search.trim() }),
        },
      },
      signal,
    })),
    placeholderData: keepPreviousData,
  });
}

export const dimensionsApi = {
  async save(body: SaveDimension) {
    return body.id === null
      ? unwrap(await api.POST('/api/core/dimensions', { body }))
      : unwrap(await api.PUT('/api/core/dimensions/{id}', { params: { path: { id: body.id } }, body }));
  },
};

/** Parintii posibili: valori active de acelasi tip si din aceeasi firma, fara valoarea insasi si descendentii ei. */
export function parentOptions(values: readonly DimensionValue[], companyId: number, self: number | undefined): DimensionValue[] {
  const descendants = new Set<number>();
  if (self !== undefined) {
    let frontier = [self];
    while (frontier.length > 0) {
      const next = values.filter((v) => v.parentId != null && frontier.includes(v.parentId) && !descendants.has(v.id)).map((v) => v.id);
      next.forEach((id) => descendants.add(id));
      frontier = next;
    }
  }
  return values.filter((v) => v.companyId === companyId && v.isActive && v.id !== self && !descendants.has(v.id));
}
