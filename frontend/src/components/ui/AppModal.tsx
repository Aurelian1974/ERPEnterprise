import { Loader2, X } from 'lucide-react'

// ─── Types ────────────────────────────────────────────────────────────────────

const SIZE_CLASSES = {
  sm: 'max-w-sm',
  md: 'max-w-md',
  lg: 'max-w-lg',
  xl: 'max-w-xl',
} as const

type ModalSize = keyof typeof SIZE_CLASSES

export interface AppModalProps {
  title: string
  subtitle?: string
  icon?: React.ReactNode
  size?: ModalSize
  scrollable?: boolean
  onClose: () => void
  children: React.ReactNode
  footer?: React.ReactNode
}

export interface AppModalFooterProps {
  onClose: () => void
  pending?: boolean
  disabled?: boolean
  submitLabel?: string
  cancelLabel?: string
  /** Set to true for a subtle bg on the footer (matches PartnerType style) */
  subtle?: boolean
}

// ─── AppModal ─────────────────────────────────────────────────────────────────

export function AppModal({
  title,
  subtitle,
  icon,
  size = 'lg',
  scrollable = false,
  onClose,
  children,
  footer,
}: AppModalProps) {
  return (
    <div className="fixed inset-0 z-[9999] flex items-center justify-center bg-black/40 p-4 backdrop-blur-sm">
      <div
        className={`flex w-full ${SIZE_CLASSES[size]} flex-col overflow-hidden rounded-xl bg-white shadow-2xl ring-1 ring-black/8`}
      >
        {/* Header */}
        <div className="flex items-center gap-3 border-b border-[#E5E7EB] px-6 py-4">
          {icon && (
            <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-[#EBF5FF]">
              {icon}
            </div>
          )}
          <div className="min-w-0 flex-1">
            <h2 className="text-sm font-semibold leading-tight text-[#111827]">{title}</h2>
            {subtitle && (
              <p className="truncate text-xs text-[#9CA3AF]">{subtitle}</p>
            )}
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg p-1.5 text-[#9CA3AF] transition-colors hover:bg-[#F1F3F6] hover:text-[#4B5563]"
          >
            <X size={16} />
          </button>
        </div>

        {/* Body */}
        <div
          className={scrollable ? 'overflow-y-auto' : undefined}
          style={scrollable ? { maxHeight: 'calc(100vh - 200px)' } : undefined}
        >
          {children}
        </div>

        {/* Footer (optional) */}
        {footer}
      </div>
    </div>
  )
}

// ─── AppModalFooter ───────────────────────────────────────────────────────────

export function AppModalFooter({
  onClose,
  pending = false,
  disabled = false,
  submitLabel = 'Salvează',
  cancelLabel = 'Anulare',
  subtle = false,
}: AppModalFooterProps) {
  return (
    <div
      className={`flex items-center justify-end gap-2.5 border-t border-[#E5E7EB] px-6 py-4 ${subtle ? 'bg-[#F8F9FB]' : ''}`}
    >
      <button
        type="button"
        onClick={onClose}
        disabled={pending}
        className="rounded-lg border border-[#E5E7EB] bg-white px-4 py-2 text-sm font-medium text-[#4B5563] transition-colors hover:bg-[#F1F3F6] disabled:opacity-50"
      >
        {cancelLabel}
      </button>
      <button
        type="submit"
        disabled={pending || disabled}
        className="flex items-center gap-2 rounded-lg bg-[#185FA5] px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-[#1E88D0] disabled:opacity-60"
      >
        {pending && <Loader2 size={13} className="animate-spin" />}
        {submitLabel}
      </button>
    </div>
  )
}
