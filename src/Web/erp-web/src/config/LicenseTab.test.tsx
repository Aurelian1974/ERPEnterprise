import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/problem';
import type { License } from './licenseApi';
import { LicenseTab } from './LicenseTab';

const upload = vi.fn();
let current: License;

vi.mock('./licenseApi', async (original) => ({
  ...(await original<typeof import('./licenseApi')>()),
  useLicense: () => ({ data: current, isError: false }),
  licenseApi: { upload: (file: Blob) => upload(file) },
}));

const sample: License = {
  hasLicense: true,
  licenseId: 'LIC-2026-000123',
  keyId: '1',
  customerName: 'Grupul Exemplu',
  fiscalCodes: ['RO10000008', 'RO12345674'],
  modules: ['Core', 'Finance'],
  users: 100,
  payrollEmployees: 300,
  posTerminals: 10,
  issuedAt: '2026-11-15',
  validUntil: null,
  updatesUntil: '2027-06-30',
  state: 'Valid',
  clockBehind: false,
  history: [],
};

function renderTab() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <LicenseTab />
    </QueryClientProvider>,
  );
}

describe('Configurare › Licență', () => {
  beforeEach(() => {
    current = sample;
    upload.mockReset();
  });

  it('arată licența activă: clientul, CUI-urile, nelimitat și modulele licențiate', () => {
    renderTab();

    expect(screen.getByText('Grupul Exemplu')).toBeInTheDocument();
    expect(screen.getByText('RO10000008, RO12345674')).toBeInTheDocument();
    expect(screen.getByText('Nelimitat')).toBeInTheDocument();
    expect(screen.getByText('Validă')).toBeInTheDocument();
    expect(screen.getByText('Finanțe și contabilitate')).toHaveClass('on');
    expect(screen.getByText('Vânzări')).toHaveClass('off');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('avertizează când ceasul serverului e în urmă', () => {
    current = { ...sample, clockBehind: true };
    renderTab();

    expect(screen.getByRole('alert')).toHaveTextContent('Ceasul serverului este în urma');
  });

  it('afișează mesajul serverului când fișierul încărcat nu e o licență', async () => {
    upload.mockRejectedValue(new ApiError(400, { title: 'Datele trimise nu sunt valide.', errors: { fileBase64: ['Fișierul nu este o licență ERP.'] } }));
    renderTab();

    fireEvent.change(screen.getByLabelText('Fișierul de licență (.lic)'), { target: { files: [new File(['x'], 'a.lic')] } });

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Fișierul nu este o licență ERP.'));
    expect(upload).toHaveBeenCalledTimes(1);
  });
});
