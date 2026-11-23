import { describe, expect, it } from 'vitest';
import { paperStatusLabel } from './CommercialPapersPage';

describe('paperStatusLabel', () => {
  it('names the status by direction', () => {
    expect(paperStatusLabel(1, 1)).toBe('paper.status.portfolio');
    expect(paperStatusLabel(1, 2)).toBe('paper.status.issued');
    expect(paperStatusLabel(3, 1)).toBe('paper.status.collected');
    expect(paperStatusLabel(3, 2)).toBe('paper.status.paid');
    expect(paperStatusLabel(4, 1)).toBe('paper.status.refused');
  });
});
