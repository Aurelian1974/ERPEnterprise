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

/** „Firma: Rol” pe randuri, ca in coloana „Firme și roluri”; atribuirile fara firma = toata instalarea. */
export function roleLines(roles: readonly { readonly companyName?: string | null; readonly roleName: string }[]): string[] {
  const byCompany = new Map<string, string[]>();
  for (const role of roles) {
    const company = role.companyName ?? t('user.roles.allCompanies');
    byCompany.set(company, [...(byCompany.get(company) ?? []), role.roleName]);
  }
  return [...byCompany.entries()].map(([company, names]) => `${company}: ${names.join(', ')}`);
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
