import { describe, expect, it } from 'vitest';
import { moveRule } from './MatchingRulesPage';

describe('moveRule', () => {
  it('moves a rule before another or to the end', () => {
    expect(moveRule([1, 2, 3, 4], 4, 2)).toEqual([1, 4, 2, 3]);
    expect(moveRule([1, 2, 3, 4], 1, 4)).toEqual([2, 3, 1, 4]);
    expect(moveRule([1, 2, 3, 4], 2, undefined)).toEqual([1, 3, 4, 2]);
  });
});
