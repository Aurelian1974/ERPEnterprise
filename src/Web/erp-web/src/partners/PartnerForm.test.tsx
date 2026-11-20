import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NotifierProvider } from '../components/Notifier';
import { unsavedChanges } from '../components/UnsavedChanges';
import type { OrgLookups } from '../org/orgApi';
import type { PartnerDetail, PartnerLookups } from './partnerApi';
import { PartnerForm, type PartnerSection } from './PartnerForm';

const save = vi.fn();
const revealPersonalId = vi.fn();

vi.mock('./partnerApi', async (original) => ({
  ...(await original<typeof import('./partnerApi')>()),
  partnerApi: { save: (body: unknown) => save(body), revealPersonalId: (id: number) => revealPersonalId(id) },
}));

vi.mock('@syncfusion/ej2-react-notifications', () => ({ ToastComponent: () => null }));

// Ca in OrgForms.test.tsx: campurile Syncfusion devin echivalente HTML legate prin acelasi Controller.
vi.mock('../components/Field', async () => {
  const { Controller } = await import('react-hook-form');
  type Props = { control: never; name: string; label: string; disabled?: boolean };
  const Error = ({ message }: { message: string | undefined }) => (message === undefined ? null : <span role="alert">{message}</span>);
  const select = ({ control, name, label }: Props) => (
    <Controller control={control} name={name} render={({ field, fieldState }) => (
      <label>{label}<input aria-label={label} value={String(field.value ?? '')} onChange={(e) => field.onChange(e.target.value === '' ? null : e.target.value)} />
        <Error message={fieldState.error?.message} /></label>
    )} />
  );
  return {
    ErrorBanner: ({ message }: { message: string | undefined }) => (message === undefined ? null : <div role="alert">{message}</div>),
    TextField: ({ control, name, label }: Props) => (
      <Controller control={control} name={name} render={({ field, fieldState }) => (
        <label>{label}<input aria-label={label} value={(field.value as string | null) ?? ''} onChange={(e) => field.onChange(e.target.value)} />
          <Error message={fieldState.error?.message} /></label>
      )} />
    ),
    SelectField: select,
    MultiSelectField: select,
    SwitchField: ({ control, name, label, disabled }: Props) => (
      <Controller control={control} name={name} render={({ field }) => (
        <input type="checkbox" role="switch" aria-label={label} checked={field.value === true} disabled={disabled === true}
          onChange={(e) => field.onChange(e.target.checked)} />
      )} />
    ),
  };
});

const orgLookups: OrgLookups = {
  countries: [{ code: 'RO', name: 'România' }, { code: 'DE', name: 'Germania' }],
  counties: [{ code: 'SV', countryCode: 'RO', name: 'Suceava' }],
  precisionPolicies: [],
  documentTypes: [],
  addressTypes: [],
};

const lookups: PartnerLookups = {
  groups: [],
  properties: [{ id: 10, code: 'SEGMENT', name: 'Segment', dataType: 5, appliesTo: 1, section: 'Comercial', sortOrder: 10, isRequired: true, isFilterable: true }],
  options: [{ id: 101, propertyDefinitionId: 10, code: 'RETAIL', label: 'Retail', isActive: true }],
  users: [],
  companies: [],
};

const partner = (changes: Partial<PartnerDetail> = {}): PartnerDetail => ({
  id: 5, code: 'AGN', name: 'Agro Nord Distribuție SRL', partnerType: 1, fiscalCode: '33445566', hasPersonalId: false, isCustomer: true, isSupplier: false,
  partnerGroupId: null, vatPayer: true, vatOnCollection: false, anafInactive: false, anafCheckedAt: null, tradeRegisterNo: 'J33/567/2012',
  address: 'Str. Gării nr. 21', city: 'Suceava', countyCode: 'SV', countryCode: 'RO', postalCode: '720004', phone: null, email: null,
  intraGroupCompanyId: null, isActive: true, createdAt: '2025-02-03T08:00:00Z', modifiedByName: 'Ana Ionescu', lastChangedAt: '2026-11-10T09:12:00Z',
  rowVersion: 'AAAAAAAAB9E=', addresses: [], bankAccounts: [], contacts: [], properties: [{ propertyDefinitionId: 10, valueOptionId: 101 }],
  ...changes,
});

