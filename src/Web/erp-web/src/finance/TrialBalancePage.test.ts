import { describe, expect, it } from 'vitest';
import { sideBalance } from './AccountLedgerPage';
import { accountClass, hasClosing } from './TrialBalancePage';

describe('trial balance and ledger', () => {
  it('groups accounts by class and keeps rows with a closing balance', () => {
    expect(accountClass('4111.01')).toBe('4');
    expect(accountClass('')).toBe('');
    expect(hasClosing({ closingDebit: '0.00', closingCredit: '12.00' })).toBe(true);
    expect(hasClosing({ closingDebit: '0.00', closingCredit: '0.00' })).toBe(false);
  });

  it('shows a running balance with its side', () => {
    expect(sideBalance('38940.00')).toBe('38.940,00 D');
    expect(sideBalance('-300.50')).toBe('300,50 C');
    expect(sideBalance('0.00')).toBe('0,00');
  });
});
