import { describe, expect, it } from 'vitest';
import { filterLines, LineStatus, remainder, type ReconciliationLine } from './bankApi';

const line = (id: number, amount: string, matchStatus: number) => ({ id, amount, matchStatus }) as ReconciliationLine;

describe('bankApi', () => {
  it('computes the remainder to explain in cents, from invariant or Romanian amounts', () => {
    expect(remainder('9850.00', [{ amount: '10000' }, { amount: '-150,00' }])).toBe('0.00');
    expect(remainder('-12.40', [{ amount: '-10' }])).toBe('-2.40');
    expect(remainder('0.30', [{ amount: '0.10' }, { amount: '0.20' }])).toBe('0.00');
    expect(remainder('100', [])).toBe('100.00');
  });

  it('filters the open lines by direction and status', () => {
    const lines = [line(1, '10.00', LineStatus.Unmatched), line(2, '-5.00', LineStatus.Proposed), line(3, '7.00', LineStatus.Explained)];
    expect(filterLines(lines, 'in').map((l) => l.id)).toEqual([1, 3]);
    expect(filterLines(lines, 'out').map((l) => l.id)).toEqual([2]);
    expect(filterLines(lines, 'proposed').map((l) => l.id)).toEqual([2]);
    expect(filterLines(lines, 'explained').map((l) => l.id)).toEqual([3]);
    expect(filterLines(lines, 'all')).toHaveLength(3);
  });
});
