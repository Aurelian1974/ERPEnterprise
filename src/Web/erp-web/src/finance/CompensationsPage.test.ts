import { describe, expect, it } from 'vitest';
import { balanceOf } from './CompensationsPage';

describe('balanceOf', () => {
  it('sums the lines in cents; a balanced PV gives zero', () => {
    expect(balanceOf([{ amount: '1530.00' }, { amount: '11370.00' }, { amount: '-8400.00' }, { amount: '-4500.00' }])).toBe(0);
    expect(balanceOf([{ amount: '300.10' }, { amount: '-250.05' }])).toBe(5005);
  });
});
