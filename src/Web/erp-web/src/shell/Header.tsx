import { DropDownListComponent } from '@syncfusion/ej2-react-dropdowns';
import { useQueryClient } from '@tanstack/react-query';
import { companyStore } from '../app/company';
import { DropDownButtonComponent, type MenuEventArgs } from '@syncfusion/ej2-react-splitbuttons';
import type { CurrentUser } from '../api/session';
import { t } from '../i18n';
import { browser } from '../app/browser';
import { signOut } from './signOut';

const userActions = [
  { id: 'password', text: t('user.changePassword') },
  { id: '2fa', text: t('user.twoFactor') },
  { separator: true },
  { id: 'signout', text: t('user.signOut') },
];

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter((part) => part.length > 0);
  const first = parts[0]?.charAt(0) ?? '';
  const last = parts.length > 1 ? (parts[parts.length - 1]?.charAt(0) ?? '') : '';
  return (first + last).toLocaleUpperCase('ro-RO') || '?';
}

function onUserAction(args: MenuEventArgs): void {
  switch (args.item.id) {
    case 'password':
      browser.navigate('/account/password');
      break;
    case '2fa':
      browser.navigate('/account/2fa');
      break;
    case 'signout':
      void signOut();
      break;
  }
}

/** Antetul (macheta 02): firma curenta, notificari, utilizatorul. Firmele vin in etapa 4, notificarile in etapa 10. */
export function Header({ user }: { readonly user: CurrentUser }) {
  const queryClient = useQueryClient();
  return (
    <header className="top">
      <a className="brand" href="/">
        <span className="mark" aria-hidden="true">E</span>
        {t('app.name')}
      </a>
      <div className="top-right">
        <div className="company">
          <span className="company-label" aria-hidden="true">{t('header.company')}</span>
          <DropDownListComponent
            id="company"
            htmlAttributes={{ 'aria-label': t('header.company') }}
            dataSource={user.companies.map((c) => ({ value: c.id, text: c.name }))}
            fields={{ text: 'text', value: 'value' }}
            value={user.companyId > 0 ? user.companyId : null}
            placeholder={t('header.noCompany')}
            enabled={user.companies.length > 1 || (user.companies.length === 1 && user.companyId !== user.companies[0]?.id)}
            change={(args: { value: number | null }) => {
              if (args.value !== null && args.value !== user.companyId) {
                companyStore.set(args.value);
                // Datele tuturor ecranelor depind de firma curenta (antetul X-Company-Id).
                void queryClient.resetQueries();
              }
            }}
            width="240px"
          />
        </div>
        <button type="button" className="icon-btn" aria-label={t('header.notifications')} title={t('header.notificationsPending')} disabled>
          <span aria-hidden="true">🔔</span>
        </button>
        <DropDownButtonComponent
          cssClass="user e-flat"
          items={userActions}
          select={onUserAction}
          // Componenta nu transmite aria-label; eticheta accesibila se pune pe butonul creat.
          ref={(button: DropDownButtonComponent | null) => button?.element.setAttribute('aria-label', t('header.userMenu'))}
        >
          <span className="avatar" aria-hidden="true">{initials(user.displayName)}</span>
          <span className="user-name">{user.displayName}</span>
        </DropDownButtonComponent>
      </div>
    </header>
  );
}
