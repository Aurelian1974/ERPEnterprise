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
import { fileToBase64 } from './bankApi';
import { downloadFile } from './paymentOrderApi';

type Chart = components['schemas']['ChartDto'];
type AltChart = components['schemas']['AltChartDto'];

export const chartKeys = { all: ['finance', 'charts'] as const };
const purposes: readonly TextKey[] = ['alt.purpose.1', 'alt.purpose.2', 'alt.purpose.3', 'alt.purpose.4', 'alt.purpose.5'];

/** Planurile alternative ale instalării (fără cel românesc), pentru listele de alegere din ecranele C1. */
export function useAltCharts() {
  return useQuery({
    queryKey: chartKeys.all,
    queryFn: async () => unwrap(await api.GET('/api/finance/charts')),
    select: (charts: readonly Chart[]) => charts.filter((c) => c.kind === 2),
  });
}

/**
 * Contabilitate › Planuri alternative (02.02, C1 3.1, 4.2; macheta c1-02): lista planurilor cu scopul, firmele, conturile și regulile;
 * fișa planului cu datele lui, conturile (adăugare și import din Excel) și firmele pentru care se calculează maparea.
 */
export function AltChartsPage() {
  const user = useOutletContext<CurrentUser>();
  const canManage = user.permissions.includes('Fin.Chart.Manage');
  const [selected, setSelected] = useState<number | 'new' | undefined>();
  const charts = useQuery({ queryKey: chartKeys.all, queryFn: async () => unwrap(await api.GET('/api/finance/charts')) });

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.altCharts')}</div>
      <div className="head">
        <div><h1>{t('alt.title')}</h1><div className="sub2">{t('alt.subtitle')}</div></div>
        {canManage && <div className="actions"><button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('alt.new')}</button></div>}
      </div>
      <div className="split">
        <section className="card" aria-label={t('alt.title')}>
          <ErrorBanner message={charts.isError ? charts.error.message : undefined} />
          <table className="partner-list cash-list" aria-busy={charts.isFetching}>
            <thead><tr><th scope="col">{t('alt.col.chart')}</th><th scope="col">{t('alt.col.purpose')}</th><th scope="col">{t('alt.col.state')}</th></tr></thead>
            <tbody>
              {(charts.data ?? []).map((c) => (
                <tr key={c.id} className={c.kind === 1 ? '' : c.id === selected ? 'clickable picked' : 'clickable'} tabIndex={c.kind === 1 ? -1 : 0}
                  onClick={() => c.kind === 2 && setSelected(c.id)} onKeyDown={(e) => e.key === 'Enter' && c.kind === 2 && setSelected(c.id)}>
                  <td><div className="strong mono">{c.code}</div><div className="muted small">{c.name}</div></td>
                  <td className="small">{c.kind === 1 ? t('alt.statutory') : `${t(purposes[(c.purpose ?? 5) - 1] ?? 'alt.purpose.5')} · ${c.languageCode}`}
                    <div className="muted small">{t('alt.counts', { accounts: c.accountCount, rules: c.ruleCount })}
                      {c.kind === 2 ? ` · ${c.companyCount === 0 ? t('alt.allCompanies') : t('alt.companies', { count: c.companyCount })}` : ''}</div></td>
                  <td>{c.kind === 1 ? <span className="pill">{t('alt.statutoryPill')}</span>
                    : c.isActive ? <span className="pill ok">{t('common.active')}</span> : <span className="pill">{t('common.inactive')}</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="muted small">{t('alt.hint')}</p>
        </section>
        <aside className="card">
          {selected === 'new' ? <ChartForm key="new" canManage={canManage} onSaved={(id) => setSelected(id)} />
            : selected === undefined ? <p className="muted">{t('alt.pick')}</p> : <ChartDetail key={selected} id={selected} canManage={canManage} />}
        </aside>
      </div>
    </div>
  );
}

function ChartDetail({ id, canManage }: { readonly id: number; readonly canManage: boolean }) {
  const [tab, setTab] = useState<'accounts' | 'data'>('accounts');
  const chart = useQuery({
    queryKey: [...chartKeys.all, id],
    queryFn: async () => unwrap(await api.GET('/api/finance/charts/{id}', { params: { path: { id } } })),
  });
  if (chart.isError) return <ErrorBanner message={chart.error.message} />;
  if (chart.data === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  const c = chart.data;
  return (
    <div>
      <h2><span className="mono">{c.code}</span> {c.name}</h2>
      <p className="muted small">{t(purposes[(c.purpose ?? 5) - 1] ?? 'alt.purpose.5')} · {c.languageCode}
        {' · '}{c.companies.length === 0 ? t('alt.allCompanies') : c.companies.map((x) => x.companyName).join(', ')}
        {c.requireCompleteMapping ? ` · ${t('alt.requireComplete')}` : ''}</p>
      <div className="tabs" role="tablist">
        <button type="button" role="tab" aria-selected={tab === 'accounts'} className={tab === 'accounts' ? 'tab on' : 'tab'} onClick={() => setTab('accounts')}>
          {t('alt.tab.accounts')}</button>
        <button type="button" role="tab" aria-selected={tab === 'data'} className={tab === 'data' ? 'tab on' : 'tab'} onClick={() => setTab('data')}>
          {t('alt.tab.data')}</button>
      </div>
      {tab === 'accounts' ? <ChartAccounts chart={c} canManage={canManage} /> : <ChartForm chart={c} canManage={canManage} onSaved={() => undefined} />}
    </div>
  );
}

function ChartForm({ chart, canManage, onSaved }: { readonly chart?: AltChart; readonly canManage: boolean; readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const companies = useOutletContext<CurrentUser>().companies;
  const [code, setCode] = useState(chart?.code ?? '');
  const [name, setName] = useState(chart?.name ?? '');
  const [purpose, setPurpose] = useState(chart?.purpose ?? 1);
  const [language, setLanguage] = useState(chart?.languageCode ?? 'en');
  const [complete, setComplete] = useState(chart?.requireCompleteMapping ?? false);
  const [active, setActive] = useState(chart?.isActive ?? true);
  const [firms, setFirms] = useState<readonly number[]>(chart?.companies.map((x) => x.companyId) ?? []);
  const [error, setError] = useState<string | undefined>();

  const save = async () => {
    setError(undefined);
    try {
      const body = { id: chart?.id ?? null, code, name, purpose, languageCode: language, requireCompleteMapping: complete, isActive: active, companies: [...firms],
        rowVersion: chart?.rowVersion ?? null };
      const saved = chart === undefined ? unwrap(await api.POST('/api/finance/charts', { body }))
        : unwrap(await api.PUT('/api/finance/charts/{id}', { params: { path: { id: chart.id } }, body }));
      notify(t('alt.saved'));
      await queryClient.invalidateQueries({ queryKey: chartKeys.all });
      onSaved(saved.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="settings-form">
      {chart === undefined && <h2>{t('alt.new')}</h2>}
      <label>{t('alt.code')}<input className="cell-input" value={code} maxLength={20} disabled={!canManage} onChange={(e) => setCode(e.target.value)} /></label>
      <label>{t('alt.name')}<input className="cell-input wide-input" value={name} maxLength={200} disabled={!canManage} onChange={(e) => setName(e.target.value)} /></label>
      <label>{t('alt.col.purpose')}
        <select className="cell-input" value={purpose} disabled={!canManage} onChange={(e) => setPurpose(Number(e.target.value))}>
          {purposes.map((p, i) => <option key={p} value={i + 1}>{t(p)}</option>)}
        </select>
      </label>
      <label>{t('alt.language')}<input className="cell-input" value={language} maxLength={2} disabled={!canManage} onChange={(e) => setLanguage(e.target.value)} /></label>
      <fieldset className="plain">
        <legend>{t('alt.firms')}</legend>
        {companies.map((c) => (
          <label key={c.id} className="check"><input type="checkbox" disabled={!canManage} checked={firms.includes(c.id)}
            onChange={(e) => setFirms(e.target.checked ? [...firms, c.id] : firms.filter((f) => f !== c.id))} />{c.name}</label>
        ))}
        <p className="muted small">{t('alt.firmsHint')}</p>
      </fieldset>
      <label className="check"><input type="checkbox" checked={complete} disabled={!canManage} onChange={(e) => setComplete(e.target.checked)} />{t('alt.requireComplete')}</label>
      <label className="check"><input type="checkbox" checked={active} disabled={!canManage} onChange={(e) => setActive(e.target.checked)} />{t('common.active')}</label>
      <ErrorBanner message={error} />
      {canManage && <div className="actions"><button type="button" className="btn primary" disabled={code.trim() === '' || name.trim() === ''}
        onClick={() => void save()}>{t('common.save')}</button></div>}
    </div>
  );
}

function ChartAccounts({ chart, canManage }: { readonly chart: AltChart; readonly canManage: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const accounts = useQuery({
    queryKey: [...chartKeys.all, chart.id, 'accounts'],
    queryFn: async () => unwrap(await api.GET('/api/finance/gl-accounts', { params: { query: { chartId: chart.id, includeInactive: true } } })),
  });
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [parentId, setParentId] = useState<number | ''>('');
  const [file, setFile] = useState<File | undefined>();
  const [report, setReport] = useState<string | undefined>();
  const [error, setError] = useState<string | undefined>();
  const list = accounts.data ?? [];
  const depth = (id: number | null): number => (id === null ? 0 : 1 + depth(list.find((a) => a.id === id)?.parentId ?? null));

  const run = async (action: () => Promise<unknown>, message: TextKey) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: chartKeys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  const add = () => run(async () => {
    unwrap(await api.POST('/api/finance/charts/{id}/accounts', {
      params: { path: { id: chart.id } },
      body: { id: null, chartId: chart.id, parentId: parentId === '' ? null : parentId, code: code.trim(), name: name.trim(), isActive: true, rowVersion: null },
    }));
    setCode('');
    setName('');
  }, 'alt.accountSaved');
  const importFile = (confirm: boolean) => run(async () => {
    if (file === undefined) return;
    const r = unwrap(await api.POST('/api/finance/charts/{id}/accounts/import', {
      params: { path: { id: chart.id } }, body: { chartId: chart.id, fileName: file.name, fileBase64: await fileToBase64(file), confirm },
    }));
    setReport(t('chart.importResult', { total: r.totalRows, valid: r.validRows, existing: r.existingRows, errors: r.errorRows, imported: r.importedRows }));
    if (r.errorFile !== null) downloadFile({ fileName: r.errorFile.fileName, contentType: r.errorFile.contentType, contentBase64: r.errorFile.content });
  }, confirm ? 'alt.imported' : 'alt.checked');

  return (
    <div>
      {list.length === 0 ? <p className="muted">{t('alt.noAccounts')}</p> : (
        <table className="partner-list cash-list">
          <thead><tr><th scope="col">{t('alt.account')}</th><th scope="col">{t('alt.col.state')}</th></tr></thead>
          <tbody>{list.map((a) => (
            <tr key={a.id}><td style={{ paddingLeft: `${8 + (depth(a.parentId) * 16)}px` }}><span className={a.isPostable ? 'mono' : 'mono strong'}>{a.code}</span> {a.name}</td>
              <td className="small">{a.isActive ? '' : t('common.inactive')}</td></tr>))}</tbody>
        </table>
      )}
      {canManage && (
        <div className="settings-form">
          <h3>{t('alt.addAccount')}</h3>
          <div className="row-between">
            <label>{t('alt.code')}<input className="cell-input" value={code} maxLength={20} onChange={(e) => setCode(e.target.value)} /></label>
            <label>{t('alt.name')}<input className="cell-input wide-input" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} /></label>
            <label>{t('alt.parent')}
              <select className="cell-input" value={parentId} onChange={(e) => setParentId(e.target.value === '' ? '' : Number(e.target.value))}>
                <option value="">{t('common.none')}</option>
                {list.map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
              </select>
            </label>
          </div>
          <div className="actions"><button type="button" className="btn" disabled={code.trim() === '' || name.trim() === ''} onClick={() => void add()}>
            {t('common.add')}</button></div>
          <h3>{t('alt.import')}</h3>
          <p className="muted small">{t('alt.importHelp')}</p>
          <input type="file" accept=".xlsx" aria-label={t('chart.file')} onChange={(e) => { setFile(e.target.files?.[0]); setReport(undefined); }} />
          {report !== undefined && <p>{report}</p>}
          <div className="actions">
            <button type="button" className="btn" disabled={file === undefined} onClick={() => void importFile(false)}>{t('chart.check')}</button>
            <button type="button" className="btn primary" disabled={file === undefined || report === undefined} onClick={() => void importFile(true)}>
              {t('chart.confirmImport')}</button>
          </div>
        </div>
      )}
      <ErrorBanner message={error} />
    </div>
  );
}
