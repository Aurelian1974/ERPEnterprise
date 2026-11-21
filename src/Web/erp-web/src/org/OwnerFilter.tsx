import { t } from '../i18n';
import { ownerFilters, type OwnerOptions } from './owner';

/** Filtrul „Aparține” al listelor de articole si parteneri: toate vizibile, comune, ale firmei, ale unui punct de lucru sau locatii. */
export function OwnerFilter({ options, value, onChange }: {
  readonly options: OwnerOptions | null | undefined;
  readonly value: string;
  readonly onChange: (value: string) => void;
}) {
  const choices = ownerFilters(options);
  return choices.length === 0 ? null : (
    <select className={value === '' ? 'chip' : 'chip on'} aria-label={t('owner.field')} value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">{t('owner.filter.any')}</option>
      {choices.map((c) => <option key={c.value} value={c.value}>{c.text}</option>)}
    </select>
  );
}
