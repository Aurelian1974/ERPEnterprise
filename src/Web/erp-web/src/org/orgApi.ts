import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { forCompany, unwrap } from '../api/problem';
import type { components } from '../api/schema';

type Schemas = components['schemas'];
export type CompanyDetail = Schemas['CompanyDetailDto'];
export type BankAccount = Schemas['CompanyBankAccountDto'];
export type OrgLookups = Schemas['OrgLookupsDto'];
export type NumberSeries = Schemas['NumberSeriesDto'];
export type Saved = Schemas['SavedEntity'];
export type SaveGroup = Schemas['SaveCompanyGroup'];
export type SaveCompany = Schemas['SaveCompany'];
export type SaveBranch = Schemas['SaveBranch'];
export type SaveLocation = Schemas['SaveLocation'];
export type SaveBankAccount = Schemas['SaveCompanyBankAccount'];
export type CompanyAddress = Schemas['CompanyAddressDto'];
export type CompanyContact = Schemas['CompanyContactDto'];
export type SaveCompanyContact = Schemas['SaveCompanyContact'];
export type SaveCompanyAddress = Schemas['SaveCompanyAddress'];
export type AddressType = Schemas['AddressTypeDto'];
export type SaveAddressType = Schemas['SaveAddressType'];
export type SaveSeries = Schemas['SaveNumberSeries'];

export const orgKeys = {
  tree: ['core', 'org', 'tree'] as const,
  lookups: ['core', 'org', 'lookups'] as const,
  company: (id: number) => ['core', 'company', id] as const,
  series: (companyId: number) => ['core', 'number-series', companyId] as const,
};

export function useOrgTree() {
  return useQuery({ queryKey: orgKeys.tree, queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/org/tree', { signal })) });
}

export function useOrgLookups() {
  return useQuery({
    queryKey: orgKeys.lookups,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/org/lookups', { signal })),
    staleTime: 30 * 60_000,
  });
}

/** Fisa firmei; drepturile se verifica pe firma respectiva (antetul X-Company-Id). */
export function useCompany(id: number | undefined) {
  return useQuery({
    queryKey: orgKeys.company(id ?? 0),
    enabled: id !== undefined,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/core/companies/{id}', { params: { path: { id: id ?? 0 } }, signal, ...forCompany(id ?? 0) })),
  });
}

export function useNumberSeries(companyId: number | undefined) {
  return useQuery({
    queryKey: orgKeys.series(companyId ?? 0),
    enabled: companyId !== undefined,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/core/companies/{companyId}/number-series', {
          params: { path: { companyId: companyId ?? 0 }, query: {} },
          signal,
          ...forCompany(companyId ?? 0),
        }),
      ),
  });
}

// Grupurile si firmele noi se salveaza la nivelul instalarii (X-Company-Id: 0); restul pe firma lor.
export const orgApi = {
  async saveGroup(body: SaveGroup): Promise<Saved> {
    return body.id === null
      ? unwrap(await api.POST('/api/core/groups', { body, ...forCompany(0) }))
      : unwrap(await api.PUT('/api/core/groups/{id}', { params: { path: { id: body.id } }, body, ...forCompany(0) }));
  },
  async saveCompany(body: SaveCompany): Promise<Saved> {
    return body.id === null
      ? unwrap(await api.POST('/api/core/companies', { body, ...forCompany(0) }))
      : unwrap(await api.PUT('/api/core/companies/{id}', { params: { path: { id: body.id } }, body, ...forCompany(body.id) }));
  },
  async saveBranch(body: SaveBranch): Promise<Saved> {
    const companyId = body.companyId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/companies/{companyId}/branches', { params: { path: { companyId } }, body, ...forCompany(companyId) }))
      : unwrap(await api.PUT('/api/core/companies/{companyId}/branches/{id}', { params: { path: { companyId, id: body.id } }, body, ...forCompany(companyId) }));
  },
  async saveLocation(body: SaveLocation): Promise<Saved> {
    const companyId = body.companyId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/companies/{companyId}/locations', { params: { path: { companyId } }, body, ...forCompany(companyId) }))
      : unwrap(await api.PUT('/api/core/companies/{companyId}/locations/{id}', { params: { path: { companyId, id: body.id } }, body, ...forCompany(companyId) }));
  },
  async saveBankAccount(body: SaveBankAccount): Promise<Saved> {
    const companyId = body.companyId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/companies/{companyId}/bank-accounts', { params: { path: { companyId } }, body, ...forCompany(companyId) }))
      : unwrap(await api.PUT('/api/core/companies/{companyId}/bank-accounts/{id}', { params: { path: { companyId, id: body.id } }, body, ...forCompany(companyId) }));
  },
  async saveAddressType(body: SaveAddressType): Promise<Saved> {
    return body.id == null
      ? unwrap(await api.POST('/api/core/address-types', { body }))
      : unwrap(await api.PUT('/api/core/address-types/{id}', { params: { path: { id: body.id } }, body }));
  },
  async saveAddress(body: SaveCompanyAddress): Promise<Saved> {
    const companyId = body.companyId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/companies/{companyId}/addresses', { params: { path: { companyId } }, body, ...forCompany(companyId) }))
      : unwrap(await api.PUT('/api/core/companies/{companyId}/addresses/{id}', { params: { path: { companyId, id: body.id } }, body, ...forCompany(companyId) }));
  },
  async saveContact(body: SaveCompanyContact): Promise<Saved> {
    const companyId = body.companyId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/companies/{companyId}/contacts', { params: { path: { companyId } }, body, ...forCompany(companyId) }))
      : unwrap(await api.PUT('/api/core/companies/{companyId}/contacts/{id}', { params: { path: { companyId, id: body.id } }, body, ...forCompany(companyId) }));
  },
  async saveSeries(body: SaveSeries): Promise<Saved> {
    const companyId = body.companyId;
    return body.id === null
      ? unwrap(await api.POST('/api/core/companies/{companyId}/number-series', { params: { path: { companyId } }, body, ...forCompany(companyId) }))
      : unwrap(await api.PUT('/api/core/companies/{companyId}/number-series/{id}', { params: { path: { companyId, id: body.id } }, body, ...forCompany(companyId) }));
  },
  async closeSeries(companyId: number, id: number, rowVersion: string): Promise<Saved> {
    return unwrap(
      await api.POST('/api/core/companies/{companyId}/number-series/{id}/close', {
        params: { path: { companyId, id } },
        body: { rowVersion },
        ...forCompany(companyId),
      }),
    );
  },
};
