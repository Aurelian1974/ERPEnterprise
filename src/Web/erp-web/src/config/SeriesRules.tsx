import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { t, type TextKey } from '../i18n';
import type { NumberSeries } from '../org/orgApi';
import type { OrgTree } from '../org/orgTree';

type Rule = components['schemas']['NumberSeriesRuleDto'];
type RulePage = components['schemas']['NumberSeriesRulePageDto'];

const rulesKey = (companyId: number) => ['core', 'number-series-rules', companyId] as const;

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

/** Textul conditiilor unei reguli („Utilizator: Ion Popescu · Partener: non-UE”). */
export function ruleConditions(rule: Pick<Rule, 'userName' | 'branchName' | 'locationName' | 'partnerScope'>): string {
  return [
    rule.userName == null ? null : `${t('seriesRule.user')}: ${rule.userName}`,
    rule.branchName == null ? null : `${t('seriesRule.branch')}: ${rule.branchName}`,
    rule.locationName == null ? null : `${t('seriesRule.location')}: ${rule.locationName}`,
    rule.partnerScope == null ? null : `${t('seriesRule.partner')}: ${t(`seriesRule.scope.${rule.partnerScope as 1 | 2 | 3}`)}`,
  ].filter((x) => x !== null).join(' · ');
}

/**
 * Regulile de alegere a seriei (02.01, 3.6 punctul 16), sub lista seriilor: pentru un tip de document, conditiile (utilizator, punct
 * de lucru, locatie, tipul partenerului) si seria propusa; prima regula potrivita, in ordine, castiga. Pe document utilizatorul poate
 * alege oricand alta serie deschisa.
 */
