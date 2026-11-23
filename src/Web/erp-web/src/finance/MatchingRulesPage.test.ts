import { describe, expect, it } from 'vitest';
import { moveRule, testLineStatus } from './MatchingRulesPage';

describe('moveRule', () => {
  it('moves a rule before another or to the end', () => {
    expect(moveRule([1, 2, 3, 4], 4, 2)).toEqual([1, 4, 2, 3]);
    expect(moveRule([1, 2, 3, 4], 1, 4)).toEqual([2, 3, 1, 4]);
    expect(moveRule([1, 2, 3, 4], 2, undefined)).toEqual([1, 3, 4, 2]);
  });
});

describe('testLineStatus', () => {
  const line = { matchStatus: 1, explainedAs: null, explainedByRuleName: null, takenByRuleName: null } as const;
  it('says what would happen to each line', () => {
    expect(testLineStatus(line, 'Comisioane')).toBe('ar fi explicată de regulă');
    expect(testLineStatus({ ...line, takenByRuleName: 'Taxe' }, 'Comisioane')).toBe('prinsă înainte de „Taxe”');
    expect(testLineStatus({ ...line, matchStatus: 3, explainedAs: 'BANK-COMISION', explainedByRuleName: 'Altă regulă' }, 'Comisioane'))
      .toBe('deja explicată: BANK-COMISION');
    expect(testLineStatus({ ...line, matchStatus: 4, explainedAs: 'BANK-COMISION', explainedByRuleName: 'Comisioane' }, 'Comisioane'))
      .toBe('ar fi explicată de regulă');
    expect(testLineStatus({ ...line, matchStatus: 4, explainedAs: 'PLATA' }, undefined)).toBe('deja validată: PLATA');
  });
});
