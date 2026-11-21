import { useRef, useState, type ReactNode } from 'react';
import { Link, useOutletContext, useSearchParams } from 'react-router';
import { TextBoxComponent } from '@syncfusion/ej2-react-inputs';
import type { CurrentUser } from '../api/session';
import { tabKeyTarget } from '../components/tabs';
import { useNotifier } from '../components/Notifier';
import { formatDate } from '../format/date';
import { t } from '../i18n';
import { Pager, useDebounced } from '../partners/PartnersPage';
import { UserKind, useUser, useUserLookups, useUsers, type UserCriteria, type UserDetail, type UserListItem, type UserLookups } from './userApi';
import { UserForm, type UserSection } from './UserForm';
import { accessTypeLabel, initials, isoToday, kindLabel, kindOf, roleLines, userStatus } from './userModel';
import { dateTimeText, UserHistoryTab, UserSecurityTab, UserSessionsTab } from './UserTabs';

type Chip = 'all' | 'employees' | 'externals' | 'inactive' | 'expiring' | 'noTwoFactor';

/** Selectia din URL (?id=5 sau ?nou=1), ca fisa sa poata fi deschisa dintr-un link. */
function useSelection() {
  const [params, setParams] = useSearchParams();
  const id = Number(params.get('id'));
  return {
    id: Number.isInteger(id) && id > 0 ? id : undefined,
    creating: params.get('nou') === '1',
    select: (userId: number) => setParams({ id: String(userId) }),
    startCreate: () => setParams({ nou: '1' }),
    clear: () => setParams({}),
  };
}

/** Parola temporara de la creare sau resetare: aratata o singura data, pana o inchide administratorul (02.01, 11.2). */
interface IssuedPassword {
  readonly userName: string;
  readonly password: string;
}

/**
 * Utilizatori (macheta 06): indicatorii, lista cu cautare si filtre in stanga, fisa utilizatorului ales in dreapta (06b angajat,
 * 06c extern). Toata administrarea cere Core.User.Manage, la nivelul instalarii.
 */
