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
import { formatAmount } from './cashApi';
import { downloadFile } from './paymentOrderApi';
import { monthRange, previousMonth } from './taxModel';

type ReportRow = components['schemas']['ReportRowDto'];

const thisYear = () => new Date().getFullYear();
const today = () => new Date().toISOString().slice(0, 10);

function useCan() {
  const user = useOutletContext<CurrentUser>();
  return (permission: string) => user.permissions.includes(permission);
}

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

const amount = (v: string | null | undefined) => (v == null ? '—' : formatAmount(v));

/**
 * Rapoarte › Situatii financiare (02.02, C5 3.6, 4.2; macheta c5-03): anul si tipul, formularele F10 si F20 cu anul curent si precedent,
 * verificarile (bilant echilibrat), generarea fisierului (doar pe anul inchis), depunerea; registrul inventar al anului.
 */
export function FinancialStatementsPage() {
  const can = useCan();
  const { act, error } = useAct();
  const [year, setYear] = useState(thisYear() - 1);
  const [kind, setKind] = useState(1);
  const [form, setForm] = useState('F10');
  const [registration, setRegistration] = useState('');
  const data = useQuery({
    queryKey: ['finance', 'statements', year, kind],
    queryFn: async () => unwrap(await api.GET('/api/finance/financial-statements/{year}', { params: { path: { year }, query: { kind } } })),
  });
  const s = data.data ?? null;
  const lines = (s?.lines ?? []).filter((l) => l.formCode === form && !l.rowCode.endsWith('.90') && !l.rowCode.endsWith('.91'));
  const path = { params: { path: { year }, query: { kind } } };
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.reports')} › {t('menu.statements')}</div>
      <div className="head">
        <div><h1>{t('menu.statements')}</h1><div className="sub2">{t('stmt.subtitle')}</div></div>
        {can('Fin.Report.Statements') && s?.status !== 4 && (
          <button type="button" className="btn" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/financial-statements/{year}/calculate', path)),
            'stmt.calculated')}>{s === null ? t('stmt.calculate') : t('stmt.recalculate')}</button>
        )}
      </div>
      <ErrorBanner message={data.isError ? data.error.message : error} />
      <div className="split">
        <section className="card" aria-label={t('menu.statements')}>
          <div className="chips-row">
            <input type="number" className="cell-input num" aria-label={t('stmt.year')} value={year} onChange={(e) => setYear(Number(e.target.value))} />
            {[1, 2].map((k) => <button key={k} type="button" className={kind === k ? 'chip on' : 'chip'} aria-pressed={kind === k} onClick={() => setKind(k)}>
              {t(k === 1 ? 'stmt.annual' : 'stmt.semiannual')}</button>)}
          </div>
          <div className="chips-row">
            {['F10', 'F20'].map((f) => <button key={f} type="button" className={form === f ? 'chip on' : 'chip'} aria-pressed={form === f} onClick={() => setForm(f)}>
              {t(`stmt.form.${f}` as TextKey)}</button>)}
          </div>
          {s === null ? <p className="muted">{t('stmt.notCalculated')}</p> : (
            <table className="partner-list cash-list">
              <thead><tr><th>{t('stmt.row')}</th><th className="num">{year - 1}</th><th className="num">{year}</th></tr></thead>
              <tbody>{lines.map((l) => (
                <tr key={l.rowCode} className={l.isTotal ? 'strong' : undefined}>
                  <td>{l.label}<div className="muted small">{l.rowCode}</div></td>
                  <td className="num">{amount(l.priorYear)}</td><td className="num">{amount(l.currentYear)}</td>
                </tr>))}</tbody>
            </table>
          )}
        </section>
        <div>
          {s !== null && (
            <section className="card" aria-label={t('stmt.state')}>
              <h2>{t('stmt.title', { year })} <span className={s.status === 4 ? 'pill ok' : 'pill info'}>{t(`stmt.status.${String(s.status)}` as TextKey)}</span></h2>
              <p className="muted small">{t(`stmt.category.${String(s.entityCategory)}` as TextKey)}{s.registrationNumber != null ? ` · ${t('stmt.registration', { number: s.registrationNumber })}` : ''}</p>
              <p className={s.isBalanced ? 'check-ok' : 'check-bad'}>{s.isBalanced ? t('stmt.balanced') : t('stmt.unbalanced')}</p>
              <div className="row-between">
                {s.status < 3 && can('Fin.Report.Statements') && (
                  <button type="button" className="btn primary" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/financial-statements/{year}/generate',
                    { params: { path: { year } }, body: { year, kind, rowVersion: s.rowVersion } })), 'stmt.generated')}>{t('stmt.generate')}</button>
                )}
                {s.fileName != null && (
                  <button type="button" className="btn" onClick={() => void act(async () => {
                    const f = unwrap(await api.GET('/api/finance/financial-statements/{year}/file', path));
                    downloadFile({ fileName: f.fileName, contentType: f.contentType, contentBase64: f.content });
                  }, 'stmt.downloaded')}>{s.fileName}</button>
                )}
              </div>
              {s.status === 3 && can('Fin.Report.StatementsSubmit') && (
                <div className="row-between">
                  <label>{t('stmt.registrationNumber')}<input className="cell-input" maxLength={50} value={registration} onChange={(e) => setRegistration(e.target.value)} /></label>
                  <button type="button" className="btn primary" disabled={registration.trim() === ''} onClick={() => void act(async () => unwrap(await api.POST(
                    '/api/finance/financial-statements/{year}/submit', { params: { path: { year } }, body: { year, kind, rowVersion: s.rowVersion, registrationNumber: registration.trim() } })),
                  'stmt.submitted')}>{t('stmt.submit')}</button>
                </div>
              )}
            </section>
          )}
          {can('Fin.Report.InventoryRegister') && <InventoryRegisterCard year={year} />}
        </div>
      </div>
    </div>
  );
}

