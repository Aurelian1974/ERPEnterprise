import { describe, expect, it } from 'vitest';
import { parentOptions, type DimensionValue } from './dimensionsApi';

const value = (id: number, parentId: number | null, companyId = 3, isActive = true) =>
  ({ id, parentId, companyId, isActive, code: `V${id}`, name: `V${id}` }) as DimensionValue;

describe('dimensiunile', () => {
  it('părintele e din aceeași firmă, activ, și nu e valoarea însăși sau un descendent al ei', () => {
    const values = [value(1, null), value(2, 1), value(3, 2), value(4, null, 9), value(5, null, 3, false), value(6, null)];
    expect(parentOptions(values, 3, 1).map((v) => v.id)).toEqual([6]);
    expect(parentOptions(values, 3, undefined).map((v) => v.id)).toEqual([1, 2, 3, 6]);
  });
});
