import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { z } from 'zod';
import { ApiError } from '../api/problem';
import { t } from '../i18n';

/**
 * Erorile 400 pe camp de la server ajung la campurile formularului (UI-10); restul (403, 409) devin mesajul de sus.
 * Intoarce mesajul pentru banner, daca exista.
 */
export function applyServerErrors<T extends FieldValues>(error: unknown, setError: UseFormSetError<T>, fields: readonly string[]): string | undefined {
  if (!(error instanceof ApiError)) {
    return t('app.loadError');
  }
  let unmatched = false;
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    if (fields.includes(field)) {
      setError(field as Path<T>, { type: 'server', message: messages.join(' ') });
    } else {
      unmatched = true;
    }
  }
  return Object.keys(error.fieldErrors).length === 0 || unmatched ? error.message : undefined;
}

export const required = z.string().trim().min(1, t('common.required'));
export const optionalText = z.string().trim().nullable().transform((v) => (v === null || v === '' ? null : v));
export const code = required.max(20).regex(/^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$/, 'Codul are 1–20 caractere: litere, cifre, „.”, „_” sau „-”.');

/** Suma introdusa in format romanesc (200,00 sau 200.00) -> sir zecimal invariant pentru API (UI-05). */
export const money = z
  .string()
  .trim()
  .nullable()
  .refine((v) => v === null || v === '' || /^\d{1,17}([.,]\d{1,2})?$/.test(v), 'Suma are cel mult 2 zecimale, de exemplu 200,00.')
  .transform((v) => (v === null || v === '' ? null : v.replace(',', '.')));

/** Afisarea unei sume invariante in format romanesc, fara separator de mii (camp editabil). */
export const moneyForInput = (value: string | null) => (value === null ? '' : value.replace('.', ','));
