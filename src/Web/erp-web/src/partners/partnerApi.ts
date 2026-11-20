import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components, paths } from '../api/schema';

type Schemas = components['schemas'];
export type PartnerPage = Schemas['PartnerPageDto'];
export type PartnerListItem = Schemas['PartnerListItemDto'];
export type PartnerDetail = Schemas['PartnerDetailDto'];
export type PartnerLookups = Schemas['PartnerLookupsDto'];
export type PartnerAddress = Schemas['PartnerAddressDto'];
export type PartnerBankAccount = Schemas['PartnerBankAccountDto'];
export type PartnerContact = Schemas['PartnerContactDto'];
export type PartnerGroup = Schemas['PartnerGroupDto'];
export type PropertyDefinition = Schemas['PropertyDefinitionDto'];
export type PropertyValue = Schemas['PartnerPropertyValue'];
export type HistoryEntry = Schemas['PartnerHistoryEntryDto'];
export type SavePartner = Schemas['SavePartner'];
export type SavePartnerAddress = Schemas['SavePartnerAddress'];
export type SavePartnerBankAccount = Schemas['SavePartnerBankAccount'];
export type SavePartnerContact = Schemas['SavePartnerContact'];
export type SavePartnerGroup = Schemas['SavePartnerGroup'];
export type Saved = Schemas['SavedEntity'];
export type AnafCompany = Schemas['AnafCompanyDto'];

/** Tipul partenerului (02.01, 3.2). */
export const PartnerType = { LegalRomania: 1, LegalEu: 2, LegalNonEu: 3, Individual: 4 } as const;

/** Tipul de date al unei proprietati configurabile (02.01, 3.2). */
export const DataType = { Text: 1, Number: 2, Date: 3, Bool: 4, List: 5, MultiList: 6, User: 7 } as const;

/** Filtrele listei; Role: 1 clienti, 2 furnizori. */
export interface PartnerCriteria {
  readonly search: string;
  readonly role?: 1 | 2 | undefined;
  readonly partnerType?: number | undefined;
  readonly anafInactive?: boolean | undefined;
  readonly showInactive: boolean;
  readonly propertyDefinitionId?: number | undefined;
  readonly propertyOptionId?: number | undefined;
  readonly propertyBool?: boolean | undefined;
  readonly page: number;
}

export const PAGE_SIZE = 50;

// Partenerii sunt la nivelul instalarii; permisiunile si proprietatile valabile tin de firma curenta (antetul X-Company-Id,
// pus de client). La schimbarea firmei, antetul aplicatiei reincarca toate interogarile.
export const partnerKeys = {
  all: ['core', 'partners'] as const,
  list: (criteria: PartnerCriteria) => ['core', 'partners', 'list', criteria] as const,
  detail: (id: number) => ['core', 'partners', 'detail', id] as const,
  history: (id: number) => ['core', 'partners', 'history', id] as const,
  lookups: ['core', 'partners', 'lookups'] as const,
};

type SearchQuery = NonNullable<NonNullable<paths['/api/core/partners']['get']['parameters']['query']>>;

/** Parametrii cererii de cautare, fara cei necompletati (API-ul ii trateaza ca lipsa filtrului). */
export function searchQuery(criteria: PartnerCriteria): SearchQuery {
  const search = criteria.search.trim();
  const entries: [keyof SearchQuery, string | number | boolean | undefined][] = [
    ['Search', search === '' ? undefined : search],
    ['Role', criteria.role],
    ['PartnerType', criteria.partnerType],
    ['IsActive', criteria.showInactive ? undefined : true],
    ['AnafInactive', criteria.anafInactive],
    ['PropertyDefinitionId', criteria.propertyDefinitionId],
    ['PropertyOptionId', criteria.propertyOptionId],
    ['PropertyBool', criteria.propertyBool],
    ['Page', criteria.page],
    ['PageSize', PAGE_SIZE],
  ];
  return Object.fromEntries(entries.filter(([, value]) => value !== undefined)) as SearchQuery;
}

export function usePartners(criteria: PartnerCriteria) {
  return useQuery({
    queryKey: partnerKeys.list(criteria),
    // Pagina anterioara ramane afisata cat se incarca urmatoarea (UI-12: fara golirea listei la fiecare tasta).
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/core/partners', {
          params: { query: searchQuery(criteria) },
          signal,
        }),
      ),
  });
}

export function usePartner(id: number | undefined) {
  return useQuery({
    queryKey: partnerKeys.detail(id ?? 0),
    enabled: id !== undefined,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/partners/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  });
}

export function usePartnerHistory(id: number, enabled: boolean) {
  return useQuery({
    queryKey: partnerKeys.history(id),
    enabled,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/partners/{id}/history', { params: { path: { id } }, signal })),
  });
}

export function usePartnerLookups() {
  return useQuery({
    queryKey: partnerKeys.lookups,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/partners/lookups', { signal })),
    staleTime: 10 * 60_000,
  });
}

export const partnerApi = {
  async save(body: SavePartner): Promise<Saved> {
    return body.id === null
      ? unwrap(await api.POST('/api/core/partners', { body }))
      : unwrap(await api.PUT('/api/core/partners/{id}', { params: { path: { id: body.id } }, body }));
  },
  /** „Preia de la ANAF”: datele contribuabilului din registrele ANAF, la data de azi (nu salveaza nimic). */
  async lookupAnaf(fiscalCode: string): Promise<AnafCompany> {
    return unwrap(await api.GET('/api/core/anaf/companies/{fiscalCode}', { params: { path: { fiscalCode } } }));
  },
  async revealPersonalId(id: number): Promise<string | null> {
    return unwrap(await api.POST('/api/core/partners/{id}/personal-id', { params: { path: { id } } })).personalId ?? null;
  },
  async saveAddress(body: SavePartnerAddress): Promise<Saved> {
    const partnerId = body.partnerId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/partners/{partnerId}/addresses', { params: { path: { partnerId } }, body }))
      : unwrap(await api.PUT('/api/core/partners/{partnerId}/addresses/{id}', { params: { path: { partnerId, id: body.id } }, body }));
  },
  async saveBankAccount(body: SavePartnerBankAccount): Promise<Saved> {
    const partnerId = body.partnerId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/partners/{partnerId}/bank-accounts', { params: { path: { partnerId } }, body }))
      : unwrap(await api.PUT('/api/core/partners/{partnerId}/bank-accounts/{id}', { params: { path: { partnerId, id: body.id } }, body }));
  },
  async saveContact(body: SavePartnerContact): Promise<Saved> {
    const partnerId = body.partnerId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/partners/{partnerId}/contacts', { params: { path: { partnerId } }, body }))
      : unwrap(await api.PUT('/api/core/partners/{partnerId}/contacts/{id}', { params: { path: { partnerId, id: body.id } }, body }));
  },
  async saveGroup(body: SavePartnerGroup): Promise<Saved> {
    return body.id === null
      ? unwrap(await api.POST('/api/core/partner-groups', { body }))
      : unwrap(await api.PUT('/api/core/partner-groups/{id}', { params: { path: { id: body.id } }, body }));
  },
};
