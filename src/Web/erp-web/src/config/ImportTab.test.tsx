import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NotifierProvider } from '../components/Notifier';
import { ImportTab } from './ImportTab';

const get = vi.fn();
const post = vi.fn();

vi.mock('../api/client', () => ({
  api: { GET: (path: string, init: unknown) => get(path, init), POST: (path: string, init: unknown) => post(path, init) },
}));
vi.mock('@syncfusion/ej2-react-notifications', () => ({ ToastComponent: () => null }));
vi.mock('../components/Field', () => ({
  ErrorBanner: ({ message }: { message: string | undefined }) => (message === undefined ? null : <div role="alert">{message}</div>),
}));

const ok = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });

const job = (changes: Record<string, unknown> = {}) => ({
  id: 12, entityType: 'Partner', fileName: 'parteneri_initial.xlsx', status: 1, anafCheck: true, totalRows: 1250, validRows: 1231, errorRows: 19,
  warningRows: 42, importedRows: 0, lastError: null, createdAt: '2026-11-20T08:00:00Z', createdByName: 'Andrei Matei', completedAt: null,
  rowVersion: 'AA==', errors: [{ row: 14, column: 'CUI', value: 'RO1234567', message: 'CUI-ul nu este valid (cifra de control greșită).' }],
  warnings: [{ row: 58, column: 'CUI', value: 'RO18547290', message: 'Există deja partenerul „Logistic Partner SRL” (LP); se actualizează.' }],
  ...changes,
});

function renderTab(url: string) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <NotifierProvider>
        <MemoryRouter initialEntries={[url]}>
          <ImportTab />
        </MemoryRouter>
      </NotifierProvider>
    </QueryClientProvider>,
  );
}

describe('importul de parteneri (macheta 07g)', () => {
  beforeEach(() => {
    get.mockReset();
    post.mockReset();
    get.mockImplementation((path: string) => Promise.resolve(ok(path === '/api/core/import' ? [] : job())));
  });

  it('raportul arată numărătorile, erorile și avertizările, apoi confirmă rândurile valide', async () => {
    post.mockResolvedValue(ok(job({ status: 2, importedRows: 1231 })));
    renderTab('/admin/config/import?import=12');

    expect(await screen.findByText('CUI-ul nu este valid (cifra de control greșită).')).toBeInTheDocument();
    expect(screen.getByText('1.231')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Avertizări (1)' }));
    expect(screen.getByText(/se actualizează/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Importă cele 1.231 rânduri valide' }));
    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/core/import/{id}/confirm', { params: { path: { id: 12 } } }));
    expect(await screen.findByText(/Au fost importate 1\.231 rânduri din 1\.250/)).toBeInTheDocument();
  });

  it('fără import în URL: șablonul, fișierul și verificarea ANAF; validarea cere un fișier', async () => {
    renderTab('/admin/config/import');

    expect(await screen.findByRole('button', { name: '⇩ Descarcă șablonul Excel' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Verificare ANAF/ })).toBeChecked();
    expect(screen.getByRole('button', { name: 'Validează fișierul' })).toBeDisabled();
  });
});
