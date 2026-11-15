import { useEffect } from 'react';
import { Outlet } from 'react-router';
import { antiforgeryToken } from '../api/client';
import { useCurrentUser } from '../api/session';
import { t } from '../i18n';
import { Header } from './Header';
import { Sidebar } from './Sidebar';

/** Cadrul ecranelor: antet, meniu, zona de lucru. Fara sesiune, clientul API trimite la /account/login. */
export function AppShell() {
  const user = useCurrentUser();

  useEffect(() => {
    // Tokenul antiforgery al sesiunii curente (cookie-ul XSRF-TOKEN poate fi ramas de la alta sesiune).
    if (user.data !== undefined) {
      void antiforgeryToken(true);
    }
  }, [user.data]);

  if (user.isPending) {
    return (
      <div className="center" role="status">
        {t('app.loading')}
      </div>
    );
  }

  if (user.isError) {
    return (
      <div className="center" role="alert">
        <p>{t('app.loadError')}</p>
        <button type="button" className="btn ghost" onClick={() => void user.refetch()}>
          {t('app.retry')}
        </button>
      </div>
    );
  }

  return (
    <>
      <a className="skip" href="#main">
        {t('app.skipToContent')}
      </a>
      <Header user={user.data} />
      <div className="layout">
        <Sidebar user={user.data} />
        <main className="main" id="main" tabIndex={-1}>
          <Outlet context={user.data} />
        </main>
      </div>
    </>
  );
}
