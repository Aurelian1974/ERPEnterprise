import { t, type TextKey } from '../i18n';
import { formatAmount } from './cashApi';

/** Luna in romaneste: „octombrie 2026”. */
export function monthLabel(year: number, month: number): string {
  return `${t(`fx.month.${month}` as TextKey)} ${year}`;
}

/** O diferenta cu semn explicit, ca in machete: „+335,29”, „−1.204,16”, „0,00”. */
export function signed(value: string | null | undefined): string {
  if (value == null || value === '') return '';
  const n = Number(value);
  if (n > 0) return `+${formatAmount(value)}`;
  return formatAmount(value);
}

/** Clasa CSS a unei diferente: castig sau pierdere. */
export function signClass(value: string | null | undefined): string {
  const n = Number(value ?? 0);
  return n > 0 ? 'num gain' : n < 0 ? 'num loss' : 'num';
}

/** Cursul cu 4 zecimale, ca in machete (5,0790). */
export function rateText(value: string | null | undefined): string {
  return value == null ? '' : Number(value).toLocaleString('ro-RO', { minimumFractionDigits: 4, maximumFractionDigits: 6 });
}

/** Luna de reevaluat propusa: luna trecuta fata de ziua data (AAAA-LL-ZZ). */
export function previousMonth(today: string): { readonly year: number; readonly month: number } {
  const year = Number(today.slice(0, 4));
  const month = Number(today.slice(5, 7));
  return month === 1 ? { year: year - 1, month: 12 } : { year, month: month - 1 };
}
