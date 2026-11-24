import type { components } from '../api/schema';

export type GlAccountItem = components['schemas']['GlAccountItemDto'];

/** Un rând vizibil din arborele planului: contul, adâncimea și dacă are copii (macheta c1-01). */
export interface ChartRow {
  readonly account: GlAccountItem;
  readonly depth: number;
  readonly hasChildren: boolean;
  readonly expanded: boolean;
}

/** Filtrele din antetul arborelui: toate conturile, doar analiticele clientului, doar cele inactive. */
export type ChartFilter = 'all' | 'analytics' | 'inactive';

/**
 * Rândurile vizibile ale arborelui, în ordinea codului: un cont apare dacă se potrivește cu căutarea (simbol la început sau denumire) și
 * cu filtrul, ori dacă un descendent se potrivește (ca să rămână calea până la el). Cu căutare sau filtru, ramurile potrivite sunt
 * deschise; altfel doar cele din `expanded`.
 */
export function visibleRows(accounts: readonly GlAccountItem[], expanded: ReadonlySet<number>, search: string, filter: ChartFilter): ChartRow[] {
  const byParent = new Map<number | null, GlAccountItem[]>();
  for (const a of accounts) {
    const key = a.parentId ?? null;
    byParent.set(key, [...(byParent.get(key) ?? []), a]);
  }

  for (const list of byParent.values()) {
    list.sort((x, y) => x.code.localeCompare(y.code, 'ro'));
  }

  const term = search.trim().toLocaleLowerCase('ro');
  const filtering = term.length > 0 || filter !== 'all';
  const matches = (a: GlAccountItem) =>
    (term.length === 0 || a.code.toLocaleLowerCase('ro').startsWith(term) || a.name.toLocaleLowerCase('ro').includes(term))
    && (filter === 'all' || (filter === 'analytics' && !a.isSystem) || (filter === 'inactive' && !a.isActive));

  const keep = new Map<number, boolean>();
  const visit = (a: GlAccountItem): boolean => {
    const children = byParent.get(a.id) ?? [];
    let any = matches(a);
    for (const c of children) {
      any = visit(c) || any;
    }

    keep.set(a.id, any);
    return any;
  };
  const roots = byParent.get(null) ?? [];
  roots.forEach(visit);

  const rows: ChartRow[] = [];
  const walk = (a: GlAccountItem, depth: number) => {
    if (!keep.get(a.id)) {
      return;
    }

    const children = (byParent.get(a.id) ?? []).filter((c) => keep.get(c.id));
    const open = filtering || expanded.has(a.id);
    rows.push({ account: a, depth, hasChildren: children.length > 0, expanded: open && children.length > 0 });
    if (open) {
      children.forEach((c) => walk(c, depth + 1));
    }
  };
  roots.forEach((r) => walk(r, 0));
  return rows;
}

/** Tipul contului: 1 activ, 2 pasiv, 3 bifuncțional. */
export function accountTypeKey(type: number | null | undefined): 'chart.type.asset' | 'chart.type.liability' | 'chart.type.both' | undefined {
  switch (type) {
    case 1:
      return 'chart.type.asset';
    case 2:
      return 'chart.type.liability';
    case 3:
      return 'chart.type.both';
    default:
      return undefined;
  }
}

/** Nivelul în plan: clasă, grupă, sintetic de gradul I, sintetic de gradul II, analitic. */
export function levelKey(level: number) {
  return (['chart.level.1', 'chart.level.2', 'chart.level.3', 'chart.level.4'] as const)[level - 1] ?? 'chart.level.5';
}

/** Codul propus pentru un analitic nou: codul părintelui, punct și primul sufix numeric liber, pe două cifre. */
export function nextAnalyticCode(parentCode: string, accounts: readonly GlAccountItem[]): string {
  const used = new Set(accounts.map((a) => a.code.toUpperCase()));
  for (let n = 1; n < 100; n++) {
    const code = `${parentCode}.${String(n).padStart(2, '0')}`;
    if (!used.has(code.toUpperCase())) {
      return code;
    }
  }

  return `${parentCode}.`;
}
