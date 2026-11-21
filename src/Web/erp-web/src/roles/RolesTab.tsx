import { Fragment, useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { moduleName, t, type TextKey } from '../i18n';
import { ro } from '../i18n/ro';
import { nodeName } from '../users/userModel';
import { roleApi, roleKeys, useRoleMatrix, type MatrixUser, type Permission, type Role, type RoleMatrix } from './roleApi';
import {
  cellState, globalOverrides, groupPermissions, localOverrides, sameDraft, splitCode, STANDARD_ACTIONS, toggle, toInput,
  type CellState, type DraftOverride, type ModuleGroup,
} from './roleModel';

/** Textul unei chei construite din codul tehnic (resursa, actiunea); un cod fara text ramane ca atare. */
function label(prefix: string, code: string): string {
  const key = `${prefix}.${code}`;
  return key in ro ? t(key as TextKey) : code;
}

const resourceLabel = (code: string) => label('role.res', code);
const actionLabel = (action: string) => label('role.act', action);
const actionShort = (action: string) => label('role.actShort', action);

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

/**
 * Configurare › Roluri (02.01, 8 si 11): „Pe rol” (macheta 07e, drepturile unui rol) si „Roluri și utilizatori” (rolurile cu
 * utilizatorii lor dedesubt si exceptiile fiecaruia, pe firma sau pe toate firmele; deciziile din 21.11.2026).
 */
export function RolesTab({ user }: { readonly user: CurrentUser }) {
  const [view, setView] = useState<'role' | 'users'>('role');
  const [companyId, setCompanyId] = useState<number | null>(user.companyId > 0 ? user.companyId : null);
  const [creating, setCreating] = useState(0);
  const matrix = useRoleMatrix(view === 'role' ? null : companyId);

  return (
    <div className="roles-tab">
      <div className="roles-head">
        <div className="segmented two" role="tablist" aria-label={t('config.tab.roles')}>
          <button type="button" role="tab" aria-selected={view === 'role'} className={view === 'role' ? 'on' : ''} onClick={() => setView('role')}>
            {t('role.view.byRole')}
          </button>
          <button type="button" role="tab" aria-selected={view === 'users'} className={view === 'users' ? 'on' : ''} onClick={() => setView('users')}>
            {t('role.view.users')}
          </button>
        </div>
        {view === 'role' && (
          <button type="button" className="btn primary" onClick={() => setCreating((n) => n + 1)}>{t('role.new')}</button>
        )}
      </div>
      {matrix.error !== null && <ErrorBanner message={problem(matrix.error)} />}
      {matrix.data === undefined ? (
        <p className="muted">{t('app.loading')}</p>
      ) : view === 'role' ? (
        <RolesByRole matrix={matrix.data} creating={creating} />
      ) : (
        <RolesByUser matrix={matrix.data} companyId={companyId} onCompany={setCompanyId} currentUserId={user.userId} />
      )}
    </div>
  );
}

// ---------- Pe rol (macheta 07e) ----------

function RolesByRole({ matrix, creating }: { readonly matrix: RoleMatrix; readonly creating: number }) {
  const [selected, setSelected] = useState<number | 'new' | undefined>(matrix.roles[0]?.id);
  const [copyOf, setCopyOf] = useState<Role | undefined>();
  const [search, setSearch] = useState('');
  const [lastCreating, setLastCreating] = useState(creating);
  if (creating !== lastCreating) {
    setLastCreating(creating);
    setCopyOf(undefined);
    setSelected('new');
  }

  const role = typeof selected === 'number' ? matrix.roles.find((r) => r.id === selected) : undefined;
  const needle = search.trim().toLowerCase();
  const roles = matrix.roles.filter((r) => needle === '' || r.name.toLowerCase().includes(needle) || r.code.toLowerCase().includes(needle));

  return (
    <div className="roles-split">
      <section className="card role-list" aria-label={t('config.tab.roles')}>
        <input className="search" type="search" placeholder={t('role.search')} aria-label={t('role.search')} value={search}
          onChange={(e) => setSearch(e.target.value)} />
        <ul>
          {roles.map((r) => (
            <li key={r.id}>
              <button type="button" className={r.id === selected ? 'current' : ''} aria-current={r.id === selected ? 'true' : undefined}
                onClick={() => { setCopyOf(undefined); setSelected(r.id); }}>
                <span>{r.name}</span>
                <span className="muted small">
                  {r.isSystem ? t('role.systemUsers', { count: r.userCount }) : t('role.userCount', { count: r.userCount })}
                </span>
              </button>
            </li>
          ))}
        </ul>
        <p className="muted small">{t('role.systemHint')}</p>
      </section>
      <section className="card role-detail">
        {selected === 'new' || role !== undefined ? (
          <RoleEditor key={role === undefined ? `new-${copyOf?.id ?? 0}-${creating}` : `${role.id}-${role.rowVersion}`}
            role={selected === 'new' ? undefined : role} copyOf={copyOf} permissions={matrix.permissions}
            onSaved={(id) => { setCopyOf(undefined); setSelected(id); }}
            onCopy={(r) => { setCopyOf(r); setSelected('new'); }}
            onDeleted={() => setSelected(undefined)} />
        ) : (
          <p className="muted empty-detail">{t('role.empty')}</p>
        )}
      </section>
    </div>
  );
}

function RoleEditor({ role, copyOf, permissions, onSaved, onCopy, onDeleted }: {
  readonly role: Role | undefined;
  readonly copyOf: Role | undefined;
  readonly permissions: readonly Permission[];
  readonly onSaved: (id: number) => void;
  readonly onCopy: (role: Role) => void;
  readonly onDeleted: () => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const source = role ?? copyOf;
  const initial = useMemo(() => ({
    code: role?.code ?? '',
    name: role?.name ?? (copyOf === undefined ? '' : `${copyOf.name} ${t('role.copySuffix')}`),
    description: source?.description ?? '',
    permissionIds: new Set(source?.hasAllPermissions ? permissions.map((p) => p.id) : source?.permissionIds ?? []),
  }), [role, copyOf, source, permissions]);
  const [code, setCode] = useState(initial.code);
  const [name, setName] = useState(initial.name);
  const [description, setDescription] = useState(initial.description);
  const [checked, setChecked] = useState<ReadonlySet<number>>(initial.permissionIds);
  const [search, setSearch] = useState('');
  const [onlyChecked, setOnlyChecked] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const readOnly = role?.hasAllPermissions === true;
  const dirty = !readOnly && (code !== initial.code || name !== initial.name || description !== initial.description
    || checked.size !== initial.permissionIds.size || [...checked].some((id) => !initial.permissionIds.has(id)) || role === undefined);
  const guard = useUnsavedChanges(dirty && (role !== undefined || code !== '' || name !== '' || checked.size > 0));

  const groups = groupPermissions(permissions);
  const needle = search.trim().toLowerCase();
  const visible = (resource: string, list: readonly Permission[]) =>
    (!onlyChecked || list.some((p) => checked.has(p.id)))
    && (needle === '' || resourceLabel(resource).toLowerCase().includes(needle) || resource.toLowerCase().includes(needle)
      || list.some((p) => p.description.toLowerCase().includes(needle)));

  const flip = (id: number) => setChecked((current) => {
    const next = new Set(current);
    if (!next.delete(id)) {
      next.add(id);
    }
    return next;
  });

  const save = async () => {
    setError(undefined);
    if (code.trim() === '' || name.trim() === '') {
      setError(t('common.required'));
      return;
    }
    setBusy(true);
    try {
      const saved = await roleApi.save({
        id: role?.id ?? null, code: code.trim(), name: name.trim(), description: description.trim() === '' ? null : description.trim(),
        permissionIds: [...checked], rowVersion: role?.rowVersion ?? null,
      });
      guard.markClean();
      await Promise.all([queryClient.invalidateQueries({ queryKey: roleKeys.all }), queryClient.invalidateQueries({ queryKey: ['core', 'me'] })]);
      notify(t('common.saved'));
      onSaved(saved.id);
    } catch (e) {
      const message = problem(e);
      setError(message);
      notify(message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (role === undefined) {
      return;
    }
    setBusy(true);
    try {
      await roleApi.remove(role.id, role.rowVersion);
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: roleKeys.all });
      notify(t('role.deleted'));
      onDeleted();
    } catch (e) {
      const message = problem(e);
      setError(message);
      setConfirmDelete(false);
      notify(message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="role-editor">
      <div className="role-editor-head">
        <h2>{role?.name ?? t('role.newTitle')}</h2>
        {role !== undefined && <span className="pill">{role.isSystem ? t('role.systemRole') : t('role.clientRole')}</span>}
        {role !== undefined && <span className="muted">· {t('role.assigned', { count: role.userCount })}</span>}
      </div>
      <ErrorBanner message={error} />
      {readOnly ? (
        <p className="banner dashed small">{t('role.adminHint')}</p>
      ) : (
        <div className="grid g3 role-fields">
          <label className="field">
            <span className="label">{t('field.code')}</span>
            <input className="cell-input" value={code} disabled={role?.isSystem === true} maxLength={50} onChange={(e) => setCode(e.target.value.toUpperCase())} />
          </label>
          <label className="field">
            <span className="label">{t('field.name')}</span>
            <input className="cell-input" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} />
          </label>
          <label className="field">
            <span className="label">{t('role.description')}</span>
            <input className="cell-input" value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
          </label>
        </div>
      )}
      <div className="role-filter">
        <input className="search" type="search" placeholder={t('role.permSearch')} aria-label={t('role.permSearch')} value={search}
          onChange={(e) => setSearch(e.target.value)} />
        <button type="button" className={onlyChecked ? 'chip on' : 'chip'} aria-pressed={onlyChecked} onClick={() => setOnlyChecked((v) => !v)}>
          {t('role.onlyChecked')}
        </button>
      </div>
      <div className="matrix-scroll">
        <table className="simple perm-table">
          <thead>
            <tr>
              <th scope="col">{t('role.col.resource')}</th>
              {STANDARD_ACTIONS.map((a) => <th key={a} scope="col" className="c">{actionLabel(a)}</th>)}
              <th scope="col">{t('role.col.other')}</th>
            </tr>
          </thead>
          <tbody>
            {groups.map((group) => {
              const resources = group.resources.filter((r) => visible(r.code, r.permissions));
              return resources.length === 0 ? null : (
                <Fragment key={group.module}>
                  <tr className="section-row"><th colSpan={STANDARD_ACTIONS.length + 2} scope="colgroup">{moduleName(group.module)}</th></tr>
                  {resources.map((resource) => {
                    const others = resource.permissions.filter((p) => !(STANDARD_ACTIONS as readonly string[]).includes(splitCode(p.code).action));
                    return (
                      <tr key={resource.code}>
                        <th scope="row" className="resource">
                          {resourceLabel(resource.code)}
                          <code>{resource.code}</code>
                        </th>
                        {STANDARD_ACTIONS.map((action) => {
                          const p = resource.permissions.find((x) => splitCode(x.code).action === action);
                          return (
                            <td key={action} className="c">
                              {p === undefined ? <span className="na" aria-hidden="true" /> : (
                                <input type="checkbox" checked={checked.has(p.id)} disabled={readOnly} title={p.description}
                                  aria-label={`${resourceLabel(resource.code)} · ${actionLabel(action)}`} onChange={() => flip(p.id)} />
                              )}
                            </td>
                          );
                        })}
                        <td className="others">
                          <div>
                          {others.map((p) => (
                            <label key={p.id} className="other-perm" title={p.description}>
                              <input type="checkbox" checked={checked.has(p.id)} disabled={readOnly} onChange={() => flip(p.id)} />
                              {actionLabel(splitCode(p.code).action)}
                            </label>
                          ))}
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </Fragment>
              );
            })}
          </tbody>
        </table>
        {groups.every((g) => g.resources.every((r) => !visible(r.code, r.permissions))) && <p className="muted">{t('role.noPermissions')}</p>}
      </div>
      <div className="form-footer">
        <span className="muted small legend-na"><span className="na" aria-hidden="true" /> {t('role.naHint')}</span>
        <div className="actions">
          {role !== undefined && !role.isSystem && (
            confirmDelete ? (
              <button type="button" className="btn danger" disabled={busy} onClick={() => void remove()}>{t('role.confirmDelete')}</button>
            ) : (
              <button type="button" className="btn ghost" disabled={busy} onClick={() => setConfirmDelete(true)}>{t('role.delete')}</button>
            )
          )}
          {role !== undefined && <button type="button" className="btn ghost" disabled={busy} onClick={() => onCopy(role)}>{t('role.copy')}</button>}
          {!readOnly && <button type="button" className="btn primary" disabled={busy || !dirty} onClick={() => void save()}>{t('common.save')}</button>}
        </div>
      </div>
    </div>
  );
}

// ---------- Roluri și utilizatori ----------

interface Column {
  readonly permission: Permission;
  readonly first: boolean;
  readonly moduleFirst: boolean;
}

function columnsOf(groups: readonly ModuleGroup[]): Column[] {
  return groups.flatMap((g, gi) => g.resources.flatMap((r, ri) => r.permissions.map((permission, pi) => ({
    permission, first: pi === 0, moduleFirst: gi > 0 && ri === 0 && pi === 0,
  }))));
}

/**
 * Rolurile cu utilizatorii lor si exceptiile. readOnly (fisa firmei): firma e fixa, fara clic pe celule si fara salvare.
 */
export function RolesByUser({ matrix, companyId, onCompany, currentUserId, readOnly = false }: {
  readonly matrix: RoleMatrix;
  readonly companyId: number | null;
  readonly onCompany?: (id: number | null) => void;
  readonly currentUserId: number;
  readonly readOnly?: boolean;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const allGroups = useMemo(() => groupPermissions(matrix.permissions), [matrix.permissions]);
  const [module, setModule] = useState<string>(allGroups[0]?.module ?? '');
  const [search, setSearch] = useState('');
  const [onlyOverrides, setOnlyOverrides] = useState(false);
  const [collapsed, setCollapsed] = useState<ReadonlySet<number>>(new Set());
  const [drafts, setDrafts] = useState<ReadonlyMap<number, ReadonlyMap<number, DraftOverride>>>(new Map());
  const [status, setStatus] = useState<string>(t('role.hint.users'));
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const guard = useUnsavedChanges(drafts.size > 0);

  const groups = allGroups.filter((g) => module === '' || g.module === module);
  const columns = columnsOf(groups);
  const adminRoles = new Set(matrix.roles.filter((r) => r.hasAllPermissions).map((r) => r.id));
  const rolePermissions = useMemo(() => new Map(matrix.roles.map((r) => [r.id, new Set(r.permissionIds)])), [matrix.roles]);
  const usersById = new Map(matrix.users.map((u) => [u.id, u]));

  const isAdmin = (u: MatrixUser) => u.roleIds.some((id) => adminRoles.has(id));
  const inherited = (u: MatrixUser, permissionId: number) => isAdmin(u) || u.roleIds.some((id) => rolePermissions.get(id)?.has(permissionId) === true);
  const serverDraft = (u: MatrixUser) => localOverrides(matrix.overrides, u.id, companyId);
  const draftOf = (u: MatrixUser) => drafts.get(u.id) ?? serverDraft(u);
  const overrideCount = (u: MatrixUser) => draftOf(u).size + globalOverrides(matrix.overrides, u.id, companyId).size;

  const stateOf = (u: MatrixUser, permissionId: number): CellState =>
    cellState(inherited(u, permissionId), draftOf(u).get(permissionId)?.effect, globalOverrides(matrix.overrides, u.id, companyId).get(permissionId));

  const click = (u: MatrixUser, p: Permission) => {
    if (readOnly) {
      return;
    }
    if (isAdmin(u)) {
      setStatus(t('role.lock.admin'));
      return;
    }
    if (u.id === currentUserId) {
      setStatus(t('role.lock.self'));
      return;
    }
    const next = toggle(stateOf(u, p.id));
    if (next === undefined) {
      setStatus(t('role.lock.global'));
      return;
    }
    const draft = new Map(draftOf(u));
    if (next === null) {
      draft.delete(p.id);
    } else {
      const before = serverDraft(u).get(p.id);
      draft.set(p.id, { effect: next, reason: before?.effect === next ? before.reason : '' });
    }
    setDrafts((current) => {
      const all = new Map(current);
      if (sameDraft(draft, serverDraft(u))) {
        all.delete(u.id);
      } else {
        all.set(u.id, draft);
      }
      return all;
    });
    const what = next === null ? t('role.pending.inherit') : next === 'G' ? t('role.pending.grant') : t('role.pending.revoke');
    setStatus(`${u.displayName} · ${resourceLabel(splitCode(p.code).resource)} · ${actionLabel(splitCode(p.code).action)}: ${what}`);
  };

  const setReason = (userId: number, permissionId: number, reason: string) => setDrafts((current) => {
    const draft = current.get(userId);
    const entry = draft?.get(permissionId);
    if (draft === undefined || entry === undefined) {
      return current;
    }
    return new Map(current).set(userId, new Map(draft).set(permissionId, { ...entry, reason }));
  });

  const save = async () => {
    setError(undefined);
    const missing = [...drafts.values()].some((d) => [...d.values()].some((o) => o.effect === 'G' && o.reason.trim() === ''));
    if (missing) {
      setError(t('role.pending.reasonRequired'));
      return;
    }
    setBusy(true);
    const remaining = new Map(drafts);
    try {
      for (const [userId, draft] of drafts) {
        try {
          await roleApi.saveOverrides(userId, companyId, toInput(draft));
          remaining.delete(userId);
        } catch (e) {
          throw new Error(t('role.userFailed', { user: usersById.get(userId)?.displayName ?? String(userId), message: problem(e) }), { cause: e });
        }
      }
      guard.markClean();
      notify(t('role.savedOverrides'));
    } catch (e) {
      const message = problem(e);
      setError(message);
      notify(message, 'error');
    } finally {
      setDrafts(remaining);
      setBusy(false);
      await queryClient.invalidateQueries({ queryKey: roleKeys.all });
    }
  };

  const needle = search.trim().toLowerCase();
  const matchesUser = (u: MatrixUser) => (!onlyOverrides || overrideCount(u) > 0)
    && (needle === '' || u.displayName.toLowerCase().includes(needle) || u.userName.toLowerCase().includes(needle));
  const rows = matrix.roles.map((role) => {
    const members = matrix.users.filter((u) => u.roleIds.includes(role.id));
    const roleMatches = needle === '' || role.name.toLowerCase().includes(needle);
    const shown = members.filter((u) => matchesUser(u) || (roleMatches && !onlyOverrides && needle !== ''));
    return { role, members: shown, visible: members.length > 0 && (shown.length > 0 || (roleMatches && !onlyOverrides)) };
  }).filter((r) => r.visible);
  const allCollapsed = rows.length > 0 && rows.every((r) => collapsed.has(r.role.id));
  const roleNames = new Map(matrix.roles.map((r) => [r.id, r.name]));

  const pending = [...drafts].flatMap(([userId, draft]) => {
    const u = usersById.get(userId);
    if (u === undefined) {
      return [];
    }
    const server = serverDraft(u);
    const ids = new Set([...draft.keys(), ...server.keys()]);
    return [...ids].flatMap((permissionId) => {
      const now = draft.get(permissionId);
      const before = server.get(permissionId);
      if (now?.effect === before?.effect && (now === undefined || now.reason.trim() === before?.reason.trim())) {
        return [];
      }
      const p = matrix.permissions.find((x) => x.id === permissionId);
      return [{ u, permissionId, now, code: p?.code ?? String(permissionId) }];
    });
  });

  return (
    <section className="card user-matrix">
      <div className="matrix-toolbar">
        {onCompany !== undefined && (
          <label className="field inline">
            <span className="label">{t('role.company')}</span>
            <select className="cell-input" value={companyId ?? ''} disabled={drafts.size > 0} title={drafts.size > 0 ? t('role.lock.company') : undefined}
              onChange={(e) => onCompany(e.target.value === '' ? null : Number(e.target.value))}>
              <option value="">{t('role.allCompanies')}</option>
              {matrix.companies.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </label>
        )}
        <label className="field inline">
          <span className="label">{t('role.module')}</span>
          <select className="cell-input" value={module} onChange={(e) => setModule(e.target.value)}>
            <option value="">{t('role.allModules')}</option>
            {allGroups.map((g) => <option key={g.module} value={g.module}>{moduleName(g.module)}</option>)}
          </select>
        </label>
        <input className="search" type="search" placeholder={t('role.userSearch')} aria-label={t('role.userSearch')} value={search}
          onChange={(e) => setSearch(e.target.value)} />
        <button type="button" className={onlyOverrides ? 'chip on' : 'chip'} aria-pressed={onlyOverrides} onClick={() => setOnlyOverrides((v) => !v)}>
          {t('role.onlyOverrides')}
        </button>
        <button type="button" className="chip" onClick={() => setCollapsed(allCollapsed ? new Set() : new Set(rows.map((r) => r.role.id)))}>
          {allCollapsed ? t('role.expandAll') : t('role.collapseAll')}
        </button>
      </div>
      {companyId === null && <p className="muted small">{t('role.hint.allCompanies')}</p>}
      <ErrorBanner message={error} />
      {rows.length === 0 ? (
        <p className="muted">{t('role.noUsers')}</p>
      ) : (
        <div className="matrix-scroll">
          <table className="user-perm-matrix">
            <thead>
              <tr className="mod">
                <th scope="col" rowSpan={3} className="name">{t('role.col.roleUser')}</th>
                {groups.map((g, gi) => (
                  <th key={g.module} scope="colgroup" colSpan={g.resources.reduce((n, r) => n + r.permissions.length, 0)} className={gi > 0 ? 'sep' : ''}>
                    {moduleName(g.module)}
                  </th>
                ))}
              </tr>
              <tr className="res">
                {groups.flatMap((g, gi) => g.resources.map((r, ri) => (
                  <th key={r.code} scope="colgroup" colSpan={r.permissions.length} className={gi > 0 && ri === 0 ? 'sep' : 'rsep'}>
                    {resourceLabel(r.code)}
                  </th>
                )))}
              </tr>
              <tr className="act">
                {columns.map((c) => (
                  <th key={c.permission.id} scope="col" className={c.moduleFirst ? 'sep' : c.first ? 'rsep' : ''} title={c.permission.description}>
                    {actionShort(splitCode(c.permission.code).action)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map(({ role, members }) => {
                const open = !collapsed.has(role.id);
                return (
                  <Fragment key={role.id}>
                    <tr className="matrix-role-row">
                      <th scope="row" className="name">
                        <button type="button" className="twisty" aria-expanded={open}
                          aria-label={open ? t('role.collapse', { name: role.name }) : t('role.expand', { name: role.name })}
                          onClick={() => setCollapsed((current) => {
                            const next = new Set(current);
                            if (!next.delete(role.id)) {
                              next.add(role.id);
                            }
                            return next;
                          })}>
                          {open ? '▾' : '▸'}
                        </button>
                        {role.name}
                        <span className="pill">{role.isSystem ? t('role.systemUsers', { count: role.userCount }) : t('role.userCount', { count: role.userCount })}</span>
                      </th>
                      {columns.map((c) => {
                        const on = role.hasAllPermissions || rolePermissions.get(role.id)?.has(c.permission.id) === true;
                        return (
                          <td key={c.permission.id} className={c.moduleFirst ? 'sep' : c.first ? 'rsep' : ''}>
                            <span className={on ? 'cell role-on' : 'cell'} role="img"
                              aria-label={`${role.name} · ${c.permission.code}: ${on ? t('role.legend.role') : t('role.legend.none')}`}>
                              {on ? '✓' : ''}
                            </span>
                          </td>
                        );
                      })}
                    </tr>
                    {open && members.map((u) => {
                      const others = u.roleIds.filter((id) => id !== role.id).map((id) => roleNames.get(id)).filter((n) => n !== undefined);
                      const nodes = u.assignments.filter((a) => a.roleId === role.id).map(nodeName);
                      const count = overrideCount(u);
                      const locked = isAdmin(u) || u.id === currentUserId;
                      const edited = drafts.get(u.id);
                      const server = serverDraft(u);
                      return (
                        <tr key={u.id} className={u.isActive ? 'matrix-user-row' : 'matrix-user-row inactive'}>
                          <th scope="row" className="name">
                            <span className="user-name">
                              {u.displayName}
                              {count > 0 && <span className="pill info">{count === 1 ? t('role.overrideOne') : t('role.overrideMany', { count })}</span>}
                              {!u.isActive && <span className="pill off">{t('role.inactive')}</span>}
                            </span>
                            <span className="muted small">
                              {u.userName}{nodes.length > 0 ? ` · ${t('role.on', { nodes: nodes.join(', ') })}` : ''}
                              {others.length > 0 ? ` · ${t('role.alsoIn', { roles: others.join(', ') })}` : ''}
                            </span>
                          </th>
                          {columns.map((c) => {
                            const s = stateOf(u, c.permission.id);
                            const changed = edited !== undefined && edited.get(c.permission.id)?.effect !== server.get(c.permission.id)?.effect;
                            const kind = s.local === 'G' ? 'grant' : s.local === 'R' ? 'revoke'
                              : s.global === 'G' ? 'grant global' : s.global === 'R' ? 'revoke global' : s.inherited ? 'inherited' : '';
                            const symbol = s.local === 'G' || s.global === 'G' ? '+' : s.local === 'R' || s.global === 'R' ? '−' : s.inherited ? '✓' : '';
                            const text = s.local === 'G' || s.global === 'G' ? t('role.state.grant') : s.local === 'R' || s.global === 'R'
                              ? t('role.state.revoke') : s.inherited ? t('role.state.inherited') : t('role.state.none');
                            return (
                              <td key={c.permission.id} className={c.moduleFirst ? 'sep' : c.first ? 'rsep' : ''}>
                                <button type="button"
                                  className={['cell', kind, s.redundant ? 'redundant' : '', changed ? 'changed' : '', locked || s.global !== undefined ? 'locked' : ''].join(' ').trim()}
                                  aria-label={`${u.displayName} · ${resourceLabel(splitCode(c.permission.code).resource)} · ${actionLabel(splitCode(c.permission.code).action)}: ${text}${s.redundant ? ` (${t('role.state.redundant')})` : ''}`}
                                  title={s.global !== undefined ? t('role.lock.global') : s.redundant ? t('role.state.redundant') : c.permission.description}
                                  disabled={busy || readOnly}
                                  onClick={() => click(u, c.permission)}>
                                  {symbol}
                                </button>
                              </td>
                            );
                          })}
                        </tr>
                      );
                    })}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      <div className="matrix-legend" aria-hidden="true">
        <span><span className="cell role-on">✓</span> {t('role.legend.role')}</span>
        <span><span className="cell inherited">✓</span> {t('role.legend.inherited')}</span>
        <span><span className="cell grant">+</span> {t('role.legend.grant')}</span>
        <span><span className="cell revoke">−</span> {t('role.legend.revoke')}</span>
        <span><span className="cell grant global">+</span> {t('role.legend.global')}</span>
        <span><span className="cell grant redundant">+</span> {t('role.legend.redundant')}</span>
        <span><span className="cell" /> {t('role.legend.none')}</span>
      </div>
      {readOnly ? <p className="muted small">{t('role.hint.readOnly')}</p> : <p className="matrix-status" aria-live="polite">{status}</p>}
      {pending.length > 0 && (
        <div className="pending-changes">
          <h3 className="section">{t('role.pending.title')}</h3>
          <table className="simple">
            <tbody>
              {pending.map(({ u, permissionId, now, code }) => (
                <tr key={`${u.id}-${permissionId}`}>
                  <td>{u.displayName}</td>
                  <td>{resourceLabel(splitCode(code).resource)} · {actionLabel(splitCode(code).action)}</td>
                  <td>{now === undefined ? t('role.pending.inherit') : now.effect === 'G' ? t('role.pending.grant') : t('role.pending.revoke')}</td>
                  <td className="reason">
                    {now !== undefined && (
                      <input className="cell-input" placeholder={t('role.pending.reason')} aria-label={`${t('role.pending.reason')} · ${u.displayName} · ${code}`}
                        aria-invalid={now.effect === 'G' && now.reason.trim() === ''} maxLength={500} value={now.reason}
                        onChange={(e) => setReason(u.id, permissionId, e.target.value)} />
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="actions">
            <button type="button" className="btn ghost" disabled={busy} onClick={() => { setDrafts(new Map()); setError(undefined); guard.markClean(); }}>
              {t('common.cancel')}
            </button>
            <button type="button" className="btn primary" disabled={busy} onClick={() => void save()}>{t('common.save')}</button>
          </div>
        </div>
      )}
    </section>
  );
}
