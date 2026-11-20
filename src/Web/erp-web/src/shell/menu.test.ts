import { describe, expect, it } from 'vitest';
import { moduleAccent, moduleForPath, visibleGroups, type MenuGroup } from './menu';

const groups: MenuGroup[] = [
  {
    module: 'Core',
    icon: '⛭',
    items: [
      { id: 'partners', label: 'menu.home', path: '/parteneri', icon: '', module: 'Core', permission: 'Core.Partner.View' },
      { id: 'users', label: 'menu.home', path: '/utilizatori', icon: '', module: 'Core', permission: 'Core.User.Manage' },
    ],
  },
  {
    module: 'Finance',
    icon: '▤',
    items: [{ id: 'notes', label: 'menu.home', path: '/note', icon: '', module: 'Finance', permission: 'Finance.Journal.View' }],
  },
];

describe('meniul', () => {
  it('arată doar ecranele permise ale modulelor active', () => {
    const visible = visibleGroups(groups, { modules: ['Core'], permissions: ['Core.Partner.View', 'Finance.Journal.View'] });

    expect(visible.map((group) => group.module)).toEqual(['Core']);
    expect(visible[0]?.items.map((item) => item.id)).toEqual(['partners']);
  });

  it('ascunde modulul fără niciun ecran permis', () => {
    expect(visibleGroups(groups, { modules: ['Core', 'Finance'], permissions: ['Core.Partner.View'] }).map((g) => g.module)).toEqual(['Core']);
  });

  it('un modul nelicențiat nu apare, chiar dacă rolul are permisiunea', () => {
    expect(visibleGroups(groups, { modules: ['Core'], permissions: ['Finance.Journal.View'] })).toEqual([]);
  });
});

describe('accentul modulului (10 Interfață și temă vizuală)', () => {
  it('fiecare modul are accentul lui, iar unul necunoscut îl ia pe cel al Nucleului', () => {
    expect(moduleAccent('Core')).toBe('--accent-core');
    expect(moduleAccent('Finance')).toBe('--accent-finance');
    expect(moduleAccent('Wms')).toBe('--accent-stock');
    expect(moduleAccent('Necunoscut')).toBe('--accent-core');
  });

  it('banda din antet urmează ecranul deschis, cu potrivirea cea mai lungă', () => {
    expect(moduleForPath(groups, '/note')).toBe('Finance');
    expect(moduleForPath(groups, '/note/2026')).toBe('Finance');
    expect(moduleForPath(groups, '/parteneri')).toBe('Core');
    // Un ecran fără intrare în meniu (Acasă, 404) rămâne pe accentul Nucleului.
    expect(moduleForPath(groups, '/')).toBe('Core');
    // „/notebook” nu e sub „/note”: potrivirea e pe segmente, nu pe prefix de text.
    expect(moduleForPath(groups, '/notebook')).toBe('Core');
  });
});
