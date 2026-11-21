import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNotifier } from '../components/Notifier';
import { formatDate } from '../format/date';
import { t } from '../i18n';
import { HistoryView } from '../partners/PartnerTabs';
import { userApi, userKeys, useUserHistory, useUserSessions, type UserDetail } from './userApi';

const time = new Intl.DateTimeFormat('ro-RO', { timeZone: 'Europe/Bucharest', hour: '2-digit', minute: '2-digit' });

/** „15.11.2026 09:12”, sau „—” fara valoare. */
export function dateTimeText(value: string | null | undefined): string {
  if (value == null) {
    return t('common.none');
  }
  const date = new Date(value);
  return `${formatDate(date)} ${time.format(date)}`;
}

/** O actiune de securitate: confirmare, apel, reincarcarea fisei, a sesiunilor si a istoricului, notificare (UI-11). */
function useSecurityAction(userId: number) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [busy, setBusy] = useState(false);
  return {
    busy,
    async run(confirmText: string | undefined, action: () => Promise<void>, done: string) {
      if (confirmText !== undefined && !window.confirm(confirmText)) {
        return;
      }
      setBusy(true);
      try {
        await action();
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: userKeys.detail(userId) }),
          queryClient.invalidateQueries({ queryKey: userKeys.sessions(userId) }),
          queryClient.invalidateQueries({ queryKey: userKeys.history(userId) }),
          queryClient.invalidateQueries({ queryKey: [...userKeys.all, 'list'] }),
        ]);
        notify(done);
      } catch (e) {
        notify(e instanceof Error ? e.message : t('app.loadError'), 'error');
      } finally {
        setBusy(false);
      }
    },
  };
}

/** Tab-ul Securitate (06b): starea contului de autentificare si actiunile administratorului (Core.User.Manage). */
export function UserSecurityTab({ user, onPassword }: { readonly user: UserDetail; readonly onPassword: (password: string) => void }) {
  const action = useSecurityAction(user.id);
  const technical = user.externalAccessType === 'TECHNICAL';
  const facts: [string, string][] = [
    [t('user.security.account'), t(user.hasAccount ? (user.hasPassword ? 'user.security.local' : 'user.security.noPassword') : 'user.security.noAccount')],
    [t('user.security.passwordChanged'), user.passwordChangedAt == null ? t('common.none') : dateTimeText(user.passwordChangedAt)],
    [t('user.security.mustChange'), t(user.mustChangePassword ? 'common.yes' : 'common.no')],
    [t('user.field.twoFactor'), t(user.twoFactorEnabled ? 'user.twoFactor.on' : user.twoFactorRequired ? 'user.twoFactor.pending' : 'user.twoFactor.off')],
    [t('user.security.twoFactorRequired'), t(user.twoFactorRequired ? 'common.yes' : 'common.no')],
    [t('user.security.locked'), user.lockedUntil == null ? t('common.no') : t('user.security.lockedUntil', { date: dateTimeText(user.lockedUntil) })],
    [t('user.security.lastLogin'), dateTimeText(user.lastLoginAt)],
    [t('user.security.sessions'), String(user.activeSessions)],
  ];
  return (
    <div className="tab-body">
      <dl className="facts">
        {facts.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
      {technical && <div className="banner info">{t('user.security.technicalHint')}</div>}
      <div className="actions">
        {!technical && (
          <button type="button" className="btn ghost" disabled={action.busy}
            onClick={() => void action.run(t('user.security.resetPasswordConfirm', { name: user.displayName }), async () => {
              onPassword(await userApi.resetPassword(user.id));
            }, t('user.security.passwordReset'))}>
            {t(user.hasAccount ? 'user.security.resetPassword' : 'user.security.createAccount')}
          </button>
        )}
        {user.lockedUntil != null && (
          <button type="button" className="btn ghost" disabled={action.busy}
            onClick={() => void action.run(undefined, () => userApi.unlock(user.id), t('user.security.unlocked'))}>
            {t('user.security.unlock')}
          </button>
        )}
        {user.twoFactorEnabled && (
          <button type="button" className="btn ghost" disabled={action.busy}
            onClick={() => void action.run(t('user.security.resetTwoFactorConfirm', { name: user.displayName }), () => userApi.resetTwoFactor(user.id),
              t('user.security.twoFactorReset'))}>
            {t('user.security.resetTwoFactor')}
          </button>
        )}
      </div>
      <p className="muted small">{t('user.security.hint')}</p>
    </div>
  );
}

/** Tab-ul Sesiuni: sesiunile active ale contului, cu inchiderea uneia sau a tuturor. */
export function UserSessionsTab({ user, active }: { readonly user: UserDetail; readonly active: boolean }) {
  const sessions = useUserSessions(user.id, active);
  const action = useSecurityAction(user.id);
  if (sessions.data === undefined) {
    return sessions.isError ? (
      <div className="banner error" role="alert">
        {t('app.loadError')}{' '}
        <button type="button" className="btn ghost" onClick={() => void sessions.refetch()}>
          {t('app.retry')}
        </button>
      </div>
    ) : (
      <p className="muted" role="status">{t('app.loading')}</p>
    );
  }
  return (
    <div className="tab-body">
      {sessions.data.length === 0 ? (
        <p className="muted">{t('user.sessions.empty')}</p>
      ) : (
        <>
          <table className="simple">
            <thead>
              <tr>
                <th scope="col">{t('user.sessions.started')}</th>
                <th scope="col">{t('user.sessions.lastSeen')}</th>
                <th scope="col">{t('user.sessions.ip')}</th>
                <th scope="col">{t('user.sessions.browser')}</th>
                <th scope="col"><span className="sr-only">{t('common.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {sessions.data.map((s) => (
                <tr key={s.sessionId}>
                  <td>{dateTimeText(s.createdAt)}</td>
                  <td>{dateTimeText(s.lastSeenAt)}</td>
                  <td>{s.ipAddress ?? t('common.none')}</td>
                  <td className="muted small">{s.userAgent ?? t('common.none')}</td>
                  <td>
                    <button type="button" className="btn ghost small" disabled={action.busy}
                      onClick={() => void action.run(undefined, () => userApi.revokeSessions(user.id, s.sessionId), t('user.sessions.revoked'))}>
                      {t('user.sessions.revoke')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <button type="button" className="btn ghost" disabled={action.busy}
            onClick={() => void action.run(t('user.sessions.revokeAllConfirm', { name: user.displayName }), () => userApi.revokeSessions(user.id, null),
              t('user.sessions.revoked'))}>
            {t('user.sessions.revokeAll')}
          </button>
        </>
      )}
      <p className="muted small">{t('user.sessions.hint')}</p>
    </div>
  );
}

export function UserHistoryTab({ userId, active }: { readonly userId: number; readonly active: boolean }) {
  return <HistoryView history={useUserHistory(userId, active)} />;
}
