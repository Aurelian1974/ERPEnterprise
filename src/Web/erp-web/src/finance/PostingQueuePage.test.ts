import { describe, expect, it } from 'vitest';
import type { components } from '../api/schema';
import { entryKindKey, resolvedFromKey } from './JournalEntriesPage';
import { groupByReason } from './PostingQueuePage';

type QueueItem = components['schemas']['PostingQueueItemDto'];

const item = (id: number, reason: number, date: string): QueueItem => ({
  id, eventId: `e${String(id)}`, eventType: 'FacturaEmisa', sourceDocumentType: null, sourceDocumentId: null, sourceDocumentNumber: null, accountingDate: date,
  reason, details: null, status: 1, attempts: 1, lastAttemptAt: '2026-11-13T10:00:00', resolvedEntryId: null, ignoreReason: null, rowVersion: 'AA==',
});

describe('posting queue and entries', () => {
  it('groups pending rows by reason, oldest first', () => {
    const groups = groupByReason([item(1, 3, '2026-11-12'), item(2, 1, '2026-11-13'), item(3, 1, '2026-11-10')]);
    expect(groups.map(([reason, rows]) => [reason, rows.map((r) => r.id)])).toEqual([[1, [3, 2]], [3, [1]]]);
  });

  it('labels drafts, kinds and where the account came from', () => {
    expect(entryKindKey(2, 1)).toBe('entry.kind.draft');
    expect(entryKindKey(6, 2)).toBe('entry.kind.6');
    expect(resolvedFromKey(8)).toBe('entry.from.8');
    expect(resolvedFromKey(null)).toBeUndefined();
  });
});
