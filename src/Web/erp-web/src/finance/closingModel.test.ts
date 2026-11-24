import { describe, expect, it } from 'vitest';
import type { components } from '../api/schema';
import { blocks, defaultPeriod, previousMonthEnd, runSummary } from './closingModel';

type Step = components['schemas']['CloseStepDto'];
const step = (severity: number, outcome: number, overrideReason: string | null = null): Step => ({
  stepCode: 'X', name: 'X', severity, allowOverride: true, outcome, summary: '', issues: [], overrideReason, overriddenByName: null, overriddenAt: null,
});

describe('closingModel', () => {
  it('counts blocking, warning, ok and overridden steps', () => {
    const steps = [step(1, 2), step(1, 2, 'motiv'), step(2, 2), step(1, 1), step(2, 1)];
    expect(runSummary(steps)).toEqual({ blocking: 1, warnings: 1, ok: 2, overridden: 1 });
    expect(blocks(step(1, 2))).toBe(true);
    expect(blocks(step(2, 2))).toBe(false);
  });

  it('proposes the first open month and the previous month end', () => {
    const period = (id: number, status: number) => ({ id, periodNo: id, startDate: '', endDate: '', status, isImported: false, softClosedAt: null, softClosedByName: null,
      closedAt: null, closedByName: null, rowVersion: '' });
    const years = [{ id: 1, year: 2026, startDate: '', endDate: '', status: 1, vatPeriodicity: 1, closedAt: null, lockedAt: null, lockReason: null, rowVersion: '',
      periods: [period(1, 3), period(2, 1), period(3, 1)] }];
    expect(defaultPeriod(years)?.id).toBe(2);
    expect(previousMonthEnd('2026-11-24')).toBe('2026-10-31');
    expect(previousMonthEnd('2026-03-05')).toBe('2026-02-28');
  });
});
