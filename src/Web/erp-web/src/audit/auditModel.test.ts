import { describe, expect, it } from 'vitest';
import { actionOption, actionText, actionTone, diffChanges, entityLink, entityName, fieldLabel, formatValue, intervalStart, loginReason } from './auditModel';

describe('jurnalul de audit', () => {
  it('salvările devin Creare sau Modificare, celelalte acțiuni își păstrează textul', () => {
    expect(actionText('Core.Partner.Save', true)).toBe('Creare');
    expect(actionText('Core.Partner.Save', false)).toBe('Modificare');
    expect(actionText('Core.User.ResetPassword')).toBe('Resetare parolă');
    expect(actionText('Core.Altceva.Ruleaza')).toBe('Ruleaza');
    expect(actionOption('Core.Dimension.Save')).toBe('Dimensiune · Salvare');
    expect(entityName('Core.Necunoscut')).toBe('Necunoscut');
  });

  it('vizualizarea datelor personale e avertizare, ștergerea e eroare', () => {
    expect(actionTone('Core.Partner.ViewPersonalData')).toBe('warn');
    expect(actionTone('Core.Role.Delete')).toBe('error');
    expect(actionTone('Core.Partner.Save')).toBe('info');
  });

  it('față de intrarea anterioară apar doar câmpurile schimbate', () => {
    const rows = diffChanges(
      '{"name":"Logistic Partner SRL","vatOnCollection":true,"paymentTermDays":45,"creditLimit":75000.5,"isActive":true}',
      '{"name":"Logistic Partner SRL","vatOnCollection":false,"paymentTermDays":30,"creditLimit":50000,"isActive":true}');
    expect(rows).toEqual([
      { field: 'vatOnCollection', before: 'Nu', after: 'Da' },
      { field: 'paymentTermDays', before: '30', after: '45' },
      { field: 'creditLimit', before: '50000', after: '75.000,5' },
    ]);
    expect(fieldLabel('vatOnCollection')).toBe('TVA la încasare');
    expect(fieldLabel('altCamp')).toBe('altCamp');
  });

  it('la creare apar toate câmpurile completate; JSON lipsă sau greșit nu strică ecranul', () => {
    expect(diffChanges('{"code":"P01","parentId":null,"validFrom":"2026-01-01"}', null))
      .toEqual([{ field: 'code', before: undefined, after: 'P01' }, { field: 'validFrom', before: undefined, after: '01.01.2026' }]);
    expect(diffChanges(null, null)).toEqual([]);
    expect(diffChanges('nu e json', '{}')).toEqual([]);
    expect(diffChanges('{"permissionIds":[3,1]}', '{"permissionIds":[1]}')).toEqual([{ field: 'permissionIds', before: '1', after: '3, 1' }]);
  });

  it('formatează valorile și leagă înregistrările care au fișă proprie', () => {
    expect([formatValue(null), formatValue(''), formatValue(false)]).toEqual(['—', '—', 'Nu']);
    expect(entityLink('Core.Partner', 7)).toBe('/nomenclatoare/parteneri?id=7');
    expect(entityLink('Core.Company', 3)).toBe('/admin/firme?nod=c-3');
    expect(entityLink('Core.VatRate', 3)).toBeUndefined();
    expect(entityLink('Core.Partner', null)).toBeUndefined();
    expect(loginReason('LockedOut')).toBe('Cont blocat după încercări greșite');
    expect(loginReason('Altul')).toBe('Altul');
  });

  it('începutul intervalului e rotunjit la minut; „tot jurnalul” nu are limită', () => {
    const now = new Date('2026-11-21T10:30:45Z');
    expect(intervalStart('all', now)).toBeUndefined();
    expect(intervalStart('7', now)).toBe('2026-11-14T10:30:00.000Z');
  });
});
