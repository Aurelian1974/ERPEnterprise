---
name: ui-currency-input
description: >-
  Componentă CurrencyInput pentru ERP — format românesc 1.234,56 RON,
  separator mii punct, separator zecimal virgulă, monedă configurabilă,
  integrat cu React Hook Form + Zod. Valoarea internă e number, nu string.
---

# CurrencyInput Component

## Când se aplică
Când utilizatorul cere un câmp de sumă monetară, preț, sau valoare numerică
formatată în format românesc (1.234,56) într-un formular ERP.

---

## 1. Componentă

```tsx
// components/common/CurrencyInput/CurrencyInput.tsx
import { useState, useRef } from 'react';
import { cn } from '@/lib/utils';

interface CurrencyInputProps {
  value?:       number | null;
  onChange:     (value: number | null) => void;
  currency?:    string;       // 'RON' | 'EUR' | 'USD'
  decimals?:    number;       // 2 default
  min?:         number;
  max?:         number;
  disabled?:    boolean;
  placeholder?: string;
  className?:   string;
  align?:       'left' | 'right';   // right pentru coloane tabel
}

// Formatare număr → string display (1234.56 → "1.234,56")
function formatDisplay(value: number, decimals: number): string {
  return new Intl.NumberFormat('ro-RO', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);
}

// Parsare string input → număr (acceptă "1.234,56" sau "1234,56" sau "1234.56")
function parseInput(raw: string): number | null {
  if (!raw.trim()) return null;
  // Elimină separatorii de mii (punct în ro-RO)
  // Înlocuiește virgula cu punct pentru parseFloat
  const normalized = raw
    .replace(/\./g, '')    // elimină punctele (separatori mii)
    .replace(',', '.');    // virgula → punct pentru parseFloat
  const num = parseFloat(normalized);
  return isNaN(num) ? null : num;
}

export function CurrencyInput({
  value,
  onChange,
  currency   = 'RON',
  decimals   = 2,
  min,
  max,
  disabled   = false,
  placeholder = '0,00',
  className,
  align      = 'right',
}: CurrencyInputProps) {
  const [editing, setEditing]   = useState(false);
  const [rawInput, setRawInput] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  // Valoarea afișată — formatată când nu e în edit mode
  const displayValue = editing
    ? rawInput
    : value != null
      ? formatDisplay(value, decimals)
      : '';

  const handleFocus = () => {
    setEditing(true);
    // La focus, afișează valoarea brută editabilă (cu virgulă)
    setRawInput(
      value != null
        ? value.toFixed(decimals).replace('.', ',')
        : ''
    );
    // Selectează tot la focus (UX ERP standard)
    setTimeout(() => inputRef.current?.select(), 0);
  };

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const raw = e.target.value;
    // Permite: cifre, punct, virgulă, minus
    if (/^-?[\d.,]*$/.test(raw)) {
      setRawInput(raw);
    }
  };

  const handleBlur = () => {
    setEditing(false);
    const parsed = parseInput(rawInput);

    if (parsed === null) {
      onChange(null);
      return;
    }

    // Aplică min/max
    let clamped = parsed;
    if (min !== undefined) clamped = Math.max(min, clamped);
    if (max !== undefined) clamped = Math.min(max, clamped);

    // Rotunjire la decimale configurate
    const rounded = parseFloat(clamped.toFixed(decimals));
    onChange(rounded);
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') inputRef.current?.blur();
    if (e.key === 'Escape') {
      setRawInput(value != null ? value.toFixed(decimals).replace('.', ',') : '');
      inputRef.current?.blur();
    }
  };

  return (
    <div className={cn('relative flex items-center', className)}>
      <input
        ref={inputRef}
        type="text"
        inputMode="decimal"
        value={displayValue}
        onChange={handleChange}
        onFocus={handleFocus}
        onBlur={handleBlur}
        onKeyDown={handleKeyDown}
        disabled={disabled}
        placeholder={placeholder}
        className={cn(
          'w-full rounded-md border border-border-default bg-white',
          'px-3 py-2 pr-14 text-sm',
          'focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500',
          'disabled:bg-surface-subtle disabled:text-text-muted disabled:cursor-not-allowed',
          'placeholder:text-text-muted',
          'font-mono',   // font monospaced pentru aliniere cifre
          align === 'right' && 'text-right',
          align === 'left'  && 'text-left',
        )}
      />
      {/* Suffix monedă */}
      <span className="absolute right-3 text-xs text-text-muted pointer-events-none select-none">
        {currency}
      </span>
    </div>
  );
}
```

