import { describe, expect, it } from 'vitest';
import { advanceStatusLabel, isTrip, Justification, Purpose, reportStatusLabel } from './advanceApi';
import { toReportBody } from './ExpenseReportsPage';

describe('avansuri și deconturi (machetele f1-05, f1-06)', () => {
  it('stările și scopurile au etichetele lor', () => {
    expect(advanceStatusLabel(0)).toBe('advance.status.toPay');
    expect(advanceStatusLabel(4)).toBe('advance.status.overdue');
    expect(reportStatusLabel(3)).toBe('report.status.approved');
    expect([isTrip(Purpose.DomesticTrip), isTrip(Purpose.Purchase)]).toEqual([true, false]);
  });

  it('diurna trimite zilele, nu suma; celelalte linii trimit suma și TVA-ul', () => {
    const body = toReportBody({
      employeeId: 9, cashAdvanceId: 4, documentDate: '13.11.2026', description: '',
      lines: [
        { expenseTypeId: 1, justificationKind: Justification.Receipt, documentNo: 'BF 0231', amount: '310.00', vatAmount: '53.80', days: '' },
        { expenseTypeId: 8, justificationKind: Justification.Receipt, documentNo: '', amount: '', vatAmount: '', days: '3' },
      ],
    }, undefined, new Set([8]));
    expect(body).toMatchObject({ id: null, employeeId: 9, cashAdvanceId: 4, documentDate: '2026-11-13', description: null });
    expect(body.lines[0]).toMatchObject({ justificationKind: Justification.Receipt, amount: '310.00', vatAmount: '53.80', days: null, documentNo: 'BF 0231' });
    expect(body.lines[1]).toMatchObject({ justificationKind: Justification.PerDiem, amount: null, vatAmount: null, days: '3' });
  });
});

describe('configurarea trezoreriei (machetele cf-01…cf-03)', () => {
  it('plafonul introdus românește devine sumă invariantă', async () => {
    const { parseAmount } = await import('./TreasurySettingsTab');
    expect(parseAmount('5.000,50')).toBe('5000.50');
    expect(parseAmount('3000')).toBe('3000');
    expect(parseAmount('abc')).toBeNull();
  });
});
