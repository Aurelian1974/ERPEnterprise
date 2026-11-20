import { describe, expect, it } from 'vitest';
import type { PropertyDefinition } from './partnerApi';
import { searchQuery } from './partnerApi';
import { anafBadge, anafToForm, applies, displayFiscalCode, fromFormProps, propError, roToIso, toFormProps } from './partnerModel';

const definition = (id: number, dataType: number, changes: Partial<PropertyDefinition> = {}): PropertyDefinition => ({
  id, code: `P${id}`, name: `Proprietatea ${id}`, dataType, appliesTo: 3, section: 'Comercial', sortOrder: id, isRequired: false, isFilterable: false, ...changes,
});

describe('proprietățile partenerului (02.01, 3.2)', () => {
  const definitions = [definition(1, 1), definition(2, 2), definition(3, 3), definition(4, 4), definition(5, 5), definition(6, 6), definition(7, 7)];

  it('valorile serverului trec în formular și înapoi neschimbate, numerele cu virgulă și datele zz.ll.aaaa', () => {
    const values = [
      { propertyDefinitionId: 1, valueText: 'Livrare dimineața' },
      { propertyDefinitionId: 2, valueNumber: '4.5' },
      { propertyDefinitionId: 3, valueDate: '2027-06-30' },
      { propertyDefinitionId: 4, valueBool: false },
      { propertyDefinitionId: 5, valueOptionId: 11 },
      { propertyDefinitionId: 6, valueOptionId: 21 },
      { propertyDefinitionId: 6, valueOptionId: 22 },
      { propertyDefinitionId: 7, valueUserId: 3 },
    ];
    const form = toFormProps(definitions, values);
    expect(form).toEqual({ p1: 'Livrare dimineața', p2: '4,5', p3: '30.06.2027', p4: false, p5: '11', p6: ['21', '22'], p7: '3' });
    expect(fromFormProps(definitions, form)).toEqual(values);
  });

  it('valorile goale nu se trimit', () => {
    expect(fromFormProps(definitions, { p1: '  ', p2: null, p6: [], p4: null })).toEqual([]);
  });

  it('obligatorie doar dacă se aplică rolului; numărul și data se validează după tip', () => {
    const segment = definition(1, 5, { isRequired: true, appliesTo: 1 });
    expect(applies(segment, false, true)).toBe(false);
    expect(propError(segment, null, applies(segment, false, true))).toBeUndefined();
    expect(propError(segment, null, applies(segment, true, false))).toBe('Câmpul este obligatoriu.');
    expect(propError(definition(2, 2), 'patru', true)).toBe('Introduceți un număr (cel mult 6 zecimale).');
    expect(propError(definition(2, 2), '4,25', true)).toBeUndefined();
    expect(propError(definition(3, 3), '31.02.2027', true)).toBe('Data are forma zz.ll.aaaa.');
  });

  it('o dată inexistentă nu se convertește', () => {
    expect(roToIso('29.02.2028')).toBe('2028-02-29');
    expect(roToIso('29.02.2027')).toBeUndefined();
    expect(roToIso('2027-02-01')).toBeUndefined();
  });
});

describe('lista partenerilor', () => {
  it('CUI-ul plătitorului de TVA român apare cu RO; ANAF doar la juridicele române', () => {
    expect(displayFiscalCode({ partnerType: 1, fiscalCode: '33445566', vatPayer: true })).toBe('RO33445566');
    expect(displayFiscalCode({ partnerType: 1, fiscalCode: '33445566', vatPayer: false })).toBe('33445566');
    expect(displayFiscalCode({ partnerType: 2, fiscalCode: 'DE298765432', vatPayer: true })).toBe('DE298765432');
    expect(anafBadge({ partnerType: 1, anafInactive: true, vatOnCollection: true })?.kind).toBe('bad');
    expect(anafBadge({ partnerType: 1, anafInactive: false, vatOnCollection: true })?.text).toBe('TVA la încasare');
    expect(anafBadge({ partnerType: 2, anafInactive: false, vatOnCollection: false })).toBeUndefined();
  });

  it('cererea de căutare omite filtrele necompletate și arată implicit doar partenerii activi', () => {
    expect(searchQuery({ search: '  agro ', showInactive: false, page: 2 })).toEqual({ Search: 'agro', IsActive: true, Page: 2, PageSize: 50 });
    expect(searchQuery({ search: '', role: 2, showInactive: true, page: 1 })).toEqual({ Role: 2, Page: 1, PageSize: 50 });
  });
});

describe('datele ANAF în fișă', () => {
  const company = {
    fiscalCode: '14080700', name: 'REALITATEA MEDIA SA', tradeRegisterNo: 'J40/6736/2001', vatPayer: false, vatOnCollection: true, anafInactive: true,
    splitVat: false, eInvoiceRegistered: false, registrationState: null, address: 'Str. Frumoasă nr. 20', city: 'Sector 1 Mun. București',
    countyCode: 'B', postalCode: '010987', phone: null, checkedAt: '2026-11-20T08:30:00Z',
  };

  it('TVA la încasare cere plătitor; județul se ia doar dacă există în nomenclator; câmpurile goale ANAF nu șterg datele', () => {
    const values = anafToForm(company, [{ code: 'B', countryCode: 'RO' }]);
    expect(values).toMatchObject({ vatPayer: false, vatOnCollection: false, anafInactive: true, countyCode: 'B', countryCode: 'RO' });
    expect('phone' in values).toBe(false);
    expect('countyCode' in anafToForm(company, [{ code: 'CJ', countryCode: 'RO' }])).toBe(false);
  });
});
