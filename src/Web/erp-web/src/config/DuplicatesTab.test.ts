import { describe, expect, it } from 'vitest';
import { reasonsText } from './DuplicatesTab';

describe('dublurile', () => {
  it('motivele se arată în română', () => {
    expect(reasonsText('Email,Name')).toBe('același e-mail, aceeași denumire');
    expect(reasonsText('Iban')).toBe('același IBAN');
  });
});
