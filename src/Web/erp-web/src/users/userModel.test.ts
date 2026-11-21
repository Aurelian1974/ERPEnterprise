import { describe, expect, it } from 'vitest';
import { daysBetween, initials, isoToday, nodeGroups, nodeInput, nodeKey, roleLines, userStatus } from './userModel';

const base = { isActive: true, isEmployee: false, isSystem: false, validUntil: null, employeeLeaveDate: null };

describe('userModel', () => {
  it('ziua de azi e cea din București, nu UTC', () => {
    expect(isoToday(new Date('2026-11-21T22:30:00Z'))).toBe('2026-11-22');
    expect(daysBetween('2026-11-21', '2026-12-31')).toBe(40);
  });

  it('starea externului: expiră în 60 de zile, activ până la, expirat, dezactivat', () => {
    expect(userStatus({ ...base, validUntil: '2026-12-31' }, '2026-11-21')).toEqual({ kind: 'warn', text: 'Expiră 31.12.2026' });
    expect(userStatus({ ...base, validUntil: '2027-06-30' }, '2026-11-21')).toEqual({ kind: 'ok', text: 'Activ până 30.06.2027' });
    expect(userStatus({ ...base, validUntil: '2026-11-20' }, '2026-11-21').kind).toBe('bad');
    expect(userStatus({ ...base, isActive: false, validUntil: '2027-06-30' }, '2026-11-21')).toEqual({ kind: 'off', text: 'Dezactivat' });
  });

  it('angajatul cu data încetării azi nu mai are acces; contul de sistem nu expiră', () => {
    expect(userStatus({ ...base, isEmployee: true, employeeLeaveDate: '2026-11-21' }, '2026-11-21').kind).toBe('bad');
    expect(userStatus({ ...base, isEmployee: true }, '2026-11-21')).toEqual({ kind: 'ok', text: 'Activ' });
    expect(userStatus({ ...base, isSystem: true, validUntil: '2020-01-01' }, '2026-11-21').kind).toBe('ok');
  });

  it('rolurile se grupează pe nod: grup, firmă, punct de lucru; cele fără nod sunt pentru toate firmele', () => {
    expect(roleLines([
      { companyId: 3, companyName: 'Exemplu Distribuție SRL', roleName: 'Operator vânzări' },
      { companyName: null, roleName: 'Administrator' },
      { companyId: 3, companyName: 'Exemplu Distribuție SRL', roleName: 'Gestionar' },
      { companyId: 3, companyName: 'Exemplu Distribuție SRL', branchId: 5, branchName: 'Punct Nord', roleName: 'Gestionar' },
      { companyGroupId: 4, groupName: 'Holding', roleName: 'Auditor' },
    ])).toEqual(['Exemplu Distribuție SRL: Operator vânzări, Gestionar', 'Toate firmele: Administrator',
      'Exemplu Distribuție SRL › Punct Nord: Gestionar', 'Grupul Holding: Auditor']);
    expect(initials('Maria  Ionescu Pop')).toBe('MI');
  });

  it('nodul unei atribuiri trece în cheie și înapoi, iar lista pune punctele de lucru și locațiile sub firmă', () => {
    for (const key of ['', 'g4', 'c3', 'b5', 'l9']) {
      expect(nodeKey({ ...nodeInput(key) })).toBe(key);
    }
    expect(nodeInput('b5')).toEqual({ companyGroupId: null, companyId: null, branchId: 5, locationId: null });
    const groups = nodeGroups({
      companies: [{ id: 3, name: 'Exemplu SRL' }],
      groups: [{ id: 4, name: 'Holding' }],
      branches: [{ id: 5, companyId: 3, name: 'Punct Nord' }],
      locations: [{ id: 9, companyId: 3, branchId: 5, name: 'Magazin' }, { id: 10, companyId: 3, branchId: null, name: 'Depozit' }],
    }, false);
    expect(groups.map((g) => g.label)).toEqual(['Grupuri de firme', 'Exemplu SRL']);
    expect(groups[1]?.options.map((o) => o.value)).toEqual(['c3', 'b5', 'l9', 'l10']);
    expect(nodeGroups({ companies: [], groups: [], branches: [], locations: [] }, true)[0]?.options[0]?.value).toBe('');
  });
});
