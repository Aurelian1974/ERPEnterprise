import { useState } from 'react';
import { useOutletContext } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useDimensions } from '../dimensions/dimensionsApi';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { chartKeys, useAltCharts } from './AltChartsPage';
import { conditionParts, moveRule, sourceText, targetText, type MappingRule } from './mappingModel';

type Simulation = components['schemas']['SimulationDto'];

const dimensionTypes = ['COST_CENTER', 'PROJECT', 'WAREHOUSE', 'SITE'] as const;

/** Alegerea planului alternativ, comună ecranelor Reguli de mapare și Verificarea mapării. */
export function ChartPicker({ value, onChange }: { readonly value: number | undefined; readonly onChange: (id: number) => void }) {
  const charts = useAltCharts();
  return (
    <label className="inline">{t('map.chart')}
      <select className="cell-input" value={value ?? ''} onChange={(e) => onChange(Number(e.target.value))}>
        <option value="" disabled>{t('common.select')}</option>
        {(charts.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.code} · {c.name}</option>)}
      </select>
    </label>
  );
}

export function conditionsText(rule: MappingRule): string {
  const parts = conditionParts(rule).map((p) => (p.value === undefined ? t(p.key as TextKey) : `${t(p.key as TextKey)} = ${p.value}`));
  return parts.length === 0 ? '—' : parts.join(' · ');
}

/**
 * Contabilitate › Reguli de mapare (02.02, C1 3.5, 4.5; macheta c1-03): regulile unui plan alternativ în ordinea priorității (prima
 * potrivită câștigă), mutarea unei reguli mai sus sau mai jos, fișa regulii (sursă, condiții, ținte, vigoare) și simulatorul.
 */
