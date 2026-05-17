---
name: ui-status-badge
description: >-
  StatusBadge pentru ERP — toate statusurile standard cu culori corecte
  din design system. Compact, outline, cu iconiță opțional.
  Folosit în tabele, detalii, carduri.
---

# StatusBadge Component

```tsx
// components/common/StatusBadge/StatusBadge.tsx
import { cn } from '@/lib/utils';

export type ERPStatus =
  | 'draft' | 'processing' | 'approved' | 'rejected'
  | 'cancelled' | 'completed' | 'overdue' | 'locked'
  | 'active' | 'inactive' | 'pending';

const STATUS_CONFIG: Record<ERPStatus, {
  label:    string;
  bg:       string;
  text:     string;
  border:   string;
}> = {
  draft:      { label: 'Draft',           bg: '#F3F4F6', text: '#374151', border: '#D1D5DB' },
  processing: { label: 'În procesare',    bg: '#EFF6FF', text: '#1E40AF', border: '#BFDBFE' },
  approved:   { label: 'Aprobat',         bg: '#F0FDF4', text: '#166534', border: '#BBF7D0' },
  rejected:   { label: 'Respins',         bg: '#FFF1F2', text: '#9F1239', border: '#FECDD3' },
  cancelled:  { label: 'Anulat',          bg: '#F9FAFB', text: '#6B7280', border: '#E5E7EB' },
  completed:  { label: 'Finalizat',       bg: '#F0FDF4', text: '#166534', border: '#BBF7D0' },
  overdue:    { label: 'Întârziat',       bg: '#FFFBEB', text: '#92400E', border: '#FDE68A' },
  locked:     { label: 'Blocat',          bg: '#FFF1F2', text: '#9F1239', border: '#FECDD3' },
  active:     { label: 'Activ',           bg: '#F0FDF4', text: '#166534', border: '#BBF7D0' },
  inactive:   { label: 'Inactiv',         bg: '#F9FAFB', text: '#6B7280', border: '#E5E7EB' },
  pending:    { label: 'În așteptare',    bg: '#FFFBEB', text: '#92400E', border: '#FDE68A' },
};

interface StatusBadgeProps {
  status:     ERPStatus | number;   // acceptă enum numeric (1=Draft, 2=Approved etc.)
  statusMap?: Record<number, ERPStatus>;  // mapare numeric → ERPStatus
  className?: string;
  size?:      'sm' | 'md';
}

export function StatusBadge({
  status,
  statusMap,
  className,
  size = 'sm',
}: StatusBadgeProps) {
  // Rezolvă status numeric la string
  const statusKey: ERPStatus = typeof status === 'number'
    ? (statusMap?.[status] ?? 'draft')
    : status;

  const config = STATUS_CONFIG[statusKey];
  if (!config) return null;

  return (
    <span
      className={cn(
        'inline-flex items-center font-medium rounded-full border',
        size === 'sm' ? 'px-2 py-0.5 text-xs' : 'px-3 py-1 text-sm',
        className
      )}
      style={{
        backgroundColor: config.bg,
        color:           config.text,
        borderColor:     config.border,
      }}
    >
      {config.label}
    </span>
  );
}

// Mapare standard pentru Invoice (1-4)
export const INVOICE_STATUS_MAP: Record<number, ERPStatus> = {
  1: 'draft',
  2: 'approved',
  3: 'completed',
  4: 'cancelled',
};

// Utilizare
// <StatusBadge status="approved" />
// <StatusBadge status={invoice.status} statusMap={INVOICE_STATUS_MAP} />
```
