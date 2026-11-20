import { t } from '../i18n';
import { DataType, PartnerType, type AnafCompany, type PartnerListItem, type PropertyDefinition, type PropertyValue } from './partnerApi';

/**
 * Valoarea unei proprietati in formular: text (text, numar in format romanesc, data zz.ll.aaaa), da/nu, optiunea sau
 * utilizatorul ales (id ca text) ori lista de optiuni (lista multipla). Gol = null sau lista goala.
 */
export type PropInput = string | boolean | string[] | null;

export const propKey = (id: number) => `p${id}`;

/** Proprietatea se aplica rolului partenerului (AppliesTo: 1 client, 2 furnizor, 3 ambele). */
export function applies(definition: Pick<PropertyDefinition, 'appliesTo'>, isCustomer: boolean, isSupplier: boolean): boolean {
  return definition.appliesTo === 3 || (definition.appliesTo === 1 && isCustomer) || (definition.appliesTo === 2 && isSupplier);
}

/** „2027-06-30” → „30.06.2027”. */
export function isoToRo(value: string): string {
  const [year, month, day] = value.split('-');
  return `${day}.${month}.${year}`;
}

/** „30.06.2027” → „2027-06-30”; undefined daca nu e o data calendaristica. */
export function roToIso(value: string): string | undefined {
  const match = /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/.exec(value.trim());
  if (match === null) {
    return undefined;
  }
  const [day, month, year] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) {
    return undefined;
  }
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

const numberPattern = /^-?\d{1,13}([.,]\d{1,6})?$/;

/** Valorile salvate ale partenerului, in forma formularului, pentru toate proprietatile cunoscute. */
export function toFormProps(definitions: readonly PropertyDefinition[], values: readonly PropertyValue[]): Record<string, PropInput> {
  const result: Record<string, PropInput> = {};
  for (const definition of definitions) {
    const own = values.filter((v) => v.propertyDefinitionId === definition.id);
    const first = own[0];
    switch (definition.dataType) {
      case DataType.Bool:
        result[propKey(definition.id)] = first?.valueBool ?? null;
        break;
      case DataType.MultiList:
        result[propKey(definition.id)] = own.map((v) => String(v.valueOptionId));
        break;
      case DataType.List:
        result[propKey(definition.id)] = first?.valueOptionId == null ? null : String(first.valueOptionId);
        break;
      case DataType.User:
        result[propKey(definition.id)] = first?.valueUserId == null ? null : String(first.valueUserId);
        break;
      case DataType.Number:
        result[propKey(definition.id)] = first?.valueNumber == null ? null : first.valueNumber.replace('.', ',');
        break;
      case DataType.Date:
        result[propKey(definition.id)] = first?.valueDate == null ? null : isoToRo(first.valueDate);
        break;
      default:
        result[propKey(definition.id)] = first?.valueText ?? null;
    }
  }
  return result;
}

const isEmpty = (value: PropInput | undefined) =>
  value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0) || (typeof value === 'string' && value.trim() === '');

/** Mesajul de eroare al valorii, dupa tipul proprietatii (aceleasi reguli ca pe server); obligativitatea doar daca se aplica rolului. */
export function propError(definition: PropertyDefinition, value: PropInput | undefined, applicable: boolean): string | undefined {
  if (isEmpty(value)) {
    return applicable && definition.isRequired && definition.dataType !== DataType.Bool ? t('common.required') : undefined;
  }
  if (definition.dataType === DataType.Number && (typeof value !== 'string' || !numberPattern.test(value.trim()))) {
    return t('validation.number');
  }
  if (definition.dataType === DataType.Date && (typeof value !== 'string' || roToIso(value) === undefined)) {
    return t('validation.date');
  }
  return undefined;
}

/** Valorile pentru API: toate proprietatile completate, si cele ascunse de rolul curent (nu se pierd la schimbarea rolului). */
export function fromFormProps(definitions: readonly PropertyDefinition[], props: Record<string, PropInput>): PropertyValue[] {
  const result: PropertyValue[] = [];
  for (const definition of definitions) {
    const value = props[propKey(definition.id)];
    if (isEmpty(value)) {
      continue;
    }
    const id = definition.id;
    switch (definition.dataType) {
      case DataType.Bool:
        result.push({ propertyDefinitionId: id, valueBool: value === true });
        break;
      case DataType.MultiList:
        for (const option of value as string[]) {
          result.push({ propertyDefinitionId: id, valueOptionId: Number(option) });
        }
        break;
      case DataType.List:
        result.push({ propertyDefinitionId: id, valueOptionId: Number(value) });
        break;
      case DataType.User:
        result.push({ propertyDefinitionId: id, valueUserId: Number(value) });
        break;
      case DataType.Number:
        result.push({ propertyDefinitionId: id, valueNumber: (value as string).trim().replace(',', '.') });
        break;
      case DataType.Date:
        result.push({ propertyDefinitionId: id, valueDate: roToIso(value as string) ?? null });
        break;
      default:
        result.push({ propertyDefinitionId: id, valueText: (value as string).trim() });
    }
  }
  return result;
}

/** CUI-ul afisat: „RO” in fata pentru platitorii de TVA romani (codul de TVA), altfel codul ca atare. */
export function displayFiscalCode(partner: Pick<PartnerListItem, 'partnerType' | 'fiscalCode' | 'vatPayer'>): string {
  if (partner.fiscalCode == null) {
    return '';
  }
  return partner.partnerType === PartnerType.LegalRomania && partner.vatPayer ? `RO${partner.fiscalCode}` : partner.fiscalCode;
}

/** Eticheta ANAF din lista: inactiv, TVA la incasare sau activ, numai pentru juridicele romane. */
export function anafBadge(partner: Pick<PartnerListItem, 'partnerType' | 'anafInactive' | 'vatOnCollection'>): { text: string; kind: 'ok' | 'warn' | 'bad' } | undefined {
  if (partner.partnerType !== PartnerType.LegalRomania) {
    return undefined;
  }
  if (partner.anafInactive) {
    return { text: t('partner.anaf.inactive'), kind: 'bad' };
  }
  return partner.vatOnCollection ? { text: t('partner.anaf.vatOnCollection'), kind: 'warn' } : { text: t('partner.anaf.active'), kind: 'ok' };
}

/**
 * Campurile fisei completate din raspunsul ANAF. Judetul se ia doar daca exista in nomenclator (codul auto ANAF, de exemplu
 * „SV”, „B”); telefonul doar daca ANAF il are. Momentul verificarii se salveaza odata cu fisa.
 */
export function anafToForm(company: AnafCompany, counties: readonly { code: string; countryCode: string }[]): Record<string, string | boolean | null> {
  const values: Record<string, string | boolean | null> = {
    fiscalCode: company.fiscalCode,
    name: company.name,
    countryCode: 'RO',
    vatPayer: company.vatPayer,
    vatOnCollection: company.vatPayer && company.vatOnCollection,
    anafInactive: company.anafInactive,
    anafCheckedAt: company.checkedAt,
  };
  if (company.tradeRegisterNo != null) {
    values.tradeRegisterNo = company.tradeRegisterNo;
  }
  if (company.address != null) {
    values.address = company.address;
  }
  if (company.city != null) {
    values.city = company.city;
  }
  if (company.postalCode != null) {
    values.postalCode = company.postalCode;
  }
  if (company.countyCode != null && counties.some((c) => c.countryCode === 'RO' && c.code === company.countyCode)) {
    values.countyCode = company.countyCode;
  }
  if (company.phone != null) {
    values.phone = company.phone;
  }
  return values;
}
