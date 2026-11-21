import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/problem';
import { applyServerErrors, money } from './forms';

describe('formulare', () => {
  it('erorile 400 ajung pe câmpuri, fără banner când toate sunt ale formularului', () => {
    const setError = vi.fn();
    const error = new ApiError(400, { title: 'Datele trimise nu sunt valide.', code: 'CORE_VALIDATION', errors: { fiscalCode: ['CUI-ul nu este valid.'] } });

    const banner = applyServerErrors(error, setError, ['fiscalCode', 'name']);

    expect(setError).toHaveBeenCalledWith('fiscalCode', { type: 'server', message: 'CUI-ul nu este valid.' });
    expect(banner).toBeUndefined();
  });

  it('erorile fără câmp în formular apar în banner cu textul lor', () => {
    const error = new ApiError(400, { title: 'Datele trimise nu sunt valide.', code: 'CORE_VALIDATION', errors: { 'lines[0].factorToBase': ['UM de bază are factorul 1.'] } });
    expect(applyServerErrors(error, vi.fn(), ['code'])).toBe('UM de bază are factorul 1.');
  });

  it('un conflict 409 devine mesajul de sus', () => {
    const banner = applyServerErrors(new ApiError(409, { title: 'Codul firmei există deja.', code: 'CORE_DUPLICATE_CODE' }), vi.fn(), ['code']);
    expect(banner).toBe('Codul firmei există deja.');
  });

  it('suma în format românesc pleacă la API ca șir zecimal invariant', () => {
    expect(money.parse('200,50')).toBe('200.50');
    expect(money.parse('')).toBeNull();
    expect(money.safeParse('1.234,56').success).toBe(false);
  });
});
