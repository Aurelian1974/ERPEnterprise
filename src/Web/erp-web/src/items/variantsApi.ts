import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';

export type AttributePage = components['schemas']['ItemAttributePageDto'];
export type Attribute = components['schemas']['ItemAttributeDto'];
export type AttributeValue = components['schemas']['ItemAttributeValueDto'];
export type Variant = components['schemas']['ItemVariantItemDto'];

export const variantKeys = {
  attributes: ['core', 'item-attributes'] as const,
  variants: (itemId: number) => ['core', 'items', itemId, 'variants'] as const,
};

export function useItemAttributes() {
  return useQuery({
    queryKey: variantKeys.attributes,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/item-attributes', { signal })),
  });
}

export function useItemVariants(itemId: number, enabled = true) {
  return useQuery({
    queryKey: variantKeys.variants(itemId),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/items/{id}/variants', { params: { path: { id: itemId } }, signal })),
    enabled,
  });
}

/** Numarul de variante pe care l-ar crea valorile alese: produsul numarului de valori pe atribut. */
export function combinationCount(values: readonly AttributeValue[], chosen: ReadonlySet<number>): number {
  const perAttribute = new Map<number, number>();
  for (const v of values) {
    if (chosen.has(v.id)) {
      perAttribute.set(v.attributeId, (perAttribute.get(v.attributeId) ?? 0) + 1);
    }
  }
  return perAttribute.size === 0 ? 0 : [...perAttribute.values()].reduce((a, b) => a * b, 1);
}
