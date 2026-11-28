import type { components } from '../api/schema';

type Step = components['schemas']['CloseStepDto'];
type Period = components['schemas']['AccountingPeriodDto'];
type Year = components['schemas']['FiscalYearDto'];

/** Pasul blocheaza inchiderea: blocant, esuat si netrecut cu motiv (C3 3.3). */
export function blocks(step: Pick<Step, 'severity' | 'outcome' | 'overrideReason'>): boolean {
  return step.severity === 1 && step.outcome === 2 && step.overrideReason == null;
}

/** Numaratorile din antetul ecranului „Inchiderea lunii”: pasi blocanti, avertismente, in regula, trecuti cu motiv. */
export function runSummary(steps: readonly Step[]): { readonly blocking: number; readonly warnings: number; readonly ok: number; readonly overridden: number } {
  return {
    blocking: steps.filter(blocks).length,
    warnings: steps.filter((s) => s.severity === 2 && s.outcome === 2).length,
    ok: steps.filter((s) => s.outcome === 1).length,
    overridden: steps.filter((s) => s.outcome === 2 && s.overrideReason != null).length,
  };
}

/** Luna propusa implicit: prima luna neinchisa a anului curent, altfel ultima luna a ultimului an. */
export function defaultPeriod(years: readonly Year[]): Period | undefined {
  const all = years.flatMap((y) => y.periods);
  return all.find((p) => p.status < 3) ?? all.at(-1);
}

/** Ultima zi a lunii dinaintea datei date („2026-11-24” → „2026-10-31”), pentru concordantele implicite. */
export function previousMonthEnd(iso: string): string {
  const d = new Date(`${iso.slice(0, 7)}-01T00:00:00Z`);
  d.setUTCDate(0);
  return d.toISOString().slice(0, 10);
}
