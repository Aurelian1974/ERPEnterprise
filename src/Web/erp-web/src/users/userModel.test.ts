import { describe, expect, it } from 'vitest';
import { daysBetween, initials, isoToday, roleLines, userStatus } from './userModel';

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

  it('rolurile se grupează pe firmă, cele fără firmă sunt pentru toate firmele', () => {
    expect(roleLines([
      { companyName: 'Exemplu Distribuție SRL', roleName: 'Operator vânzări' },
      { companyName: null, roleName: 'Administrator' },
      { companyName: 'Exemplu Distribuție SRL', roleName: 'Gestionar' },
    ])).toEqual(['Exemplu Distribuție SRL: Operator vânzări, Gestionar', 'Toate firmele: Administrator']);
    expect(initials('Maria  Ionescu Pop')).toBe('MI');
  });
});
