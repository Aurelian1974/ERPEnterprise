import type { TextKey } from '../i18n';

/** O intrare de meniu: apare numai daca modulul e activ in licenta si utilizatorul are permisiunea (UI-07). */
export interface MenuItem {
  readonly id: string;
  readonly label: TextKey;
  readonly path: string;
  readonly icon: string;
  /** Codul modulului din licenta; lipsa = disponibila oricarui utilizator autentificat. */
  readonly module?: string;
  /** Permisiunea de vizualizare a ecranului; lipsa = fara permisiune speciala. */
  readonly permission?: string;
}

/** Un grup de meniu pe modul; apare numai daca are cel putin o intrare vizibila. */
export interface MenuGroup {
  readonly module: string;
  /** Eticheta grupului; implicit numele modulului. */
  readonly label?: TextKey;
  readonly icon: string;
  readonly items: readonly MenuItem[];
}

export interface MenuAccess {
  readonly modules: readonly string[];
  readonly permissions: readonly string[];
}

export const homeItem: MenuItem = { id: 'home', label: 'menu.home', path: '/', icon: '⌂' };

/** Ecranele modulelor se adauga aici pe masura ce sunt implementate (etapele 4-10 ale Nucleului, apoi celelalte module). */
export const menuGroups: readonly MenuGroup[] = [
  {
    module: 'Core',
    label: 'menu.nomenclatures',
    icon: '▤',
    items: [
      { id: 'partners', label: 'menu.partners', path: '/nomenclatoare/parteneri', icon: '', module: 'Core', permission: 'Core.Partner.View' },
      { id: 'items', label: 'menu.items', path: '/nomenclatoare/articole', icon: '', module: 'Core', permission: 'Core.Item.View' },
    ],
  },
  {
    module: 'Core',
    label: 'menu.admin',
    icon: '⛭',
    items: [
      { id: 'users', label: 'menu.users', path: '/admin/utilizatori', icon: '', module: 'Core', permission: 'Core.User.Manage' },
      { id: 'employees', label: 'menu.employees', path: '/admin/angajati', icon: '', module: 'Core', permission: 'Core.Employee.View' },
      { id: 'companies', label: 'menu.companies', path: '/admin/firme', icon: '', module: 'Core', permission: 'Core.Company.View' },
      { id: 'config', label: 'menu.config', path: '/admin/config/serii', icon: '', module: 'Core', permission: 'Core.NumberSeries.Manage' },
    ],
  },
];

/**
 * Accentul fiecarui modul (10 Interfata si tema vizuala, secțiunea 5): banda de sub antet si marcajul
 * intrarii active din meniu. Modulele fara accent propriu il iau pe cel al familiei lor.
 */
const moduleAccents: Readonly<Record<string, string>> = {
  Core: '--accent-core',
  Finance: '--accent-finance',
  Reporting: '--accent-finance',
  Sales: '--accent-sales',
  Pos: '--accent-sales',
  Crm: '--accent-sales',
  Purchasing: '--accent-purchasing',
  Inventory: '--accent-stock',
  Wms: '--accent-stock',
  Tms: '--accent-stock',
  Manufacturing: '--accent-production',
  Construction: '--accent-production',
  Hr: '--accent-hr',
  Integrations: '--accent-integrations',
};

/** Tokenul de accent al modulului; un modul necunoscut primeste accentul Nucleului. */
export function moduleAccent(module: string): string {
  return moduleAccents[module] ?? '--accent-core';
}

/** Modulul ecranului deschis, pentru banda de accent din antet. Potrivirea cea mai lunga castiga. */
export function moduleForPath(groups: readonly MenuGroup[], pathname: string): string {
  let bestModule: string | undefined;
  let bestLength = -1;
  for (const group of groups) {
    for (const item of group.items) {
      const matches = pathname === item.path || pathname.startsWith(`${item.path}/`);
      if (matches && item.path.length > bestLength) {
        bestModule = item.module ?? group.module;
        bestLength = item.path.length;
      }
    }
  }
  return bestModule ?? 'Core';
}

export function canSee(item: MenuItem, access: MenuAccess): boolean {
  return (
    (item.module === undefined || access.modules.includes(item.module)) &&
    (item.permission === undefined || access.permissions.includes(item.permission))
  );
}

/** Grupurile vizibile, fiecare doar cu intrarile permise; serverul ramane autoritatea pentru drepturi si licenta. */
export function visibleGroups(groups: readonly MenuGroup[], access: MenuAccess): MenuGroup[] {
  return groups
    .filter((group) => access.modules.includes(group.module))
    .map((group) => ({ ...group, items: group.items.filter((item) => canSee(item, access)) }))
    .filter((group) => group.items.length > 0);
}
