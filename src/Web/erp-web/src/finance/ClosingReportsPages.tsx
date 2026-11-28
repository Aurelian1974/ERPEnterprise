import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { t, type TextKey } from '../i18n';
import { formatAmount } from './cashApi';
import { previousMonthEnd } from './closingModel';
import { usePeriodPicker } from './MonthClosePage';
import { reasonKey } from './PostingQueuePage';

type ClosingLine = components['schemas']['ClosingLineDto'];

/**
 * Inchideri › Concordante (02.02, C3 3.4; macheta c3-03): diferentele dintre contabilitate si evidenta operativa la o data (clienti si furnizori
 * pe partener, case), cu documentele din coada „De contat” care le explica.
 */
export function ReconciliationPage() {
  const [asOf, setAsOf] = useState(previousMonthEnd(new Date().toISOString().slice(0, 10)));
  const rows = useQuery({
    queryKey: ['finance', 'reconciliation', asOf],
    queryFn: async () => unwrap(await api.GET('/api/finance/reconciliation', { params: { query: { asOf } } })),
  });
  const data = rows.data ?? [];
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.closing')} › {t('menu.reconciliation')}</div>
      <div className="head">
        <div><h1>{t('recon.title')}</h1><div className="sub2">{t('recon.subtitle')}</div></div>
        <div className="actions"><input className="cell-input" type="date" aria-label={t('recon.asOf')} value={asOf} onChange={(e) => setAsOf(e.target.value)} /></div>
      </div>
      <ErrorBanner message={rows.isError ? rows.error.message : undefined} />
      <section className="card" aria-label={t('recon.title')}>
        {rows.data !== undefined && data.length === 0 ? <p className="check-ok">✓ {t('recon.none')}</p> : (
          <table className="partner-list cash-list">
            <thead><tr><th scope="col">{t('recon.area')}</th><th scope="col">{t('recon.key')}</th><th scope="col" className="num">{t('recon.ledger')}</th>
              <th scope="col" className="num">{t('recon.subledger')}</th><th scope="col" className="num">{t('recon.difference')}</th><th scope="col">{t('recon.explained')}</th></tr></thead>
            <tbody>{data.map((r) => (
              <tr key={`${String(r.area)}-${String(r.keyId)}`}>
                <td>{t(`recon.area.${String(r.area)}` as TextKey)}</td>
                <td className="strong">{r.keyName ?? '—'}</td>
                <td className="num">{formatAmount(r.ledger)}</td>
                <td className="num">{formatAmount(r.subledger)}</td>
                <td className="num check-bad">{formatAmount(r.difference)}</td>
                <td className="small">{r.explanations.length === 0 ? t('recon.unexplained')
                  : r.explanations.map((e) => `${e.documentNo} (${t(reasonKey(e.reason))}, ${formatAmount(e.amount)})`).join('; ')}</td>
              </tr>))}</tbody>
          </table>
        )}
      </section>
    </div>
  );
}

function LinesTable({ title, lines }: { readonly title: string; readonly lines: readonly ClosingLine[] }) {
  return (
    <section className="card" aria-label={title}>
      <h2>{title}</h2>
      {lines.length === 0 ? <p className="muted">{t('closingEntries.nothing')}</p> : (
        <table className="partner-list cash-list">
          <thead><tr><th scope="col">{t('entry.account')}</th><th scope="col" className="num">{t('entry.debit')}</th><th scope="col" className="num">{t('entry.credit')}</th></tr></thead>
          <tbody>{lines.map((l, i) => (
            <tr key={i}><td><span className="mono strong">{l.accountCode}</span> {l.accountName}</td>
              <td className="num">{l.side === 1 ? formatAmount(l.amountRon) : ''}</td><td className="num">{l.side === 2 ? formatAmount(l.amountRon) : ''}</td></tr>))}</tbody>
        </table>
      )}
    </section>
  );
}

/**
 * Inchideri › Note de inchidere (02.02, C3 3.5; macheta c3-04): regularizarea TVA si inchiderea claselor 6 si 7 in 121 pentru luna aleasa, in
 * simulare (aceeasi procedura care le scrie la inchidere), ca rezultatul lunii sa se verifice inainte.
 */
export function ClosingEntriesPage() {
  const { period, picker } = usePeriodPicker();
  const preview = useQuery({
    queryKey: ['finance', 'closing-preview', period?.id ?? 0],
    enabled: period !== undefined,
    queryFn: async () => unwrap(await api.GET('/api/finance/periods/{id}/closing-entries', { params: { path: { id: period?.id ?? 0 } } })),
  });
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.closing')} › {t('menu.closingEntries')}</div>
      <div className="head">
        <div><h1>{t('closingEntries.title')}</h1><div className="sub2">{t('closingEntries.subtitle')}</div></div>
        <div className="actions">{picker}</div>
      </div>
      <ErrorBanner message={preview.isError ? preview.error.message : undefined} />
      {preview.data !== undefined && (
        <div className="split">
          <LinesTable title={t('closingEntries.vat')} lines={preview.data.vatSettlement} />
          <LinesTable title={t('closingEntries.pl')} lines={preview.data.profitAndLoss} />
        </div>
      )}
    </div>
  );
}
