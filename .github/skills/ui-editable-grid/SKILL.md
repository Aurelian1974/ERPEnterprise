---
name: ui-editable-grid
description: >-
  EditableGrid Syncfusion pentru linii de document ERP (facturi, comenzi, oferte).
  Batch editing inline, CurrencyInput în template, SearchableSelect pentru produs,
  calcule automate totale. Integrat cu React Hook Form useFieldArray ca fallback.
---

# EditableGrid — Syncfusion Grid cu Batch Editing

## Când se aplică
Orice document ERP cu linii repetabile editabile inline:
facturi, comenzi achiziție, oferte, bonuri de consum.

## Două abordări — alege în funcție de complexitate

```
Syncfusion Batch Edit  → linii simple (qty, price, vat) — recomandat pentru tabele mari
RHF useFieldArray      → linii cu componente custom complexe (SearchableSelect, DatePicker)
                          sau validare Zod per linie necesară
```

---

## 1. Syncfusion Batch Editing (recomandat)

```tsx
// components/common/EditableGrid/EditableGrid.tsx
import { useRef, useState, useEffect } from 'react';
import {
  GridComponent, ColumnsDirective, ColumnDirective,
  Inject, Edit, Toolbar, Page,
  type EditSettingsModel,
  type ToolbarItems,
  type SaveEventArgs,
} from '@syncfusion/ej2-react-grids';
import { NumericTextBoxComponent } from '@syncfusion/ej2-react-inputs';
import { CurrencyDisplay } from '@/components/common/CurrencyInput/CurrencyInput';

export interface GridLine {
  id?:         string | number;   // temporar pentru rânduri noi
  productId?:  string;
  description: string;
  quantity:    number;
  unitPrice:   number;
  vatRate:     number;
  lineTotal?:  number;            // calculat
}

interface EditableGridProps {
  lines:      GridLine[];
  onChange:   (lines: GridLine[]) => void;
  currency?:  string;
  disabled?:  boolean;
}

export function EditableGrid({
  lines,
  onChange,
  currency  = 'RON',
  disabled  = false,
}: EditableGridProps) {
  const gridRef   = useRef<GridComponent>(null);
  const [data, setData] = useState<GridLine[]>(() =>
    lines.map((l) => ({
      ...l,
      lineTotal: l.quantity * l.unitPrice * (1 + l.vatRate),
    }))
  );

  const editSettings: EditSettingsModel = {
    allowEditing:  !disabled,
    allowAdding:   !disabled,
    allowDeleting: !disabled,
    mode:          'Batch',            // editare inline fără dialog
    showConfirmDialog:   false,
    showDeleteConfirmDialog: true,
  };

  const toolbarItems: ToolbarItems[] = disabled
    ? []
    : ['Add', 'Delete', 'Update', 'Cancel'];

  // Recalculare total la fiecare save batch
  const handleBatchSave = (args: SaveEventArgs) => {
    const updated = (gridRef.current?.dataSource as GridLine[]) ?? [];
    const withTotals = updated.map((l) => ({
      ...l,
      lineTotal: (l.quantity ?? 0) * (l.unitPrice ?? 0) * (1 + (l.vatRate ?? 0.19)),
    }));
    setData(withTotals);
    onChange(withTotals);
  };

  // Recalcul la fiecare modificare celulă (pentru afișare total în timp real)
  const handleCellSaved = () => {
    const current = (gridRef.current?.dataSource as GridLine[]) ?? [];
    const withTotals = current.map((l) => ({
      ...l,
      lineTotal: (l.quantity ?? 0) * (l.unitPrice ?? 0) * (1 + (l.vatRate ?? 0.19)),
    }));
    setData(withTotals);
  };

  // Editor numeric custom pentru cantitate și preț
  let quantityElem: HTMLElement;
  let quantityObj: NumericTextBoxComponent;

  const quantityParams = {
    create: () => {
      quantityElem = document.createElement('input');
      return quantityElem;
    },
    read:    () => quantityObj.value ?? 0,
    destroy: () => quantityObj.destroy(),
    write:   (args: { rowData: GridLine; element: HTMLElement }) => {
      quantityObj = new NumericTextBoxComponent({
        value:   args.rowData.quantity,
        min:     0,
        decimals: 3,
        format:  'N3',
        locale:  'ro',
      });
      quantityObj.appendTo(args.element as HTMLElement);
    },
  };

  // Totals calculate
  const subtotal = data.reduce((s, l) => s + (l.quantity ?? 0) * (l.unitPrice ?? 0), 0);
  const vat      = data.reduce((s, l) =>
    s + (l.quantity ?? 0) * (l.unitPrice ?? 0) * (l.vatRate ?? 0.19), 0);
  const total    = subtotal + vat;

  return (
    <div className="space-y-4">
      <GridComponent
        ref={gridRef}
        dataSource={data}
        editSettings={editSettings}
        toolbar={toolbarItems}
        allowPaging={false}
        locale="ro"
        batchSave={handleBatchSave}
        cellSaved={handleCellSaved}
        loadingIndicator={{ indicatorType: 'Shimmer' }}
        className="syncfusion-erp-grid"
      >
        <ColumnsDirective>
          <ColumnDirective
            field="description"
            headerText="Descriere"
            minWidth={150}
            validationRules={{ required: true }}
          />
          <ColumnDirective
            field="quantity"
            headerText="Cantitate"
            width={100}
            textAlign="Right"
            format="N3"
            edit={quantityParams}
            validationRules={{ required: true, min: 0 }}
          />
          <ColumnDirective
            field="unitPrice"
            headerText="Preț unitar"
            width={130}
            textAlign="Right"
            format="N2"
            validationRules={{ required: true, min: 0 }}
          />
          <ColumnDirective
            field="vatRate"
            headerText="TVA %"
            width={90}
            textAlign="Right"
            template={(row: GridLine) => `${Math.round((row.vatRate ?? 0.19) * 100)}%`}
          />
          <ColumnDirective
            field="lineTotal"
            headerText="Total linie"
            width={130}
            textAlign="Right"
            allowEditing={false}
            template={(row: GridLine) => (
              <CurrencyDisplay value={row.lineTotal} currency={currency} />
            )}
          />
        </ColumnsDirective>
        <Inject services={[Edit, Toolbar, Page]} />
      </GridComponent>

      {/* Totals */}
      <div className="border-t border-border-default pt-3 space-y-1 max-w-xs ml-auto">
        {[
          { label: 'Subtotal', value: subtotal },
          { label: 'TVA',      value: vat },
        ].map(({ label, value }) => (
          <div key={label} className="flex justify-between text-sm text-text-secondary">
            <span>{label}</span>
            <CurrencyDisplay value={value} currency={currency} />
          </div>
        ))}
        <div className="flex justify-between text-base font-semibold
                        border-t border-border-default pt-2">
          <span>Total</span>
          <CurrencyDisplay value={total} currency={currency} />
        </div>
      </div>
    </div>
  );
}
```

