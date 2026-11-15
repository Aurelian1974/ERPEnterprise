import { useMemo, useRef, useState } from 'react';
import { useOutletContext, useSearchParams } from 'react-router';
import { TextBoxComponent } from '@syncfusion/ej2-react-inputs';
import type { CurrentUser } from '../api/session';
import { t } from '../i18n';
import { BankAccountsTab, CompanySeriesTab } from './CompanyTabs';
import { BranchForm, CompanyForm, GroupForm, LocationForm, OrgReadOnly } from './OrgForms';
import { useCompany, useOrgLookups, useOrgTree, type OrgLookups, type Saved } from './orgApi';
import { buildTree, countNodes, nodeId, type NodeKind, type OrgTree, type TreeNode } from './orgTree';
import { OrgTreeView, type OrgTreeHandle } from './OrgTreeView';

/** Selectia din URL (?nod=c-5 sau ?nou=company, ?nou=branch&firma=5), ca ecranul sa poata fi deschis dintr-un link. */
function useSelection() {
  const [params, setParams] = useSearchParams();
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

        <section className="card detail-card" aria-live="polite">
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
        <BranchForm tree={tree} lookups={lookups} companyId={branch.companyId} branchId={branch.id} onSaved={onSaved('branch')} onCancel={selection.clear} />
      );
    }
    case 'location': {
      const location = tree.locations.find((l) => l.id === node.id);
      return location === undefined ? null : (
        <LocationForm tree={tree} lookups={lookups} companyId={location.companyId} locationId={location.id} onSaved={onSaved('location')} onCancel={selection.clear} />
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

type CompanyTab = 'general' | 'bank' | 'series';

function CompanyPanel({ tree, lookups, companyId, selection, canManage }: {
  readonly tree: OrgTree;
  readonly lookups: OrgLookups;
  readonly companyId: number;
  readonly selection: ReturnType<typeof useSelection>;
  readonly canManage: boolean;
}) {
  const company = useCompany(companyId);
  const [tab, setTab] = useState<CompanyTab>('general');
  const user = useOutletContext<CurrentUser>();
  const canSeeSeries = user.permissions.includes('Core.NumberSeries.Manage');

  if (company.data === undefined) {
    return <p role="status">{company.isError ? company.error.message : t('app.loading')}</p>;
  }
  const data = company.data;
  const group = tree.groups.find((g) => g.id === data.groupId);
  const branches = tree.branches.filter((b) => b.companyId === companyId).length;
  const locations = tree.locations.filter((l) => l.companyId === companyId).length;
  const tabs: [CompanyTab, string][] = [
    ['general', t('org.tab.general')],
    ['bank', t('org.tab.bank')],
    ...(canSeeSeries ? [['series', t('org.tab.series')] as [CompanyTab, string]] : []),
  ];

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
      <div className="tabs" role="tablist">
        {tabs.map(([key, label]) => (
          <button key={key} type="button" role="tab" id={`tab-${key}`} aria-selected={tab === key} aria-controls={`panel-${key}`}
            className={tab === key ? 'tab on' : 'tab'} onClick={() => setTab(key)}>
            {label}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
        {tab === 'general' && (
          <CompanyForm company={data} lookups={lookups} tree={tree} onSaved={() => undefined} onCancel={() => undefined} />
        )}
        {tab === 'bank' && <BankAccountsTab company={data} canManage={canManage} />}
        {tab === 'series' && <CompanySeriesTab companyId={companyId} canManage={canSeeSeries} />}
      </div>
    </div>
  );
}
