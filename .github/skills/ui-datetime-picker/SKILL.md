---
name: ui-datetime-picker
description: >-
  Componentă custom DatePicker / DateTimePicker / DateRangePicker pentru ERP.
  Format românesc dd.MM.yyyy, locale ro-RO, integrat cu React Hook Form,
  shadcn/ui Popover + Calendar, fără dependențe externe de date.
---

# DateTimePicker — Custom Component

## Când se aplică
Când utilizatorul cere un câmp de dată, dată+oră, sau interval de date
integrat cu React Hook Form + Zod într-un formular ERP.

## Stack
- `shadcn/ui` — Popover, Calendar, Button, Input
- `date-fns` — formatare și parsing, locale română
- `react-hook-form` — integrare via `Controller`
- `zod` — validare schemă

## Instalare dependențe
```bash
npx shadcn@latest add popover calendar
npm install date-fns
```

---

## 1. DatePicker — simplu (dd.MM.yyyy)

```tsx
// components/common/DatePicker/DatePicker.tsx
import { useState } from 'react';
import { format, parse, isValid } from 'date-fns';
import { ro } from 'date-fns/locale';
import { CalendarIcon } from 'lucide-react';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Calendar } from '@/components/ui/calendar';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/utils';

interface DatePickerProps {
  value?:       Date | null;
  onChange:     (date: Date | null) => void;
  placeholder?: string;
  disabled?:    boolean;
  minDate?:     Date;
  maxDate?:     Date;
  className?:   string;
}

export function DatePicker({
  value,
  onChange,
  placeholder = 'zz.ll.aaaa',
  disabled,
  minDate,
  maxDate,
  className,
}: DatePickerProps) {
  const [open, setOpen]         = useState(false);
  const [inputValue, setInput]  = useState(
    value ? format(value, 'dd.MM.yyyy') : ''
  );

  // Typing direct în input
  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const raw = e.target.value;
    setInput(raw);

    // Parsează când are formatul complet
    if (raw.length === 10) {
      const parsed = parse(raw, 'dd.MM.yyyy', new Date());
      if (isValid(parsed)) {
        onChange(parsed);
      }
    }
    if (raw === '') onChange(null);
  };

  const handleCalendarSelect = (date: Date | undefined) => {
    if (date) {
      onChange(date);
      setInput(format(date, 'dd.MM.yyyy'));
    }
    setOpen(false);
  };

  return (
    <div className={cn('flex gap-1', className)}>
      <Input
        value={inputValue}
        onChange={handleInputChange}
        placeholder={placeholder}
        disabled={disabled}
        maxLength={10}
        className="flex-1 font-mono text-sm"
        onBlur={() => {
          // Formatează la blur dacă e dată validă
          if (value) setInput(format(value, 'dd.MM.yyyy'));
        }}
      />
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button
            variant="outline"
            size="icon"
            disabled={disabled}
            className="shrink-0"
          >
            <CalendarIcon className="h-4 w-4" />
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-auto p-0" align="end">
          <Calendar
            mode="single"
            selected={value ?? undefined}
            onSelect={handleCalendarSelect}
            locale={ro}
            fromDate={minDate}
            toDate={maxDate}
            initialFocus
          />
        </PopoverContent>
      </Popover>
    </div>
  );
}
```

---

## 2. DateTimePicker — cu selector oră:minut

```tsx
// components/common/DatePicker/DateTimePicker.tsx
import { useState } from 'react';
import { format, parse, isValid, setHours, setMinutes } from 'date-fns';
import { ro } from 'date-fns/locale';
import { CalendarIcon, ClockIcon } from 'lucide-react';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Calendar } from '@/components/ui/calendar';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

interface DateTimePickerProps {
  value?:    Date | null;
  onChange:  (date: Date | null) => void;
  disabled?: boolean;
}

export function DateTimePicker({ value, onChange, disabled }: DateTimePickerProps) {
  const [open, setOpen] = useState(false);

  const dateStr = value ? format(value, 'dd.MM.yyyy') : '';
  const timeStr = value ? format(value, 'HH:mm') : '';

  const handleDateSelect = (date: Date | undefined) => {
    if (!date) return;
    const base = value ?? new Date();
    const merged = setMinutes(
      setHours(date, base.getHours()),
      base.getMinutes()
    );
    onChange(merged);
    setOpen(false);
  };

  const handleTimeChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const [h, m] = e.target.value.split(':').map(Number);
    if (!value || isNaN(h) || isNaN(m)) return;
    onChange(setMinutes(setHours(value, h), m));
  };

  return (
    <div className="flex gap-1">
      {/* Date part */}
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button
            variant="outline"
            disabled={disabled}
            className="flex-1 justify-start font-normal font-mono text-sm"
          >
            <CalendarIcon className="mr-2 h-4 w-4 shrink-0" />
            {dateStr || <span className="text-muted-foreground">zz.ll.aaaa</span>}
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-auto p-0" align="start">
          <Calendar
            mode="single"
            selected={value ?? undefined}
            onSelect={handleDateSelect}
            locale={ro}
            initialFocus
          />
        </PopoverContent>
      </Popover>

      {/* Time part */}
      <div className="relative">
        <ClockIcon className="absolute left-2 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input
          type="time"
          value={timeStr}
          onChange={handleTimeChange}
          disabled={disabled || !value}
          className="w-28 pl-8 font-mono text-sm"
        />
      </div>
    </div>
  );
}
```

