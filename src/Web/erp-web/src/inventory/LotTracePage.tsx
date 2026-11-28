import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { deliveredSummary, lotsApi, qtyToRo, useLots, useLotTrace, type LotTraceMovement } from './lotsApi';

const SalesTypes = ['ISSUE_SALE', 'POS_SALE', 'RETURN_FROM_CUSTOMER'];

/** Clientii din trasabilitate (inainte), grupati pe partener: gestiunile, documentele, bucatile nete si ultima livrare. */
export function traceCustomers(forward: readonly LotTraceMovement[]) {
  const map = new Map<number, { name: string; warehouses: Set<string>; documents: Set<string>; qty: number; last: string }>();
  for (const m of forward.filter((x) => x.partnerId != null && SalesTypes.includes(x.movementTypeCode))) {
    const entry = map.get(m.partnerId ?? 0) ?? { name: m.partnerName ?? '—', warehouses: new Set<string>(), documents: new Set<string>(), qty: 0, last: m.movementDate };
    entry.warehouses.add(m.warehouseCode);
    entry.documents.add(`${m.sourceDocumentType} ${m.sourceDocumentNumber ?? m.sourceDocumentId}`);
    entry.qty -= Number(m.qtyBase);
    entry.last = m.movementDate > entry.last ? m.movementDate : entry.last;
    map.set(m.partnerId ?? 0, entry);
  }
  return [...map.entries()].map(([partnerId, e]) => ({ partnerId, ...e })).filter((e) => e.qty !== 0).sort((a, b) => b.qty - a.qty);
}

/**
 * Stocuri › Stoc › Trasabilitate (02.05, S4 3.6; macheta s4-05): drumul unui lot de la furnizor la clienti — inapoi prin genealogie, inainte prin
 * miscarile lotului si ale loturilor rezultate, stocul acum (inclusiv tranzitul) — si pornirea retragerii lui.
 */
