import { useSyncExternalStore } from 'react';
import type { CurrentUser } from '../api/session';
import { t } from '../i18n';

// Nodul curent din antet (21.11.2026): toata firma („”), un punct de lucru („b5”) sau o locatie („l9”), memorat in browser pe firma.
// Decide proprietarul articolelor si partenerilor noi si filtreaza listele lor. Serverul verifica oricum dreptul pe nod.
const prefix = 'erp.node.';
const listeners = new Set<() => void>();
let version = 0;

function read(companyId: number): string | undefined {
  try {
    return localStorage.getItem(prefix + companyId) ?? undefined;
  } catch {
    return undefined;
  }
}

export const nodeStore = {
  get: read,
  set(companyId: number, key: string): void {
    try {
      localStorage.setItem(prefix + companyId, key);
    } catch {
      // Fara stocare in browser nodul ramane doar pana la reincarcare.
    }
    version += 1;
    listeners.forEach((listener) => listener());
  },
  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
  version: () => version,
};

export interface ContextOption {
  readonly value: string;
  readonly text: string;
}

type ContextUser = Pick<CurrentUser, 'companyId' | 'companies' | 'wholeCompany' | 'nodes'>;

/** Optiunile selectorului: „Toată firma” (doar cu rol pe firma), apoi punctele de lucru cu locatiile lor sub ele. */
export function contextOptions(user: ContextUser): ContextOption[] {
  const options: ContextOption[] = user.wholeCompany ? [{ value: '', text: t('header.wholeCompany') }] : [];
  for (const node of user.nodes) {
    options.push(node.branchId != null
      ? { value: `b${node.branchId}`, text: node.name }
      : { value: `l${node.locationId ?? 0}`, text: node.parentBranchId != null && user.nodes.some((n) => n.branchId === node.parentBranchId)
        ? `↳ ${node.name}` : node.name });
  }
  return options;
}

/** Nodul ales, daca e inca al utilizatorului; altfel toata firma sau primul lui nod. */
export function effectiveContext(user: ContextUser, stored: string | undefined): string {
  const options = contextOptions(user);
  if (stored !== undefined && options.some((o) => o.value === stored)) {
    return stored;
  }
  return options[0]?.value ?? '';
}

/** Numele nodului curent: firma (toata firma) sau punctul de lucru / locatia. */
export function contextName(user: ContextUser, key: string): string {
  const company = user.companies.find((c) => c.id === user.companyId)?.name ?? '';
  const node = key === '' ? undefined
    : user.nodes.find((n) => (key.startsWith('b') ? n.branchId : n.locationId) === Number(key.slice(1)));
  return node === undefined ? company : `${company} › ${node.name}`;
}

/** Parametrii cautarii pentru nodul din antet. */
export function contextQuery(key: string): { ContextBranchId?: number; ContextLocationId?: number } {
  return key.startsWith('b') ? { ContextBranchId: Number(key.slice(1)) } : key.startsWith('l') ? { ContextLocationId: Number(key.slice(1)) } : {};
}

/** Proprietarul articolelor si partenerilor noi: firma pentru „toată firma”, altfel nodul. */
export function contextOwner(key: string): string {
  return key === '' ? 'company' : key;
}

/** Nodul curent pentru utilizatorul dat (se reevalueaza la schimbarea din antet). */
export function useContextNode(user: ContextUser): string {
  useSyncExternalStore(nodeStore.subscribe, nodeStore.version);
  return effectiveContext(user, nodeStore.get(user.companyId));
}
