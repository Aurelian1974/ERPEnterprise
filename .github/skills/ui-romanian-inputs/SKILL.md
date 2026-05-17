---
name: ui-romanian-inputs
description: >-
  Componente specifice României pentru ERP: CNPInput (validare + extragere date),
  CUIInput (validare CIF + lookup ANAF opțional), AddressInput (județe + localități).
  Toate integrate cu React Hook Form + Zod.
---

# Romanian Inputs

## Când se aplică
Când utilizatorul cere câmpuri specifice României: CNP, CUI/CIF, adresă cu
județ și localitate, cod poștal.

---

## 1. CNPInput — Cod Numeric Personal

```tsx
// components/common/RomanianInputs/CNPInput.tsx
import { forwardRef } from 'react';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/utils';

interface CNPInputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  showExtracted?: boolean;   // afișează data nașterii și sexul extras
}

// Validare CNP conform algoritmului românesc
export function validateCNP(cnp: string): boolean {
  if (!/^\d{13}$/.test(cnp)) return false;

  const weights = [2, 7, 9, 1, 4, 6, 3, 5, 8, 2, 7, 9];
  const sum = weights.reduce((acc, w, i) => acc + w * parseInt(cnp[i]), 0);
  const remainder = sum % 11;
  const checkDigit = remainder === 10 ? 1 : remainder;

  return checkDigit === parseInt(cnp[12]);
}

// Extrage informații din CNP
export function extractCNPData(cnp: string) {
  if (!validateCNP(cnp)) return null;

  const s = parseInt(cnp[0]);
  const year  = parseInt(cnp.slice(1, 3));
  const month = parseInt(cnp.slice(3, 5));
  const day   = parseInt(cnp.slice(5, 7));

  // Secole bazate pe prima cifră
  const centuryMap: Record<number, number> = {
    1: 1900, 2: 1900,   // născuți 1900-1999, bărbați/femei
    3: 1800, 4: 1800,   // născuți 1800-1899
    5: 2000, 6: 2000,   // născuți 2000+
    7: 1900, 8: 1900,   // rezidenți
    9: 1900,            // străini
  };

  const century = centuryMap[s] ?? 1900;
  const fullYear = century + year;

  const birthDate = new Date(fullYear, month - 1, day);
  const gender    = s % 2 === 1 ? 'M' : 'F';
  const county    = parseInt(cnp.slice(7, 9));

  return { birthDate, gender, county };
}

export const CNPInput = forwardRef<HTMLInputElement, CNPInputProps>(
  ({ className, showExtracted = false, onChange, value, ...props }, ref) => {
    const cnpStr    = String(value ?? '');
    const isValid   = cnpStr.length === 13 && validateCNP(cnpStr);
    const extracted = isValid ? extractCNPData(cnpStr) : null;

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
      // Permite doar cifre, maxim 13
      const clean = e.target.value.replace(/\D/g, '').slice(0, 13);
      e.target.value = clean;
      onChange?.(e);
    };

    return (
      <div>
        <Input
          ref={ref}
          inputMode="numeric"
          maxLength={13}
          value={value}
          onChange={handleChange}
          className={cn('font-mono', className)}
          {...props}
        />
        {showExtracted && extracted && (
          <p className="mt-1 text-xs text-text-secondary">
            Născut: {extracted.birthDate.toLocaleDateString('ro-RO')} ·
            Sex: {extracted.gender === 'M' ? 'Masculin' : 'Feminin'}
          </p>
        )}
      </div>
    );
  }
);
CNPInput.displayName = 'CNPInput';

// Zod schema
export const cnpSchema = z
  .string()
  .length(13, 'CNP-ul trebuie să aibă 13 cifre')
  .refine(validateCNP, { message: 'CNP invalid' });
```

---

## 2. CUIInput — Cod Unic de Înregistrare

