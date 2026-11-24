import { useMemo, useState } from 'react';
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
import { fileToBase64 } from './bankApi';
import { accountTypeKey, levelKey, nextAnalyticCode, visibleRows, type ChartFilter, type GlAccountItem } from './chartModel';
import { downloadFile } from './paymentOrderApi';

type Detail = components['schemas']['GlAccountDetailDto'];
type ImportResult = components['schemas']['GlAccountImportDto'];

const keys = { all: ['finance', 'gl-accounts'] as const };
const download = (file: components['schemas']['DownloadedFile']) =>
  downloadFile({ fileName: file.fileName, contentType: file.contentType, contentBase64: file.content });
const filters: readonly { value: ChartFilter; label: TextKey }[] = [
  { value: 'all', label: 'chart.filter.all' }, { value: 'analytics', label: 'chart.filter.analytics' }, { value: 'inactive', label: 'chart.filter.inactive' },
];
const dimensionTypes = ['COST_CENTER', 'PROJECT', 'WAREHOUSE', 'SITE'] as const;
const modeKeys: readonly TextKey[] = ['chart.mode.0', 'chart.mode.1', 'chart.mode.2'];

function typeText(type: number | null, empty: string) {
  const key = accountTypeKey(type);
  return key === undefined ? empty : t(key);
}

/**
 * Contabilitate › Plan de conturi (02.02, C1 3.2–3.4, 4.1; macheta c1-01): arborele planului românesc (clase, grupe, sintetice, analitice)
 * cu căutare și filtre; fișa contului cu Date generale, Dimensiuni, Roluri și Istoric; analiticele noi și importul lor din Excel.
 */
