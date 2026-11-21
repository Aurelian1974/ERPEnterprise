import { describe, expect, it } from 'vitest';
import { cellState, groupPermissions, localOverrides, toggle, toInput } from './roleModel';

const permission = (id: number, code: string) => ({ id, code, moduleCode: code.slice(0, code.indexOf('.')), description: code });

describe('matricea de drepturi', () => {
  it('grupează catalogul pe module și resurse, cu acțiunile în ordinea coloanelor', () => {
    const groups = groupPermissions([
      permission(1, 'Core.Partner.ViewPersonalData'), permission(2, 'Core.Partner.Manage'), permission(3, 'Core.Partner.View'),
      permission(4, 'Core.Import.Run'), permission(5, 'Sales.Invoice.View'),
    ]);
    expect(groups.map((g) => g.module)).toEqual(['Core', 'Sales']);
    expect(groups[0]?.resources.map((r) => r.code)).toEqual(['Core.Partner', 'Core.Import']);
    expect(groups[0]?.resources[0]?.permissions.map((p) => p.id)).toEqual([3, 2, 1]);
  });

  it('revocarea câștigă, iar excepțiile fără efect se recunosc', () => {
    expect(cellState(true, 'R', undefined).effective).toBe(false);
    expect(cellState(false, 'G', undefined).effective).toBe(true);
    expect(cellState(false, 'G', 'R').effective).toBe(false);
    expect(cellState(true, undefined, 'R').effective).toBe(false);
    expect(cellState(true, 'G', undefined).redundant).toBe(true);
    expect(cellState(false, 'R', undefined).redundant).toBe(true);
    expect(cellState(false, 'R', 'G').redundant).toBe(false);
  });

  it('clicul revocă moștenirea, adaugă lipsa, revine la moștenire și nu atinge excepțiile pentru toate firmele', () => {
    expect(toggle(cellState(true, undefined, undefined))).toBe('R');
    expect(toggle(cellState(false, undefined, undefined))).toBe('G');
    expect(toggle(cellState(true, 'R', undefined))).toBeNull();
    expect(toggle(cellState(false, undefined, 'G'))).toBeUndefined();
  });

  it('excepțiile locale sunt cele ale firmei vederii, iar motivul gol nu pleacă la server', () => {
    const overrides = [
      { userId: 7, permissionId: 1, companyId: 3, effect: 'G', reason: 'Concediu' },
      { userId: 7, permissionId: 2, companyId: null, effect: 'R', reason: null },
      { userId: 8, permissionId: 1, companyId: 3, effect: 'R', reason: null },
    ];
    expect([...localOverrides(overrides, 7, 3).keys()]).toEqual([1]);
    expect([...localOverrides(overrides, 7, null).keys()]).toEqual([2]);
    expect(toInput(new Map([[2, { effect: 'R', reason: '  ' }]]))).toEqual([{ permissionId: 2, effect: 'R', reason: null }]);
  });
});
