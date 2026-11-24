import { describe, expect, it } from 'vitest';
import { conditionParts, moveRule, sourceText, targetText, type MappingRule } from './mappingModel';

const rule = (id: number, priority: number, extra: Partial<MappingRule> = {}): MappingRule => ({
  id, priority, sourceKind: 1, sourceAccountId: 1, sourceAccountCode: '628', sourceFrom: null, sourceTo: null, sourceAccounts: [], companyId: null, side: 0,
  partnerKind: null, partnerGroupId: null, partnerId: null, partnerName: null, dimensionType: null, dimensionCondition: 0, dimensionValueId: null,
  dimensionValueCode: null, dimensionValueName: null, itemGroupId: null, currencyCode: null, balanceSign: 0, validFrom: '2026-01-01', validTo: null,
  isActive: true, note: null, rowVersion: 'AA==', targets: [{ accountId: 9, code: '6900', name: 'Other', percent: '100' }], ...extra,
});

describe('mapping rules', () => {
  it('describes the source, the targets and the conditions', () => {
    expect(sourceText(rule(1, 10))).toBe('628');
    expect(sourceText(rule(1, 10, { sourceKind: 2, sourceFrom: '6000', sourceTo: '6999' }))).toBe('6000 – 6999');
    expect(sourceText(rule(1, 10, { sourceKind: 3, sourceAccounts: [{ accountId: 1, code: '4111' }, { accountId: 2, code: '413' }] }))).toBe('4111, 413');
    expect(targetText(rule(1, 10, { targets: [{ accountId: 1, code: '6110', name: 'L', percent: '60' }, { accountId: 2, code: '6400', name: 'A', percent: '40' }] })))
      .toBe('6110 · 60%  ·  6400 · 40%');
    expect(conditionParts(rule(1, 10, { dimensionCondition: 1, dimensionType: 'COST_CENTER', dimensionValueName: 'Logistică', balanceSign: 0 })))
      .toEqual([{ key: 'chart.dim.COST_CENTER', value: 'Logistică' }]);
  });

  it('moves a rule one place keeping the existing priorities', () => {
    expect(moveRule([rule(1, 60), rule(2, 61), rule(3, 69)], 2, -1)).toEqual([{ id: 2, priority: 60 }, { id: 1, priority: 61 }, { id: 3, priority: 69 }]);
    expect(moveRule([rule(1, 60), rule(2, 61)], 1, -1)).toEqual([{ id: 1, priority: 60 }, { id: 2, priority: 61 }]);
  });
});
