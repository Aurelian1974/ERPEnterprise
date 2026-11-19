import { createBrowserRouter } from 'react-router';
import { HomePage } from '../home/HomePage';
import { AppShell } from '../shell/AppShell';
import { ConfigPage } from '../config/ConfigPage';
import { OrgPage } from '../org/OrgPage';
import { NotFoundPage } from './NotFoundPage';

// Rutele ecranelor; modulele isi adauga rutele aici odata cu intrarile din meniu (shell/menu.ts).
export const routes = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'admin/firme', element: <OrgPage /> },
      { path: 'admin/config/serii', element: <ConfigPage /> },
      { path: 'admin/config/licenta', element: <ConfigPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];

export const createRouter = () => createBrowserRouter(routes);
