import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';

export type ItemReference = components['schemas']['ItemReferenceItemDto'];
export type ItemTranslation = components['schemas']['ItemTranslationDto'];
type SaveReference = components['schemas']['SaveItemReference'];

/** Limbile pentru traduceri si pentru limba documentelor partenerului (ISO 639-1); romana e implicita. */
export const LANGUAGES = ['en', 'de', 'fr', 'it', 'es', 'hu', 'bg', 'pl', 'nl', 'uk', 'ru'] as const;

export const catalogKeys = {
  references: (itemId: number) => ['core', 'items', itemId, 'references'] as const,
  translations: (itemId: number) => ['core', 'items', itemId, 'translations'] as const,
};

export function useItemReferences(itemId: number, enabled: boolean) {
  return useQuery({
    queryKey: catalogKeys.references(itemId),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/items/{id}/references', { params: { path: { id: itemId } }, signal })),
    enabled,
  });
}

export function useItemTranslations(itemId: number, enabled: boolean) {
  return useQuery({
    queryKey: catalogKeys.translations(itemId),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/items/{id}/translations', { params: { path: { id: itemId } }, signal })),
    enabled,
  });
}

/** Partenerii activi cu rolul cerut (1 client, 2 furnizor), pentru lista din formularul codului. */
export async function searchPartnersByRole(text: string, role: 1 | 2) {
  const page = unwrap(await api.GET('/api/core/partners', {
    params: { query: { ...(text.trim() === '' ? {} : { Search: text.trim() }), Role: role, IsActive: true, PageSize: 20 } },
  }));
  return page.items;
}

export const catalogApi = {
  async saveReference(itemId: number, body: SaveReference) {
    return body.id === null
      ? unwrap(await api.POST('/api/core/items/{id}/references', { params: { path: { id: itemId } }, body }))
      : unwrap(await api.PUT('/api/core/items/{id}/references/{referenceId}', { params: { path: { id: itemId, referenceId: body.id ?? 0 } }, body }));
  },
  async saveTranslations(itemId: number, translations: ItemTranslation[]) {
    return unwrap(await api.PUT('/api/core/items/{id}/translations', { params: { path: { id: itemId } }, body: { itemId, translations } }));
  },
};

/** Traducerile de trimis: fara randurile goale, limba cu litere mici; o limba repetata intoarce eroarea. */
export function cleanTranslations(rows: readonly ItemTranslation[]): ItemTranslation[] | 'duplicate' {
  const list = rows.map((r) => ({ languageCode: r.languageCode.trim().toLowerCase(), name: r.name.trim() })).filter((r) => r.name !== '');
  return new Set(list.map((r) => r.languageCode)).size === list.length ? list : 'duplicate';
}
