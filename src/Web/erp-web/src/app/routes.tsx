import { createBrowserRouter } from 'react-router';
import { HomePage } from '../home/HomePage';
import { AppShell } from '../shell/AppShell';
import { ConfigPage } from '../config/ConfigPage';
import { TwoFactorPage } from '../account/TwoFactorPage';
import { OrgPage } from '../org/OrgPage';
import { PartnersPage } from '../partners/PartnersPage';
import { ItemsPage } from '../items/ItemsPage';
import { UsersPage } from '../users/UsersPage';
import { EmployeesPage } from '../employees/EmployeesPage';
import { NotFoundPage } from './NotFoundPage';

// Rutele ecranelor; modulele isi adauga rutele aici odata cu intrarile din meniu (shell/menu.ts).
export const routes = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'admin/firme', element: <OrgPage /> },
      { path: 'admin/utilizatori', element: <UsersPage /> },
      { path: 'admin/angajati', element: <EmployeesPage /> },
      { path: 'admin/config/serii', element: <ConfigPage /> },
      { path: 'admin/config/licenta', element: <ConfigPage /> },
      { path: 'admin/config/tipuri-adrese', element: <ConfigPage /> },
      { path: 'admin/config/grupe-parteneri', element: <ConfigPage /> },
      { path: 'admin/config/setari', element: <ConfigPage /> },
      { path: 'admin/config/proprietati', element: <ConfigPage /> },
      { path: 'admin/config/import', element: <ConfigPage /> },
      { path: 'nomenclatoare/parteneri', element: <PartnersPage /> },
      { path: 'nomenclatoare/articole', element: <ItemsPage /> },
      { path: 'admin/config/nomenclatoare', element: <ConfigPage /> },
      { path: 'admin/config/roluri', element: <ConfigPage /> },
      { path: 'admin/config/cursuri', element: <ConfigPage /> },
      { path: 'admin/config/dimensiuni', element: <ConfigPage /> },
      { path: 'admin/config/precizie', element: <ConfigPage /> },
      { path: 'admin/config/audit', element: <ConfigPage /> },
      { path: 'admin/config', element: <ConfigPage /> },
      { path: 'cont/2fa', element: <TwoFactorPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];

export const createRouter = () => createBrowserRouter(routes);
