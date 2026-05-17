---
name: ui-step-wizard
description: >-
  StepWizard pentru formulare multi-step ERP: validare per step cu Zod,
  progress indicator, back/next navigation, submit la final.
  PrintLayout pentru documente printabile cu @media print.
  AuditTrail — timeline din audit.audit_log.
---

# StepWizard, PrintLayout, AuditTrail

---

## StepWizard

```tsx
// components/common/StepWizard/StepWizard.tsx
import { useState } from 'react';
import { CheckIcon } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { ZodSchema } from 'zod';
import type { UseFormReturn } from 'react-hook-form';

export interface WizardStep {
  id:          string;
  title:       string;
  description?: string;
  schema?:     ZodSchema;         // validare Zod pentru acest step
  component:   React.ReactNode;
}

interface StepWizardProps {
  steps:       WizardStep[];
  form:        UseFormReturn<any>;
  onSubmit:    (data: any) => void;
  isSubmitting?: boolean;
}

export function StepWizard({
  steps,
  form,
  onSubmit,
  isSubmitting,
}: StepWizardProps) {
  const [current, setCurrent] = useState(0);
  const isFirst = current === 0;
  const isLast  = current === steps.length - 1;

  const goNext = async () => {
    const step = steps[current];
    if (step.schema) {
      // Validează doar câmpurile din step-ul curent
      const values = form.getValues();
      const result = step.schema.safeParse(values);
      if (!result.success) {
        // Triggerează erorile RHF pentru câmpurile invalide
        result.error.issues.forEach((issue) => {
          const path = issue.path.join('.');
          form.setError(path as any, { message: issue.message });
        });
        return;
      }
    }
    setCurrent((c) => c + 1);
  };

  const goBack = () => setCurrent((c) => c - 1);

  return (
    <div className="space-y-6">
      {/* Progress indicator */}
      <div className="flex items-center">
        {steps.map((step, index) => (
          <div key={step.id} className="flex items-center flex-1 last:flex-none">
            {/* Circle */}
            <div className={cn(
              'h-8 w-8 rounded-full flex items-center justify-center',
              'text-xs font-semibold shrink-0 border-2 transition-colors',
              index < current
                ? 'bg-primary-500 border-primary-500 text-white'
                : index === current
                  ? 'border-primary-500 text-primary-600'
                  : 'border-border-strong text-text-muted'
            )}>
              {index < current
                ? <CheckIcon className="h-4 w-4" />
                : index + 1
              }
            </div>

            {/* Label */}
            <div className="ml-2 hidden sm:block">
              <p className={cn(
                'text-xs font-medium',
                index === current ? 'text-primary-600' : 'text-text-muted'
              )}>
                {step.title}
              </p>
            </div>

            {/* Connector */}
            {index < steps.length - 1 && (
              <div className={cn(
                'flex-1 h-0.5 mx-3',
                index < current ? 'bg-primary-500' : 'bg-border-default'
              )} />
            )}
          </div>
        ))}
      </div>

      {/* Step content */}
      <div className="min-h-[300px]">
        <div className="mb-4">
          <h3 className="text-base font-semibold">{steps[current].title}</h3>
          {steps[current].description && (
            <p className="text-sm text-text-muted mt-1">
              {steps[current].description}
            </p>
          )}
        </div>
        {steps[current].component}
      </div>

      {/* Navigation */}
      <div className="flex justify-between pt-4 border-t border-border-default">
        <Button
          type="button"
          variant="outline"
          onClick={goBack}
          disabled={isFirst}
        >
          Înapoi
        </Button>

        {isLast ? (
          <Button
            type="button"
            onClick={form.handleSubmit(onSubmit)}
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Se salvează...' : 'Finalizează'}
          </Button>
        ) : (
          <Button type="button" onClick={goNext}>
            Continuă
          </Button>
        )}
      </div>
    </div>
  );
}
```

---

## PrintLayout — Document Printabil

