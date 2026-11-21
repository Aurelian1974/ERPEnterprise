import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components, paths } from '../api/schema';

type Schemas = components['schemas'];
export type EmployeePage = Schemas['EmployeePageDto'];
export type EmployeeListItem = Schemas['EmployeeListItemDto'];
export type EmployeeDetail = Schemas['EmployeeDetailDto'];
export type EmployeeLookups = Schemas['EmployeeLookupsDto'];
export type SaveEmployee = Schemas['SaveEmployee'];
export type Saved = Schemas['SavedEntity'];

export interface EmployeeCriteria {
  readonly search: string;
  readonly companyId?: number | undefined;
  readonly showInactive: boolean;
  readonly withoutUser?: boolean | undefined;
  readonly page: number;
}

export const PAGE_SIZE = 50;

export const employeeKeys = {
  all: ['core', 'employees'] as const,
  list: (criteria: EmployeeCriteria) => ['core', 'employees', 'list', criteria] as const,
  detail: (id: number) => ['core', 'employees', 'detail', id] as const,
  history: (id: number) => ['core', 'employees', 'history', id] as const,
  lookups: ['core', 'employees', 'lookups'] as const,
};

type SearchQuery = NonNullable<NonNullable<paths['/api/core/employees']['get']['parameters']['query']>>;

export function searchQuery(criteria: EmployeeCriteria): SearchQuery {
  const search = criteria.search.trim();
  const entries: [keyof SearchQuery, string | number | boolean | undefined][] = [
    ['Search', search === '' ? undefined : search],
    ['CompanyId', criteria.companyId],
    ['IsActive', criteria.showInactive ? undefined : true],
    ['WithoutUser', criteria.withoutUser],
    ['Page', criteria.page],
    ['PageSize', PAGE_SIZE],
  ];
  return Object.fromEntries(entries.filter(([, value]) => value !== undefined)) as SearchQuery;
}

export function useEmployees(criteria: EmployeeCriteria) {
  return useQuery({
    queryKey: employeeKeys.list(criteria),
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/employees', { params: { query: searchQuery(criteria) }, signal })),
  });
}

export function useEmployee(id: number | undefined) {
  return useQuery({
    queryKey: employeeKeys.detail(id ?? 0),
    enabled: id !== undefined,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/employees/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  });
}

export function useEmployeeHistory(id: number, enabled: boolean) {
  return useQuery({
    queryKey: employeeKeys.history(id),
    enabled,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/employees/{id}/history', { params: { path: { id } }, signal })),
  });
}

export function useEmployeeLookups() {
  return useQuery({
    queryKey: employeeKeys.lookups,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/employees/lookups', { signal })),
    staleTime: 10 * 60_000,
  });
}

export const employeeApi = {
  async save(body: SaveEmployee): Promise<Saved> {
    return body.id === null
      ? unwrap(await api.POST('/api/core/employees', { body }))
      : unwrap(await api.PUT('/api/core/employees/{id}', { params: { path: { id: body.id } }, body }));
  },
};
