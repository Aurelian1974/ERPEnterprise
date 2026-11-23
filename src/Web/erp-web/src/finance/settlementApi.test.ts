import { describe, expect, it } from 'vitest';
import { agingShares, balanceParams, overdueDays, Role } from './settlementApi';

describe('balanceParams', () => {
  it('maps the screen filter to the API query', () => {
    expect(balanceParams({ asOf: '2026-11-13', filter: 'customers', search: '' })).toEqual({ asOf: '2026-11-13', role: Role.Customer });
    expect(balanceParams({ asOf: '2026-11-13', filter: 'overdue', search: ' mega ' })).toEqual({ asOf: '2026-11-13', search: 'mega', role: Role.Customer,
      onlyOverdue: true });
    expect(balanceParams({ asOf: '2026-11-13', filter: 'suppliers', search: '' })).toEqual({ asOf: '2026-11-13', role: Role.Supplier });
    expect(balanceParams({ asOf: '2026-11-13', filter: 'both', search: '' })).toEqual({ asOf: '2026-11-13', bothRoles: true });
    expect(balanceParams({ asOf: '2026-11-13', filter: 'unallocated', search: '' })).toEqual({ asOf: '2026-11-13', onlyUnallocated: true });
  });
});

describe('agingShares', () => {
  it('splits the balance on aging buckets, supplier balances included', () => {
    expect(agingShares({ currentRon: '400.00', days30Ron: '0.00', days60Ron: '600.00', over60Ron: '0.00' })).toEqual([40, 0, 60, 0]);
    expect(agingShares({ currentRon: '-300.00', days30Ron: '0.00', days60Ron: '0.00', over60Ron: '0.00' })).toEqual([100, 0, 0, 0]);
    expect(agingShares({ currentRon: '0.00', days30Ron: '0.00', days60Ron: '0.00', over60Ron: '0.00' })).toEqual([0, 0, 0, 0]);
  });
});

describe('overdueDays', () => {
  it('counts the days after the due date only', () => {
    expect(overdueDays('2026-10-29', '2026-11-13')).toBe(15);
    expect(overdueDays('2026-11-13', '2026-11-13')).toBe(0);
    expect(overdueDays('2026-11-20', '2026-11-13')).toBe(0);
  });
});
