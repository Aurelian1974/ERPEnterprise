import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { adjustmentKindLabel, moneyToRo, useCostAdjustments, useRecalcQueue, valuationApi, valuationKeys } from './valuationApi';

/**
 * Stocuri › Evaluare › Recalculare cost (02.05, S3 3.5; macheta s3-02): coada de recalculare (cheile refacute de job la 15 minute sau la cerere)
 * si ultimele ajustari de cost: recalculari, regularizari ale negativului, costuri adaugate, diferente de pret.
 */
export function CostRecalcPage() {
  const queue = useRecalcQueue();
  const [kind, setKind] = useState<number | undefined>();
  const adjustments = useCostAdjustments(kind);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.valuation')} › {t('menu.costRecalc')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.costRecalc')}</h1>
          <div className="sub2">{t('inv.recalc.subtitle')}</div>
        </div>
      </div>
      <section className="card">
        <h2 className="form-title">{t('inv.recalc.queue')}</h2>
        {queue.data?.length === 0 && <p className="muted">{t('inv.recalc.empty')}</p>}
        {queue.data !== undefined && queue.data.length > 0 && (
          <table className="inline-table">
            <thead>
              <tr>
                <th scope="col">{t('inv.col.warehouse')}</th>
                <th scope="col">{t('inv.item.variant')}</th>
                <th scope="col">{t('inv.recalc.from')}</th>
                <th scope="col">{t('inv.recalc.enqueued')}</th>
                <th scope="col" />
              </tr>
            </thead>
            <tbody>
              {queue.data.map((q) => (
                <tr key={`${q.warehouseId}-${q.depotId ?? 0}-${q.itemVariantId}-${q.lotId ?? 0}`}>
                  <td>{q.warehouseCode}</td>
                  <td>{q.variantCode}</td>
                  <td>{isoToRo(q.fromDate)}</td>
                  <td className="small">{new Date(q.enqueuedAt).toLocaleString('ro-RO')}</td>
                  <td>
                    <button type="button" className="btn ghost small" onClick={() => void (async () => {
                      try {
                        const changed = await valuationApi.recalculate(q);
                        await queryClient.invalidateQueries({ queryKey: valuationKeys.all });
                        notify(t('inv.recalc.done', { count: changed }));
                      } catch (e) {
                        notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
                      }
                    })()}>
                      {t('inv.recalc.run')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      <section className="card">
        <h2 className="form-title">{t('inv.recalc.adjustments')}</h2>
        <div className="chips-row">
          {[undefined, 1, 2, 3, 4].map((k) => (
            <button key={k ?? 0} type="button" className={`chip${kind === k ? ' on' : ''}`} onClick={() => setKind(k)}>
              {k === undefined ? t('inv.filter.all') : t(adjustmentKindLabel(k))}
            </button>
          ))}
        </div>
        <table className="inline-table">
          <thead>
            <tr>
              <th scope="col">{t('inv.recalc.date')}</th>
              <th scope="col">{t('inv.recalc.kind')}</th>
              <th scope="col">{t('inv.col.warehouse')}</th>
              <th scope="col">{t('inv.item.variant')}</th>
              <th scope="col">{t('inv.recalc.document')}</th>
              <th scope="col" className="num">{t('inv.recalc.amount')}</th>
              <th scope="col">{t('inv.recalc.inStock')}</th>
            </tr>
          </thead>
          <tbody>
            {(adjustments.data ?? []).map((a) => (
              <tr key={a.id}>
                <td>{isoToRo(a.accountingDate)}</td>
                <td>{t(adjustmentKindLabel(a.kind))}</td>
                <td>{a.warehouseCode}</td>
                <td>{a.variantCode}</td>
                <td className="small">{a.sourceDocumentType ? `${a.sourceDocumentType} ${a.sourceDocumentId ?? ''}` : '—'}</td>
                <td className="num">{moneyToRo(a.amount)}</td>
                <td>{a.inStock ? t('inv.recalc.stock') : t('inv.recalc.movement')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
