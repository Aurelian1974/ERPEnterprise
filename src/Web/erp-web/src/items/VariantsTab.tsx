import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { itemKeys } from './itemApi';
import { combinationCount, useItemAttributes, useItemVariants, variantKeys, type Variant } from './variantsApi';

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

/**
 * Tab-ul „Variante” al articolului (02.01, 3.6 punctul 13): alegerea valorilor atributelor (marime, culoare) si generarea
 * combinatiilor lipsa; lista variantelor cu cod, denumire si stare. Codurile de bare pe varianta se adauga din tab-ul Coduri de bare.
 */
export function VariantsTab({ itemId, canManage, active }: { readonly itemId: number; readonly canManage: boolean; readonly active: boolean }) {
  const attributes = useItemAttributes();
  const variants = useItemVariants(itemId, active);
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [chosen, setChosen] = useState<ReadonlySet<number>>(new Set());
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);

  if (attributes.data === undefined || variants.data === undefined) {
    const failed = attributes.error ?? variants.error;
    return failed !== null ? <ErrorBanner message={problem(failed)} /> : <p className="muted" role="status">{t('app.loading')}</p>;
  }
  const used = new Set(variants.data.attributeIds);
  // Dupa prima generare, variantele noi folosesc aceleasi atribute.
  const available = attributes.data.attributes.filter((a) => a.isActive && (used.size === 0 || used.has(a.id)));
  const count = combinationCount(attributes.data.values, chosen);
  const toggle = (id: number) => setChosen((current) => {
    const next = new Set(current);
    if (!next.delete(id)) {
      next.add(id);
    }
    return next;
  });

  const generate = async () => {
    setError(undefined);
    setBusy(true);
    try {
      const created = unwrap(await api.POST('/api/core/items/{id}/variants/generate', { params: { path: { id: itemId } }, body: { itemId, valueIds: [...chosen] } }));
      setChosen(new Set());
      await queryClient.invalidateQueries({ queryKey: variantKeys.variants(itemId) });
      await queryClient.invalidateQueries({ queryKey: itemKeys.history(itemId) });
      notify(t('variant.generated', { count: created }));
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="tab-body variants-tab">
      <ErrorBanner message={error} />
      {canManage && (
        <section className="card variant-generator" aria-labelledby="variant-gen-title">
          <h3 id="variant-gen-title">{t('variant.generate')}</h3>
          {available.length === 0 && <p className="muted">{t('variant.noAttributes')}</p>}
          <div className="variant-attributes">
            {available.map((a) => (
              <fieldset key={a.id}>
                <legend>{a.name}</legend>
                {attributes.data.values.filter((v) => v.attributeId === a.id && v.isActive).map((v) => (
                  <label key={v.id} className="check">
                    <input type="checkbox" checked={chosen.has(v.id)} onChange={() => toggle(v.id)} />
                    {v.name} <span className="muted small">{v.code}</span>
                  </label>
                ))}
              </fieldset>
            ))}
          </div>
          <div className="form-footer">
            <span className="muted small">{used.size > 0 ? t('variant.sameAttributes') : t('variant.hint')}</span>
            <button type="button" className="btn primary" disabled={busy || count === 0} onClick={() => void generate()}>
              {t('variant.generateCount', { count })}
            </button>
          </div>
        </section>
      )}
      {variants.data.variants.length === 0 ? <p className="muted">{t('variant.empty')}</p> : (
        <table className="simple">
          <thead>
            <tr>
              <th scope="col">{t('field.code')}</th>
              <th scope="col">{t('field.name')}</th>
              <th scope="col">{t('variant.barcodes')}</th>
              <th scope="col">{t('dim.state')}</th>
              {canManage && <th scope="col" aria-label={t('common.save')} />}
            </tr>
          </thead>
          <tbody>
            {variants.data.variants.map((v) => <VariantRow key={`${v.id}-${v.rowVersion}`} itemId={itemId} variant={v} canManage={canManage} />)}
          </tbody>
        </table>
      )}
    </div>
  );
}

function VariantRow({ itemId, variant, canManage }: { readonly itemId: number; readonly variant: Variant; readonly canManage: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [code, setCode] = useState(variant.code);
  const [name, setName] = useState(variant.name);
  const [isActive, setIsActive] = useState(variant.isActive);
  const [busy, setBusy] = useState(false);
  const dirty = code !== variant.code || name !== variant.name || isActive !== variant.isActive;

  const save = async () => {
    setBusy(true);
    try {
      unwrap(await api.PUT('/api/core/items/{id}/variants/{variantId}', {
        params: { path: { id: itemId, variantId: variant.id } },
        body: { id: variant.id, itemId, code: code.trim(), name: name.trim(), isActive, rowVersion: variant.rowVersion },
      }));
      await queryClient.invalidateQueries({ queryKey: variantKeys.variants(itemId) });
      notify(t('common.saved'));
    } catch (e) {
      notify(problem(e), 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <tr className={variant.isActive ? '' : 'inactive'}>
      <td><input className="cell-input" value={code} maxLength={50} disabled={!canManage} aria-label={t('field.code')} onChange={(e) => setCode(e.target.value)} /></td>
      <td><input className="cell-input" value={name} maxLength={250} disabled={!canManage} aria-label={t('field.name')} onChange={(e) => setName(e.target.value)} /></td>
      <td className="num">{variant.barcodeCount}</td>
      <td>
        <label className="check">
          <input type="checkbox" checked={isActive} disabled={!canManage} onChange={(e) => setIsActive(e.target.checked)} />
          {t('common.active')}
        </label>
      </td>
      {canManage && (
        <td><button type="button" className="btn ghost small" disabled={busy || !dirty || code.trim() === '' || name.trim() === ''} onClick={() => void save()}>{t('common.save')}</button></td>
      )}
    </tr>
  );
}
