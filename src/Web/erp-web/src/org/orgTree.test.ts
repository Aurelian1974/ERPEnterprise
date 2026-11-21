import { describe, expect, it } from 'vitest';
import { buildTree, type OrgTree } from './orgTree';

const rv = 'AAAAAAAAB9E=';
const tree: OrgTree = {
  groups: [
    { id: 1, code: 'GRP', name: 'Grupul Exemplu', parentGroupId: null, parentCompanyId: 10, isActive: true, rowVersion: rv },
    { id: 2, code: 'SUB', name: 'Subgrup Construcții', parentGroupId: 1, parentCompanyId: null, isActive: true, rowVersion: rv },
  ],
  companies: [
    { id: 10, groupId: 1, code: 'EXH', name: 'Exemplu Holding SA', fiscalCode: '10000001', vatPayer: true, isActive: true },
    { id: 11, groupId: 2, code: 'EXC', name: 'Exemplu Construct SRL', fiscalCode: '34567890', vatPayer: false, isActive: true },
  ],
  branches: [
    { id: 20, companyId: 10, code: 'PL-CJ', name: 'Punct de lucru Cluj', address: 'Str. 1', city: 'Cluj-Napoca', citySirutaCode: null, countyCode: 'CJ', countryCode: 'RO', postalCode: null, phone: null, email: null, isActive: true, rowVersion: rv },
  ],
  locations: [
    { id: 30, companyId: 10, branchId: 20, code: 'DEP-CJ', name: 'Depozit Brașov', locationType: 2, address: null, city: null, citySirutaCode: null, countyCode: null, countryCode: null, postalCode: null, phone: null, email: null, isActive: true, rowVersion: rv },
    { id: 31, companyId: 11, branchId: null, code: 'SH', name: 'Șantier Hala Bacău', locationType: 5, address: null, city: null, citySirutaCode: null, countyCode: null, countryCode: null, postalCode: null, phone: null, email: null, isActive: false, rowVersion: rv },
  ],
  contacts: [],
};

const typeName = (type: number) => (type === 2 ? 'depozit' : 'șantier');
const ids = (filter: Partial<Parameters<typeof buildTree>[1]>) =>
  buildTree(tree, { search: '', onlyCompanies: false, showInactive: false, ...filter }, typeName).map((n) => n.id).sort();

describe('arborele structurii', () => {
  it('leagă nodurile de părinți, marchează firma-mamă și ascunde implicit nodurile inactive', () => {
    const nodes = buildTree(tree, { search: '', onlyCompanies: false, showInactive: false }, typeName);

    // Ordinea din macheta 03: firma-mama, firmele, punctele de lucru, locatiile, apoi subgrupurile.
    expect(nodes.map((n) => [n.id, n.parentId])).toEqual([
      ['c-10', 'g-1'],
      ['c-11', 'g-2'],
      ['b-20', 'c-10'],
      ['l-30', 'b-20'],
      ['g-1', undefined],
      ['g-2', 'g-1'],
    ]);
    expect(nodes.find((n) => n.id === 'c-10')?.isGroupParent).toBe(true);
    expect(nodes.find((n) => n.id === 'c-10')?.detail).toBe('RO10000001');
    expect(nodes.find((n) => n.id === 'c-11')?.hasChildren).toBe(false);
  });

  it('arată inactivele la cerere și doar firmele cu filtrul „Doar firme”', () => {
    expect(ids({ showInactive: true })).toContain('l-31');
    expect(ids({ onlyCompanies: true })).toEqual(['c-10', 'c-11', 'g-1', 'g-2']);
  });

  it('caută fără diacritice și păstrează strămoșii rezultatului', () => {
    expect(ids({ search: 'brasov' })).toEqual(['b-20', 'c-10', 'g-1', 'l-30']);
    expect(ids({ search: '34567890' })).toEqual(['c-11', 'g-1', 'g-2']);
  });

  it('un nod al cărui părinte nu e vizibil devine rădăcină', () => {
    const partial: OrgTree = { ...tree, groups: [] };
    const nodes = buildTree(partial, { search: '', onlyCompanies: false, showInactive: false }, typeName);
    expect(nodes.find((n) => n.id === 'c-10')?.parentId).toBeUndefined();
  });
});
