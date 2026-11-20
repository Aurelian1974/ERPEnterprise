import { describe, expect, it } from 'vitest';
import { fileNameFrom, fileToBase64 } from './download';

describe('descărcarea fișierelor', () => {
  it('numele vine din Content-Disposition, cu forma UTF-8 înaintea celei simple', () => {
    expect(fileNameFrom("attachment; filename=parteneri.xlsx; filename*=UTF-8''parteneri-%C8%99ablon.xlsx", 'x.xlsx')).toBe('parteneri-șablon.xlsx');
    expect(fileNameFrom('attachment; filename="parteneri-2026-11-20.xlsx"', 'x.xlsx')).toBe('parteneri-2026-11-20.xlsx');
    expect(fileNameFrom(null, 'x.xlsx')).toBe('x.xlsx');
  });

  it('fișierul încărcat ajunge în Base64 neschimbat', async () => {
    const bytes = new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0xff, 0x00]);
    expect(await fileToBase64(new Blob([bytes]))).toBe(btoa(String.fromCharCode(...bytes)));
  });
});
