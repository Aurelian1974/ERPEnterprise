import { cn } from '../../lib/utils'

export type InvoiceStatusLabel =
  | 'Emisă'
  | 'Încasată'
  | 'Parțial'
  | 'Scadentă'
  | 'Anulată'
  | 'Draft'

const statusStyles: Record<
  InvoiceStatusLabel,
  { bg: string; text: string; border: string }
> = {
  Emisă: {
    bg: 'bg-primary-50',
    text: 'text-primary-700',
    border: 'border-primary-200',
  },
  Încasată: {
    bg: 'bg-success-bg',
    text: 'text-success-text',
    border: 'border-success-border',
  },
  Parțial: {
    bg: 'bg-warning-bg',
    text: 'text-warning-text',
    border: 'border-warning-border',
  },
  Scadentă: {
    bg: 'bg-danger-bg',
    text: 'text-danger-text',
    border: 'border-danger-border',
  },
  Anulată: {
    bg: 'bg-[#F9FAFB]',
    text: 'text-[#6B7280]',
    border: 'border-[#E5E7EB]',
  },
  Draft: {
    bg: 'bg-[#F3F4F6]',
    text: 'text-[#374151]',
    border: 'border-[#D1D5DB]',
  },
}

interface StatusBadgeProps {
  status: InvoiceStatusLabel
  className?: string
}

export default function StatusBadge({ status, className }: StatusBadgeProps): React.ReactElement {
  const styles = statusStyles[status]
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium',
        styles.bg,
        styles.text,
        styles.border,
        className,
      )}
    >
      {status}
    </span>
  )
}
