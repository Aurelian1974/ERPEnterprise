import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import {
  DocumentStatus, documentKeys, documentsApi, receiptDifference, resolutions, shipmentStatusLabel, useStockDocument, useTransitShipments,
  type StockDocument,
} from './documentsApi';

/**
 * Stocuri › De primit (02.05, S2 3.4, 4.3; macheta s2-02): expeditiile in tranzit spre gestiunile permise; nota de primire cu cantitatile
 * trimise si primite si rezolvarea fiecarei diferente (ramane in tranzit, retur, lipsa imputabila sau nu, plus).
 */
export function ReceiptsPage() {
  const [openOnly, setOpenOnly] = useState(true);
  const shipments = useTransitShipments(openOnly);
  const [receiptId, setReceiptId] = useState<number | undefined>();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.stockDocuments')} › {t('menu.receipts')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.receipts')}</h1>
          <div className="sub2">{t('inv.receipts.subtitle')}</div>
        </div>
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.receipts')}>
          <label className="check">
            <input type="checkbox" checked={openOnly} onChange={(e) => setOpenOnly(e.target.checked)} /> {t('inv.receipts.openOnly')}
          </label>
          {shipments.data === undefined && <p role="status">{shipments.isError ? t('app.loadError') : t('app.loading')}</p>}
          {shipments.data?.length === 0 && <p className="muted">{t('inv.receipts.empty')}</p>}
          {shipments.data !== undefined && shipments.data.length > 0 && (
            <table className="partner-list">
              <thead>
                <tr>
                  <th scope="col">{t('inv.doc.NP')}</th>
                  <th scope="col">{t('inv.col.warehouse')}</th>
                  <th scope="col">{t('inv.col.status')}</th>
                </tr>
              </thead>
              <tbody>
                {shipments.data.map((s) => (
                  <tr key={s.id}>
                    <td>
                      {s.npNumber ?? s.npDocumentId}
                      <div className="muted small">{`${isoToRo(s.shippedDate)} · ${t('inv.receipts.age', { days: s.ageDays })}`}</div>
                    </td>
                    <td className="small">{`${s.fromWarehouseCode} → ${s.toWarehouseCode}`}</td>
                    <td>
                      <span className="pill">{t(shipmentStatusLabel(s.status))}</span>{' '}
                      {(s.status === 1 || s.status === 2) && (
                        <button type="button" className="btn ghost small" onClick={() => void (async () => {
                          try {
                            const created = await documentsApi.createReceipt(s.id, new Date().toISOString().slice(0, 10));
                            await queryClient.invalidateQueries({ queryKey: documentKeys.all });
                            setReceiptId(created.id);
                          } catch (e) {
                            notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
                          }
                        })()}>
                          {t('inv.receipts.receive')}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <section className="card detail-card">
          {receiptId === undefined ? <p className="muted empty-detail">{t('inv.receipts.select')}</p> : <ReceiptPanel key={receiptId} id={receiptId} />}
        </section>
      </div>
    </div>
  );
}

function ReceiptPanel({ id }: { readonly id: number }) {
  const receipt = useStockDocument(id);
  if (receipt.data === undefined) {
    return <p role="status">{receipt.isError ? receipt.error.message : t('app.loading')}</p>;
  }
  return <ReceiptEditor key={receipt.data.rowVersion} receipt={receipt.data} />;
}

function ReceiptEditor({ receipt }: { readonly receipt: StockDocument }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const draft = receipt.status === DocumentStatus.Draft;
  const [rows, setRows] = useState(() => receipt.lines.map((l) => ({
    lineId: l.id, received: (l.receivedQtyBase ?? l.qtyBase).replace('.', ','), resolution: l.differenceResolution ?? null,
  })));
  const run = async (action: () => Promise<unknown>) => {
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: ['inventory'] });
      notify(t('common.saved'));
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };
  const body = () => rows.map((r) => ({ lineId: r.lineId, receivedQtyBase: r.received.trim().replace(',', '.'), resolution: r.resolution }));

  return (
    <div className="node-form">
      <h2 className="form-title">{`NR ${receipt.number ?? t('inv.doc.draftNumber')}`} <span className="pill">{t(DocumentStatus.Draft === receipt.status ? 'inv.status.draft' : 'inv.status.validated')}</span></h2>
      <table className="inline-table">
        <thead>
          <tr>
            <th scope="col">{t('inv.item.variant')}</th>
            <th scope="col">{t('inv.receipts.sent')}</th>
            <th scope="col">{t('inv.receipts.received')}</th>
            <th scope="col">{t('inv.receipts.resolution')}</th>
          </tr>
        </thead>
        <tbody>
          {receipt.lines.map((line, index) => {
            const row = rows[index] ?? { lineId: line.id, received: line.qtyBase, resolution: null };
            const difference = receiptDifference(line.qtyBase, row.received);
            return (
              <tr key={line.id}>
                <td>{`${line.variantCode} · ${line.itemName}`}</td>
                <td className="num">{line.qtyBase.replace('.', ',')}</td>
                <td>
                  <input className="cell-input num" value={row.received} disabled={!draft} aria-invalid={difference === 'invalid'} aria-label={t('inv.receipts.received')}
                    onChange={(e) => setRows(rows.map((r, i) => (i === index ? { ...r, received: e.target.value } : r)))} />
                </td>
                <td>
                  {difference === 'shortage' || difference === 'surplus' ? (
                    <select className="cell-input" value={row.resolution ?? ''} disabled={!draft} aria-label={t('inv.receipts.resolution')}
                      onChange={(e) => setRows(rows.map((r, i) => (i === index ? { ...r, resolution: e.target.value === '' ? null : Number(e.target.value) } : r)))}>
                      <option value="">{t('inv.receipts.choose')}</option>
                      {resolutions.filter((o) => o.forShortage === (difference === 'shortage')).map((o) => <option key={o.value} value={o.value}>{t(o.label)}</option>)}
                    </select>
                  ) : (
                    <span className="muted small">{t('inv.receipts.noDifference')}</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {draft && (
        <div className="form-footer">
          <span className="muted">{t('inv.receipts.hint')}</span>
          <div className="actions">
            <button type="button" className="btn ghost" onClick={() => void run(() => documentsApi.saveReceipt(receipt.id, body(), receipt.rowVersion))}>
              {t('common.save')}
            </button>
            <button type="button" className="btn primary" onClick={() => void run(async () => {
              const saved = await documentsApi.saveReceipt(receipt.id, body(), receipt.rowVersion);
              await documentsApi.validate(receipt.id, saved.rowVersion);
            })}>
              {t('inv.doc.validate')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