export function UsersPage() {
  const user = useOutletContext<CurrentUser>();
  const lookups = useUserLookups();
  const selection = useSelection();
  const [search, setSearch] = useState('');
  const [chip, setChip] = useState<Chip>('all');
  const [companyId, setCompanyId] = useState<number | undefined>();
  const [roleId, setRoleId] = useState<number | undefined>();
  const [issued, setIssued] = useState<IssuedPassword | undefined>();
  const debounced = useDebounced(search);
  const filterKey = JSON.stringify([debounced, chip, companyId, roleId]);
  const [paging, setPaging] = useState({ key: filterKey, page: 1 });
  const page = paging.key === filterKey ? paging.page : 1;
  const today = isoToday();
  const criteria: UserCriteria = {
    search: debounced,
    kind: chip === 'employees' ? 1 : chip === 'externals' ? 2 : undefined,
    filter: chip === 'inactive' ? 1 : chip === 'expiring' ? 2 : chip === 'noTwoFactor' ? 3 : undefined,
    companyId,
    roleId,
    page,
  };
  const list = useUsers(criteria);
  const canSeeEmployees = user.permissions.includes('Core.Employee.View');

  if (lookups.data === undefined) {
    return <div className="page" role="status">{lookups.isError ? t('app.loadError') : t('app.loading')}</div>;
  }
  const data = list.data;
  const counters = data?.counters;
  const chips: [Chip, string][] = [
    ['all', t('user.filter.all')],
    ['employees', t('user.filter.employees')],
    ['externals', t('user.filter.externals')],
    ['inactive', t('user.filter.inactive')],
    ['expiring', t('user.filter.expiring')],
    ['noTwoFactor', t('user.filter.noTwoFactor')],
  ];

  return (
    <div className="page wide">
      <div className="crumb">{t('menu.admin')} › {t('menu.users')}</div>
      <div className="head">
        <div>
          <h1>{t('user.title')}</h1>
          <div className="sub2">{t('user.subtitle')}</div>
        </div>
        <div className="actions">
          {canSeeEmployees && (
            <Link to="/admin/angajati" className="btn ghost">
              {t('menu.employees')}
            </Link>
          )}
          <button type="button" className="btn primary" onClick={selection.startCreate}>
            {t('user.new')}
          </button>
        </div>
      </div>

      {issued !== undefined && <PasswordBanner issued={issued} onClose={() => setIssued(undefined)} />}

      {counters !== undefined && (
        <div className="tiles users-tiles" aria-label={t('user.tiles')}>
          <Tile label={t('user.tile.active')} value={counters.activeCount}
            note={counters.licensedUsers == null ? t('user.tile.noLimit') : t('user.tile.ofLicensed', { count: counters.licensedUsers })}
            kind={counters.licensedUsers != null && counters.activeCount >= counters.licensedUsers ? 'warn' : undefined} />
          <Tile label={t('user.tile.employees')} value={counters.employeeCount} note={t('user.tile.employeesNote')} />
          <Tile label={t('user.tile.externals')} value={counters.externalCount} note={t('user.tile.externalsNote')} />
          <Tile label={t('user.tile.expiring')} value={counters.expiringCount} note={t('user.tile.expiringNote')}
            kind={counters.expiringCount > 0 ? 'warn' : undefined} />
          <Tile label={t('user.tile.noTwoFactor')} value={counters.withoutTwoFactorCount} note={t('user.tile.noTwoFactorNote')}
            kind={counters.withoutTwoFactorCount > 0 ? 'bad' : undefined} />
        </div>
      )}

      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.users')}>
          <TextBoxComponent
            placeholder={t('user.searchPlaceholder')}
            value={search}
            input={(args: { value: string }) => setSearch(args.value)}
            showClearButton
            htmlAttributes={{ 'aria-label': t('user.searchPlaceholder') }}
          />
          <div className="chips-row">
            {chips.map(([key, label]) => (
              <button key={key} type="button" className={chip === key ? 'chip on' : 'chip'} aria-pressed={chip === key} onClick={() => setChip(key)}>
                {label}
              </button>
            ))}
          </div>
          <Filters lookups={lookups.data} companyId={companyId} roleId={roleId} onCompany={setCompanyId} onRole={setRoleId} />
          {list.isError && data === undefined && (
            <div className="banner error" role="alert">
              {t('app.loadError')}{' '}
              <button type="button" className="btn ghost" onClick={() => void list.refetch()}>
                {t('app.retry')}
              </button>
            </div>
          )}
          {data !== undefined && data.items.length === 0 && <p className="muted">{t('user.empty')}</p>}
          {data !== undefined && data.items.length > 0 && (
            <table className="partner-list user-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('user.col.user')}</th>
                  <th scope="col">{t('user.col.kind')}</th>
                  <th scope="col">{t('user.col.roles')}</th>
                  <th scope="col">{t('user.col.twoFactor')}</th>
                  <th scope="col">{t('user.col.status')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((item) => (
                  <UserRow key={item.id} item={item} today={today} current={item.id === selection.id} onSelect={() => selection.select(item.id)} />
                ))}
              </tbody>
            </table>
          )}
          {data !== undefined && data.total > 0 && <Pager page={page} total={data.total} onPage={(next) => setPaging({ key: filterKey, page: next })} />}
        </section>

        <section className="card detail-card">
          {selection.creating ? (
            <div className="company-panel">
              <UserForm key="new" user={undefined} lookups={lookups.data} section="account" currentUserId={user.userId}
                onSaved={(saved, userName) => {
                  if (saved.temporaryPassword != null) {
                    setIssued({ userName, password: saved.temporaryPassword });
                  }
                  selection.select(saved.id);
                }}
                onCancel={selection.clear} />
              <p className="muted small">{t('user.saveFirst')}</p>
            </div>
          ) : selection.id === undefined ? (
            <p className="muted empty-detail">{t('user.select')}</p>
          ) : (
            <UserPanel key={selection.id} userId={selection.id} lookups={lookups.data} currentUserId={user.userId}
              onPassword={(userName, password) => setIssued({ userName, password })} />
          )}
        </section>
      </div>
    </div>
  );
}

function Tile({ label, value, note, kind }: { readonly label: string; readonly value: number; readonly note: string; readonly kind?: 'warn' | 'bad' | undefined }) {
  return (
    <div className={kind === undefined ? 'tile' : `tile ${kind}`}>
      <span className="tile-label">{label}</span>
      <strong>{value.toLocaleString('ro-RO')}</strong>
      <span className="muted small">{note}</span>
    </div>
  );
}

