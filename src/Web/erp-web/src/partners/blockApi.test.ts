import { describe, expect, it } from 'vitest';
import { blockedText, maskOf, parseLimit } from './blockApi';

describe('blocarea și limita de credit', () => {
  it('masca vine din bife, iar „tot” se scrie ca atare', () => {
    expect(maskOf([1, 4])).toBe(5);
    const label = (f: number) => ({ 1: 'vânzare', 2: 'achiziție', 4: 'plăți' })[f] ?? '';
    expect(blockedText(5, 7, label, 'tot')).toBe('vânzare, plăți');
    expect(blockedText(7, 7, label, 'tot')).toBe('tot');
    expect(blockedText(0, 7, label, 'tot')).toBe('');
  });

  it('limita acceptă formatul românesc și cel cu punct; gol = fără limită', () => {
    expect(parseLimit('10.000,50')).toBe('10000.50');
    expect(parseLimit('2500.5')).toBe('2500.50');
    expect(parseLimit(' ')).toBeNull();
    expect(parseLimit('-5')).toBeUndefined();
    expect(parseLimit('1,234')).toBeUndefined();
  });
});
