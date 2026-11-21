import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { dimensionKeys, useDimensions, type DimensionPage } from './dimensionsApi';

type DimensionType = components['schemas']['DimensionTypeDto'];

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

/** Tipul de dimensiune nou sau ales (3.6 punctul 15): codul e fix dupa creare; tipurile livrate nu se dezactiveaza. */
export function DimensionTypeForm({ type, onDone }: { readonly type: DimensionType | undefined; readonly onDone: (code: string | undefined) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [code, setCode] = useState(type?.code ?? '');
  const [name, setName] = useState(type?.name ?? '');
  const [order, setOrder] = useState(String(type?.sortOrder ?? 100));
  const [isActive, setIsActive] = useState(type?.isActive ?? true);
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);

  const save = async () => {
    setError(undefined);
    setBusy(true);
    try {
      const body = { code, name: name.trim(), sortOrder: Number(order), isActive, isNew: type === undefined, rowVersion: type?.rowVersion ?? null };
      if (type === undefined) {
        unwrap(await api.POST('/api/core/dimension-types', { body }));
      } else {
        unwrap(await api.PUT('/api/core/dimension-types/{code}', { params: { path: { code: type.code } }, body }));
      }
      await queryClient.invalidateQueries({ queryKey: dimensionKeys.all });
      notify(t('common.saved'));
      onDone(code);
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="subform dimension-type-form" noValidate onSubmit={(e) => { e.preventDefault(); void save(); }} aria-label={t('dimType.edit')}>
      <ErrorBanner message={error} />
      <label className="field">
        <span className="label">{t('field.code')}</span>
        <input className="cell-input" value={code} maxLength={20} disabled={type !== undefined} onChange={(e) => setCode(e.target.value.toUpperCase())} />
      </label>
      <label className="field">
        <span className="label">{t('field.name')}</span>
        <input className="cell-input" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
      </label>
      <label className="field">
        <span className="label">{t('dimType.order')}</span>
        <input className="cell-input num" type="number" min={0} max={9999} value={order} onChange={(e) => setOrder(e.target.value)} />
      </label>
      <label className="check">
        <input type="checkbox" checked={isActive} disabled={type?.isSystem === true} onChange={(e) => setIsActive(e.target.checked)} />
        {t('field.isActive')}
      </label>
      {type?.isSystem === true && <p className="muted small">{t('dimType.system')}</p>}
      <div className="form-footer">
        <button type="button" className="btn ghost small" onClick={() => onDone(undefined)}>{t('common.cancel')}</button>
        <button type="submit" className="btn primary small" disabled={busy || code === '' || name.trim() === ''}>{t('common.save')}</button>
      </div>
    </form>
  );
}

const combinationsKey = ['core', 'dimension-combinations'] as const;

/**
 * Combinatiile blocate ale firmei curente (3.6 punctul 14): doua tipuri care nu se folosesc impreuna pe acelasi document sau linie,
 * ori o pereche de valori (de exemplu un proiect doar cu centrele de cost ale lui).
 */
export function DimensionCombinations({ page, companyId }: { readonly page: DimensionPage; readonly companyId: number }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const query = useQuery({
    queryKey: combinationsKey,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/dimension-combinations', { signal })),
  });
  const types = page.types.filter((x) => x.isActive);
  const [typeA, setTypeA] = useState(types[0]?.code ?? '');
  const [typeB, setTypeB] = useState(types[1]?.code ?? '');
  const [valueA, setValueA] = useState<number | null>(null);
  const [valueB, setValueB] = useState<number | null>(null);
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  // Valorile active ale firmei curente pentru fiecare tip ales.
  const valuesA = useDimensions({ type: typeA, companyId, search: '', includeInactive: false }).data?.values ?? [];
  const valuesB = useDimensions({ type: typeB, companyId, search: '', includeInactive: false }).data?.values ?? [];

  const run = async (action: () => Promise<unknown>, message: string) => {
    setError(undefined);
    setBusy(true);
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: combinationsKey });
      notify(message);
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card dimension-combinations" aria-labelledby="dim-comb-title">
      <div className="card-head"><h2 id="dim-comb-title">{t('dimComb.title')}</h2></div>
      <p className="muted small">{t('dimComb.hint')}</p>
      <ErrorBanner message={error ?? (query.isError ? problem(query.error) : undefined)} />
      {query.data !== undefined && query.data.length > 0 && (
        <table className="simple">
          <thead>
            <tr><th scope="col">{t('dimComb.first')}</th><th scope="col">{t('dimComb.second')}</th><th scope="col">{t('dimComb.note')}</th><th scope="col" aria-label={t('dimComb.unblock')} /></tr>
          </thead>
          <tbody>
            {query.data.map((c) => (
              <tr key={c.id}>
                <td>{c.typeAName}{c.valueACode == null ? '' : `: ${c.valueACode}`}</td>
                <td>{c.typeBName}{c.valueBCode == null ? '' : `: ${c.valueBCode}`}</td>
                <td className="muted">{c.note ?? (c.valueAId == null ? t('dimComb.typesTogether') : '')}</td>
                <td>
                  <button type="button" className="btn ghost small" disabled={busy}
                    onClick={() => void run(async () => unwrap(await api.DELETE('/api/core/dimension-combinations/{id}', { params: { path: { id: c.id } } })), t('dimComb.unblocked'))}>
                    {t('dimComb.unblock')}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {query.data !== undefined && query.data.length === 0 && <p className="muted">{t('dimComb.empty')}</p>}
      <div className="grid g3 dim-comb-form">
        <label className="field">
          <span className="label">{t('dimComb.first')}</span>
          <select className="cell-input" value={typeA} onChange={(e) => { setTypeA(e.target.value); setValueA(null); }}>
            {types.map((x) => <option key={x.code} value={x.code}>{x.name}</option>)}
          </select>
          <select className="cell-input" aria-label={t('dimComb.valueA')} value={valueA ?? ''} onChange={(e) => setValueA(e.target.value === '' ? null : Number(e.target.value))}>
            <option value="">{t('dimComb.anyValue')}</option>
            {valuesA.map((v) => <option key={v.id} value={v.id}>{v.code} · {v.name}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('dimComb.second')}</span>
          <select className="cell-input" value={typeB} onChange={(e) => { setTypeB(e.target.value); setValueB(null); }}>
            {types.map((x) => <option key={x.code} value={x.code}>{x.name}</option>)}
          </select>
          <select className="cell-input" aria-label={t('dimComb.valueB')} value={valueB ?? ''} onChange={(e) => setValueB(e.target.value === '' ? null : Number(e.target.value))}>
            <option value="">{t('dimComb.anyValue')}</option>
            {valuesB.map((v) => <option key={v.id} value={v.id}>{v.code} · {v.name}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('dimComb.note')}</span>
          <input className="cell-input" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} />
          <button type="button" className="btn primary small" disabled={busy || typeA === typeB || (valueA === null) !== (valueB === null)}
            onClick={() => void run(async () => {
              unwrap(await api.POST('/api/core/dimension-combinations', {
                body: { typeA, valueAId: valueA, typeB, valueBId: valueB, note: note.trim() === '' ? null : note.trim() },
              }));
              setNote('');
            }, t('dimComb.blocked'))}>
            {t('dimComb.block')}
          </button>
        </label>
      </div>
    </section>
  );
}
