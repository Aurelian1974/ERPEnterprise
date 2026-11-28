import { useState } from 'react';
import { useOutletContext } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { formatAmount } from './cashApi';
import { downloadFile } from './paymentOrderApi';
import { calendarSummary, d300Sections, monthRange, pairRows, previousMonth } from './taxModel';

type TaxReturn = components['schemas']['TaxReturnDto'];

const today = () => new Date().toISOString().slice(0, 10);
const statusKey = (status: number | null | undefined) => `tax.status.${String(status ?? 0)}` as TextKey;
const statusClass = (status: number | null | undefined) => (status === 4 ? 'pill ok' : status === 3 ? 'pill info' : status === 2 ? 'pill info' : 'pill warn');

function useCan() {
  const user = useOutletContext<CurrentUser>();
  return (permission: string) => user.permissions.includes(permission);
}

/** Actiunile comune: mesajul de succes, eroarea si reimprospatarea datelor Finantelor. */
function useAct() {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const act = async (action: () => Promise<unknown>, message: TextKey) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: ['finance'] });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  return { act, error };
}

function MonthInput({ value, onChange }: { readonly value: string; readonly onChange: (v: string) => void }) {
  return <input type="month" className="cell-input" aria-label={t('tax.month')} value={value} onChange={(e) => { if (e.target.value !== '') onChange(e.target.value); }} />;
}

/**
 * Panoul unei declaratii (machetele c4-01, c4-02): starea, fisierele (descarcare), verificarea, generarea (doar pe lunile inchise si cu
 * datele neschimbate) si inregistrarea depunerii cu numarul din recipisa.
 */
function ReturnPanel({ id }: { readonly id: number }) {
  const can = useCan();
  const { act, error } = useAct();
  const [registration, setRegistration] = useState('');
  const data = useQuery({ queryKey: ['finance', 'tax-return', id], queryFn: async () => unwrap(await api.GET('/api/finance/tax-returns/{id}', { params: { path: { id } } })) });
  const r = data.data;
  if (r === undefined) {
    return <section className="card"><ErrorBanner message={data.isError ? data.error.message : undefined} /></section>;
  }
  const body = { id: r.id, rowVersion: r.rowVersion };
  return (
    <section className="card" aria-label={r.formCode}>
      <h2>{r.formCode} {isoToRo(r.periodFrom)}–{isoToRo(r.periodTo)} <span className={statusClass(r.status)}>{t(statusKey(r.status))}</span></h2>
      <p className="muted small">{r.formName}{r.dueDate != null ? ` · ${t('tax.due', { date: isoToRo(r.dueDate) })}` : ''}</p>
      {r.needsReview && <p className="pill warn">{t('tax.needsReview')}</p>}
      <p>{t('tax.amountDue', { amount: formatAmount(r.amountDue) })}{r.amountRefundable !== '0.00' ? ` · ${t('tax.amountRefundable', { amount: formatAmount(r.amountRefundable) })}` : ''}</p>
      <h3>{t('tax.files')}</h3>
      {r.files.length === 0 ? <p className="muted small">{t('tax.noFiles')}</p> : (
        <ul className="plain-list">{r.files.map((f) => (
          <li key={f.id}>
            <button type="button" className="link" onClick={() => void act(async () => {
              const file = unwrap(await api.GET('/api/finance/tax-return-files/{id}', { params: { path: { id: f.id } } }));
              downloadFile({ fileName: file.fileName, contentType: file.contentType, contentBase64: file.content });
            }, 'tax.downloaded')}>{f.fileName}</button>
            <span className="muted small"> · {t(`tax.fileKind.${String(f.kind)}` as TextKey)}</span>
          </li>))}
        </ul>
      )}
      {r.registrationNumber != null && <p className="small">{t('tax.registration', { number: r.registrationNumber })}</p>}
      <div className="row-between">
        {r.status === 1 && can('Fin.Tax.Prepare') && (
          <button type="button" className="btn" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/tax-returns/{id}/verify',
            { params: { path: { id: r.id } }, body })), 'tax.verified')}>{t('tax.verify')}</button>
        )}
        {(r.status === 2 || r.status === 3) && can('Fin.Tax.Generate') && (
          <button type="button" className="btn primary" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/tax-returns/{id}/generate',
            { params: { path: { id: r.id } }, body })), 'tax.generated')}>{t('tax.generate')}</button>
        )}
      </div>
      {r.status === 3 && can('Fin.Tax.Submit') && (
        <div className="row-between">
          <label>{t('tax.registrationNumber')}<input className="cell-input" maxLength={50} value={registration} onChange={(e) => setRegistration(e.target.value)} /></label>
          <button type="button" className="btn primary" disabled={registration.trim() === ''} onClick={() => void act(async () => unwrap(await api.POST(
            '/api/finance/tax-returns/{id}/submit', { params: { path: { id: r.id } }, body: { ...body, registrationNumber: registration.trim(), receiptFileName: null, receiptBase64: null } })),
          'tax.submitted')}>{t('tax.submit')}</button>
        </div>
      )}
      <ErrorBanner message={error} />
    </section>
  );
}

