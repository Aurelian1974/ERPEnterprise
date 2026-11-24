import { describe, expect, it } from 'vitest';
import { calendarSummary, d300Sections, monthRange, pairRows, previousMonth } from './taxModel';

const line = (rowCode: string, columnCode: string, value: string) =>
  ({ rowCode, columnCode, label: rowCode, isManual: false, calculatedValue: value, manualValue: null, manualReason: null });

describe('taxModel', () => {
  it('computes month ranges and the previous month', () => {
    expect(monthRange('2026-02')).toEqual({ from: '2026-02-01', to: '2026-02-28' });
    expect(previousMonth('2026-01-15')).toBe('2025-12');
  });

  it('splits D300 lines into sections and pairs base with VAT', () => {
    const lines = [line('L-S21', 'BASE', '1000.00'), line('L-S21', 'VAT', '210.00'), line('A-S21', 'VAT', '95.00'), line('TOTAL', 'DUE', '115.00')];
    const s = d300Sections(lines);
    expect(pairRows(s.sales)).toEqual([{ rowCode: 'L-S21', label: 'L-S21', base: '1000.00', vat: '210.00' }]);
    expect(pairRows(s.purchases)[0]?.base).toBe('0.00');
    expect(s.totals).toHaveLength(1);
  });

  it('counts the calendar items', () => {
    const item = (status: number | null, needsReview = false) =>
      ({ formCode: 'D300', formName: '', periodFrom: '', periodTo: '', dueDate: '', returnId: null, status, needsReview, amountDue: null });
    expect(calendarSummary([item(null), item(3), item(4, true)])).toEqual({ toFile: 2, review: 1, ready: 1, filed: 1 });
  });
});