```tsx
// components/common/RomanianInputs/CUIInput.tsx
import { useState } from 'react';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { SearchIcon, Loader2Icon } from 'lucide-react';

// Validare CUI/CIF românesc
export function validateCUI(cui: string): boolean {
  // Elimină prefixul RO dacă există
  const clean = cui.toUpperCase().replace(/^RO/, '').trim();
  if (!/^\d{2,10}$/.test(clean)) return false;

  const weights = [7, 5, 3, 2, 1, 7, 5, 3, 2];
  const digits  = clean.split('').map(Number);
  const control = digits.pop()!;

  // Pad la 9 cifre
  while (digits.length < 9) digits.unshift(0);

  const sum       = weights.reduce((acc, w, i) => acc + w * digits[i], 0);
  const remainder = (sum * 10) % 11;
  const checkDigit = remainder === 10 ? 0 : remainder;

  return checkDigit === control;
}

interface CUIInputProps {
  value?:      string;
  onChange:    (value: string) => void;
  onLookup?:  (data: ANAFCompanyData) => void;  // callback cu date ANAF
  disabled?:   boolean;
  className?:  string;
}

interface ANAFCompanyData {
  cui:       string;
  name:      string;
  address:   string;
  county:    string;
  regCom?:   string;
}

export function CUIInput({ value = '', onChange, onLookup, disabled, className }: CUIInputProps) {
  const [loading, setLoading] = useState(false);

  const clean    = value.toUpperCase().replace(/^RO/, '');
  const isValid  = clean.length >= 2 && validateCUI(value);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const raw = e.target.value.replace(/[^0-9ROro]/g, '').toUpperCase();
    onChange(raw);
  };

  // Lookup ANAF — via backend (proxy pentru ANAF OpenAPI)
  const handleLookup = async () => {
    if (!isValid || !onLookup) return;
    setLoading(true);
    try {
      const res = await fetch(`/api/v1/administration/anaf/company/${clean}`);
      if (res.ok) {
        const data: ANAFCompanyData = await res.json();
        onLookup(data);
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex gap-1">
      <Input
        value={value}
        onChange={handleChange}
        placeholder="RO12345678"
        disabled={disabled}
        maxLength={12}
        className={cn('font-mono flex-1', className)}
      />
      {onLookup && (
        <Button
          type="button"
          variant="outline"
          size="icon"
          disabled={!isValid || loading || disabled}
          onClick={handleLookup}
          title="Caută în ANAF"
        >
          {loading
            ? <Loader2Icon className="h-4 w-4 animate-spin" />
            : <SearchIcon className="h-4 w-4" />
          }
        </Button>
      )}
    </div>
  );
}

// Zod schema
export const cuiSchema = z
  .string()
  .min(2, 'CUI invalid')
  .refine(validateCUI, { message: 'CUI/CIF invalid' });
```

---

## 3. AddressInput — Adresă completă România