function InventoryRegisterCard({ year }: { readonly year: number }) {
  const { act, error } = useAct();
  const data = useQuery({ queryKey: ['finance', 'inventory-register', year], queryFn: async () => unwrap(await api.GET('/api/finance/inventory-register/{year}', { params: { path: { year } } })) });
  const r = data.data ?? null;
  const path = { params: { path: { year } } };
  return (
    <section className="card" aria-label={t('stmt.inventory')}>
      <h2>{t('stmt.inventory')}</h2>
      <p className="muted small">{r === null ? t('stmt.inventoryMissing') : t(r.isFinal ? 'stmt.inventoryFinal' : 'stmt.inventoryDraft', { lines: r.lines.length })}</p>
      <div className="row-between">
        <button type="button" className="btn" onClick={() => void act(async () => unwrap(await api.POST('/api/finance/inventory-register/{year}', path)), 'stmt.inventoryGenerated')}>
          {t('stmt.inventoryGenerate')}</button>
        {r !== null && (
          <button type="button" className="btn" onClick={() => void act(async () => {
            const f = unwrap(await api.GET('/api/finance/inventory-register/{year}/pdf', path));
            downloadFile({ fileName: f.fileName, contentType: f.contentType, contentBase64: f.content });
          }, 'stmt.downloaded')}>{t('stmt.inventoryPdf')}</button>
        )}
      </div>
      <ErrorBanner message={error} />
    </section>
  );
}

