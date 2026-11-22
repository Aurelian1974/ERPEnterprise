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
import { TreasuryAccountsPage } from '../finance/TreasuryAccountsPage';
import { CashDeskPage } from '../finance/CashDeskPage';
import { CashDocumentPage } from '../finance/CashDocumentPage';
import { CashRegisterPage } from '../finance/CashRegisterPage';
import { AdvancesPage } from '../finance/AdvancesPage';
import { ExpenseReportsPage } from '../finance/ExpenseReportsPage';
import { BankStatementsPage } from '../finance/BankStatementsPage';
import { BankReconcilePage } from '../finance/BankReconcilePage';
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
      { path: 'finante/trezorerie/conturi', element: <TreasuryAccountsPage /> },
      { path: 'finante/trezorerie/casa', element: <CashDeskPage /> },
      { path: 'finante/trezorerie/casa/document', element: <CashDocumentPage /> },
      { path: 'finante/trezorerie/registru', element: <CashRegisterPage /> },
      { path: 'finante/trezorerie/avansuri', element: <AdvancesPage /> },
      { path: 'finante/trezorerie/deconturi', element: <ExpenseReportsPage /> },
      { path: 'finante/banca/extrase', element: <BankStatementsPage /> },
      { path: 'finante/banca/extras-de-cont', element: <BankReconcilePage /> },
      { path: 'nomenclatoare/articole', element: <ItemsPage /> },
      { path: 'admin/config/nomenclatoare', element: <ConfigPage /> },
      { path: 'admin/config/roluri', element: <ConfigPage /> },
      { path: 'admin/config/cursuri', element: <ConfigPage /> },
      { path: 'admin/config/dimensiuni', element: <ConfigPage /> },
      { path: 'admin/config/precizie', element: <ConfigPage /> },
      { path: 'admin/config/audit', element: <ConfigPage /> },
      { path: 'admin/config/dubluri', element: <ConfigPage /> },
      { path: 'admin/config/atribute', element: <ConfigPage /> },
      { path: 'admin/config/finante-trezorerie', element: <ConfigPage /> },
      { path: 'admin/config', element: <ConfigPage /> },
      { path: 'cont/2fa', element: <TwoFactorPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];

export const createRouter = () => createBrowserRouter(routes);
