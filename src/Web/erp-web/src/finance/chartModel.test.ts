import { describe, expect, it } from 'vitest';
import { nextAnalyticCode, visibleRows, type GlAccountItem } from './chartModel';

const acc = (id: number, code: string, parentId: number | null, extra: Partial<GlAccountItem> = {}): GlAccountItem => ({
  id, code, name: `Cont ${code}`, parentId, level: Math.min(code.length, 4), accountType: 1, isPostable: true, companyId: null, isOffBalance: false,
  isManagement: false, statutoryReporting: true, currencyMode: 0, currencyCode: null, partyRequirement: 0, manualPosting: true, saftAccountCode: code,
  taxTreatment: 1, closingTarget: 0, validFrom: null, validTo: null, isSystem: true, isActive: true, ...extra,
});

const plan = [
  acc(1, '5', null), acc(2, '51', 1), acc(3, '512', 2), acc(4, '5121', 3), acc(5, '5121.01', 4, { isSystem: false, name: 'BCR RON' }),
  acc(6, '6', null), acc(7, '62', 6), acc(8, '628', 7, { isActive: false }),
];

describe('visibleRows', () => {
  it('shows only the roots until a branch is opened', () => {
    expect(visibleRows(plan, new Set(), '', 'all').map((r) => r.account.code)).toEqual(['5', '6']);
    expect(visibleRows(plan, new Set([1, 2]), '', 'all').map((r) => [r.account.code, r.depth])).toEqual([['5', 0], ['51', 1], ['512', 2], ['6', 0]]);
  });

  it('keeps the path to matching accounts and opens it when searching or filtering', () => {
    expect(visibleRows(plan, new Set(), 'bcr', 'all').map((r) => r.account.code)).toEqual(['5', '51', '512', '5121', '5121.01']);
    expect(visibleRows(plan, new Set(), '', 'analytics').map((r) => r.account.code)).toEqual(['5', '51', '512', '5121', '5121.01']);
    expect(visibleRows(plan, new Set(), '', 'inactive').map((r) => r.account.code)).toEqual(['6', '62', '628']);
    expect(visibleRows(plan, new Set(), '62', 'all').map((r) => r.account.code)).toEqual(['6', '62', '628']);
  });
});

describe('nextAnalyticCode', () => {
  it('proposes the first free two-digit suffix', () => {
    expect(nextAnalyticCode('5121', plan)).toBe('5121.02');
    expect(nextAnalyticCode('401', plan)).toBe('401.01');
  });
});
