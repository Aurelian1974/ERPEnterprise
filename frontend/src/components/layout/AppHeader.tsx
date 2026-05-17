import { useRef, useState, useEffect } from 'react'
import { Link, useRouterState } from '@tanstack/react-router'
import { Search, Bell, Grid3X3, Building2, Users, Package } from 'lucide-react'
import { cn } from '../../lib/utils'
import { moduleTabs } from './nav-config'

const adminDropdownItems = [
  { label: 'Parteneri', to: '/administration/parteneri', icon: Building2 },
  { label: 'Personal', to: '/administration/persoane', icon: Users },
  { label: 'Articole', to: '/administration/articole', icon: Package },
] as const

export default function AppHeader(): React.ReactElement {
  const { location } = useRouterState()
  const pathname = location.pathname
  const [adminDropdownOpen, setAdminDropdownOpen] = useState(false)
  const dropdownRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function handleClickOutside(e: MouseEvent): void {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setAdminDropdownOpen(false)
      }
    }
    if (adminDropdownOpen) {
      document.addEventListener('mousedown', handleClickOutside)
    }
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [adminDropdownOpen])

  function getActiveModuleId(): string {
    // Check specific modules first (most specific prefix wins), fall back to dashboard
    for (const tab of moduleTabs) {
      if (tab.id === 'dashboard') continue
      if (pathname.startsWith(tab.matchPrefix)) return tab.id
    }
    return 'dashboard'
  }

  const activeModuleId = getActiveModuleId()

  return (
    <header className="flex h-14 shrink-0 items-stretch border-b border-primary-700 bg-primary-600 px-4 z-10">
      {/* Logo */}
      <div className="flex items-center gap-2 pr-5 border-r border-primary-500 mr-3">
        <div className="flex h-8 w-8 items-center justify-center rounded-md bg-white">
          <Grid3X3 size={16} className="text-primary-600" />
        </div>
        <span className="text-sm font-bold text-white tracking-tight whitespace-nowrap">
          ValyanERP
        </span>
      </div>

      {/* Module tabs */}
      <nav className="flex items-stretch gap-0.5">
        {moduleTabs.map((tab) => {
          const Icon = tab.icon
          const isActive = activeModuleId === tab.id
          const tabClass = cn(
            'flex items-center gap-1.5 px-3 text-sm font-medium border-b-2 transition-colors whitespace-nowrap',
            isActive
              ? 'border-white text-white'
              : 'border-transparent text-primary-100 hover:text-white hover:border-white/50',
          )

          // Administrare tab with dropdown
          if (tab.id === 'administration') {
            return (
              <div key={tab.id} className="relative flex items-stretch" ref={dropdownRef}>
                <button
                  className={cn(tabClass, 'gap-1.5')}
                  onClick={() => setAdminDropdownOpen((o) => !o)}
                  aria-haspopup="true"
                  aria-expanded={adminDropdownOpen}
                >
                  <Icon size={14} />
                  {tab.label}
                  <svg
                    width="12"
                    height="12"
                    viewBox="0 0 12 12"
                    className={cn('transition-transform duration-150', adminDropdownOpen && 'rotate-180')}
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                  >
                    <path d="M2 4l4 4 4-4" />
                  </svg>
                </button>
                {adminDropdownOpen && (
                  <div className="absolute top-full left-0 mt-0.5 min-w-[180px] rounded-md border border-primary-200 bg-white py-1 shadow-lg z-50">
                    {adminDropdownItems.map((item) => {
                      const ItemIcon = item.icon
                      return (
                        <Link
                          key={item.to}
                          // eslint-disable-next-line @typescript-eslint/no-explicit-any
                          to={item.to as any}
                          onClick={() => setAdminDropdownOpen(false)}
                          className="flex items-center gap-2 px-4 py-2 text-sm text-primary-800 hover:bg-primary-50 hover:text-primary-600 transition-colors"
                        >
                          <ItemIcon size={15} className="text-primary-400" />
                          {item.label}
                        </Link>
                      )
                    })}
                  </div>
                )}
              </div>
            )
          }

          if (tab.to !== null) {
            return (
              // eslint-disable-next-line @typescript-eslint/no-explicit-any
              <Link key={tab.id} to={tab.to as any} className={tabClass}>
                <Icon size={14} />
                {tab.label}
              </Link>
            )
          }

          return (
            <button
              key={tab.id}
              className={cn(tabClass, 'cursor-not-allowed opacity-50')}
              disabled
              title="Em curând"
            >
              <Icon size={14} />
              {tab.label}
            </button>
          )
        })}
      </nav>

      {/* Spacer */}
      <div className="flex-1" />

      {/* Right actions */}
      <div className="flex items-center gap-0.5">
        <button
          className="flex h-8 w-8 items-center justify-center rounded-md text-primary-100
                     hover:bg-primary-700 hover:text-white transition-colors"
          aria-label="Căutare"
        >
          <Search size={16} />
        </button>

        <button
          className="relative flex h-8 w-8 items-center justify-center rounded-md text-primary-100
                     hover:bg-primary-700 hover:text-white transition-colors"
          aria-label="Notificări"
        >
          <Bell size={16} />
          <span
            className="absolute top-1 right-1 flex h-4 w-4 items-center justify-center
                       rounded-full bg-danger-icon text-[10px] font-semibold text-white"
          >
            3
          </span>
        </button>

        <button
          className="ml-1 flex h-8 w-8 items-center justify-center rounded-full
                     bg-white text-primary-600 text-xs font-semibold
                     hover:bg-primary-50 transition-colors"
          aria-label="Profil utilizator"
        >
          A
        </button>
      </div>
    </header>
  )
}
