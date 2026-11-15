import { memo, useEffect, useMemo, useRef } from 'react';
import { TreeViewComponent, type NodeExpandEventArgs, type NodeSelectEventArgs } from '@syncfusion/ej2-react-navigations';
import { t } from '../i18n';
import type { NodeKind, TreeNode } from './orgTree';

const kindLetter: Record<NodeKind, string> = { group: 'G', company: 'F', branch: 'P', location: 'L' };

// Sablonul nodului e o functie stabila (definita o singura data): o functie noua la fiecare randare face TreeView sa
// redeseneze tot arborele (flicker, pierderea selectiei).
function nodeTemplate(data: TreeNode) {
  return (
    <span className={data.isActive ? 'tree-node' : 'tree-node inactive'}>
      <span className={`kind kind-${data.kind}`} aria-hidden="true">{kindLetter[data.kind]}</span>
      <span className="tree-name">{data.name}</span>
      {data.isGroupParent && <span className="tag">{t('org.parentBadge')}</span>}
      <span className="tree-detail">{data.detail}</span>
    </span>
  );
}

export interface OrgTreeHandle {
  expandAll(): void;
}

/**
 * Arborele structurii (Syncfusion TreeView). Se redeseneaza doar cand se schimba nodurile (date sau filtre); selectia se
 * reaplica dupa fiecare redesenare, iar nodurile stranse de utilizator raman stranse (collapsed).
 */
export const OrgTreeView = memo(function OrgTreeView({ nodes, selectedId, onSelect, collapsed, handle }: {
  readonly nodes: TreeNode[];
  readonly selectedId: string | undefined;
  readonly onSelect: (id: string) => void;
  readonly collapsed: Set<string>;
  readonly handle: { current: OrgTreeHandle | null };
}) {
  const tree = useRef<TreeViewComponent | null>(null);
  const selected = useRef(selectedId);
  const select = useRef(onSelect);
  useEffect(() => {
    selected.current = selectedId;
    select.current = onSelect;
  }, [selectedId, onSelect]);

  const fields = useMemo(
    () => ({ dataSource: nodes as unknown as { [key: string]: object }[], id: 'id', parentID: 'parentId', text: 'name', hasChildren: 'hasChildren', expanded: 'expanded' }),
    [nodes],
  );

  useEffect(() => {
    handle.current = { expandAll: () => tree.current?.expandAll() };
  }, [handle]);

  // Selectia din URL (alt nod ales, nod nou dupa salvare) si dupa redesenarea arborelui (date noi): DOM-ul e nou, dar
  // lista interna de selectie e aceeasi, deci o golim intai ca reaplicarea sa marcheze nodul.
  useEffect(() => {
    const view = tree.current;
    if (view === null || selectedId === undefined) {
      return;
    }
    view.selectedNodes = [];
    view.dataBind();
    view.selectedNodes = [selectedId];
    view.dataBind();
  }, [selectedId, nodes]);

  // Handler-e stabile: citesc valorile curente prin referinte, deci TreeView nu primeste proprietati noi la randare.
  const events = useMemo(
    () => ({
      dataBound: () => {
        const view = tree.current;
        if (view !== null && selected.current !== undefined) {
          // Dupa redesenare DOM-ul e nou, dar lista interna de selectie e aceeasi: o golim, ca reaplicarea sa marcheze nodul.
          view.selectedNodes = [];
          view.dataBind();
          view.selectedNodes = [selected.current];
        }
      },
      nodeSelected: (args: NodeSelectEventArgs) => {
        const id = (args.nodeData as { id?: unknown }).id;
        if (args.isInteracted && typeof id === 'string' && id !== selected.current) {
          select.current(id);
        }
      },
      nodeCollapsed: (args: NodeExpandEventArgs) => {
        const id = (args.nodeData as { id?: unknown }).id;
        if (typeof id === 'string') {
          collapsed.add(id);
        }
      },
      nodeExpanded: (args: NodeExpandEventArgs) => {
        const id = (args.nodeData as { id?: unknown }).id;
        if (typeof id === 'string') {
          collapsed.delete(id);
        }
      },
    }),
    [collapsed],
  );

  return (
    <TreeViewComponent
      ref={tree}
      fields={fields}
      nodeTemplate={nodeTemplate}
      fullRowSelect
      dataBound={events.dataBound}
      nodeSelected={events.nodeSelected}
      nodeCollapsed={events.nodeCollapsed}
      nodeExpanded={events.nodeExpanded}
    />
  );
});
