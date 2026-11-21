import { describe, expect, it } from 'vitest';
import { ruleConditions } from './SeriesRules';

describe('regulile de alegere a seriei', () => {
  it('descrie condițiile completate, în ordine', () => {
    expect(ruleConditions({ userName: 'Ion Popescu', branchName: null, locationName: null, partnerScope: 3 }))
      .toBe('Utilizator: Ion Popescu · Partener: din afara UE (export)');
    expect(ruleConditions({ userName: null, branchName: 'PL Cluj', locationName: 'Magazin 1', partnerScope: null }))
      .toBe('Punct de lucru: PL Cluj · Locație: Magazin 1');
  });
});
