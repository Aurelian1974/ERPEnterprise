import { describe, expect, it } from 'vitest';
import { exclusionReason } from './LotProposalPage';
import { lineComplete } from './LotChangesPage';
import { proposedAction } from './ExpiriesPage';
import { traceCustomers } from './LotTracePage';
import {
  deliveredSummary, expiryTone, groupCustomers, LotStatus, lotStatusLabel, lotStatusTone, lotTotals, qtyToRo, recallOutcomeLabel, type LotItem,
  type LotTraceMovement, type RecallCustomer,
} from './lotsApi';

const lot = (patch: Partial<LotItem>): LotItem => ({
  lotId: 1, itemVariantId: 1, variantCode: 'APA05', variantName: 'Apă', lotCode: 'L1', ckuCode: 'APA05·L1·270222', supplierLotCode: null, expiryDate: null,
  expiryKind: 2, daysLeft: 45, status: LotStatus.Available, isTechnical: false, warehouseId: 1, warehouseCode: 'DEP-CJ', qtyOnHand: '100.000000', ...patch,
});

const move = (patch: Partial<LotTraceMovement>): LotTraceMovement => ({
  depth: 0, movementId: 1, lotId: 1, ckuCode: 'C', movementDate: '2026-11-20', movementTypeCode: 'ISSUE_SALE', warehouseCode: 'DEP-CJ', qtyBase: '-10',
  partnerId: 7, partnerName: 'Mega Retail SRL', sourceDocumentType: 'AV', sourceDocumentId: 1, sourceDocumentNumber: 'EXD 1', ...patch,
});

describe('loturile (02.05, S4.5)', () => {
  it('cantitățile se afișează românește, fără zecimalele nule', () => {
    expect(qtyToRo('12000.000000')).toBe('12.000');
    expect(qtyToRo('2.500000')).toBe('2,5');
    expect(qtyToRo('-3')).toBe('−3');
    expect(qtyToRo(null)).toBe('—');
  });

  it('starea și termenul dau eticheta și culoarea pastilei', () => {
    expect(lotStatusLabel(LotStatus.Quarantine)).toBe('inv.lot.status.quarantine');
    expect(lotStatusTone(LotStatus.Recalled)).toBe('bad');
    expect(expiryTone(9)).toBe('warn');
    expect(expiryTone(-1)).toBe('bad');
    expect(expiryTone(45)).toBeUndefined();
    expect(expiryTone(null)).toBeUndefined();
  });

  it('totalul listei numără doar loturile disponibile și neexpirate ca disponibil', () => {
    const totals = lotTotals([lot({ lotId: 1, qtyOnHand: '480' }), lot({ lotId: 2, qtyOnHand: '1920', status: LotStatus.Quarantine }), lot({ lotId: 3, qtyOnHand: '10', daysLeft: -2 })]);
    expect(totals).toEqual({ qty: 2410, lots: 3, available: 480 });
  });

  it('acțiunea propusă la expirare depinde de tipul termenului și de zilele rămase', () => {
    expect(proposedAction({ daysLeft: -1, expiryKind: 1, status: LotStatus.Expired })).toBe('inv.exp.action.scrap');
    expect(proposedAction({ daysLeft: -6, expiryKind: 2, status: LotStatus.Available })).toBe('inv.exp.action.donate');
    expect(proposedAction({ daysLeft: 3, expiryKind: 1, status: LotStatus.Available })).toBe('inv.exp.action.discount');
    expect(proposedAction({ daysLeft: 22, expiryKind: 2, status: LotStatus.Available })).toBe('inv.exp.action.priority');
  });

  it('lotul din afara propunerii își spune motivul', () => {
    const proposal = [{ lotId: 2, ckuCode: 'C', lotCode: 'L2', expiryDate: null, qtyBase: 10, daysLeft: 45, isFirstChoice: true }];
    expect(exclusionReason(lot({ status: LotStatus.Quarantine }), proposal, 4)).toBe('inv.prop.excludedStatus');
    expect(exclusionReason(lot({ daysLeft: -1 }), proposal, 5)).toBe('inv.prop.excludedExpired');
    expect(exclusionReason(lot({}), proposal, 6)).toBe('inv.prop.notNeeded');
    expect(exclusionReason(lot({}), [], 6)).toBe('inv.prop.excludedShelfLife');
    expect(exclusionReason(lot({}), proposal, 2)).toBeUndefined();
  });

  it('linia SL cere lotul sursă, o destinație diferită sau un cod nou și cantitatea', () => {
    expect(lineComplete({ fromLotId: 1, toLotId: 2, newLotCode: '', qty: '3' })).toBe(true);
    expect(lineComplete({ fromLotId: 1, toLotId: 1, newLotCode: '', qty: '3' })).toBe(false);
    expect(lineComplete({ fromLotId: 1, toLotId: undefined, newLotCode: 'L-NOU', qty: '2,5' })).toBe(true);
    expect(lineComplete({ fromLotId: 1, toLotId: undefined, newLotCode: ' ', qty: '2' })).toBe(false);
    expect(lineComplete({ fromLotId: undefined, toLotId: 2, newLotCode: '', qty: '2' })).toBe(false);
  });

  it('clienții retragerii și ai trasabilității se grupează pe partener, cu bucățile nete', () => {
    const customers: RecallCustomer[] = [
      { id: 1, lotId: 1, ckuCode: 'C', partnerId: 7, partnerName: 'Mega', sourceDocumentType: 'AV', sourceDocumentId: 1, sourceDocumentNumber: null, movementDate: '2026-11-19', qtyBase: '2880', outcome: null, outcomeNote: null },
      { id: 2, lotId: 1, ckuCode: 'C', partnerId: 7, partnerName: 'Mega', sourceDocumentType: 'AV', sourceDocumentId: 2, sourceDocumentNumber: null, movementDate: '2026-11-21', qtyBase: '120', outcome: 1, outcomeNote: null },
      { id: 3, lotId: 1, ckuCode: 'C', partnerId: 8, partnerName: 'Hotel', sourceDocumentType: 'FV', sourceDocumentId: 3, sourceDocumentNumber: null, movementDate: '2026-11-20', qtyBase: '960', outcome: null, outcomeNote: null },
    ];
    expect(groupCustomers(customers)).toEqual([
      { partnerId: 7, name: 'Mega', documents: 2, qty: 3000, last: '2026-11-21' },
      { partnerId: 8, name: 'Hotel', documents: 1, qty: 960, last: '2026-11-20' },
    ]);
    expect(recallOutcomeLabel(null)).toBe('inv.recall.outcome.pending');

    const forward = [
      move({ movementId: 1, movementTypeCode: 'LOAD', qtyBase: '100', partnerId: null, partnerName: null }),
      move({ movementId: 2, qtyBase: '-30' }),
      move({ movementId: 3, qtyBase: '5', movementTypeCode: 'RETURN_FROM_CUSTOMER' }),
      move({ movementId: 4, qtyBase: '-20', partnerId: 8, partnerName: 'Hotel', sourceDocumentNumber: 'FV 9' }),
    ];
    expect(traceCustomers(forward).map((c) => [c.name, c.qty])).toEqual([['Mega Retail SRL', 25], ['Hotel', 20]]);
    expect(deliveredSummary(forward)).toEqual({ received: 100, delivered: 45, customers: 2 });
  });
});
