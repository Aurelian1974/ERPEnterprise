import { describe, expect, it } from 'vitest';
import { amountColumns, CashKind, filterDay, formatAmount, operationsFor, statusLabel, type CashDayDocument, type CashOperationType } from './cashApi';
import { toCashBody } from './CashDocumentPage';

function doc(id: number, kind: number, status: number, totalAmount = '10.00', reversalOfId: number | null = null): CashDayDocument {
  return { id, kind, number: null, status, personName: null, operationName: null, totalAmount, description: null, reversalOfId, reversedById: null,
    validatedAt: null };
}

function op(id: number, direction: number): CashOperationType {
  return { id, code: `OP${id}`, name: `Operațiune ${id}`, direction, requiresPartner: false, requiresEmployee: false, requiresCounterAccount: false,
    cashLimitCategory: null };
}

describe('casa (machetele f1-02, f1-03)', () => {
  it('sumele invariante se afișează românește, fără calcule cu number', () => {
    expect(formatAmount('20982.5')).toBe('20.982,50');
    expect(formatAmount('-640.00')).toBe('−640,00');
    expect(formatAmount('1234567.89')).toBe('1.234.567,89');
    expect(formatAmount(undefined)).toBe('');
  });

  it('filtrul zilei separă încasările, plățile și ciornele; suma stă pe coloana sensului', () => {
    const reversal = doc(4, CashKind.CashOut, 2, '-5.00', 2);
    const day = [doc(1, CashKind.Receipt, 2), doc(2, CashKind.CashOut, 2), doc(3, CashKind.CashIn, 1), reversal];
    expect(filterDay(day, 'in').map((d) => d.id)).toEqual([1, 3]);
    expect(filterDay(day, 'out').map((d) => d.id)).toEqual([2, 4]);
    expect(filterDay(day, 'drafts').map((d) => d.id)).toEqual([3]);
    expect(amountColumns(reversal)).toEqual({ cashIn: undefined, cashOut: '-5.00' });
    expect(statusLabel(reversal)).toBe('cash.status.reversal');
  });

  it('liniile oferă doar tipurile de operațiuni cu sensul documentului', () => {
    const types = [op(1, 1), op(2, 2), op(3, 1)];
    expect(operationsFor(types, CashKind.Receipt).map((o) => o.id)).toEqual([1, 3]);
    expect(operationsFor(types, CashKind.CashOut).map((o) => o.id)).toEqual([2]);
  });

  it('salvarea trimite doar plătitorul ales, data ISO și liniile; soldul inițial nu are linii', () => {
    const values = {
      kind: CashKind.Receipt, treasuryAccountId: 3, documentDate: '13.11.2026', payer: 'person' as const, partnerId: 12, employeeId: null,
      personName: 'Popescu Maria', personIdDocument: 'CI XB 123456', description: '', openingAmount: '',
      lines: [{ operationTypeId: 1, amount: '412.50', counterTreasuryAccountId: null, settlementRef: 'FV EXD 10244', note: '' }],
    };
    expect(toCashBody(values, undefined)).toMatchObject({
      id: null, documentDate: '2026-11-13', partnerId: null, personName: 'Popescu Maria', personIdDocument: 'CI XB 123456', description: null,
      openingAmount: null, lines: [{ operationTypeId: 1, amount: '412.50', settlementRef: 'FV EXD 10244', note: null }],
    });
    expect(toCashBody({ ...values, kind: CashKind.OpeningBalance, payer: 'none', openingAmount: '1500,5' }, undefined)).toMatchObject({
      personName: null, openingAmount: '1500.5', lines: [],
    });
  });
});
