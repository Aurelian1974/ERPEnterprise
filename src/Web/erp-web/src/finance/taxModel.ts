import type { components } from '../api/schema';

type TaxLine = components['schemas']['TaxReturnLineDto'];
type CalendarItem = components['schemas']['TaxCalendarItemDto'];

/** Prima si ultima zi a lunii dintr-o valoare „AAAA-LL” (campul de tip month). */
export function monthRange(month: string): { readonly from: string; readonly to: string } {
  const last = new Date(Date.UTC(Number(month.slice(0, 4)), Number(month.slice(5, 7)), 0)).getUTCDate();
  return { from: `${month}-01`, to: `${month}-${String(last).padStart(2, '0')}` };
}

/** Luna precedenta datei („2026-11-24” → „2026-10”), luna implicita a declaratiilor. */
export function previousMonth(iso: string): string {
  const d = new Date(`${iso.slice(0, 7)}-01T00:00:00Z`);
  d.setUTCMonth(d.getUTCMonth() - 1);
  return d.toISOString().slice(0, 7);
}

/** Randurile D300 pe sectiuni (macheta c4-02): livrarile (colectata), achizitiile (deductibila) si totalurile. */
export function d300Sections(lines: readonly TaxLine[]): {
  readonly sales: readonly TaxLine[]; readonly purchases: readonly TaxLine[]; readonly totals: readonly TaxLine[];
} {
  return {
    sales: lines.filter((l) => l.rowCode.startsWith('L-')),
    purchases: lines.filter((l) => l.rowCode.startsWith('A-')),
    totals: lines.filter((l) => l.rowCode === 'TOTAL'),
  };
}

/** Randurile unui tip de operatiune: baza si TVA-ul pe acelasi rand de tabel. */
export function pairRows(lines: readonly TaxLine[]): readonly { readonly rowCode: string; readonly label: string; readonly base: string; readonly vat: string }[] {
  const codes = [...new Set(lines.map((l) => l.rowCode))];
  return codes.map((code) => {
    const of = (column: string) => lines.find((l) => l.rowCode === code && l.columnCode === column);
    const value = (l: TaxLine | undefined) => l?.manualValue ?? l?.calculatedValue ?? '0.00';
    return { rowCode: code, label: of('BASE')?.label ?? code, base: value(of('BASE')), vat: value(of('VAT')) };
  });
}

/** Numaratorile din antetul calendarului (macheta c4-01): de depus, de verificat, gata de depus, depuse (sumele raman text, fara calcule in browser). */
export function calendarSummary(items: readonly CalendarItem[]): {
  readonly toFile: number; readonly review: number; readonly ready: number; readonly filed: number;
} {
  return {
    toFile: items.filter((i) => (i.status ?? 0) < 4).length,
    review: items.filter((i) => i.needsReview).length,
    ready: items.filter((i) => i.status === 3).length,
    filed: items.filter((i) => i.status === 4).length,
  };
}
