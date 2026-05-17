---
name: ui-multi-select
description: >-
  Componentă MultiSelect pentru ERP — selecție multiplă cu tags/chips,
  căutare filtrare, keyboard navigation, integrat cu React Hook Form.
  shadcn/ui Command + Popover. Pentru liste statice sau async mici.
---

# MultiSelect Component

## Când se aplică
Când utilizatorul cere selecție din valori multiple: categorii, roluri, permisiuni,
etichete, statusuri — liste relativ mici (sub 200 opțiuni) care pot fi static
sau async. Pentru seturi mari folosește `SearchableSelect` cu multi.

## Instalare
```bash
npx shadcn@latest add command popover badge
```

---

## 1. Componentă

```tsx
// components/common/MultiSelect/MultiSelect.tsx
import { useState, useRef } from 'react';
import { CheckIcon, ChevronsUpDownIcon, XIcon } from 'lucide-react';
import { Command, CommandEmpty, CommandGroup, CommandInput,
         CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

export interface MultiSelectOption {
  value: string;
  label: string;
  color?: string;   // opțional — pentru badge colorat (statusuri)
}

interface MultiSelectProps {
  value?:       string[];
  onChange:     (values: string[]) => void;
  options:      MultiSelectOption[];
  placeholder?: string;
  maxDisplay?:  number;      // câte tags afișate înainte de "+N mai multe"
  disabled?:    boolean;
  className?:   string;
}

export function MultiSelect({
  value       = [],
  onChange,
  options,
  placeholder = 'Selectează...',
  maxDisplay  = 3,
  disabled    = false,
  className,
}: MultiSelectProps) {
  const [open, setOpen]     = useState(false);
  const [search, setSearch] = useState('');

  const selected  = options.filter((o) => value.includes(o.value));
  const displayed = selected.slice(0, maxDisplay);
  const overflow  = selected.length - maxDisplay;

  const filtered = options.filter((o) =>
    o.label.toLowerCase().includes(search.toLowerCase())
  );

  const toggle = (optionValue: string) => {
    const next = value.includes(optionValue)
      ? value.filter((v) => v !== optionValue)
      : [...value, optionValue];
    onChange(next);
  };

  const removeTag = (e: React.MouseEvent, optionValue: string) => {
    e.stopPropagation();
    onChange(value.filter((v) => v !== optionValue));
  };

  const clearAll = (e: React.MouseEvent) => {
    e.stopPropagation();
    onChange([]);
  };

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          disabled={disabled}
          className={cn(
            'h-auto min-h-10 w-full justify-between px-3 py-2',
            'font-normal text-sm',
            className
          )}
        >
          <div className="flex flex-wrap gap-1 flex-1 min-w-0">
            {selected.length === 0 && (
              <span className="text-muted-foreground">{placeholder}</span>
            )}
            {displayed.map((opt) => (
              <Badge
                key={opt.value}
                variant="secondary"
                className="gap-1 px-2 py-0.5 text-xs font-normal"
                style={opt.color ? { backgroundColor: opt.color + '20',
                  color: opt.color, borderColor: opt.color + '40' } : {}}
              >
                {opt.label}
                <XIcon
                  className="h-3 w-3 cursor-pointer hover:opacity-70"
                  onClick={(e) => removeTag(e, opt.value)}
                />
              </Badge>
            ))}
            {overflow > 0 && (
              <Badge variant="outline" className="text-xs font-normal px-2 py-0.5">
                +{overflow} mai multe
              </Badge>
            )}
          </div>
          <div className="flex items-center gap-1 ml-2 shrink-0">
            {value.length > 0 && (
              <XIcon
                className="h-3.5 w-3.5 text-muted-foreground hover:text-foreground"
                onClick={clearAll}
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
        <Command>
          <CommandInput
            placeholder="Filtrează..."
            value={search}
            onValueChange={setSearch}
          />
          <CommandList>
            {filtered.length === 0 && (
              <CommandEmpty>Niciun rezultat.</CommandEmpty>
            )}
            <CommandGroup>
              {filtered.map((option) => {
                const isSelected = value.includes(option.value);
                return (
                  <CommandItem
                    key={option.value}
                    value={option.value}
                    onSelect={() => toggle(option.value)}
                    className="flex items-center gap-2"
                  >
                    {/* Checkbox vizual */}
                    <div className={cn(
                      'h-4 w-4 rounded border flex items-center justify-center',
                      isSelected
                        ? 'bg-primary-500 border-primary-500'
                        : 'border-border-strong'
                    )}>
                      {isSelected && (
                        <CheckIcon className="h-3 w-3 text-white" />
                      )}
                    </div>
                    <span className="text-sm">{option.label}</span>
                  </CommandItem>
                );
              })}
            </CommandGroup>
          </CommandList>

          {/* Footer cu count */}
          {value.length > 0 && (
            <div className="border-t border-border-subtle px-3 py-2
                            flex items-center justify-between">
              <span className="text-xs text-text-muted">
                {value.length} selectate
              </span>
              <Button
                variant="ghost"
                size="sm"
                className="h-6 px-2 text-xs text-text-secondary"
                onClick={() => onChange([])}
              >
                Șterge tot
              </Button>
            </div>
          )}
        </Command>
      </PopoverContent>
    </Popover>
  );
}
```

---

## 2. Integrare React Hook Form

```tsx
import { MultiSelect } from '@/components/common/MultiSelect/MultiSelect';
import { FormField, FormItem, FormLabel, FormMessage } from '@/components/ui/form';

const roleOptions: MultiSelectOption[] = [
  { value: 'finance.manager',   label: 'Manager Financiar' },
  { value: 'finance.accountant', label: 'Contabil' },
  { value: 'hr.manager',        label: 'Manager HR' },
  { value: 'inventory.manager', label: 'Manager Stocuri' },
];

const schema = z.object({
  roles: z.array(z.string()).min(1, 'Selectați cel puțin un rol'),
});

<FormField
  control={form.control}
  name="roles"
  render={({ field }) => (
    <FormItem>
      <FormLabel>Roluri</FormLabel>
      <MultiSelect
        value={field.value ?? []}
        onChange={field.onChange}
        options={roleOptions}
        placeholder="Selectează roluri..."
        maxDisplay={3}
      />
      <FormMessage />
    </FormItem>
  )}
/>
```

---

## 3. Cu culori pentru statusuri

```tsx
const statusOptions: MultiSelectOption[] = [
  { value: '1', label: 'Draft',      color: '#6B7280' },
  { value: '2', label: 'Aprobat',    color: '#166534' },
  { value: '3', label: 'Respins',    color: '#9F1239' },
  { value: '4', label: 'Întârziat',  color: '#92400E' },
];

// Utilizare în FilterBar pentru filtrare tabel
<MultiSelect
  value={filters.statuses ?? []}
  onChange={(v) => setFilters((f) => ({ ...f, statuses: v }))}
  options={statusOptions}
  placeholder="Toate statusurile"
  maxDisplay={2}
/>
```

## Reguli obligatorii
- `maxDisplay` = 3 default — nu supraaglomera trigger-ul cu tags
- Overflow afișat ca `"+N mai multe"` badge — nu ascunde informația
- Filtrare client-side cu `CommandInput` — pentru < 200 opțiuni
- Pentru > 200 opțiuni → `SearchableSelect` cu server-side search
- `XIcon` pe fiecare tag — remove individual fără a deschide dropdown
- Footer cu count + "Șterge tot" — UX ERP standard
- `color` prop pe opțiuni — pentru statusuri colorate vizibil
