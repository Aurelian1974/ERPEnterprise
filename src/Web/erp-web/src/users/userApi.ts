import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components, paths } from '../api/schema';

type Schemas = components['schemas'];
export type UserPage = Schemas['UserPageDto'];
export type UserListItem = Schemas['UserListItemDto'];
export type UserDetail = Schemas['UserDetailDto'];
export type UserLookups = Schemas['UserLookupsDto'];
export type UserRoleInput = Schemas['UserRoleInput'];
export type UserSession = Schemas['IdentityAccountSession'];
export type SaveUser = Schemas['SaveUser'];
export type UserSaved = Schemas['UserSavedDto'];

/** Tipul contului (02.01, 3.4): angajat, extern sau cont de sistem (administratorul integrat, creat de installer). */
export const UserKind = { Employee: 1, External: 2, System: 3 } as const;

/** Tipurile de acces ale externilor (macheta 06c); TECHNICAL = cont tehnic, fara parola interactiva. */
export const externalAccessTypes = ['AUDITOR', 'CONSULTANT', 'ACCOUNTANT', 'TECHNICAL'] as const;
export type ExternalAccessType = (typeof externalAccessTypes)[number];

/** Filtrele listei: Kind 1 angajati, 2 externi; Filter 1 inactivi, 2 expira curand, 3 fara 2FA. */
export interface UserCriteria {
  readonly search: string;
  readonly kind?: 1 | 2 | undefined;
  readonly filter?: 1 | 2 | 3 | undefined;
  readonly companyId?: number | undefined;
  readonly roleId?: number | undefined;
  readonly page: number;
}

export const PAGE_SIZE = 50;

export const userKeys = {
  all: ['core', 'users'] as const,
  list: (criteria: UserCriteria) => ['core', 'users', 'list', criteria] as const,
  detail: (id: number) => ['core', 'users', 'detail', id] as const,
  history: (id: number) => ['core', 'users', 'history', id] as const,
  sessions: (id: number) => ['core', 'users', 'sessions', id] as const,
  lookups: ['core', 'users', 'lookups'] as const,
};

type SearchQuery = NonNullable<NonNullable<paths['/api/core/users']['get']['parameters']['query']>>;

/** Parametrii cererii de cautare, fara cei necompletati. */
export function searchQuery(criteria: UserCriteria): SearchQuery {
  const search = criteria.search.trim();
  const entries: [keyof SearchQuery, string | number | undefined][] = [
    ['Search', search === '' ? undefined : search],
    ['Kind', criteria.kind],
    ['Filter', criteria.filter],
    ['CompanyId', criteria.companyId],
    ['RoleId', criteria.roleId],
    ['Page', criteria.page],
    ['PageSize', PAGE_SIZE],
  ];
  return Object.fromEntries(entries.filter(([, value]) => value !== undefined)) as SearchQuery;
}

export function useUsers(criteria: UserCriteria) {
  return useQuery({
    queryKey: userKeys.list(criteria),
    // Pagina anterioara ramane afisata cat se incarca urmatoarea (UI-12).
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/users', { params: { query: searchQuery(criteria) }, signal })),
  });
}

export function useUser(id: number | undefined) {
  return useQuery({
    queryKey: userKeys.detail(id ?? 0),
    enabled: id !== undefined,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/users/{id}', { params: { path: { id: id ?? 0 } }, signal })),
  });
}

export function useUserHistory(id: number, enabled: boolean) {
  return useQuery({
    queryKey: userKeys.history(id),
    enabled,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/users/{id}/history', { params: { path: { id } }, signal })),
  });
}

export function useUserSessions(id: number, enabled: boolean) {
  return useQuery({
    queryKey: userKeys.sessions(id),
    enabled,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/users/{id}/sessions', { params: { path: { id } }, signal })),
  });
}

export function useUserLookups() {
  return useQuery({
    queryKey: userKeys.lookups,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/users/lookups', { signal })),
    staleTime: 5 * 60_000,
  });
}

export const userApi = {
  async save(body: SaveUser): Promise<UserSaved> {
    return body.id === null
      ? unwrap(await api.POST('/api/core/users', { body }))
      : unwrap(await api.PUT('/api/core/users/{id}', { params: { path: { id: body.id } }, body }));
  },
  /** Parola temporara noua, aratata o singura data; sesiunile utilizatorului se inchid. */
  async resetPassword(id: number): Promise<string> {
    return unwrap(await api.POST('/api/core/users/{id}/reset-password', { params: { path: { id } } })).temporaryPassword;
  },
  async unlock(id: number): Promise<void> {
    unwrap(await api.POST('/api/core/users/{id}/unlock', { params: { path: { id } } }));
  },
  async resetTwoFactor(id: number): Promise<void> {
    unwrap(await api.POST('/api/core/users/{id}/reset-two-factor', { params: { path: { id } } }));
  },
  /** O sesiune anume sau toate (null). */
  async revokeSessions(id: number, sessionId: string | null): Promise<void> {
    unwrap(await api.POST('/api/core/users/{id}/revoke-sessions', { params: { path: { id } }, body: { sessionId } }));
  },
};
