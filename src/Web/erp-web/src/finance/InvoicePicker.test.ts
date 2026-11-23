import { describe, expect, it } from 'vitest';
import { invoiceChoices, invoiceText } from './InvoicePicker';
import type { OpenItem } from './settlementApi';

const item = (id: number, role: number, kind: number, open: string, extra: Partial<OpenItem> = {}): OpenItem => ({ openItemId: id, role, kind,
  sourceDocumentType: 'Sales.Invoice', sourceDocumentId: id, documentNo: `F-${id}`, documentDate: '2026-11-01', dueDate: '2026-11-15', currencyCode: 'RON',
  amount: open, openAmount: open, isDisputed: false, disputeNote: null, isBlockedForPayment: false, eInvoiceStatus: 2, isVatOnCollection: false, vatAmount: '0.00',
  vatNonExigibleRon: '0.00', closedAt: null, rowVersion: 'AA==', ...extra });

describe('invoiceChoices', () => {
  it('offers the receivables of a customer or the payables of a supplier, never payments', () => {
    const items = [item(1, 1, 1, '100.00'), item(2, 1, 5, '-50.00'), item(3, 1, 2, '-20.00'), item(4, 2, 1, '-300.00'), item(5, 2, 5, '40.00')];
    expect(invoiceChoices(items, 1).map((i) => i.openItemId)).toEqual([1]);
    expect(invoiceChoices(items, 2).map((i) => i.openItemId)).toEqual([4]);
  });

  it('labels an invoice with its rest and due date', () => {
    expect(invoiceText(item(4, 2, 1, '-1300.50'))).toBe('F-4 · rest 1.300,50 · scad. 15.11.2026');
    expect(invoiceText(item(1, 1, 1, '10.00', { isDisputed: true, currencyCode: 'EUR' }))).toBe('F-1 · rest 10,00 EUR · scad. 15.11.2026 · litigiu');
  });
});
