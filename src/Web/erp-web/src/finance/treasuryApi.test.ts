import { describe, expect, it } from 'vitest';
import { groupAccounts, proposedGlAccount, TreasuryKind, type TreasuryItem } from './treasuryApi';
import { toSaveBody } from './TreasuryAccountsPage';

function item(id: number, kind: number, isActive = true): TreasuryItem {
  return { id, kind, code: `C${id}`, name: `Cont ${id}`, branchId: null, branchName: null, currencyCode: 'RON', glAccountCode: null,
    companyBankAccountId: null, bankName: null, iban: null, isActive, cashierCount: 0 };
}

describe('conturile de trezorerie (macheta f1-01)', () => {
  const accounts = [item(1, TreasuryKind.Cash), item(2, TreasuryKind.Bank), item(3, TreasuryKind.Transit, false), item(4, TreasuryKind.Cash)];

  it('lista e grupată pe tip, în ordinea băncilor, caselor, cardurilor și tranzitului', () => {
    expect(groupAccounts(accounts, 'all').map((g) => [g.kind, g.accounts.map((a) => a.id)])).toEqual([
      [TreasuryKind.Bank, [2]], [TreasuryKind.Cash, [1, 4]], [TreasuryKind.Transit, [3]],
    ]);
    expect(groupAccounts(accounts, TreasuryKind.Cash).map((g) => g.kind)).toEqual([TreasuryKind.Cash]);
    expect(groupAccounts(accounts, 'inactive').flatMap((g) => g.accounts.map((a) => a.id))).toEqual([3]);
  });

  it('simbolul contabil propus urmează tipul și moneda, ca pe server', () => {
    expect(proposedGlAccount(TreasuryKind.Cash, 'RON')).toBe('5311');
    expect(proposedGlAccount(TreasuryKind.Cash, 'EUR')).toBe('5314');
    expect(proposedGlAccount(TreasuryKind.Bank, 'EUR')).toBe('5124');
    expect(proposedGlAccount(TreasuryKind.Deposit, 'RON')).toBeUndefined();
  });

  it('salvarea trimite datele în format ISO, iar câmpurile de casă doar pentru case', () => {
    const values = {
      kind: TreasuryKind.Cash, code: 'CASA-CLUJ', name: 'Casa PL Cluj', branchId: 7, currencyCode: 'RON', companyBankAccountId: 3, glAccountCode: '',
      responsibleUserId: 9, receiptSeriesId: 11, cashInSeriesId: null, cashOutSeriesId: null, maxClosingBalance: '40000.00', closeMode: 2,
      openingDate: '01.01.2026', isActive: true, cashiers: [{ userId: 9, canValidate: true, validFrom: '01.11.2026', validTo: '' }],
    };
    expect(toSaveBody(values, undefined)).toMatchObject({
      id: null, rowVersion: null, companyBankAccountId: null, glAccountCode: null, responsibleUserId: 9, maxClosingBalance: '40000.00', closeMode: 2,
      openingDate: '2026-01-01', cashiers: [{ userId: 9, canValidate: true, validFrom: '2026-11-01', validTo: null }],
    });
    expect(toSaveBody({ ...values, kind: TreasuryKind.Bank }, undefined)).toMatchObject({
      companyBankAccountId: 3, responsibleUserId: null, receiptSeriesId: null, maxClosingBalance: null, closeMode: 1, cashiers: [],
    });
  });
});
