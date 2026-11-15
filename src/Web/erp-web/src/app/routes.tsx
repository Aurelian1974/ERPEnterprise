import { createBrowserRouter } from 'react-router';
import { HomePage } from '../home/HomePage';
import { AppShell } from '../shell/AppShell';
import { NotFoundPage } from './NotFoundPage';

// Rutele ecranelor; modulele isi adauga rutele aici odata cu intrarile din meniu (shell/menu.ts).
export const routes = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <HomePage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];

export const createRouter = () => createBrowserRouter(routes);
