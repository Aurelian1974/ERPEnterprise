import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { formatDate } from '../format/date';
import { t, type TextKey } from '../i18n';
import {
  blockApi, blockKeys, ITEM_BLOCK_FLAGS, maskOf, parseLimit, PARTNER_BLOCK_FLAGS, useItemBlock, usePartnerBlock, type ItemBlock, type PartnerBlock,
} from './blockApi';
import { partnerKeys } from './partnerApi';

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

const flagLabel = (kind: 'partner' | 'item', flag: number) => t(`block.${kind}.${flag}` as TextKey);

/**
 * Tab-ul „Blocare și credit” al partenerului (02.01, 3.6 punctul 6): blocarea partiala (vanzare, achizitie, plati) cu motiv,
 * comuna tuturor firmelor, si limita de credit a clientului pe firma curenta, cu avertizare sau blocare la depasire.
 */
export function PartnerBlockTab({ partnerId, active, canManage, canCredit, isCustomer }: {
  readonly partnerId: number;
  readonly active: boolean;
  readonly canManage: boolean;
  readonly canCredit: boolean;
  readonly isCustomer: boolean;
}) {
  const query = usePartnerBlock(partnerId, active);
  if (query.data === undefined) {
    return query.isError ? <ErrorBanner message={problem(query.error)} /> : <p className="muted" role="status">{t('app.loading')}</p>;
  }
  return (
    <div className="tab-body block-tab">
      <BlockForm key={`b-${query.data.blockRowVersion ?? 'nou'}`} kind="partner" id={partnerId} block={query.data} rowVersion={query.data.blockRowVersion}
        canManage={canManage} />
      {isCustomer && <CreditForm key={`c-${query.data.creditRowVersion ?? 'nou'}`} partnerId={partnerId} block={query.data} canCredit={canCredit} />}
    </div>
  );
}

/** Tab-ul „Blocare” al articolului: vanzare, achizitie, cu motiv. */
export function ItemBlockTab({ itemId, active, canManage }: { readonly itemId: number; readonly active: boolean; readonly canManage: boolean }) {
  const query = useItemBlock(itemId, active);
  if (query.data === undefined) {
    return query.isError ? <ErrorBanner message={problem(query.error)} /> : <p className="muted" role="status">{t('app.loading')}</p>;
  }
  return (
    <div className="tab-body block-tab">
      <BlockForm key={query.data.rowVersion ?? 'nou'} kind="item" id={itemId} block={query.data} rowVersion={query.data.rowVersion} canManage={canManage} />
    </div>
  );
}

