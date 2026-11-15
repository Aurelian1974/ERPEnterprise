import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { NotifierProvider, useNotifier } from './Notifier';

// Toast-ul Syncfusion manipuleaza DOM-ul direct (animatii); in jsdom se verifica doar regiunea role="status".
vi.mock('@syncfusion/ej2-react-notifications', () => ({ ToastComponent: () => null }));

function SaveButton() {
  const { notify } = useNotifier();
  return (
    <button type="button" onClick={() => notify('Modificările au fost salvate.')}>
      Salvează
    </button>
  );
}

describe('notificări', () => {
  it('confirmarea salvării e anunțată și cititoarelor de ecran', () => {
    render(
      <NotifierProvider>
        <SaveButton />
      </NotifierProvider>,
    );

    act(() => screen.getByRole('button', { name: 'Salvează' }).click());

    expect(screen.getByRole('status')).toHaveTextContent('Modificările au fost salvate.');
  });
});
