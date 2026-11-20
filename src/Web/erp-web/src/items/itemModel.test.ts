import { describe, expect, it } from 'vitest';
import { flattenGroups } from '../config/NomenclaturesTab';
import { effectiveLines } from './ItemForm';
import { fromRo, groupPath, itemSearchQuery, percentText, toRo, type ItemGroup } from './itemApi';

const group = (id: number, name: string, parentId: number | null, sortOrder = 10): ItemGroup =>
  ({ id, code: `G${id}`, name, parentId, sortOrder, isActive: true, usageCount: 0, rowVersion: 'AA==' });

describe('articolele (02.01, 3.3)', () => {
  const groups = [group(1, 'Băuturi', null), group(2, 'Ape minerale', 1), group(3, 'Sucuri', 1, 5), group(4, 'Alimente', null, 20)];

  it('grupa apare cu calea părinților, iar arborele pune părintele înaintea copiilor, în ordinea lor', () => {
    expect(groupPath(groups, 2)).toBe('Băuturi › Ape minerale');
    expect(groupPath(groups, null)).toBe('');
    expect(flattenGroups(groups).map((g) => `${g.depth}:${g.group.name}`)).toEqual(['0:Băuturi', '1:Sucuri', '1:Ape minerale', '0:Alimente']);
  });

  it('excepția articolului înlocuiește linia din set, iar o UM nouă se adaugă; baza rămâne a setului', () => {
    const set = [
      { uomId: 1, factorToBase: '1', isBase: true, isDefaultSales: false, isDefaultPurchase: false, isDefaultPos: true },
      { uomId: 2, factorToBase: '6', isBase: false, isDefaultSales: true, isDefaultPurchase: false, isDefaultPos: false },
    ];
    const lines = effectiveLines(set, [
      { uomId: 2, factorToBase: '12', isDefaultSales: true, isDefaultPurchase: false, isDefaultPos: false },
      { uomId: 4, factorToBase: '480', isDefaultSales: false, isDefaultPurchase: true, isDefaultPos: false },
      { uomId: 1, factorToBase: '5', isDefaultSales: false, isDefaultPurchase: false, isDefaultPos: false },
    ]);
    expect(lines.map((l) => [l.uomId, l.factorToBase, l.exception])).toEqual([[1, '1', false], [2, '12', true], [4, '480', true]]);
  });

  it('procentele și numerele trec între formatul românesc și cel al API-ului', () => {
    expect(percentText('11.00')).toBe('11%');
    expect(percentText('5.50')).toBe('5,5%');
    expect(toRo('0.523')).toBe('0,523');
    expect(fromRo(' 0,523 ')).toBe('0.523');
    expect(fromRo('')).toBeNull();
  });

  it('căutarea omite filtrele goale și arată implicit doar articolele active', () => {
    expect(itemSearchQuery({ search: ' apa ', isStockable: false, showInactive: false, page: 1 }))
      .toEqual({ Search: 'apa', IsStockable: false, IsActive: true, Page: 1, PageSize: 50 });
  });
});
