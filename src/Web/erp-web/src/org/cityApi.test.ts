import { describe, expect, it } from 'vitest';
import { cityLabel } from './cityApi';

describe('lista de localități SIRUTA', () => {
  it('arată unitatea căreia îi aparține satul și codul poștal', () => {
    expect(cityLabel({ sirutaCode: 1286, name: 'Gârbova de Jos', kind: 11, parentName: 'Aiud', parentKind: 4, postalCode: '515204' }))
      .toBe('Gârbova de Jos · mun. Aiud · 515204');
    expect(cityLabel({ sirutaCode: 4508, name: 'Cărpiniș', kind: 23, parentName: 'Gârbova', parentKind: 3, postalCode: '517306' }))
      .toBe('Cărpiniș · com. Gârbova · 517306');
  });

  it('reședința nu repetă numele unității, iar sectoarele arată Bucureștiul fără prescurtare', () => {
    expect(cityLabel({ sirutaCode: 1026, name: 'Alba Iulia', kind: 9, parentName: 'Alba Iulia', parentKind: 1, postalCode: '510005' }))
      .toBe('Alba Iulia · 510005');
    expect(cityLabel({ sirutaCode: 179141, name: 'Sectorul 1', kind: 6, parentName: 'București', parentKind: 9, postalCode: null }))
      .toBe('Sectorul 1 · București');
  });
});
