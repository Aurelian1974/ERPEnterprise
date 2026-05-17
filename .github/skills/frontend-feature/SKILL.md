---
name: frontend-feature
description: >-
  Generează un feature frontend complet pentru ERP: api.ts cu TanStack Query hooks,
  schemas.ts cu Zod, tipuri din OpenAPI generated, componentă List, componentă Form,
  permission guard. Fără barrel files, fără fetch direct, fără any.
---

# Frontend Feature

## Când se aplică
Când utilizatorul cere să creeze sau să completeze un feature frontend
(pagină de list, formular de creare/editare, detaliu) pentru orice modul ERP.

## Structura de fișiere

```
src/features/{module}/{entity}/
  api.ts           ← TanStack Query hooks + query keys
  schemas.ts       ← Zod schemas pentru forme
  types.ts         ← tipuri locale dacă nu există în generated
  {Entity}List.tsx ← pagină/componentă de listing
  {Entity}Form.tsx ← formular create/edit
  {Entity}Detail.tsx ← detaliu (opțional)
```

**Reguli structură:**
- Niciodată barrel files (`index.ts` cu re-exporturi)
- Tipuri API din `src/api/generated/` — niciodată scrise manual
- Fiecare fișier are un singur scop

---

## 1. api.ts — Query Keys + Hooks

```typescript
// features/finance/invoices/api.ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/axios';
import type {
  InvoiceListDto,
  InvoiceDetailDto,
  CreateInvoiceRequest,
  UpdateInvoiceRequest,
  PagedResult,
} from '@/api/generated';

// Query keys — obiect centralizat, niciodată strings inline
export const invoiceKeys = {
  all:    ['invoices']                              as const,
  list:   (filters: InvoiceFilters) =>
            [...invoiceKeys.all, 'list', filters]   as const,
  detail: (id: string) =>
            [...invoiceKeys.all, 'detail', id]      as const,
};

// Tipuri pentru filtre
export interface InvoiceFilters {
  page?:       number;
  pageSize?:   number;
  search?:     string;
  status?:     number;
  customerId?: string;
  dateFrom?:   string;
  dateTo?:     string;
}

// LIST
export const useInvoices = (filters: InvoiceFilters = {}) =>
  useQuery({
    queryKey: invoiceKeys.list(filters),
    queryFn:  () =>
      api.get<PagedResult<InvoiceListDto>>('/finance/invoices', {
        params: filters,
      }),
    staleTime: 30_000,
    placeholderData: (prev) => prev,   // keeps old data while fetching
  });

// GET BY ID
export const useInvoice = (id: string) =>
  useQuery({
    queryKey: invoiceKeys.detail(id),
    queryFn:  () =>
      api.get<InvoiceDetailDto>(`/finance/invoices/${id}`),
    enabled:  Boolean(id),
    staleTime: 60_000,
  });

// CREATE
export const useCreateInvoice = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateInvoiceRequest) =>
      api.post<string>('/finance/invoices', data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: invoiceKeys.all });
    },
  });
};

// UPDATE
export const useUpdateInvoice = (id: string) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateInvoiceRequest) =>
      api.put(`/finance/invoices/${id}`, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: invoiceKeys.detail(id) });
      queryClient.invalidateQueries({ queryKey: invoiceKeys.all });
    },
  });
};

// DELETE
export const useDeleteInvoice = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      api.delete(`/finance/invoices/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: invoiceKeys.all });
    },
  });
};
```

---

## 2. schemas.ts — Zod Schemas

```typescript
// features/finance/invoices/schemas.ts
import { z } from 'zod';

export const invoiceLineSchema = z.object({
  productId:   z.string().uuid({ message: 'Select a valid product' }),
  description: z.string().min(1).max(500),
  quantity:    z.number({ invalid_type_error: 'Quantity is required' })
                .positive({ message: 'Quantity must be greater than 0' }),
  unitPrice:   z.number({ invalid_type_error: 'Unit price is required' })
                .positive({ message: 'Unit price must be greater than 0' }),
  vatRate:     z.number().min(0).max(1).default(0.19),
});

