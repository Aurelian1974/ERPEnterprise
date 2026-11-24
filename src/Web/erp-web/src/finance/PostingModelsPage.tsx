import { useState } from 'react';
import { useOutletContext } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { formatAmount } from './cashApi';
import { resolvedFromKey } from './JournalEntriesPage';

type Model = components['schemas']['PostingModelDto'];
type Variant = components['schemas']['PostingVariantDto'];
type LineInput = components['schemas']['PostingModelLineInput'];

const modelKeys = { all: ['finance', 'posting-models'] as const };

/** Dimensiunile copiate de o linie (masca de biti din model) ca text: partener, angajat, centru de cost, proiect, gestiune, santier. */
export function dimensionsText(mask: number): string {
  const names: [number, TextKey][] = [[1, 'models.dim.partner'], [2, 'models.dim.employee'], [4, 'models.dim.costCenter'], [8, 'models.dim.project'],
    [16, 'models.dim.warehouse'], [32, 'models.dim.site']];
  const picked = names.filter(([bit]) => (mask & bit) !== 0).map(([, key]) => t(key));
  return picked.length === 0 ? '—' : picked.length === names.length ? t('models.dim.all') : picked.join(', ');
}

/**
 * Contabilitate › Modele de contare (02.02, C2 3.2, 4.2; macheta c2-03): evenimentele cu model, variantele lor in ordinea incercarii, liniile
 * variantei (componenta, filtrul, partea, cheia, contul implicit, gruparea, dimensiunile), modificarea liniilor si simularea pe o nota reala.
 */
export function PostingModelsPage() {
  const user = useOutletContext<CurrentUser>();
  const models = useQuery({ queryKey: modelKeys.all, queryFn: async () => unwrap(await api.GET('/api/finance/posting-models')) });
  const [search, setSearch] = useState('');
  const [picked, setPicked] = useState<number | undefined>();
  const list = (models.data ?? []).filter((m) => `${m.name} ${m.eventType}`.toLowerCase().includes(search.trim().toLowerCase()));
  const modelId = picked ?? list[0]?.id;

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.postingModels')}</div>
      <div className="head"><div><h1>{t('models.title')}</h1><div className="sub2">{t('models.subtitle')}</div></div></div>
      <ErrorBanner message={models.isError ? models.error.message : undefined} />
      <div className="split">
        <section className="card" aria-label={t('models.title')}>
          <input className="cell-input wide-input" placeholder={t('models.search')} value={search} onChange={(e) => setSearch(e.target.value)} />
          <table className="partner-list cash-list">
            <tbody>{list.map((m) => (
              <tr key={m.id} className={m.id === modelId ? 'picked' : undefined} onClick={() => setPicked(m.id)}>
                <td><span className="strong">{m.name}</span><div className="muted small mono">{m.eventType}</div></td>
                <td className="small">{t('models.variants', { count: m.variantCount })}</td>
              </tr>))}</tbody>
          </table>
        </section>
        {modelId !== undefined && <ModelDetail key={modelId} id={modelId} canManage={user.permissions.includes('Fin.PostingModel.Manage')} />}
      </div>
    </div>
  );
}

function ModelDetail({ id, canManage }: { readonly id: number; readonly canManage: boolean }) {
  const model = useQuery({ queryKey: [...modelKeys.all, id], queryFn: async () => unwrap(await api.GET('/api/finance/posting-models/{id}', { params: { path: { id } } })) });
  const [variantId, setVariantId] = useState<number | undefined>();
  const m = model.data;
  if (m === undefined) {
    return <section className="card"><ErrorBanner message={model.isError ? model.error.message : undefined} /></section>;
  }
  const variant = m.variants.find((v) => v.id === variantId) ?? m.variants.find((v) => v.isActive) ?? m.variants[0];
  return (
    <section className="card" aria-label={m.name}>
      <h2>{m.name} <span className="muted mono small">{m.eventType}</span> {m.isSystem && <span className="pill">{t('models.shipped')}</span>}</h2>
      <p className="muted small">{t('models.journal', { journal: `${m.journalCode} · ${m.journalName}` })}</p>
      <div className="chips" role="group" aria-label={t('models.variant')}>
        {m.variants.map((v) => (
          <button key={v.id} type="button" className={v.id === variant?.id ? 'chip on' : 'chip'} onClick={() => setVariantId(v.id)}>
            {v.priority} · {v.name}{v.isActive ? '' : ` (${t('models.inactive')})`}</button>
        ))}
      </div>
      {variant !== undefined && <VariantLines key={`${variant.id}-${variant.rowVersion}`} model={m} variant={variant} canManage={canManage} />}
      <Simulation />
    </section>
  );
}

function conditions(v: Variant): string {
  const parts = [v.requiredFlags != null ? t('models.requires', { flags: v.requiredFlags }) : undefined,
    v.forbiddenFlags != null ? t('models.forbids', { flags: v.forbiddenFlags }) : undefined,
    v.partnerKind != null ? t(`roles.kind.${String(v.partnerKind)}` as TextKey) : undefined, v.documentType ?? undefined].filter((x) => x !== undefined);
  return parts.length === 0 ? t('models.noConditions') : parts.join(' · ');
}

