import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { useWarehouses, ValuationBasis } from './inventoryApi';
import { monthStart, moneyToRo, useDischargeSimulation, useRetailDischarges, valuationApi, valuationKeys, type RetailDischarge } from './valuationApi';

/**
 * Stocuri › Evaluare › Descarcare la pret cu amanuntul (02.05, S3 3.7, 4.4; macheta s3-03): pentru o gestiune la pret cu amanuntul si o luna,
 * soldurile si rulajele din miscari, coeficientul K, adaosul si TVA-ul descarcate, costul vanzarilor si nota propusa; validarea documentului DA.
 */
export function RetailDischargePage() {
  const warehouses = useWarehouses('');
  const stores = (warehouses.data?.warehouses ?? []).filter((w) => w.valuationBasis === ValuationBasis.RetailPrice);
  const [warehouse, setWarehouse] = useState<number | undefined>();
  const [month, setMonth] = useState(new Date().toISOString().slice(0, 7));
  const current = warehouse ?? stores[0]?.id;
  const periodStart = monthStart(month);
  const simulation = useDischargeSimulation(current, periodStart);
  const discharges = useRetailDischarges(current);
  const validated = (discharges.data ?? []).find((d) => d.periodStart === periodStart);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const shown = validated ?? simulation.data;

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.valuation')} › {t('menu.retailDischarge')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.retailDischarge')}</h1>
          <div className="sub2">{t('inv.da.subtitle')}</div>
        </div>
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.retailDischarge')}>
          <div className="chips-row">
            <select className="chip" aria-label={t('inv.col.warehouse')} value={current ?? ''} onChange={(e) => setWarehouse(Number(e.target.value))}>
              {stores.map((w) => <option key={w.id} value={w.id}>{`${w.code} · ${w.name}`}</option>)}
            </select>
            <input className="chip" type="month" aria-label={t('inv.da.month')} value={month} onChange={(e) => setMonth(e.target.value)} />
          </div>
          {stores.length === 0 && <p className="muted">{t('inv.da.noStores')}</p>}
          <table className="partner-list">
            <tbody>
              {(discharges.data ?? []).map((d) => (
                <tr key={d.id} className={d.periodStart === periodStart ? 'selected' : undefined} onClick={() => setMonth(d.periodStart.slice(0, 7))}>
                  <td>
                    {`DA ${d.number ?? ''}`} <span className="pill">{t('inv.status.validated')}</span>
                    <div className="muted small">{`${d.periodStart.slice(0, 7)} · K ${d.k.toFixed(6).replace('.', ',')}`}</div>
                  </td>
                  <td className="num">{moneyToRo(d.markup)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
        <section className="card detail-card">
          {current === undefined && <p className="muted empty-detail">{t('inv.da.noStores')}</p>}
          {current !== undefined && shown === undefined && (
            <p role="status">{simulation.isError ? simulation.error.message : t('app.loading')}</p>
          )}
          {current !== undefined && shown !== undefined && (
            <div className="node-form">
              <h2 className="form-title">
                {t('inv.da.title', { month: periodStart.slice(0, 7) })}{' '}
                <span className="pill">{validated === undefined ? t('inv.da.simulation') : t('inv.status.validated')}</span>
              </h2>
              <DischargeTable discharge={shown} />
              <div className="banner dashed">
                <div>{`378 = 607   ${moneyToRo(shown.markup)}   ${t('inv.da.noteMarkup')}`}</div>
                <div>{`4428 = 607   ${moneyToRo(shown.salesVat)}   ${t('inv.da.noteVat')}`}</div>
              </div>
              {validated === undefined && (
                <div className="form-footer">
                  <span className="muted">{t('inv.da.hint')}</span>
                  <div className="actions">
                    <button type="button" className="btn primary" onClick={() => void (async () => {
                      try {
                        await valuationApi.validateDischarge(current, periodStart);
                        await queryClient.invalidateQueries({ queryKey: valuationKeys.all });
                        notify(t('common.saved'));
                      } catch (e) {
                        notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
                      }
                    })()}>
                      {t('inv.doc.validate')}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}
        </section>
      </div>
    </div>
  );
}

function DischargeTable({ discharge }: { readonly discharge: RetailDischarge }) {
  const rows: readonly [string, string][] = [
    [t('inv.da.openingMarkup'), discharge.openingMarkup],
    [t('inv.da.markupIn'), discharge.markupIn],
    [t('inv.da.openingRetail'), discharge.openingRetail],
    [t('inv.da.retailIn'), discharge.retailIn],
    [t('inv.da.openingVat'), discharge.openingVat],
    [t('inv.da.vatIn'), discharge.vatIn],
    [t('inv.da.retailOut'), discharge.retailOut],
    [t('inv.da.salesVat'), discharge.salesVat],
    [t('inv.da.salesNet'), discharge.salesNet],
    [t('inv.da.markup'), discharge.markup],
    [t('inv.da.cost'), discharge.cost],
  ];
  return (
    <table className="inline-table">
      <tbody>
        {rows.map(([label, value]) => (
          <tr key={label}>
            <td>{label}</td>
            <td className="num">{moneyToRo(value)}</td>
          </tr>
        ))}
        <tr>
          <td><strong>K</strong></td>
          <td className="num"><strong>{discharge.k.toFixed(6).replace('.', ',')}</strong></td>
        </tr>
      </tbody>
    </table>
  );
}