/** Declaratia unui formular pentru luna aleasa: cautata fara recalculare; „Calculeaza” o creeaza sau o recalculeaza. */
function useMonthReturn(formCode: string, month: string) {
  const { from, to } = monthRange(month);
  const found = useQuery({
    queryKey: ['finance', 'tax-return-find', formCode, from],
    queryFn: async () => unwrap(await api.GET('/api/finance/tax-returns', { params: { query: { formCode, periodFrom: from } } })),
  });
  const id = found.data ?? null;
  const detail = useQuery({
    queryKey: ['finance', 'tax-return', id ?? 0],
    enabled: id !== null,
    queryFn: async () => unwrap(await api.GET('/api/finance/tax-returns/{id}', { params: { path: { id: id ?? 0 } } })),
  });
  const calculate = async () => unwrap(await api.POST('/api/finance/tax-returns', { body: { formCode, periodFrom: from, periodTo: to } }));
  return { id, ret: id === null ? undefined : detail.data, calculate, from, to };
}

/** Declaratii › Calendar declaratii (02.02, C4 3.7; macheta c4-01): termenele lunii cu starea declaratiei si panoul celei alese. */
export function TaxCalendarPage() {
  const can = useCan();
  const { act, error } = useAct();
  const [month, setMonth] = useState(today().slice(0, 7));
  const [picked, setPicked] = useState<string | undefined>();
  const { from, to } = monthRange(month);
  const items = useQuery({ queryKey: ['finance', 'tax-calendar', from], queryFn: async () => unwrap(await api.GET('/api/finance/tax-calendar', { params: { query: { from, to } } })) });
  const list = items.data ?? [];
  const key = (i: (typeof list)[number]) => `${i.formCode}|${i.periodFrom}`;
  const current = list.find((i) => key(i) === picked) ?? list[0];
  const s = calendarSummary(list);
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.declarations')} › {t('menu.taxCalendar')}</div>
      <div className="head">
        <div><h1>{t('menu.taxCalendar')}</h1><div className="sub2">{t('tax.calendarSubtitle')}</div></div>
        {can('Fin.Tax.Prepare') && (
          <button type="button" className="btn" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/tax-calendar',
            { body: { year: Number(month.slice(0, 4)) } })), 'tax.calendarGenerated')}>{t('tax.generateCalendar', { year: month.slice(0, 4) })}</button>
        )}
      </div>
      <div className="kpi-row">
        <div className="kpi"><div className="kpi-label">{t('tax.toFile')}</div><div className="kpi-value">{s.toFile}</div></div>
        <div className={s.review > 0 ? 'kpi warn' : 'kpi'}><div className="kpi-label">{t('tax.review')}</div><div className="kpi-value">{s.review}</div></div>
        <div className="kpi"><div className="kpi-label">{t('tax.ready')}</div><div className="kpi-value">{s.ready}</div></div>
        <div className="kpi"><div className="kpi-label">{t('tax.filed')}</div><div className="kpi-value ok">{s.filed}</div></div>
      </div>
      <ErrorBanner message={items.isError ? items.error.message : error} />
      <div className="split">
        <section className="card" aria-label={t('menu.taxCalendar')}>
          <div className="chips-row"><MonthInput value={month} onChange={setMonth} /></div>
          {list.length === 0 ? <p className="muted">{t('tax.noDeadlines')}</p> : (
            <table className="partner-list cash-list">
              <thead><tr><th>{t('tax.form')}</th><th>{t('tax.period')}</th><th>{t('tax.deadline')}</th><th>{t('tax.state')}</th><th className="num">{t('tax.amount')}</th></tr></thead>
              <tbody>{list.map((i) => (
                <tr key={key(i)} className={current !== undefined && key(i) === key(current) ? 'picked' : undefined} onClick={() => setPicked(key(i))}>
                  <td><span className="strong">{i.formCode}</span><div className="muted small">{i.formName}</div></td>
                  <td>{isoToRo(i.periodFrom)}–{isoToRo(i.periodTo)}</td>
                  <td>{isoToRo(i.dueDate)}</td>
                  <td><span className={statusClass(i.status)}>{t(statusKey(i.status))}</span>{i.needsReview && <span className="pill warn">{t('tax.needsReviewShort')}</span>}</td>
                  <td className="num">{i.amountDue != null ? formatAmount(i.amountDue) : '—'}</td>
                </tr>))}</tbody>
            </table>
          )}
        </section>
        {current?.returnId != null ? <ReturnPanel key={current.returnId} id={current.returnId} /> : current !== undefined && (
          <section className="card">
            <h2>{current.formCode} {isoToRo(current.periodFrom)}–{isoToRo(current.periodTo)}</h2>
            <p className="muted">{t('tax.notCalculated')}</p>
            {can('Fin.Tax.Prepare') && (
              <button type="button" className="btn primary" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/tax-returns',
                { body: { formCode: current.formCode, periodFrom: current.periodFrom, periodTo: current.periodTo } })), 'tax.calculated')}>{t('tax.calculate')}</button>
            )}
          </section>
        )}
      </div>
    </div>
  );
}

