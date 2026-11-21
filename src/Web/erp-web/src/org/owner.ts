import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { t } from '../i18n';

export type OwnerEntity = 'Item' | 'Partner';
export type OwnerOptions = components['schemas']['OwnerOptionsDto'];

/** Nivelurile proprietarului (02.01, 3.2-3.4; 21.11.2026), ca OwnerLevels din server. */
export const OwnerLevel = { Common: 0, Company: 1, Branch: 2, Location: 3 } as const;

/** Cheia proprietarului in liste si formulare: „common”, „company”, „b5” (punct de lucru), „l9” (locatie). */
export const OWNER_COMMON = 'common';
export const OWNER_COMPANY = 'company';

export interface OwnerChoice {
  readonly value: string;
  readonly text: string;
}

/** Proprietarii posibili in firma curenta: ce poate vedea (filtrul listei) si unde poate adauga sau modifica. */
export function useOwnerOptions(entity: OwnerEntity) {
  return useQuery({
    queryKey: ['core', 'owner-options', entity],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/core/owner-options/{entity}', { params: { path: { entity } }, signal })) as OwnerOptions | null,
    staleTime: 5 * 60_000,
  });
}

export function ownerKey(level: number, nodeId: number | null | undefined): string {
  return level === OwnerLevel.Branch && nodeId != null ? `b${nodeId}`
    : level === OwnerLevel.Location && nodeId != null ? `l${nodeId}`
      : level === OwnerLevel.Company ? OWNER_COMPANY : OWNER_COMMON;
}

/** Cheia in campurile comenzii de salvare (OwnerLevel + OwnerNodeId). */
export function ownerFields(key: string): { ownerLevel: number; ownerNodeId: number | null } {
  if (key.startsWith('b')) {
    return { ownerLevel: OwnerLevel.Branch, ownerNodeId: Number(key.slice(1)) };
  }
  if (key.startsWith('l')) {
    return { ownerLevel: OwnerLevel.Location, ownerNodeId: Number(key.slice(1)) };
  }
  return { ownerLevel: key === OWNER_COMPANY ? OwnerLevel.Company : OwnerLevel.Common, ownerNodeId: null };
}

function nodeText(node: OwnerOptions['nodes'][number]): string {
  return node.locationId != null && node.parentBranchId != null ? `    ↳ ${node.name}` : `↳ ${node.name}`;
}

/**
 * Proprietarii din care alege utilizatorul la adaugare sau modificare: comun si firma doar cu dreptul pe toata firma, apoi
 * nodurile lui. Proprietarul curent ramane in lista chiar daca nu e al lui (fisa e atunci doar pentru citire pe server).
 */
export function ownerChoices(options: OwnerOptions | null | undefined, current?: { readonly key: string; readonly name: string | null }): OwnerChoice[] {
  const choices: OwnerChoice[] = [];
  if (options == null || options.manageWholeCompany) {
    choices.push({ value: OWNER_COMMON, text: t('owner.common') });
  }
  if (options != null) {
    if (options.manageWholeCompany) {
      choices.push({ value: OWNER_COMPANY, text: t('owner.company', { name: options.companyName }) });
    }
    for (const node of options.nodes.filter((n) => n.canManage)) {
      choices.push({ value: node.branchId != null ? `b${node.branchId}` : `l${node.locationId ?? 0}`, text: nodeText(node) });
    }
  }
  if (current !== undefined && !choices.some((c) => c.value === current.key)) {
    choices.unshift({ value: current.key, text: current.key === OWNER_COMMON ? t('owner.common') : (current.name ?? current.key) });
  }
  return choices;
}

/** Filtrul listei: toate cele vizibile, doar comune, doar ale firmei curente, ale unui punct de lucru sau ale unei locatii. */
export function ownerFilters(options: OwnerOptions | null | undefined): OwnerChoice[] {
  if (options == null) {
    return [];
  }
  return [
    { value: OWNER_COMMON, text: t('owner.filter.common') },
    { value: OWNER_COMPANY, text: t('owner.filter.company', { name: options.companyName }) },
    ...options.nodes.filter((n) => n.canView).map((n) => ({ value: n.branchId != null ? `b${n.branchId}` : `l${n.locationId ?? 0}`, text: nodeText(n) })),
  ];
}

/** Filtrul ales in parametrii cautarii (Owner 1 comune / 2 ale firmei, OwnerBranchId, OwnerLocationId). */
export function ownerQuery(key: string | undefined): { Owner?: number; OwnerBranchId?: number; OwnerLocationId?: number } {
  if (key === undefined || key === '') {
    return {};
  }
  if (key === OWNER_COMMON) {
    return { Owner: 1 };
  }
  if (key === OWNER_COMPANY) {
    return { Owner: 2 };
  }
  return key.startsWith('b') ? { OwnerBranchId: Number(key.slice(1)) } : { OwnerLocationId: Number(key.slice(1)) };
}

/** Textul coloanei „Aparține”: comun sau numele nodului. */
export function ownerLabel(level: number, name: string | null | undefined): string {
  return level === OwnerLevel.Common || name == null ? t('owner.common') : name;
}
