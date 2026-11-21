import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useOutletContext, useSearchParams } from 'react-router';
import { TextBoxComponent } from '@syncfusion/ej2-react-inputs';
import type { CurrentUser } from '../api/session';
import { companyStore } from '../app/company';
import { tabKeyTarget } from '../components/tabs';
import { t } from '../i18n';
import { AddressesTab, BankAccountsTab, CompanySeriesTab, ContactsSection, ContactsTab } from './CompanyTabs';
import { CompanyUsersTab } from '../roles/CompanyUsersTab';
import { BranchForm, CompanyForm, GroupForm, LocationForm, OrgReadOnly } from './OrgForms';
import { useCompany, useOrgLookups, useOrgTree, type OrgLookups, type Saved } from './orgApi';
import { buildTree, countNodes, nodeId, type NodeKind, type OrgTree, type TreeNode } from './orgTree';
import { OrgTreeView, type OrgTreeHandle } from './OrgTreeView';

/** Selectia din URL (?nod=c-5 sau ?nou=company, ?nou=branch&firma=5), ca ecranul sa poata fi deschis dintr-un link. */
function useSelection() {
  const [params, setParams] = useSearchParams();
  // Firma aleasa din antet devine nodul selectat: ecranul arata firma curenta, nu ramane pe firma anterioara.
  useEffect(
    () =>
      companyStore.subscribe(() => {
        const companyId = companyStore.get();
        if (companyId !== undefined) {
          setParams({ nod: nodeId('company', companyId) });
        }
      }),
    [setParams],
  );
  return {
    node: params.get('nod') ?? undefined,
    create: params.get('nou') ?? undefined,
    parent: params.get('parinte') ?? undefined,
    select: (node: string) => setParams({ nod: node }),
    startCreate: (kind: NodeKind, parent?: string) => setParams(parent === undefined ? { nou: kind } : { nou: kind, parinte: parent }),
    clear: () => setParams({}),
  };
}

/** Firme si structura organizatorica (macheta 03): arborele in stanga, fisa nodului ales in dreapta. */
export function OrgPage() {
  const user = useOutletContext<CurrentUser>();
  const tree = useOrgTree();
  const lookups = useOrgLookups();
  const selection = useSelection();
  const [search, setSearch] = useState('');
  const [onlyCompanies, setOnlyCompanies] = useState(false);
  const [showInactive, setShowInactive] = useState(false);
  const treeHandle = useRef<OrgTreeHandle | null>(null);
  // Nodurile stranse de utilizator raman stranse si dupa reincarcarea datelor (de exemplu dupa o salvare).
  const [collapsed] = useState(() => new Set<string>());
  const canManage = user.permissions.includes('Core.Company.Manage');

  const built = useMemo(
    () => (tree.data === undefined ? [] : buildTree(tree.data, { search, onlyCompanies, showInactive }, (type) => t(`locationType.${type as 1}`))),
    [tree.data, search, onlyCompanies, showInactive],
  );
  // Aceeasi referinta cat timp arborele afisat e identic: o salvare care nu schimba arborele nu-l redeseneaza.
  const key = JSON.stringify(built);
  const searching = search.trim().length > 0;
  const nodes = useMemo(
    () => (JSON.parse(key) as TreeNode[]).map((n) => ({ ...n, expanded: searching ? n.expanded : !collapsed.has(n.id) })),
    [key, searching, collapsed],
  );
  const onSelect = selection.select;

  if (tree.data === undefined || lookups.data === undefined) {
    return <div className="page" role="status">{tree.isError || lookups.isError ? t('app.loadError') : t('app.loading')}</div>;
  }
  const counts = countNodes(tree.data);

  return (
    <div className="page wide">
      <div className="crumb">{t('menu.admin')} › {t('menu.companies')}</div>
      <div className="head">
        <div>
          <h1>{t('org.title')}</h1>
          <div className="sub2">{t('org.subtitle', counts)}</div>
        </div>
        {canManage && (
          <div className="actions">
            <button type="button" className="btn ghost" onClick={() => selection.startCreate('group')}>
              + {t('org.addGroup')}
            </button>
            <button type="button" className="btn primary" onClick={() => selection.startCreate('company')}>
              + {t('org.addCompany')}
            </button>
          </div>
        )}
      </div>

      <div className="split">
        <section className="card tree-card" aria-label={t('menu.companies')}>
          <TextBoxComponent
            placeholder={t('org.searchPlaceholder')}
            value={search}
            input={(args: { value: string }) => setSearch(args.value)}
            showClearButton
            htmlAttributes={{ 'aria-label': t('org.searchPlaceholder') }}
          />
          <div className="chips-row">
            <button type="button" className={onlyCompanies ? 'chip' : 'chip on'} aria-pressed={!onlyCompanies} onClick={() => setOnlyCompanies(false)}>
              {t('org.filterAll')}
            </button>
            <button type="button" className={onlyCompanies ? 'chip on' : 'chip'} aria-pressed={onlyCompanies} onClick={() => setOnlyCompanies(true)}>
              {t('org.filterCompanies')}
            </button>
            <button type="button" className={showInactive ? 'chip on' : 'chip'} aria-pressed={showInactive} onClick={() => setShowInactive(!showInactive)}>
              {t('org.showInactive')}
            </button>
            <button type="button" className="chip" onClick={() => treeHandle.current?.expandAll()}>
              {t('org.expandAll')}
            </button>
          </div>
          {nodes.length === 0 ? (
            <p className="muted">{t('org.empty')}</p>
          ) : (
            <OrgTreeView nodes={nodes} selectedId={selection.node} onSelect={onSelect} collapsed={collapsed} handle={treeHandle} />
          )}
          <div className="legend" aria-hidden="true">
            {(['group', 'company', 'branch', 'location'] as const).map((kind) => (
              <span key={kind}>
                <i className={`kind-${kind}`} />
                {t(`org.kind.${kind}`)}
              </span>
            ))}
          </div>
        </section>

        {/* Fara aria-live aici: cititorul de ecran ar citi tot formularul la fiecare nod ales; salvarile le anunta Notifier. */}
        <section className="card detail-card">
          <OrgReadOnly.Provider value={!canManage}>
            <Detail tree={tree.data} lookups={lookups.data} selection={selection} canManage={canManage} />
          </OrgReadOnly.Provider>
        </section>
      </div>
    </div>
  );
}