function Filters({ lookups, companyId, roleId, onCompany, onRole }: {
  readonly lookups: UserLookups;
  readonly companyId: number | undefined;
  readonly roleId: number | undefined;
  readonly onCompany: (id: number | undefined) => void;
  readonly onRole: (id: number | undefined) => void;
}) {
  return (
    <div className="chips-row property-filter">
      <select className="chip" aria-label={t('user.filter.company')} value={companyId ?? ''}
        onChange={(e) => onCompany(e.target.value === '' ? undefined : Number(e.target.value))}>
        <option value="">{t('user.filter.anyCompany')}</option>
        {lookups.companies.map((c) => (
          <option key={c.id} value={c.id}>{c.name}</option>
        ))}
      </select>
      <select className="chip" aria-label={t('user.filter.role')} value={roleId ?? ''} onChange={(e) => onRole(e.target.value === '' ? undefined : Number(e.target.value))}>
        <option value="">{t('user.filter.anyRole')}</option>
        {lookups.roles.map((r) => (
          <option key={r.id} value={r.id}>{r.name}</option>
        ))}
      </select>
    </div>
  );
}

function UserRow({ item, today, current, onSelect }: { readonly item: UserListItem; readonly today: string; readonly current: boolean; readonly onSelect: () => void }) {
  const status = userStatus(item, today);
  const kind = kindOf(item);
  const linked = kind === UserKind.Employee
    ? t('user.linkedEmployee', { no: item.employeeNo ?? '' })
    : kind === UserKind.External ? [item.organizationName, accessTypeLabel(item.externalAccessType)].filter(Boolean).join(' · ') : t('user.kind.system');
  return (
    <tr className={[current ? 'current' : '', item.isActive ? '' : 'inactive'].join(' ').trim()} onClick={onSelect}>
      <td>
        <div className="user-cell">
          <span className="avatar small" aria-hidden="true">{initials(item.displayName)}</span>
          <div>
            <button type="button" className="row-link" aria-current={current ? 'true' : undefined} onClick={(e) => {
              e.stopPropagation();
              onSelect();
            }}>
              {item.displayName}
            </button>
            <div className="muted small">{item.userName} · {linked}</div>
          </div>
        </div>
      </td>
      <td><span className={kind === UserKind.External ? 'tag supplier' : 'tag customer'}>{kindLabel(kind)}</span></td>
      <td className="small">{roleLines(item.roles).map((line) => <div key={line}>{line}</div>)}</td>
      <td>{item.twoFactorEnabled == null ? t('common.none') : t(item.twoFactorEnabled ? 'common.yes' : 'common.no')}</td>
      <td>
        <span className={`pill ${status.kind}`}>{status.text}</span>
        <div className="muted small">{item.lastLoginAt == null ? t('user.neverLoggedIn') : dateTimeText(item.lastLoginAt)}</div>
      </td>
    </tr>
  );
}

function PasswordBanner({ issued, onClose }: { readonly issued: IssuedPassword; readonly onClose: () => void }) {
  const { notify } = useNotifier();
  return (
    <div className="banner warn password-banner" role="alert">
      <div>
        <strong>{t('user.password.title', { userName: issued.userName })}</strong>
        <div className="mono password-value">{issued.password}</div>
        <div className="small">{t('user.password.hint')}</div>
      </div>
      <div className="actions">
        <button type="button" className="btn ghost" onClick={() => {
          void navigator.clipboard?.writeText(issued.password).then(() => notify(t('user.password.copied')), () => notify(t('user.password.copyFailed'), 'error'));
        }}>
          {t('user.password.copy')}
        </button>
        <button type="button" className="btn primary" onClick={onClose}>
          {t('user.password.done')}
        </button>
      </div>
    </div>
  );
}

type UserTab = UserSection | 'security' | 'sessions' | 'history';

