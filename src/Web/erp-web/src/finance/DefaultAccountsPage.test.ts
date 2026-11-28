import { describe, expect, it } from 'vitest';
import type { components } from '../api/schema';
import { effective } from './DefaultAccountsPage';
import { dimensionsText } from './PostingModelsPage';

type Assignment = components['schemas']['RoleAssignmentDto'];

const a = (id: number, role: string, companyId: number | null, code: string, partnerKind: number | null = null): Assignment => ({
  id, companyId, role, accountId: id, accountCode: code, accountName: code, partnerKind, currencyCode: null, validFrom: '2000-01-01', validTo: null,
});

describe('default accounts and models', () => {
  it('uses the company assignments of a role, else the install defaults', () => {
    const all = [a(1, 'SupplierPayable', null, '401'), a(2, 'SupplierPayable', 1, '401.01', 1), a(3, 'SupplierPayable', 1, '401.02', 2), a(4, 'VatCollected', null, '4427')];
    expect(effective(all, 'SupplierPayable').map((x) => x.accountCode)).toEqual(['401.01', '401.02']);
    expect(effective(all, 'VatCollected').map((x) => x.accountCode)).toEqual(['4427']);
    expect(effective(all, 'PurchaseExpense')).toEqual([]);
  });

  it('describes the copied dimensions', () => {
    expect(dimensionsText(63)).toBe('toate');
    expect(dimensionsText(0)).toBe('—');
    expect(dimensionsText(1 | 4)).toBe('partener, centru de cost');
  });
});