/** Rapoarte › Balanta de grup (02.02, C5 3.7, 4.3; macheta c5-04): planul alternativ si luna, coloanele pe firme, eliminarile, consolidatul si diferentele. */
export function GroupBalancePage() {
  const { act, error } = useAct();
  const charts = useQuery({ queryKey: ['finance', 'charts'], queryFn: async () => unwrap(await api.GET('/api/finance/charts')) });
  const alternative = (charts.data ?? []).filter((c) => c.kind === 2);
  const [chartId, setChartId] = useState<number | undefined>();
  const chart = chartId ?? alternative[0]?.id;
  const [month, setMonth] = useState(previousMonth(today()));
  const [runId, setRunId] = useState<number | undefined>();
  const year = Number(month.slice(0, 4));
  const m = Number(month.slice(5, 7));
  const run = useQuery({
    queryKey: ['finance', 'group-balance', runId ?? 0], enabled: runId !== undefined,
    queryFn: async () => unwrap(await api.GET('/api/finance/group-balance/{runId}', { params: { path: { runId: runId ?? 0 } } })),
  });
  const differences = useQuery({
    queryKey: ['finance', 'intercompany', chart ?? 0, month], enabled: chart !== undefined && runId !== undefined,
    queryFn: async () => unwrap(await api.GET('/api/finance/intercompany-differences', { params: { query: { chartId: chart ?? 0, year, month: m } } })),
  });
  const start = (final: boolean) => void act(async () => {
    setRunId(unwrap(await api.POST('/api/finance/group-balance', { body: { chartId: chart ?? 0, year, month: m, final } })));
  }, 'group.ran');
  const r = run.data;
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.reports')} › {t('menu.groupBalance')}</div>
      <div className="head"><div><h1>{t('menu.groupBalance')}</h1><div className="sub2">{t('group.subtitle')}</div></div></div>
      <ErrorBanner message={error} />
      <section className="card" aria-label={t('menu.groupBalance')}>
        <div className="chips-row">
          <select className="cell-input" aria-label={t('group.chart')} value={chart ?? ''} onChange={(e) => setChartId(Number(e.target.value))}>
            {alternative.map((c) => <option key={c.id} value={c.id}>{c.code} · {c.name}</option>)}
          </select>
          <input type="month" className="cell-input" aria-label={t('tax.month')} value={month} onChange={(e) => { if (e.target.value !== '') setMonth(e.target.value); }} />
          <button type="button" className="btn" disabled={chart === undefined} onClick={() => start(false)}>{t('group.preview')}</button>
          <button type="button" className="btn primary" disabled={chart === undefined} onClick={() => start(true)}>{t('group.final')}</button>
        </div>
        {alternative.length === 0 && <p className="muted">{t('group.noCharts')}</p>}
        {r !== undefined && (
          <table className="partner-list cash-list">
            <thead><tr><th>{t('group.account')}</th>{r.companies.map((c) => <th key={c.id} className="num">{c.code}</th>)}<th className="num">{t('group.eliminations')}</th>
              <th className="num">{t('group.consolidated')}</th></tr></thead>
            <tbody>{r.rows.map((row) => (
              <tr key={row.accountCode}>
                <td><span className="strong">{row.accountCode}</span> {row.accountName}</td>
                {r.companies.map((c) => <td key={c.id} className="num">{amount(row.byCompany[String(c.id)])}</td>)}
                <td className="num">{amount(row.eliminations)}</td><td className="num strong">{amount(row.consolidated)}</td>
              </tr>))}</tbody>
          </table>
        )}
        {r !== undefined && <p className="muted small">{t(r.isFinal ? 'group.isFinal' : 'group.isPreview')}</p>}
      </section>
      {(differences.data ?? []).length > 0 && (
        <section className="card" aria-label={t('group.differences')}>
          <h2>{t('group.differences')}</h2>
          <table className="partner-list cash-list">
            <thead><tr><th>{t('group.pair')}</th><th>{t('group.companies')}</th><th className="num">{t('group.first')}</th><th className="num">{t('group.second')}</th>
              <th className="num">{t('tax.difference')}</th></tr></thead>
            <tbody>{(differences.data ?? []).map((d) => (
              <tr key={`${d.pairName}|${String(d.firstCompanyId)}|${String(d.secondCompanyId)}`}>
                <td>{d.pairName}</td><td>{d.firstCompanyCode} → {d.secondCompanyCode}</td>
                <td className="num">{amount(d.firstAmount)}</td><td className="num">{amount(d.secondAmount)}</td>
                <td className={d.difference === '0.00' ? 'num' : 'num check-bad'}>{amount(d.difference)}</td>
              </tr>))}</tbody>
          </table>
        </section>
      )}
    </div>
  );
}