export const createInvoiceSchema = z.object({
  customerId: z.string().uuid({ message: 'Select a customer' }),
  dueDate:    z.string().min(1, { message: 'Due date is required' }),
  lines:      z.array(invoiceLineSchema)
               .min(1, { message: 'At least one line is required' }),
});

export const invoiceFiltersSchema = z.object({
  search:     z.string().optional(),
  status:     z.coerce.number().optional(),
  customerId: z.string().uuid().optional(),
  dateFrom:   z.string().optional(),
  dateTo:     z.string().optional(),
});

// Tipuri derivate din schema — niciodată scrise manual
export type CreateInvoiceForm    = z.infer<typeof createInvoiceSchema>;
export type InvoiceLineForm      = z.infer<typeof invoiceLineSchema>;
export type InvoiceFiltersForm   = z.infer<typeof invoiceFiltersSchema>;
```

---

## 3. {Entity}List.tsx — Listing cu Syncfusion Grid

```tsx
// features/finance/invoices/InvoiceList.tsx
import { useState } from 'react';
import { useInvoices, useDeleteInvoice, type InvoiceFilters } from './api';
import { usePermission } from '@/hooks/usePermission';
import { DataTable, type DataTableColumn } from '@/components/common/DataTable/DataTable';
import { StatusBadge, INVOICE_STATUS_MAP } from '@/components/common/StatusBadge/StatusBadge';
import { CurrencyDisplay } from '@/components/common/CurrencyInput/CurrencyInput';
import { ConfirmDialog } from '@/components/common/ConfirmDialog';
import { Button } from '@/components/ui/button';
import { Link } from '@tanstack/react-router';
import type { InvoiceListDto } from '@/api/generated';

export function InvoiceList() {
  const [gridState, setGridState] = useState({ page: 1, pageSize: 25 });
  const [deleteId, setDeleteId]   = useState<string | null>(null);

  const canCreate = usePermission('finance.invoices.create');
  const canDelete = usePermission('finance.invoices.delete');

  const { data, isLoading } = useInvoices(gridState);
  const deleteMutation = useDeleteInvoice();

  const columns: DataTableColumn[] = [
    {
      field:      'invoiceNumber',
      headerText: 'Număr',
      width:      130,
      template:   (row: InvoiceListDto) => (
        <Link
          to="/finance/invoices/$id"
          params={{ id: row.id }}
          className="text-primary-500 hover:underline font-mono"
        >
          {row.invoiceNumber}
        </Link>
      ),
    },
    { field: 'customerName', headerText: 'Client', minWidth: 160 },
    {
      field:      'status',
      headerText: 'Status',
      width:      130,
      template:   (row: InvoiceListDto) => (
        <StatusBadge status={row.status} statusMap={INVOICE_STATUS_MAP} />
      ),
    },
    {
      field:      'dueDate',
      headerText: 'Scadent',
      width:      110,
      format:     'dd.MM.yyyy',
      textAlign:  'Center',
    },
    {
      field:      'totalAmount',
      headerText: 'Total',
      width:      130,
      textAlign:  'Right',
      template:   (row: InvoiceListDto) => (
        <CurrencyDisplay value={row.totalAmount} currency="RON" />
      ),
    },
    ...(canDelete ? [{
      field:      'actions',
      headerText: '',
      width:      80,
      allowSorting:   false,
      allowFiltering: false,
      template:   (row: InvoiceListDto) => (
        <Button
          variant="ghost"
          size="sm"
          className="text-danger-icon hover:text-danger-text h-7"
          onClick={() => setDeleteId(row.id)}
        >
          Șterge
        </Button>
      ),
    }] : []),
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold text-text-primary">Facturi</h1>
        {canCreate && (
          <Link to="/finance/invoices/new">
            <Button>Factură nouă</Button>
          </Link>
        )}
      </div>

      <DataTable
        columns={columns}
        dataSource={data?.items ?? []}
        totalCount={data?.totalCount ?? 0}
        pageSize={gridState.pageSize}
        onStateChange={setGridState}
        loading={isLoading}
        toolbar={['Search', 'ExcelExport']}
      />

      <ConfirmDialog
        open={Boolean(deleteId)}
        title="Ștergere factură"
        description="Această acțiune nu poate fi anulată."
        onConfirm={() => {
          if (deleteId) deleteMutation.mutate(deleteId);
          setDeleteId(null);
        }}
        onCancel={() => setDeleteId(null)}
      />
    </div>
  );
}
```

---

## 4. {Entity}Form.tsx — Formular cu React Hook Form + Zod

```tsx
// features/finance/invoices/InvoiceForm.tsx
import { useFieldArray, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { useCreateInvoice } from './api';
import { createInvoiceSchema, type CreateInvoiceForm } from './schemas';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';

export function InvoiceForm() {
  const navigate  = useNavigate();
  const createMutation = useCreateInvoice();

  const form = useForm<CreateInvoiceForm>({
    resolver:      zodResolver(createInvoiceSchema),
    defaultValues: {
      customerId: '',
      dueDate:    '',
      lines:      [{ productId: '', description: '', quantity: 1, unitPrice: 0, vatRate: 0.19 }],
    },
  });

  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name:    'lines',
  });

  const onSubmit = (data: CreateInvoiceForm): void => {
    createMutation.mutate(data, {
      onSuccess: (id) => navigate({ to: '/finance/invoices/$id', params: { id } }),
    });
  };

  return (
    <Form {...form}>
      {/* niciodată <form> HTML direct — wrapper din shadcn/ui */}
      <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-6">

        <FormField
          control={form.control}
          name="customerId"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Customer</FormLabel>
              <FormControl>
                <Input placeholder="Select customer..." {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="dueDate"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Due Date</FormLabel>
              <FormControl>
                <Input type="date" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        {/* Invoice Lines */}
        <div className="space-y-4">
          {fields.map((field, index) => (
            <div key={field.id} className="flex gap-4 items-end">
              <FormField
                control={form.control}
                name={`lines.${index}.quantity`}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Qty</FormLabel>
                    <FormControl>
                      <Input
                        type="number"
                        {...field}
                        onChange={(e) => field.onChange(Number(e.target.value))}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <Button
                type="button"
                variant="outline"
                onClick={() => remove(index)}
                disabled={fields.length === 1}
              >
                Remove
              </Button>
            </div>
          ))}

          <Button
            type="button"
            variant="outline"
            onClick={() => append({
              productId: '', description: '', quantity: 1, unitPrice: 0, vatRate: 0.19,
            })}
          >
            Add Line
          </Button>
        </div>

        <Button type="submit" disabled={createMutation.isPending}>
          {createMutation.isPending ? 'Saving...' : 'Save Invoice'}
        </Button>
      </form>
    </Form>
  );
}
```

---

## 5. Hooks comune

```typescript
// hooks/usePermission.ts
import { useAuthStore } from '@/store/auth.store';