function VariantLines({ model, variant, canManage }: { readonly model: Model; readonly variant: Variant; readonly canManage: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [editing, setEditing] = useState(false);
  const [lines, setLines] = useState<LineInput[]>(() => variant.lines.map((l) => ({ ...l })));
  const [error, setError] = useState<string | undefined>();
  const update = (index: number, change: Partial<LineInput>) => setLines((all) => all.map((l, i) => i === index ? { ...l, ...change } : l));
  const save = async () => {
    setError(undefined);
    try {
      unwrap(await api.PUT('/api/finance/posting-variants/{id}/lines', { params: { path: { id: variant.id } }, body: { variantId: variant.id, lines, rowVersion: variant.rowVersion } }));
      notify(t('models.saved'));
      setEditing(false);
      await queryClient.invalidateQueries({ queryKey: modelKeys.all });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };
  return (
    <>
      <p className="small">{t('models.conditions')}: {conditions(variant)}</p>
      <table className="partner-list cash-list">
        <thead><tr><th scope="col">#</th><th scope="col">{t('models.component')}</th><th scope="col">{t('models.filter')}</th><th scope="col">{t('manual.side')}</th>
          <th scope="col">{t('models.key')}</th><th scope="col">{t('models.default')}</th><th scope="col">{t('models.grouping')}</th><th scope="col">{t('models.dimensions')}</th></tr></thead>
        <tbody>{lines.map((l, i) => (
          <tr key={i}>
            <td>{i + 1}</td>
            <td className="strong">{l.component}{l.sign === -1 ? ' (−)' : ''}</td>
            <td className="small">{l.filterVatCategory ?? l.filterOperationTypeCode ?? '—'}</td>
            <td><span className={l.side === 1 ? 'pill' : 'pill ok'}>{l.side === 1 ? t('entry.debit') : t('entry.credit')}</span></td>
            <td>{editing ? (
              <select className="cell-input" aria-label={t('models.key')} value={l.accountKey} onChange={(e) => update(i, { accountKey: e.target.value })}>
                {model.accountKeys.map((k) => <option key={k.code} value={k.code} title={k.chain}>{k.code}</option>)}</select>
            ) : <span className="mono strong" title={model.accountKeys.find((k) => k.code === l.accountKey)?.chain}>{l.accountKey}</span>}</td>
            <td>{editing ? <input className="cell-input mono" aria-label={t('models.default')} value={l.defaultAccountCode ?? ''}
              onChange={(e) => update(i, { defaultAccountCode: e.target.value.trim() === '' ? null : e.target.value.trim() })} /> : <span className="mono">{l.defaultAccountCode ?? '—'}</span>}</td>
            <td className="small">{t(`models.group.${String(l.groupBy ?? 1)}` as TextKey)}</td>
            <td className="small">{dimensionsText(l.copyDimensions ?? 63)}</td>
          </tr>))}</tbody>
      </table>
      <ErrorBanner message={error} />
      {canManage && (
        <div className="actions">
          <span className="muted small">{t('models.editHint')}</span>
          {editing ? <>
            <button type="button" className="btn" onClick={() => { setEditing(false); setLines(variant.lines.map((l) => ({ ...l }))); }}>{t('common.cancel')}</button>
            <button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button>
          </> : <button type="button" className="btn primary" onClick={() => setEditing(true)}>{t('models.edit')}</button>}
        </div>
      )}
    </>
  );
}

function Simulation() {
  const [entryId, setEntryId] = useState('');
  const [run, setRun] = useState<number | undefined>();
  const result = useQuery({
    queryKey: ['finance', 'posting-simulation', run ?? 0],
    enabled: run !== undefined,
    retry: false,
    queryFn: async () => unwrap(await api.GET('/api/finance/journal-entries/{id}/simulation', { params: { path: { id: run ?? 0 } } })),
  });
  const r = result.data;
  return (
    <div className="settings-form simulator">
      <h3>{t('models.simulation')}</h3>
      <div className="row-between">
        <label>{t('models.entryId')}<input className="cell-input" inputMode="numeric" value={entryId} onChange={(e) => setEntryId(e.target.value)} /></label>
        <button type="button" className="btn" disabled={!/^\d+$/.test(entryId)} onClick={() => setRun(Number(entryId))}>{t('models.simulate')}</button>
      </div>
      <p className="muted small">{t('models.simulationHint')}</p>
      <ErrorBanner message={result.isError ? result.error.message : r?.failure != null ? r.details ?? '' : undefined} />
      {r !== undefined && r.failure == null && (
        <table className="partner-list cash-list">
          <tbody>{r.lines.map((l, i) => {
            const from = resolvedFromKey(l.resolvedFrom);
            return (
              <tr key={i}><td className="mono strong">{l.accountCode}</td><td className="num">{l.side === 1 ? formatAmount(l.amountRon) : ''}</td>
                <td className="num">{l.side === 2 ? formatAmount(l.amountRon) : ''}</td><td className="small">{from === undefined ? '' : t(from)}</td></tr>
            );
          })}</tbody>
        </table>
      )}
    </div>
  );
}
