import { useCallback, useEffect, useId, useRef, useSyncExternalStore } from 'react';
import { useBlocker, type Location } from 'react-router';
import { t } from '../i18n';

/**
 * Garda modificarilor nesalvate (UI-18): o fisa cu modificari nu se paraseste fara confirmare.
 *
 * Fiecare formular isi anunta starea prin useUnsavedChanges(isDirty). Un singur UnsavedChangesGuard, in cadrul
 * aplicatiei, opreste navigarile (React Router permite un singur blocker activ), reimprospatarea paginii si, la cerere,
 * schimbarea firmei curente din antet. Starea e sincrona, nu trece printr-un efect: dupa o salvare reusita formularul
 * se marcheaza curat inainte sa navigheze spre inregistrarea salvata, altfel garda si-ar bloca propria salvare.
 */

type Listener = () => void;

const dirty = new Set<string>();
const listeners = new Set<Listener>();
let pending: ((discard: boolean) => void) | undefined;

const emit = () => {
  for (const listener of listeners) {
    listener();
  }
};

export const unsavedChanges = {
  any(): boolean {
    return dirty.size > 0;
  },
  set(id: string, isDirty: boolean): void {
    const had = dirty.has(id);
    if (isDirty && !had) {
      dirty.add(id);
      emit();
    } else if (!isDirty && had) {
      dirty.delete(id);
      emit();
    }
  },
  clear(): void {
    if (dirty.size > 0) {
      dirty.clear();
      emit();
    }
  },
  /** Pentru actiunile care nu sunt navigari (firma curenta din antet): true daca utilizatorul renunta la modificari. */
  confirmDiscard(): Promise<boolean> {
    if (dirty.size === 0) {
      return Promise.resolve(true);
    }
    pending?.(false);
    return new Promise((resolve) => {
      pending = (discard) => {
        pending = undefined;
        if (discard) {
          unsavedChanges.clear();
        }
        resolve(discard);
        emit();
      };
      emit();
    });
  },
  /** Exista o confirmare ceruta de confirmDiscard care asteapta raspuns. */
  isAsking(): boolean {
    return pending !== undefined;
  },
  answer(discard: boolean): void {
    pending?.(discard);
  },
  subscribe(listener: Listener): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
};

/** O navigare se opreste numai daca schimba ecranul sau inregistrarea (ruta ori parametrii), nu doar ancora. */
export function shouldBlock(hasChanges: boolean, current: Pick<Location, 'pathname' | 'search'>, next: Pick<Location, 'pathname' | 'search'>): boolean {
  return hasChanges && (current.pathname !== next.pathname || current.search !== next.search);
}

/**
 * Inregistreaza starea unui formular la garda. Intoarce markClean, de apelat dupa o salvare reusita, inainte de navigarea
 * spre inregistrarea salvata (formularul trebuie resetat pe valorile salvate in acelasi pas).
 */
export function useUnsavedChanges(isDirty: boolean): { markClean: () => void } {
  const id = useId();
  useEffect(() => {
    unsavedChanges.set(id, isDirty);
  }, [id, isDirty]);
  useEffect(() => () => unsavedChanges.set(id, false), [id]);
  const markClean = useCallback(() => unsavedChanges.set(id, false), [id]);
  return { markClean };
}

/** Garda si dialogul ei; se pune o singura data, in cadrul aplicatiei (AppShell). */
export function UnsavedChangesGuard() {
  const hasChanges = useSyncExternalStore(unsavedChanges.subscribe, unsavedChanges.any);
  const asking = useSyncExternalStore(unsavedChanges.subscribe, unsavedChanges.isAsking);
  const blocker = useBlocker(({ currentLocation, nextLocation }) => shouldBlock(unsavedChanges.any(), currentLocation, nextLocation));

  // Reimprospatarea sau inchiderea paginii: browserul arata propria intrebare.
  useEffect(() => {
    if (!hasChanges) {
      return;
    }
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [hasChanges]);

  const open = blocker.state === 'blocked' || asking;
  const stay = () => (blocker.state === 'blocked' ? blocker.reset() : unsavedChanges.answer(false));
  const discard = () => {
    if (blocker.state === 'blocked') {
      unsavedChanges.clear();
      blocker.proceed();
    } else {
      unsavedChanges.answer(true);
    }
  };

  return <ConfirmDiscardDialog open={open} onStay={stay} onDiscard={discard} />;
}

/** Dialogul de confirmare: <dialog> nativ (focus captiv, Escape = Raman), stilizat cu tokenii temei (UI-13, UI-14). */
export function ConfirmDiscardDialog({ open, onStay, onDiscard }: { readonly open: boolean; readonly onStay: () => void; readonly onDiscard: () => void }) {
  const dialog = useRef<HTMLDialogElement | null>(null);
  const titleId = useId();
  const textId = useId();

  useEffect(() => {
    const element = dialog.current;
    if (element === null) {
      return;
    }
    if (open && !element.open) {
      // jsdom si browserele vechi nu au showModal: atunci dialogul se deschide fara fundal modal.
      if (typeof element.showModal === 'function') {
        element.showModal();
      } else {
        element.setAttribute('open', '');
      }
    } else if (!open && element.open) {
      if (typeof element.close === 'function') {
        element.close();
      } else {
        element.removeAttribute('open');
      }
    }
  }, [open]);

  return (
    <dialog
      ref={dialog}
      className="confirm-dialog"
      aria-labelledby={titleId}
      aria-describedby={textId}
      onCancel={(event) => {
        // Escape: dialogul se inchide prin starea lui, nu de browser, ca garda sa afle raspunsul.
        event.preventDefault();
        onStay();
      }}
    >
      <h2 id={titleId}>{t('unsaved.title')}</h2>
      <p id={textId}>{t('unsaved.text')}</p>
      <div className="actions">
        <button type="button" className="btn ghost" onClick={onDiscard}>
          {t('unsaved.discard')}
        </button>
        {/* „Raman” e alegerea implicita: primeste focusul, deci Enter nu pierde modificarile. */}
        <button type="button" className="btn primary" onClick={onStay} autoFocus>
          {t('unsaved.stay')}
        </button>
      </div>
    </dialog>
  );
}
