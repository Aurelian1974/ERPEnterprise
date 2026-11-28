import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo, roToIso } from '../partners/partnerModel';
import { handoverStatusLabel, HandoverStatus, inventoryApi, inventoryKeys, KeeperRole, useWarehouseKeepers } from './inventoryApi';

/**
 * Tab-ul Gestionari al gestiunii (02.05, S1 3.2 si 4.2; macheta s1-02): titularul si inlocuitorii cu perioadele lor, adaugarea unui
 * gestionar, predarea-primirea (pornire, finalizare, anulare). Cu inventar obligatoriu gestiunea ramane blocata pana la finalizare.
 */
export function KeepersTab({ warehouseId, canManage }: { readonly warehouseId: number; readonly canManage: boolean }) {
  const keepers = useWarehouseKeepers(warehouseId, true);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [employee, setEmployee] = useState('');
  const [role, setRole] = useState<number>(KeeperRole.Substitute);
  const [from, setFrom] = useState('');
  const [toEmployee, setToEmployee] = useState('');
  const [handoverDate, setHandoverDate] = useState('');
  const [notes, setNotes] = useState('');

  if (keepers.data === undefined) {
    return <p role="status">{keepers.isError ? keepers.error.message : t('app.loading')}</p>;
  }
  const data = keepers.data;
  const open = data.handovers.find((h) => h.status === HandoverStatus.Draft || h.status === HandoverStatus.Counting);
  const run = async (action: () => Promise<unknown>, after?: () => void) => {
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: inventoryKeys.all });
      notify(t('common.saved'));
      after?.();
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };
  const fromIso = roToIso(from);
  const handoverIso = roToIso(handoverDate);

  return (
    <div className="form-body plain">
      <h3 className="section">{t('inv.keepers.title')}</h3>
      {data.keepers.length === 0 && <p className="muted">{t('inv.keepers.empty')}</p>}
      {data.keepers.length > 0 && (
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('inv.keepers.employee')}</th>
              <th scope="col">{t('inv.keepers.role')}</th>
              <th scope="col">{t('inv.keepers.from')}</th>
              <th scope="col">{t('inv.keepers.to')}</th>
              <th scope="col">{t('inv.keepers.note')}</th>
            </tr>
          </thead>
          <tbody>
            {data.keepers.map((k) => (
              <tr key={k.id}>
                <td>
                  {k.employeeName}
                  {k.leaveDate != null && <div className="muted small">{t('inv.keepers.leaves', { date: isoToRo(k.leaveDate) })}</div>}
                </td>
                <td>{t(k.role === KeeperRole.Holder ? 'inv.keepers.holder' : 'inv.keepers.substitute')}</td>
                <td>{isoToRo(k.validFrom)}</td>
                <td>{k.validTo == null ? t('inv.keepers.current') : isoToRo(k.validTo)}</td>
                <td className="small">{k.guaranteeNote ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {canManage && (
        <div className="chips-row">
          <select className="chip" aria-label={t('inv.keepers.employee')} value={employee} onChange={(e) => setEmployee(e.target.value)}>
            <option value="">{t('inv.keepers.choose')}</option>
            {data.employees.map((e) => <option key={e.id} value={e.id}>{e.name}</option>)}
          </select>
          <select className="chip" aria-label={t('inv.keepers.role')} value={role} onChange={(e) => setRole(Number(e.target.value))}>
            <option value={KeeperRole.Holder}>{t('inv.keepers.holder')}</option>
            <option value={KeeperRole.Substitute}>{t('inv.keepers.substitute')}</option>
          </select>
          <input className="cell-input" placeholder="zz.ll.aaaa" aria-label={t('inv.keepers.from')} value={from} onChange={(e) => setFrom(e.target.value)} />
          <button type="button" className="btn ghost" disabled={employee === '' || fromIso === undefined}
            onClick={() => void run(() => inventoryApi.saveKeeper(warehouseId, {
              id: null, warehouseId, employeeId: Number(employee), role, validFrom: fromIso ?? '', validTo: null, guaranteeNote: null, rowVersion: null,
            }), () => {
              setEmployee('');
              setFrom('');
            })}>
            {t('inv.keepers.add')}
          </button>
        </div>
      )}
      <p className="muted small">{t('inv.keepers.hint')}</p>

      <h3 className="section">{t('inv.handover.title')}</h3>
      {open !== undefined && (
        <div className="banner warn">
          <div>
            {t('inv.handover.open', {
              date: isoToRo(open.handoverDate), from: open.fromEmployeeName ?? t('common.none'), to: open.toEmployeeName,
              status: t(handoverStatusLabel(open.status)),
            })}
          </div>
          {open.requiresCount && open.inventoryCountId == null && <div className="small">{t('inv.handover.needsCount')}</div>}
          {canManage && (
            <div className="actions">
              <button type="button" className="btn primary" disabled={open.requiresCount && open.inventoryCountId == null}
                onClick={() => void run(() => inventoryApi.completeHandover(open.id, open.rowVersion))}>
                {t('inv.handover.complete')}
              </button>
              <button type="button" className="btn ghost" onClick={() => void run(() => inventoryApi.cancelHandover(open.id, open.rowVersion))}>
                {t('inv.handover.cancel')}
              </button>
            </div>
          )}
        </div>
      )}
      {open === undefined && canManage && (
        <div className="chips-row">
          <select className="chip" aria-label={t('inv.handover.to')} value={toEmployee} onChange={(e) => setToEmployee(e.target.value)}>
            <option value="">{t('inv.handover.to')}</option>
            {data.employees.map((e) => <option key={e.id} value={e.id}>{e.name}</option>)}
          </select>
          <input className="cell-input" placeholder="zz.ll.aaaa" aria-label={t('inv.handover.date')} value={handoverDate}
            onChange={(e) => setHandoverDate(e.target.value)} />
          <input className="cell-input" placeholder={t('inv.handover.notes')} aria-label={t('inv.handover.notes')} value={notes}
            onChange={(e) => setNotes(e.target.value)} />
          <button type="button" className="btn ghost" disabled={toEmployee === '' || handoverIso === undefined}
            onClick={() => void run(() => inventoryApi.startHandover(warehouseId, handoverIso ?? '', Number(toEmployee), notes.trim() === '' ? null : notes.trim()),
              () => {
                setToEmployee('');
                setHandoverDate('');
                setNotes('');
              })}>
            {t('inv.handover.start')}
          </button>
        </div>
      )}
      {data.handovers.filter((h) => h.id !== open?.id).length > 0 && (
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('inv.handover.date')}</th>
              <th scope="col">{t('inv.handover.from')}</th>
              <th scope="col">{t('inv.handover.to')}</th>
              <th scope="col">{t('inv.col.status')}</th>
            </tr>
          </thead>
          <tbody>
            {data.handovers.filter((h) => h.id !== open?.id).map((h) => (
              <tr key={h.id}>
                <td>{isoToRo(h.handoverDate)}</td>
                <td>{h.fromEmployeeName ?? t('common.none')}</td>
                <td>{h.toEmployeeName}</td>
                <td>{t(handoverStatusLabel(h.status))}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