function renderForm(detail: PartnerDetail | undefined, section: PartnerSection = 'general', canViewPersonalData = false) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <NotifierProvider>
        <PartnerForm partner={detail} lookups={lookups} orgLookups={orgLookups} section={section} canManage canViewPersonalData={canViewPersonalData}
          onSaved={() => undefined} onCancel={() => undefined} />
      </NotifierProvider>
    </QueryClientProvider>,
  );
}

const submit = () => fireEvent.click(screen.getByRole('button', { name: 'Salvează' }));

describe('fișa partenerului', () => {
  beforeEach(() => {
    save.mockReset();
    save.mockResolvedValue({ id: 5, rowVersion: 'AAAAAAAAB9F=' });
    revealPersonalId.mockReset();
    unsavedChanges.clear();
  });

  it('salvează datele generale împreună cu proprietățile, cu versiunea văzută', async () => {
    renderForm(partner());

    submit();

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0]?.[0]).toMatchObject({
      id: 5, rowVersion: 'AAAAAAAAB9E=', fiscalCode: '33445566', changePersonalId: false, personalId: null,
      properties: [{ propertyDefinitionId: 10, valueOptionId: 101 }],
    });
  });

  it('fără niciun rol bifat nu se trimite nimic', async () => {
    renderForm(partner());

    fireEvent.click(screen.getByRole('checkbox', { name: 'Client' }));
    submit();

    expect(await screen.findByText('Bifați cel puțin un rol: client sau furnizor.')).toBeInTheDocument();
    expect(save).not.toHaveBeenCalled();
  });

  it('segmentul obligatoriu pentru clienți se cere, iar pentru un furnizor nu', async () => {
    renderForm(partner({ properties: [] }), 'properties');

    submit();
    expect(await screen.findByText('Câmpul este obligatoriu.')).toBeInTheDocument();
    expect(save).not.toHaveBeenCalled();
  });

  it('un partener nou fără cod lasă serverul să-l aloce din serie', async () => {
    renderForm(undefined);

    fireEvent.change(screen.getByLabelText('CUI *'), { target: { value: 'RO33445566' } });
    fireEvent.change(screen.getByLabelText('Denumire *'), { target: { value: 'Agro Nord' } });
    fireEvent.change(screen.getByLabelText('Adresă *'), { target: { value: 'Str. Gării 21' } });
    fireEvent.change(screen.getByLabelText('Județ'), { target: { value: 'SV' } });
    fireEvent.change(screen.getByLabelText('Localitate *'), { target: { value: 'Suceava' } });
    fireEvent.change(screen.getByLabelText('Segment *'), { target: { value: '101' } });
    submit();

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0]?.[0]).toMatchObject({ id: null, code: null, partnerType: 1, countryCode: 'RO', rowVersion: null });
  });

  it('CNP-ul nu vine cu fișa: se afișează la cerere, iar o salvare fără modificarea lui îl păstrează (A13)', async () => {
    revealPersonalId.mockResolvedValue('1800101221144');
    renderForm(partner({ partnerType: 4, fiscalCode: null, name: 'Popescu Maria', hasPersonalId: true }), 'general', true);

    expect(screen.queryByText('1800101221144')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Afișează' }));
    expect(await screen.findByText('1800101221144')).toBeInTheDocument();
    expect(revealPersonalId).toHaveBeenCalledWith(5);

    submit();
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0]?.[0]).toMatchObject({ partnerType: 4, fiscalCode: null, changePersonalId: false, personalId: null });
  });

  it('fără permisiunea de date personale nu există butonul de afișare a CNP-ului', () => {
    renderForm(partner({ partnerType: 4, fiscalCode: null, hasPersonalId: true }), 'general', false);
    expect(screen.queryByRole('button', { name: 'Afișează' })).not.toBeInTheDocument();
  });
});
