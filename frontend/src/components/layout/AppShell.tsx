import React, { useState, useRef, useEffect } from 'react'
import { Link, useRouterState } from '@tanstack/react-router'
import {
  LayoutDashboard,
  FileText,
  FileInput,
  CreditCard,
  Landmark,
  BookOpen,
  Users,
  Building2,
  FileSignature,
  Package,
  Warehouse,
  ArrowLeftRight,
  Clock,
  Star,
  Search,
  Bell,
  ChevronRight,
  ChevronDown,
  Truck,
  UserSquare2,
  BarChart3,
  Settings,
  TriangleAlert,
  Tag,
  Network,
  User,
  UserCog,
  DollarSign,
} from 'lucide-react'

// ─── Types ────────────────────────────────────────────────────────────────────

interface NavItem {
  label: string
  to: string
  icon: React.ReactNode
  badge?: number
  badgeVariant?: 'primary' | 'warning'
}

interface NavSection {
  title: string
  items: NavItem[]
}

interface ModuleTab {
  label: string
  module: string
  icon: React.ReactNode
}

// ─── Config ───────────────────────────────────────────────────────────────────

const moduleTabs: ModuleTab[] = [
  { label: 'Dashboard', module: 'dashboard', icon: <LayoutDashboard size={15} /> },
  { label: 'Financiar', module: 'finance', icon: <FileText size={15} /> },
  { label: 'Logistică', module: 'logistics', icon: <Truck size={15} /> },
  { label: 'HR', module: 'hr', icon: <UserSquare2 size={15} /> },
  { label: 'Rapoarte', module: 'reports', icon: <BarChart3 size={15} /> },
  { label: 'Administrare', module: 'administration', icon: <Settings size={15} /> },
  { label: 'Setări', module: 'settings', icon: <Settings size={15} /> },
]

const adminDropdownItems = [
  { label: 'Parteneri', to: '/administration/parteneri', icon: <Building2 size={14} /> },
  { label: 'Personal', to: '/administration/persoane', icon: <Users size={14} /> },
  { label: 'Articole', to: '/administration/articole', icon: <Package size={14} /> },
] as const

