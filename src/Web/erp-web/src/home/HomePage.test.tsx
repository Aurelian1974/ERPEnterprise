import { render, screen } from '@testing-library/react';
import { createMemoryRouter, Outlet, RouterProvider } from 'react-router';
import { describe, expect, it } from 'vitest';
import type { CurrentUser } from '../api/session';
import { HomePage } from './HomePage';

const user: CurrentUser = {
  userId: 7,
  userName: 'ion.popescu',
  displayName: 'Ion Popescu',
  language: 'ro',
  companyId: 0,
  permissions: ['Core.Partner.View', 'Core.Item.View'],
  modules: ['Core', 'Finance'],
  licenseState: 'Valid',
  companies: [],
  wholeCompany: true,
  nodes: [],
};

function renderHome(current: CurrentUser) {
  const router = createMemoryRouter([
    {
      path: '/',
      Component: () => <OutletWith user={current} />,
      children: [{ index: true, element: <HomePage now={new Date('2026-11-15T22:30:00Z')} /> }],
    },
  ]);
  render(<RouterProvider router={router} />);
}

function OutletWith({ user: current }: { readonly user: CurrentUser }) {
  return <Outlet context={current} />;
}

describe('pagina Acasă', () => {
  it('salută după prenume, cu data din fusul orar al României', () => {
    renderHome(user);

    expect(screen.getByRole('heading', { level: 1, name: 'Bună ziua, Ion' })).toBeInTheDocument();
    // 22:30 UTC = 00:30 pe 16 noiembrie la Bucuresti
    expect(screen.getByText('Luni, 16 noiembrie 2026')).toBeInTheDocument();
    expect(screen.getByText('Finanțe și contabilitate')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('avertizează când licența e doar pentru consultare', () => {
    renderHome({ ...user, licenseState: 'ReadOnly' });

    expect(screen.getByRole('alert')).toHaveTextContent('doar pentru consultare');
  });
});
