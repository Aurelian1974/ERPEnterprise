import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';

type Entity = 1 | 2;

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

/** Motivele perechii in romana („denumire, e-mail”). */
export function reasonsText(reasons: string): string {
  return reasons.split(',').filter((r) => r !== '').map((r) => t(`dup.reason.${r}` as TextKey)).join(', ');
}

/**
 * Configurare › Dubluri (02.01, 3.6 punctul 7): perechile posibile de parteneri sau articole dublate, cu motivul; „Unește” muta
 * datele inregistrarii renuntate pe cea pastrata si o dezactiveaza (ireversibil), „Nu sunt dubluri” ascunde perechea.
 */
export function DuplicatesTab({ user }: { readonly user: CurrentUser }) {
  const canPartners = user.permissions.includes('Core.Partner.Manage');
  const canItems = user.permissions.includes('Core.Item.Manage');
  const [entity, setEntity] = useState<Entity>(canPartners ? 1 : 2);
  const [confirm, setConfirm] = useState<{ keep: number; remove: number; keepName: string; removeName: string } | undefined>();
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const query = useQuery({
    queryKey: ['core', 'duplicates', entity],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/duplicates/{entityType}', { params: { path: { entityType: entity } }, signal })),
  });

  const run = async (action: () => Promise<unknown>, message: string) => {
    setError(undefined);
    setBusy(true);
    try {
      await action();
      setConfirm(undefined);
      await queryClient.invalidateQueries({ queryKey: ['core', 'duplicates', entity] });
      await queryClient.invalidateQueries({ queryKey: entity === 1 ? ['core', 'partners'] : ['core', 'items'] });
      notify(message);
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };
  const merge = (keep: number, remove: number) => run(async () => {
    if (entity === 1) {
      unwrap(await api.POST('/api/core/duplicates/1/merge', { body: { keepId: keep, removeId: remove } }));
    } else {
      unwrap(await api.POST('/api/core/duplicates/2/merge', { body: { keepId: keep, removeId: remove } }));
    }
  }, t('dup.merged'));
  const ignore = (first: number, second: number) => run(async () => {
    if (entity === 1) {
      unwrap(await api.POST('/api/core/duplicates/1/ignore', { body: { firstId: first, secondId: second } }));
    } else {
      unwrap(await api.POST('/api/core/duplicates/2/ignore', { body: { firstId: first, secondId: second } }));
    }
  }, t('dup.ignored'));

  return (
    <section className="card duplicates" aria-labelledby="dup-title">
      <div className="card-head">
        <h2 id="dup-title">{t('dup.title')}</h2>
        <div className="chips-row">
          {canPartners && <button type="button" className={entity === 1 ? 'chip on' : 'chip'} aria-pressed={entity === 1} onClick={() => { setEntity(1); setConfirm(undefined); }}>{t('dup.partners')}</button>}
          {canItems && <button type="button" className={entity === 2 ? 'chip on' : 'chip'} aria-pressed={entity === 2} onClick={() => { setEntity(2); setConfirm(undefined); }}>{t('dup.items')}</button>}
        </div>
      </div>
      <p className="muted small">{t(entity === 1 ? 'dup.hint.partners' : 'dup.hint.items')}</p>
      <ErrorBanner message={error ?? (query.isError ? problem(query.error) : undefined)} />
      {confirm !== undefined && (
        <div className="banner warn" role="alertdialog" aria-label={t('dup.confirmTitle')}>
          <p>{t('dup.confirm', { remove: confirm.removeName, keep: confirm.keepName })}</p>
          <div className="actions">
            <button type="button" className="btn ghost" onClick={() => setConfirm(undefined)}>{t('common.cancel')}</button>
            <button type="button" className="btn primary" disabled={busy} onClick={() => void merge(confirm.keep, confirm.remove)}>{t('dup.mergeNow')}</button>
          </div>
        </div>
      )}
      {query.data !== undefined && query.data.length === 0 && <p className="muted">{t('dup.none')}</p>}
      {query.data !== undefined && query.data.length > 0 && (
        <table className="simple">
          <thead>
            <tr>
              <th scope="col">{t('dup.first')}</th>
              <th scope="col">{t('dup.second')}</th>
              <th scope="col">{t('dup.reasons')}</th>
              <th scope="col">{t('dup.actions')}</th>
            </tr>
          </thead>
          <tbody>
            {query.data.map((p) => (
              <tr key={`${p.firstId}-${p.secondId}`}>
                <td><strong>{p.firstName}</strong><div className="muted small">{p.firstCode}{p.firstFiscalCode == null ? '' : ` · ${p.firstFiscalCode}`}</div></td>
                <td><strong>{p.secondName}</strong><div className="muted small">{p.secondCode}{p.secondFiscalCode == null ? '' : ` · ${p.secondFiscalCode}`}</div></td>
                <td>{reasonsText(p.reasons)}</td>
                <td className="dup-actions">
                  <button type="button" className="btn ghost small" disabled={busy}
                    onClick={() => setConfirm({ keep: p.firstId, remove: p.secondId, keepName: p.firstName, removeName: p.secondName })}>{t('dup.keepFirst')}</button>
                  <button type="button" className="btn ghost small" disabled={busy}
                    onClick={() => setConfirm({ keep: p.secondId, remove: p.firstId, keepName: p.secondName, removeName: p.firstName })}>{t('dup.keepSecond')}</button>
                  <button type="button" className="btn ghost small" disabled={busy} onClick={() => void ignore(p.firstId, p.secondId)}>{t('dup.notDuplicate')}</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
