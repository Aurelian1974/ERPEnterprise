import { describe, expect, it } from 'vitest';
import {
  allowsNegativeStock, filterWarehouses, holderOn, quantityForApi, quantityForInput, quantityPattern, usesLocation, usesThirdParty, WarehouseKind,
  type WarehouseItem, type WarehouseKeepers,
} from './inventoryApi';
import { toWarehouseBody } from './WarehousesPage';
import { receiptDifference, toLinesBody } from './documentsApi';

const row = (id: number, isActive: boolean, isBlocked: boolean): WarehouseItem => ({
  id, code: `G${id}`, name: `Gestiune ${id}`, kind: 1, valuationBasis: 1, locationId: 1, locationName: 'Cluj', thirdPartyId: null, thirdPartyName: null,
  negativeStockPolicy: 1, purpose: 1, dimensionValueId: id, isBlocked, isActive,
});

describe('gestiunile (02.05, S1 3.1)', () => {
  it('filtrează după chip-urile machetei s1-01', () => {
    const rows = [row(1, true, false), row(2, true, true), row(3, false, false)];
    expect(filterWarehouses(rows, 'all').map((r) => r.id)).toEqual([1, 2, 3]);
    expect(filterWarehouses(rows, 'active').map((r) => r.id)).toEqual([1, 2]);
    expect(filterWarehouses(rows, 'blocked').map((r) => r.id)).toEqual([2]);
    expect(filterWarehouses(rows, 'inactive').map((r) => r.id)).toEqual([3]);
  });

  it('tipul decide locația, partenerul și stocul negativ', () => {
    expect([1, 2, 3, 4, 5].map(usesLocation)).toEqual([true, true, false, true, false]);
    expect([1, 2, 3, 4, 5].map(usesThirdParty)).toEqual([false, false, false, true, true]);
    expect([1, 2, 3, 4, 5].map(allowsNegativeStock)).toEqual([true, true, false, false, true]);
  });

  it('corpul salvării păstrează doar câmpurile tipului', () => {
    const body = toWarehouseBody({
      kind: WarehouseKind.AtThirdParty, code: 'LA-LOG', name: 'La Logistic Alfa', locationId: 9, valuationBasis: 2, retailPriceListId: 4, thirdPartyId: 7,
      negativeStockPolicy: 2, defaultCostCenterId: null, deliveryOrderMode: 1, returnWarehouseId: null, purpose: 2,
    }, undefined);
    expect(body).toMatchObject({ id: null, locationId: null, valuationBasis: 1, retailPriceListId: null, thirdPartyId: 7, negativeStockPolicy: 2, purpose: 1 });

    const custody = toWarehouseBody({
      kind: WarehouseKind.Custody, code: 'CUST-PL', name: 'Custodie', locationId: 3, valuationBasis: 1, retailPriceListId: null, thirdPartyId: 7,
      negativeStockPolicy: 2, defaultCostCenterId: null, deliveryOrderMode: 1, returnWarehouseId: null, purpose: 1,
    }, undefined);
    expect(custody).toMatchObject({ locationId: 3, negativeStockPolicy: 1 });
  });

  it('titularul la o dată vine din perioadele gestionarilor', () => {
    const keepers: WarehouseKeepers['keepers'] = [
      { id: 1, employeeId: 10, employeeName: 'Gheorghe Ilie', role: 1, validFrom: '2026-01-01', validTo: '2026-11-14', guaranteeNote: null, handoverId: null,
        leaveDate: null, rowVersion: 'AA==' },
      { id: 2, employeeId: 11, employeeName: 'Mihai Stan', role: 1, validFrom: '2026-11-15', validTo: null, guaranteeNote: null, handoverId: 5,
        leaveDate: null, rowVersion: 'AA==' },
      { id: 3, employeeId: 12, employeeName: 'Ana Pop', role: 2, validFrom: '2026-01-01', validTo: null, guaranteeNote: null, handoverId: null,
        leaveDate: null, rowVersion: 'AA==' },
    ];
    expect(holderOn(keepers, '2026-11-14')?.employeeName).toBe('Gheorghe Ilie');
    expect(holderOn(keepers, '2026-11-15')?.employeeName).toBe('Mihai Stan');
    expect(holderOn(keepers, '2025-12-31')).toBeUndefined();
  });

  it('cantitățile circulă ca text invariant, cu cel mult șase zecimale', () => {
    expect(quantityPattern.test('12,5')).toBe(true);
    expect(quantityPattern.test('1.1234567')).toBe(false);
    expect(quantityForApi(' 12,5 ')).toBe('12.5');
    expect(quantityForApi('')).toBeNull();
    expect(quantityForInput('0.25')).toBe('0,25');
    expect(quantityForInput(null)).toBe('');
  });

  it('diferența la primire se compară pe text, fără number', () => {
    expect(receiptDifference('50', '26')).toBe('shortage');
    expect(receiptDifference('10', '12,5')).toBe('surplus');
    expect(receiptDifference('0.1', '0,100000')).toBe('none');
    expect(receiptDifference('1', 'x')).toBe('invalid');
  });

  it('liniile documentului circulă cu cantitatea și costul invariante', () => {
    expect(toLinesBody([{ itemVariantId: 4, label: 'APA', qty: '2,5', unitCost: '' }])[0]).toMatchObject({ qty: '2.5', unitCost: null, uomFactor: '1' });
  });
});
