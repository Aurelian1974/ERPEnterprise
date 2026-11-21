import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { t } from '../i18n';
import { shortDay } from '../rates/ratesModel';
import { dimensionKeys, dimensionsApi, parentOptions, useDimensions, type DimensionPage, type DimensionValue } from './dimensionsApi';

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

/**
 * Configurare › Dimensiuni (02.01, 3.5; macheta 07k): tipurile fixe in stanga, valorile tipului ales (cautare, firma, inactive) si
 * fisa valorii (cod, denumire, firma, parinte, responsabil, valabilitate). Dezactivarea inlocuieste stergerea.
 */
export function DimensionsTab({ user }: { readonly user: CurrentUser }) {
  const [type, setType] = useState('COST_CENTER');
  const [companyId, setCompanyId] = useState<number | undefined>();
  const [search, setSearch] = useState('');
  const [includeInactive, setIncludeInactive] = useState(false);
  const [selected, setSelected] = useState<number | 'new' | undefined>();
  const query = useDimensions({ type, companyId, search, includeInactive });
  const data = query.data;
  const current = typeof selected === 'number' ? data?.values.find((v) => v.id === selected) : undefined;
  const typeName = data?.types.find((x) => x.code === type)?.name ?? '';

  return (
    <div className="dimensions-layout">
      <section className="card dimension-types" aria-label={t('dim.types')}>
        <span className="eyebrow">{t('dim.types')}</span>
        <ul>
          {(data?.types ?? []).map((x) => (
            <li key={x.code}>
              <button type="button" className={x.code === type ? 'current' : ''} aria-current={x.code === type ? 'true' : undefined}
                onClick={() => { setType(x.code); setSelected(undefined); }}>
                <span>{x.name}</span>
                <span className="muted small">{x.valueCount}</span>
              </button>
            </li>
          ))}
        </ul>
        <p className="muted small">{t('dim.typesHint')}</p>
      </section>

      <section className="card dimension-values" aria-labelledby="dim-values-title">
        <div className="card-head">
          <h2 id="dim-values-title">{typeName}</h2>
          <button type="button" className="btn primary" onClick={() => setSelected('new')}>{t('dim.new')}</button>
        </div>
        <div className="role-filter">
          <input className="search" type="search" placeholder={t('dim.search')} aria-label={t('dim.search')} value={search}
            onChange={(e) => setSearch(e.target.value)} />
          <select className={companyId === undefined ? 'chip' : 'chip on'} aria-label={t('dim.company')} value={companyId ?? ''}
            onChange={(e) => setCompanyId(e.target.value === '' ? undefined : Number(e.target.value))}>
            <option value="">{t('dim.allCompanies')}</option>
            {(data?.companies ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
          <button type="button" className={includeInactive ? 'chip on' : 'chip'} aria-pressed={includeInactive} onClick={() => setIncludeInactive((v) => !v)}>
            {t('dim.inactive')}
          </button>
        </div>
        {query.isError && <ErrorBanner message={problem(query.error)} />}
        <div className="matrix-scroll">
          <table className="simple dim-table">
            <thead>
              <tr>
                <th scope="col">{t('field.code')}</th>
                <th scope="col">{t('field.name')}</th>
                <th scope="col">{t('dim.company')}</th>
                <th scope="col">{t('dim.parent')}</th>
                <th scope="col">{t('dim.state')}</th>
              </tr>
            </thead>
            <tbody>
              {data !== undefined && data.values.length === 0 && (
                <tr><td colSpan={5} className="muted">{t('dim.empty')}</td></tr>
              )}
              {(data?.values ?? []).map((v) => (
                <tr key={v.id} className={v.id === selected ? 'current' : ''} onClick={() => setSelected(v.id)}>
                  <td><button type="button" className="row-link" aria-current={v.id === selected ? 'true' : undefined}
                    onClick={(e) => { e.stopPropagation(); setSelected(v.id); }}>{v.code}</button></td>
                  <td>{v.name}</td>
                  <td>{v.companyName}</td>
                  <td className="muted">{v.parentCode ?? '—'}</td>
                  <td><span className={v.isActive ? 'pill ok' : 'pill off'}>{v.isActive ? t('common.active') : t('dim.inactiveOne')}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="muted small">{t('dim.count', { count: data?.values.length ?? 0 })}</p>
      </section>

      <section className="card dimension-detail">
        {data !== undefined && (selected === 'new' || current !== undefined) ? (
          <DimensionForm key={selected === 'new' ? `new-${type}` : `${current?.id}-${current?.rowVersion}`} page={data} type={type} value={current}
            defaultCompany={companyId ?? (user.companyId > 0 ? user.companyId : data.companies[0]?.id)} onDone={(id) => setSelected(id)} />
        ) : (
          <p className="muted empty-detail">{t('dim.select')}</p>
        )}
      </section>
    </div>
  );
}

function DimensionForm({ page, type, value, defaultCompany, onDone }: {
  readonly page: DimensionPage;
  readonly type: string;
  readonly value: DimensionValue | undefined;
  readonly defaultCompany: number | undefined;
  readonly onDone: (id: number | undefined) => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [companyId, setCompanyId] = useState(value?.companyId ?? defaultCompany ?? 0);
  const [code, setCode] = useState(value?.code ?? '');
  const [name, setName] = useState(value?.name ?? '');
  const [parentId, setParentId] = useState<number | null>(value?.parentId ?? null);
  const [responsible, setResponsible] = useState<number | null>(value?.responsibleUserId ?? null);
  const [validFrom, setValidFrom] = useState(value?.validFrom ?? '');
  const [validTo, setValidTo] = useState(value?.validTo ?? '');
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const dirty = value === undefined
    ? code !== '' || name !== ''
    : code !== value.code || name !== value.name || parentId !== (value.parentId ?? null) || responsible !== (value.responsibleUserId ?? null)
      || validFrom !== (value.validFrom ?? '') || validTo !== (value.validTo ?? '');
  const guard = useUnsavedChanges(dirty);

  const save = async (isActive: boolean) => {
    setError(undefined);
    setBusy(true);
    try {
      const saved = await dimensionsApi.save({
        id: value?.id ?? null, companyId, dimensionType: value?.dimensionType ?? type, code: code.trim(), name: name.trim(), parentId,
        responsibleUserId: responsible, validFrom: validFrom === '' ? null : validFrom, validTo: validTo === '' ? null : validTo, isActive,
        rowVersion: value?.rowVersion ?? null,
      });
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: dimensionKeys.all });
      notify(isActive ? t('common.saved') : t('dim.deactivated'));
      onDone(saved.id);
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="role-editor">
      <div className="role-editor-head">
        <h2>{value?.code ?? t('dim.newTitle')}</h2>
        {value !== undefined && <span className={value.isActive ? 'pill ok' : 'pill off'}>{value.isActive ? t('common.active') : t('dim.inactiveOne')}</span>}
      </div>
      <ErrorBanner message={error} />
      <label className="field">
        <span className="label">{t('field.code')}</span>
        <input className="cell-input" value={code} maxLength={30} onChange={(e) => setCode(e.target.value.toUpperCase())} />
      </label>
      <label className="field">
        <span className="label">{t('field.name')}</span>
        <input className="cell-input" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} />
      </label>
      <label className="field">
        <span className="label">{t('dim.company')}</span>
        <select className="cell-input" value={companyId} disabled={value !== undefined} title={value === undefined ? undefined : t('dim.companyLocked')}
          onChange={(e) => { setCompanyId(Number(e.target.value)); setParentId(null); }}>
          {value !== undefined && !page.companies.some((c) => c.id === value.companyId) && <option value={value.companyId}>{value.companyName}</option>}
          {page.companies.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
      </label>
      <label className="field">
        <span className="label">{t('dim.parentLong')}</span>
        <select className="cell-input" value={parentId ?? ''} onChange={(e) => setParentId(e.target.value === '' ? null : Number(e.target.value))}>
          <option value="">{t('dim.noParent')}</option>
          {parentOptions(page.values, companyId, value?.id).map((v) => <option key={v.id} value={v.id}>{v.code} · {v.name}</option>)}
        </select>
      </label>
      <label className="field">
        <span className="label">{t('dim.responsible')}</span>
        <select className="cell-input" value={responsible ?? ''} onChange={(e) => setResponsible(e.target.value === '' ? null : Number(e.target.value))}>
          <option value="">{t('dim.noResponsible')}</option>
          {page.users.map((u) => <option key={u.id} value={u.id}>{u.displayName}</option>)}
        </select>
      </label>
      <div className="grid g2">
        <label className="field">
          <span className="label">{t('dim.validFrom')}</span>
          <input className="cell-input" type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} />
        </label>
        <label className="field">
          <span className="label">{t('dim.validTo')}</span>
          <input className="cell-input" type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} />
        </label>
      </div>
      {value?.validFrom != null && (
        <p className="muted small">{t('dim.validity', { from: shortDay(value.validFrom), to: value.validTo == null ? t('dim.noEnd') : shortDay(value.validTo) })}</p>
      )}
      <p className="banner dashed small">{t('dim.hint')}</p>
      <div className="form-footer">
        <span className="muted small">{value !== undefined && value.activeChildren > 0 ? t('dim.children', { count: value.activeChildren }) : ''}</span>
        <div className="actions">
          {value !== undefined && (
            <button type="button" className="btn ghost" disabled={busy} onClick={() => void save(!value.isActive)}>
              {value.isActive ? t('dim.deactivate') : t('dim.activate')}
            </button>
          )}
          <button type="button" className="btn primary" disabled={busy || !dirty} onClick={() => void save(value?.isActive ?? true)}>{t('common.save')}</button>
        </div>
      </div>
    </div>
  );
}
