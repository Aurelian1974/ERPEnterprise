import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { HistoryEntry } from '../partners/partnerApi';

export type AuditPage = components['schemas']['AuditPageDto'];
export type AuditEntry = components['schemas']['AuditEntryItemDto'];
export type AuditDetail = components['schemas']['AuditEntryDetailDto'];
export type LoginPage = components['schemas']['LoginEventPageDto'];
export type LoginEvent = components['schemas']['LoginEventDto'];

export const AUDIT_PAGE_SIZE = 50;

/** Filtrele jurnalului; from/to sunt momente ISO (UTC), from inclusiv, to exclusiv. */
export interface AuditFilter {
  readonly from?: string | undefined;
  readonly to?: string | undefined;
  readonly userId?: number | undefined;
  readonly entityType?: string | undefined;
  readonly entityId?: number | undefined;
  readonly action?: string | undefined;
  readonly search: string;
  readonly page: number;
}

export interface LoginFilter {
  readonly from?: string | undefined;
  readonly to?: string | undefined;
  readonly success?: boolean | undefined;
  readonly search: string;
  readonly page: number;
}

export const auditKeys = {
  all: ['core', 'audit'] as const,
  list: (filter: AuditFilter) => ['core', 'audit', 'list', filter] as const,
  entry: (id: number) => ['core', 'audit', 'entry', id] as const,
  logins: (filter: LoginFilter) => ['core', 'audit', 'logins', filter] as const,
  companyHistory: (id: number) => ['core', 'audit', 'company', id] as const,
};

/** Parametrii cererii fara valorile goale (undefined sau text gol). */
const defined = <T extends Record<string, unknown>>(value: T) =>
  Object.fromEntries(Object.entries(value).filter(([, v]) => v !== undefined && v !== '')) as { [K in keyof T]?: Exclude<T[K], undefined> };

/** Configurare › Audit (macheta 07h): pagina jurnalului si valorile filtrelor din interval. */
export function useAuditPage(filter: AuditFilter, enabled = true) {
  return useQuery({
    queryKey: auditKeys.list(filter),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/audit', {
      params: {
        query: defined({
          From: filter.from, To: filter.to, UserId: filter.userId, EntityType: filter.entityType, EntityId: filter.entityId,
          Action: filter.action, Search: filter.search.trim(), Page: filter.page, PageSize: AUDIT_PAGE_SIZE,
        }),
      },
      signal,
    })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useAuditEntry(id: number | undefined) {
  return useQuery({
    queryKey: auditKeys.entry(id ?? 0),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/audit/{id}', { params: { path: { id: id ?? 0 } }, signal })),
    enabled: id !== undefined,
  });
}

/** Jurnalul de autentificari (comutatorul „Autentificări”). */
export function useLoginEvents(filter: LoginFilter, enabled: boolean) {
  return useQuery({
    queryKey: auditKeys.logins(filter),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/audit/logins', {
      params: {
        query: defined({
          From: filter.from, To: filter.to, Success: filter.success, Search: filter.search.trim(), Page: filter.page, PageSize: AUDIT_PAGE_SIZE,
        }),
      },
      signal,
    })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/** Tab-ul Istoric al fisei firmei. */
export function useCompanyHistory(id: number, enabled: boolean) {
  return useQuery({
    queryKey: auditKeys.companyHistory(id),
    queryFn: async ({ signal }): Promise<HistoryEntry[]> =>
      unwrap(await api.GET('/api/core/companies/{id}/history', { params: { path: { id } }, signal })),
    enabled,
  });
}
