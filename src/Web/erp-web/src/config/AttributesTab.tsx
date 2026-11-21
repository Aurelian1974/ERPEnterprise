import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { useItemAttributes, variantKeys, type Attribute, type AttributePage } from '../items/variantsApi';

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

type ValueRow = { id: number | null; code: string; name: string; sortOrder: number; isActive: boolean };

/** Configurare › Atribute variante (02.01, 3.6 punctul 13): atributele (marime, culoare) si valorile lor, comune instalarii. */
export function AttributesTab() {
  const query = useItemAttributes();
  const [selected, setSelected] = useState<number | 'new' | undefined>();
  if (query.data === undefined) {
    return query.isError ? <ErrorBanner message={problem(query.error)} /> : <p className="muted" role="status">{t('app.loading')}</p>;
  }
  const data = query.data;
  const current = typeof selected === 'number' ? data.attributes.find((a) => a.id === selected) : undefined;
  return (
    <div className="split attributes">
      <section className="card dimension-types" aria-labelledby="attr-title">
        <div className="card-head">
          <h2 id="attr-title">{t('attr.title')}</h2>
          <button type="button" className="btn ghost small" onClick={() => setSelected('new')}>{t('attr.add')}</button>
        </div>
        <ul>
          {data.attributes.map((a) => (
            <li key={a.id}>
              <button type="button" className={a.id === selected ? 'current' : ''} onClick={() => setSelected(a.id)}>
                <span>{a.name}{a.isActive ? '' : ` · ${t('dim.inactiveOne')}`}</span>
                <span className="muted small">{data.values.filter((v) => v.attributeId === a.id && v.isActive).length}</span>
              </button>
            </li>
          ))}
        </ul>
        <p className="muted small">{t('attr.hint')}</p>
      </section>
      <section className="card detail-card">
        {selected === undefined ? <p className="muted empty-detail">{t('attr.select')}</p> : (
          <AttributeForm key={selected === 'new' ? 'new' : `${current?.id}-${current?.rowVersion}`} page={data} attribute={current}
            onSaved={(id) => setSelected(id)} />
        )}
      </section>
    </div>
  );
}

function AttributeForm({ page, attribute, onSaved }: { readonly page: AttributePage; readonly attribute: Attribute | undefined; readonly onSaved: (id: number) => void }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [code, setCode] = useState(attribute?.code ?? '');
  const [name, setName] = useState(attribute?.name ?? '');
  const [order, setOrder] = useState(String(attribute?.sortOrder ?? 10));
  const [isActive, setIsActive] = useState(attribute?.isActive ?? true);
  const [values, setValues] = useState<ValueRow[]>(page.values.filter((v) => v.attributeId === attribute?.id)
    .map((v) => ({ id: v.id, code: v.code, name: v.name, sortOrder: v.sortOrder, isActive: v.isActive })));
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const set = (index: number, change: Partial<ValueRow>) => setValues(values.map((v, i) => (i === index ? { ...v, ...change } : v)));

  const save = async () => {
    setError(undefined);
    setBusy(true);
    try {
      const body = { id: attribute?.id ?? null, code, name, sortOrder: Number(order), isActive, rowVersion: attribute?.rowVersion ?? null,
        values: values.filter((v) => v.code.trim() !== '' || v.id !== null) };
      const saved = attribute === undefined
        ? unwrap(await api.POST('/api/core/item-attributes', { body }))
        : unwrap(await api.PUT('/api/core/item-attributes/{id}', { params: { path: { id: attribute.id } }, body }));
      await queryClient.invalidateQueries({ queryKey: variantKeys.attributes });
      notify(t('common.saved'));
      onSaved(saved.id);
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save(); }} aria-label={t('attr.edit')}>
      <ErrorBanner message={error} />
      <div className="grid g3">
        <label className="field"><span className="label">{t('field.code')}</span>
          <input className="cell-input" value={code} maxLength={20} onChange={(e) => setCode(e.target.value.toUpperCase())} /></label>
        <label className="field"><span className="label">{t('field.name')}</span>
          <input className="cell-input" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} /></label>
        <label className="field"><span className="label">{t('dimType.order')}</span>
          <input className="cell-input num" type="number" value={order} onChange={(e) => setOrder(e.target.value)} /></label>
        <label className="check"><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />{t('field.isActive')}</label>
      </div>
      <h3 className="section">{t('attr.values')}</h3>
      <table className="simple">
        <thead><tr><th scope="col">{t('field.code')}</th><th scope="col">{t('field.name')}</th><th scope="col">{t('dimType.order')}</th><th scope="col">{t('common.active')}</th></tr></thead>
        <tbody>
          {values.map((v, index) => (
            <tr key={v.id ?? `n${index}`}>
              <td><input className="cell-input" value={v.code} maxLength={20} aria-label={t('field.code')} onChange={(e) => set(index, { code: e.target.value.toUpperCase() })} /></td>
              <td><input className="cell-input" value={v.name} maxLength={100} aria-label={t('field.name')} onChange={(e) => set(index, { name: e.target.value })} /></td>
              <td><input className="cell-input num" type="number" value={v.sortOrder} aria-label={t('dimType.order')} onChange={(e) => set(index, { sortOrder: Number(e.target.value) })} /></td>
              <td><input type="checkbox" checked={v.isActive} aria-label={t('common.active')} onChange={(e) => set(index, { isActive: e.target.checked })} /></td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="form-footer">
        <button type="button" className="btn ghost small" onClick={() => setValues([...values, { id: null, code: '', name: '', sortOrder: (values.length + 1) * 10, isActive: true }])}>
          {t('attr.addValue')}
        </button>
        <button type="submit" className="btn primary" disabled={busy || code === '' || name.trim() === ''}>{t('common.save')}</button>
      </div>
    </form>
  );
}
