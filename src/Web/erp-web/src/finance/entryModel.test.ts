import { describe, expect, it } from 'vitest';
import { balance, formula, fromCents, toCents } from './entryModel';

describe('entryModel', () => {
  it('parses amounts to cents without floating point', () => {
    expect(toCents('1200.50')).toBe(120050);
    expect(toCents('0,1')).toBe(10);
    expect(toCents('-2420')).toBe(-242000);
    expect(toCents('12.345')).toBeNull();
    expect(toCents('abc')).toBeNull();
    expect(fromCents(-1205)).toBe('-12.05');
  });

  it('computes the debit and credit totals and the difference', () => {
    expect(balance([{ side: 1, amountRon: '0.10' }, { side: 1, amountRon: '0.20' }, { side: 2, amountRon: '0.30' }])).toEqual({ debit: 30, credit: 30, difference: 0 });
    expect(balance([{ side: 1, amountRon: '100' }, { side: 2, amountRon: '90' }]).difference).toBe(1000);
  });

  it('writes the entry the way a Romanian accountant does', () => {
    expect(formula([{ accountCode: '4111', side: 1, amountRon: '15004' }, { accountCode: '707', side: 2, amountRon: '12400' },
      { accountCode: '4427', side: 2, amountRon: '2604' }])).toEqual(['4111 = %  15.004,00', '    707  12.400,00', '    4427  2.604,00']);
    expect(formula([{ accountCode: '628', side: 1, amountRon: '100' }, { accountCode: '462', side: 2, amountRon: '100' }])).toEqual(['628 = 462  100,00']);
    expect(formula([{ accountCode: '628', side: 1, amountRon: '100' }, { accountCode: '4426', side: 1, amountRon: '19' },
      { accountCode: '401', side: 2, amountRon: '119' }])[0]).toBe('% = 401  119,00');
    expect(formula([{ accountCode: '628', side: 1, amountRon: '100' }])).toEqual([]);
  });
});
