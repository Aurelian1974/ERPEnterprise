import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { t, type TextKey } from '../i18n';

type Page = components['schemas']['DefaultDimensionPageDto'];
type Row = { dimensionType: string; typeName: string; valueId: number | null; posting: number | null };

/** Sursa dimensiunilor implicite: 1 partener, 2 articol, 3 locatie. */
export type DimensionSource = 1 | 2 | 3;

const key = (source: DimensionSource, id: number) => ['core', 'default-dimensions', source, id] as const;

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

/** Randurile de trimis: doar tipurile cu regula; regula fixa fara valoare intoarce tipul care o incalca. */
export function defaultsToSave(rows: readonly Row[]): { dimensionType: string; dimensionValueId: number | null; valuePosting: number }[] | string {
  const invalid = rows.find((r) => r.posting === 3 && r.valueId === null);
  if (invalid !== undefined) {
    return invalid.typeName;
  }
  return rows.filter((r) => r.posting !== null).map((r) => ({ dimensionType: r.dimensionType, dimensionValueId: r.valueId, valuePosting: r.posting ?? 1 }));
}

/**
 * Dimensiunile implicite ale unui partener, articol sau locatii pe firma curenta (02.01, 3.6 punctul 14): pentru fiecare tip activ,
 * valoarea propusa pe documente si regula (optionala, obligatorie, fixa). Documentele le iau automat si le verifica la validare.
 */
export function DefaultDimensionsTab({ source, sourceId, active, canManage }: {
  readonly source: DimensionSource;
  readonly sourceId: number;
  readonly active: boolean;
  readonly canManage: boolean;
}) {
  const query = useQuery({
    queryKey: key(source, sourceId),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/default-dimensions/{source}/{sourceId}', { params: { path: { source, sourceId } }, signal })),
    enabled: active,
  });
  if (query.data === undefined) {
    return query.isError ? <ErrorBanner message={problem(query.error)} /> : <p className="muted" role="status">{t('app.loading')}</p>;
  }
  return <Editor key={JSON.stringify(query.data.rows)} source={source} sourceId={sourceId} page={query.data} canManage={canManage} />;
}

function Editor({ source, sourceId, page, canManage }: {
  readonly source: DimensionSource;
  readonly sourceId: number;
  readonly page: Page;
  readonly canManage: boolean;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const initial: Row[] = page.rows.map((r) => ({ dimensionType: r.dimensionType, typeName: r.typeName, valueId: r.dimensionValueId ?? null, posting: r.valuePosting ?? null }));
  const [rows, setRows] = useState(initial);
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const dirty = JSON.stringify(rows) !== JSON.stringify(initial);
  const guard = useUnsavedChanges(dirty);
  const set = (index: number, change: Partial<Row>) => setRows(rows.map((r, i) => (i === index ? { ...r, ...change } : r)));

  const save = async () => {
    const body = defaultsToSave(rows);
    if (typeof body === 'string') {
      setError(t('defdim.fixedNeedsValue', { type: body }));
      return;
    }
    setError(undefined);
    setBusy(true);
    try {
      unwrap(await api.PUT('/api/core/default-dimensions/{source}/{sourceId}', { params: { path: { source, sourceId } }, body: { source, sourceId, rows: body } }));
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: key(source, sourceId) });
      notify(t('common.saved'));
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="tab-body">
      <ErrorBanner message={error} />
      <table className="simple">
        <thead>
          <tr><th scope="col">{t('defdim.type')}</th><th scope="col">{t('defdim.value')}</th><th scope="col">{t('defdim.rule')}</th></tr>
        </thead>
        <tbody>
          {rows.map((r, index) => (
            <tr key={r.dimensionType}>
              <td>{r.typeName}</td>
              <td>
                <select className="cell-input" aria-label={`${r.typeName}: ${t('defdim.value')}`} value={r.valueId ?? ''} disabled={!canManage}
                  onChange={(e) => set(index, { valueId: e.target.value === '' ? null : Number(e.target.value), posting: r.posting ?? 1 })}>
                  <option value="">{t('defdim.noValue')}</option>
                  {page.values.filter((v) => v.dimensionType === r.dimensionType).map((v) => <option key={v.id} value={v.id}>{v.code} · {v.name}</option>)}
                </select>
              </td>
              <td>
                <select className="cell-input" aria-label={`${r.typeName}: ${t('defdim.rule')}`} value={r.posting ?? ''} disabled={!canManage}
                  onChange={(e) => set(index, { posting: e.target.value === '' ? null : Number(e.target.value) })}>
                  <option value="">{t('defdim.posting.none')}</option>
                  {[1, 2, 3].map((p) => <option key={p} value={p}>{t(`defdim.posting.${p}` as TextKey)}</option>)}
                </select>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="banner dashed small">{t(`defdim.hint.${source}` as TextKey)}</p>
      {canManage && (
        <div className="form-footer">
          <span />
          <button type="button" className="btn primary" disabled={busy || !dirty} onClick={() => void save()}>{t('common.save')}</button>
        </div>
      )}
    </div>
  );
}
