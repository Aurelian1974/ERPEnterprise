import { act, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, Link, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { shouldBlock, unsavedChanges, UnsavedChangesGuard, useUnsavedChanges } from './UnsavedChanges';
import { tabKeyTarget } from './tabs';

afterEach(() => unsavedChanges.clear());

describe('starea modificărilor nesalvate', () => {
  it('ține minte fiecare formular separat', () => {
    unsavedChanges.set('a', true);
    unsavedChanges.set('b', true);
    unsavedChanges.set('a', false);
    expect(unsavedChanges.any()).toBe(true);
    unsavedChanges.set('b', false);
    expect(unsavedChanges.any()).toBe(false);
  });

  it('fără modificări, confirmarea nu întreabă nimic', async () => {
    await expect(unsavedChanges.confirmDiscard()).resolves.toBe(true);
    expect(unsavedChanges.isAsking()).toBe(false);
  });

  it('„Rămân” păstrează modificările; „Renunț” le șterge', async () => {
    unsavedChanges.set('a', true);
    const stay = unsavedChanges.confirmDiscard();
    expect(unsavedChanges.isAsking()).toBe(true);
    unsavedChanges.answer(false);
    await expect(stay).resolves.toBe(false);
    expect(unsavedChanges.any()).toBe(true);

    const discard = unsavedChanges.confirmDiscard();
    unsavedChanges.answer(true);
    await expect(discard).resolves.toBe(true);
    expect(unsavedChanges.any()).toBe(false);
  });

  it('oprește numai schimbarea ecranului sau a înregistrării, și numai cu modificări', () => {
    const here = { pathname: '/admin/firme', search: '?nod=c-1' };
    expect(shouldBlock(true, here, { pathname: '/admin/firme', search: '?nod=c-2' })).toBe(true);
    expect(shouldBlock(true, here, { pathname: '/', search: '' })).toBe(true);
    expect(shouldBlock(true, here, { ...here })).toBe(false);
    expect(shouldBlock(false, here, { pathname: '/', search: '' })).toBe(false);
  });
});

/** O fișă cu modificări controlate din test și o legătură spre alt ecran, cu garda montată ca în AppShell. */
function Sheet() {
  useUnsavedChanges(true);
  return (
    <>
      <h1>Fișa</h1>
      <Link to="/alt-ecran">Alt ecran</Link>
      <UnsavedChangesGuard />
    </>
  );
}

function renderApp() {
  const router = createMemoryRouter(
    [
      { path: '/', element: <Sheet /> },
      { path: '/alt-ecran', element: <h1>Alt ecran</h1> },
    ],
    { initialEntries: ['/'] },
  );
  render(<RouterProvider router={router} />);
  return router;
}

describe('garda în aplicație (UI-18)', () => {
  it('o navigare cu modificări cere confirmare; „Rămân” rămâne pe fișă', async () => {
    const router = renderApp();

    fireEvent.click(screen.getByRole('link', { name: 'Alt ecran' }));

    expect(await screen.findByRole('dialog', { name: 'Aveți modificări nesalvate' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Rămân' }));
    expect(router.state.location.pathname).toBe('/');
    expect(screen.getByRole('heading', { name: 'Fișa' })).toBeInTheDocument();
  });

  it('„Renunț la modificări” continuă navigarea', async () => {
    const router = renderApp();

    fireEvent.click(screen.getByRole('link', { name: 'Alt ecran' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Renunț la modificări' }));

    await screen.findByRole('heading', { name: 'Alt ecran' });
    expect(router.state.location.pathname).toBe('/alt-ecran');
  });

  it('confirmarea cerută din antet folosește același dialog', async () => {
    renderApp();

    let answer: Promise<boolean> | undefined;
    act(() => {
      answer = unsavedChanges.confirmDiscard();
    });
    fireEvent.click(await screen.findByRole('button', { name: 'Rămân' }));

    await expect(answer).resolves.toBe(false);
  });
});

describe('tastatura tab-urilor (WAI-ARIA)', () => {
  it('săgețile trec circular, Home și End la capete, restul tastelor nu fac nimic', () => {
    expect(tabKeyTarget(0, 'ArrowRight', 3)).toBe(1);
    expect(tabKeyTarget(2, 'ArrowRight', 3)).toBe(0);
    expect(tabKeyTarget(0, 'ArrowLeft', 3)).toBe(2);
    expect(tabKeyTarget(1, 'Home', 3)).toBe(0);
    expect(tabKeyTarget(1, 'End', 3)).toBe(2);
    expect(tabKeyTarget(1, 'Enter', 3)).toBeUndefined();
    expect(tabKeyTarget(0, 'ArrowRight', 0)).toBeUndefined();
  });
});
