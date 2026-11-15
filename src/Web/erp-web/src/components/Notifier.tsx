import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';
import { ToastComponent } from '@syncfusion/ej2-react-notifications';
import { t } from '../i18n';

export type NoticeKind = 'success' | 'error';

interface Notifier {
  /** Confirmarea unei operatiuni (de exemplu salvarea), afisata cateva secunde in coltul ecranului. */
  readonly notify: (message: string, kind?: NoticeKind) => void;
}

const NotifierContext = createContext<Notifier>({ notify: () => undefined });

export const useNotifier = () => useContext(NotifierContext);

/** Notificarile aplicatiei (Toast Syncfusion), anuntate si cititoarelor de ecran prin regiunea role="status". */
export function NotifierProvider({ children }: { readonly children: ReactNode }) {
  const toast = useRef<ToastComponent | null>(null);
  const [announcement, setAnnouncement] = useState('');

  const notify = useCallback((message: string, kind: NoticeKind = 'success') => {
    setAnnouncement(message);
    toast.current?.show({
      content: message,
      cssClass: kind === 'success' ? 'e-toast-success' : 'e-toast-danger',
      icon: kind === 'success' ? 'e-success toast-icons' : 'e-error toast-icons',
      timeOut: kind === 'success' ? 3000 : 6000,
    });
  }, []);

  const value = useMemo(() => ({ notify }), [notify]);

  return (
    <NotifierContext.Provider value={value}>
      {children}
      <ToastComponent ref={toast} position={{ X: 'Right', Y: 'Top' }} showCloseButton newestOnTop width="340px" />
      <div className="sr-only" role="status" aria-live="polite">
        {announcement}
      </div>
    </NotifierContext.Provider>
  );
}

export const savedMessage = () => t('common.saved');