```tsx
// components/common/RomanianInputs/AddressInput.tsx
import { useState, useEffect } from 'react';
import { Input } from '@/components/ui/input';
import { SearchableSelect } from '@/components/common/SearchableSelect/SearchableSelect';
import { FormItem, FormLabel } from '@/components/ui/form';
import { ROMANIAN_COUNTIES } from './data/counties';

interface Address {
  street:   string;
  city:     string;
  county:   string;   // cod județ (ex: "BV", "B", "CJ")
  zipCode:  string;
  country:  string;
}

interface AddressInputProps {
  value?:    Partial<Address>;
  onChange:  (address: Partial<Address>) => void;
  disabled?: boolean;
}

export function AddressInput({ value = {}, onChange, disabled }: AddressInputProps) {
  const update = (field: keyof Address, val: string) =>
    onChange({ ...value, [field]: val });

  // Localități pentru județul selectat — din API sau date statice
  const searchCities = async (search: string) => {
    const countyCode = value.county;
    if (!countyCode) return [];
    const res = await fetch(
      `/api/v1/administration/cities?county=${countyCode}&search=${search}`
    );
    const data = await res.json();
    return data.map((c: { id: string; name: string }) => ({
      value: c.name,
      label: c.name,
    }));
  };

  return (
    <div className="grid grid-cols-1 gap-3">
      {/* Stradă */}
      <FormItem>
        <FormLabel>Stradă, număr, bloc, apartament</FormLabel>
        <Input
          value={value.street ?? ''}
          onChange={(e) => update('street', e.target.value)}
          placeholder="Str. Exemplu, nr. 1, bl. A, ap. 2"
          disabled={disabled}
        />
      </FormItem>

      <div className="grid grid-cols-2 gap-3">
        {/* Județ */}
        <FormItem>
          <FormLabel>Județ</FormLabel>
          <SearchableSelect
            value={value.county ?? null}
            onChange={(v) => {
              onChange({ ...value, county: v ?? '', city: '' });
            }}
            queryKey={['counties']}
            queryFn={async (search) =>
              ROMANIAN_COUNTIES
                .filter((c) =>
                  c.label.toLowerCase().includes(search.toLowerCase())
                )
                .slice(0, 50)
            }
            placeholder="Selectează județ..."
            disabled={disabled}
          />
        </FormItem>

        {/* Localitate */}
        <FormItem>
          <FormLabel>Localitate</FormLabel>
          <SearchableSelect
            value={value.city ?? null}
            onChange={(v) => update('city', v ?? '')}
            queryKey={['cities', value.county ?? '']}
            queryFn={searchCities}
            placeholder="Selectează localitate..."
            disabled={disabled || !value.county}
          />
        </FormItem>
      </div>

      <div className="grid grid-cols-2 gap-3">
        {/* Cod poștal */}
        <FormItem>
          <FormLabel>Cod poștal</FormLabel>
          <Input
            value={value.zipCode ?? ''}
            onChange={(e) => update('zipCode', e.target.value.replace(/\D/g, '').slice(0, 6))}
            placeholder="500001"
            inputMode="numeric"
            maxLength={6}
            disabled={disabled}
            className="font-mono"
          />
        </FormItem>

        {/* Țară — default România */}
        <FormItem>
          <FormLabel>Țară</FormLabel>
          <Input
            value={value.country ?? 'România'}
            onChange={(e) => update('country', e.target.value)}
            disabled={disabled}
          />
        </FormItem>
      </div>
    </div>
  );
}

// data/counties.ts — lista completă județe România
export const ROMANIAN_COUNTIES = [
  { value: 'AB', label: 'Alba' },
  { value: 'AR', label: 'Arad' },
  { value: 'AG', label: 'Argeș' },
  { value: 'BC', label: 'Bacău' },
  { value: 'BH', label: 'Bihor' },
  { value: 'BN', label: 'Bistrița-Năsăud' },
  { value: 'BT', label: 'Botoșani' },
  { value: 'BV', label: 'Brașov' },
  { value: 'BR', label: 'Brăila' },
  { value: 'B',  label: 'București' },
  { value: 'BZ', label: 'Buzău' },
  { value: 'CS', label: 'Caraș-Severin' },
  { value: 'CL', label: 'Călărași' },
  { value: 'CJ', label: 'Cluj' },
  { value: 'CT', label: 'Constanța' },
  { value: 'CV', label: 'Covasna' },
  { value: 'DB', label: 'Dâmbovița' },
  { value: 'DJ', label: 'Dolj' },
  { value: 'GL', label: 'Galați' },
  { value: 'GR', label: 'Giurgiu' },
  { value: 'GJ', label: 'Gorj' },
  { value: 'HR', label: 'Harghita' },
  { value: 'HD', label: 'Hunedoara' },
  { value: 'IL', label: 'Ialomița' },
  { value: 'IS', label: 'Iași' },
  { value: 'IF', label: 'Ilfov' },
  { value: 'MM', label: 'Maramureș' },
  { value: 'MH', label: 'Mehedinți' },
  { value: 'MS', label: 'Mureș' },
  { value: 'NT', label: 'Neamț' },
  { value: 'OT', label: 'Olt' },
  { value: 'PH', label: 'Prahova' },
  { value: 'SM', label: 'Satu Mare' },
  { value: 'SJ', label: 'Sălaj' },
  { value: 'SB', label: 'Sibiu' },
  { value: 'SV', label: 'Suceava' },
  { value: 'TR', label: 'Teleorman' },
  { value: 'TM', label: 'Timiș' },
  { value: 'TL', label: 'Tulcea' },
  { value: 'VS', label: 'Vaslui' },
  { value: 'VL', label: 'Vâlcea' },
  { value: 'VN', label: 'Vrancea' },
];
```

---

## Integrare React Hook Form

```tsx
// Schema Zod pentru adresă
const addressSchema = z.object({
  street:  z.string().min(3, 'Adresa stradă este obligatorie'),
  city:    z.string().min(1, 'Localitatea este obligatorie'),
  county:  z.string().min(1, 'Județul este obligatoriu'),
  zipCode: z.string().length(6, 'Codul poștal trebuie să aibă 6 cifre'),
  country: z.string().default('România'),
});

// Schema formular cu CNP și CUI
const partnerSchema = z.object({
  cnp:     cnpSchema,
  cui:     cuiSchema,
  address: addressSchema,
});

// Utilizare AddressInput cu RHF
<FormField
  control={form.control}
  name="address"
  render={({ field }) => (
    <FormItem>
      <FormLabel>Adresă</FormLabel>
      <AddressInput
        value={field.value}
        onChange={field.onChange}
      />
      <FormMessage />
    </FormItem>
  )}
/>
```

## Reguli obligatorii
- CNP: validare cu algoritmul oficial (9 ponderi + modulo 11)
- CUI: elimină prefixul `RO` la validare, îl acceptă în input
- Cod poștal: 6 cifre, `inputMode="numeric"`, `font-mono`
- Județ resetează Localitate — cele două sunt dependente
- Lookup ANAF: via backend (proxy) — nu direct din FE (CORS + securitate)
