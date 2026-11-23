import { useState } from 'react';
import { useOutletContext } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { formatAmount } from './cashApi';
import { parseAmount } from './TreasurySettingsTab';

type Rule = components['schemas']['MatchingRuleDto'];
type SaveRule = components['schemas']['SaveMatchingRule'];
type Lookups = components['schemas']['BankLookupsDto'];
type RuleTest = components['schemas']['MatchingRuleTestDto'];
type RuleTestLine = components['schemas']['MatchingRuleTestLineDto'];
type RuleFilter = 'active' | 'inactive' | 'remembered';

const rulesKey = ['finance', 'bank', 'rules'] as const;

/** Ce s-ar intampla cu o linie la „Testeaza pe 30 de zile”: prinsa de alta regula inainte, deja explicata altfel sau explicata de regula. */
export function testLineStatus(line: Pick<RuleTestLine, 'matchStatus' | 'explainedAs' | 'explainedByRuleName' | 'takenByRuleName'>, ruleName: string | undefined): string {
  if (line.takenByRuleName != null) return t('rules.test.taken', { name: line.takenByRuleName });
  const explained = line.matchStatus === 3 || line.matchStatus === 4;
  if (explained && (line.explainedByRuleName == null || line.explainedByRuleName !== ruleName)) {
    return t(line.matchStatus === 4 ? 'rules.test.validated' : 'rules.test.explained', { type: line.explainedAs ?? '—' });
  }
  return t('rules.test.would');
}

/** Textul condiților unei reguli, pe scurt (macheta f2-04, coloana Regulă · condiții). */
export function conditionText(rule: Pick<Rule, 'direction' | 'counterpartyIban' | 'counterpartyFiscalCode' | 'remittanceContains' | 'bankTransactionCode'
  | 'amountFrom' | 'amountTo'>, accountCode: string | undefined): string {
  const parts: string[] = [];
  if (accountCode !== undefined) parts.push(accountCode);
  if (rule.direction === 1) parts.push(t('rules.in'));
  if (rule.direction === 2) parts.push(t('rules.out'));
  if (rule.counterpartyIban != null) parts.push(`IBAN …${rule.counterpartyIban.slice(-4)}`);
  if (rule.counterpartyFiscalCode != null) parts.push(t('rules.fiscalCode', { code: rule.counterpartyFiscalCode }));
  if (rule.remittanceContains != null) parts.push(`„${rule.remittanceContains}”`);
  if (rule.bankTransactionCode != null) parts.push(t('rules.code', { code: rule.bankTransactionCode }));
  if (rule.amountFrom != null || rule.amountTo != null) parts.push(`${formatAmount(rule.amountFrom ?? '0')} – ${rule.amountTo == null ? '∞' : formatAmount(rule.amountTo)}`);
  return parts.join(' · ');
}

/** Noua ordine a Id-urilor dupa mutarea regulii `moved` inaintea regulii `before` (sau la sfarsit, daca `before` lipseste). */
export function moveRule(ids: readonly number[], moved: number, before: number | undefined): number[] {
  const rest = ids.filter((id) => id !== moved);
  const index = before === undefined ? rest.length : rest.indexOf(before);
  return [...rest.slice(0, index < 0 ? rest.length : index), moved, ...rest.slice(index < 0 ? rest.length : index)];
}

/**
 * Bancă › Reguli de potrivire (02.02, F2 3.5; macheta f2-04): regulile firmei în ordinea priorității, cu condițiile, acțiunea, validarea
 * automată și numărul de linii explicate; fișa regulii cu condițiile (toate trebuie îndeplinite) și acțiunea. Validarea automată e permisă
 * doar pentru tipurile fără partener.
 */
