import { formatDate, timeZone } from '../format/date';
import { t, type TextKey } from '../i18n';
import { UserKind, type UserDetail, type UserListItem } from './userApi';

/** Ziua de azi ca „aaaa-ll-zz”, in fusul orar Europe/Bucharest (UI-04). */
export function isoToday(now: Date = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);
}

/** Zilele dintre doua date „aaaa-ll-zz” (negative = in trecut). */
export function daysBetween(fromIso: string, toIso: string): number {
  return Math.round((Date.parse(`${toIso}T00:00:00Z`) - Date.parse(`${fromIso}T00:00:00Z`)) / 86_400_000);
}

/** Pragul „Expiră curând” al listei (macheta 06): 60 de zile, ca filtrul serverului. */
export const EXPIRING_DAYS = 60;

export type StatusKind = 'ok' | 'warn' | 'off' | 'bad';

/** Starea afisata in lista si in antetul fisei: activ, expira, activ pana la, dezactivat. */
export function userStatus(user: Pick<UserListItem, 'isActive' | 'isEmployee' | 'isSystem' | 'validUntil' | 'employeeLeaveDate'>, today: string):
  { readonly kind: StatusKind; readonly text: string } {
  if (!user.isActive) {
    return { kind: 'off', text: t('user.status.inactive') };
  }
  const end = user.isEmployee ? user.employeeLeaveDate : user.isSystem ? null : user.validUntil;
  if (end != null) {
    const date = formatDate(new Date(`${end}T12:00:00Z`));
    if (daysBetween(today, end) < (user.isEmployee ? 1 : 0)) {
      return { kind: 'bad', text: t('user.status.expired', { date }) };
    }
    if (daysBetween(today, end) <= EXPIRING_DAYS) {
      return { kind: 'warn', text: t('user.status.expires', { date }) };
    }
    return { kind: 'ok', text: t('user.status.activeUntil', { date }) };
  }
  return { kind: 'ok', text: t('user.status.active') };
}

export function kindOf(user: Pick<UserDetail, 'isSystem' | 'isEmployee'>): number {
  return user.isSystem ? UserKind.System : user.isEmployee ? UserKind.Employee : UserKind.External;
}

export function kindLabel(kind: number): string {
  return t(kind === UserKind.Employee ? 'user.kind.employee' : kind === UserKind.External ? 'user.kind.external' : 'user.kind.system');
}

export function accessTypeLabel(type: string | null | undefined): string {
  return type == null ? '' : t(`user.accessType.${type}` as TextKey);
}

interface RoleNode {
  readonly companyGroupId?: number | null;
  readonly groupName?: string | null;
  readonly companyId?: number | null;
  readonly companyName?: string | null;
  readonly branchId?: number | null;
  readonly branchName?: string | null;
  readonly locationId?: number | null;
  readonly locationName?: string | null;
}

/** Nodul unei atribuiri (21.11.2026), ca text: grupul, firma sau „firma › punct de lucru / locatie”; fara nod = toata instalarea. */
export function nodeName(role: RoleNode): string {
  if (role.companyGroupId != null) {
    return t('user.roles.groupNode', { name: role.groupName ?? '' });
  }
  if (role.companyId == null) {
    return t('user.roles.allCompanies');
  }
  const node = role.branchName ?? role.locationName;
  return node == null ? (role.companyName ?? '') : `${role.companyName ?? ''} › ${node}`;
}

/** „Nod: Rol” pe randuri, ca in coloana „Firme și roluri”. */
export function roleLines(roles: readonly (RoleNode & { readonly roleName: string })[]): string[] {
  const byNode = new Map<string, string[]>();
  for (const role of roles) {
    const node = nodeName(role);
    byNode.set(node, [...(byNode.get(node) ?? []), role.roleName]);
  }
  return [...byNode.entries()].map(([node, names]) => `${node}: ${names.join(', ')}`);
}