/** Fisa utilizatorului cu tab-urile machetei; un panou se monteaza la prima vizita si apoi doar se ascunde (UI-18). */
function UserPanel({ userId, lookups, currentUserId, onPassword }: {
  readonly userId: number;
  readonly lookups: UserLookups;
  readonly currentUserId: number;
  readonly onPassword: (userName: string, password: string) => void;
}) {
  const user = useUser(userId);
  const [tab, setTab] = useState<UserTab>('account');
  const [visited, setVisited] = useState<ReadonlySet<UserTab>>(() => new Set<UserTab>(['account']));
  const tabButtons = useRef(new Map<UserTab, HTMLButtonElement>());

  if (user.data === undefined) {
    return <p role="status">{user.isError ? user.error.message : t('app.loading')}</p>;
  }
  const data: UserDetail = user.data;
  const tabs: [UserTab, string][] = [
    ['account', t('user.tab.account')],
    ['roles', t('user.tab.roles')],
    ['security', t('user.tab.security')],
    ['sessions', t('user.tab.sessions')],
    ['history', t('user.tab.history')],
  ];
  const open = (next: UserTab, focus = false) => {
    setTab(next);
    setVisited((current) => (current.has(next) ? current : new Set([...current, next])));
    if (focus) {
      tabButtons.current.get(next)?.focus();
    }
  };
  const panel = (key: UserTab, content: ReactNode) =>
    visited.has(key) && (
      <div role="tabpanel" id={`panel-${key}`} aria-labelledby={`tab-${key}`} hidden={tab !== key}>
        {content}
      </div>
    );
  const formSection: UserSection | undefined = tab === 'account' || tab === 'roles' ? tab : undefined;
  const status = userStatus(data, isoToday());
  const companies = new Set(data.roles.map((r) => r.companyId ?? 0)).size;
  const subtitle = [
    data.userName,
    data.lastLoginAt == null ? t('user.neverLoggedIn') : t('user.lastLogin', { date: dateTimeText(data.lastLoginAt) }),
    data.twoFactorEnabled ? t('user.twoFactor.on') : t('user.twoFactor.off'),
  ].join(' · ');

  return (
    <div className="company-panel">
      <div className="panel-head">
        <div className="user-cell">
          <span className="avatar" aria-hidden="true">{initials(data.displayName)}</span>
          <div>
            <h2>
              {data.displayName} <span className={`pill ${status.kind}`}>{status.text}</span>{' '}
              <span className="tag type">{kindLabel(kindOf(data))}</span>
            </h2>
            <div className="muted">{subtitle}</div>
            <div className="chips-row">
              <span className="chip static">{companies === 1 ? t('user.companyCountOne') : t('user.companyCount', { count: companies })}</span>
              <span className="chip static">{data.roles.length === 1 ? t('user.roleCountOne') : t('user.roleCount', { count: data.roles.length })}</span>
              {data.sponsorName != null && <span className="chip static">{t('user.sponsorChip', { name: data.sponsorName })}</span>}
              <span className="chip static">{t('user.createdChip', { date: formatDate(new Date(data.createdAt)) })}</span>
            </div>
          </div>
        </div>
      </div>
      <div className="tabs" role="tablist" aria-label={data.displayName}>
        {tabs.map(([key, label], index) => (
          <button
            key={key}
            ref={(button) => {
              if (button === null) {
                tabButtons.current.delete(key);
              } else {
                tabButtons.current.set(key, button);
              }
            }}
            type="button"
            role="tab"
            id={`tab-${key}`}
            aria-selected={tab === key}
            aria-controls={key === 'account' || key === 'roles' ? 'user-form' : visited.has(key) ? `panel-${key}` : undefined}
            tabIndex={tab === key ? 0 : -1}
            className={tab === key ? 'tab on' : 'tab'}
            onClick={() => open(key)}
            onKeyDown={(event) => {
              const target = tabKeyTarget(index, event.key, tabs.length);
              const next = target === undefined ? undefined : tabs[target]?.[0];
              if (next !== undefined) {
                event.preventDefault();
                open(next, true);
              }
            }}
          >
            {label}
          </button>
        ))}
      </div>
      {/* Cont si Roluri sunt un singur formular, cu o singura salvare; ramane montat pe celelalte tab-uri. */}
      <div id="user-form" role="tabpanel" aria-labelledby={`tab-${formSection ?? 'account'}`} hidden={formSection === undefined}>
        <UserForm user={data} lookups={lookups} section={formSection} currentUserId={currentUserId} onSaved={() => undefined} onCancel={() => undefined} />
      </div>
      {panel('security', <UserSecurityTab user={data} onPassword={(password) => onPassword(data.userName, password)} />)}
      {panel('sessions', <UserSessionsTab user={data} active={tab === 'sessions'} />)}
      {panel('history', <UserHistoryTab userId={userId} active={tab === 'history'} />)}
    </div>
  );
}