---

## 2. RHF useFieldArray (pentru linii cu componente custom)

```tsx
// Când ai nevoie de SearchableSelect, DatePicker per linie
// sau validare Zod strictă per câmp

import { useFieldArray, useFormContext } from 'react-hook-form';
import { PlusIcon, Trash2Icon } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { CurrencyInput, CurrencyDisplay } from '@/components/common/CurrencyInput/CurrencyInput';
import { SearchableSelect } from '@/components/common/SearchableSelect/SearchableSelect';
import { searchProducts } from '@/features/inventory/api';

export const invoiceLineSchema = z.object({
  productId:   z.string().uuid('Selectați un produs'),
  description: z.string().min(1),
  quantity:    z.number().positive(),
  unitPrice:   z.number().positive(),
  vatRate:     z.number().min(0).max(1).default(0.19),
});

export function InvoiceLinesFieldArray({ name = 'lines', currency = 'RON', disabled = false }) {
  const form = useFormContext();
  const { fields, append, remove } = useFieldArray({ control: form.control, name });
  const lines = form.watch(name) ?? [];

  const subtotal = lines.reduce((s: number, l: any) =>
    s + (l.quantity ?? 0) * (l.unitPrice ?? 0), 0);
  const vat = lines.reduce((s: number, l: any) =>
    s + (l.quantity ?? 0) * (l.unitPrice ?? 0) * (l.vatRate ?? 0.19), 0);

  return (
    <div className="space-y-3">
      {/* Header */}
      <div className="grid grid-cols-[2fr_1fr_1fr_1fr_1fr_32px] gap-2
                      text-xs font-semibold text-text-secondary uppercase
                      tracking-wide pb-1 border-b border-border-default">
        <span>Produs</span>
        <span className="text-right">Cant.</span>
        <span className="text-right">Preț</span>
        <span className="text-right">TVA</span>
        <span className="text-right">Total</span>
        <span />
      </div>

      {fields.map((field, index) => {
        const line      = lines[index] ?? {};
        const lineTotal = (line.quantity ?? 0) * (line.unitPrice ?? 0)
                        * (1 + (line.vatRate ?? 0.19));
        return (
          <div key={field.id}
               className="grid grid-cols-[2fr_1fr_1fr_1fr_1fr_32px] gap-2 items-center">
            <SearchableSelect
              value={form.watch(`${name}.${index}.productId`) || null}
              onChange={(v) => {
                form.setValue(`${name}.${index}.productId`, v ?? '');
              }}
              queryKey={['products']}
              queryFn={searchProducts}
              placeholder="Produs..."
              disabled={disabled}
            />
            <input
              type="number"
              step="0.001"
              {...form.register(`${name}.${index}.quantity`, { valueAsNumber: true })}
              disabled={disabled}
              className="text-right font-mono text-sm border border-border-default
                         rounded px-2 py-1.5 w-full focus:ring-1 focus:ring-primary-500"
            />
            <CurrencyInput
              value={form.watch(`${name}.${index}.unitPrice`)}
              onChange={(v) => form.setValue(`${name}.${index}.unitPrice`, v ?? 0)}
              currency={currency}
              disabled={disabled}
              align="right"
            />
            <div className="text-right text-sm text-text-secondary">
              {Math.round((form.watch(`${name}.${index}.vatRate`) ?? 0.19) * 100)}%
            </div>
            <CurrencyDisplay value={lineTotal} currency={currency} className="justify-end" />
            {!disabled && (
              <Button type="button" variant="ghost" size="icon"
                className="h-7 w-7 text-danger-icon hover:text-danger-text"
                onClick={() => remove(index)}
                disabled={fields.length === 1}>
                <Trash2Icon className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
        );
      })}

      {!disabled && (
        <Button type="button" variant="outline" size="sm"
          className="text-primary-500 border-primary-200 hover:bg-primary-50"
          onClick={() => append({
            productId: '', description: '', quantity: 1, unitPrice: 0, vatRate: 0.19,
          })}>
          <PlusIcon className="h-4 w-4 mr-1" /> Adaugă linie
        </Button>
      )}

      {/* Totals */}
      <div className="border-t border-border-default pt-3 space-y-1 max-w-xs ml-auto">
        <div className="flex justify-between text-sm text-text-secondary">
          <span>Subtotal</span><CurrencyDisplay value={subtotal} currency={currency} />
        </div>
        <div className="flex justify-between text-sm text-text-secondary">
          <span>TVA</span><CurrencyDisplay value={vat} currency={currency} />
        </div>
        <div className="flex justify-between font-semibold border-t border-border-default pt-2">
          <span>Total</span><CurrencyDisplay value={subtotal + vat} currency={currency} />
        </div>
      </div>
    </div>
  );
}
```

## Când folosești ce

| Criteriu | Syncfusion Batch Edit | RHF useFieldArray |
|---|---|---|
| Linii simple (text, număr) | ✅ Recomandat | Posibil |
| SearchableSelect per linie | ❌ Complicat | ✅ Natural |
| Validare Zod strictă per câmp | ❌ | ✅ |
| Volume mari (50+ linii) | ✅ Virtualizare built-in | ⚠️ Performanță |
| Export Excel linii | ✅ Built-in | ❌ Manual |