/** Cheia nodului in lista din fisa: „” toata instalarea, „g7” grup, „c3” firma, „b5” punct de lucru, „l9” locatie. */
export const INSTALLATION = '';

export function nodeKey(role: RoleNode): string {
  return role.companyGroupId != null ? `g${role.companyGroupId}`
    : role.locationId != null ? `l${role.locationId}`
      : role.branchId != null ? `b${role.branchId}`
        : role.companyId != null ? `c${role.companyId}` : INSTALLATION;
}

/** Cheia inapoi in campurile comenzii; firma punctului de lucru sau a locatiei o deduce serverul. */
export function nodeInput(key: string): { companyGroupId: number | null; companyId: number | null; branchId: number | null; locationId: number | null } {
  const id = Number(key.slice(1));
  const kind = key.charAt(0);
  return {
    companyGroupId: kind === 'g' ? id : null,
    companyId: kind === 'c' ? id : null,
    branchId: kind === 'b' ? id : null,
    locationId: kind === 'l' ? id : null,
  };
}

/** Ce acopera nodul: toate firmele, firmele grupului, toata firma, punctul de lucru cu locatiile lui sau doar locatia. */
export function scopeText(key: string): string {
  const kind = key.charAt(0);
  return t(kind === 'g' ? 'user.roles.scope.group' : kind === 'c' ? 'user.roles.scope.company' : kind === 'b' ? 'user.roles.scope.branch'
    : kind === 'l' ? 'user.roles.scope.location' : 'user.roles.scope.installation');
}

export interface NodeOption {
  readonly value: string;
  readonly text: string;
}

export interface NodeGroup {
  readonly label: string;
  readonly options: readonly NodeOption[];
}

interface NodeLookups {
  readonly companies: readonly { readonly id: number; readonly name: string }[];
  readonly groups: readonly { readonly id: number; readonly name: string }[];
  readonly branches: readonly { readonly id: number; readonly companyId: number; readonly name: string }[];
  readonly locations: readonly { readonly id: number; readonly companyId: number; readonly branchId?: number | null; readonly name: string }[];
}

/**
 * Nodurile din lista fisei: „Toate firmele” (doar Administrator), grupurile, apoi fiecare firma cu punctele ei de lucru si locatiile
 * (locatiile unui punct de lucru sub el, cele direct in firma la sfarsit).
 */
export function nodeGroups(lookups: NodeLookups, isAdmin: boolean): NodeGroup[] {
  const groups: NodeGroup[] = [];
  if (isAdmin) {
    groups.push({ label: t('user.roles.installation'), options: [{ value: INSTALLATION, text: t('user.roles.allCompanies') }] });
  }
  if (lookups.groups.length > 0) {
    groups.push({ label: t('user.roles.groups'), options: lookups.groups.map((g) => ({ value: `g${g.id}`, text: g.name })) });
  }
  for (const company of lookups.companies) {
    const options: NodeOption[] = [{ value: `c${company.id}`, text: t('user.roles.wholeCompanyOf', { name: company.name }) }];
    for (const branch of lookups.branches.filter((b) => b.companyId === company.id)) {
      options.push({ value: `b${branch.id}`, text: `↳ ${branch.name}` });
      for (const location of lookups.locations.filter((l) => l.branchId === branch.id)) {
        options.push({ value: `l${location.id}`, text: `    ↳ ${location.name}` });
      }
    }
    for (const location of lookups.locations.filter((l) => l.companyId === company.id && l.branchId == null)) {
      options.push({ value: `l${location.id}`, text: `↳ ${location.name}` });
    }
    groups.push({ label: company.name, options });
  }
  return groups;
}

/** Initialele pentru avatar: prima litera din primele doua cuvinte ale numelui. */
export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter((part) => part.length > 0)
    .slice(0, 2)
    .map((part) => part.charAt(0).toLocaleUpperCase('ro-RO'))
    .join('');
}
