import type { components } from '../api/schema';

type Schemas = components['schemas'];
export type OrgTree = Schemas['OrgTreeDto'];
export type NodeKind = 'group' | 'company' | 'branch' | 'location';

/** Un nod plat pentru TreeView (Syncfusion): parintele prin parentId, ca in macheta 03. */
export interface TreeNode {
  readonly id: string;
  parentId: string | undefined;
  readonly kind: NodeKind;
  readonly entityId: number;
  readonly companyId: number | undefined;
  readonly code: string;
  readonly name: string;
  /** Textul din dreapta: CUI, cod sau tipul locatiei. */
  readonly detail: string;
  readonly isActive: boolean;
  readonly isGroupParent: boolean;
  hasChildren: boolean;
  expanded: boolean;
}

export interface TreeFilter {
  readonly search: string;
  readonly onlyCompanies: boolean;
  readonly showInactive: boolean;
}

export const nodeId = (kind: NodeKind, id: number) => `${kind.charAt(0)}-${id}`;

const normalize = (text: string) => text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLocaleLowerCase('ro-RO');

/** Nodurile vizibile dupa filtre; un nod care se potriveste cautarii isi arata stramosii (expandati). */
export function buildTree(tree: OrgTree, filter: TreeFilter, locationTypeName: (type: number) => string): TreeNode[] {
  const parentCompanies = new Set(tree.groups.map((g) => g.parentCompanyId).filter((id): id is number => id !== null));
  const all: TreeNode[] = [
    ...tree.groups.map((g) => node('group', g.id, g.parentGroupId === null ? undefined : nodeId('group', g.parentGroupId), undefined, g.code, g.name, 'grup', g.isActive)),
    ...tree.companies.map((c) => ({
      ...node('company', c.id, c.groupId === null ? undefined : nodeId('group', c.groupId), c.id, c.code, c.name, (c.vatPayer ? 'RO' : '') + c.fiscalCode, c.isActive),
      isGroupParent: parentCompanies.has(c.id),
    })),
    ...tree.branches.map((b) => node('branch', b.id, nodeId('company', b.companyId), b.companyId, b.code, b.name, b.code, b.isActive)),
    ...tree.locations.map((l) =>
      node('location', l.id, l.branchId === null ? nodeId('company', l.companyId) : nodeId('branch', l.branchId), l.companyId, l.code, l.name, locationTypeName(l.locationType), l.isActive),
    ),
  ];

  const byId = new Map(all.map((n) => [n.id, n]));
  // Un parinte invizibil (de exemplu grupul fara drept) face din nod o radacina.
  for (const n of all) {
    if (n.parentId !== undefined && !byId.has(n.parentId)) {
      n.parentId = undefined;
    }
  }

  let visible = all.filter((n) => filter.showInactive || n.isActive);
  if (filter.onlyCompanies) {
    visible = visible.filter((n) => n.kind === 'group' || n.kind === 'company');
  }

  const term = normalize(filter.search.trim());
  if (term.length > 0) {
    const keep = new Set<string>();
    for (const n of visible) {
      if (normalize(`${n.name} ${n.code} ${n.detail}`).includes(term)) {
        for (let current: TreeNode | undefined = n; current !== undefined; current = current.parentId === undefined ? undefined : byId.get(current.parentId)) {
          keep.add(current.id);
          if (current !== n) {
            current.expanded = true;
          }
        }
      }
    }
    visible = visible.filter((n) => keep.has(n.id));
  }

  const ids = new Set(visible.map((n) => n.id));
  for (const n of visible) {
    n.hasChildren = visible.some((child) => child.parentId === n.id);
    if (n.parentId !== undefined && !ids.has(n.parentId)) {
      n.parentId = undefined;
    }
  }
  return visible;
}

function node(kind: NodeKind, id: number, parentId: string | undefined, companyId: number | undefined, code: string, name: string, detail: string, isActive: boolean): TreeNode {
  return { id: nodeId(kind, id), parentId, kind, entityId: id, companyId, code, name, detail, isActive, isGroupParent: false, hasChildren: false, expanded: true };
}

/** Numarul de noduri pe tip (subtitlul ecranului). */
export function countNodes(tree: OrgTree) {
  return { groups: tree.groups.length, companies: tree.companies.length, branches: tree.branches.length, locations: tree.locations.length };
}