const sidebarByModule: Record<string, NavSection[]> = {
  administration: [
    {
      title: 'PARTENERI',
      items: [
        { label: 'Parteneri', to: '/administration/parteneri', icon: <Building2 size={15} /> },
        { label: 'Tipuri parteneri', to: '/administration/tipuri-parteneri', icon: <Tag size={15} /> },
        { label: 'Ierarhie parteneri', to: '/administration/ierarhie-parteneri', icon: <Network size={15} /> },
      ],
    },
    {
      title: 'PERSONAL',
      items: [
        { label: 'Persoane', to: '/administration/persoane', icon: <User size={15} /> },
        { label: 'Utilizatori', to: '/administration/utilizatori', icon: <UserCog size={15} /> },
      ],
    },
    {
      title: 'ARTICOLE',
      items: [
        { label: 'Articole', to: '/administration/articole', icon: <Package size={15} /> },
        { label: 'Tipuri de articole', to: '/administration/tipuri-articole', icon: <Tag size={15} /> },
        { label: 'Catalog articole', to: '/administration/catalog-articole', icon: <BookOpen size={15} /> },
        { label: 'Liste de preț', to: '/administration/liste-pret', icon: <DollarSign size={15} /> },
      ],
    },
  ],
  dashboard: [
    {
      title: 'GENERAL',
      items: [
        { label: 'Dashboard', to: '/', icon: <LayoutDashboard size={15} /> },
        { label: 'Activitate recentă', to: '/', icon: <Clock size={15} /> },
        { label: 'Favorite', to: '/', icon: <Star size={15} /> },
      ],
    },
  ],
  finance: [
    {
      title: 'GENERAL',
      items: [
        { label: 'Dashboard', to: '/', icon: <LayoutDashboard size={15} /> },
        { label: 'Activitate recentă', to: '/', icon: <Clock size={15} /> },
        { label: 'Favorite', to: '/', icon: <Star size={15} /> },
      ],
    },
    {
      title: 'FINANCIAR',
      items: [
        { label: 'Facturi emise', to: '/finance/invoices', icon: <FileText size={15} />, badge: 12, badgeVariant: 'primary' },
        { label: 'Facturi primite', to: '/finance/invoices', icon: <FileInput size={15} /> },
        { label: 'Plăți', to: '/finance/invoices', icon: <CreditCard size={15} />, badge: 3, badgeVariant: 'warning' },
        { label: 'Trezorerie', to: '/finance/invoices', icon: <Landmark size={15} /> },
        { label: 'Contabilitate', to: '/finance/invoices', icon: <BookOpen size={15} /> },
      ],
    },
    {
      title: 'PARTENERI',
      items: [
        { label: 'Clienți', to: '/finance/invoices', icon: <Users size={15} /> },
        { label: 'Furnizori', to: '/finance/invoices', icon: <Building2 size={15} /> },
        { label: 'Contracte', to: '/finance/invoices', icon: <FileSignature size={15} /> },
      ],
    },
    {
      title: 'STOCURI',
      items: [
        { label: 'Produse', to: '/finance/invoices', icon: <Package size={15} /> },
        { label: 'Depozite', to: '/finance/invoices', icon: <Warehouse size={15} /> },
        { label: 'Transferuri', to: '/finance/invoices', icon: <ArrowLeftRight size={15} /> },
      ],
    },
  ],
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function getActiveModule(pathname: string): string {
  if (pathname.startsWith('/finance')) return 'finance'
  if (pathname.startsWith('/hr')) return 'hr'
  if (pathname.startsWith('/logistics')) return 'logistics'
  if (pathname.startsWith('/reports')) return 'reports'
  if (pathname.startsWith('/administration')) return 'administration'
  if (pathname.startsWith('/settings')) return 'settings'
  return 'dashboard'
}

function getAdminSections(pathname: string): NavSection[] {
  const all = sidebarByModule.administration
  const parteneriPaths = ['/administration/parteneri', '/administration/tipuri-parteneri', '/administration/ierarhie-parteneri']
  const personalPaths  = ['/administration/persoane', '/administration/utilizatori']
  const articolePaths  = ['/administration/articole', '/administration/tipuri-articole', '/administration/catalog-articole', '/administration/liste-pret']

  if (parteneriPaths.some((p) => pathname.startsWith(p))) return all.filter((s) => s.title === 'PARTENERI')
  if (personalPaths.some((p) => pathname.startsWith(p)))  return all.filter((s) => s.title === 'PERSONAL')
  if (articolePaths.some((p) => pathname.startsWith(p)))  return all.filter((s) => s.title === 'ARTICOLE')
  return all
}

// ─── Badge ────────────────────────────────────────────────────────────────────

function NavBadge({ count, variant }: { count: number; variant: 'primary' | 'warning' }): React.ReactElement {
  const cls =
    variant === 'warning'
      ? 'bg-[#FFF7ED] text-[#C2410C] border border-[#FED7AA]'
      : 'bg-[#EBF5FF] text-[#1670B0] border border-[#C5E2FA]'
  return (
    <span className={`ml-auto inline-flex h-5 min-w-5 items-center justify-center rounded-full px-1.5 text-[11px] font-semibold ${cls}`}>
      {count}
    </span>
  )
}

// ─── SidebarNavItem ───────────────────────────────────────────────────────────

function SidebarNavItem({ item }: { item: NavItem }): React.ReactElement {
  return (
    <Link
      to={item.to}
      activeProps={{ className: 'border-l-2 border-[#1E88D0] bg-[#EBF5FF] pl-[9px] font-medium text-[#1670B0] [&_span.nav-icon]:text-[#1E88D0]' }}
      className="group flex items-center gap-2.5 rounded-md px-2.5 py-[7px] text-[13px] text-[#4B5563] transition-colors hover:bg-[#F1F3F6] hover:text-[#111827]"
    >
      <span className="nav-icon shrink-0 text-[#9CA3AF]">{item.icon}</span>
      <span className="flex-1 truncate">{item.label}</span>
      {item.badge !== undefined && (
        <NavBadge count={item.badge} variant={item.badgeVariant ?? 'primary'} />
      )}
    </Link>
  )
}

// ─── AppShell ─────────────────────────────────────────────────────────────────

interface AppShellProps {
  children: React.ReactNode
}

export default function AppShell({ children }: AppShellProps): React.ReactElement {
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const activeModule = getActiveModule(pathname)
  const sections =
    activeModule === 'administration'
      ? getAdminSections(pathname)
      : (sidebarByModule[activeModule] ?? sidebarByModule.dashboard)
  const [adminOpen, setAdminOpen] = useState(false)
  const adminRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function handleClickOutside(e: MouseEvent): void {
      if (adminRef.current && !adminRef.current.contains(e.target as Node)) {
        setAdminOpen(false)
      }
    }
    if (adminOpen) document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [adminOpen])

  return (
    <div className="flex h-screen flex-col overflow-hidden">
      {/* ── Header ── */}
      <header className="flex h-12 shrink-0 items-center bg-[#1A2B3C] px-4">
        {/* Logo */}
        <div className="flex items-center gap-2 pr-6">
          <div className="flex h-7 w-7 items-center justify-center rounded bg-[#1E88D0] text-white">
            <span className="text-[10px] font-bold leading-none">ERP</span>
          </div>
          <span className="text-[14px] font-semibold text-white">ValyanERP</span>
        </div>

        {/* Module tabs */}
        <nav className="flex h-full items-center">
          {moduleTabs.map((tab) => {
            const isActive = tab.module === activeModule
            const tabClass = [
              'relative flex h-full items-center gap-1.5 px-3.5 text-[13px] font-medium transition-colors',
              isActive
                ? 'text-white after:absolute after:bottom-0 after:left-3.5 after:right-3.5 after:h-[2px] after:rounded-t-sm after:bg-[#2596D9] after:content-[""]'
                : 'text-white/55 hover:text-white/85',
            ].join(' ')

            if (tab.module === 'administration') {
              return (
                <div key={tab.module} className="relative flex h-full items-center" ref={adminRef}>
                  <button
                    className={tabClass}
                    onClick={() => setAdminOpen((o) => !o)}
                  >
                    <span className={isActive ? 'text-[#4AAAE8]' : 'text-white/40'}>{tab.icon}</span>
                    {tab.label}
                    <ChevronDown
                      size={12}
                      className={`transition-transform duration-150 ${adminOpen ? 'rotate-180' : ''}`}
                    />
                  </button>
                  {adminOpen && (
                    <div className="absolute top-full left-0 z-50 mt-0.5 min-w-[160px] rounded-md border border-[#334155] bg-[#1E2D3D] py-1 shadow-lg">
                      {adminDropdownItems.map((item) => (
                        <Link
                          key={item.to}
                          to={item.to}
                          onClick={() => setAdminOpen(false)}
                          className="flex items-center gap-2 px-4 py-2 text-[13px] text-white/70 transition-colors hover:bg-white/10 hover:text-white"
                        >
                          <span className="text-[#4AAAE8]">{item.icon}</span>
                          {item.label}
                        </Link>
                      ))}
                    </div>
                  )}
                </div>
              )
            }

            return (
              <Link
                key={tab.module}
                to={tab.module === 'finance' ? '/finance/invoices' : '/'}
                className={tabClass}
              >
                <span className={isActive ? 'text-[#4AAAE8]' : 'text-white/40'}>{tab.icon}</span>
                {tab.label}
              </Link>
            )
          })}
        </nav>

        {/* Right actions */}
        <div className="ml-auto flex items-center gap-0.5">
          <button className="flex h-8 w-8 items-center justify-center rounded text-white/55 transition-colors hover:bg-white/10 hover:text-white">
            <Search size={16} />
          </button>
          <button className="relative flex h-8 w-8 items-center justify-center rounded text-white/55 transition-colors hover:bg-white/10 hover:text-white">
            <Bell size={16} />
            <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-[#EF4444]" />
          </button>
        </div>
      </header>

      {/* ── Body ── */}
      <div className="flex flex-1 overflow-hidden">
        {/* Sidebar */}
        <aside className="flex w-52 shrink-0 flex-col overflow-y-auto border-r border-[#E5E7EB] bg-[#F8F9FB]">
          <nav className="flex-1 p-2 pt-3">
            {sections.map((section) => (
              <div key={section.title} className="mb-4">
                <p className="mb-1 px-2.5 text-[10px] font-semibold uppercase tracking-wider text-[#9CA3AF]">
                  {section.title}
                </p>
                {section.items.map((item) => (
                  <SidebarNavItem key={item.label} item={item} />
                ))}
              </div>
            ))}
          </nav>
        </aside>

        {/* Main */}
        <main className="flex flex-1 flex-col overflow-auto bg-[#F1F3F6] p-3">
          {children}
        </main>
      </div>
    </div>
  )
}

// ─── AlertBanner ─────────────────────────────────────────────────────────────

export function AlertBanner({
  message,
  linkText,
  linkTo,
}: {
  message: string
  linkText?: string
  linkTo?: string
}): React.ReactElement {
  return (
    <div className="flex items-center gap-2 border-b border-[#FDE68A] bg-[#FFFBEB] px-6 py-2.5 text-[13px] text-[#92400E]">
      <TriangleAlert size={15} className="shrink-0 text-[#F59E0B]" />
      <span>{message}</span>
      {linkText && (
        <Link to={linkTo ?? '/'} className="ml-1 font-semibold underline underline-offset-2 hover:text-[#78350F]">
          {linkText}
        </Link>
      )}
    </div>
  )
}

// ─── Breadcrumb ───────────────────────────────────────────────────────────────

export function Breadcrumb({
  items,
}: {
  items: { label: string; to?: string }[]
}): React.ReactElement {
  return (
    <nav className="flex items-center gap-1 text-[12px] text-[#9CA3AF]">
      {items.map((item, i) => (
        <React.Fragment key={item.label}>
          {i > 0 && <ChevronRight size={11} className="text-[#D1D5DB]" />}
          {item.to ? (
            <Link to={item.to} className="hover:text-[#4B5563] hover:underline">
              {item.label}
            </Link>
          ) : (
            <span className="text-[#4B5563]">{item.label}</span>
          )}
        </React.Fragment>
      ))}
    </nav>
  )
}
