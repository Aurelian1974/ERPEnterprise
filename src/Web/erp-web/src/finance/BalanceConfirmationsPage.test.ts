import { describe, expect, it } from 'vitest';
import { filterConfirmations } from './BalanceConfirmationsPage';

describe('filterConfirmations', () => {
  const items = [0, 1, 2, 3, 4].map((status) => ({ id: status, status }));

  it('pune la „fără răspuns” confirmările generate, trimise și marcate fără răspuns', () => {
    expect(filterConfirmations(items, 'unanswered').map((c) => c.id)).toEqual([0, 1, 4]);
  });

  it('separă confirmatele și contestatele; „toate” le păstrează pe toate', () => {
    expect(filterConfirmations(items, 'confirmed').map((c) => c.id)).toEqual([2]);
    expect(filterConfirmations(items, 'contested').map((c) => c.id)).toEqual([3]);
    expect(filterConfirmations(items, 'all')).toHaveLength(5);
  });
});
