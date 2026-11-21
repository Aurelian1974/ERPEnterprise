import { describe, expect, it } from 'vitest';
import { formatNumber, previewLine } from './precisionApi';

describe('previzualizarea preciziei', () => {
  it('reproduce exemplul din macheta: 3,250 × 12,345678 dă 40,14 cu 2 zecimale și 40,12 cu 4 sau 6', () => {
    expect(previewLine(3.25, 12.345678, 2, 3).amount).toBe(40.14);
    expect(previewLine(3.25, 12.345678, 4, 3).amount).toBe(40.12);
    expect(previewLine(3.25, 12.345678, 6, 3).amount).toBe(40.12);
    expect(formatNumber(12.3457, 4)).toBe('12,3457');
  });
});
