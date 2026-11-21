import { describe, expect, it } from 'vitest';
import { ownerFields, ownerFilters, ownerKey, ownerQuery, type OwnerOptions } from './owner';

const options = (manageWholeCompany: boolean): OwnerOptions => ({
  companyId: 3, companyName: 'Exemplu SRL', viewWholeCompany: manageWholeCompany, manageWholeCompany,
  nodes: [
    { branchId: 5, locationId: null, parentBranchId: null, code: 'N', name: 'Punct Nord', isActive: true, canView: true, canManage: true },
    { branchId: null, locationId: 9, parentBranchId: 5, code: 'M', name: 'Magazin', isActive: true, canView: true, canManage: true },
    { branchId: 6, locationId: null, parentBranchId: null, code: 'S', name: 'Punct Sud', isActive: true, canView: false, canManage: false },
  ],
});

describe('proprietarul articolelor și partenerilor', () => {
  it('cheia trece în câmpurile comenzii și înapoi', () => {
    for (const [level, node] of [[0, null], [1, null], [2, 5], [3, 9]] as const) {
      const fields = ownerFields(ownerKey(level, node));
      expect([fields.ownerLevel, fields.ownerNodeId]).toEqual([level, node]);
    }
  });

  it('filtrul listei arată doar nodurile vizibile și ajunge în parametrii căutării', () => {
    expect(ownerFilters(options(false)).map((c) => c.value)).toEqual(['common', 'company', 'b5', 'l9']);
    expect(ownerFilters(options(true), 'l9').map((c) => c.value)).toEqual(['common', 'company', 'b5', 'l9']);
    expect(ownerFilters(options(true), 'b6').map((c) => c.value)).toEqual(['common', 'company']);
    expect(ownerQuery('common')).toEqual({ Owner: 1 });
    expect(ownerQuery('company')).toEqual({ Owner: 2 });
    expect(ownerQuery('b5')).toEqual({ OwnerBranchId: 5 });
    expect(ownerQuery('l9')).toEqual({ OwnerLocationId: 9 });
    expect(ownerQuery('')).toEqual({});
  });
});
