import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NotifierProvider } from '../components/Notifier';
import { unsavedChanges } from '../components/UnsavedChanges';
import type { CompanyDetail, OrgLookups } from './orgApi';
import { branchSchema, CompanyForm, companySchema, countyBelongsTo, locationSchema } from './OrgForms';
import type { OrgTree } from './orgTree';

const saveCompany = vi.fn();

vi.mock('./orgApi', async (original) => ({
  ...(await original<typeof import('./orgApi')>()),
  orgApi: { saveCompany: (body: unknown) => saveCompany(body) },
}));

// Toast-ul Syncfusion, ca in Notifier.test.tsx: in jsdom ramane doar regiunea role="status".
vi.mock('@syncfusion/ej2-react-notifications', () => ({ ToastComponent: () => null }));

// Componentele Syncfusion nu se pot monta in jsdom (cad la randare), deci campurile devin aici echivalente HTML simple,
// legate la formular prin acelasi Controller. Testul verifica logica formularului, nu invelisul Syncfusion.
vi.mock('../components/Field', async () => {
  const { Controller } = await import('react-hook-form');
  type Props = { control: never; name: string; label: string; disabled?: boolean };
  const Error = ({ message }: { message: string | undefined }) => (message === undefined ? null : <span role="alert">{message}</span>);
  return {
    ErrorBanner: ({ message }: { message: string | undefined }) => (message === undefined ? null : <div role="alert">{message}</div>),
    TextField: ({ control, name, label }: Props) => (
      <Controller control={control} name={name} render={({ field, fieldState }) => (
        <label>{label}<input aria-label={label} value={(field.value as string | null) ?? ''} onChange={(e) => field.onChange(e.target.value)} /><Error message={fieldState.error?.message} /></label>
      )} />
    ),
    SelectField: ({ control, name, label }: Props) => (
      <Controller control={control} name={name} render={({ field, fieldState }) => (
        <label>{label}<input aria-label={label} value={String(field.value ?? '')} readOnly /><Error message={fieldState.error?.message} /></label>
      )} />
    ),
    SwitchField: ({ control, name, label, disabled }: Props) => (
      <Controller control={control} name={name} render={({ field }) => (
        <input type="checkbox" role="switch" aria-label={label} checked={field.value === true} disabled={disabled === true}
          onChange={(e) => field.onChange(e.target.checked)} />
      )} />
    ),
  };
});

const lookups: OrgLookups = {
  countries: [
    { code: 'RO', name: 'România' },
    { code: 'DE', name: 'Germania' },
  ],
  counties: [
    { code: 'B', countryCode: 'RO', name: 'București' },
    { code: 'CJ', countryCode: 'RO', name: 'Cluj' },
  ],
  precisionPolicies: [{ id: 1, code: 'STANDARD', name: 'Standard' }],
  documentTypes: [],
};

const tree: OrgTree = { groups: [], companies: [], branches: [], locations: [] };

const company = (changes: Partial<CompanyDetail> = {}): CompanyDetail => ({
  id: 7,
  groupId: null,
  code: 'EXD',
  name: 'Exemplu Distribuție SRL',
  fiscalCode: '12345674',
  vatPayer: true,
  vatOnCollection: false,
  tradeRegisterNo: 'J40/1234/2015',
  shareCapital: '200.00',
  address: 'Str. Exemplului nr. 10',
  city: 'București',
  countyCode: 'B',
  countryCode: 'RO',
  postalCode: '010101',
  functionalCurrency: 'RON',
  defaultPrecisionPolicyId: 1,
  includeInConsolidation: true,
  isActive: true,
  modifiedByName: 'Ion Popescu',
  lastChangedAt: '2026-11-12T10:24:00Z',
  rowVersion: 'AAAAAAAAB9E=',
  bankAccounts: [],
  ...changes,
});

function renderForm(detail: CompanyDetail) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <NotifierProvider>
        <CompanyForm company={detail} lookups={lookups} tree={tree} onSaved={() => undefined} onCancel={() => undefined} />
      </NotifierProvider>
    </QueryClientProvider>,
  );
}

const submit = () => fireEvent.click(screen.getByRole('button', { name: 'Salvează' }));

