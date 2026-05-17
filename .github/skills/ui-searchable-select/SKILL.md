---
name: ui-searchable-select
description: >-
  Componentă SearchableSelect pentru ERP — async search cu debounce,
  TanStack Query, seturi mari de date (clienți, produse, furnizori),
  integrat cu React Hook Form. shadcn/ui Popover + Command.
---

# SearchableSelect Component

## Când se aplică
Când utilizatorul cere un câmp de selecție pentru entități cu volume mari
(clienți, produse, furnizori, angajați — mii de înregistrări).
Nu folosi un `<select>` simplu pentru acestea.

## Instalare
```bash
npx shadcn@latest add command popover
```

---

## 1. Componentă

```tsx
// components/common/SearchableSelect/SearchableSelect.tsx
import { useState, useCallback } from 'react';
import { useQuery } from '@tanstack/react-query';
import { CheckIcon, ChevronsUpDownIcon, XIcon } from 'lucide-react';
import { Command, CommandEmpty, CommandGroup, CommandInput,
         CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useDebounce } from '@/hooks/useDebounce';

export interface SelectOption {
  value:    string;   // ID (Guid ca string)
  label:    string;   // text afișat
  sublabel?: string;  // text secundar (CUI, cod produs, etc.)
}

interface SearchableSelectProps {
  value?:         string | null;
  onChange:       (value: string | null) => void;
  queryKey:       string[];                              // TanStack Query key prefix
  queryFn:        (search: string) => Promise<SelectOption[]>;
  placeholder?:   string;
  searchPlaceholder?: string;
  disabled?:      boolean;
  clearable?:     boolean;
  className?:     string;
}

export function SearchableSelect({
  value,
  onChange,
  queryKey,
  queryFn,
  placeholder       = 'Selectează...',
  searchPlaceholder = 'Caută...',
  disabled          = false,
  clearable         = true,
  className,
}: SearchableSelectProps) {
  const [open, setOpen]     = useState(false);
  const [search, setSearch] = useState('');
  const debouncedSearch     = useDebounce(search, 300);

  // Query async — caută în backend
  const { data = [], isFetching } = useQuery({
    queryKey: [...queryKey, 'search', debouncedSearch],
    queryFn:  () => queryFn(debouncedSearch),
    enabled:  open,
    staleTime: 30_000,
  });

  // Query pentru label-ul valorii selectate (când avem ID dar nu label)
  const { data: selectedOption } = useQuery({
    queryKey: [...queryKey, 'selected', value],
    queryFn:  () => queryFn(value ?? '').then((r) => r[0] ?? null),
    enabled:  Boolean(value) && !data.find((o) => o.value === value),
    staleTime: Infinity,
  });

  const currentLabel = data.find((o) => o.value === value)?.label
    ?? selectedOption?.label
    ?? value
    ?? '';

  const handleSelect = (optionValue: string) => {
    onChange(optionValue === value ? null : optionValue);
    setOpen(false);
    setSearch('');
  };

  const handleClear = (e: React.MouseEvent) => {
    e.stopPropagation();
    onChange(null);
  };

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          aria-expanded={open}
          disabled={disabled}
          className={cn(
            'w-full justify-between font-normal text-sm',
            !value && 'text-muted-foreground',
            className
          )}
        >
          <span className="truncate">
            {value ? currentLabel : placeholder}
          </span>
          <div className="flex items-center gap-1 ml-2 shrink-0">
            {clearable && value && (
              <XIcon
                className="h-3.5 w-3.5 text-muted-foreground hover:text-foreground"
                onClick={handleClear}
              />
            )}
            <ChevronsUpDownIcon className="h-3.5 w-3.5 text-muted-foreground" />
          </div>
        </Button>
      </PopoverTrigger>

      <PopoverContent
        className="w-[var(--radix-popover-trigger-width)] p-0"
        align="start"
      >
        <Command shouldFilter={false}>
          <CommandInput
            placeholder={searchPlaceholder}
            value={search}
            onValueChange={setSearch}
          />
          <CommandList>
            {isFetching && (
              <div className="py-6 text-center text-sm text-muted-foreground">
                Se caută...
              </div>
            )}
            {!isFetching && data.length === 0 && (
              <CommandEmpty>Niciun rezultat.</CommandEmpty>
            )}
            {!isFetching && data.length > 0 && (
              <CommandGroup>
                {data.map((option) => (
                  <CommandItem
                    key={option.value}
                    value={option.value}
                    onSelect={handleSelect}
                    className="flex items-center justify-between"
                  >
                    <div>
                      <div className="text-sm">{option.label}</div>
                      {option.sublabel && (
                        <div className="text-xs text-muted-foreground">
                          {option.sublabel}
                        </div>
                      )}
                    </div>
                    {value === option.value && (
                      <CheckIcon className="h-4 w-4 text-primary-500" />
                    )}
                  </CommandItem>
                ))}
              </CommandGroup>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
```

---

## 2. useDebounce hook

```typescript
// hooks/useDebounce.ts
import { useState, useEffect } from 'react';

export function useDebounce<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);

  return debounced;
}
```

---

## 3. Query functions (per entitate)

```typescript
// features/finance/invoices/api.ts
import { api } from '@/lib/axios';
import type { SelectOption } from '@/components/common/SearchableSelect/SearchableSelect';

// Căutare clienți pentru SearchableSelect
export const searchCustomers = async (search: string): Promise<SelectOption[]> => {
  const data = await api.get<{ id: string; name: string; cui: string }[]>(
    '/finance/customers/search',
    { params: { search, pageSize: 20 } }
  );
  return data.map((c) => ({
    value:    c.id,
    label:    c.name,
    sublabel: c.cui,       // afișat sub nume — util pentru identificare rapidă
  }));
};

// Căutare produse
export const searchProducts = async (search: string): Promise<SelectOption[]> => {
  const data = await api.get<{ id: string; name: string; code: string }[]>(
    '/inventory/products/search',
    { params: { search, pageSize: 20 } }
  );
  return data.map((p) => ({
    value:    p.id,
    label:    p.name,
    sublabel: p.code,
  }));
};
```

---

## 4. Integrare React Hook Form

```tsx
import { SearchableSelect } from '@/components/common/SearchableSelect/SearchableSelect';
import { searchCustomers } from '@/features/finance/invoices/api';
import { FormField, FormItem, FormLabel, FormMessage } from '@/components/ui/form';

const schema = z.object({
  customerId: z.string().uuid({ message: 'Selectați un client' }),
  productId:  z.string().uuid({ message: 'Selectați un produs' }),
});

<FormField
  control={form.control}
  name="customerId"
  render={({ field }) => (
    <FormItem>
      <FormLabel>Client</FormLabel>
      <SearchableSelect
        value={field.value ?? null}
        onChange={field.onChange}
        queryKey={['customers']}
        queryFn={searchCustomers}
        placeholder="Selectează client..."
        searchPlaceholder="Caută după nume sau CUI..."
        clearable={false}
      />
      <FormMessage />
    </FormItem>
  )}
/>
```

## Reguli obligatorii
- `debounce` 300ms — nu face request la fiecare tastă
- `enabled: open` — query rulează doar când popover-ul e deschis
- `pageSize: 20` la backend — nu returna toate înregistrările
- `shouldFilter={false}` pe Command — filtrarea e server-side
- Label-ul valorii selectate = cache din query — nu trimite request separat dacă există în data
- `staleTime: Infinity` pentru query-ul de label selected — nu se schimbă
- `sublabel` pentru cod sau identificator secundar (CUI, cod produs)
