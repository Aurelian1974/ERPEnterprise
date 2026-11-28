import { formatAmount } from './cashApi';

/** O linie de nota, cat ii trebuie formulei si echilibrului: contul, partea (1 debit, 2 credit) si suma invarianta („1234.50”). */
export interface EntryLine {
  readonly accountCode: string;
  readonly side: number;
  readonly amountRon: string;
}

/** Suma invarianta in bani (intregi), ca echilibrul sa nu depinda de virgula mobila; null daca textul nu e o suma. */
export function toCents(value: string): number | null {
  const text = value.trim().replace(/\s/g, '').replace(',', '.');
  if (!/^-?\d+(\.\d{1,2})?$/.test(text)) {
    return null;
  }
  const negative = text.startsWith('-');
  const [whole = '0', fraction = ''] = (negative ? text.slice(1) : text).split('.');
  const cents = Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
  return negative ? -cents : cents;
}

/** Banii inapoi in suma invarianta („-12.05”). */
export function fromCents(cents: number): string {
  const sign = cents < 0 ? '-' : '';
  const abs = Math.abs(cents);
  return `${sign}${Math.floor(abs / 100).toString()}.${(abs % 100).toString().padStart(2, '0')}`;
}

/** Totalurile debit si credit ale liniilor si diferenta (debit − credit), in bani; liniile cu suma invalida nu se aduna. */
export function balance(lines: readonly Pick<EntryLine, 'side' | 'amountRon'>[]): { readonly debit: number; readonly credit: number; readonly difference: number } {
  let debit = 0;
  let credit = 0;
  for (const line of lines) {
    const cents = toCents(line.amountRon) ?? 0;
    if (line.side === 1) {
      debit += cents;
    } else {
      credit += cents;
    }
  }
  return { debit, credit, difference: debit - credit };
}

/**
 * Nota in formatul obisnuit al contabilului roman (C2 3.5): „4111 = % 15.004,00” urmat de conturile creditoare, „% = 401” urmat de cele
 * debitoare, „628 = 462 100,00” pentru o singura pereche, „% = %” cand ambele parti au mai multe conturi.
 */
export function formula(lines: readonly EntryLine[]): string[] {
  const debit = group(lines.filter((l) => l.side === 1));
  const credit = group(lines.filter((l) => l.side === 2));
  if (debit.length === 0 || credit.length === 0) {
    return [];
  }
  const total = formatAmount(fromCents(debit.reduce((s, l) => s + l.cents, 0)));
  const row = (l: Grouped) => `    ${l.code}  ${formatAmount(fromCents(l.cents))}`;
  const [d] = debit;
  const [c] = credit;
  if (debit.length === 1 && credit.length === 1 && d !== undefined && c !== undefined) {
    return [`${d.code} = ${c.code}  ${total}`];
  }
  if (debit.length === 1 && d !== undefined) {
    return [`${d.code} = %  ${total}`, ...credit.map(row)];
  }
  if (credit.length === 1 && c !== undefined) {
    return [`% = ${c.code}  ${total}`, ...debit.map(row)];
  }
  return [`% = %  ${total}`, ...debit.map((l) => `  D ${l.code}  ${formatAmount(fromCents(l.cents))}`),
    ...credit.map((l) => `  C ${l.code}  ${formatAmount(fromCents(l.cents))}`)];
}

interface Grouped { readonly code: string; readonly cents: number }

function group(lines: readonly EntryLine[]): Grouped[] {
  const map = new Map<string, number>();
  for (const line of lines) {
    map.set(line.accountCode, (map.get(line.accountCode) ?? 0) + (toCents(line.amountRon) ?? 0));
  }
  return [...map.entries()].map(([code, cents]) => ({ code, cents }));
}
