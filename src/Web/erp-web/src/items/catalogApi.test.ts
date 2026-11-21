import { describe, expect, it } from 'vitest';
import { cleanTranslations } from './catalogApi';

describe('traducerile articolului', () => {
  it('se trimit fără rândurile goale, cu limba în litere mici', () => {
    expect(cleanTranslations([{ languageCode: 'EN', name: ' Cable ' }, { languageCode: 'de', name: '  ' }])).toEqual([{ languageCode: 'en', name: 'Cable' }]);
  });

  it('o limbă repetată nu se trimite', () => {
    expect(cleanTranslations([{ languageCode: 'en', name: 'A' }, { languageCode: 'EN', name: 'B' }])).toBe('duplicate');
  });
});