const emptyRow = (n: number): ReportRow => ({ rowCode: String(n), label: '', formula: '', isTotal: false, indent: 0 });

/** Rapoarte › Rapoarte definite (02.02, C5 3.5, decizia 4; macheta c5-05): cele livrate si ale firmei, randurile cu formule si previzualizarea pe luna. */
export function DefinedReportsPage() {
  const can = useCan();
  const { act, error } = useAct();
  const list = useQuery({ queryKey: ['finance', 'report-definitions'], queryFn: async () => unwrap(await api.GET('/api/finance/report-definitions')) });
  const [picked, setPicked] = useState<number | 'new' | undefined>();
  const items = list.data ?? [];
  const current = picked ?? items[0]?.id;
  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.reports')} › {t('menu.definedReports')}</div>
      <div className="head">
        <div><h1>{t('menu.definedReports')}</h1><div className="sub2">{t('rep.subtitle')}</div></div>
        {can('Fin.Report.Define') && <button type="button" className="btn primary" onClick={() => setPicked('new')}>{t('rep.new')}</button>}
      </div>
      <ErrorBanner message={list.isError ? list.error.message : error} />
      <div className="split">
        <section className="card" aria-label={t('menu.definedReports')}>
          {[1, 2].map((kind) => (
            <div key={kind}>
              <h3>{t(kind === 1 ? 'rep.delivered' : 'rep.own')}</h3>
              <table className="partner-list cash-list"><tbody>{items.filter((i) => i.kind === kind).map((i) => (
                <tr key={i.id} className={i.id === current ? 'picked' : undefined} onClick={() => setPicked(i.id)}>
                  <td><span className="strong">{i.name}</span><div className="muted small">{i.code} · {t('rep.rows', { count: i.rows })}</div></td>
                </tr>))}</tbody></table>
            </div>
          ))}
        </section>
        {current === 'new' ? <ReportEditor key="new" act={act} onSaved={(id) => setPicked(id)} />
          : current !== undefined && <ReportDetail key={current} id={current} act={act} canDefine={can('Fin.Report.Define')} />}
      </div>
    </div>
  );
}

type Act = (action: () => Promise<unknown>, message: TextKey) => Promise<void>;

function ReportDetail({ id, act, canDefine }: { readonly id: number; readonly act: Act; readonly canDefine: boolean }) {
  const def = useQuery({ queryKey: ['finance', 'report-definition', id], queryFn: async () => unwrap(await api.GET('/api/finance/report-definitions/{id}', { params: { path: { id } } })) });
  const [month, setMonth] = useState(previousMonth(today()));
  const [edit, setEdit] = useState(false);
  const { from, to } = monthRange(month);
  const result = useQuery({
    queryKey: ['finance', 'report-run', id, month],
    queryFn: async () => unwrap(await api.GET('/api/finance/report-definitions/{id}/run', { params: { path: { id }, query: { from, to } } })),
  });
  const d = def.data;
  if (d === undefined) {
    return <section className="card" />;
  }
  if (edit) {
    return <ReportEditor initial={d} act={act} onSaved={() => setEdit(false)} />;
  }
  const values = new Map((result.data?.rows ?? []).map((r) => [r.rowCode, r.current]));
  return (
    <section className="card" aria-label={d.name}>
      <h2>{d.name} <span className="pill">{t(d.kind === 1 ? 'rep.readOnly' : 'rep.ownShort')}</span></h2>
      <div className="chips-row">
        <input type="month" className="cell-input" aria-label={t('tax.month')} value={month} onChange={(e) => { if (e.target.value !== '') setMonth(e.target.value); }} />
        {d.kind === 2 && canDefine && <button type="button" className="btn" onClick={() => setEdit(true)}>{t('rep.edit')}</button>}
      </div>
      <ErrorBanner message={result.isError ? result.error.message : undefined} />
      <table className="partner-list cash-list">
        <thead><tr><th>{t('rep.row')}</th><th>{t('rep.formula')}</th><th className="num">{t('rep.value')}</th></tr></thead>
        <tbody>{d.rows.map((r) => (
          <tr key={r.rowCode} className={r.isTotal ? 'strong' : undefined}>
            <td>{r.label}<div className="muted small">{r.rowCode}</div></td><td className="mono small">{r.formula ?? ''}</td>
            <td className="num">{amount(values.get(r.rowCode))}</td>
          </tr>))}</tbody>
      </table>
    </section>
  );
}

