import { formatDate, timeZone } from '../format/date';
import { t, type TextKey } from '../i18n';
import { ro } from '../i18n/ro';

/** Intervalele filtrului „Interval” (macheta 07h). */
export const INTERVALS = ['today', '7', '30', '90', '365', 'all'] as const;
export type Interval = (typeof INTERVALS)[number];

/** Inceputul intervalului (ISO, UTC), rotunjit la minut ca cheia interogarii sa ramana stabila; „all” = fara limita. */
export function intervalStart(interval: Interval, now: Date): string | undefined {
  if (interval === 'all') {
    return undefined;
  }
  const start = new Date(now);
  if (interval === 'today') {
    start.setHours(0, 0, 0, 0);
  } else {
    start.setDate(start.getDate() - Number(interval));
    start.setSeconds(0, 0);
  }
  return start.toISOString();
}

function lookup(key: string): string | undefined {
  return key in ro ? t(key as TextKey) : undefined;
}

/** „Core.Partner” → „Partener”; o entitate necunoscuta ramane cu codul ei, fara „Core.”. */
export function entityName(entityType: string): string {
  return lookup(`audit.entity.${entityType}`) ?? entityType.replace(/^Core\./, '');
}

/**
 * Textul actiunii: salvarile devin „Creare” (prima intrare a inregistrarii) sau „Modificare”; celelalte au textul lor
 * (de exemplu „Resetare parolă”), iar o actiune necunoscuta ramane cu ultimul segment al codului.
 */
export function actionText(action: string, isFirst?: boolean): string {
  if (action.endsWith('.Save')) {
    return isFirst === undefined ? t('audit.verb.save') : isFirst ? t('audit.verb.create') : t('audit.verb.update');
  }
  return lookup(`audit.action.${action}`) ?? action.slice(action.lastIndexOf('.') + 1);
}

/** Optiunea din filtrul „Acțiune”: entitatea si actiunea („Partener · Salvare”). */
export function actionOption(action: string): string {
  const index = action.lastIndexOf('.');
  return `${entityName(index > 0 ? action.slice(0, index) : action)} · ${actionText(action)}`;
}

/** Culoarea etichetei: vizualizarea datelor personale e avertizare, stergerile si esecurile sunt erori. */
export function actionTone(action: string): 'warn' | 'error' | 'info' {
  if (action.endsWith('.ViewPersonalData')) {
    return 'warn';
  }
  return /\.(Delete|Fail|Cancel)$/.test(action) ? 'error' : 'info';
}

/** Numele campului din Changes („isActive” → „Activ”); un camp necunoscut ramane cu numele lui. */
export function fieldLabel(field: string): string {
  return lookup(`audit.field.${field}`) ?? field;
}

const dateTimeFormat = new Intl.DateTimeFormat('ro-RO', { timeZone, hour: '2-digit', minute: '2-digit' });
const secondsFormat = new Intl.DateTimeFormat('ro-RO', { timeZone, hour: '2-digit', minute: '2-digit', second: '2-digit' });

/** „14.11.2026 10:44”. */
export function whenText(value: string): string {
  const date = new Date(value);
  return `${formatDate(date)} ${dateTimeFormat.format(date)}`;
}

/** „14.11.2026, 10:44:07”. */
export function whenLongText(value: string): string {
  const date = new Date(value);
  return `${formatDate(date)}, ${secondsFormat.format(date)}`;
}

/** O valoare din Changes, ca text: Da / Nu, datele zz.ll.aaaa, listele separate prin virgula, lipsa = „—”. */
export function formatValue(value: unknown): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  if (typeof value === 'boolean') {
    return value ? t('common.yes') : t('common.no');
  }
  if (typeof value === 'number') {
    return Number.isInteger(value) ? String(value) : value.toLocaleString('ro-RO', { maximumFractionDigits: 6 });
  }
  if (typeof value === 'string') {
    const day = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
    if (day !== null) {
      return `${day[3]}.${day[2]}.${day[1]}`;
    }
    return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value) ? whenText(value) : value;
  }
  if (Array.isArray(value)) {
    return value.map(formatValue).join(', ');
  }
  return JSON.stringify(value);
}

export interface ChangeRow {
  readonly field: string;
  readonly before: string | undefined;
  readonly after: string;
}

function parse(json: string | null | undefined): Record<string, unknown> | undefined {
  if (json === null || json === undefined) {
    return undefined;
  }
  try {
    const value: unknown = JSON.parse(json);
    return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Tabelul Camp / Inainte / Dupa: intrarea pastreaza valorile de dupa modificare, iar cele de dinainte vin din intrarea anterioara
 * a aceleiasi inregistrari. Fara intrare anterioara (crearea) se arata toate campurile, fara „Înainte”; altfel doar cele schimbate.
 */
export function diffChanges(changes: string | null | undefined, previous: string | null | undefined): ChangeRow[] {
  const after = parse(changes) ?? {};
  const before = parse(previous);
  if (before === undefined) {
    return Object.entries(after)
      .filter(([, value]) => value !== null && value !== undefined)
      .map(([field, value]) => ({ field, before: undefined, after: formatValue(value) }));
  }
  const fields = [...Object.keys(after), ...Object.keys(before).filter((key) => !(key in after))];
  return fields
    .filter((field) => JSON.stringify(after[field] ?? null) !== JSON.stringify(before[field] ?? null))
    .map((field) => ({ field, before: formatValue(before[field]), after: formatValue(after[field]) }));
}

/** Fisa inregistrarii, pentru „Deschide fișa”; doar entitatile cu ecran propriu. */
export function entityLink(entityType: string, entityId: number | null | undefined): string | undefined {
  if (entityId === null || entityId === undefined) {
    return undefined;
  }
  switch (entityType) {
    case 'Core.Partner': return `/nomenclatoare/parteneri?id=${entityId}`;
    case 'Core.Item': return `/nomenclatoare/articole?id=${entityId}`;
    case 'Core.User': return `/admin/utilizatori?id=${entityId}`;
    case 'Core.Employee': return `/admin/angajati?id=${entityId}`;
    case 'Core.Company': return `/admin/firme?nod=c-${entityId}`;
    case 'Core.Branch': return `/admin/firme?nod=b-${entityId}`;
    case 'Core.Location': return `/admin/firme?nod=l-${entityId}`;
    case 'Core.CompanyGroup': return `/admin/firme?nod=g-${entityId}`;
    default: return undefined;
  }
}

/** Motivul unei autentificari esuate (LoginFailures din Erp.Auth). */
export function loginReason(reason: string | null | undefined): string {
  return reason === null || reason === undefined ? '' : lookup(`audit.login.reason.${reason}`) ?? reason;
}