export function LotTracePage() {
  const [params, setParams] = useSearchParams();
  const lotId = params.get('lot') === null ? undefined : Number(params.get('lot'));
  const [search, setSearch] = useState('');
  const found = useLots(undefined, undefined, search);
  const trace = useLotTrace(lotId);
  const navigate = useNavigate();
  const { notify } = useNotifier();
  const options = search.trim() === '' ? [] : [...new Map((found.data ?? []).map((l) => [l.lotId, l])).values()].slice(0, 8);
  const data = trace.data;
  const customers = data === undefined ? [] : traceCustomers(data.forward);
  const summary = data === undefined ? undefined : deliveredSummary(data.forward);
  const startRecall = async () => {
    if (data === undefined) {
      return;
    }
    try {
      const saved = await lotsApi.saveRecall({
        id: null, documentDate: new Date().toISOString().slice(0, 10), sourceKind: 2, reason: t('inv.trace.defaultReason', { lot: data.ckuCode }),
        supplierPartnerId: null, supplierLotCode: null, lotIds: [data.lotId], rowVersion: null,
      });
      void navigate(`/stocuri/retrageri?id=${saved.id}`);
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.stock')} › {t('menu.lotTrace')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.lotTrace')}</h1>
          <div className="sub2">{t('inv.trace.subtitle')}</div>
        </div>
        {data !== undefined && <button type="button" className="btn primary" onClick={() => void startRecall()}>{t('inv.trace.startRecall')}</button>}
      </div>
      <section className="card">
        <div className="chips-row">
          {data !== undefined && <span className="chip on">{`${t('inv.lot.lot')} ${data.ckuCode}`}</span>}
          {data?.supplierLotCode != null && <span className="chip on">{`${t('inv.lot.supplierLot')} ${data.supplierLotCode}`}</span>}
          <input className="cell-input" placeholder={t('inv.lot.search')} aria-label={t('inv.lot.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
          {options.map((l) => (
            <button key={l.lotId} type="button" className="chip" onClick={() => { setParams({ lot: String(l.lotId) }); setSearch(''); }}>
              {`${l.lotCode} · ${l.variantCode}`}
            </button>
          ))}
        </div>
        {data !== undefined && (
          <div className="muted small">
            {t('inv.trace.back')}{' '}
            {[data.supplierName, data.receiptDocument, data.firstReceiptDate == null ? null : isoToRo(data.firstReceiptDate)].filter(Boolean).join(' · ') || '—'}
            {data.back.map((b) => ` · ${t('inv.trace.fromLot', { cku: b.ckuCode, depth: b.depth })}`).join('')}
          </div>
        )}
      </section>
      {lotId === undefined && <p className="muted">{t('inv.trace.select')}</p>}
      {trace.isError && <p role="alert">{trace.error.message}</p>}
      {data !== undefined && summary !== undefined && (
        <section className="card">
          <h2 className="form-title">{t('inv.trace.forward')}</h2>
          <div className="muted small">
            {t('inv.trace.summary', { received: qtyToRo(summary.received), delivered: qtyToRo(summary.delivered), customers: summary.customers,
              stock: qtyToRo(data.stockNow.reduce((s, x) => s + Number(x.qtyOnHand), 0)) })}
          </div>
          <table className="inline-table">
            <thead>
              <tr>
                <th scope="col">{t('inv.trace.customer')}</th>
                <th scope="col">{t('inv.trace.from')}</th>
                <th scope="col">{t('inv.trace.documents')}</th>
                <th scope="col" className="num">{t('inv.serial.pieces')}</th>
                <th scope="col">{t('inv.trace.last')}</th>
              </tr>
            </thead>
            <tbody>
              {customers.map((c) => (
                <tr key={c.partnerId}>
                  <td>{c.name}</td>
                  <td><code>{[...c.warehouses].join(', ')}</code></td>
                  <td>{c.documents.size === 1 ? [...c.documents][0] : t('inv.trace.documentCount', { count: c.documents.size })}</td>
                  <td className="num">{qtyToRo(c.qty)}</td>
                  <td>{isoToRo(c.last)}</td>
                </tr>
              ))}
              {customers.length === 0 && <tr><td colSpan={5} className="muted">{t('inv.trace.noCustomers')}</td></tr>}
            </tbody>
          </table>
          <h3 className="section-title">{t('inv.trace.stockNow')}</h3>
          <table className="inline-table">
            <tbody>
              {data.stockNow.map((s) => <tr key={s.warehouseId}><td><code>{s.warehouseCode}</code></td><td className="num"><strong>{qtyToRo(s.qtyOnHand)}</strong></td></tr>)}
              {data.stockNow.length === 0 && <tr><td className="muted">{t('inv.lot.noStock')}</td></tr>}
            </tbody>
          </table>
          <h3 className="section-title">{t('inv.trace.movements')}</h3>
          <table className="inline-table">
            <thead>
              <tr>
                <th scope="col">{t('inv.doc.date')}</th>
                <th scope="col">{t('inv.lot.lot')}</th>
                <th scope="col">{t('inv.serial.movement')}</th>
                <th scope="col">{t('inv.col.warehouse')}</th>
                <th scope="col">{t('inv.serial.document')}</th>
                <th scope="col" className="num">{t('inv.serial.pieces')}</th>
              </tr>
            </thead>
            <tbody>
              {data.forward.slice(0, 200).map((m) => (
                <tr key={m.movementId}>
                  <td>{isoToRo(m.movementDate)}</td>
                  <td>{m.depth > 0 ? `↳ ${m.ckuCode}` : m.ckuCode}</td>
                  <td><code>{m.movementTypeCode}</code></td>
                  <td><code>{m.warehouseCode}</code></td>
                  <td>{`${m.sourceDocumentType} ${m.sourceDocumentNumber ?? ''}`}{m.partnerName == null ? '' : ` · ${m.partnerName}`}</td>
                  <td className="num">{qtyToRo(m.qtyBase)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {data.forward.length > 200 && <div className="muted small">{t('inv.trace.more', { count: data.forward.length - 200 })}</div>}
        </section>
      )}
    </div>
  );
}