export function MatchingRulesPage() {
  const user = useOutletContext<CurrentUser>();
  const canManage = user.permissions.includes('Fin.BankRule.Manage');
  const rules = useQuery({ queryKey: rulesKey, queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/matching-rules', { signal })) });
  const lookups = useQuery({ queryKey: ['finance', 'bank', 'lookups'], queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/bank-lookups', { signal })) });
  const [filter, setFilter] = useState<RuleFilter>('active');
  const [selected, setSelected] = useState<number | 'new' | undefined>();
  const all = rules.data ?? [];
  const visible = all.filter((r) => (filter === 'inactive' ? !r.isActive : filter === 'remembered' ? r.createdFromLineId != null : r.isActive));
  const current = selected === 'new' ? undefined : all.find((r) => r.id === (selected ?? visible[0]?.id));
  const accountCode = (id: number | null | undefined) => lookups.data?.accounts.find((a) => a.id === id)?.code;
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [dragged, setDragged] = useState<number | undefined>();
  const canReorder = canManage && filter === 'active';
  const reorder = async (order: number[]) => {
    try {
      unwrap(await api.POST('/api/finance/matching-rules/reorder', { body: { ruleIds: order } }));
      await queryClient.invalidateQueries({ queryKey: rulesKey });
    } catch (e) {
      notify(e instanceof ApiError ? e.message : t('common.notSaved'), 'error');
    }
  };
  const ids = visible.map((r) => r.id);
  const step = (id: number, delta: number) => {
    const index = ids.indexOf(id);
    const target = index + delta;
    if (target < 0 || target >= ids.length) return;
    void reorder(moveRule(ids, id, delta < 0 ? ids[target] : ids[target + 1]));
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.bank')} › {t('menu.matchingRules')}</div>
      <div className="head">
        <div>
          <h1>{t('rules.title')}</h1>
          <div className="sub2">{t('rules.subtitle')}</div>
        </div>
        <div className="actions">
          {canManage && <button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('rules.new')}</button>}
        </div>
      </div>
      <div className="split cash-document bank-split">
        <section className="card" aria-label={t('rules.title')}>
          <div className="chips-row" role="group" aria-label={t('rules.filter')}>
            {(['active', 'inactive', 'remembered'] as const).map((f) => (
              <button key={f} type="button" className={filter === f ? 'chip on' : 'chip'} aria-pressed={filter === f} onClick={() => setFilter(f)}>
                {t(`rules.filter.${f}`)}</button>
            ))}
          </div>
          {rules.isError && <ErrorBanner message={rules.error.message} />}
          {rules.data !== undefined && visible.length === 0 ? <p className="muted">{t('rules.empty')}</p> : (
            <table className="partner-list cash-list" aria-busy={rules.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('rules.col.priority')}</th>
                  <th scope="col">{t('rules.col.rule')}</th>
                  <th scope="col">{t('rules.col.action')}</th>
                  <th scope="col" className="num">{t('rules.col.usage')}</th>
                </tr>
              </thead>
              <tbody>
                {visible.map((r) => (
                  <tr key={r.id} className={[r.id === current?.id ? 'selected' : '', dragged === r.id ? 'dragging' : ''].join(' ').trim() || undefined}
                    onClick={() => setSelected(r.id)} aria-selected={r.id === current?.id}
                    draggable={canReorder} onDragStart={() => setDragged(r.id)} onDragEnd={() => setDragged(undefined)}
                    onDragOver={(e) => { if (canReorder && dragged !== undefined) e.preventDefault(); }}
                    onDrop={(e) => { e.preventDefault(); if (dragged !== undefined && dragged !== r.id) void reorder(moveRule(ids, dragged, r.id)); setDragged(undefined); }}>
                    <td className="num rule-priority">
                      {canReorder && <span className="drag-handle" aria-hidden="true" title={t('rules.drag')}>⋮⋮</span>}
                      {r.priority}
                      {canReorder && (
                        <span className="rule-steps">
                          <button type="button" className="btn ghost small" aria-label={t('rules.up', { name: r.name })} disabled={ids.indexOf(r.id) === 0}
                            onClick={(e) => { e.stopPropagation(); step(r.id, -1); }}>↑</button>
                          <button type="button" className="btn ghost small" aria-label={t('rules.down', { name: r.name })} disabled={ids.indexOf(r.id) === ids.length - 1}
                            onClick={(e) => { e.stopPropagation(); step(r.id, 1); }}>↓</button>
                        </span>
                      )}
                    </td>
                    <td><strong>{r.name}</strong><div className="muted small">{conditionText(r, accountCode(r.treasuryAccountId))}</div></td>
                    <td className="small">{r.operationTypeCode}{r.partnerName == null ? '' : ` · ${r.partnerName}`}
                      <div><span className={r.autoValidate ? 'pill ok' : 'pill'}>{t(r.autoValidate ? 'rules.autoValidate' : 'rules.explainOnly')}</span></div></td>
                    <td className="num">{r.usageCount ?? 0}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <div className="banner dashed">{t('rules.hint')}</div>
        </section>
        <aside className="card cash-side">
          {lookups.data === undefined ? <p className="muted" role="status">{t('app.loading')}</p>
            : selected === 'new' || current !== undefined
              ? <RuleForm key={current?.id ?? 'new'} rule={current} lookups={lookups.data} canManage={canManage} onSaved={(id) => setSelected(id)} />
              : <p className="muted">{t('rules.pick')}</p>}
        </aside>
      </div>
    </div>
  );
}

function RuleForm({ rule, lookups, canManage, onSaved }: { readonly rule: Rule | undefined; readonly lookups: Lookups; readonly canManage: boolean;
  readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [draft, setDraft] = useState<SaveRule>(() => ({
    id: rule?.id ?? null, priority: rule?.priority ?? 100, name: rule?.name ?? '', treasuryAccountId: rule?.treasuryAccountId ?? null,
    direction: rule?.direction ?? null, counterpartyIban: rule?.counterpartyIban ?? null, counterpartyFiscalCode: rule?.counterpartyFiscalCode ?? null,
    remittanceContains: rule?.remittanceContains ?? null, bankTransactionCode: rule?.bankTransactionCode ?? null,
    amountFrom: rule?.amountFrom?.replace('.', ',') ?? null, amountTo: rule?.amountTo?.replace('.', ',') ?? null,
    operationTypeId: rule?.operationTypeId ?? lookups.operationTypes[0]?.id ?? 0, partnerId: rule?.partnerId ?? null, costCenterId: rule?.costCenterId ?? null,
    projectId: rule?.projectId ?? null, noteTemplate: rule?.noteTemplate ?? null, autoValidate: rule?.autoValidate ?? false, isActive: rule?.isActive ?? true,
    rowVersion: rule?.rowVersion ?? null,
  }));
  const [error, setError] = useState<string | undefined>();
  const type = lookups.operationTypes.find((ty) => ty.id === draft.operationTypeId);
  const text = (value: string) => (value.trim() === '' ? null : value);
  const set = (patch: Partial<SaveRule>) => setDraft({ ...draft, ...patch });

  const save = async (patch: Partial<SaveRule> = {}) => {
    setError(undefined);
    const body = { ...draft, ...patch };
    const from = body.amountFrom == null ? null : parseAmount(body.amountFrom);
    const to = body.amountTo == null ? null : parseAmount(body.amountTo);
    if ((body.amountFrom != null && from === null) || (body.amountTo != null && to === null)) {
      setError(t('rules.invalidAmount'));
      return;
    }
    try {
      const saved = body.id === null
        ? unwrap(await api.POST('/api/finance/matching-rules', { body: { ...body, amountFrom: from, amountTo: to } }))
        : unwrap(await api.PUT('/api/finance/matching-rules/{id}', { params: { path: { id: body.id } }, body: { ...body, amountFrom: from, amountTo: to } }));
      notify(t('common.saved'));
      await queryClient.invalidateQueries({ queryKey: rulesKey });
      onSaved(saved.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  const [testResult, setTestResult] = useState<RuleTest | undefined>();
  const [testing, setTesting] = useState(false);
  const runTest = async () => {
    setError(undefined);
    const from = draft.amountFrom == null ? null : parseAmount(draft.amountFrom);
    const to = draft.amountTo == null ? null : parseAmount(draft.amountTo);
    if ((draft.amountFrom != null && from === null) || (draft.amountTo != null && to === null)) {
      setError(t('rules.invalidAmount'));
      return;
    }
    setTesting(true);
    try {
      setTestResult(unwrap(await api.POST('/api/finance/matching-rules/test', { body: {
        id: draft.id, priority: draft.priority, treasuryAccountId: draft.treasuryAccountId, direction: draft.direction, counterpartyIban: draft.counterpartyIban,
        counterpartyFiscalCode: draft.counterpartyFiscalCode, remittanceContains: draft.remittanceContains, bankTransactionCode: draft.bankTransactionCode,
        amountFrom: from, amountTo: to, operationTypeId: draft.operationTypeId, partnerId: draft.partnerId, noteTemplate: draft.noteTemplate,
      } })));
    } catch (e) {
      setTestResult(undefined);
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    } finally {
      setTesting(false);
    }
  };

  return (
    <div className="bank-line settings-form">
      <h2>{rule?.name ?? t('rules.newTitle')} {rule !== undefined && <span className={rule.isActive ? 'pill ok' : 'pill'}>{t(rule.isActive ? 'rules.active' : 'rules.inactive')}</span>}</h2>
      {rule?.createdFromLineId != null && <p className="muted small">{t('rules.remembered')}</p>}
      <label>{t('rules.name')}<input className="cell-input" value={draft.name} disabled={!canManage} onChange={(e) => set({ name: e.target.value })} /></label>
      <label>{t('rules.col.priority')}<input className="cell-input num" type="number" min={1} value={draft.priority} disabled={!canManage}
        onChange={(e) => set({ priority: Number(e.target.value) })} /></label>
      <h3 className="section-title">{t('rules.conditions')}</h3>
      <label>{t('bank.account')}
        <select className="cell-input" value={draft.treasuryAccountId ?? ''} disabled={!canManage} onChange={(e) => set({ treasuryAccountId: e.target.value === '' ? null : Number(e.target.value) })}>
          <option value="">{t('rules.anyAccount')}</option>
          {lookups.accounts.map((a) => <option key={a.id} value={a.id}>{a.code}</option>)}
        </select>
      </label>
      <label>{t('rules.direction')}
        <select className="cell-input" value={draft.direction ?? ''} disabled={!canManage} onChange={(e) => set({ direction: e.target.value === '' ? null : Number(e.target.value) })}>
          <option value="">{t('rules.anyDirection')}</option>
          <option value={1}>{t('rules.in')}</option>
          <option value={2}>{t('rules.out')}</option>
        </select>
      </label>
      <label>{t('rules.iban')}<input className="cell-input mono" value={draft.counterpartyIban ?? ''} disabled={!canManage} onChange={(e) => set({ counterpartyIban: text(e.target.value) })} /></label>
      <label>{t('rules.fiscalCodeLabel')}<input className="cell-input" value={draft.counterpartyFiscalCode ?? ''} disabled={!canManage}
        onChange={(e) => set({ counterpartyFiscalCode: text(e.target.value) })} /></label>
      <label>{t('rules.contains')}<input className="cell-input" value={draft.remittanceContains ?? ''} disabled={!canManage}
        onChange={(e) => set({ remittanceContains: text(e.target.value) })} /><span className="muted small">{t('rules.containsHint')}</span></label>
      <label>{t('rules.bankCode')}<input className="cell-input" value={draft.bankTransactionCode ?? ''} disabled={!canManage}
        onChange={(e) => set({ bankTransactionCode: text(e.target.value) })} /></label>
      <div className="row-between">
        <label>{t('rules.amountFrom')}<input className="cell-input num" value={draft.amountFrom ?? ''} disabled={!canManage} onChange={(e) => set({ amountFrom: text(e.target.value) })} /></label>
        <label>{t('rules.amountTo')}<input className="cell-input num" value={draft.amountTo ?? ''} disabled={!canManage} onChange={(e) => set({ amountTo: text(e.target.value) })} /></label>
      </div>
      <h3 className="section-title">{t('rules.action')}</h3>
      <label>{t('bank.col.type')}
        <select className="cell-input" value={draft.operationTypeId} disabled={!canManage}
          onChange={(e) => set({ operationTypeId: Number(e.target.value), autoValidate: false })}>
          {lookups.operationTypes.map((ty) => <option key={ty.id} value={ty.id}>{`${ty.code} · ${ty.name}`}</option>)}
        </select>
      </label>
      <label>{t('rules.note')}<input className="cell-input" value={draft.noteTemplate ?? ''} disabled={!canManage} onChange={(e) => set({ noteTemplate: text(e.target.value) })} /></label>
      <label className="check"><input type="checkbox" checked={draft.autoValidate} disabled={!canManage || type?.requiresPartner === true}
        onChange={(e) => set({ autoValidate: e.target.checked })} />{t('rules.autoValidateLabel')}</label>
      <div className="banner warn">{t('rules.autoValidateHint')}</div>
      <ErrorBanner message={error} />
      {testResult !== undefined && <RuleTestResult result={testResult} ruleName={rule?.name} />}
      <div className="actions">
        <button type="button" className="btn ghost" disabled={testing} onClick={() => void runTest()}>{t('rules.test.run')}</button>
        {canManage && rule !== undefined && <button type="button" className="btn ghost" onClick={() => void save({ isActive: !draft.isActive })}>
          {t(draft.isActive ? 'rules.deactivate' : 'rules.activate')}</button>}
        {canManage && <button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button>}
      </div>
    </div>
  );
}

/** Rezultatul „Testeaza pe 30 de zile” (macheta f2-04): nimic nu se modifica; rezumatul, totalurile pe moneda si primele 200 de linii. */
function RuleTestResult({ result, ruleName }: { readonly result: RuleTest; readonly ruleName: string | undefined }) {
  const totals = result.totals.map((x) => [x.totalIn !== '0.00' && `+${formatAmount(x.totalIn)}`, x.totalOut !== '0.00' && `−${formatAmount(x.totalOut)}`]
    .filter(Boolean).join(' / ') + ` ${x.currencyCode}`).join('; ');
  return (
    <section className="rule-test" aria-label={t('rules.test.title')} role="status">
      <h3 className="section-title">{t('rules.test.title')}</h3>
      <p>{result.lines === 0 ? t('rules.test.none', { from: isoToRo(result.fromDate) })
        : t('rules.test.summary', { count: result.lines, from: isoToRo(result.fromDate), totals })}</p>
      {result.alreadyExplained > 0 && <p className="muted small">{t('rules.test.alreadyExplained', { count: result.alreadyExplained })}</p>}
      {result.takenByOtherRule > 0 && <div className="banner warn">{t('rules.test.takenByOther', { count: result.takenByOtherRule })}</div>}
      {result.items.length > 0 && (
        <table className="partner-list cash-list">
          <thead><tr><th>{t('bank.col.date')}</th><th>{t('bank.account')}</th><th>{t('rules.test.details')}</th><th className="num">{t('bank.col.amount')}</th>
            <th>{t('rules.test.outcome')}</th></tr></thead>
          <tbody>
            {result.items.map((l) => (
              <tr key={l.id}>
                <td>{isoToRo(l.bookingDate)}</td>
                <td>{l.accountCode}</td>
                <td><div>{l.counterpartyName ?? '—'}</div><div className="muted small">{l.remittanceInfo}</div></td>
                <td className={l.amount.startsWith('-') ? 'num neg' : 'num pos'}>{formatAmount(l.amount)}</td>
                <td className="small">{testLineStatus(l, ruleName)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {result.lines > result.items.length && <p className="muted small">{t('rules.test.more', { count: result.items.length })}</p>}
    </section>
  );
}
