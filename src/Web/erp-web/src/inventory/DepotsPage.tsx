import { useState } from 'react';
import { useOutletContext } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import type { CurrentUser } from '../api/session';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { inventoryApi, inventoryKeys, StockLevel, useDepots, useWarehouses, type DepotItem } from './inventoryApi';

const levelLabel = (level: number) => t(level === StockLevel.Depot ? 'inv.level.depot' : 'inv.level.warehouse');
const sourceLabel = (source: number) => t(source === 3 ? 'inv.level.fromLocation' : source === 2 ? 'inv.level.fromBranch' : 'inv.level.fromCompany');

/**
 * Stocuri › Depozite (02.05, S1 3.8; macheta s1-05): nivelul de evidenta al firmei si al fiecarei locatii (cu sursa lui), depozitele din
 * locatiile cu nivelul „depozit”, gestiunile din fiecare depozit si gestiunea implicita.
 */
export function DepotsPage() {
  const user = useOutletContext<CurrentUser>();
  const depots = useDepots();
  const warehouses = useWarehouses('');
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const canManage = user.permissions.includes('Inv.Warehouse.Manage');
  const canSettings = user.permissions.includes('Inv.Settings.Manage');
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [location, setLocation] = useState('');

  if (depots.data === undefined) {
    return <div className="page" role="status">{depots.isError ? t('app.loadError') : t('app.loading')}</div>;
  }
  const data = depots.data;
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
  const depotLocations = data.locations.filter((l) => l.level === StockLevel.Depot);
  const candidates = (warehouses.data?.warehouses ?? []).filter((w) => w.isActive && w.locationId == null && (w.kind === 1 || w.kind === 2 || w.kind === 4));

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.depots')}</div>
      <div className="head">
        <div>
          <h1>{t('inv.depots.title')}</h1>
          <div className="sub2">{t('inv.depots.subtitle')}</div>
        </div>
      </div>

      <section className="card" aria-label={t('inv.level.title')}>
        <h2>{t('inv.level.title')}</h2>
        <p>
          {t('inv.level.company')}: <strong>{levelLabel(data.companyLevel)}</strong>
        </p>
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('inv.field.location')}</th>
              <th scope="col">{t('inv.level.label')}</th>
              <th scope="col">{t('inv.level.source')}</th>
              {canSettings && <th scope="col"><span className="sr-only">{t('inv.level.change')}</span></th>}
            </tr>
          </thead>
          <tbody>
            {data.locations.map((l) => (
              <tr key={l.id}>
                <td>{`${l.code} · ${l.name}`}</td>
                <td>{levelLabel(l.level)}</td>
                <td className="small">{sourceLabel(l.source)}</td>
                {canSettings && (
                  <td>
                    <button type="button" className="btn ghost small" onClick={() => void run(() => inventoryApi.setStockLevel({
                      branchId: null, locationId: l.id, level: l.level === StockLevel.Depot ? StockLevel.Warehouse : StockLevel.Depot,
                    }))}>
                      {l.level === StockLevel.Depot ? t('inv.level.toWarehouse') : t('inv.level.toDepot')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
        <p className="muted small">{t('inv.level.hint')}</p>
      </section>

      <section className="card" aria-label={t('inv.depots.title')}>
        <h2>{t('inv.depots.list')}</h2>
        {data.depots.length === 0 && <p className="muted">{t('inv.depots.empty')}</p>}
        {data.depots.map((d) => (
          <DepotCard key={d.id} depot={d} candidates={candidates} canManage={canManage} run={run} />
        ))}
        {canManage && depotLocations.length > 0 && (
          <div className="chips-row">
            <input className="cell-input" placeholder={t('inv.field.code')} aria-label={t('inv.field.code')} value={code}
              onChange={(e) => setCode(e.target.value.toUpperCase())} />
            <input className="cell-input" placeholder={t('inv.field.name')} aria-label={t('inv.field.name')} value={name}
              onChange={(e) => setName(e.target.value)} />
            <select className="chip" aria-label={t('inv.field.location')} value={location} onChange={(e) => setLocation(e.target.value)}>
              <option value="">{t('inv.field.location')}</option>
              {depotLocations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
            </select>
            <button type="button" className="btn primary" disabled={code.trim() === '' || name.trim() === '' || location === ''}
              onClick={() => void run(() => inventoryApi.saveDepot({
                id: null, code: code.trim(), name: name.trim(), locationId: Number(location), defaultWarehouseId: null, isActive: true, rowVersion: null,
              }), () => {
                setCode('');
                setName('');
              })}>
              {t('inv.depots.new')}
            </button>
          </div>
        )}
        <p className="muted small">{t('inv.depots.hint')}</p>
      </section>
    </div>
  );
}

function DepotCard({ depot, candidates, canManage, run }: {
  readonly depot: DepotItem;
  readonly candidates: readonly { readonly id: number; readonly code: string; readonly name: string }[];
  readonly canManage: boolean;
  readonly run: (action: () => Promise<unknown>) => Promise<void>;
}) {
  const [adding, setAdding] = useState('');
  const inDepot = new Set(depot.warehouses.map((w) => w.id));
  return (
    <div className="banner dashed">
      <strong>{`${depot.code} · ${depot.name}`}</strong> <span className="muted">{depot.locationName}</span>{' '}
      {depot.isBlocked && <span className="pill warn">{t('inv.blocked')}</span>}{' '}
      <span className={depot.isActive ? 'pill ok' : 'pill off'}>{depot.isActive ? t('common.active') : t('common.inactive')}</span>
      <ul>
        {depot.warehouses.map((w) => (
          <li key={w.id}>
            {`${w.code} · ${w.name}`}
            {w.id === depot.defaultWarehouseId && <span className="pill ok">{t('inv.depots.default')}</span>}
            {canManage && w.id !== depot.defaultWarehouseId && (
              <>
                {' '}
                <button type="button" className="btn ghost small" onClick={() => void run(() => inventoryApi.saveDepot({
                  id: depot.id, code: depot.code, name: depot.name, locationId: depot.locationId, defaultWarehouseId: w.id, isActive: depot.isActive,
                  rowVersion: depot.rowVersion,
                }))}>
                  {t('inv.depots.makeDefault')}
                </button>
                <button type="button" className="btn ghost small" onClick={() => void run(() => inventoryApi.setDepotWarehouse(depot.id, w.id, false))}>
                  {t('inv.depots.remove')}
                </button>
              </>
            )}
          </li>
        ))}
      </ul>
      {canManage && (
        <div className="chips-row">
          <select className="chip" aria-label={t('inv.depots.addWarehouse')} value={adding} onChange={(e) => setAdding(e.target.value)}>
            <option value="">{t('inv.depots.addWarehouse')}</option>
            {candidates.filter((c) => !inDepot.has(c.id)).map((c) => <option key={c.id} value={c.id}>{`${c.code} · ${c.name}`}</option>)}
          </select>
          <button type="button" className="btn ghost" disabled={adding === ''}
            onClick={() => void run(() => inventoryApi.setDepotWarehouse(depot.id, Number(adding), true)).then(() => setAdding(''))}>
            {t('inv.depots.add')}
          </button>
        </div>
      )}
    </div>
  );
}
