import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';

type Profile = components['schemas']['BankFormatProfileDto'];
type SaveProfile = components['schemas']['SaveBankFormatProfile'];
type Acquirer = components['schemas']['CardAcquirerDto'];

const keys = {
  profiles: ['finance', 'bank', 'profiles'] as const,
  accounts: ['finance', 'bank', 'accounts'] as const,
  acquirers: ['finance', 'bank', 'acquirers'] as const,
};

const statementFormats = [[1, 'CAMT.053'], [2, 'MT940'], [3, 'CSV'], [4, 'cfg.bank.noImport']] as const;
const paymentFormats = [[1, 'pain.001.001.03'], [2, 'pain.001.001.09'], [3, 'CSV'], [4, 'cfg.bank.noExport']] as const;
const formatText = (list: typeof statementFormats | typeof paymentFormats, value: number) => {
  const found = list.find(([v]) => v === value)?.[1] ?? '';
  return found.startsWith('cfg.') ? t(found as Parameters<typeof t>[0]) : found;
};

function useRun() {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  return async (action: () => Promise<unknown>, key: readonly string[]): Promise<string | undefined> => {
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: key });
      notify(t('common.saved'));
      return undefined;
    } catch (e) {
      return e instanceof ApiError ? e.message : t('common.notSaved');
    }
  };
}

/**
 * Configurare › Finanțe › Bancă › Profile (02.02, F2 3.1; macheta cf-04): profilele de format bancar (livrate și CSV ale clientului, cu maparea
 * verificată la salvare) și profilul și folderul de import al fiecărui cont bancar al firmei.
 */
