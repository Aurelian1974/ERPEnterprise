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
import { PenaltiesPage } from '../finance/PenaltiesPage';
import { FxDifferencesPage } from '../finance/FxDifferencesPage';
import { FxExposurePage } from '../finance/FxExposurePage';
import { ChartOfAccountsPage } from '../finance/ChartOfAccountsPage';
import { AltChartsPage } from '../finance/AltChartsPage';
import { MappingRulesPage } from '../finance/MappingRulesPage';
import { MappingCheckPage } from '../finance/MappingCheckPage';
import { JournalEntriesPage } from '../finance/JournalEntriesPage';
import { ManualEntryPage } from '../finance/ManualEntryPage';
import { PostingQueuePage } from '../finance/PostingQueuePage';
import { PostingModelsPage } from '../finance/PostingModelsPage';
import { DefaultAccountsPage } from '../finance/DefaultAccountsPage';
import { TrialBalancePage } from '../finance/TrialBalancePage';
import { AccountLedgerPage } from '../finance/AccountLedgerPage';
import { MonthClosePage } from '../finance/MonthClosePage';
import { ClosingEntriesPage, ReconciliationPage } from '../finance/ClosingReportsPages';
import { YearClosePage } from '../finance/YearClosePage';
import { AccountingPeriodsPage } from '../finance/AccountingPeriodsPage';
import { FxRevaluationPage } from '../finance/FxRevaluationPage';
import { PaymentOrdersPage } from '../finance/PaymentOrdersPage';
import { MatchingRulesPage } from '../finance/MatchingRulesPage';
import { PartnerBalancesPage } from '../finance/PartnerBalancesPage';
import { PartnerFinancePage } from '../finance/PartnerFinancePage';
import { AllocationPage } from '../finance/AllocationPage';
import { DueSchedulePage } from '../finance/DueSchedulePage';
import { CompensationsPage } from '../finance/CompensationsPage';
import { BalanceConfirmationsPage } from '../finance/BalanceConfirmationsPage';
import { CommercialPapersPage } from '../finance/CommercialPapersPage';
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
      { path: 'finante/banca/ordine-de-plata', element: <PaymentOrdersPage /> },
      { path: 'finante/banca/reguli', element: <MatchingRulesPage /> },
      { path: 'finante/decontari/solduri', element: <PartnerBalancesPage /> },
      { path: 'finante/decontari/parteneri/:id', element: <PartnerFinancePage /> },
      { path: 'finante/decontari/alocare', element: <AllocationPage /> },
      { path: 'finante/decontari/scadentar', element: <DueSchedulePage /> },
      { path: 'finante/decontari/compensari', element: <CompensationsPage /> },
      { path: 'finante/decontari/efecte', element: <CommercialPapersPage /> },
      { path: 'finante/decontari/confirmari', element: <BalanceConfirmationsPage /> },
      { path: 'finante/decontari/penalitati', element: <PenaltiesPage /> },
      { path: 'finante/valuta/reevaluare', element: <FxRevaluationPage /> },
      { path: 'finante/valuta/diferente', element: <FxDifferencesPage /> },
      { path: 'finante/valuta/pozitie', element: <FxExposurePage /> },
      { path: 'finante/contabilitate/note', element: <JournalEntriesPage /> },
      { path: 'finante/contabilitate/note/noua', element: <ManualEntryPage /> },
      { path: 'finante/contabilitate/note/:id', element: <ManualEntryPage /> },
      { path: 'finante/contabilitate/de-contat', element: <PostingQueuePage /> },
      { path: 'finante/contabilitate/modele-contare', element: <PostingModelsPage /> },
      { path: 'finante/contabilitate/conturi-implicite', element: <DefaultAccountsPage /> },
      { path: 'finante/contabilitate/plan-de-conturi', element: <ChartOfAccountsPage /> },
      { path: 'finante/contabilitate/planuri-alternative', element: <AltChartsPage /> },
      { path: 'finante/contabilitate/reguli-mapare', element: <MappingRulesPage /> },
      { path: 'finante/contabilitate/verificare-mapare', element: <MappingCheckPage /> },
      { path: 'finante/contabilitate/perioade', element: <AccountingPeriodsPage /> },
      { path: 'finante/inchideri/luna', element: <MonthClosePage /> },
      { path: 'finante/inchideri/concordante', element: <ReconciliationPage /> },
      { path: 'finante/inchideri/note', element: <ClosingEntriesPage /> },
      { path: 'finante/inchideri/anul', element: <YearClosePage /> },
      { path: 'finante/rapoarte/balanta', element: <TrialBalancePage /> },
      { path: 'finante/rapoarte/fisa-contului', element: <AccountLedgerPage /> },
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
      { path: 'admin/config/finante-rate-referinta', element: <ConfigPage /> },
      { path: 'admin/config/finante-solduri-initiale', element: <ConfigPage /> },
      { path: 'admin/config/finante-valuta', element: <ConfigPage /> },
      { path: 'admin/config', element: <ConfigPage /> },
      { path: 'cont/2fa', element: <TwoFactorPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];

export const createRouter = () => createBrowserRouter(routes);
