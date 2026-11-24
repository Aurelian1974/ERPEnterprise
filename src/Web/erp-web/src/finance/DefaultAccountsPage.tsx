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

type Assignment = components['schemas']['RoleAssignmentDto'];

/** Rolurile conturilor, in ordinea din ecran (C1 3.2, C2 3.3). */
export const accountRoles = ['CustomerReceivable', 'SupplierPayable', 'CustomerAdvance', 'SupplierAdvance', 'SupplierInvoicesToReceive', 'SalesRevenue',
  'PurchaseExpense', 'CostOfGoodsSold', 'VatCollected', 'VatDeductible', 'VatPending', 'VatPayable', 'VatReceivable', 'FxGain', 'FxLoss', 'IndexGain',
  'IndexLoss', 'CashInTransit', 'RoundingGain', 'RoundingLoss', 'ProfitOrLoss', 'RetainedEarnings', 'ProfitAppropriation'] as const;

interface Row {
  readonly key: number;
  readonly role: string;
  readonly accountCode: string;
  readonly partnerKind: number | undefined;
  readonly currencyCode: string;
}

let rowKey = 0;

/** Atribuirile in vigoare ale unui rol: ale firmei, daca are, altfel cele ale instalarii (C1 3.2). */
export function effective(assignments: readonly Assignment[], role: string): Assignment[] {
  const own = assignments.filter((a) => a.role === role && a.companyId != null);
  return own.length > 0 ? own : assignments.filter((a) => a.role === role && a.companyId == null);
}

/**
 * Contabilitate › Conturi implicite (02.02, C1 3.2, C2 3.3; macheta c2-05): rolurile conturilor in vigoare pe firma, cu conditiile pe tipul
 * partenerului si pe moneda, rolurile neatribuite evidentiate si atribuirile proprii ale firmei, salvate in bloc. Conturile pe grupe de
 * articole si pe gestiuni vin cu modulele Articole si Stocuri.
 */
