import { describe, expect, it } from 'vitest';
import { defaultsToSave } from './DefaultDimensions';

describe('dimensiunile implicite', () => {
  it('trimit doar tipurile cu regulă, iar regula fixă fără valoare e refuzată', () => {
    const rows = [
      { dimensionType: 'COST_CENTER', typeName: 'Centre de cost', valueId: 4, posting: 3 },
      { dimensionType: 'PROJECT', typeName: 'Proiecte', valueId: null, posting: 2 },
      { dimensionType: 'SITE', typeName: 'Șantiere', valueId: null, posting: null },
    ];
    expect(defaultsToSave(rows)).toEqual([
      { dimensionType: 'COST_CENTER', dimensionValueId: 4, valuePosting: 3 },
      { dimensionType: 'PROJECT', dimensionValueId: null, valuePosting: 2 },
    ]);
    expect(defaultsToSave([{ dimensionType: 'SITE', typeName: 'Șantiere', valueId: null, posting: 3 }])).toBe('Șantiere');
  });
});
