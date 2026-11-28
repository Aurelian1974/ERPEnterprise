import { timeZone } from '../format/date';
import type { BnrRate, ExchangeRates } from './ratesApi';

/** Ziua „aaaa-ll-zz” mutata cu n zile (fara fus orar: data calendaristica). */
export function addDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

/** Zilele intervalului, de la cea mai noua la cea mai veche. */
export function daysDesc(from: string, to: string): string[] {
  const days: string[] = [];
  for (let day = to; day >= from; day = addDays(day, -1)) {
    days.push(day);
  }
  return days;
}

const weekday = new Intl.DateTimeFormat('ro-RO', { timeZone: 'UTC', weekday: 'long' });

/** „Vineri 13.11.2026”. */
export function dayLabel(iso: string): string {
  const name = weekday.format(new Date(`${iso}T00:00:00Z`));
  return `${name.charAt(0).toLocaleUpperCase('ro-RO')}${name.slice(1)} ${shortDay(iso)}`;
}

/** „13.11.2026”. */
export function shortDay(iso: string): string {
  const [y, m, d] = iso.split('-');
  return `${d}.${m}.${y}`;
}

export const isWeekend = (iso: string) => [0, 6].includes(new Date(`${iso}T00:00:00Z`).getUTCDay());

/** Cursul cu virgula romaneasca si cel putin 4 zecimale, cum il publica BNR („5.0812” → „5,0812”; „4.41” → „4,4100”). */
export function formatRate(value: string): string {
  const [whole, fraction = ''] = value.split('.');
  return `${whole},${fraction.padEnd(4, '0')}`;
}

/** Ora preluarii („13:04”), in ora Romaniei. */
export function importTime(utc: string): string {
  return new Intl.DateTimeFormat('ro-RO', { timeZone, hour: '2-digit', minute: '2-digit' }).format(new Date(utc));
}

export interface RateRow {
  readonly day: string;
  readonly rates: ReadonlyMap<string, BnrRate>;
  /**
   * Ziua cursului folosit pe documentele zilei: ultima zi cu curs strict anterioara (cursul de vineri se foloseste sambata, duminica
   * si luni; 02.01, 4.3), daca exista.
   */
  readonly documentsRateDay: string | undefined;
  readonly importedAt: string | undefined;
}

/** Randurile tabelului BNR: fiecare zi a intervalului, cu cursurile publicate in ea si ziua cursului folosit pe documentele ei. */
export function rateRows(data: Pick<ExchangeRates, 'from' | 'to' | 'bnrRates' | 'previousRates'>): RateRow[] {
  const byDay = new Map<string, Map<string, BnrRate>>();
  for (const rate of data.bnrRates) {
    const day = byDay.get(rate.rateDate) ?? new Map<string, BnrRate>();
    day.set(rate.currencyCode, rate);
    byDay.set(rate.rateDate, day);
  }
  const previous = data.previousRates.reduce<string | undefined>((max, r) => (max === undefined || r.rateDate > max ? r.rateDate : max), undefined);
  const days = daysDesc(data.from, data.to);
  return days.map((day) => {
    const rates = byDay.get(day) ?? new Map<string, BnrRate>();
    const earlier = [...byDay.keys()].filter((d) => d < day).sort().at(-1) ?? previous;
    return {
      day,
      rates,
      documentsRateDay: earlier,
      importedAt: [...rates.values()].map((r) => r.importedAt).sort().at(-1),
    };
  });
}

/** Urmatoarea zi lucratoare dupa ultima zi cu curs (sarbatorile legale nu se cunosc aici). */
export function nextBankingDay(lastRateDate: string): string {
  let day = addDays(lastRateDate, 1);
  while (isWeekend(day)) {
    day = addDays(day, 1);
  }
  return day;
}