export function DefaultAccountsPage() {
  const user = useOutletContext<CurrentUser>();
  const canManage = user.permissions.includes('Fin.Account.Manage');
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const roles = useQuery({ queryKey: ['finance', 'gl-account-roles'], queryFn: async () => unwrap(await api.GET('/api/finance/gl-account-roles')) });
  const accounts = useQuery({ queryKey: ['finance', 'gl-accounts'], queryFn: async () => unwrap(await api.GET('/api/finance/gl-accounts')) });
  const postable = useMemo(() => new Map((accounts.data ?? []).filter((a) => a.isPostable && a.isActive).map((a) => [a.code, a])), [accounts.data]);
  const [rows, setRows] = useState<Row[] | undefined>();
  const [error, setError] = useState<string | undefined>();
  const data = roles.data ?? [];
  const own = rows ?? data.filter((a) => a.companyId != null).map((a) => ({ key: ++rowKey, role: a.role, accountCode: a.accountCode,
    partnerKind: a.partnerKind ?? undefined, currencyCode: a.currencyCode ?? '' }));
  const update = (key: number, change: Partial<Row>) => setRows(own.map((r) => r.key === key ? { ...r, ...change } : r));

  const save = async () => {
    setError(undefined);
    if (own.some((r) => !postable.has(r.accountCode.trim()))) {
      setError(t('manual.fixAccounts'));
      return;
    }
    try {
      unwrap(await api.PUT('/api/finance/gl-account-roles', { body: { roles: own.map((r) => ({ role: r.role, accountId: postable.get(r.accountCode.trim())?.id ?? 0,
        partnerKind: r.partnerKind ?? null, currencyCode: r.currencyCode.trim() === '' ? null : r.currencyCode.trim().toUpperCase(), validFrom: null, validTo: null })) } }));
      notify(t('roles.saved'));
      setRows(undefined);
      await queryClient.invalidateQueries({ queryKey: ['finance', 'gl-account-roles'] });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.defaultAccounts')}</div>
      <div className="head">
        <div><h1>{t('roles.title')}</h1><div className="sub2">{t('roles.subtitle')}</div></div>
        {canManage && rows !== undefined && <div className="actions"><button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button></div>}
      </div>
      <p className="muted small">{t('roles.chain')}</p>
      <ErrorBanner message={roles.isError ? roles.error.message : error} />
      <div className="split">
        <section className="card" aria-label={t('roles.effective')}>
          <h2>{t('roles.effective')}</h2>
          <table className="partner-list cash-list">
            <thead><tr><th scope="col">{t('roles.role')}</th><th scope="col">{t('roles.condition')}</th><th scope="col">{t('entry.account')}</th></tr></thead>
            <tbody>{accountRoles.map((role) => {
              const list = effective(data, role);
              return list.length === 0 ? (
                <tr key={role} className="row-bad"><td><span className="strong">{t(`acctRole.${role}` as TextKey)}</span><div className="muted small mono">{role}</div></td>
                  <td>{t('roles.unassigned')}</td><td className="error-text">{t('roles.unassigned')}</td></tr>
              ) : list.map((a, i) => (
                <tr key={a.id}>
                  <td>{i === 0 && <><span className="strong">{t(`acctRole.${role}` as TextKey)}</span><div className="muted small mono">{role}</div></>}</td>
                  <td className="small">{[a.partnerKind != null ? t(`roles.kind.${String(a.partnerKind)}` as TextKey) : t('roles.any'), a.currencyCode,
                    a.companyId == null ? t('roles.installDefault') : undefined, a.validTo != null ? `→ ${isoToRo(a.validTo)}` : undefined].filter((x) => x != null).join(' · ')}</td>
                  <td className="mono strong" title={a.accountName}>{a.accountCode}</td>
                </tr>
              ));
            })}</tbody>
          </table>
          <p className="muted small">{t('roles.partnerHint')}</p>
        </section>
        <section className="card" aria-label={t('roles.own')}>
          <h2>{t('roles.own')}</h2>
          <p className="muted small">{t('roles.ownHint')}</p>
          <datalist id="role-accounts">{[...postable.values()].map((a) => <option key={a.id} value={a.code}>{a.name}</option>)}</datalist>
          <table className="partner-list cash-list">
            <thead><tr><th scope="col">{t('roles.role')}</th><th scope="col">{t('roles.partnerKind')}</th><th scope="col">{t('roles.currency')}</th>
              <th scope="col">{t('entry.account')}</th><th scope="col" /></tr></thead>
            <tbody>{own.map((r) => (
              <tr key={r.key}>
                <td><select className="cell-input" aria-label={t('roles.role')} disabled={!canManage} value={r.role} onChange={(e) => update(r.key, { role: e.target.value })}>
                  {accountRoles.map((role) => <option key={role} value={role}>{t(`acctRole.${role}` as TextKey)}</option>)}</select></td>
                <td><select className="cell-input" aria-label={t('roles.partnerKind')} disabled={!canManage} value={r.partnerKind ?? ''}
                  onChange={(e) => update(r.key, { partnerKind: e.target.value === '' ? undefined : Number(e.target.value) })}>
                  <option value="">{t('roles.any')}</option>{[1, 2, 3, 4].map((k) => <option key={k} value={k}>{t(`roles.kind.${String(k)}` as TextKey)}</option>)}</select></td>
                <td><input className="cell-input mono" aria-label={t('roles.currency')} disabled={!canManage} maxLength={3} size={4} value={r.currencyCode}
                  onChange={(e) => update(r.key, { currencyCode: e.target.value })} /></td>
                <td><input className="cell-input mono" list="role-accounts" aria-label={t('entry.account')} disabled={!canManage} value={r.accountCode}
                  onChange={(e) => update(r.key, { accountCode: e.target.value })} />
                  <div className="muted small">{postable.get(r.accountCode.trim())?.name ?? (r.accountCode.trim() === '' ? '' : t('manual.unknownAccount'))}</div></td>
                <td>{canManage && <button type="button" className="btn small" aria-label={t('entry.delete')} onClick={() => setRows(own.filter((x) => x.key !== r.key))}>✕</button>}</td>
              </tr>))}</tbody>
          </table>
          {canManage && (
            <div className="actions">
              <button type="button" className="btn" onClick={() => setRows([...own, { key: ++rowKey, role: 'SupplierPayable', accountCode: '', partnerKind: undefined, currencyCode: '' }])}>
                {t('roles.add')}</button>
            </div>
          )}
        </section>
      </div>
    </div>
  );
}
