import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { t, type TextKey } from '../i18n';
import {
  catalogApi, catalogKeys, cleanTranslations, LANGUAGES, searchPartnersByRole, useItemReferences, useItemTranslations, type ItemReference,
  type ItemTranslation,
} from './catalogApi';
import type { ItemDetail, ItemLookups } from './itemApi';
import { itemKeys } from './itemApi';
import { effectiveLines } from './ItemForm';

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

/**
 * Tab-ul „Coduri la parteneri” al articolului (02.01, 3.6 punctul 11): codul, denumirea si UM la fiecare client sau furnizor.
 * Receptiile si facturile primite gasesc articolul dupa codul furnizorului; facturile emise pot tipari codul clientului.
 */
export function ItemReferencesTab({ item, lookups, canManage, active }: {
  readonly item: ItemDetail;
  readonly lookups: ItemLookups;
  readonly canManage: boolean;
  readonly active: boolean;
}) {
  const query = useItemReferences(item.id, active);
  const [selected, setSelected] = useState<ItemReference | 'new' | undefined>();
  const units = new Map(lookups.units.map((u) => [u.id, u.code]));
  const set = lookups.uomSets.find((s) => s.id === item.uomSetId);
  const uoms = effectiveLines(set?.lines ?? [], item.uoms).map((l) => ({ id: l.uomId, code: units.get(l.uomId) ?? '' }));

  if (query.data === undefined) {
    return query.isError ? <ErrorBanner message={problem(query.error)} /> : <p className="muted" role="status">{t('app.loading')}</p>;
  }
  return (
    <div className="tab-body">
      <div className="toolbar">
        {canManage && <button type="button" className="btn ghost" onClick={() => setSelected('new')}>{t('itemRef.add')}</button>}
      </div>
      {query.data.length === 0 ? <p className="muted">{t('itemRef.empty')}</p> : (
        <table className="simple dim-table">
          <thead>
            <tr>
              <th scope="col">{t('itemRef.type')}</th>
              <th scope="col">{t('itemRef.partner')}</th>
              <th scope="col">{t('itemRef.code')}</th>
              <th scope="col">{t('itemRef.description')}</th>
              <th scope="col">{t('itemRef.uom')}</th>
              <th scope="col">{t('dim.state')}</th>
            </tr>
          </thead>
          <tbody>
            {query.data.map((r) => (
              <tr key={r.id} className={selected !== 'new' && selected?.id === r.id ? 'current' : ''} onClick={() => canManage && setSelected(r)}>
                <td>{t(`itemRef.type.${r.referenceType as 1 | 2}`)}</td>
                <td>{r.partnerName} <span className="muted small">{r.partnerCode}</span></td>
                <td className="strong">{r.code}</td>
                <td>{r.description ?? '—'}</td>
                <td>{r.uomId == null ? t('item.barcode.baseUom') : units.get(r.uomId) ?? ''}</td>
                <td><span className={r.isActive ? 'pill ok' : 'pill off'}>{r.isActive ? t('common.active') : t('dim.inactiveOne')}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {selected !== undefined && (
        <ReferenceForm key={selected === 'new' ? 'new' : `${selected.id}-${selected.rowVersion}`} itemId={item.id} reference={selected === 'new' ? undefined : selected}
          uoms={uoms} onDone={() => setSelected(undefined)} />
      )}
    </div>
  );
}

function ReferenceForm({ itemId, reference, uoms, onDone }: {
  readonly itemId: number;
  readonly reference: ItemReference | undefined;
  readonly uoms: readonly { id: number; code: string }[];
  readonly onDone: () => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [type, setType] = useState<1 | 2>((reference?.referenceType ?? 2) as 1 | 2);
  const [partnerId, setPartnerId] = useState<number | null>(reference?.partnerId ?? null);
  const [search, setSearch] = useState('');
  const [partners, setPartners] = useState<{ id: number; name: string; code: string }[]>(
    reference === undefined ? [] : [{ id: reference.partnerId, name: reference.partnerName, code: reference.partnerCode }]);
  const [code, setCode] = useState(reference?.code ?? '');
  const [description, setDescription] = useState(reference?.description ?? '');
  const [uomId, setUomId] = useState<number | null>(reference?.uomId ?? null);
  const [isActive, setIsActive] = useState(reference?.isActive ?? true);
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const guard = useUnsavedChanges(reference === undefined ? code !== '' || partnerId !== null
    : code !== reference.code || partnerId !== reference.partnerId || type !== reference.referenceType || description !== (reference.description ?? '')
      || uomId !== (reference.uomId ?? null) || isActive !== reference.isActive);

  // Partenerii cu rolul ales care incep cu textul cautat; cel deja ales ramane in lista.
  useEffect(() => {
    let live = true;
    const timer = setTimeout(() => {
      void searchPartnersByRole(search, type).then((list) => {
        if (live) {
          setPartners((current) => {
            const chosen = current.find((p) => p.id === partnerId);
            const found = list.map((p) => ({ id: p.id, name: p.name, code: p.code }));
            return chosen === undefined || found.some((p) => p.id === chosen.id) ? found : [chosen, ...found];
          });
        }
      }, () => undefined);
    }, 250);
    return () => {
      live = false;
      clearTimeout(timer);
    };
  }, [search, type, partnerId]);

  const save = async () => {
    setError(undefined);
    setBusy(true);
    try {
      await catalogApi.saveReference(itemId, {
        id: reference?.id ?? null, itemId, partnerId: partnerId ?? 0, referenceType: type, code: code.trim(),
        description: description.trim() === '' ? null : description.trim(), uomId, isActive, rowVersion: reference?.rowVersion ?? null,
      });
      guard.markClean();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: catalogKeys.references(itemId) }),
        queryClient.invalidateQueries({ queryKey: itemKeys.history(itemId) }),
      ]);
      notify(t('common.saved'));
      onDone();
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="subform" noValidate onSubmit={(e) => { e.preventDefault(); void save(); }} aria-label={t('itemRef.edit')}>
      <ErrorBanner message={error} />
      <div className="grid g3">
        <label className="field">
          <span className="label">{t('itemRef.type')}</span>
          <select className="cell-input" value={type} onChange={(e) => { setType(Number(e.target.value) as 1 | 2); setPartnerId(null); }}>
            <option value={2}>{t('itemRef.type.2')}</option>
            <option value={1}>{t('itemRef.type.1')}</option>
          </select>
        </label>
        <label className="field">
          <span className="label">{t('itemRef.findPartner')}</span>
          <input className="cell-input" type="search" value={search} placeholder={t('itemRef.findPartnerHint')} onChange={(e) => setSearch(e.target.value)} />
        </label>
        <label className="field">
          <span className="label">{t('itemRef.partner')} *</span>
          <select className="cell-input" value={partnerId ?? ''} onChange={(e) => setPartnerId(e.target.value === '' ? null : Number(e.target.value))}>
            <option value="">{t('itemRef.choosePartner')}</option>
            {partners.map((p) => <option key={p.id} value={p.id}>{p.name} · {p.code}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="label">{t('itemRef.code')} *</span>
          <input className="cell-input" value={code} maxLength={50} onChange={(e) => setCode(e.target.value)} />
        </label>
        <label className="field span2">
          <span className="label">{t('itemRef.description')}</span>
          <input className="cell-input" value={description} maxLength={250} onChange={(e) => setDescription(e.target.value)} />
        </label>
        <label className="field">
          <span className="label">{t('itemRef.uom')}</span>
          <select className="cell-input" value={uomId ?? ''} onChange={(e) => setUomId(e.target.value === '' ? null : Number(e.target.value))}>
            <option value="">{t('item.barcode.baseUom')}</option>
            {uoms.map((u) => <option key={u.id} value={u.id}>{u.code}</option>)}
          </select>
        </label>
        <label className="check">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          {t('field.isActive')}
        </label>
      </div>
      <div className="form-footer">
        <span className="muted small">{t('itemRef.hint')}</span>
        <div className="actions">
          <button type="button" className="btn ghost" onClick={onDone}>{t('common.cancel')}</button>
          <button type="submit" className="btn primary" disabled={busy || partnerId === null || code.trim() === ''}>{t('common.save')}</button>
        </div>
      </div>
    </form>
  );
}

/** Tab-ul „Traduceri” (3.6 punctul 12): denumirea articolului in alte limbi, folosita dupa limba documentelor partenerului. */
export function ItemTranslationsTab({ itemId, itemName, canManage, active }: {
  readonly itemId: number;
  readonly itemName: string;
  readonly canManage: boolean;
  readonly active: boolean;
}) {
  const query = useItemTranslations(itemId, active);
  if (query.data === undefined) {
    return query.isError ? <ErrorBanner message={problem(query.error)} /> : <p className="muted" role="status">{t('app.loading')}</p>;
  }
  return <TranslationsForm key={JSON.stringify(query.data)} itemId={itemId} itemName={itemName} initial={query.data} canManage={canManage} />;
}

function TranslationsForm({ itemId, itemName, initial, canManage }: {
  readonly itemId: number;
  readonly itemName: string;
  readonly initial: readonly ItemTranslation[];
  readonly canManage: boolean;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [rows, setRows] = useState<ItemTranslation[]>([...initial]);
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const dirty = JSON.stringify(rows) !== JSON.stringify(initial);
  const guard = useUnsavedChanges(dirty);
  const free = LANGUAGES.filter((l) => !rows.some((r) => r.languageCode === l));

  const save = async () => {
    const list = cleanTranslations(rows);
    if (list === 'duplicate') {
      setError(t('itemTr.duplicate'));
      return;
    }
    setError(undefined);
    setBusy(true);
    try {
      await catalogApi.saveTranslations(itemId, list);
      guard.markClean();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: catalogKeys.translations(itemId) }),
        queryClient.invalidateQueries({ queryKey: itemKeys.history(itemId) }),
      ]);
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
          <tr><th scope="col">{t('itemTr.language')}</th><th scope="col">{t('itemTr.name')}</th><th scope="col" aria-label={t('itemTr.remove')} /></tr>
        </thead>
        <tbody>
          <tr className="muted"><td>{t('lang.ro')}</td><td>{itemName}</td><td /></tr>
          {rows.map((r, index) => (
            <tr key={r.languageCode}>
              <td>{t(`lang.${r.languageCode}` as TextKey)}</td>
              <td>
                <input className="cell-input" value={r.name} maxLength={250} disabled={!canManage} aria-label={t('itemTr.name')}
                  onChange={(e) => setRows(rows.map((x, i) => (i === index ? { ...x, name: e.target.value } : x)))} />
              </td>
              <td>
                {canManage && (
                  <button type="button" className="btn ghost small" onClick={() => setRows(rows.filter((_, i) => i !== index))}>{t('itemTr.remove')}</button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {canManage && (
        <div className="form-footer">
          <select className="chip" aria-label={t('itemTr.add')} value="" disabled={free.length === 0}
            onChange={(e) => e.target.value !== '' && setRows([...rows, { languageCode: e.target.value, name: '' }])}>
            <option value="">{t('itemTr.add')}</option>
            {free.map((l) => <option key={l} value={l}>{t(`lang.${l}` as TextKey)}</option>)}
          </select>
          <button type="button" className="btn primary" disabled={busy || !dirty} onClick={() => void save()}>{t('common.save')}</button>
        </div>
      )}
      <p className="banner dashed small">{t('itemTr.hint')}</p>
    </div>
  );
}
