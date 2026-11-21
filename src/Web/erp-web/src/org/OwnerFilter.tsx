import { t } from '../i18n';
import { ownerFilters, type OwnerOptions } from './owner';

/** Filtrul „Aparține” al listelor de articole si parteneri: toate vizibile, comune, ale firmei, ale unui punct de lucru sau locatii. */
export function OwnerFilter({ options, context, value, onChange }: {
  readonly options: OwnerOptions | null | undefined;
  /** Nodul din antet: filtrul arata doar nodurile lui (si punctul de lucru al locatiei). */
  readonly context: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
}) {
  const choices = ownerFilters(options, context);
  return choices.length === 0 ? null : (
    <select className={value === '' ? 'chip' : 'chip on'} aria-label={t('owner.field')} value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">{t('owner.filter.context')}</option>
      {choices.map((c) => <option key={c.value} value={c.value}>{c.text}</option>)}
    </select>
  );
}

/** Proprietarul unui articol / partener nou: cheia (org/owner) si numele nodului din antet. */
export interface NewOwner {
  readonly key: string;
  readonly name: string;
}

/** „Aparține” in fisa: doar pentru citire (se ia din antet la adaugare si nu se schimba). */
export function OwnerDisplay({ text }: { readonly text: string }) {
  return (
    <div className="field">
      <span className="label">{t('owner.field')}</span>
      <span className="readonly-value" title={t('owner.fromContext')}>{text}</span>
    </div>
  );
}
