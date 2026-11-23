import { describe, expect, it } from 'vitest';
import { awaitsApproval, filterOrders, type PaymentOrderItem } from './paymentOrderApi';

const order = (id: number, status: number, confirmationStatus: number | null, isApproved = false, origin = 1, reversalOfId: number | null = null) =>
  ({ id, status, confirmationStatus, isApproved, origin, reversalOfId }) as PaymentOrderItem;

describe('paymentOrderApi', () => {
  it('awaits approval only for validated, unconfirmed, unapproved orders that are not from the statement', () => {
    expect(awaitsApproval(order(1, 2, 1))).toBe(true);
    expect(awaitsApproval(order(2, 2, 1, true))).toBe(false);
    expect(awaitsApproval(order(3, 2, 2))).toBe(false);
    expect(awaitsApproval(order(4, 2, 1, false, 2))).toBe(false);
    expect(awaitsApproval(order(5, 2, null, false, 1, 9))).toBe(false);
    expect(awaitsApproval(order(6, 1, null))).toBe(false);
  });

  it('filters drafts, unconfirmed and rejected orders', () => {
    const orders = [order(1, 1, null), order(2, 2, 1), order(3, 2, 2, true), order(4, 2, 3, true)];
    expect(filterOrders(orders, 'drafts').map((o) => o.id)).toEqual([1]);
    expect(filterOrders(orders, 'unconfirmed').map((o) => o.id)).toEqual([2]);
    expect(filterOrders(orders, 'rejected').map((o) => o.id)).toEqual([4]);
    expect(filterOrders(orders, 'toApprove').map((o) => o.id)).toEqual([2]);
    expect(filterOrders(orders, 'all')).toHaveLength(4);
  });
});
