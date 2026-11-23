import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { percent } from './PenaltiesPage';

/**
 * Configurare › Finanțe › Rate de referință BNR (02.02, F3 3.10 regula 2, OG 13/2011): rata valabilă de la fiecare dată, din care se
 * calculează dobânda legală penalizatoare (rata + 8 puncte); comună tuturor firmelor instalării.
 */
export function ReferenceRatesTab() {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const rates = useQuery({ queryKey: ['finance', 'penalties', 'rates'], queryFn: async () => unwrap(await api.GET('/api/finance/reference-rates')) });
  const [draft, setDraft] = useState({ validFrom: isoToday(), rate: '' });
  const [error, setError] = useState<string | undefined>();
  const save = async (validFrom: string, rate: string, remove: boolean) => {
    setError(undefined);
    try {
      unwrap(await api.POST('/api/finance/reference-rates', { body: { validFrom, rate: rate.replace(',', '.'), delete: remove } }));
      notify(t('pen.rateSaved'));
      await queryClient.invalidateQueries({ queryKey: ['finance', 'penalties'] });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <section className="card" aria-labelledby="rates-title">
      <h2 id="rates-title">{t('pen.rates')}</h2>
      <p className="muted small">{t('pen.ratesHelp')}</p>
      {rates.isError && <ErrorBanner message={rates.error.message} />}
      <table className="partner-list cash-list">
        <thead>
          <tr><th scope="col">{t('pen.rateFrom')}</th><th scope="col" className="num">{t('pen.rateValue')}</th><th scope="col" className="num">{t('pen.legalRate')}</th><th /></tr>
        </thead>
        <tbody>
          {(rates.data ?? []).map((r) => (
            <tr key={r.validFrom}>
              <td>{isoToRo(r.validFrom)}</td><td className="num">{percent(r.rate)}%</td><td className="num">{percent(Number(r.rate) + 8)}%</td>
              <td><button type="button" className="btn ghost" onClick={() => void save(r.validFrom, '0', true)}>{t('pen.rateDelete')}</button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="order-from-invoices">
        <label>{t('pen.rateFrom')}<input className="cell-input" type="date" value={draft.validFrom} onChange={(e) => setDraft({ ...draft, validFrom: e.target.value })} /></label>
        <label>{t('pen.rateValue')}<input className="cell-input num" value={draft.rate} onChange={(e) => setDraft({ ...draft, rate: e.target.value })} /></label>
        <button type="button" className="btn primary" disabled={draft.rate.trim() === ''} onClick={() => void save(draft.validFrom, draft.rate, false)}>{t('pen.rateAdd')}</button>
      </div>
      <ErrorBanner message={error} />
    </section>
  );
}