/** Declaratii › Decont TVA (02.02, C4 3.4, 4.1; macheta c4-02): randurile din registru, comparatia cu P300 si explicatiile, panoul declaratiei. */
export function VatReturnPage() {
  const can = useCan();
  const { act, error } = useAct();
  const [month, setMonth] = useState(previousMonth(today()));
  const { id, ret, calculate } = useMonthReturn('D300', month);
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.declarations')} › {t('menu.vatReturn')}</div>
      <div className="head">
        <div><h1>{t('menu.vatReturn')}</h1><div className="sub2">{t('tax.vatSubtitle')}</div></div>
        {can('Fin.Tax.Prepare') && <button type="button" className="btn" onClick={() => void act(calculate, 'tax.calculated')}>{id === null ? t('tax.calculate') : t('tax.recalculate')}</button>}
      </div>
      <ErrorBanner message={error} />
      <div className="split">
        <section className="card" aria-label={t('menu.vatReturn')}>
          <div className="chips-row"><MonthInput value={month} onChange={setMonth} /></div>
          {ret === undefined ? <p className="muted">{t('tax.notCalculated')}</p> : <D300Lines ret={ret} />}
          <p className="muted small">{t('tax.readOnlyRows')}</p>
        </section>
        {id !== null && <div><ReturnPanel key={id} id={id} />{ret !== undefined && ret.comparison.length > 0 && <Comparison ret={ret} />}</div>}
      </div>
    </div>
  );
}

function D300Lines({ ret }: { readonly ret: TaxReturn }) {
  const s = d300Sections(ret.lines);
  const section = (title: TextKey, rows: ReturnType<typeof pairRows>) => (
    <>
      <tr><th colSpan={3}>{t(title)}</th></tr>
      {rows.map((r) => <tr key={r.rowCode}><td>{r.label}<div className="muted small">{r.rowCode}</div></td><td className="num">{formatAmount(r.base)}</td><td className="num">{formatAmount(r.vat)}</td></tr>)}
    </>
  );
  return (
    <table className="partner-list cash-list">
      <thead><tr><th>{t('tax.row')}</th><th className="num">{t('tax.base')}</th><th className="num">{t('tax.vat')}</th></tr></thead>
      <tbody>
        {section('tax.collected', pairRows(s.sales))}
        {section('tax.deductible', pairRows(s.purchases))}
        {s.totals.map((l) => <tr key={l.columnCode} className="strong"><td>{l.label}</td><td /><td className="num">{formatAmount(l.calculatedValue)}</td></tr>)}
      </tbody>
    </table>
  );
}

