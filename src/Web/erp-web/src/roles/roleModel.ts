import type { Permission, PermissionOverride } from './roleApi';

export type Effect = 'G' | 'R';

/** Actiunile cu coloana proprie in vederea „Pe rol” (macheta 07e); celelalte apar la „Alte drepturi”. */
export const STANDARD_ACTIONS = ['View', 'Manage', 'Validate', 'Cancel', 'ViewPersonalData'] as const;

export interface ResourceGroup {
  readonly code: string;
  readonly permissions: readonly Permission[];
}

export interface ModuleGroup {
  readonly module: string;
  readonly resources: readonly ResourceGroup[];
}

/** „Core.Partner.ViewPersonalData” -> resursa „Core.Partner” si actiunea „ViewPersonalData”. */
export function splitCode(code: string): { readonly resource: string; readonly action: string } {
  const dot = code.lastIndexOf('.');
  return { resource: code.slice(0, dot), action: code.slice(dot + 1) };
}

const actionOrder = (action: string) => {
  const index = (STANDARD_ACTIONS as readonly string[]).indexOf(action);
  return index === -1 ? STANDARD_ACTIONS.length : index;
};

/** Catalogul grupat pe module si resurse, in ordinea serverului; actiunile in ordinea coloanelor. */
export function groupPermissions(permissions: readonly Permission[]): ModuleGroup[] {
  const modules = new Map<string, Map<string, Permission[]>>();
  for (const permission of permissions) {
    const resources = modules.get(permission.moduleCode) ?? new Map<string, Permission[]>();
    modules.set(permission.moduleCode, resources);
    const { resource } = splitCode(permission.code);
    resources.set(resource, [...(resources.get(resource) ?? []), permission]);
  }
  return [...modules].map(([module, resources]) => ({
    module,
    resources: [...resources].map(([code, list]) => ({
      code,
      permissions: [...list].sort((a, b) => actionOrder(splitCode(a.code).action) - actionOrder(splitCode(b.code).action)
        || a.code.localeCompare(b.code)),
    })),
  }));
}

/**
 * Starea unei celule pe randul unui utilizator. inherited = dreptul vine dintr-un rol al lui; local = exceptia de pe firma vederii
 * (editabila); global = exceptia pentru toate firmele, vazuta dintr-o firma (doar citire). Revocarea castiga.
 */
export interface CellState {
  readonly effective: boolean;
  readonly inherited: boolean;
  readonly local: Effect | undefined;
  readonly global: Effect | undefined;
  /** Exceptia nu schimba nimic: drept adaugat desi e mostenit, sau revocat desi nu e dat. */
  readonly redundant: boolean;
}

export function cellState(inherited: boolean, local: Effect | undefined, global: Effect | undefined): CellState {
  const granted = inherited || local === 'G' || global === 'G';
  const revoked = local === 'R' || global === 'R';
  const redundant = (local === 'G' && (inherited || global === 'G')) || (local === 'R' && !inherited && global !== 'G');
  return { effective: granted && !revoked, inherited, local, global, redundant };
}

/**
 * Clic pe o celula a utilizatorului: exceptia locala dispare (revine mostenirea); altfel dreptul efectiv se revoca, iar lipsa lui
 * devine drept adaugat. O exceptie pentru toate firmele, vazuta dintr-o firma, nu se schimba de aici (undefined = fara actiune).
 */
export function toggle(state: CellState): Effect | null | undefined {
  if (state.local !== undefined) {
    return null;
  }
  if (state.global !== undefined) {
    return undefined;
  }
  return state.inherited ? 'R' : 'G';
}

export interface DraftOverride {
  readonly effect: Effect;
  readonly reason: string;
}

/** Exceptiile editabile ale utilizatorului in vedere: pe firma aleasa sau, pe „toate firmele”, cele cu CompanyId null. */
export function localOverrides(overrides: readonly PermissionOverride[], userId: number, companyId: number | null): Map<number, DraftOverride> {
  return new Map(overrides
    .filter((o) => o.userId === userId && o.companyId === companyId)
    .map((o) => [o.permissionId, { effect: o.effect as Effect, reason: o.reason ?? '' }]));
}

/** Exceptiile pentru toate firmele vazute dintr-o firma (pe „toate firmele” ele sunt chiar cele locale). */
export function globalOverrides(overrides: readonly PermissionOverride[], userId: number, companyId: number | null): Map<number, Effect> {
  return companyId === null
    ? new Map()
    : new Map(overrides.filter((o) => o.userId === userId && o.companyId === null).map((o) => [o.permissionId, o.effect as Effect]));
}

/** Lista trimisa la salvare; motivul se curata, iar la revocare un motiv gol nu se trimite. */
export function toInput(draft: ReadonlyMap<number, DraftOverride>) {
  return [...draft].map(([permissionId, o]) => ({
    permissionId,
    effect: o.effect,
    reason: o.reason.trim() === '' ? null : o.reason.trim(),
  }));
}

export const sameDraft = (a: ReadonlyMap<number, DraftOverride>, b: ReadonlyMap<number, DraftOverride>) =>
  a.size === b.size && [...a].every(([id, o]) => b.get(id)?.effect === o.effect && b.get(id)?.reason.trim() === o.reason.trim());
