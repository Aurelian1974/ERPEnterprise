import { describe, expect, it } from 'vitest';
import { addDays, dayLabel, notes, payable } from './DueSchedulePage';

describe('due schedule helpers', () => {
  it('labels days and adds days without time zones', () => {
    expect(dayLabel('2026-11-16')).toBe('Lu 16.11');
    expect(dayLabel('2026-11-22')).toBe('Du 22.11');
    expect(addDays('2026-11-23', 13)).toBe('2026-12-06');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
  });

  it('lists the observations and refuses blocked or disputed invoices for payment orders', () => {
    const base = { anafInactive: false, eInvoiceStatus: 4, spvExempt: false, isDisputed: false, isBlockedForPayment: false, partnerBlocked: false,
      partnerBankAccountId: 3 };
    expect(notes(base)).toEqual([]);
    expect(notes({ ...base, anafInactive: true, eInvoiceStatus: 5 })).toEqual(['due.note.anaf', 'settle.einv.missing']);
    expect(notes({ ...base, eInvoiceStatus: 5, spvExempt: true, partnerBankAccountId: null })).toEqual(['due.note.noAccount']);
    expect(payable(base)).toBe(true);
    expect(payable({ ...base, partnerBlocked: true })).toBe(false);
  });
});
