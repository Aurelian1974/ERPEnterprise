import { createFileRoute } from '@tanstack/react-router'
import { AlertBanner, Breadcrumb } from '../../../../components/layout/AppShell'
import { FileText, TrendingUp, AlertCircle, Clock, Plus, Search, Filter } from 'lucide-react'

export const Route = createFileRoute('/_auth/finance/invoices/')({
  component: RouteComponent,
})

// ─── Mock data (to be replaced with real API) ─────────────────────────────────

const mockInvoices = [
  { id: 'F-2024-156', client: 'SC Tehno Build SRL', date: '15.01.2024', value: '12.450,00 RON', status: 'Emisă' },
  { id: 'F-2024-155', client: 'Construct Pro SRL', date: '14.01.2024', value: '8.900,00 RON', status: 'Încasată' },
  { id: 'F-2024-154', client: 'Alpha Trading SA', date: '12.01.2024', value: '23.800,00 RON', status: 'Parțial' },
  { id: 'F-2024-153', client: 'Beta Impex SRL', date: '10.01.2024', value: '5.200,00 RON', status: 'Scadentă' },
  { id: 'F-2024-152', client: 'Delta Services SRL', date: '08.01.2024', value: '16.300,00 RON', status: 'Încasată' },
  { id: 'F-2024-151', client: 'Sigma Grup SA', date: '05.01.2024', value: '7.650,00 RON', status: 'Scadentă' },
  { id: 'F-2024-150', client: 'Omega Prod SRL', date: '03.01.2024', value: '9.100,00 RON', status: 'Emisă' },
]

// ─── StatusBadge ─────────────────────────────────────────────────────────────

const statusStyles: Record<string, string> = {
  Emisă:    'bg-[#F3F4F6] text-[#374151] border-[#E5E7EB]',
  Încasată: 'bg-[#F0FDF4] text-[#166534] border-[#BBF7D0]',
  Parțial:  'bg-[#FFFBEB] text-[#92400E] border-[#FDE68A]',
  Scadentă: 'bg-[#FFF1F2] text-[#9F1239] border-[#FECDD3]',
}

function StatusBadge({ status }: { status: string }): React.ReactElement {
  const cls = statusStyles[status] ?? 'bg-[#F3F4F6] text-[#374151] border-[#E5E7EB]'
  return (
    <span className={`inline-flex items-center rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${cls}`}>
      {status}
    </span>
  )
}

// ─── KpiCard ─────────────────────────────────────────────────────────────────

function KpiCard({
  label,
  value,
  sub,
  icon,
  highlight,
}: {
  label: string
  value: string
  sub: string
  icon: React.ReactNode
  highlight?: boolean
}): React.ReactElement {
  return (
    <div className={`flex flex-col gap-2 rounded-lg border p-4 shadow-[0_1px_2px_0_rgba(0,0,0,0.04)] ${highlight ? 'border-[#FECDD3] bg-[#FFF1F2]' : 'border-[#E5E7EB] bg-white'}`}>
      <div className="flex items-center justify-between">
        <span className="text-[11px] font-medium uppercase tracking-wide text-[#6B7280]">{label}</span>
        <span className={`flex h-7 w-7 items-center justify-center rounded-md ${highlight ? 'bg-[#FECDD3] text-[#9F1239]' : 'bg-[#EBF5FF] text-[#1E88D0]'}`}>
          {icon}
        </span>
      </div>
      <p className={`text-[20px] font-semibold leading-none ${highlight ? 'text-[#9F1239]' : 'text-[#111827]'}`}>{value}</p>
      <p className="text-[11px] text-[#6B7280]">{sub}</p>
    </div>
  )
}

// ─── RouteComponent ───────────────────────────────────────────────────────────

