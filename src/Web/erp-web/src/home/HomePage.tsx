import { useOutletContext } from 'react-router';
import type { CurrentUser } from '../api/session';
import { formatLongDate } from '../format/date';
import { moduleName, t, type TextKey } from '../i18n';

const licenseWarnings: Readonly<Record<string, TextKey>> = {
  ExpiringSoon: 'license.ExpiringSoon',
  Grace: 'license.Grace',
  ReadOnly: 'license.ReadOnly',
};

/** Prenumele pentru salut: primul cuvant al numelui afisat. */
export function firstName(displayName: string): string {
  return displayName.trim().split(/\s+/)[0] ?? displayName;
}

/**
 * Pagina Acasa (macheta 02). Indicatorii, sarcinile si documentele recente din macheta vin de la modulele care le detin
 * (Finante, Vanzari, Integrari); pana atunci pagina arata salutul, avertismentul de licenta si accesul utilizatorului.
 */
export function HomePage({ now = new Date() }: { readonly now?: Date }) {
  const user = useOutletContext<CurrentUser>();
  const warning = licenseWarnings[user.licenseState];

  return (
    <div className="page">
      <div className="crumb">{t('menu.home')}</div>
      <div className="head">
        <div>
          <h1>{t('home.greeting', { name: firstName(user.displayName) })}</h1>
          <div className="sub2">{formatLongDate(now)}</div>
        </div>
      </div>

      {warning !== undefined && (
        <div className={user.licenseState === 'ExpiringSoon' ? 'banner warn' : 'banner error'} role="alert">
          {t(warning)}
        </div>
      )}

      <section className="card welcome" aria-labelledby="welcome-title">
        <h2 id="welcome-title">{t('home.welcomeTitle')}</h2>
        <p>{t('home.welcomeText')}</p>
      </section>

      <div className="cards">
        <section className="card" aria-labelledby="modules-title">
          <h2 id="modules-title">{t('home.modulesTitle')}</h2>
          <ul className="chips">
            {user.modules
              .map(moduleName)
              .sort((a, b) => a.localeCompare(b, 'ro'))
              .map((name) => (
                <li key={name}>{name}</li>
              ))}
          </ul>
        </section>
        <section className="card" aria-labelledby="permissions-title">
          <h2 id="permissions-title">{t('home.permissionsTitle')}</h2>
          <p>{t('home.permissionsCount', { count: user.permissions.length })}</p>
        </section>
      </div>
    </div>
  );
}