export const usePermission = (permission: string): boolean => {
  const { permissions } = useAuthStore();
  return permissions.includes('*') || permissions.includes(permission);
};

// hooks/useCurrentUser.ts
import { useAuthStore } from '@/store/auth.store';

export const useCurrentUser = () => useAuthStore((s) => s.user);

// store/auth.store.ts
import { create } from 'zustand';

interface AuthState {
  user:        { id: string; name: string; email: string } | null;
  permissions: string[];
  tenantId:    string | null;
  setAuth:     (user: AuthState['user'], permissions: string[], tenantId: string) => void;
  clear:       () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  user:        null,
  permissions: [],
  tenantId:    null,
  setAuth:     (user, permissions, tenantId) => set({ user, permissions, tenantId }),
  clear:       () => set({ user: null, permissions: [], tenantId: null }),
}));
```

---

## Reguli obligatorii

```
api.ts          — TanStack Query hooks, query keys centralizate, niciodată fetch direct
schemas.ts      — Zod schemas, tipuri derivate cu z.infer, niciodată tipuri manuale
Tipuri API      — din src/api/generated/, niciodată scrise manual
Niciodată any   — unknown + narrowing dacă e necesar
Niciodată       — barrel files (index.ts cu re-exporturi)
Permisiuni      — usePermission() verificat înainte de render butoane destructive
State server    — TanStack Query, nu useState + useEffect + fetch
State UI        — Zustand, nu prop drilling > 2 nivele
Form            — React Hook Form + Zod, niciodată controlled cu useState per câmp
invalidateQueries — după orice mutație de succes
```