/** Comparatia cu P300 (decizia 5): diferentele peste prag cer explicatie inainte de verificare. */
function Comparison({ ret }: { readonly ret: TaxReturn }) {
  const can = useCan();
  const { act, error } = useAct();
  const [texts, setTexts] = useState<Record<string, string>>({});
  return (
    <section className="card" aria-label={t('tax.p300')}>
      <h2>{t('tax.p300')}</h2>
      <table className="partner-list cash-list">
        <thead><tr><th>{t('tax.row')}</th><th className="num">{t('tax.declared')}</th><th className="num">{t('tax.precompleted')}</th><th className="num">{t('tax.difference')}</th></tr></thead>
        <tbody>{ret.comparison.map((c) => {
          const k = `${c.rowCode}|${c.columnCode}`;
          return (
            <tr key={k}>
              <td>{c.rowCode} {c.columnCode}{c.isOverThreshold && <span className="pill bad">{t('tax.overThreshold')}</span>}
                {c.isOverThreshold && can('Fin.Tax.Prepare') && ret.status < 4 && (
                  <div className="row-between">
                    <input className="cell-input wide-input" aria-label={t('tax.explanation')} maxLength={1000} value={texts[k] ?? c.explanation ?? ''}
                      onChange={(e) => setTexts({ ...texts, [k]: e.target.value })} />
                    <button type="button" className="btn" disabled={(texts[k] ?? '').trim() === ''} onClick={() => void act(async () => unwrap(await api.POST(
                      '/api/finance/tax-returns/{id}/explain', { params: { path: { id: ret.id } }, body: { id: ret.id, rowCode: c.rowCode, columnCode: c.columnCode, explanation: (texts[k] ?? '').trim() } })),
                    'tax.explained')}>{t('tax.explain')}</button>
                  </div>
                )}
                {c.explanation != null && <div className="muted small">{c.explanation}</div>}
              </td>
              <td className="num">{formatAmount(c.declared)}</td><td className="num">{formatAmount(c.precompleted)}</td><td className="num">{formatAmount(c.difference)}</td>
            </tr>
          );
        })}</tbody>
      </table>
      <ErrorBanner message={error} />
    </section>
  );
}

/** Declaratii › Jurnale de TVA (02.02, C4 3.1; macheta c4-03): jurnalul de vanzari sau de cumparari al lunii, cu totalurile pe tip de operatiune. */
export function VatJournalPage() {
  const [month, setMonth] = useState(previousMonth(today()));
  const [direction, setDirection] = useState(1);
  const year = Number(month.slice(0, 4));
  const m = Number(month.slice(5, 7));
  const data = useQuery({
    queryKey: ['finance', 'vat-journal', month, direction],
    queryFn: async () => unwrap(await api.GET('/api/finance/vat-journal', { params: { query: { year, month: m, direction } } })),
  });
  const j = data.data;
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.declarations')} › {t('menu.vatJournal')}</div>
      <div className="head"><div><h1>{t('menu.vatJournal')}</h1><div className="sub2">{t('tax.journalSubtitle')}</div></div></div>
      <ErrorBanner message={data.isError ? data.error.message : undefined} />
      <section className="card" aria-label={t('menu.vatJournal')}>
        <div className="chips-row">
          <MonthInput value={month} onChange={setMonth} />
          {[1, 2].map((d) => (
            <button key={d} type="button" className={direction === d ? 'chip on' : 'chip'} aria-pressed={direction === d} onClick={() => setDirection(d)}>
              {t(d === 1 ? 'tax.salesJournal' : 'tax.purchaseJournal')}</button>
          ))}
        </div>
        {j !== undefined && (
          <>
            <div className="kpi-row">{j.totals.map((x) => (
              <div key={x.operationTypeCode} className="kpi"><div className="kpi-label">{x.operationTypeCode} · {x.name}</div>
                <div className="kpi-value">{formatAmount(x.vatRon)}</div><div className="muted small">{t('tax.baseOf', { amount: formatAmount(x.baseRon), rows: x.rows })}</div></div>
            ))}</div>
            {j.rows.length === 0 ? <p className="muted">{t('tax.noRows')}</p> : (
              <table className="partner-list cash-list">
                <thead><tr><th>{t('tax.document')}</th><th>{t('tax.partner')}</th><th>{t('tax.operation')}</th><th className="num">{t('tax.base')}</th><th className="num">{t('tax.vat')}</th></tr></thead>
                <tbody>{j.rows.map((r) => (
                  <tr key={r.id}>
                    <td><span className="strong">{r.documentNumber}</span><div className="muted small">{isoToRo(r.documentDate)}{r.isVatOnCollection ? ` · ${t('tax.onCollection')}` : ''}</div></td>
                    <td>{r.partnerName ?? '—'}<div className="muted small">{r.partnerFiscalCode ?? ''}</div></td>
                    <td>{r.operationTypeCode}<div className="muted small">{r.vatRateCode}</div></td>
                    <td className="num">{formatAmount(r.baseRon)}</td><td className="num">{formatAmount(r.vatRon)}</td>
                  </tr>))}</tbody>
              </table>
            )}
          </>
        )}
      </section>
    </div>
  );
}