describe('formularul firmei', () => {
  beforeEach(() => {
    saveCompany.mockReset();
    saveCompany.mockResolvedValue({ id: 7, rowVersion: 'AAAAAAAAB9F=' });
    unsavedChanges.clear();
  });

  it('oprirea „Plătitor de TVA” oprește și „TVA la încasare”, deci serverul nu mai respinge un câmp blocat (A2)', async () => {
    renderForm(company({ vatPayer: true, vatOnCollection: true }));

    fireEvent.click(screen.getByRole('switch', { name: 'Plătitor de TVA' }));
    submit();

    await waitFor(() => expect(saveCompany).toHaveBeenCalledTimes(1));
    expect(saveCompany.mock.calls[0]?.[0]).toMatchObject({ vatPayer: false, vatOnCollection: false });
  });

  it('un județ care nu aparține țării se golește, nu ajunge ascuns la server (A1)', async () => {
    // Firma în Germania, dar cu județul „B” rămas de la România: exact ce lăsa în urmă schimbarea țării.
    renderForm(company({ countryCode: 'DE', countyCode: 'B' }));

    submit();

    await waitFor(() => expect(saveCompany).toHaveBeenCalledTimes(1));
    expect(saveCompany.mock.calls[0]?.[0]).toMatchObject({ countryCode: 'DE', countyCode: null });
  });

  it('pentru România fără județ, eroarea apare pe câmp și nu se trimite nimic (A3)', async () => {
    renderForm(company({ countryCode: 'RO', countyCode: null }));

    submit();

    expect(await screen.findByText('Alegeți județul.')).toBeInTheDocument();
    expect(saveCompany).not.toHaveBeenCalled();
  });

  it('după o salvare reușită formularul nu mai are modificări, deci garda nu oprește navigarea (UI-18)', async () => {
    renderForm(company({ vatPayer: true, vatOnCollection: true }));
    fireEvent.click(screen.getByRole('switch', { name: 'Plătitor de TVA' }));
    await waitFor(() => expect(unsavedChanges.any()).toBe(true));

    submit();

    await waitFor(() => expect(saveCompany).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(unsavedChanges.any()).toBe(false));
  });
});

describe('regulile adresei', () => {
  const companyValues = {
    code: 'EXD', name: 'Exemplu', fiscalCode: 'RO12345674', tradeRegisterNo: 'J40/1234/2015', shareCapital: '200,00', groupId: null,
    vatPayer: true, vatOnCollection: false, address: 'Str. X 1', city: 'București', postalCode: null, defaultPrecisionPolicyId: 1,
    includeInConsolidation: true, isActive: true,
  };

  it('județul e obligatoriu pentru România și opțional în rest, la firmă și la punctul de lucru', () => {
    expect(companySchema.safeParse({ ...companyValues, countryCode: 'RO', countyCode: null }).success).toBe(false);
    expect(companySchema.safeParse({ ...companyValues, countryCode: 'RO', countyCode: 'B' }).success).toBe(true);
    expect(companySchema.safeParse({ ...companyValues, countryCode: 'DE', countyCode: null }).success).toBe(true);

    const branch = { code: 'PL-CJ', name: 'Cluj', address: 'Str. Y 2', city: 'Cluj-Napoca', postalCode: null, isActive: true };
    const missing = branchSchema.safeParse({ ...branch, countryCode: 'RO', countyCode: null });
    expect(missing.success).toBe(false);
    expect(missing.error?.issues[0]?.path).toEqual(['countyCode']);
    expect(branchSchema.safeParse({ ...branch, countryCode: 'DE', countyCode: null }).success).toBe(true);
  });

  it('la locație adresa e opțională, dar dacă s-a ales România se cere județul', () => {
    const location = { code: 'DEP-1', name: 'Depozit', branchId: null, locationType: 2, address: null, city: null, postalCode: null, isActive: true };
    expect(locationSchema.safeParse({ ...location, countryCode: null, countyCode: null }).success).toBe(true);
    expect(locationSchema.safeParse({ ...location, countryCode: 'RO', countyCode: null }).success).toBe(false);
    expect(locationSchema.safeParse({ ...location, countryCode: 'RO', countyCode: 'CJ' }).success).toBe(true);
  });

  it('un județ gol aparține oricărei țări; unul ales, numai țării lui', () => {
    expect(countyBelongsTo(lookups, 'DE', null)).toBe(true);
    expect(countyBelongsTo(lookups, 'RO', 'B')).toBe(true);
    expect(countyBelongsTo(lookups, 'DE', 'B')).toBe(false);
  });
});
