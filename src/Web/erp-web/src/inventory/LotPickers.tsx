import { useState } from 'react';
import { t } from '../i18n';
import { searchVariants, type VariantOption } from './documentsApi';
import { useWarehouses } from './inventoryApi';
import { searchPartners, type PartnerOption } from './lotsApi';

/** Alegerea variantei (cautare dupa cod sau nume), comuna ecranelor de loturi si serii (S4.5). */
export function VariantPicker({ value, onChange }: {
  readonly value: { id: number; label: string } | undefined; readonly onChange: (v: { id: number; label: string } | undefined) => void;
}) {
  const [search, setSearch] = useState('');
  const [options, setOptions] = useState<VariantOption[]>([]);
  if (value !== undefined) {
    return (
      <button type="button" className="chip on" title={t('inv.lot.clearVariant')} onClick={() => onChange(undefined)}>{`${value.label} ✕`}</button>
    );
  }
  return (
    <span className="chips-row inline">
      <input className="cell-input" placeholder={t('inv.doc.searchVariant')} aria-label={t('inv.doc.searchVariant')} value={search}
        onChange={(e) => setSearch(e.target.value)}
        onKeyDown={(e) => { if (e.key === 'Enter') { void searchVariants(search, undefined).then(setOptions, () => setOptions([])); } }} />
      <button type="button" className="btn ghost" onClick={() => void searchVariants(search, undefined).then(setOptions, () => setOptions([]))}>
        {t('inv.doc.find')}
      </button>
      {options.slice(0, 8).map((o) => (
        <button key={o.id} type="button" className="chip" onClick={() => { onChange({ id: o.id, label: `${o.code} · ${o.name}` }); setOptions([]); }}>
          {`${o.code} · ${o.name}`}
        </button>
      ))}
    </span>
  );
}

/** Gestiunile active ale firmei, cu optiunea „toate” cand allowAll. */
export function WarehouseSelect({ value, onChange, allowAll = true }: {
  readonly value: number | undefined; readonly onChange: (id: number | undefined) => void; readonly allowAll?: boolean;
}) {
  const warehouses = useWarehouses('');
  const rows = (warehouses.data?.warehouses ?? []).filter((w) => w.isActive);
  return (
    <select className="chip" aria-label={t('inv.col.warehouse')} value={value ?? ''}
      onChange={(e) => onChange(e.target.value === '' ? undefined : Number(e.target.value))}>
      {allowAll ? <option value="">{t('inv.movements.allWarehouses')}</option> : <option value="">{t('inv.lot.chooseWarehouse')}</option>}
      {rows.map((w) => <option key={w.id} value={w.id}>{`${w.code} · ${w.name}`}</option>)}
    </select>
  );
}

/** Alegerea partenerului (client sau furnizor) dupa nume, cod sau CUI. */
export function PartnerPicker({ value, onChange, placeholder }: {
  readonly value: { id: number; name: string } | undefined; readonly onChange: (p: { id: number; name: string } | undefined) => void;
  readonly placeholder: string;
}) {
  const [search, setSearch] = useState('');
  const [options, setOptions] = useState<PartnerOption[]>([]);
  if (value !== undefined) {
    return <button type="button" className="chip on" onClick={() => onChange(undefined)}>{`${value.name} ✕`}</button>;
  }
  const find = () => void searchPartners(search).then(setOptions, () => setOptions([]));
  return (
    <span className="chips-row inline">
      <input className="cell-input" placeholder={placeholder} aria-label={placeholder} value={search} onChange={(e) => setSearch(e.target.value)}
        onKeyDown={(e) => { if (e.key === 'Enter') { find(); } }} />
      <button type="button" className="btn ghost" onClick={find}>{t('inv.doc.find')}</button>
      {options.map((o) => (
        <button key={o.id} type="button" className="chip" onClick={() => { onChange({ id: o.id, name: o.name }); setOptions([]); }}>{o.name}</button>
      ))}
    </span>
  );
}