/** Declaratii › SAF-T (D406) (02.02, C4 3.5, 4.3; macheta c4-04): verificarile pe categorii, rezumatul lunii si panoul declaratiei. */
export function SaftPage() {
  const can = useCan();
  const { act, error } = useAct();
  const [month, setMonth] = useState(previousMonth(today()));
  const { id, ret, calculate, from, to } = useMonthReturn('D406', month);
  const checks = useQuery({ queryKey: ['finance', 'saft-checks', from], queryFn: async () => unwrap(await api.GET('/api/finance/saft-checks', { params: { query: { from, to } } })) });
  const issues = checks.data ?? [];
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.declarations')} › {t('menu.saft')}</div>
      <div className="head">
        <div><h1>{t('menu.saft')}</h1><div className="sub2">{t('tax.saftSubtitle')}</div></div>
        {can('Fin.Tax.Prepare') && <button type="button" className="btn" onClick={() => void act(calculate, 'tax.calculated')}>{id === null ? t('tax.calculate') : t('tax.recalculate')}</button>}
      </div>
      <ErrorBanner message={checks.isError ? checks.error.message : error} />
      <div className="split">
        <section className="card" aria-label={t('menu.saft')}>
          <div className="chips-row"><MonthInput value={month} onChange={setMonth} /></div>
          <div className="kpi-row">{[1, 2, 3].map((c) => {
            const count = issues.filter((i) => i.category === c).length;
            return <div key={c} className={count > 0 ? (c === 3 ? 'kpi warn' : 'kpi bad') : 'kpi'}><div className="kpi-label">{t(`tax.saftCategory.${String(c)}` as TextKey)}</div><div className="kpi-value">{count}</div></div>;
          })}</div>
          {issues.length === 0 ? <p className="check-ok">{t('tax.saftClean')}</p> : (
            <ul className="plain-list">{issues.map((i) => <li key={`${i.code}|${i.reference}`} className={i.category === 3 ? 'small' : 'small check-bad'}>{i.message}</li>)}</ul>
          )}
          {ret !== undefined && (
            <table className="partner-list cash-list">
              <tbody>{ret.lines.map((l) => <tr key={`${l.rowCode}|${l.columnCode}`}><td>{l.label}</td><td className="num">{l.rowCode === 'DEBIT' || l.rowCode === 'CREDIT' ? formatAmount(l.calculatedValue) : l.calculatedValue.split('.')[0]}</td></tr>)}</tbody>
            </table>
          )}
        </section>
        {id !== null && <ReturnPanel key={id} id={id} />}
      </div>
    </div>
  );
}

