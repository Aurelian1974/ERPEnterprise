import { useState, useEffect } from 'react'
import { Link, useRouterState } from '@tanstack/react-router'
import { ChevronRight } from 'lucide-react'
import { cn } from '../../lib/utils'
import { sidebarSections, moduleTabs } from './nav-config'

export default function AppSidebar(): React.ReactElement {
  const { location } = useRouterState()
  const pathname = location.pathname

  function getActiveModuleId(): string {
    for (const tab of moduleTabs) {
      if (tab.id === 'dashboard') continue
      if (pathname.startsWith(tab.matchPrefix)) return tab.id
    }
    return 'dashboard'
  }

  const activeModuleId = getActiveModuleId()

  const visibleSections = sidebarSections.filter((s) => s.moduleId === activeModuleId)

  // Initialize: expand all visible sections
  const [expanded, setExpanded] = useState<Set<string>>(
    () => new Set(sidebarSections.filter((s) => s.moduleId === activeModuleId).map((s) => s.label)),
  )

  // Auto-expand all sections when active module changes
  useEffect(() => {
    setExpanded(new Set(sidebarSections.filter((s) => s.moduleId === activeModuleId).map((s) => s.label)))
  }, [activeModuleId])

  function toggleSection(label: string): void {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(label)) {
        next.delete(label)
      } else {
        next.add(label)
      }
      return next
    })
  }

  return (
    <aside className="flex w-[200px] shrink-0 flex-col border-r border-primary-200 bg-primary-50 overflow-y-auto">
      {visibleSections.map((section) => {
        const isExpanded = expanded.has(section.label)

        return (
          <div key={section.label} className="pt-2">
            {/* Section header — click to toggle */}
            <button
              onClick={() => toggleSection(section.label)}
              className="flex w-full items-center justify-between px-3 py-1.5 text-[11px] font-semibold uppercase tracking-wider text-gray-500 hover:text-gray-700 transition-colors"
            >
              {section.label}
              <ChevronRight
                size={12}
                className={cn('transition-transform duration-200', isExpanded && 'rotate-90')}
              />
            </button>

            {/* Animated expand/collapse via CSS Grid */}
            <div
              className={cn(
                'grid transition-[grid-template-rows] duration-200 ease-in-out',
                isExpanded ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]',
              )}
            >
              <div className="overflow-hidden">
                {section.items.map((item) => {
                  const Icon = item.icon

                  const content = (
                    <>
                      <Icon size={15} className="shrink-0" />
                      <span className="flex-1 truncate">{item.label}</span>
                      {item.badge != null && (
                        <span
                          className={cn(
                            'flex h-5 min-w-5 items-center justify-center rounded-full px-1.5 text-[11px] font-semibold',
                            item.badgeUrgent
                              ? 'bg-danger-bg text-danger-text'
                              : 'bg-primary-100 text-primary-700',
                          )}
                        >
                          {item.badge}
                        </span>
                      )}
                    </>
                  )

                  if (!item.to) {
                    return (
                      <div
                        key={item.label}
                        className="flex items-center gap-2 px-3 py-1.5 text-sm text-gray-400 cursor-default select-none"
                      >
                        {content}
                      </div>
                    )
                  }

                  return (
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    <Link
                      key={item.label}
                      to={item.to as any}
                      activeOptions={{ exact: item.to === '/' }}
                      className={cn(
                        'flex items-center gap-2 px-3 py-1.5 text-sm text-gray-600',
                        'hover:bg-primary-100 transition-colors',
                        '[&.active]:border-l-2 [&.active]:border-primary-500',
                        '[&.active]:bg-primary-100 [&.active]:text-primary-700 [&.active]:font-medium',
                      )}
                    >
                      {content}
                    </Link>
                  )
                })}
              </div>
            </div>
          </div>
        )
      })}
      <div className="pb-4" />
    </aside>
  )
}