export function ChartOfAccountsPage() {
  const user = useOutletContext<CurrentUser>();
  const canManage = user.permissions.includes('Fin.Account.Manage');
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<ChartFilter>('all');
  const [expanded, setExpanded] = useState<ReadonlySet<number>>(new Set());
  const [selected, setSelected] = useState<number | undefined>();
  const [mode, setMode] = useState<'detail' | 'new' | 'import'>('detail');
  const list = useQuery({
    queryKey: [...keys.all, filter === 'inactive'],
    queryFn: async () => unwrap(await api.GET('/api/finance/gl-accounts', { params: { query: { includeInactive: filter === 'inactive' } } })),
  });
  const accounts = useMemo(() => list.data ?? [], [list.data]);
  const rows = useMemo(() => visibleRows(accounts, expanded, search, filter), [accounts, expanded, search, filter]);
  const analytics = accounts.filter((a) => !a.isSystem).length;
  const current = accounts.find((a) => a.id === selected);

  const toggle = (id: number) => setExpanded((old) => {
    const next = new Set(old);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.chartOfAccounts')}</div>
      <div className="head">
        <div><h1>{t('chart.title')}</h1><div className="sub2">{t('chart.subtitle')}</div></div>
        {canManage && (
          <div className="actions">
            <button type="button" className="btn" onClick={() => setMode('import')}>{t('chart.import')}</button>
            <button type="button" className="btn primary" disabled={current === undefined || current.level < 3 || !current.isActive}
              title={current === undefined || current.level < 3 ? t('chart.pickParent') : undefined} onClick={() => setMode('new')}>{t('chart.newAnalytic')}</button>
          </div>
        )}
      </div>
      <div className="split">
        <section className="card tree-card" aria-label={t('chart.title')}>
          <input className="cell-input wide-input" type="search" placeholder={t('chart.search')} value={search} onChange={(e) => setSearch(e.target.value)}
            aria-label={t('chart.search')} />
          <div className="chips-row" role="group" aria-label={t('chart.filter')}>
            {filters.map((f) => (
              <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                onClick={() => setFilter(f.value)}>{t(f.label)}</button>
            ))}
          </div>
          <ErrorBanner message={list.isError ? list.error.message : undefined} />
          <table className="partner-list cash-list chart-tree" aria-busy={list.isFetching}>
            <thead><tr><th scope="col">{t('chart.col.account')}</th><th scope="col">{t('chart.col.type')}</th></tr></thead>
            <tbody>
              {rows.map(({ account: a, depth, hasChildren, expanded: open }) => (
                <tr key={a.id} className={`${a.id === selected ? 'clickable picked' : 'clickable'}${a.isActive ? '' : ' inactive'}`} tabIndex={0}
                  onClick={() => { setSelected(a.id); setMode('detail'); }} onKeyDown={(e) => e.key === 'Enter' && setSelected(a.id)}>
                  <td style={{ paddingLeft: `${8 + depth * 16}px` }}>
                    {hasChildren ? (
                      <button type="button" className="link tree-toggle" aria-expanded={open} aria-label={open ? t('chart.collapse') : t('chart.expand')}
                        onClick={(e) => { e.stopPropagation(); toggle(a.id); }}>{open ? '▾' : '▸'}</button>
                    ) : <span className="tree-toggle" />}
                    <span className={a.level <= 2 ? 'strong' : a.isSystem ? 'mono' : 'mono analytic'}>{a.code}</span> {a.name}
                    {a.companyId !== null && <span className="pill small">{t('chart.oneCompany')}</span>}
                  </td>
                  <td className="muted small">{typeText(a.accountType, '')}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="muted small">{t('chart.summary', { shipped: accounts.length - analytics, analytics })}</p>
        </section>
        <aside className="card">
          {mode === 'import' ? <ImportPanel onDone={() => setMode('detail')} />
            : mode === 'new' && current !== undefined ? (
              <NewAnalytic parent={current} code={nextAnalyticCode(current.code, accounts)}
                onSaved={(id) => { setExpanded((old) => new Set([...old, current.id])); setSelected(id); setMode('detail'); }} />
            ) : selected === undefined ? <p className="muted">{t('chart.pick')}</p>
              : <AccountDetail key={selected} id={selected} canManage={canManage} />}
        </aside>
      </div>
    </div>
  );
}

function NewAnalytic({ parent, code: proposed, onSaved }: { readonly parent: GlAccountItem; readonly code: string; readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [code, setCode] = useState(proposed);
  const [name, setName] = useState('');
  const [currency, setCurrency] = useState('');
  const [onlyCompany, setOnlyCompany] = useState(false);
  const [error, setError] = useState<string | undefined>();

  const save = async () => {
    setError(undefined);
    try {
      const saved = unwrap(await api.POST('/api/finance/gl-accounts', {
        body: {
          id: null, parentId: parent.id, code: code.trim(), name: name.trim(), onlyThisCompany: onlyCompany, currencyMode: currency.trim() === '' ? 0 : 2,
          currencyCode: currency.trim() === '' ? null : currency.trim().toUpperCase(), partyRequirement: 0, statutoryReporting: true,
          manualPosting: parent.manualPosting, taxTreatment: parent.taxTreatment, validFrom: null, validTo: null, rowVersion: null,
        },
      }));
      notify(t('chart.saved'));
      await queryClient.invalidateQueries({ queryKey: keys.all });
      onSaved(saved.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="settings-form">
      <h2>{t('chart.newUnder', { code: parent.code, name: parent.name })}</h2>
      <label>{t('chart.code')}<input className="cell-input" value={code} onChange={(e) => setCode(e.target.value)} /></label>
      <label>{t('chart.name')}<input className="cell-input wide-input" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} /></label>
      <label>{t('chart.currencyOnly')}<input className="cell-input" value={currency} maxLength={3} placeholder="EUR" onChange={(e) => setCurrency(e.target.value)} /></label>
      <label className="check"><input type="checkbox" checked={onlyCompany} onChange={(e) => setOnlyCompany(e.target.checked)} />{t('chart.onlyCompany')}</label>
      <p className="muted small">{t('chart.inherits')}</p>
      <ErrorBanner message={error} />
      <div className="actions">
        <button type="button" className="btn primary" disabled={name.trim() === '' || code.trim() === ''} onClick={() => void save()}>{t('common.save')}</button>
      </div>
    </div>
  );
}

function AccountDetail({ id, canManage }: { readonly id: number; readonly canManage: boolean }) {
  const [tab, setTab] = useState<'general' | 'dimensions' | 'roles' | 'history'>('general');
  const detail = useQuery({
    queryKey: [...keys.all, 'detail', id],
    queryFn: async () => unwrap(await api.GET('/api/finance/gl-accounts/{id}', { params: { path: { id } } })),
  });
  if (detail.isError) return <ErrorBanner message={detail.error.message} />;
  const a = detail.data;
  if (a === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  const tabs: readonly { value: typeof tab; label: TextKey }[] = [
    { value: 'general', label: 'chart.tab.general' }, { value: 'dimensions', label: 'chart.tab.dimensions' }, { value: 'roles', label: 'chart.tab.roles' },
    { value: 'history', label: 'chart.tab.history' },
  ];

  return (
    <div>
      <h2><span className="mono">{a.code}</span> {a.name} {a.isSystem ? <span className="pill">{t('chart.shipped')}</span> : <span className="pill info">{t('chart.analytic')}</span>}
        {!a.isActive && <span className="pill warn">{t('chart.inactiveOne')}</span>}</h2>
      <p className="muted small">
        {t(levelKey(a.level))} · {a.isPostable ? t('chart.postable') : t('chart.notPostable')} · {a.companyId === null ? t('chart.allCompanies') : t('chart.oneCompany')}
        {a.closingTarget === 1 ? ` · ${t('chart.closesMonthly')}` : a.closingTarget === 2 ? ` · ${t('chart.closesYearly')}` : ''}
      </p>
      <div className="tabs" role="tablist">
        {tabs.map((x) => (
          <button key={x.value} type="button" role="tab" aria-selected={tab === x.value} className={tab === x.value ? 'tab on' : 'tab'}
            onClick={() => setTab(x.value)}>{t(x.label)}</button>
        ))}
      </div>
      {tab === 'general' && <General a={a} canManage={canManage} />}
      {tab === 'dimensions' && <Dimensions a={a} canManage={canManage} />}
      {tab === 'roles' && (a.roles.length === 0 ? <p className="muted">{t('chart.noRoles')}</p> : (
        <table className="partner-list cash-list">
          <thead><tr><th scope="col">{t('chart.role')}</th><th scope="col">{t('chart.roleScope')}</th><th scope="col">{t('chart.validFrom')}</th></tr></thead>
          <tbody>{a.roles.map((r) => (
            <tr key={r.id}><td className="mono">{r.role}</td><td>{r.companyId === null ? t('chart.allCompanies') : t('chart.oneCompany')}
              {r.partnerKind !== null ? ` · ${t(`chart.partnerKind.${r.partnerKind}` as TextKey)}` : ''}{r.currencyCode !== null ? ` · ${r.currencyCode}` : ''}</td>
            <td>{isoToRo(r.validFrom)}</td></tr>))}</tbody>
        </table>
      ))}
      {tab === 'history' && (a.nameHistory.length === 0 ? <p className="muted">{t('chart.noHistory')}</p> : (
        <table className="partner-list cash-list">
          <thead><tr><th scope="col">{t('chart.oldName')}</th><th scope="col">{t('chart.until')}</th><th scope="col">{t('chart.changedBy')}</th></tr></thead>
          <tbody>{a.nameHistory.map((h) => (
            <tr key={h.validTo}><td>{h.name}</td><td>{isoToRo(h.validTo.slice(0, 10))}</td><td>{h.changedByName ?? ''}</td></tr>))}</tbody>
        </table>
      ))}
    </div>
  );
}

function General({ a, canManage }: { readonly a: Detail; readonly canManage: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [name, setName] = useState(a.name);
  const [currency, setCurrency] = useState(a.currencyCode ?? '');
  const [party, setParty] = useState(a.partyRequirement);
  const [statutory, setStatutory] = useState(a.statutoryReporting);
  const [manual, setManual] = useState(a.manualPosting);
  const [error, setError] = useState<string | undefined>();
  const editable = canManage;

  const run = async (action: () => Promise<unknown>, message: TextKey) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: keys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  const save = () => run(async () => unwrap(await api.PUT('/api/finance/gl-accounts/{id}', {
    params: { path: { id: a.id } },
    body: {
      id: a.id, parentId: a.parentId, code: a.code, name: name.trim(), onlyThisCompany: a.companyId !== null, currencyMode: currency.trim() === '' ? (a.currencyMode === 1 ? 1 : 0) : 2,
      currencyCode: currency.trim() === '' ? null : currency.trim().toUpperCase(), partyRequirement: party, statutoryReporting: statutory, manualPosting: manual,
      taxTreatment: a.taxTreatment, validFrom: a.validFrom, validTo: a.validTo, rowVersion: a.rowVersion,
    },
  })), 'chart.saved');
  const setActive = (isActive: boolean) => run(async () => unwrap(await api.PUT('/api/finance/gl-accounts/{id}/active', {
    params: { path: { id: a.id } }, body: { id: a.id, isActive, rowVersion: a.rowVersion },
  })), isActive ? 'chart.activated' : 'chart.deactivated');

  return (
    <div className="settings-form">
      <dl className="facts">
        <dt>{t('chart.code')}</dt><dd className="mono">{a.code}</dd>
        <dt>{t('chart.col.type')}</dt><dd>{typeText(a.accountType, '—')}</dd>
        <dt>{t('chart.saft')}</dt><dd className="mono">{a.saftAccountCode ?? '—'}</dd>
        {a.parentCode !== null && <><dt>{t('chart.parent')}</dt><dd className="mono">{a.parentCode}</dd></>}
        {a.validTo !== null && <><dt>{t('chart.validTo')}</dt><dd>{isoToRo(a.validTo)}</dd></>}
      </dl>
      <label>{t('chart.name')}<input className="cell-input wide-input" value={name} maxLength={200} disabled={!editable || a.isSystem}
        onChange={(e) => setName(e.target.value)} /></label>
      {a.isSystem && <p className="muted small">{t('chart.systemReadonly')}</p>}
      <label>{t('chart.currencyOnly')}<input className="cell-input" value={currency} maxLength={3} disabled={!editable} onChange={(e) => setCurrency(e.target.value)} /></label>
      <label>{t('chart.party')}
        <select className="cell-input" value={party} disabled={!editable} onChange={(e) => setParty(Number(e.target.value))}>
          {[0, 1, 2, 3].map((p) => <option key={p} value={p}>{t(`chart.party.${p}` as TextKey)}</option>)}
        </select>
      </label>
      <label className="check"><input type="checkbox" checked={statutory} disabled={!editable} onChange={(e) => setStatutory(e.target.checked)} />{t('chart.statutory')}</label>
      <label className="check"><input type="checkbox" checked={manual} disabled={!editable} onChange={(e) => setManual(e.target.checked)} />{t('chart.manual')}</label>
      <ErrorBanner message={error} />
      {editable && (
        <div className="actions">
          {a.isActive
            ? <button type="button" className="btn" onClick={() => void setActive(false)}>{t('chart.deactivate')}</button>
            : <button type="button" className="btn" onClick={() => void setActive(true)}>{t('chart.activate')}</button>}
          <button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button>
        </div>
      )}
    </div>
  );
}

function Dimensions({ a, canManage }: { readonly a: Detail; readonly canManage: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const own = a.dimensions.filter((d) => !d.isInherited);
  const [modes, setModes] = useState<Record<string, number | undefined>>(() => Object.fromEntries(own.map((d) => [d.dimensionType, d.mode])));
  const [error, setError] = useState<string | undefined>();
  const inherited = (type: string) => a.dimensions.find((d) => d.dimensionType === type && d.isInherited);

  const save = async () => {
    setError(undefined);
    try {
      const rules = Object.entries(modes).flatMap(([dimensionType, mode]) => (mode === undefined ? [] : [{ dimensionType, mode }]));
      unwrap(await api.PUT('/api/finance/gl-accounts/{id}/dimension-rules', { params: { path: { id: a.id } }, body: { accountId: a.id, rules } }));
      notify(t('chart.saved'));
      await queryClient.invalidateQueries({ queryKey: keys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="settings-form">
      <table className="partner-list cash-list">
        <thead><tr><th scope="col">{t('chart.dimension')}</th><th scope="col">{t('chart.mode')}</th><th scope="col">{t('chart.source')}</th></tr></thead>
        <tbody>
          {dimensionTypes.map((type) => {
            const from = inherited(type);
            const value = modes[type];
            return (
              <tr key={type}>
                <td>{t(`chart.dim.${type}` as TextKey)}</td>
                <td>
                  <select className="cell-input" value={value ?? ''} disabled={!canManage}
                    onChange={(e) => setModes({ ...modes, [type]: e.target.value === '' ? undefined : Number(e.target.value) })}>
                    <option value="">{from === undefined ? t('chart.mode.default') : t('chart.mode.inherited', { mode: t(modeKeys[from.mode] ?? 'chart.mode.1') })}</option>
                    {modeKeys.map((k, i) => <option key={k} value={i}>{t(k)}</option>)}
                  </select>
                </td>
                <td className="muted small">{value !== undefined ? t('chart.ownRule') : from !== undefined ? t('chart.inheritedFrom', { code: from.fromCode }) : ''}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <ErrorBanner message={error} />
      {canManage && <div className="actions"><button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button></div>}
    </div>
  );
}

function ImportPanel({ onDone }: { readonly onDone: () => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [file, setFile] = useState<File | undefined>();
  const [result, setResult] = useState<ImportResult | undefined>();
  const [error, setError] = useState<string | undefined>();

  const send = async (confirm: boolean) => {
    if (file === undefined) return;
    setError(undefined);
    try {
      const r = unwrap(await api.POST('/api/finance/gl-accounts/import', { body: { fileName: file.name, fileBase64: await fileToBase64(file), confirm } }));
      setResult(r);
      if (confirm) {
        notify(t('chart.imported', { count: r.importedRows }));
        await queryClient.invalidateQueries({ queryKey: keys.all });
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="settings-form">
      <h2>{t('chart.importTitle')}</h2>
      <p className="muted small">{t('chart.importHelp')}</p>
      <button type="button" className="link" onClick={() => void (async () => download(unwrap(await api.GET('/api/finance/gl-accounts/import-template'))))()}>
        {t('chart.template')}</button>
      <input type="file" accept=".xlsx" aria-label={t('chart.file')} onChange={(e) => { setFile(e.target.files?.[0]); setResult(undefined); }} />
      {result !== undefined && (
        <div>
          <p>{t('chart.importResult', { total: result.totalRows, valid: result.validRows, existing: result.existingRows, errors: result.errorRows,
            imported: result.importedRows })}</p>
          {result.errors.length > 0 && (
            <table className="partner-list cash-list">
              <thead><tr><th scope="col">{t('chart.row')}</th><th scope="col">{t('chart.code')}</th><th scope="col">{t('chart.problem')}</th></tr></thead>
              <tbody>{result.errors.map((e) => <tr key={e.row}><td>{e.row}</td><td className="mono">{e.code}</td><td>{e.message}</td></tr>)}</tbody>
            </table>
          )}
          {result.errorFile !== null && <button type="button" className="link" onClick={() => { if (result.errorFile !== null) download(result.errorFile); }}>{t('chart.downloadErrors')}</button>}
        </div>
      )}
      <ErrorBanner message={error} />
      <div className="actions">
        <button type="button" className="btn" onClick={onDone}>{t('common.cancel')}</button>
        <button type="button" className="btn" disabled={file === undefined} onClick={() => void send(false)}>{t('chart.check')}</button>
        <button type="button" className="btn primary" disabled={file === undefined || result === undefined || result.validRows === 0 || result.importedRows > 0}
          onClick={() => void send(true)}>{t('chart.confirmImport')}</button>
      </div>
    </div>
  );
}