function ReportEditor({ initial, act, onSaved }: {
  readonly initial?: components['schemas']['ReportDefinitionDto']; readonly act: Act; readonly onSaved: (id: number) => void;
}) {
  const [code, setCode] = useState(initial?.code ?? '');
  const [name, setName] = useState(initial?.name ?? '');
  const [rows, setRows] = useState<ReportRow[]>(initial?.rows.map((r) => ({ ...r })) ?? [emptyRow(1)]);
  const update = (i: number, patch: Partial<ReportRow>) => setRows(rows.map((r, j) => (j === i ? { ...r, ...patch } : r)));
  const save = () => void act(async () => {
    const body = { id: initial?.id ?? null, rowVersion: initial?.rowVersion ?? null, code: code.trim(), name: name.trim(), chartId: initial?.chartId ?? null,
      rows: rows.map((r) => ({ ...r, formula: r.formula?.trim() === '' ? null : r.formula })) };
    const id = initial === undefined ? unwrap(await api.POST('/api/finance/report-definitions', { body }))
      : unwrap(await api.PUT('/api/finance/report-definitions/{id}', { params: { path: { id: initial.id } }, body }));
    onSaved(id);
  }, 'rep.saved');
  return (
    <section className="card" aria-label={t('rep.editor')}>
      <div className="row-between">
        <label>{t('rep.code')}<input className="cell-input" maxLength={30} value={code} onChange={(e) => setCode(e.target.value)} /></label>
        <label>{t('rep.name')}<input className="cell-input wide-input" maxLength={200} value={name} onChange={(e) => setName(e.target.value)} /></label>
      </div>
      <table className="partner-list cash-list">
        <thead><tr><th>{t('rep.row')}</th><th>{t('rep.label')}</th><th>{t('rep.formula')}</th><th>{t('rep.total')}</th></tr></thead>
        <tbody>{rows.map((r, i) => (
          <tr key={i}>
            <td><input className="cell-input" aria-label={t('rep.row')} maxLength={30} value={r.rowCode} onChange={(e) => update(i, { rowCode: e.target.value })} /></td>
            <td><input className="cell-input" aria-label={t('rep.label')} maxLength={300} value={r.label} onChange={(e) => update(i, { label: e.target.value })} /></td>
            <td><input className="cell-input wide-input mono" aria-label={t('rep.formula')} maxLength={1000} value={r.formula ?? ''} onChange={(e) => update(i, { formula: e.target.value })} /></td>
            <td><input type="checkbox" aria-label={t('rep.total')} checked={r.isTotal} onChange={(e) => update(i, { isTotal: e.target.checked })} /></td>
          </tr>))}</tbody>
      </table>
      <p className="muted small">{t('rep.grammar')}</p>
      <div className="row-between">
        <button type="button" className="btn" onClick={() => setRows([...rows, emptyRow(rows.length + 1)])}>{t('rep.addRow')}</button>
        <button type="button" className="btn primary" disabled={code.trim() === '' || name.trim() === ''} onClick={save}>{t('rep.save')}</button>
      </div>
    </section>
  );
}