function Detail({ tree, lookups, selection, canManage }: {
  readonly tree: OrgTree;
  readonly lookups: OrgLookups;
  readonly selection: ReturnType<typeof useSelection>;
  readonly canManage: boolean;
}) {
  const parent = parseNode(selection.parent);
  const onSaved = (kind: NodeKind) => (saved: Saved) => selection.select(nodeId(kind, saved.id));

  if (selection.create === 'group') {
    return <GroupForm tree={tree} groupId={undefined} parentGroupId={parent?.kind === 'group' ? parent.id : undefined} onSaved={onSaved('group')} onCancel={selection.clear} />;
  }
  if (selection.create === 'company') {
    return <CompanyForm company={undefined} lookups={lookups} tree={tree} groupId={parent?.kind === 'group' ? parent.id : undefined} onSaved={onSaved('company')} onCancel={selection.clear} />;
  }
  if (selection.create === 'branch' && parent?.kind === 'company') {
    return <BranchForm tree={tree} lookups={lookups} companyId={parent.id} branchId={undefined} onSaved={onSaved('branch')} onCancel={() => selection.select(nodeId('company', parent.id))} />;
  }
  if (selection.create === 'location' && parent !== undefined) {
    const companyId = parent.kind === 'branch' ? tree.branches.find((b) => b.id === parent.id)?.companyId : parent.id;
    if (companyId !== undefined) {
      return (
        <LocationForm tree={tree} lookups={lookups} companyId={companyId} locationId={undefined} branchId={parent.kind === 'branch' ? parent.id : undefined}
          onSaved={onSaved('location')} onCancel={() => selection.select(nodeId(parent.kind, parent.id))} />
      );
    }
  }

  const node = parseNode(selection.node);
  switch (node?.kind) {
    case 'group':
      return <GroupForm tree={tree} groupId={node.id} onSaved={onSaved('group')} onCancel={selection.clear} />;
    case 'company':
      return <CompanyPanel tree={tree} lookups={lookups} companyId={node.id} selection={selection} canManage={canManage} />;
    case 'branch': {
      const branch = tree.branches.find((b) => b.id === node.id);
      return branch === undefined ? null : (
        <BranchForm tree={tree} lookups={lookups} companyId={branch.companyId} branchId={branch.id} onSaved={onSaved('branch')} onCancel={selection.clear}
          after={(
            <section className="contacts-section" aria-label={t('section.contactPersons')}>
              <h3 className="section">{t('section.contactPersons')}</h3>
              <ContactsSection key={`b-${branch.id}`} companyId={branch.companyId} branchId={branch.id} canManage={canManage}
                contacts={tree.contacts.filter((c) => c.branchId === branch.id)} />
            </section>
          )} />
      );
    }
    case 'location': {
      const location = tree.locations.find((l) => l.id === node.id);
      return location === undefined ? null : (
        <LocationForm tree={tree} lookups={lookups} companyId={location.companyId} locationId={location.id} onSaved={onSaved('location')} onCancel={selection.clear}
          after={(
            <section className="contacts-section" aria-label={t('section.contactPersons')}>
              <h3 className="section">{t('section.contactPersons')}</h3>
              <ContactsSection key={`l-${location.id}`} companyId={location.companyId} locationId={location.id} canManage={canManage}
                contacts={tree.contacts.filter((c) => c.locationId === location.id)} />
            </section>
          )} />
      );
    }
    default:
      return <p className="muted empty-detail">{t('org.selectNode')}</p>;
  }
}

