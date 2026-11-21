import { describe, expect, it } from 'vitest';
import { contextName, contextOptions, contextOwner, contextQuery, effectiveContext } from './node';

const user = (wholeCompany: boolean) => ({
  companyId: 3, companies: [{ id: 3, code: 'EX', name: 'Exemplu SRL' }], wholeCompany,
  nodes: [
    { branchId: 5, locationId: null, parentBranchId: null, name: 'Punct Nord' },
    { branchId: null, locationId: 9, parentBranchId: 5, name: 'Magazin' },
  ],
});

describe('nodul din antet', () => {
  it('„Toată firma” doar cu rol pe firmă; altfel primul nod al utilizatorului', () => {
    expect(contextOptions(user(true)).map((o) => o.value)).toEqual(['', 'b5', 'l9']);
    expect(contextOptions(user(false)).map((o) => o.value)).toEqual(['b5', 'l9']);
    expect(effectiveContext(user(false), undefined)).toBe('b5');
    expect(effectiveContext(user(false), '')).toBe('b5');
    expect(effectiveContext(user(true), 'l9')).toBe('l9');
    expect(effectiveContext(user(true), 'b77')).toBe('');
  });

  it('dă proprietarul celor noi, numele și parametrii căutării', () => {
    expect(contextOwner('')).toBe('company');
    expect(contextOwner('l9')).toBe('l9');
    expect(contextName(user(true), '')).toBe('Exemplu SRL');
    expect(contextName(user(true), 'l9')).toBe('Exemplu SRL › Magazin');
    expect(contextQuery('b5')).toEqual({ ContextBranchId: 5 });
    expect(contextQuery('')).toEqual({});
  });
});