---

## 2. Integrare React Hook Form

```tsx
import { Controller } from 'react-hook-form';
import { z } from 'zod';
import { CurrencyInput } from '@/components/common/CurrencyInput/CurrencyInput';
import { FormField, FormItem, FormLabel, FormMessage } from '@/components/ui/form';

// Schema Zod — valoarea internă e number
const schema = z.object({
  unitPrice:   z.number({ required_error: 'Prețul este obligatoriu' })
                .positive({ message: 'Prețul trebuie să fie pozitiv' }),
  discount:    z.number().min(0).max(100).optional(),
  totalAmount: z.number().min(0),
});

// Utilizare
<FormField
  control={form.control}
  name="unitPrice"
  render={({ field }) => (
    <FormItem>
      <FormLabel>Preț unitar</FormLabel>
      <CurrencyInput
        value={field.value ?? null}
        onChange={field.onChange}
        currency="RON"
        decimals={2}
        min={0}
        align="right"
      />
      <FormMessage />
    </FormItem>
  )}
/>

// În EditableGrid (coloane de tabel) — fără label, align right
<CurrencyInput
  value={row.unitPrice}
  onChange={(v) => updateLine(row.id, 'unitPrice', v)}
  currency="RON"
  align="right"
  className="w-32"
/>
```

---

## 3. Display read-only (nu input)

```tsx
// components/common/CurrencyInput/CurrencyDisplay.tsx
// Pentru afișare în tabele și detalii — nu editabil

interface CurrencyDisplayProps {
  value:     number | null | undefined;
  currency?: string;
  decimals?: number;
  className?: string;
}

export function CurrencyDisplay({
  value,
  currency  = 'RON',
  decimals  = 2,
  className,
}: CurrencyDisplayProps) {
  if (value == null) return <span className="text-text-muted">—</span>;

  const formatted = new Intl.NumberFormat('ro-RO', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);

  return (
    <span className={cn('font-mono tabular-nums', className)}>
      {formatted} <span className="text-text-muted text-xs">{currency}</span>
    </span>
  );
}
```

---

## 4. Hook utilitar pentru formatare

```typescript
// hooks/useCurrencyFormat.ts
export const useCurrencyFormat = (currency = 'RON', decimals = 2) => {
  const format = (value: number | null | undefined): string => {
    if (value == null) return '—';
    return new Intl.NumberFormat('ro-RO', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    }).format(value) + ` ${currency}`;
  };

  const formatCompact = (value: number): string =>
    new Intl.NumberFormat('ro-RO', {
      notation: 'compact',
      maximumFractionDigits: 1,
    }).format(value);

  return { format, formatCompact };
};
```

## Reguli obligatorii
- Valoarea internă (RHF + DB) = `number` — niciodată string
- Separator mii = `.` (punct), separator zecimal = `,` (virgulă) — standard ro-RO
- `font-mono` și `tabular-nums` — cifrele se aliniază vertical în tabel
- `align="right"` în coloane tabel — standard contabil
- Selectează tot la focus — UX ERP, permite suprascrierea directă
- `inputMode="decimal"` — keyboard numeric pe mobile
- Parsare permisivă — acceptă atât `1.234,56` cât și `1234,56` sau `1234.56`