function parseNode(value: string | undefined): { kind: NodeKind; id: number } | undefined {
  const match = value === undefined ? null : /^([gcbl])-(\d+)$/.exec(value);
  if (match === null) {
    return undefined;
  }
  const kinds: Record<string, NodeKind> = { g: 'group', c: 'company', b: 'branch', l: 'location' };
  return { kind: kinds[match[1] ?? 'g'] ?? 'group', id: Number(match[2]) };
}

type CompanyTab = 'general' | 'addresses' | 'contacts' | 'bank' | 'series' | 'users';

function CompanyPanel({ tree, lookups, companyId, selection, canManage }: {
  readonly tree: OrgTree;
  readonly lookups: OrgLookups;
  readonly companyId: number;
  readonly selection: ReturnType<typeof useSelection>;
  readonly canManage: boolean;
}) {
  const company = useCompany(companyId);
  const [tab, setTab] = useState<CompanyTab>('general');
  // Un panou se monteaza la prima vizita si apoi doar se ascunde (UI-18): schimbarea tab-ului nu pierde editarea.
  const [visited, setVisited] = useState<ReadonlySet<CompanyTab>>(() => new Set<CompanyTab>(['general']));
  const tabButtons = useRef(new Map<CompanyTab, HTMLButtonElement>());
  const user = useOutletContext<CurrentUser>();
  const canSeeSeries = user.permissions.includes('Core.NumberSeries.Manage');
  const canSeeUsers = user.permissions.includes('Core.User.Manage');

  if (company.data === undefined) {
    return <p role="status">{company.isError ? company.error.message : t('app.loading')}</p>;
  }
  const data = company.data;
  const group = tree.groups.find((g) => g.id === data.groupId);
  const branches = tree.branches.filter((b) => b.companyId === companyId).length;
  const locations = tree.locations.filter((l) => l.companyId === companyId).length;
  const tabs: [CompanyTab, string][] = [
    ['general', t('org.tab.general')],
    ['addresses', t('org.tab.addresses')],
    ['contacts', t('org.tab.contacts')],
    ['bank', t('org.tab.bank')],
    ...(canSeeSeries ? [['series', t('org.tab.series')] as [CompanyTab, string]] : []),
    ...(canSeeUsers ? [['users', t('org.tab.users')] as [CompanyTab, string]] : []),
  ];
  const open = (next: CompanyTab, focus = false) => {
    setTab(next);
    setVisited((current) => (current.has(next) ? current : new Set([...current, next])));
    if (focus) {
      tabButtons.current.get(next)?.focus();
    }
  };
  const panel = (key: CompanyTab, content: ReactNode) =>
    visited.has(key) && (
      <div role="tabpanel" id={`panel-${key}`} aria-labelledby={`tab-${key}`} hidden={tab !== key}>
        {content}
      </div>
    );

  return (
    <div className="company-panel">
      <div className="panel-head">
        <div>
          <h2>
            {data.name} <span className={data.isActive ? 'pill ok' : 'pill off'}>{data.isActive ? t('common.activeF') : t('common.inactiveF')}</span>
          </h2>
          <div className="muted">
            {group === undefined ? '' : `${group.name} › `}
            {data.name} · {t('field.code').toLocaleLowerCase('ro-RO')} {data.code}
          </div>
          <div className="chips-row">
            <span className="chip static">{t('org.branchCount', { count: branches })}</span>
            <span className="chip static">{t('org.locationCount', { count: locations })}</span>
            <span className="chip static">{t('org.bankCount', { count: data.bankAccounts.length })}</span>
          </div>
        </div>
        {canManage && data.isActive && (
          <div className="actions">
            <button type="button" className="btn ghost" onClick={() => selection.startCreate('branch', nodeId('company', companyId))}>
              {t('org.addBranch')}
            </button>
            <button type="button" className="btn ghost" onClick={() => selection.startCreate('location', nodeId('company', companyId))}>
              {t('org.addLocation')}
            </button>
          </div>
        )}
      </div>
      {/* Tiparul WAI-ARIA (UI-09): un singur tab in ordinea Tab, sagetile si Home/End trec intre ele. */}
      <div className="tabs" role="tablist" aria-label={data.name}>
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
            // Un panou nevizitat inca nu exista in pagina: o referinta spre el ar fi invalida.
            aria-controls={visited.has(key) ? `panel-${key}` : undefined}
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
      {panel('general', <CompanyForm company={data} lookups={lookups} tree={tree} onSaved={() => undefined} onCancel={() => undefined} />)}
      {panel('addresses', <AddressesTab company={data} lookups={lookups} canManage={canManage} />)}
      {panel('contacts', <ContactsTab company={data} canManage={canManage} />)}
      {panel('bank', <BankAccountsTab company={data} canManage={canManage} />)}
      {canSeeSeries && panel('series', <CompanySeriesTab companyId={companyId} canManage={canSeeSeries} />)}
      {canSeeUsers && panel('users', <CompanyUsersTab companyId={companyId} currentUserId={user.userId} />)}
    </div>
  );
}