export function MappingRulesPage() {
  const user = useOutletContext<CurrentUser>();
  const canManage = user.permissions.includes('Fin.Mapping.Manage');
  const charts = useAltCharts();
  const [picked, setPicked] = useState<number | undefined>();
  const chartId = picked ?? charts.data?.[0]?.id;
  const [selected, setSelected] = useState<number | 'new' | undefined>();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | undefined>();
  const rules = useQuery({
    queryKey: [...chartKeys.all, chartId ?? 0, 'rules'],
    enabled: chartId !== undefined,
    queryFn: async () => unwrap(await api.GET('/api/finance/charts/{id}/rules', { params: { path: { id: chartId ?? 0 } } })),
  });
  const list = rules.data ?? [];
  const current = list.find((r) => r.id === selected);

  const move = async (id: number, step: -1 | 1) => {
    if (chartId === undefined) return;
    setError(undefined);
    try {
      unwrap(await api.PUT('/api/finance/charts/{id}/rules/order', { params: { path: { id: chartId } }, body: { chartId, order: moveRule(list, id, step) } }));
      await queryClient.invalidateQueries({ queryKey: chartKeys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.mappingRules')}</div>
      <div className="head">
        <div><h1>{t('map.title')}</h1><div className="sub2">{t('map.subtitle')}</div></div>
        <div className="actions">
          <ChartPicker value={chartId} onChange={(id) => { setPicked(id); setSelected(undefined); }} />
          {canManage && chartId !== undefined && <button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('map.new')}</button>}
        </div>
      </div>
      {charts.data !== undefined && charts.data.length === 0 ? <p className="muted">{t('map.noCharts')}</p> : (
        <div className="split">
          <section className="card" aria-label={t('map.title')}>
            <ErrorBanner message={rules.isError ? rules.error.message : error} />
            {rules.data !== undefined && list.length === 0 ? <p className="muted">{t('map.empty')}</p> : (
              <table className="partner-list cash-list" aria-busy={rules.isFetching}>
                <thead><tr><th scope="col">{t('map.col.priority')}</th><th scope="col">{t('map.col.source')}</th><th scope="col">{t('map.col.conditions')}</th>
                  <th scope="col">{t('map.col.target')}</th>{canManage && <th scope="col"><span className="sr-only">{t('common.actions')}</span></th>}</tr></thead>
                <tbody>
                  {list.map((r, i) => (
                    <tr key={r.id} className={`${r.id === selected ? 'clickable picked' : 'clickable'}${r.isActive ? '' : ' muted'}`} tabIndex={0}
                      onClick={() => setSelected(r.id)} onKeyDown={(e) => e.key === 'Enter' && setSelected(r.id)}>
                      <td className="num">{r.priority}</td>
                      <td className="mono">{sourceText(r)}</td>
                      <td className="small">{conditionsText(r)}</td>
                      <td>{targetText(r)}<div className="muted small">{t('map.from', { date: isoToRo(r.validFrom) })}{r.isActive ? '' : ` · ${t('common.inactiveF')}`}</div></td>
                      {canManage && (
                        <td className="nowrap">
                          <button type="button" className="link" disabled={i === 0} aria-label={t('map.up')} onClick={(e) => { e.stopPropagation(); void move(r.id, -1); }}>▲</button>
                          <button type="button" className="link" disabled={i === list.length - 1} aria-label={t('map.down')}
                            onClick={(e) => { e.stopPropagation(); void move(r.id, 1); }}>▼</button>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <p className="muted small">{t('map.hint')}</p>
          </section>
          <aside className="card">
            {chartId !== undefined && (selected === 'new' || current !== undefined)
              ? <RuleForm key={`${chartId}-${selected ?? ''}`} chartId={chartId} rule={current} canManage={canManage}
                nextPriority={(list.at(-1)?.priority ?? 0) + 10} onSaved={(id) => setSelected(id)} />
              : <p className="muted">{t('map.pick')}</p>}
            {chartId !== undefined && <Simulator chartId={chartId} />}
          </aside>
        </div>
      )}
    </div>
  );
}

function RuleForm({ chartId, rule, canManage, nextPriority, onSaved }: {
  readonly chartId: number; readonly rule: MappingRule | undefined; readonly canManage: boolean; readonly nextPriority: number;
  readonly onSaved: (id: number) => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const ro = useQuery({ queryKey: ['finance', 'gl-accounts', false], queryFn: async () => unwrap(await api.GET('/api/finance/gl-accounts')) });
  const alt = useQuery({
    queryKey: [...chartKeys.all, chartId, 'accounts'],
    queryFn: async () => unwrap(await api.GET('/api/finance/gl-accounts', { params: { query: { chartId, includeInactive: true } } })),
  });
  const [priority, setPriority] = useState(rule?.priority ?? nextPriority);
  const [kind, setKind] = useState(rule?.sourceKind ?? 1);
  const [source, setSource] = useState(rule === undefined ? '' : rule.sourceKind === 2 ? `${rule.sourceFrom ?? ''}-${rule.sourceTo ?? ''}` : sourceText(rule));
  const [side, setSide] = useState(rule?.side ?? 0);
  const [partnerKind, setPartnerKind] = useState<number | ''>(rule?.partnerKind ?? '');
  const [dimType, setDimType] = useState<string>(rule?.dimensionType ?? 'COST_CENTER');
  const [dimCondition, setDimCondition] = useState(rule?.dimensionCondition ?? 0);
  const [dimValue, setDimValue] = useState<number | ''>(rule?.dimensionValueId ?? '');
  const [currency, setCurrency] = useState(rule?.currencyCode ?? '');
  const [balance, setBalance] = useState(rule?.balanceSign ?? 0);
  const [validFrom, setValidFrom] = useState(rule?.validFrom ?? `${isoToday().slice(0, 4)}-01-01`);
  const [validTo, setValidTo] = useState(rule?.validTo ?? '');
  const [active, setActive] = useState(rule?.isActive ?? true);
  const [note, setNote] = useState(rule?.note ?? '');
  const [targets, setTargets] = useState<readonly { accountId: number | ''; percent: string }[]>(
    rule?.targets.map((x) => ({ accountId: x.accountId, percent: x.percent })) ?? [{ accountId: '', percent: '100' }]);
  const [error, setError] = useState<string | undefined>();
  const values = useDimensions({ type: dimType, companyId: undefined, search: '', includeInactive: false }).data?.values ?? [];
  const leaves = (alt.data ?? []).filter((a) => a.isPostable && a.isActive);

  const save = async () => {
    setError(undefined);
    const codes = source.split(/[,\s]+/).map((c) => c.trim()).filter((c) => c !== '');
    const idOf = (code: string) => ro.data?.find((a) => a.code.toUpperCase() === code.toUpperCase())?.id;
    const ids = codes.map(idOf);
    if ((kind === 1 || kind === 3) && (ids.length === 0 || ids.some((x) => x === undefined))) {
      setError(t('map.unknownSource'));
      return;
    }

    const [from, to] = kind === 2 ? source.split('-').map((x) => x.trim()) : [null, null];
    try {
      const body = {
        id: rule?.id ?? null, chartId, priority, sourceKind: kind, sourceAccountId: kind === 1 ? ids[0] ?? null : null, sourceFrom: from ?? null, sourceTo: to ?? null,
        sourceAccounts: kind === 3 ? ids.filter((x): x is number => x !== undefined) : null, companyId: null, side: balance === 0 ? side : 0,
        partnerKind: balance === 0 && partnerKind !== '' ? partnerKind : null, partnerGroupId: null, partnerId: null,
        dimensionType: balance === 0 && dimCondition > 0 ? dimType : null, dimensionCondition: balance === 0 ? dimCondition : 0,
        dimensionValueId: balance === 0 && dimCondition === 1 && dimValue !== '' ? dimValue : null, itemGroupId: null,
        currencyCode: balance === 0 && currency.trim() !== '' ? currency.trim().toUpperCase() : null, balanceSign: balance, validFrom,
        validTo: validTo === '' ? null : validTo, isActive: active, note: note.trim() === '' ? null : note.trim(),
        targets: targets.filter((x) => x.accountId !== '').map((x) => ({ accountId: Number(x.accountId), percent: x.percent })), rowVersion: rule?.rowVersion ?? null,
      };
      const saved = rule === undefined ? unwrap(await api.POST('/api/finance/charts/{id}/rules', { params: { path: { id: chartId } }, body }))
        : unwrap(await api.PUT('/api/finance/charts/{id}/rules/{ruleId}', { params: { path: { id: chartId, ruleId: rule.id } }, body }));
      notify(t('map.saved'));
      await queryClient.invalidateQueries({ queryKey: chartKeys.all });
      onSaved(saved.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="settings-form">
      <h2>{rule === undefined ? t('map.new') : t('map.rule', { priority: rule.priority })}</h2>
      <div className="row-between">
        <label>{t('map.col.priority')}<input className="cell-input" type="number" min={1} value={priority} disabled={!canManage} onChange={(e) => setPriority(Number(e.target.value))} /></label>
        <label>{t('map.sourceKind')}
          <select className="cell-input" value={kind} disabled={!canManage} onChange={(e) => setKind(Number(e.target.value))}>
            <option value={1}>{t('map.kind.1')}</option><option value={2}>{t('map.kind.2')}</option><option value={3}>{t('map.kind.3')}</option>
          </select>
        </label>
      </div>
      <label>{t(kind === 1 ? 'map.source.1' : kind === 2 ? 'map.source.2' : 'map.source.3')}
        <input className="cell-input wide-input" value={source} disabled={!canManage} placeholder={kind === 2 ? '6000-6999' : kind === 3 ? '4111, 413' : '628'}
          onChange={(e) => setSource(e.target.value)} /></label>
      <label>{t('map.balance')}
        <select className="cell-input" value={balance} disabled={!canManage} onChange={(e) => setBalance(Number(e.target.value))}>
          <option value={0}>{t('map.balance.0')}</option><option value={1}>{t('map.cond.balance.1')}</option><option value={2}>{t('map.cond.balance.2')}</option>
        </select>
      </label>
      {balance === 0 && (
        <fieldset className="plain">
          <legend>{t('map.col.conditions')}</legend>
          <div className="row-between">
            <label>{t('map.dimension')}
              <select className="cell-input" value={dimType} disabled={!canManage} onChange={(e) => { setDimType(e.target.value); setDimValue(''); }}>
                {dimensionTypes.map((d) => <option key={d} value={d}>{t(`chart.dim.${d}` as TextKey)}</option>)}
              </select>
            </label>
            <label>{t('map.dimCondition')}
              <select className="cell-input" value={dimCondition} disabled={!canManage} onChange={(e) => setDimCondition(Number(e.target.value))}>
                {[0, 1, 2, 3].map((c) => <option key={c} value={c}>{t(`map.dimCondition.${c}` as TextKey)}</option>)}
              </select>
            </label>
            {dimCondition === 1 && (
              <label>{t('map.value')}
                <select className="cell-input" value={dimValue} disabled={!canManage} onChange={(e) => setDimValue(e.target.value === '' ? '' : Number(e.target.value))}>
                  <option value="">{t('common.select')}</option>
                  {values.map((v) => <option key={v.id} value={v.id}>{v.code} · {v.name}</option>)}
                </select>
              </label>
            )}
          </div>
          <div className="row-between">
            <label>{t('map.side')}
              <select className="cell-input" value={side} disabled={!canManage} onChange={(e) => setSide(Number(e.target.value))}>
                <option value={0}>{t('map.any')}</option><option value={1}>{t('map.cond.side.1')}</option><option value={2}>{t('map.cond.side.2')}</option>
              </select>
            </label>
            <label>{t('map.partnerKind')}
              <select className="cell-input" value={partnerKind} disabled={!canManage} onChange={(e) => setPartnerKind(e.target.value === '' ? '' : Number(e.target.value))}>
                <option value="">{t('map.any')}</option>
                {[1, 2, 3, 4].map((k) => <option key={k} value={k}>{t(`chart.partnerKind.${k}` as TextKey)}</option>)}
              </select>
            </label>
            <label>{t('map.currency')}<input className="cell-input" value={currency} maxLength={3} disabled={!canManage} onChange={(e) => setCurrency(e.target.value)} /></label>
          </div>
        </fieldset>
      )}
      <fieldset className="plain">
        <legend>{t('map.targets')}</legend>
        {targets.map((x, i) => (
          <div key={i} className="row-between">
            <select className="cell-input" value={x.accountId} disabled={!canManage} aria-label={t('map.col.target')}
              onChange={(e) => setTargets(targets.map((y, j) => (j === i ? { ...y, accountId: e.target.value === '' ? '' : Number(e.target.value) } : y)))}>
              <option value="">{t('common.select')}</option>
              {leaves.map((a) => <option key={a.id} value={a.id}>{a.code} · {a.name}</option>)}
            </select>
            <input className="cell-input" value={x.percent} disabled={!canManage} aria-label={t('map.percent')}
              onChange={(e) => setTargets(targets.map((y, j) => (j === i ? { ...y, percent: e.target.value } : y)))} /> %
            {canManage && targets.length > 1 && <button type="button" className="link" onClick={() => setTargets(targets.filter((_, j) => j !== i))}>{t('map.remove')}</button>}
          </div>
        ))}
        {canManage && <button type="button" className="link" onClick={() => setTargets([...targets, { accountId: '', percent: '' }])}>{t('map.addTarget')}</button>}
      </fieldset>
      <div className="row-between">
        <label>{t('chart.validFrom')}<input className="cell-input" type="date" value={validFrom} disabled={!canManage} onChange={(e) => setValidFrom(e.target.value)} /></label>
        <label>{t('chart.validTo')}<input className="cell-input" type="date" value={validTo} disabled={!canManage} onChange={(e) => setValidTo(e.target.value)} /></label>
      </div>
      <label>{t('map.note')}<input className="cell-input wide-input" value={note} maxLength={500} disabled={!canManage} onChange={(e) => setNote(e.target.value)} /></label>
      <label className="check"><input type="checkbox" checked={active} disabled={!canManage} onChange={(e) => setActive(e.target.checked)} />{t('common.activeF')}</label>
      <ErrorBanner message={error} />
      {canManage && <div className="actions"><button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button></div>}
    </div>
  );
}

function Simulator({ chartId }: { readonly chartId: number }) {
  const [code, setCode] = useState('');
  const [costCenter, setCostCenter] = useState<number | ''>('');
  const [side, setSide] = useState(0);
  const [date, setDate] = useState(isoToday());
  const [result, setResult] = useState<Simulation | undefined>();
  const [error, setError] = useState<string | undefined>();
  const centers = useDimensions({ type: 'COST_CENTER', companyId: undefined, search: '', includeInactive: false }).data?.values ?? [];

  const run = async () => {
    setError(undefined);
    try {
      setResult(unwrap(await api.GET('/api/finance/charts/{id}/simulate', {
        params: { path: { id: chartId }, query: { accountCode: code.trim(), side, ...(costCenter === '' ? {} : { costCenterId: costCenter }), onDate: date } },
      })));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="settings-form simulator">
      <h3>{t('map.simulator')}</h3>
      <div className="row-between">
        <label>{t('map.roAccount')}<input className="cell-input" value={code} placeholder="628" onChange={(e) => setCode(e.target.value)} /></label>
        <label>{t('chart.dim.COST_CENTER')}
          <select className="cell-input" value={costCenter} onChange={(e) => setCostCenter(e.target.value === '' ? '' : Number(e.target.value))}>
            <option value="">{t('common.none')}</option>
            {centers.map((v) => <option key={v.id} value={v.id}>{v.code} · {v.name}</option>)}
          </select>
        </label>
        <label>{t('map.side')}
          <select className="cell-input" value={side} onChange={(e) => setSide(Number(e.target.value))}>
            <option value={0}>{t('map.any')}</option><option value={1}>{t('map.cond.side.1')}</option><option value={2}>{t('map.cond.side.2')}</option>
          </select>
        </label>
        <label>{t('map.date')}<input className="cell-input" type="date" value={date} onChange={(e) => setDate(e.target.value)} /></label>
      </div>
      <div className="actions"><button type="button" className="btn" disabled={code.trim() === ''} onClick={() => void run()}>{t('map.simulate')}</button></div>
      <ErrorBanner message={error} />
      {result !== undefined && (result.ruleId == null ? <p className="banner warn">{t('map.unmapped')}</p> : (
        <p className="banner info">{t('map.result', { priority: result.priority ?? 0 })}{' '}
          <strong>{result.targets.map((x) => `${x.code} ${x.name}${result.targets.length > 1 ? ` (${x.percent}%)` : ''}`).join(', ')}</strong></p>
      ))}
    </div>
  );
}
