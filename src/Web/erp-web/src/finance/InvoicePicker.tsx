import { Controller, type Control, type FieldPath, type FieldValues } from 'react-hook-form';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { formatAmount } from './cashApi';
import { Role, useOpenItems, type OpenItem } from './settlementApi';

/** Facturile de ales pentru o plata (F3 4.2): resturile de sensul rolului (de incasat de la client, de platit furnizorului), fara litigii. */
export function invoiceChoices(items: readonly OpenItem[], role: number): OpenItem[] {
  return items.filter((i) => i.role === role && (role === Role.Customer ? Number(i.openAmount) > 0 : Number(i.openAmount) < 0) && i.kind !== 5 && i.kind !== 10);
}

/** Eticheta facturii in lista: numarul, restul si scadenta. */
export function invoiceText(item: OpenItem): string {
  const open = formatAmount(Math.abs(Number(item.openAmount)).toFixed(2));
  return `${item.documentNo} · ${t('invoice.rest', { amount: open })}${item.currencyCode === 'RON' ? '' : ` ${item.currencyCode}`} · ${t('invoice.due', { date: isoToRo(item.dueDate) })}`
    + (item.isDisputed ? ` · ${t('settle.disputed')}` : '');
}

/**
 * Factura stinsa de o linie de plata (F1 4.1, F2 3.6, F3 4.2): lista facturilor deschise ale partenerului pe rol; valoarea e referinta
 * „OI:id” din SettlementRef. Fara alegere, plata merge FIFO, dupa profilul partenerului. O referinta veche (text liber) ramane vizibila.
 */
export function InvoicePicker<T extends FieldValues>({ control, name, partnerId, role, label, className }: {
  readonly control: Control<T>;
  readonly name: FieldPath<T>;
  readonly partnerId: number;
  readonly role: number;
  readonly label: string;
  readonly className?: string;
}) {
  const items = useOpenItems(partnerId, true, role);
  const choices = invoiceChoices(items.data ?? [], role);
  return (
    <Controller
      control={control}
      name={name}
      render={({ field }) => {
        const value = (field.value as string | null | undefined) ?? '';
        const known = value === '' || choices.some((c) => `OI:${c.openItemId}` === value);
        return (
          <label className={className}>
            <span className="sr-only">{label}</span>
            <select className="cell-input" aria-label={label} value={value} onChange={(e) => field.onChange(e.target.value)} onBlur={field.onBlur}>
              <option value="">{t('invoice.fifo')}</option>
              {!known && <option value={value}>{value}</option>}
              {choices.map((c) => <option key={c.openItemId} value={`OI:${c.openItemId}`}>{invoiceText(c)}</option>)}
            </select>
          </label>
        );
      }}
    />
  );
}