---

## 3. DateRangePicker — interval de date

```tsx
// components/common/DatePicker/DateRangePicker.tsx
import { useState } from 'react';
import { format } from 'date-fns';
import { ro } from 'date-fns/locale';
import type { DateRange } from 'react-day-picker';
import { CalendarIcon } from 'lucide-react';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Calendar } from '@/components/ui/calendar';
import { Button } from '@/components/ui/button';

interface DateRangePickerProps {
  value?:    { from: Date | null; to: Date | null };
  onChange:  (range: { from: Date | null; to: Date | null }) => void;
  disabled?: boolean;
}

export function DateRangePicker({ value, onChange, disabled }: DateRangePickerProps) {
  const [open, setOpen] = useState(false);

  const label = value?.from
    ? value.to
      ? `${format(value.from, 'dd.MM.yyyy')} – ${format(value.to, 'dd.MM.yyyy')}`
      : format(value.from, 'dd.MM.yyyy')
    : 'Selectează interval';

  const handleSelect = (range: DateRange | undefined) => {
    onChange({
      from: range?.from ?? null,
      to:   range?.to   ?? null,
    });
    if (range?.from && range?.to) setOpen(false);
  };

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          disabled={disabled}
          className="w-full justify-start font-normal font-mono text-sm"
        >
          <CalendarIcon className="mr-2 h-4 w-4 shrink-0" />
          {label}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          mode="range"
          selected={{
            from: value?.from ?? undefined,
            to:   value?.to   ?? undefined,
          }}
          onSelect={handleSelect}
          locale={ro}
          numberOfMonths={2}
          initialFocus
        />
      </PopoverContent>
    </Popover>
  );
}
```

---

## 4. Integrare React Hook Form

```tsx
// Utilizare în formular cu Controller
import { Controller, useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { DatePicker } from '@/components/common/DatePicker/DatePicker';
import { DateTimePicker } from '@/components/common/DatePicker/DateTimePicker';
import { DateRangePicker } from '@/components/common/DatePicker/DateRangePicker';
import { FormField, FormItem, FormLabel, FormMessage } from '@/components/ui/form';

const schema = z.object({
  dueDate:      z.date({ required_error: 'Data scadentă este obligatorie' }),
  scheduledAt:  z.date().optional(),
  reportPeriod: z.object({
    from: z.date(),
    to:   z.date(),
  }).optional(),
});

// DatePicker în formular
<FormField
  control={form.control}
  name="dueDate"
  render={({ field }) => (
    <FormItem>
      <FormLabel>Data scadentă</FormLabel>
      <DatePicker
        value={field.value ?? null}
        onChange={field.onChange}
        minDate={new Date()}
      />
      <FormMessage />
    </FormItem>
  )}
/>

// DateTimePicker în formular
<FormField
  control={form.control}
  name="scheduledAt"
  render={({ field }) => (
    <FormItem>
      <FormLabel>Programat la</FormLabel>
      <DateTimePicker
        value={field.value ?? null}
        onChange={field.onChange}
      />
      <FormMessage />
    </FormItem>
  )}
/>
```

---

## 5. Zod schemas pentru date

```typescript
// schemas/date.schemas.ts — reutilizabile în orice formular
import { z } from 'zod';

// Dată obligatorie — nu în trecut
export const futureDateSchema = z
  .date({ required_error: 'Data este obligatorie' })
  .min(new Date(), { message: 'Data nu poate fi în trecut' });

// Dată obligatorie — orice
export const requiredDateSchema = z.date({
  required_error: 'Data este obligatorie',
  invalid_type_error: 'Format dată invalid',
});

// Interval de date
export const dateRangeSchema = z.object({
  from: z.date({ required_error: 'Data de început este obligatorie' }),
  to:   z.date({ required_error: 'Data de sfârșit este obligatorie' }),
}).refine(
  (range) => range.to >= range.from,
  { message: 'Data de sfârșit trebuie să fie după data de început', path: ['to'] }
);

// Din string ISO (pentru API responses)
export const dateFromStringSchema = z.string()
  .transform((s) => new Date(s))
  .pipe(z.date());
```

## Reguli obligatorii
- Format afișat: `dd.MM.yyyy` — întotdeauna, niciodată `yyyy-MM-dd` în UI
- Locale: `ro` din `date-fns/locale` — zilele și lunile în română
- Typing direct în input permis — nu forța utilizatorul să folosească calendarul
- `minDate` și `maxDate` configurabile — ERP are reguli business stricte pe date
- Integrat cu `FormField` + `FormMessage` — erorile Zod apar standard
- Valoarea internă = `Date` object — conversie la string ISO doar la trimitere API
