import { describe, expect, it } from 'vitest';
import { rateText, totalsText } from './PenaltiesPage';

describe('totalsText', () => {
  it('afișează leii fără monedă și celelalte monede cu codul, ca în macheta f3-08', () => {
    expect(totalsText([{ currencyCode: 'RON', total: '822.38' }, { currencyCode: 'EUR', total: '50.49' }])).toBe('822,38 + 50,49 EUR');
    expect(totalsText([])).toBe('0,00');
  });
});

describe('rateText', () => {
  it('arată rata din contract pe zi și dobânda legală pe an', () => {
    expect(rateText({ kind: 1, ratePerDay: '0.1', annualRate: null })).toBe('0,10%/zi contract');
    expect(rateText({ kind: 1, ratePerDay: null, annualRate: '14.5' })).toBe('14,50%/an dobândă legală');
    expect(rateText({ kind: 2, ratePerDay: null, annualRate: null })).toBe('Sumă fixă de recuperare (Legea 72/2013)');
  });
});
