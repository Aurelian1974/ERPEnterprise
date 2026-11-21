import { describe, expect, it } from 'vitest';
import { combinationCount } from './variantsApi';

describe('variantele articolului', () => {
  it('numărul de variante e produsul valorilor alese pe atribut', () => {
    const values = [
      { id: 1, attributeId: 10, code: 'S', name: 'S', sortOrder: 1, isActive: true },
      { id: 2, attributeId: 10, code: 'M', name: 'M', sortOrder: 2, isActive: true },
      { id: 3, attributeId: 10, code: 'L', name: 'L', sortOrder: 3, isActive: true },
      { id: 4, attributeId: 20, code: 'ROSU', name: 'Roșu', sortOrder: 1, isActive: true },
      { id: 5, attributeId: 20, code: 'ALB', name: 'Alb', sortOrder: 2, isActive: true },
    ];
    expect(combinationCount(values, new Set([1, 2, 4, 5]))).toBe(4);
    expect(combinationCount(values, new Set([1, 2, 3]))).toBe(3);
    expect(combinationCount(values, new Set())).toBe(0);
  });
});