function RouteComponent(): React.ReactElement {
  return (
    <div className="flex flex-col">
      {/* Alert banner */}
      <AlertBanner
        message="3 facturi au depășit scadența —"
        linkText="verificați secțiunea Plăți"
        linkTo="/finance/invoices"
      />

      {/* Page header */}
      <div className="border-b border-[#E5E7EB] bg-white px-6 py-4">
        <Breadcrumb items={[{ label: 'Financiar', to: '/finance/invoices' }, { label: 'Facturi emise' }]} />
        <div className="mt-1 flex items-center justify-between">
          <h1 className="text-[18px] font-semibold text-[#111827]">Facturi emise</h1>
          <button className="flex items-center gap-1.5 rounded-md bg-[#1E88D0] px-3 py-2 text-[13px] font-medium text-white shadow-sm transition-colors hover:bg-[#1670B0]">
            <Plus size={14} />
            Factură nouă
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="p-6">
        {/* KPI cards */}
        <div className="grid grid-cols-4 gap-4">
          <KpiCard
            label="Total emise"
            value="156"
            sub="luna curentă"
            icon={<FileText size={14} />}
          />
          <KpiCard
            label="Încasate"
            value="89"
            sub="57% din total"
            icon={<TrendingUp size={14} />}
          />
          <KpiCard
            label="Restanțe"
            value="12"
            sub="8% din total"
            icon={<AlertCircle size={14} />}
            highlight
          />
          <KpiCard
            label="Medie zile încasare"
            value="28 zile"
            sub="față de 32 luna trecută"
            icon={<Clock size={14} />}
          />
        </div>

        {/* Table card */}
        <div className="mt-5 overflow-hidden rounded-lg border border-[#E5E7EB] bg-white shadow-[0_1px_2px_0_rgba(0,0,0,0.04)]">
          {/* Table toolbar */}
          <div className="flex items-center justify-between border-b border-[#E5E7EB] px-4 py-3">
            <div className="relative">
              <Search size={14} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[#9CA3AF]" />
              <input
                type="text"
                placeholder="Caută factură, client..."
                className="h-8 rounded-md border border-[#E5E7EB] bg-[#F9FAFB] pl-8 pr-3 text-[13px] text-[#111827] placeholder:text-[#9CA3AF] focus:border-[#1E88D0] focus:bg-white focus:outline-none"
              />
            </div>
            <button className="flex items-center gap-1.5 rounded-md border border-[#E5E7EB] px-3 py-1.5 text-[13px] font-medium text-[#374151] hover:bg-[#F9FAFB]">
              <Filter size={13} />
              Filtre
            </button>
          </div>

          {/* Table */}
          <table className="w-full border-collapse text-[13px]">
            <thead>
              <tr className="border-b border-[#E5E7EB] bg-[#F9FAFB]">
                <th className="px-4 py-2.5 text-left text-[11px] font-semibold uppercase tracking-wide text-[#6B7280]">Nr. factură</th>
                <th className="px-4 py-2.5 text-left text-[11px] font-semibold uppercase tracking-wide text-[#6B7280]">Client</th>
                <th className="px-4 py-2.5 text-left text-[11px] font-semibold uppercase tracking-wide text-[#6B7280]">Dată emitere</th>
                <th className="px-4 py-2.5 text-right text-[11px] font-semibold uppercase tracking-wide text-[#6B7280]">Valoare</th>
                <th className="px-4 py-2.5 text-left text-[11px] font-semibold uppercase tracking-wide text-[#6B7280]">Status</th>
              </tr>
            </thead>
            <tbody>
              {mockInvoices.map((inv, i) => (
                <tr
                  key={inv.id}
                  className={`border-b border-[#F3F4F6] transition-colors hover:bg-[#F9FAFB] ${i % 2 === 0 ? 'bg-white' : 'bg-[#FAFAFA]'}`}
                >
                  <td className="px-4 py-3 font-medium text-[#1E88D0]">{inv.id}</td>
                  <td className="px-4 py-3 text-[#374151]">{inv.client}</td>
                  <td className="px-4 py-3 text-[#4B5563]">{inv.date}</td>
                  <td className="px-4 py-3 text-right font-medium text-[#111827]">{inv.value}</td>
                  <td className="px-4 py-3">
                    <StatusBadge status={inv.status} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* Pagination */}
          <div className="flex items-center justify-between border-t border-[#E5E7EB] px-4 py-2.5">
            <span className="text-[12px] text-[#6B7280]">Afișează 1–7 din 156 înregistrări</span>
            <div className="flex items-center gap-1">
              <button className="rounded border border-[#E5E7EB] px-2.5 py-1 text-[12px] text-[#374151] hover:bg-[#F3F4F6] disabled:opacity-40" disabled>
                Anterior
              </button>
              <button className="rounded border border-[#1E88D0] bg-[#1E88D0] px-2.5 py-1 text-[12px] font-medium text-white">1</button>
              <button className="rounded border border-[#E5E7EB] px-2.5 py-1 text-[12px] text-[#374151] hover:bg-[#F3F4F6]">2</button>
              <button className="rounded border border-[#E5E7EB] px-2.5 py-1 text-[12px] text-[#374151] hover:bg-[#F3F4F6]">3</button>
              <button className="rounded border border-[#E5E7EB] px-2.5 py-1 text-[12px] text-[#374151] hover:bg-[#F3F4F6]">
                Următor
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

