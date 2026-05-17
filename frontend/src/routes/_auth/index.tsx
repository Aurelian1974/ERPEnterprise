import { createFileRoute } from '@tanstack/react-router'
import { FileText, TrendingUp, AlertCircle, Clock, Plus, ArrowUpRight } from 'lucide-react'

export const Route = createFileRoute('/_auth/')({
  component: RouteComponent,
})

interface KpiCardProps {
  label: string
  value: string
  sub?: string
  icon: React.ReactNode
  trend?: 'up' | 'down' | 'neutral'
}

function KpiCard({ label, value, sub, icon, trend }: KpiCardProps): React.ReactElement {
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-[#E5E7EB] bg-white p-4 shadow-[0_1px_2px_0_rgba(0,0,0,0.05)]">
      <div className="flex items-center justify-between">
        <span className="text-[12px] font-medium uppercase tracking-wide text-[#6B7280]">{label}</span>
        <span className="flex h-8 w-8 items-center justify-center rounded-md bg-[#EBF5FF] text-[#1E88D0]">
          {icon}
        </span>
      </div>
      <div>
        <p className="text-[22px] font-semibold leading-none text-[#111827]">{value}</p>
        {sub && (
          <p className="mt-1 text-[12px] text-[#6B7280]">{sub}</p>
        )}
      </div>
      {trend === 'up' && (
        <div className="flex items-center gap-1 text-[11px] font-medium text-[#166534]">
          <ArrowUpRight size={12} />
          <span>față de luna trecută</span>
        </div>
      )}
    </div>
  )
}

function RouteComponent(): React.ReactElement {
  return (
    <div className="flex flex-col">
      {/* Page header */}
      <div className="border-b border-[#E5E7EB] bg-white px-6 py-4">
        <p className="text-[11px] uppercase tracking-wider text-[#9CA3AF]">General</p>
        <h1 className="mt-0.5 text-[18px] font-semibold text-[#111827]">Dashboard</h1>
      </div>

      {/* Content */}
      <div className="p-6">
        {/* KPI row */}
        <div className="grid grid-cols-4 gap-4">
          <KpiCard
            label="Facturi emise"
            value="156"
            sub="luna curentă"
            icon={<FileText size={16} />}
            trend="up"
          />
          <KpiCard
            label="Încasări"
            value="89.420 RON"
            sub="din 124.300 RON emis"
            icon={<TrendingUp size={16} />}
          />
          <KpiCard
            label="Restanțe"
            value="12"
            sub="facturi depășite"
            icon={<AlertCircle size={16} />}
          />
          <KpiCard
            label="Medie încasare"
            value="28 zile"
            sub="față de 32 luna trecută"
            icon={<Clock size={16} />}
            trend="up"
          />
        </div>

        {/* Quick links */}
        <div className="mt-6 grid grid-cols-3 gap-4">
          <div className="rounded-lg border border-[#E5E7EB] bg-white p-4">
            <h2 className="text-[13px] font-semibold text-[#111827]">Acțiuni rapide</h2>
            <div className="mt-3 flex flex-col gap-2">
              <button className="flex items-center gap-2 rounded-md bg-[#1E88D0] px-3 py-2 text-[13px] font-medium text-white transition-colors hover:bg-[#1670B0]">
                <Plus size={14} />
                Factură nouă
              </button>
              <button className="flex items-center gap-2 rounded-md border border-[#E5E7EB] px-3 py-2 text-[13px] font-medium text-[#374151] transition-colors hover:bg-[#F9FAFB]">
                <FileText size={14} />
                Raport lunar
              </button>
            </div>
          </div>

          <div className="col-span-2 rounded-lg border border-[#E5E7EB] bg-white p-4">
            <h2 className="text-[13px] font-semibold text-[#111827]">Activitate recentă</h2>
            <div className="mt-3 space-y-2">
              {[
                { label: 'Factură #F-2024-156 emisă', time: 'acum 10 min', color: '#166534', bg: '#F0FDF4' },
                { label: 'Plată încasată — SC Exemplu SRL', time: 'acum 1 oră', color: '#1E40AF', bg: '#EFF6FF' },
                { label: 'Factură #F-2024-151 scadentă!', time: 'ieri', color: '#9F1239', bg: '#FFF1F2' },
              ].map((ev) => (
                <div key={ev.label} className="flex items-center gap-3 rounded-md px-2 py-1.5">
                  <span className="h-2 w-2 rounded-full" style={{ backgroundColor: ev.color }} />
                  <span className="flex-1 text-[13px] text-[#374151]">{ev.label}</span>
                  <span className="text-[11px] text-[#9CA3AF]">{ev.time}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