export function BankProfiles() {
  const profiles = useQuery({ queryKey: keys.profiles, queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/bank-format-profiles', { signal })) });
  const accounts = useQuery({ queryKey: keys.accounts, queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/bank-accounts', { signal })) });
  const [editing, setEditing] = useState<Profile | 'new' | undefined>();
  const run = useRun();
  const [error, setError] = useState<string | undefined>();
  if (profiles.data === undefined || accounts.data === undefined) {
    return <p role="status" className="muted">{t('app.loading')}</p>;
  }
  const assign = async (accountId: number, profileId: number | null, folder: string | null) =>
    setError(await run(async () => unwrap(await api.PUT('/api/finance/treasury-accounts/{id}/bank-format', {
      params: { path: { id: accountId } }, body: { bankFormatProfileId: profileId, importFolder: folder } })), keys.accounts));

  return (
    <div className="settings-section">
      <table className="partner-list">
        <thead>
          <tr>
            <th scope="col">{t('cfg.bank.code')}</th><th scope="col">{t('cfg.bank.name')}</th><th scope="col">{t('cfg.bank.statement')}</th>
            <th scope="col">{t('cfg.bank.payments')}</th><th scope="col" className="num">{t('cfg.bank.accounts')}</th><th scope="col"><span className="sr-only">{t('common.save')}</span></th>
          </tr>
        </thead>
        <tbody>
          {profiles.data.map((p) => (
            <tr key={p.id}>
              <td><strong>{p.code}</strong>{p.isSystem && <div className="muted small">{t('cfg.bank.shipped')}</div>}</td>
              <td>{p.name}{p.bankBic == null ? '' : <div className="muted small mono">{p.bankBic}</div>}{!p.isActive && <span className="pill">{t('rules.inactive')}</span>}</td>
              <td>{formatText(statementFormats, p.statementFormat)}</td>
              <td>{formatText(paymentFormats, p.paymentFormat)}</td>
              <td className="num">{p.accountCount}</td>
              <td><button type="button" className="btn ghost small" onClick={() => setEditing(p)}>{t('cfg.bank.edit')}</button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="actions"><button type="button" className="btn ghost" onClick={() => setEditing('new')}>{t('cfg.bank.newProfile')}</button></div>
      {editing !== undefined && <ProfileEditor key={editing === 'new' ? 'new' : editing.id} profile={editing === 'new' ? undefined : editing} onDone={() => setEditing(undefined)} />}

      <h3 className="section-title">{t('cfg.bank.accountProfiles')}</h3>
      <table className="partner-list">
        <thead><tr><th scope="col">{t('bank.account')}</th><th scope="col">{t('cfg.bank.profile')}</th><th scope="col">{t('cfg.bank.folder')}</th></tr></thead>
        <tbody>
          {accounts.data.filter((a) => a.kind !== 5).map((a) => (
            <tr key={a.id}>
              <td><strong>{a.code}</strong><div className="muted small">{a.name} · {a.currencyCode}</div></td>
              <td>
                <select className="cell-input" value={a.bankFormatProfileId ?? ''} aria-label={t('cfg.bank.profile')}
                  onChange={(e) => void assign(a.id, e.target.value === '' ? null : Number(e.target.value), a.importFolder ?? null)}>
                  <option value="">{t('cfg.bank.detect')}</option>
                  {profiles.data.filter((p) => p.isActive).map((p) => <option key={p.id} value={p.id}>{p.code}</option>)}
                </select>
              </td>
              <td><input className="cell-input mono" defaultValue={a.importFolder ?? ''} aria-label={t('cfg.bank.folder')} placeholder="\\srv-fs\banca\bcr"
                onBlur={(e) => { const v = e.target.value.trim() === '' ? null : e.target.value.trim(); if (v !== (a.importFolder ?? null)) void assign(a.id, a.bankFormatProfileId ?? null, v); }} /></td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="muted small">{t('cfg.bank.folderHint')}</p>
      <ErrorBanner message={error} />
    </div>
  );
}

const csvSample = '{"separator":";","encoding":"utf-8","headerRow":1,"dateFormat":"dd.MM.yyyy","decimalSeparator":",","columns":{"bookingDate":"Data","amount":"Suma","counterpartyName":"Partener","counterpartyIban":"IBAN","remittanceInfo":"Detalii","bankReference":"Referinta","balance":"Sold"}}';

function ProfileEditor({ profile, onDone }: { readonly profile: Profile | undefined; readonly onDone: () => void }) {
  const run = useRun();
  const [error, setError] = useState<string | undefined>();
  const [draft, setDraft] = useState<SaveProfile>(() => ({
    id: profile?.id ?? null, code: profile?.code ?? '', name: profile?.name ?? '', bankBic: profile?.bankBic ?? null,
    statementFormat: profile?.statementFormat ?? 3, csvMapping: profile?.csvMapping ?? csvSample, paymentFormat: profile?.paymentFormat ?? 1,
    paymentMapping: profile?.paymentMapping ?? null, maxRemittanceLength: profile?.maxRemittanceLength ?? 140, isActive: profile?.isActive ?? true,
    rowVersion: profile?.rowVersion ?? null,
  }));
  const system = profile?.isSystem === true;
  const set = (patch: Partial<SaveProfile>) => setDraft({ ...draft, ...patch });
  const save = async () => {
    const message = await run(async () => (draft.id === null
      ? unwrap(await api.POST('/api/finance/bank-format-profiles', { body: draft }))
      : unwrap(await api.PUT('/api/finance/bank-format-profiles/{id}', { params: { path: { id: draft.id } }, body: draft }))), keys.profiles);
    setError(message);
    if (message === undefined) onDone();
  };
  return (
    <div className="settings-form card">
      <h3>{profile === undefined ? t('cfg.bank.newProfile') : profile.code}</h3>
      <label>{t('cfg.bank.code')}<input className="cell-input" value={draft.code} disabled={system} onChange={(e) => set({ code: e.target.value })} /></label>
      <label>{t('cfg.bank.name')}<input className="cell-input" value={draft.name} onChange={(e) => set({ name: e.target.value })} /></label>
      <label>BIC<input className="cell-input mono" value={draft.bankBic ?? ''} onChange={(e) => set({ bankBic: e.target.value.trim() === '' ? null : e.target.value })} /></label>
      <label>{t('cfg.bank.statement')}
        <select className="cell-input" value={draft.statementFormat} disabled={system} onChange={(e) => set({ statementFormat: Number(e.target.value) })}>
          {statementFormats.map(([v]) => <option key={v} value={v}>{formatText(statementFormats, v)}</option>)}
        </select>
      </label>
      {draft.statementFormat === 3 && (
        <label>{t('cfg.bank.csvMapping')}<textarea className="cell-input mono" rows={5} value={draft.csvMapping ?? ''} onChange={(e) => set({ csvMapping: e.target.value })} />
          <span className="muted small">{t('cfg.bank.csvHint')}</span></label>
      )}
      <label>{t('cfg.bank.payments')}
        <select className="cell-input" value={draft.paymentFormat} disabled={system} onChange={(e) => set({ paymentFormat: Number(e.target.value) })}>
          {paymentFormats.map(([v]) => <option key={v} value={v}>{formatText(paymentFormats, v)}</option>)}
        </select>
      </label>
      <label>{t('cfg.bank.paymentOptions')}<input className="cell-input mono" value={draft.paymentMapping ?? ''} placeholder='{"batchBooking":true,"chargeBearer":"SLEV"}'
        onChange={(e) => set({ paymentMapping: e.target.value.trim() === '' ? null : e.target.value })} /></label>
      <label>{t('cfg.bank.maxRemittance')}<input className="cell-input num" type="number" min={35} max={500} value={draft.maxRemittanceLength}
        onChange={(e) => set({ maxRemittanceLength: Number(e.target.value) })} /></label>
      <label className="check"><input type="checkbox" checked={draft.isActive} onChange={(e) => set({ isActive: e.target.checked })} />{t('rules.active')}</label>
      <ErrorBanner message={error} />
      <div className="actions">
        <button type="button" className="btn ghost" onClick={onDone}>{t('order.cancel')}</button>
        <button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button>
      </div>
    </div>
  );
}

/** Configurare › Finanțe › Bancă › Procesatori de carduri (02.02, F2 3.7): codul folosit de magazine și IBAN-ul de pe care vin decontările. */
export function CardAcquirers() {
  const list = useQuery({ queryKey: keys.acquirers, queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/card-acquirers', { signal })) });
  const run = useRun();
  const [error, setError] = useState<string | undefined>();
  const [draft, setDraft] = useState<{ id: number | null; code: string; name: string; iban: string; isActive: boolean; rowVersion: string | null }>(
    { id: null, code: '', name: '', iban: '', isActive: true, rowVersion: null });
  if (list.data === undefined) {
    return <p role="status" className="muted">{t('app.loading')}</p>;
  }
  const edit = (a: Acquirer) => setDraft({ id: a.id, code: a.code, name: a.name, iban: a.counterpartyIban ?? '', isActive: a.isActive, rowVersion: a.rowVersion });
  const save = async () => {
    const body = { id: draft.id, code: draft.code, name: draft.name, counterpartyIban: draft.iban.trim() === '' ? null : draft.iban, isActive: draft.isActive,
      rowVersion: draft.rowVersion };
    const message = await run(async () => (draft.id === null
      ? unwrap(await api.POST('/api/finance/card-acquirers', { body }))
      : unwrap(await api.PUT('/api/finance/card-acquirers/{id}', { params: { path: { id: draft.id } }, body }))), keys.acquirers);
    setError(message);
    if (message === undefined) setDraft({ id: null, code: '', name: '', iban: '', isActive: true, rowVersion: null });
  };
  return (
    <div className="settings-section">
      <table className="partner-list">
        <thead><tr><th scope="col">{t('cfg.bank.code')}</th><th scope="col">{t('cfg.bank.name')}</th><th scope="col">IBAN</th><th scope="col"><span className="sr-only">{t('cfg.bank.edit')}</span></th></tr></thead>
        <tbody>
          {list.data.map((a) => (
            <tr key={a.id}>
              <td><strong>{a.code}</strong>{!a.isActive && <span className="pill">{t('rules.inactive')}</span>}</td><td>{a.name}</td>
              <td className="mono small">{a.counterpartyIban ?? '—'}</td>
              <td><button type="button" className="btn ghost small" onClick={() => edit(a)}>{t('cfg.bank.edit')}</button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="settings-form">
        <h3>{draft.id === null ? t('cfg.bank.newAcquirer') : draft.code}</h3>
        <label>{t('cfg.bank.code')}<input className="cell-input" value={draft.code} onChange={(e) => setDraft({ ...draft, code: e.target.value })} /></label>
        <label>{t('cfg.bank.name')}<input className="cell-input" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /></label>
        <label>{t('cfg.bank.acquirerIban')}<input className="cell-input mono" value={draft.iban} onChange={(e) => setDraft({ ...draft, iban: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />{t('rules.active')}</label>
        <ErrorBanner message={error} />
        <div className="actions"><button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button></div>
      </div>
    </div>
  );
}
