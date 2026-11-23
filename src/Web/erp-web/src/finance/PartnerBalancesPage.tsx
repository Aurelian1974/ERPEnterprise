import { useState } from 'react';
import { useNavigate } from 'react-router';
import { ErrorBanner } from '../components/Field';
import { t, type TextKey } from '../i18n';
import { isoToday } from '../users/userModel';
import { formatAmount } from './cashApi';
import { agingShares, Role, usePartnerBalances, type BalanceFilter, type PartnerBalanceRow } from './settlementApi';

const filters: readonly { readonly value: BalanceFilter; readonly label: TextKey }[] = [
  { value: 'customers', label: 'settle.filter.customers' },
  { value: 'suppliers', label: 'settle.filter.suppliers' },
  { value: 'overdue', label: 'settle.filter.overdue' },
  { value: 'unallocated', label: 'settle.filter.unallocated' },
  { value: 'both', label: 'settle.filter.both' },
];

/**
 * Decontări › Solduri parteneri (02.02, F3 10; macheta f3-01): cât datorează clienții și cât datorăm furnizorilor la o dată, cu restanța,
 * vechimea pe intervale, întârzierea maximă și plățile nealocate; totalurile firmei în cartonașe. Rândul deschide fișa financiară.
 */
export function PartnerBalancesPage() {
  const navigate = useNavigate();
  const [asOf, setAsOf] = useState(isoToday());
  const [filter, setFilter] = useState<BalanceFilter>('customers');
  const [search, setSearch] = useState('');
  const list = usePartnerBalances({ asOf, filter, search });
  const totals = list.data?.totals;

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.settlements')} › {t('menu.partnerBalances')}</div>
      <div className="head">
        <div>
          <h1>{t('settle.balances.title')}</h1>
          <div className="sub2">{t('settle.balances.subtitle')}</div>
        </div>
      </div>
      {totals !== undefined && (
        <div className="kpi-row" role="group" aria-label={t('settle.balances.totals')}>
          <Kpi label={t('settle.kpi.receivables')} value={totals.receivablesRon} note={t('settle.kpi.customers', { count: totals.customersWithBalance })} />
          <Kpi label={t('settle.kpi.overdue')} value={totals.overdueReceivablesRon} note={t('settle.kpi.customers', { count: totals.customersOverdue })} tone="bad" />
          <Kpi label={t('settle.kpi.payables')} value={totals.payablesRon} note={t('settle.kpi.suppliers', { count: totals.suppliersWithBalance })} />
          <Kpi label={t('settle.kpi.unallocated')} value={totals.unallocatedRon} note={t('settle.kpi.items', { count: totals.unallocatedCount })} tone="warn" />
          <Kpi label={t('settle.kpi.vat')} value={(Number(totals.vatNonExigibleCollectedRon) + Number(totals.vatNonExigibleDeductibleRon)).toFixed(2)}
            note={t('settle.kpi.vatSplit', { collected: formatAmount(totals.vatNonExigibleCollectedRon), deductible: formatAmount(totals.vatNonExigibleDeductibleRon) })} />
        </div>
      )}
      <section className="card" aria-label={t('settle.balances.title')}>
        <div className="toolbar-row">
          <input className="cell-input search" type="search" value={search} aria-label={t('settle.search')} placeholder={t('settle.search')}
            onChange={(e) => setSearch(e.target.value)} />
          <div className="chips-row" role="group" aria-label={t('settle.filter')}>
            {filters.map((f) => (
              <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                onClick={() => setFilter(f.value)}>{t(f.label)}</button>
            ))}
          </div>
          <label className="inline-field">{t('settle.asOf')}
            <input type="date" className="cell-input" value={asOf} onChange={(e) => e.target.value !== '' && setAsOf(e.target.value)} />
          </label>
        </div>
        {list.isError && <ErrorBanner message={list.error.message} />}
        {list.data !== undefined && list.data.rows.length === 0 ? <p className="muted">{t('settle.balances.empty')}</p> : (
          <table className="partner-list cash-list" aria-busy={list.isFetching}>
            <thead>
              <tr>
                <th scope="col">{t('settle.col.partner')}</th>
                <th scope="col">{t('settle.col.role')}</th>
                <th scope="col" className="num">{t('settle.col.balance')}</th>
                <th scope="col" className="num">{t('settle.col.overdue')}</th>
                <th scope="col">{t('settle.col.aging')}</th>
                <th scope="col" className="num">{t('settle.col.maxDelay')}</th>
                <th scope="col" className="num">{t('settle.col.unallocated')}</th>
              </tr>
            </thead>
            <tbody>
              {(list.data?.rows ?? []).map((r) => (
                <tr key={`${r.partnerId}-${r.role}`} className="clickable" tabIndex={0}
                  onClick={() => void navigate(`/finante/decontari/parteneri/${r.partnerId}`)}
                  onKeyDown={(e) => e.key === 'Enter' && void navigate(`/finante/decontari/parteneri/${r.partnerId}`)}>
                  <td><div className="strong">{r.partnerName}</div><div className="muted small">{r.partnerCode}{r.fiscalCode == null ? '' : ` · ${r.fiscalCode}`}</div></td>
                  <td>
                    <span className="pill">{t(r.role === Role.Customer ? 'settle.role.customer' : 'settle.role.supplier')}</span>
                    {r.hasOtherRole && <span className="pill info">{t(r.role === Role.Customer ? 'settle.role.supplier' : 'settle.role.customer')}</span>}
                  </td>
                  <td className="num">{formatAmount(r.balanceRon)}</td>
                  <td className={r.overdueRon === '0.00' ? 'num muted' : 'num neg'}>{formatAmount(r.overdueRon)}</td>
                  <td><AgingBar row={r} /></td>
                  <td className="num">{r.maxOverdueDays === 0 ? '—' : t('settle.days', { count: r.maxOverdueDays })}</td>
                  <td className={r.unallocatedCount === 0 ? 'num muted' : 'num'}>{formatAmount(r.unallocatedRon)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <div className="aging-legend muted small">
          <span className="aging-key a0" />{t('settle.aging.current')} <span className="aging-key a1" />{t('settle.aging.d30')}
          <span className="aging-key a2" />{t('settle.aging.d60')} <span className="aging-key a3" />{t('settle.aging.over60')}
        </div>
        <p className="muted small">{t('settle.balances.hint')}</p>
      </section>
    </div>
  );
}

function Kpi({ label, value, note, tone }: { readonly label: string; readonly value: string; readonly note: string; readonly tone?: 'bad' | 'warn' }) {
  return (
    <div className={tone === undefined ? 'kpi' : `kpi ${tone}`}>
      <div className="kpi-label">{label}</div>
      <div className="kpi-value">{formatAmount(value)} <span className="muted small">RON</span></div>
      <div className="muted small">{note}</div>
    </div>
  );
}

function AgingBar({ row }: { readonly row: PartnerBalanceRow }) {
  const shares = agingShares(row);
  return (
    <div className="aging-bar" role="img" aria-label={t('settle.aging.label', { current: shares[0] ?? 0, d30: shares[1] ?? 0, d60: shares[2] ?? 0, over60: shares[3] ?? 0 })}>
      {shares.map((share, i) => share > 0 && <span key={i} className={`a${i}`} style={{ width: `${share}%` }} />)}
    </div>
  );
}