function BlockForm({ kind, id, block, rowVersion, canManage }: {
  readonly kind: 'partner' | 'item';
  readonly id: number;
  readonly block: PartnerBlock | ItemBlock;
  readonly rowVersion: string | null | undefined;
  readonly canManage: boolean;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const flags = kind === 'partner' ? PARTNER_BLOCK_FLAGS : ITEM_BLOCK_FLAGS;
  const [mask, setMask] = useState(block.blockedFor);
  const [reason, setReason] = useState(block.reason ?? '');
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const dirty = mask !== block.blockedFor || (mask > 0 && reason !== (block.reason ?? ''));
  const guard = useUnsavedChanges(dirty);
  const all = maskOf(flags);

  const save = async () => {
    setError(undefined);
    setBusy(true);
    try {
      const value = mask === 0 ? null : reason.trim();
      if (kind === 'partner') {
        await blockApi.setPartner(id, mask, value, rowVersion ?? null);
      } else {
        await blockApi.setItem(id, mask, value, rowVersion ?? null);
      }
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: kind === 'partner' ? blockKeys.partner(id) : blockKeys.item(id) });
      if (kind === 'partner') {
        await queryClient.invalidateQueries({ queryKey: partnerKeys.all });
      }
      notify(mask === 0 ? t('block.unblocked') : t('block.saved'));
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card block-card" aria-labelledby={`${kind}-block-title`}>
      <div className="role-editor-head">
        <h3 id={`${kind}-block-title`}>{t('block.title')}</h3>
        <span className={block.blockedFor > 0 ? 'pill bad' : 'pill ok'}>{block.blockedFor > 0 ? t('block.state.blocked') : t('block.state.free')}</span>
      </div>
      <ErrorBanner message={error} />
      <fieldset className="block-flags" disabled={!canManage || busy}>
        <legend className="label">{t(`block.${kind}.legend` as TextKey)}</legend>
        {flags.map((flag) => (
          <label key={flag} className="check">
            <input type="checkbox" checked={(mask & flag) !== 0} onChange={(e) => setMask(e.target.checked ? mask | flag : mask & ~flag)} />
            {flagLabel(kind, flag)}
          </label>
        ))}
        <label className="check">
          <input type="checkbox" checked={mask === all} onChange={(e) => setMask(e.target.checked ? all : 0)} />
          <strong>{t('block.all')}</strong>
        </label>
      </fieldset>
      {mask > 0 && (
        <label className="field">
          <span className="label">{t('block.reason')} *</span>
          <textarea className="cell-input" rows={2} maxLength={300} value={reason} disabled={!canManage || busy} onChange={(e) => setReason(e.target.value)} />
        </label>
      )}
      {block.blockedFor > 0 && block.blockedAt != null && (
        <p className="muted small">{t('block.since', { date: formatDate(new Date(block.blockedAt)), user: block.blockedByName ?? '—' })}</p>
      )}
      <p className="banner dashed small">{t(`block.${kind}.hint` as TextKey)}</p>
      {canManage && (
        <div className="form-footer">
          <span />
          <button type="button" className="btn primary" disabled={busy || !dirty || (mask > 0 && reason.trim() === '')} onClick={() => void save()}>
            {t('common.save')}
          </button>
        </div>
      )}
    </section>
  );
}

function CreditForm({ partnerId, block, canCredit }: { readonly partnerId: number; readonly block: PartnerBlock; readonly canCredit: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const initial = block.creditLimit === null ? '' : Number(block.creditLimit).toLocaleString('ro-RO', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const [limit, setLimit] = useState(initial);
  const [action, setAction] = useState(block.overLimitAction);
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const parsed = parseLimit(limit);
  const dirty = limit !== initial || action !== block.overLimitAction;
  const guard = useUnsavedChanges(dirty);

  const save = async () => {
    if (parsed === undefined) {
      setError(t('credit.invalid'));
      return;
    }
    setError(undefined);
    setBusy(true);
    try {
      await blockApi.setCredit(partnerId, parsed, action, block.creditRowVersion ?? null);
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: blockKeys.partner(partnerId) });
      notify(t('common.saved'));
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card block-card" aria-labelledby="credit-title">
      <div className="role-editor-head">
        <h3 id="credit-title">{t('credit.title')}</h3>
        <span className="muted">{t('credit.perCompany')}</span>
      </div>
      <ErrorBanner message={error} />
      <div className="grid g2">
        <label className="field">
          <span className="label">{t('credit.limit')}</span>
          <input className="cell-input num" inputMode="decimal" value={limit} placeholder={t('credit.none')} disabled={!canCredit || busy}
            onChange={(e) => setLimit(e.target.value)} />
        </label>
        <label className="field">
          <span className="label">{t('credit.action')}</span>
          <select className="cell-input" value={action} disabled={!canCredit || busy} onChange={(e) => setAction(Number(e.target.value))}>
            <option value={1}>{t('credit.action.1')}</option>
            <option value={2}>{t('credit.action.2')}</option>
          </select>
        </label>
      </div>
      <p className="banner dashed small">{t('credit.hint')}</p>
      {canCredit && (
        <div className="form-footer">
          <span />
          <button type="button" className="btn primary" disabled={busy || !dirty} onClick={() => void save()}>{t('common.save')}</button>
        </div>
      )}
    </section>
  );
}
