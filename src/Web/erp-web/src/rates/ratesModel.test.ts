import { describe, expect, it } from 'vitest';
import { dayLabel, daysDesc, formatRate, nextBankingDay, rateRows } from './ratesModel';

const rate = (currencyCode: string, rateDate: string, value: string) =>
  ({ currencyCode, rateDate, rate: value, multiplier: currencyCode === 'HUF' ? 100 : 1, importedAt: `${rateDate}T11:04:00Z` });

describe('cursurile valutare', () => {
  it('zilele intervalului merg de la cea mai nouă și au numele zilei', () => {
    expect(daysDesc('2026-11-12', '2026-11-14')).toEqual(['2026-11-14', '2026-11-13', '2026-11-12']);
    expect(dayLabel('2026-11-13')).toBe('Vineri 13.11.2026');
  });

  it('documentele unei zile iau ultimul curs dinaintea ei: vineri pentru sâmbătă, duminică și luni', () => {
    const rows = rateRows({
      from: '2026-11-07', to: '2026-11-10',
      bnrRates: [rate('EUR', '2026-11-09', '5.0798'), rate('HUF', '2026-11-09', '1.3119')],
      previousRates: [rate('EUR', '2026-11-06', '5.079')],
    });
    expect(rows.map((r) => [r.day, r.rates.size, r.documentsRateDay])).toEqual([
      ['2026-11-10', 0, '2026-11-09'],
      ['2026-11-09', 2, '2026-11-06'],
      ['2026-11-08', 0, '2026-11-06'],
      ['2026-11-07', 0, '2026-11-06'],
    ]);
  });

  it('cursul are virgulă și cel puțin 4 zecimale; după vineri urmează luni', () => {
    expect(formatRate('5.0812')).toBe('5,0812');
    expect(formatRate('4.41')).toBe('4,4100');
    expect(formatRate('1.313125')).toBe('1,313125');
    expect(nextBankingDay('2026-11-13')).toBe('2026-11-16');
  });
});