export function SeriesRules({ companyId, series, tree, documentTypes }: {
  readonly companyId: number;
  readonly series: readonly NumberSeries[];
  readonly tree: OrgTree;
  readonly documentTypes: readonly { value: string; text: string }[];
}) {
  const query = useQuery({
    queryKey: rulesKey(companyId),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/companies/{companyId}/number-series-rules', { params: { path: { companyId } }, signal })),
  });
  const [selected, setSelected] = useState<Rule | 'new' | undefined>();

  return (
    <section className="card series-rules" aria-labelledby="series-rules-title">
      <div className="card-head">
        <h2 id="series-rules-title">{t('seriesRule.title')}</h2>
        <button type="button" className="btn ghost small" onClick={() => setSelected('new')}>{t('seriesRule.add')}</button>
      </div>
      <p className="muted small">{t('seriesRule.hint')}</p>
      {query.isError && <ErrorBanner message={problem(query.error)} />}
      {query.data !== undefined && query.data.rules.length === 0 && <p className="muted">{t('seriesRule.empty')}</p>}
      {query.data !== undefined && query.data.rules.length > 0 && (
        <table className="simple dim-table">
          <thead>
            <tr>
              <th scope="col">{t('seriesRule.documentType')}</th>
              <th scope="col">{t('seriesRule.priority')}</th>
              <th scope="col">{t('seriesRule.when')}</th>
              <th scope="col">{t('seriesRule.series')}</th>
              <th scope="col">{t('dim.state')}</th>
            </tr>
          </thead>
          <tbody>
            {query.data.rules.map((r) => (
              <tr key={r.id} className={selected !== 'new' && selected?.id === r.id ? 'current' : ''} onClick={() => setSelected(r)}>
                <td>{r.documentTypeName}</td>
                <td>{r.priority}</td>
                <td>{ruleConditions(r)}</td>
                <td className="strong">{r.prefix.toUpperCase()}</td>
                <td><span className={r.isActive ? 'pill ok' : 'pill off'}>{r.isActive ? t('common.active') : t('dim.inactiveOne')}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {selected !== undefined && query.data !== undefined && (
        <RuleForm key={selected === 'new' ? 'new' : `${selected.id}-${selected.rowVersion}`} companyId={companyId} page={query.data}
          rule={selected === 'new' ? undefined : selected} series={series} tree={tree} documentTypes={documentTypes} onDone={() => setSelected(undefined)} />
      )}
    </section>
  );
}

function RuleForm({ companyId, page, rule, series, tree, documentTypes, onDone }: {
  readonly companyId: number;
  readonly page: RulePage;
  readonly rule: Rule | undefined;
  readonly series: readonly NumberSeries[];
  readonly tree: OrgTree;
  readonly documentTypes: readonly { value: string; text: string }[];
  readonly onDone: () => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [documentType, setDocumentType] = useState(rule?.documentTypeCode ?? documentTypes[0]?.value ?? '');
  const [priority, setPriority] = useState(String(rule?.priority ?? 10));
  const [userId, setUserId] = useState<number | null>(rule?.userId ?? null);
  const [branchId, setBranchId] = useState<number | null>(rule?.branchId ?? null);
  const [locationId, setLocationId] = useState<number | null>(rule?.locationId ?? null);
  const [scope, setScope] = useState<number | null>(rule?.partnerScope ?? null);
  const [prefix, setPrefix] = useState(rule?.prefix.toUpperCase() ?? '');
  const [isActive, setIsActive] = useState(rule?.isActive ?? true);
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const guard = useUnsavedChanges(true);
  // Prefixele seriilor firmei pentru tipul ales (din orice an), ca regula sa aiba o tinta.
  const prefixes = [...new Set(series.filter((s) => s.documentTypeCode === documentType).map((s) => s.prefix.toUpperCase()))].sort();
  const number = (v: string) => (v === '' ? null : Number(v));

  const save = async () => {
    setError(undefined);
    setBusy(true);
    try {
      const body = {
        id: rule?.id ?? null, companyId, documentTypeCode: documentType, priority: Number(priority), userId, branchId, locationId, partnerScope: scope,
        prefix, isActive, rowVersion: rule?.rowVersion ?? null,
      };
      if (rule === undefined) {
        unwrap(await api.POST('/api/core/companies/{companyId}/number-series-rules', { params: { path: { companyId } }, body }));
      } else {
        unwrap(await api.PUT('/api/core/companies/{companyId}/number-series-rules/{id}', { params: { path: { companyId, id: rule.id } }, body }));
      }
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: rulesKey(companyId) });
      notify(t('common.saved'));
      onDone();
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="subform" noValidate onSubmit={(e) => { e.preventDefault(); void save(); }} aria-label={t('seriesRule.edit')}>
      <ErrorBanner message={error} />
      <div className="grid g3">
        <label className="field">
          <span className="label">{t('seriesRule.documentType')}</span>
          <select className="cell-input" value={documentType} onChange={(e) => { setDocumentType(e.target.value); setPrefix(''); }}>
            {documentTypes.map((d) => <option key={d.value} value={d.value}>{d.text}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('seriesRule.priority')}</span>
          <input className="cell-input num" type="number" min={1} max={999} value={priority} onChange={(e) => setPriority(e.target.value)} />
        </label>
        <label className="field">
          <span className="label">{t('seriesRule.series')} *</span>
          <select className="cell-input" value={prefix} onChange={(e) => setPrefix(e.target.value)}>
            <option value="">{t('seriesRule.chooseSeries')}</option>
            {prefixes.map((p) => <option key={p} value={p}>{p}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('seriesRule.user')}</span>
          <select className="cell-input" value={userId ?? ''} onChange={(e) => setUserId(number(e.target.value))}>
            <option value="">{t('seriesRule.any')}</option>
            {page.users.map((u) => <option key={u.id} value={u.id}>{u.displayName}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('seriesRule.branch')}</span>
          <select className="cell-input" value={branchId ?? ''} onChange={(e) => setBranchId(number(e.target.value))}>
            <option value="">{t('seriesRule.any')}</option>
            {tree.branches.filter((b) => b.companyId === companyId).map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('seriesRule.location')}</span>
          <select className="cell-input" value={locationId ?? ''} onChange={(e) => setLocationId(number(e.target.value))}>
            <option value="">{t('seriesRule.any')}</option>
            {tree.locations.filter((l) => l.companyId === companyId).map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('seriesRule.partner')}</span>
          <select className="cell-input" value={scope ?? ''} onChange={(e) => setScope(number(e.target.value))}>
            <option value="">{t('seriesRule.any')}</option>
            {[1, 2, 3].map((s) => <option key={s} value={s}>{t(`seriesRule.scope.${s}` as TextKey)}</option>)}
          </select>
        </label>
        <label className="check">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          {t('field.isActive')}
        </label>
      </div>
      <div className="form-footer">
        <span />
        <div className="actions">
          <button type="button" className="btn ghost" onClick={onDone}>{t('common.cancel')}</button>
          <button type="submit" className="btn primary" disabled={busy || prefix === ''}>{t('common.save')}</button>
        </div>
      </div>
    </form>
  );
}
