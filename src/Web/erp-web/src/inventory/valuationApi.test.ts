import { describe, expect, it } from 'vitest';
import { adjustmentKindLabel, allocationBasisLabel, moneyForApi, moneyToRo, monthStart } from './valuationApi';

describe('evaluarea stocurilor (02.05, S3)', () => {
  it('documentele SM și DA privesc prima zi a lunii', () => {
    expect(monthStart('2026-11')).toBe('2026-11-01');
    expect(monthStart('2026-12-17')).toBe('2026-12-01');
  });

  it('sumele se trimit invariant, cu cel mult două zecimale și semn', () => {
    expect(moneyForApi('1.840,00'.replace('.', ''))).toBe('1840.00');
    expect(moneyForApi('-312,4')).toBe('-312.4');
    expect(moneyForApi('12,345')).toBeNull();
    expect(moneyForApi('abc')).toBeNull();
  });

  it('sumele se afișează românește', () => {
    expect(moneyToRo('31754.38')).toBe('31.754,38');
    expect(moneyToRo('-312.40')).toBe('−312,40');
    expect(moneyToRo(null)).toBe('—');
  });

  it('etichetele tipurilor', () => {
    expect(allocationBasisLabel(3)).toBe('inv.landed.basis.weight');
    expect(adjustmentKindLabel(2)).toBe('inv.adjustment.negative');
  });
});
