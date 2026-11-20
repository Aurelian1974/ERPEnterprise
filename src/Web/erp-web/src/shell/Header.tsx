import { useCallback, useMemo, useRef, useState, useSyncExternalStore, type CSSProperties } from 'react';
import { useLocation } from 'react-router';
import { DropDownListComponent } from '@syncfusion/ej2-react-dropdowns';
import { useQueryClient } from '@tanstack/react-query';
import { companyStore } from '../app/company';
import { unsavedChanges } from '../components/UnsavedChanges';
import { DropDownButtonComponent, type MenuEventArgs } from '@syncfusion/ej2-react-splitbuttons';
import type { CurrentUser } from '../api/session';
import { t } from '../i18n';
import { browser } from '../app/browser';
import { menuGroups, moduleAccent, moduleForPath } from './menu';
import { signOut } from './signOut';

const companyFields = { text: 'text', value: 'value' };
const companyAttributes = { 'aria-label': t('header.company') };

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
      browser.navigate('/cont/2fa');
      break;
    case 'signout':
      void signOut();
      break;
  }
}

/**
 * Ruleaza actiunea dupa ce Syncfusion a scos lista din pagina: evenimentul „close” vine la inceputul animatiei de
 * inchidere, iar o re-randare in timpul animatiei lasa lista blocata deschisa. Limita de timp acopera o lista neeliminata.
 */
function afterPopupRemoved(popupId: string, action: () => void): void {
  if (!document.getElementById(popupId)) {
    action();
    return;
  }
  const finish = () => {
    observer.disconnect();
    window.clearTimeout(timer);
    action();
  };
  const observer = new MutationObserver(() => {
    if (!document.getElementById(popupId)) {
      finish();
    }
  });
  observer.observe(document.body, { childList: true });
  const timer = window.setTimeout(finish, 1500);
}

/** Antetul (macheta 02): firma curenta, notificari, utilizatorul. Firmele vin in etapa 4, notificarile in etapa 10. */
export function Header({ user }: { readonly user: CurrentUser }) {
  const queryClient = useQueryClient();
  // Banda de sub antet poarta accentul modulului ecranului deschis (10, secțiunea 5).
  const { pathname } = useLocation();
  const accent = useMemo(
    () => ({ '--accent': `var(${moduleAccent(moduleForPath(menuGroups, pathname))})` }) as CSSProperties,
    [pathname],
  );
  // Lista stabila (TanStack Query pastreaza referinta cand datele nu se schimba): fara relegarea selectorului la randare (UI-12).
  const companyOptions = useMemo(() => user.companies.map((c) => ({ value: c.id, text: c.name })), [user.companies]);
  // Firma aleasa se afiseaza imediat (din companyStore), nu abia dupa raspunsul /me: altfel selectorul ar primi valoarea
  // veche in timpul reincarcarii si Syncfusion ar lasa lista deschisa, fara efect la urmatoarea alegere.
  const storedCompany = useSyncExternalStore(companyStore.subscribe, companyStore.get);
  const currentCompany = storedCompany ?? (user.companyId > 0 ? user.companyId : null);
  // Proprietati stabile: Syncfusion reaplica la randare proprietatile cu alta referinta si poate inchide lista deschisa.
  // Alegerea utilizatorului se marcheaza la „select” (singurul eveniment sigur al interactiunii; „change” vine si la relegare)
  // si se aplica dupa inchiderea listei: o re-randare in timpul selectiei/inchiderii lasa lista deschisa, iar urmatoarea
  // alegere ramane fara efect. Fara lista deschisa (alegere cu tastatura) se aplica direct la „change”.
  const pendingCompany = useRef<number | null>(null);
  const popupOpen = useRef(false);
  // La „Raman” in confirmarea modificarilor nesalvate, selectorul se recreeaza ca sa arate iar firma curenta.
  const [revision, setRevision] = useState(0);
  const applyPendingCompany = useCallback(async () => {
    const companyId = pendingCompany.current;
    pendingCompany.current = null;
    if (companyId !== null && companyId !== companyStore.get()) {
      // UI-18: schimbarea firmei curente nu e o navigare, deci o intreaba explicit; altfel fisa deschisa ar ramane pe
      // firma veche cu modificarile pierdute, iar antetul ar arata deja firma noua.
      if (!(await unsavedChanges.confirmDiscard())) {
        setRevision((r) => r + 1);
        return;
      }
      companyStore.set(companyId);
      // Datele tuturor ecranelor depind de firma curenta (antetul X-Company-Id): se reincarca pe loc, fara golirea
      // ecranului (UI-12); /me intai, ca ecranele sa primeasca noua firma si permisiunile ei, apoi restul.
      void queryClient
        .invalidateQueries({ queryKey: ['core', 'me'] })
        .then(() => queryClient.invalidateQueries({ predicate: (query) => query.queryKey[1] !== 'me' }));
    }
  }, [queryClient]);
  const onCompanySelect = useCallback((args: { itemData: { value?: unknown } | null }) => {
    const companyId = Number(args.itemData?.value);
    pendingCompany.current = Number.isInteger(companyId) && companyId > 0 ? companyId : null;
  }, []);
  const onCompanyChange = useCallback(() => {
    if (!popupOpen.current) {
      void applyPendingCompany();
    }
  }, [applyPendingCompany]);
  const onCompanyOpen = useCallback(() => {
    popupOpen.current = true;
  }, []);
  const onCompanyClose = useCallback(() => {
    popupOpen.current = false;
    afterPopupRemoved('company_popup', () => void applyPendingCompany());
  }, [applyPendingCompany]);
  return (
    <header className="top" style={accent}>
      <a className="brand" href="/">
        <span className="mark" aria-hidden="true">E</span>
        {t('app.name')}
      </a>
      <div className="top-right">
        <div className="company">
          <span className="company-label" aria-hidden="true">{t('header.company')}</span>
          <DropDownListComponent
            // Syncfusion nu mai proceseaza alegerea dupa relegarea listei; o lista noua (firma adaugata) recreeaza selectorul.
            key={`${companyOptions.map((c) => c.value).join(',')}#${revision}`}
            id="company"
            htmlAttributes={companyAttributes}
            dataSource={companyOptions}
            fields={companyFields}
            value={currentCompany}
            placeholder={t('header.noCompany')}
            enabled={user.companies.length > 1 || (user.companies.length === 1 && user.companyId !== user.companies[0]?.id)}
            select={onCompanySelect}
            change={onCompanyChange}
            open={onCompanyOpen}
            close={onCompanyClose}
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
