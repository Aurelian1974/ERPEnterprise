import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { formatDate } from '../format/date';
import { t } from '../i18n';
import { ratesApi, rateKeys, useExchangeRates, type Currency, type ManualRate } from './ratesApi';
import { addDays, dayLabel, formatRate, importTime, nextBankingDay, rateRows, shortDay } from './ratesModel';

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Bucharest' }).format(new Date());

/** Ordinea monedelor uzuale in tabel (macheta 07b); celelalte urmeaza alfabetic. */
const COMMON = ['EUR', 'USD', 'GBP', 'CHF'];
const rank = (code: string) => (COMMON.includes(code) ? COMMON.indexOf(code) : COMMON.length);

/** Monedele afisate implicit in tabelul BNR (macheta 07b): primele active, fara leu. */
const DEFAULT_COLUMNS = 5;

/**
 * Configurare › Cursuri valutare (02.01, 4.3; macheta 07b): cursurile BNR pe zile (doar citire, zilele fara curs arata ziua cursului
 * folosit), cursurile manuale (motiv obligatoriu) si monedele active. „Preia acum de la BNR” aduce cursurile ultimelor zile bancare.
 */
export function ExchangeRatesTab({ user }: { readonly user: CurrentUser }) {
  const canManual = user.permissions.includes('Core.ExchangeRate.ManageManual');
  const canCurrencies = user.permissions.includes('Core.Nomenclature.Manage');
  const [days, setDays] = useState(7);
  const to = today();
  const from = addDays(to, -(days - 1));
  const rates = useExchangeRates(from, to);
  const [shown, setShown] = useState<readonly string[] | undefined>();
  const [editing, setEditing] = useState<ManualRate | 'new' | undefined>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();

  const data = rates.data;
  const active = useMemo(() => (data?.currencies ?? []).filter((c) => c.isActive && c.code !== 'RON')
    .sort((a, b) => rank(a.code) - rank(b.code) || a.code.localeCompare(b.code)), [data]);
  const columns = shown ?? active.slice(0, DEFAULT_COLUMNS).map((c) => c.code);
  const rows = useMemo(() => (data === undefined ? [] : rateRows(data)), [data]);
  const multipliers = useMemo(() => new Map((data?.bnrRates ?? []).map((r) => [r.currencyCode, r.multiplier])), [data]);

  const importBnr = async () => {
    setBusy(true);
    setError(undefined);
    try {
      const result = await ratesApi.importBnr();
      await queryClient.invalidateQueries({ queryKey: rateKeys.all });
      notify(result.added === 0 ? t('rates.importNothing') : t('rates.importDone', { count: result.added, days: result.days }));
    } catch (e) {
      const message = problem(e);
      setError(message);
      notify(message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const toggleColumn = (code: string) =>
    setShown(columns.includes(code) ? columns.filter((c) => c !== code) : [...columns, code]);

  const setActive = async (currency: Currency, isActive: boolean) => {
    setError(undefined);
    try {
      await ratesApi.setActive(currency.code, isActive, currency.rowVersion);
      await queryClient.invalidateQueries({ queryKey: rateKeys.all });
    } catch (e) {
      setError(problem(e));
    }
  };

  if (data === undefined) {
    return <p className="muted" role="status">{rates.isError ? problem(rates.error) : t('app.loading')}</p>;
  }
  const inactive = data.currencies.filter((c) => !c.isActive);

  return (
    <div className="rates-split">
      <section className="card rates-bnr" aria-labelledby="rates-bnr-title">
        <div className="card-head rates-head">
          <div>
            <h2 id="rates-bnr-title">{t('rates.bnr.title')}</h2>
            <p className="muted small">{t('rates.bnr.subtitle')}</p>
          </div>
          <div className="rates-filters">
            <select className="chip" aria-label={t('rates.interval')} value={days} onChange={(e) => setDays(Number(e.target.value))}>
              {[7, 14, 31, 92].map((n) => <option key={n} value={n}>{t('rates.lastDays', { count: n })}</option>)}
            </select>
            {active.map((c) => (
              <button key={c.code} type="button" className={columns.includes(c.code) ? 'chip on' : 'chip'} aria-pressed={columns.includes(c.code)}
                onClick={() => toggleColumn(c.code)}>{c.code}</button>
            ))}
          </div>
        </div>
        <ErrorBanner message={error} />
        <div className="matrix-scroll">
          <table className="simple rates-table">
            <thead>
              <tr>
                <th scope="col">{t('rates.col.date')}</th>
                {columns.map((code) => (
                  <th key={code} scope="col" className="num">{(multipliers.get(code) ?? 1) > 1 ? `${multipliers.get(code)} ${code}` : code}</th>
                ))}
                <th scope="col">{t('rates.col.source')}</th>
                <th scope="col">{t('rates.col.documents')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.day} className={row.rates.size === 0 ? 'no-rate' : ''}>
                  <th scope="row">{dayLabel(row.day)}</th>
                  {columns.map((code) => {
                    const rate = row.rates.get(code);
                    return <td key={code} className="num">{rate === undefined ? '—' : formatRate(rate.rate)}</td>;
                  })}
                  <td className="muted">
                    {row.rates.size > 0
                      ? <span title={t('rates.locked')}>🔒 {t('rates.bnrAt', { time: row.importedAt === undefined ? '' : importTime(row.importedAt) })}</span>
                      : t('rates.noBnrRate')}
                  </td>
                  <td className="muted">
                    {row.documentsRateDay === undefined ? t('rates.noRateYet') : t('rates.usesRateOf', { date: shortDay(row.documentsRateDay) })}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="banner dashed small">{t('rates.bnr.hint')}</p>
        <div className="form-footer">
          <span className="muted small">
            {data.lastImportAt == null
              ? t('rates.neverImported')
              : t('rates.lastImport', {
                at: `${formatDate(new Date(data.lastImportAt))}, ${importTime(data.lastImportAt)}`,
                next: data.lastRateDate == null ? '' : dayLabel(nextBankingDay(data.lastRateDate)).toLocaleLowerCase('ro-RO'),
              })}
          </span>
          {canManual && (
            <button type="button" className="btn ghost" disabled={busy} onClick={() => void importBnr()}>⟳ {t('rates.importNow')}</button>
          )}
        </div>
      </section>

      <section className="card rates-manual" aria-labelledby="rates-manual-title">
        <div className="card-head">
          <h2 id="rates-manual-title">{t('rates.manual.title')}</h2>
          {canManual && <button type="button" className="btn primary" onClick={() => setEditing('new')}>{t('rates.manual.new')}</button>}
        </div>
        {editing !== undefined && (
          <ManualRateForm key={editing === 'new' ? 'new' : `${editing.id}-${editing.rowVersion}`} rate={editing === 'new' ? undefined : editing}
            currencies={active} onDone={() => setEditing(undefined)} />
        )}
        <table className="simple rates-table">
          <thead>
            <tr>
              <th scope="col">{t('rates.col.currency')}</th>
              <th scope="col">{t('rates.col.date')}</th>
              <th scope="col" className="num">{t('rates.col.rate')}</th>
              <th scope="col">{t('rates.col.reason')}</th>
            </tr>
          </thead>
          <tbody>
            {data.manualRates.length === 0 && (
              <tr><td colSpan={4} className="muted">{t('rates.manual.empty')}</td></tr>
            )}
            {data.manualRates.map((m) => (
              <tr key={m.id} className={canManual ? 'clickable' : ''} onClick={canManual ? () => setEditing(m) : undefined}>
                <td><strong>{m.currencyCode}</strong></td>
                <td>{shortDay(m.rateDate)}</td>
                <td className="num">{m.multiplier > 1 ? `${formatRate(m.rate)} / ${m.multiplier}` : formatRate(m.rate)}</td>
                <td>{m.note}<div className="muted small">{m.changedByName}</div></td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="banner dashed small">{t('rates.manual.hint')}</p>
        <div className="currencies-box">
          <div className="currencies-head">
            <h3 className="section">{t('rates.currencies.title')}</h3>
            <span className="muted small">{t('rates.currencies.count', { active: active.length + 1, total: data.currencies.length })}</span>
          </div>
          <div className="currency-chips">
            {data.currencies.filter((c) => c.isActive).map((c) => (
              <span key={c.code} className={c.code === 'RON' ? 'pill info' : 'pill ok'} title={c.name}>
                {c.code === 'RON' ? t('rates.currencies.base') : c.code}
                {canCurrencies && c.code !== 'RON' && (
                  <button type="button" className="chip-remove" aria-label={t('rates.currencies.deactivate', { code: c.code })}
                    onClick={() => void setActive(c, false)}>✕</button>
                )}
              </span>
            ))}
          </div>
          {canCurrencies && inactive.length > 0 && (
            <select className="chip" aria-label={t('rates.currencies.add')} value=""
              onChange={(e) => { const c = inactive.find((x) => x.code === e.target.value); if (c !== undefined) { void setActive(c, true); } }}>
              <option value="">{t('rates.currencies.add')}</option>
              {inactive.map((c) => <option key={c.code} value={c.code}>{c.code} · {c.name}</option>)}
            </select>
          )}
        </div>
      </section>
    </div>
  );
}

function ManualRateForm({ rate, currencies, onDone }: {
  readonly rate: ManualRate | undefined;
  readonly currencies: readonly Currency[];
  readonly onDone: () => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [currency, setCurrency] = useState(rate?.currencyCode ?? currencies[0]?.code ?? '');
  const [date, setDate] = useState(rate?.rateDate ?? today());
  const [value, setValue] = useState(rate === undefined ? '' : formatRate(rate.rate));
  const [multiplier, setMultiplier] = useState(rate?.multiplier ?? 1);
  const [note, setNote] = useState(rate?.note ?? '');
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);

  const save = async () => {
    setError(undefined);
    if (note.trim() === '') {
      setError(t('rates.manual.reasonRequired'));
      return;
    }
    setBusy(true);
    try {
      await ratesApi.saveManual({
        id: rate?.id ?? null, currencyCode: currency, rateDate: date, rate: value.trim().replace(',', '.'), multiplier, note: note.trim(),
        rowVersion: rate?.rowVersion ?? null,
      });
      await queryClient.invalidateQueries({ queryKey: rateKeys.all });
      notify(t('common.saved'));
      onDone();
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="manual-form">
      <ErrorBanner message={error} />
      <div className="grid g2">
        <label className="field">
          <span className="label">{t('rates.col.currency')}</span>
          <select className="cell-input" value={currency} onChange={(e) => setCurrency(e.target.value)}>
            {currencies.map((c) => <option key={c.code} value={c.code}>{c.code} · {c.name}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('rates.col.date')}</span>
          <input className="cell-input" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </label>
        <label className="field">
          <span className="label">{t('rates.col.rate')}</span>
          <input className="cell-input num" inputMode="decimal" value={value} placeholder="5,0950" onChange={(e) => setValue(e.target.value)} />
        </label>
        <label className="field">
          <span className="label">{t('rates.multiplier')}</span>
          <select className="cell-input" value={multiplier} onChange={(e) => setMultiplier(Number(e.target.value))}>
            {[1, 10, 100, 1000].map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </label>
      </div>
      <label className="field">
        <span className="label">{t('rates.col.reason')}</span>
        <input className="cell-input" value={note} maxLength={500} placeholder={t('rates.manual.reasonPlaceholder')} onChange={(e) => setNote(e.target.value)} />
      </label>
      <div className="actions">
        <button type="button" className="btn ghost" disabled={busy} onClick={onDone}>{t('common.cancel')}</button>
        <button type="button" className="btn primary" disabled={busy} onClick={() => void save()}>{t('common.save')}</button>
      </div>
    </div>
  );
}
