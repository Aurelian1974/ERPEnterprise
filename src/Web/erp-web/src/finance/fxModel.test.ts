import { describe, expect, it } from 'vitest';
import { formatAmount } from './cashApi';
import { monthLabel, previousMonth, rateText, signed, signClass } from './fxModel';

describe('fxModel', () => {
  it('scrie luna, semnul diferentei si cursul ca in machete', () => {
    expect(monthLabel(2026, 10)).toBe('octombrie 2026');
    expect(signed('335.29')).toBe('+335,29');
    expect(signed('-1204.16')).toBe(formatAmount('-1204.16'));
    expect(signClass('-1')).toBe('num loss');
    expect(signClass('0.00')).toBe('num');
    expect(rateText('5.079')).toBe('5,0790');
  });

  it('propune luna trecuta, inclusiv in ianuarie', () => {
    expect(previousMonth('2026-11-24')).toEqual({ year: 2026, month: 10 });
    expect(previousMonth('2027-01-05')).toEqual({ year: 2026, month: 12 });
  });
});

