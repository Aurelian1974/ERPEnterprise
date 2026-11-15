import { useState } from 'react';
import { NavLink } from 'react-router';
import type { CurrentUser } from '../api/session';
import { moduleName, t } from '../i18n';
import { homeItem, menuGroups, visibleGroups, type MenuGroup } from './menu';

/** Meniul lateral (macheta 02): Acasa, apoi modulele active cu ecranele permise. */
export function Sidebar({ user, groups = menuGroups }: { readonly user: CurrentUser; readonly groups?: readonly MenuGroup[] }) {
  const [collapsed, setCollapsed] = useState(false);
  const visible = visibleGroups(groups, user);

  return (
    <nav className={collapsed ? 'side collapsed' : 'side'} aria-label={t('menu.label')}>
      <NavLink to={homeItem.path} end className="item">
        <span className="ic" aria-hidden="true">{homeItem.icon}</span>
        <span className="text">{t(homeItem.label)}</span>
      </NavLink>

      {visible.length > 0 && <div className="sec">{t('menu.modules')}</div>}
      {visible.map((group) => (
        <div key={group.module} className="group" role="group" aria-label={group.label === undefined ? moduleName(group.module) : t(group.label)}>
          <div className="item open">
            <span className="ic" aria-hidden="true">{group.icon}</span>
            <span className="text">{group.label === undefined ? moduleName(group.module) : t(group.label)}</span>
          </div>
          {group.items.map((item) => (
            <NavLink key={item.id} to={item.path} className="item sub">
              <span className="text">{t(item.label)}</span>
            </NavLink>
          ))}
        </div>
      ))}

      <button type="button" className="side-foot" aria-expanded={!collapsed} onClick={() => setCollapsed(!collapsed)}>
        {collapsed ? '»' : `« ${t('menu.collapse')}`}
        {collapsed && <span className="sr-only">{t('menu.expand')}</span>}
      </button>
    </nav>
  );
}
