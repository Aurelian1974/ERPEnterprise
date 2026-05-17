import type { LucideIcon } from 'lucide-react'
import {
  LayoutDashboard,
  Clock,
  Star,
  FileText,
  FileInput,
  CreditCard,
  Landmark,
  BookOpen,
  Users,
  Package,
  Warehouse,
  ArrowLeftRight,
  BarChart2,
  Settings,
  Package2,
  Users2,
  Building2,
  Tag,
  Network,
  User,
  UserCog,
  DollarSign,
} from 'lucide-react'

export interface NavItem {
  label: string
  /** Undefined = not yet implemented, renders non-interactive */
  to?: string
  icon: LucideIcon
  badge?: number
  badgeUrgent?: boolean
}

export interface NavSection {
  label: string
  /** Matches ModuleTab.id — used for auto-expand when header tab is clicked */
  moduleId?: string
  items: NavItem[]
}

export interface ModuleTab {
  id: string
  label: string
  icon: LucideIcon
  /** Route to navigate to when clicked. Null = not yet implemented. */
  to: string | null
  /** Used to detect which module tab is active based on current pathname */
  matchPrefix: string
}

export const moduleTabs: ModuleTab[] = [
  {
    id: 'dashboard',
    label: 'Dashboard',
    icon: LayoutDashboard,
    to: '/',
    matchPrefix: '/',
  },
  {
    id: 'finance',
    label: 'Financiar',
    icon: FileText,
    to: '/finance/invoices',
    matchPrefix: '/finance',
  },
  {
    id: 'logistics',
    label: 'Logistică',
    icon: Package2,
    to: null,
    matchPrefix: '/logistics',
  },
  {
    id: 'hr',
    label: 'HR',
    icon: Users2,
    to: null,
    matchPrefix: '/hr',
  },
  {
    id: 'reports',
    label: 'Rapoarte',
    icon: BarChart2,
    to: null,
    matchPrefix: '/reports',
  },
  {
    id: 'administration',
    label: 'Administrare',
    icon: Building2,
    to: '/administration',
    matchPrefix: '/administration',
  },
  {
    id: 'settings',
    label: 'Setări',
    icon: Settings,
    to: null,
    matchPrefix: '/settings',
  },
]

export const sidebarSections: NavSection[] = [
  {
    label: 'GENERAL',
    moduleId: 'dashboard',
    items: [
      { label: 'Dashboard', to: '/', icon: LayoutDashboard },
      { label: 'Activitate recentă', icon: Clock },
      { label: 'Favorite', icon: Star },
    ],
  },
  {
    label: 'FINANCIAR',
    moduleId: 'finance',
    items: [
      { label: 'Facturi emise', to: '/finance/invoices', icon: FileText, badge: 12 },
      { label: 'Facturi primite', icon: FileInput },
      { label: 'Plăți', icon: CreditCard, badge: 3, badgeUrgent: true },
      { label: 'Trezorerie', icon: Landmark },
      { label: 'Contabilitate', icon: BookOpen },
    ],
  },
  {
    label: 'PARTENERI',
    moduleId: 'administration',
    items: [
      { label: 'Parteneri', to: '/administration/parteneri', icon: Users },
      { label: 'Tipuri parteneri', to: '/administration/tipuri-parteneri', icon: Tag },
      { label: 'Ierarhie parteneri', to: '/administration/ierarhie-parteneri', icon: Network },
    ],
  },
  {
    label: 'PERSONAL',
    moduleId: 'administration',
    items: [
      { label: 'Persoane', to: '/administration/persoane', icon: User },
      { label: 'Utilizatori', to: '/administration/utilizatori', icon: UserCog },
    ],
  },
  {
    label: 'ARTICOLE',
    moduleId: 'administration',
    items: [
      { label: 'Articole', to: '/administration/articole', icon: Package },
      { label: 'Tipuri de articole', to: '/administration/tipuri-articole', icon: Tag },
      { label: 'Catalog articole', to: '/administration/catalog-articole', icon: BookOpen },
      { label: 'Liste de preț', to: '/administration/liste-pret', icon: DollarSign },
    ],
  },
  {
    label: 'STOCURI',
    items: [
      { label: 'Produse', icon: Package },
      { label: 'Depozite', icon: Warehouse },
      { label: 'Transferuri', icon: ArrowLeftRight },
    ],
  },
]
