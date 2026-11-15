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
    label: 'menu.admin',
    icon: '⛭',
    items: [
      { id: 'companies', label: 'menu.companies', path: '/admin/firme', icon: '', module: 'Core', permission: 'Core.Company.View' },
      { id: 'config', label: 'menu.config', path: '/admin/config/serii', icon: '', module: 'Core', permission: 'Core.NumberSeries.Manage' },
    ],
  },
];

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