```tsx
// components/common/PrintLayout/PrintLayout.tsx
// CSS @media print — ascunde UI, afișează doar documentul

interface PrintLayoutProps {
  children:  React.ReactNode;
  title?:    string;   // titlu document în header print
}

export function PrintLayout({ children, title }: PrintLayoutProps) {
  return (
    <>
      {/* Buton print — ascuns la printare */}
      <div className="print:hidden mb-4 flex gap-2">
        <Button variant="outline" onClick={() => window.print()}>
          Printează
        </Button>
      </div>

      {/* Conținut document */}
      <div className="print:block">
        {children}
      </div>

      {/* Stiluri print */}
      <style>{`
        @media print {
          /* Ascunde UI */
          nav, aside, header, .print\\:hidden { display: none !important; }

          /* Dimensiuni pagină A4 */
          @page {
            size: A4 portrait;
            margin: 15mm 20mm;
          }

          body {
            font-size: 11pt;
            color: #000;
            background: #fff;
          }

          /* Evită ruperea tabelelor între pagini */
          table { page-break-inside: avoid; }
          tr    { page-break-inside: avoid; }

          /* Header pe fiecare pagină */
          thead { display: table-header-group; }
          tfoot { display: table-footer-group; }
        }
      `}</style>
    </>
  );
}

// Template factură printabilă
function InvoicePrint({ invoice }: { invoice: InvoiceDetailDto }) {
  return (
    <PrintLayout title={`Factură ${invoice.invoiceNumber}`}>
      <div className="p-8 max-w-3xl mx-auto">
        {/* Header */}
        <div className="flex justify-between mb-8">
          <div>
            <h1 className="text-2xl font-bold">FACTURĂ FISCALĂ</h1>
            <p className="text-lg font-semibold">{invoice.invoiceNumber}</p>
          </div>
          <div className="text-right text-sm">
            <p>Data: {new Date(invoice.createdAt).toLocaleDateString('ro-RO')}</p>
            <p>Scadent: {new Date(invoice.dueDate).toLocaleDateString('ro-RO')}</p>
          </div>
        </div>

        {/* Furnizor / Client */}
        <div className="grid grid-cols-2 gap-8 mb-8 text-sm">
          <div>
            <h3 className="font-semibold mb-1">Furnizor</h3>
            {/* date furnizor */}
          </div>
          <div>
            <h3 className="font-semibold mb-1">Client</h3>
            <p>{invoice.customerName}</p>
          </div>
        </div>

        {/* Linii */}
        <table className="w-full text-sm border-collapse mb-6">
          <thead>
            <tr className="border-b-2 border-black">
              <th className="text-left py-2">Descriere</th>
              <th className="text-right py-2">Cant.</th>
              <th className="text-right py-2">Preț unit.</th>
              <th className="text-right py-2">TVA</th>
              <th className="text-right py-2">Total</th>
            </tr>
          </thead>
          <tbody>
            {invoice.lines?.map((line, i) => (
              <tr key={i} className="border-b border-gray-200">
                <td className="py-1">{line.description}</td>
                <td className="text-right py-1">{line.quantity}</td>
                <td className="text-right py-1 font-mono">
                  {line.unitPrice.toLocaleString('ro-RO', { minimumFractionDigits: 2 })}
                </td>
                <td className="text-right py-1">{line.vatRate * 100}%</td>
                <td className="text-right py-1 font-mono">
                  {line.lineTotal.toLocaleString('ro-RO', { minimumFractionDigits: 2 })}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="border-t-2 border-black font-semibold">
              <td colSpan={4} className="text-right py-2">TOTAL:</td>
              <td className="text-right py-2 font-mono">
                {invoice.totalAmount.toLocaleString('ro-RO', {
                  minimumFractionDigits: 2
                })} RON
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </PrintLayout>
  );
}
```

---

## AuditTrail — Timeline Modificări

```tsx
// components/common/AuditTrail/AuditTrail.tsx
import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/axios';
import { format } from 'date-fns';
import { ro } from 'date-fns/locale';

interface AuditEntry {
  id:          number;
  action:      string;
  userName:    string;
  oldValues?:  Record<string, unknown>;
  newValues?:  Record<string, unknown>;
  createdAt:   string;
}

interface AuditTrailProps {
  entityType: string;
  entityId:   string;
}

export function AuditTrail({ entityType, entityId }: AuditTrailProps) {
  const { data = [], isLoading } = useQuery({
    queryKey: ['audit', entityType, entityId],
    queryFn:  () => api.get<AuditEntry[]>('/administration/audit', {
      params: { entityType, entityId }
    }),
    staleTime: 60_000,
  });

  if (isLoading) {
    return <div className="space-y-3">
      {[1,2,3].map(i => (
        <div key={i} className="h-16 bg-surface-muted rounded animate-pulse" />
      ))}
    </div>;
  }

  if (data.length === 0) {
    return (
      <p className="text-sm text-text-muted text-center py-8">
        Nu există istoric modificări.
      </p>
    );
  }

  return (
    <div className="space-y-0">
      {data.map((entry, index) => (
        <div key={entry.id} className="flex gap-3">
          {/* Timeline line */}
          <div className="flex flex-col items-center">
            <div className="h-2.5 w-2.5 rounded-full bg-primary-400 mt-1.5 shrink-0" />
            {index < data.length - 1 && (
              <div className="flex-1 w-px bg-border-default mt-1" />
            )}
          </div>

          {/* Content */}
          <div className="pb-4 min-w-0 flex-1">
            <div className="flex items-baseline gap-2">
              <span className="text-sm font-medium text-text-primary">
                {entry.userName}
              </span>
              <span className="text-xs text-text-muted shrink-0">
                {format(new Date(entry.createdAt), 'dd.MM.yyyy HH:mm', { locale: ro })}
              </span>
            </div>
            <p className="text-sm text-text-secondary mt-0.5">
              {entry.action}
            </p>
            {/* Diferențe valori */}
            {entry.newValues && Object.keys(entry.newValues).length > 0 && (
              <div className="mt-1 text-xs text-text-muted space-y-0.5">
                {Object.entries(entry.newValues).map(([key, newVal]) => {
                  const oldVal = entry.oldValues?.[key];
                  if (oldVal === newVal) return null;
                  return (
                    <div key={key}>
                      <span className="font-medium">{key}:</span>{' '}
                      <span className="line-through text-danger-text">
                        {String(oldVal ?? '—')}
                      </span>
                      {' → '}
                      <span className="text-success-text">
                        {String(newVal ?? '—')}
                      </span>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
```