/** Declaratii › Impozit pe profit (02.02, C4 3.6, 4.4; macheta c4-05): fisa cumulata pe trimestru, randurile manuale cu motiv, contarea. */
export function ProfitTaxPage() {
  const can = useCan();
  const { act, error } = useAct();
  const [year, setYear] = useState(Number(today().slice(0, 4)));
  const [quarter, setQuarter] = useState(Math.max(1, Math.ceil(Number(today().slice(5, 7)) / 3) - 1));
  const [edit, setEdit] = useState<{ row: string; amount: string; reason: string } | undefined>();
  const sheet = useQuery({
    queryKey: ['finance', 'profit-tax', year, quarter],
    queryFn: async () => unwrap(await api.GET('/api/finance/profit-tax/{year}/{quarter}', { params: { path: { year, quarter } } })),
  });
  const s = sheet.data ?? null;
  const path = { params: { path: { year, quarter } } };
  const manage = can('Fin.ProfitTax.Manage');
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.declarations')} › {t('menu.profitTax')}</div>
      <div className="head">
        <div><h1>{t('menu.profitTax')}</h1><div className="sub2">{t('tax.profitSubtitle')}</div></div>
        {manage && <button type="button" className="btn" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/profit-tax/{year}/{quarter}/calculate', path)), 'tax.calculated')}>{t('tax.recalculate')}</button>}
      </div>
      <ErrorBanner message={sheet.isError ? sheet.error.message : error} />
      <div className="split">
        <section className="card" aria-label={t('menu.profitTax')}>
          <div className="chips-row">
            <input type="number" className="cell-input num" aria-label={t('tax.year')} value={year} onChange={(e) => setYear(Number(e.target.value))} />
            {[1, 2, 3, 4].map((q) => (
              <button key={q} type="button" className={quarter === q ? 'chip on' : 'chip'} aria-pressed={quarter === q} onClick={() => setQuarter(q)}>
                {q === 4 ? t('tax.annual') : t('tax.quarter', { q })}</button>
            ))}
          </div>
          {s === null ? <p className="muted">{t('tax.notCalculated')}</p> : (
            <table className="partner-list cash-list">
              <tbody>{s.lines.map((l) => (
                <tr key={l.rowCode} className={l.rowCode === 'DUE' || l.rowCode === 'TAXABLE' || l.rowCode === 'RESULT' ? 'strong' : undefined}>
                  <td>{l.label}{l.isManual && <span className="pill small">{t('tax.manual')}</span>}{l.reason != null && <div className="muted small">{l.reason}</div>}
                    {l.isManual && manage && (edit?.row === l.rowCode ? (
                      <div className="row-between">
                        <input className="cell-input num" aria-label={t('tax.amount')} value={edit.amount} onChange={(e) => setEdit({ ...edit, amount: e.target.value })} />
                        <input className="cell-input wide-input" aria-label={t('period.reason')} maxLength={300} value={edit.reason} onChange={(e) => setEdit({ ...edit, reason: e.target.value })} />
                        <button type="button" className="btn" disabled={edit.reason.trim() === ''} onClick={() => void act(async () => {
                          unwrap(await api.POST('/api/finance/profit-tax/{year}/{quarter}/manual-row', { ...path, body: { year, quarter, rowCode: l.rowCode, amount: edit.amount, reason: edit.reason.trim() } }));
                          setEdit(undefined);
                        }, 'tax.saved')}>{t('tax.save')}</button>
                      </div>
                    ) : <button type="button" className="link" onClick={() => setEdit({ row: l.rowCode, amount: l.amount, reason: l.reason ?? '' })}>{t('tax.edit')}</button>)}
                  </td>
                  <td className="num">{formatAmount(l.amount)}</td>
                </tr>))}</tbody>
            </table>
          )}
        </section>
        <section className="card" aria-label={t('tax.quarter', { q: quarter })}>
          <h2>{quarter === 4 ? t('tax.annual') : t('tax.quarter', { q: quarter })} {year}</h2>
          {s !== null && <p>{t('tax.taxDue', { amount: formatAmount(s.taxDue), rate: s.taxRate })}</p>}
          {s?.entryNumber != null ? <p className="check-ok">{t('tax.posted', { number: s.entryNumber, amount: formatAmount(s.postedAmount ?? '0.00') })}</p>
            : <p className="muted small">{t('tax.postHint')}</p>}
          {manage && s !== null && (
            <button type="button" className="btn primary" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/profit-tax/{year}/{quarter}/post', path)), 'tax.postedOk')}>
              {t('tax.post')}</button>
          )}
          <p className="muted small">{t('tax.treatmentHint')}</p>
        </section>
      </div>
    </div>
  );
}
