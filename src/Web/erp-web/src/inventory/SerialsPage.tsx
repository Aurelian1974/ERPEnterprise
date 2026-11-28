import { useState } from 'react';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { VariantPicker, WarehouseSelect } from './LotPickers';
import { qtyToRo, SerialStatus, serialStatusLabel, useSerial, useSerials, type SerialItem } from './lotsApi';

const serialTone = (status: number): string =>
  (({ 1: 'ok', 2: 'info', 3: 'off', 4: 'warn', 5: 'bad', 6: 'bad' }) as Record<number, string>)[status] ?? 'ok';

/** Linia a doua din lista seriilor (macheta s4-04): unde e bucata, cui s-a vandut si pana cand are garantie. */
export function serialWhere(s: Pick<SerialItem, 'status' | 'warehouseCode' | 'soldToName' | 'warrantyUntil' | 'lotCode'>): string {
  const parts = s.status === SerialStatus.Sold
    ? [s.soldToName, s.warrantyUntil == null ? null : t('inv.serial.warrantyUntil', { date: isoToRo(s.warrantyUntil) })]
    : [s.warehouseCode, s.lotCode == null ? null : `${t('inv.lot.lot')} ${s.lotCode}`];
  return parts.filter(Boolean).join(' · ');
}

/** Stocuri › Stoc › Serii (02.05, S4 3.4; macheta s4-04): bucatile urmarite pe serie, starea, locul, clientul, garantia, identificatorii si istoricul. */
export function SerialsPage() {
  const [variant, setVariant] = useState<{ id: number; label: string } | undefined>();
  const [warehouse, setWarehouse] = useState<number | undefined>();
  const [status, setStatus] = useState<number | undefined>();
  const [search, setSearch] = useState('');
  const serials = useSerials(variant?.id, warehouse, status, search);
  const rows = serials.data ?? [];
  const [selected, setSelected] = useState<number | undefined>();
  const count = (s: number) => rows.filter((r) => r.status === s).length;

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Inventory')} › {t('menu.stock')} › {t('menu.serials')}</div>
      <div className="head">
        <div>
          <h1>{t('menu.serials')}</h1>
          <div className="sub2">{t('inv.serial.subtitle')}</div>
        </div>
      </div>
      <div className="tiles">
        {[SerialStatus.InStock, SerialStatus.InTransit, SerialStatus.Sold, SerialStatus.Returned].map((s) => (
          <div key={s} className="tile">
            <span className="tile-label">{t(serialStatusLabel(s))}</span>
            <strong>{count(s)}</strong>
          </div>
        ))}
      </div>
      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.serials')}>
          <input className="cell-input" placeholder={t('inv.serial.search')} aria-label={t('inv.serial.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
          <div className="chips-row">
            <VariantPicker value={variant} onChange={setVariant} />
            <select className="chip" aria-label={t('inv.lot.statusFilter')} value={status ?? ''}
              onChange={(e) => setStatus(e.target.value === '' ? undefined : Number(e.target.value))}>
              <option value="">{t('inv.lot.allStatuses')}</option>
              {Object.values(SerialStatus).map((s) => <option key={s} value={s}>{t(serialStatusLabel(s))}</option>)}
            </select>
            <WarehouseSelect value={warehouse} onChange={setWarehouse} />
          </div>
          {serials.isError && <p role="alert">{serials.error.message}</p>}
          {rows.length === 0 && !serials.isFetching && <p className="muted">{t('inv.serial.empty')}</p>}
          <table className="partner-list">
            <tbody>
              {rows.map((r) => (
                <tr key={r.serialId} className={selected === r.serialId ? 'selected' : undefined} onClick={() => setSelected(r.serialId)}>
                  <td>
                    <code>{r.serialNo}</code> <span className={`pill ${serialTone(r.status)}`}>{t(serialStatusLabel(r.status))}</span>
                    <div className="muted small">{serialWhere(r)}</div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {rows.length > 0 && <div className="muted small list-foot">{t('inv.serial.count', { count: rows.length })}</div>}
        </section>
        <section className="card detail-card">
          {selected === undefined ? <p className="muted empty-detail">{t('inv.serial.select')}</p> : <SerialPanel key={selected} id={selected} />}
        </section>
      </div>
    </div>
  );
}

function SerialPanel({ id }: { readonly id: number }) {
  const serial = useSerial(id);
  if (serial.data === undefined) {
    return <p role="status">{serial.isError ? serial.error.message : t('app.loading')}</p>;
  }
  const { serial: s, identifiers, movements } = serial.data;
  return (
    <div className="node-form">
      <h2 className="form-title"><code>{s.serialNo}</code> {t('inv.serial.serial')} <span className={`pill ${serialTone(s.status)}`}>{t(serialStatusLabel(s.status))}</span></h2>
      <div className="muted small">
        {[`${s.variantCode} ${s.variantName}`, s.lotCode == null ? null : `${t('inv.lot.lot')} ${s.lotCode}`,
          s.soldAt == null ? null : t('inv.serial.soldOn', { date: isoToRo(s.soldAt), name: s.soldToName ?? '—' })].filter(Boolean).join(' · ')}
      </div>
      <div className="grid g3">
        <div className="field"><span className="label">{t('inv.serial.where')}</span><span>{s.warehouseCode ?? '—'}</span></div>
        <div className="field"><span className="label">{t('inv.serial.warranty')}</span><span>{s.warrantyUntil == null ? '—' : isoToRo(s.warrantyUntil)}</span></div>
        <div className="field"><span className="label">{t('inv.serial.capture')}</span><span>{t(s.isCapturedAtIssue ? 'inv.serial.captureIssue' : 'inv.serial.captureBoth')}</span></div>
      </div>
      <h3 className="section-title">{t('inv.serial.identifiers')}</h3>
      <table className="inline-table">
        <thead><tr><th scope="col">{t('inv.serial.type')}</th><th scope="col">{t('inv.serial.value')}</th></tr></thead>
        <tbody>
          {identifiers.map((i) => <tr key={i.typeCode}><td>{i.typeName}</td><td><code>{i.value}</code></td></tr>)}
          {identifiers.length === 0 && <tr><td colSpan={2} className="muted">{t('inv.serial.noIdentifiers')}</td></tr>}
        </tbody>
      </table>
      <h3 className="section-title">{t('inv.serial.history')}</h3>
      <table className="inline-table">
        <thead>
          <tr>
            <th scope="col">{t('inv.doc.date')}</th>
            <th scope="col">{t('inv.serial.document')}</th>
            <th scope="col">{t('inv.serial.movement')}</th>
            <th scope="col">{t('inv.col.warehouse')}</th>
            <th scope="col" className="num">{t('inv.serial.pieces')}</th>
          </tr>
        </thead>
        <tbody>
          {movements.map((m) => (
            <tr key={m.movementId}>
              <td>{isoToRo(m.movementDate)}</td>
              <td>{`${m.sourceDocumentType} ${m.sourceDocumentNumber ?? ''}`}</td>
              <td><code>{m.movementTypeCode}</code></td>
              <td><code>{m.warehouseCode}</code></td>
              <td className="num">{Number(m.qtyBase) > 0 ? `+${qtyToRo(m.qtyBase)}` : qtyToRo(m.qtyBase)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {s.status === SerialStatus.Sold && <div className="banner dashed">{t('inv.serial.serviceNote')}</div>}
    </div>
  );
}
