import { useState, type ReactNode } from 'react'
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import {
  Archive, Contact, Copy, CreditCard, FileWarning, Filter, House, Inbox, KeyRound, LogOut, Menu as MenuIcon,
  Landmark, RefreshCw, Search, Settings, ShoppingCart, SquarePen, Users, Wrench, X,
} from 'lucide-react'
import { useAuth } from '../auth/AuthContext'
import { api, type SidebarCounts } from '../api/client'
import { ClientSearch, ClientSearchButton } from './ClientSearch'

/**
 * One sidebar entry. `to` may carry a query (`?view=to-fix`) for a queue that is a view of a list
 * page; the badge names a count from /api/admin/sidebar-counts (work waiting on staff).
 */
interface NavItem { to: string; label: string; icon: ReactNode; badge?: keyof SidebarCounts }
interface NavSection { kicker: string; items: NavItem[] }

const Icon = {
  Dashboard: <House strokeWidth={1.5} className="size-4 shrink-0" />,
  Search: <Search strokeWidth={1.5} className="size-4 shrink-0" />,
  Refresh: <RefreshCw strokeWidth={1.5} className="size-4 shrink-0" />,
  Wrench: <Wrench strokeWidth={1.5} className="size-4 shrink-0" />,
  Users: <Users strokeWidth={1.5} className="size-4 shrink-0" />,
  Contact: <Contact strokeWidth={1.5} className="size-4 shrink-0" />,
  Copy: <Copy strokeWidth={1.5} className="size-4 shrink-0" />,
  Cart: <ShoppingCart strokeWidth={1.5} className="size-4 shrink-0" />,
  Card: <CreditCard strokeWidth={1.5} className="size-4 shrink-0" />,
  Bank: <Landmark strokeWidth={1.5} className="size-4 shrink-0" />,
  Bulk: <Archive strokeWidth={1.5} className="size-4 shrink-0" />,
  Pencil: <SquarePen strokeWidth={1.5} className="size-4 shrink-0" />,
  Key: <KeyRound strokeWidth={1.5} className="size-4 shrink-0" />,
  KeyAlert: <FileWarning strokeWidth={1.5} className="size-4 shrink-0" />,
  Inbox: <Inbox strokeWidth={1.5} className="size-4 shrink-0" />,
  Funnel: <Filter strokeWidth={1.5} className="size-4 shrink-0" />,
  Cog: <Settings strokeWidth={1.5} className="size-4 shrink-0" />,
  Logout: <LogOut strokeWidth={1.5} className="size-4 shrink-0" />,
  Menu: <MenuIcon strokeWidth={1.5} className="size-5" />,
  Close: <X strokeWidth={1.5} className="size-6 text-white" />,
}

// Grouped by job: the work waiting on a person first, then who it is for, money, numbers,
// and last the raw records for digging.
const navSections: NavSection[] = [
  {
    kicker: 'Work',
    items: [
      { to: '/admin/renewals?view=to-fix', label: 'Renewals to fix', icon: Icon.Wrench, badge: 'renewalsToFix' },
      { to: '/admin/bpay', label: 'BPAY to pay', icon: Icon.Bank, badge: 'bpayToPay' },
      { to: '/admin/asic-key-requests', label: 'Key requests', icon: Icon.Inbox, badge: 'asicKeyRequests' },
      { to: '/admin/asic-keys?view=to-sort', label: 'Keys to sort', icon: Icon.KeyAlert, badge: 'keysToSort' },
      { to: '/admin/manual-renewal', label: 'Manual renewal', icon: Icon.Pencil },
    ],
  },
  {
    kicker: 'Clients',
    items: [
      { to: '/admin/clients', label: 'All clients', icon: Icon.Contact },
      { to: '/admin/clients/duplicates', label: 'Duplicates', icon: Icon.Copy, badge: 'clientDuplicates' },
    ],
  },
  {
    kicker: 'Money',
    items: [
      { to: '/admin/payments', label: 'Payments', icon: Icon.Card },
    ],
  },
  {
    kicker: 'Reports',
    items: [
      { to: '/admin', label: 'Overview', icon: Icon.Dashboard },
      { to: '/admin/funnel', label: 'Funnel', icon: Icon.Funnel },
    ],
  },
  {
    kicker: 'Records',
    items: [
      { to: '/admin/leads', label: 'Leads', icon: Icon.Users },
      { to: '/admin/searches', label: 'Searches', icon: Icon.Search },
      { to: '/admin/renewals', label: 'Renewals', icon: Icon.Refresh },
      { to: '/admin/ontraport-sales', label: 'Ontraport sales', icon: Icon.Cart },
      { to: '/admin/bulk-renewals', label: 'Bulk uploads', icon: Icon.Bulk },
      { to: '/admin/asic-keys', label: 'Key letters', icon: Icon.Key },
    ],
  },
  {
    kicker: 'System',
    items: [
      { to: '/admin/settings', label: 'Settings', icon: Icon.Cog },
    ],
  },
]

const allItems = navSections.flatMap((s) => s.items)

/**
 * The one entry to highlight: the longest path containing the current one (a client's page
 * lights "All clients", the duplicates page lights "Duplicates"), and among those the entry
 * whose `view` matches ("Renewals to fix" vs "Renewals").
 */
function activeItem(pathname: string, search: string): NavItem | undefined {
  const view = new URLSearchParams(search).get('view')
  let best: NavItem | undefined
  let bestLen = -1
  for (const item of allItems) {
    const [path, query] = item.to.split('?')
    const within = path === '/admin'
      ? pathname === '/admin' || pathname === '/admin/'
      : pathname === path || pathname.startsWith(path + '/')
    if (!within) continue
    if (new URLSearchParams(query ?? '').get('view') !== view) continue
    if (path.length > bestLen) { best = item; bestLen = path.length }
  }
  return best
}

function Wordmark({ size = 'lg' }: { size?: 'lg' | 'sm' }) {
  const text = size === 'lg' ? 'text-[15px]' : 'text-[14px]'
  return (
    <span className={`font-display font-bold ${text} tracking-[0.04em] text-white leading-none flex items-baseline`}>
      <span>RENEW</span>
      <span className="mx-1.5 text-zinc-600 font-normal">/</span>
      <span className="text-brand-400">TRON</span>
    </span>
  )
}

function OpsBadge() {
  return (
    <span className="text-xxs font-mono font-medium px-1.5 py-0.5 rounded bg-white/5 text-zinc-400 tracking-[0.14em]">OPS</span>
  )
}

function SidebarBody({ onNavigate, onSearch }: { onNavigate?: () => void; onSearch: () => void }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const active = activeItem(location.pathname, location.search)
  const { data: counts } = useQuery({
    queryKey: ['admin-sidebar-counts'],
    queryFn: api.admin.sidebarCounts,
    refetchInterval: 60_000,
  })

  const onSignOut = async () => {
    await logout()
    onNavigate?.()
    navigate('/login')
  }

  return (
    <div className="scrollbar-dark flex grow flex-col gap-y-7 overflow-y-auto bg-zinc-950 px-5 pb-5 ring-1 ring-white/5">
      <div className="flex h-16 shrink-0 items-center justify-between">
        <Link to="/admin" onClick={onNavigate} className="flex items-center gap-2.5 group">
          <Wordmark />
          <OpsBadge />
        </Link>
      </div>

      <div className="-mt-3">
        <ClientSearchButton onClick={() => { onNavigate?.(); onSearch() }} />
      </div>

      <nav className="flex flex-1 flex-col">
        <ul role="list" className="flex flex-1 flex-col gap-y-6">
          {navSections.map((section) => (
            <li key={section.kicker}>
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500 mb-2 px-2">{section.kicker}</div>
              <ul role="list" className="-mx-1 space-y-0.5">
                {section.items.map((item) => (
                  <li key={item.to}>
                    <Link
                      to={item.to}
                      onClick={onNavigate}
                      aria-current={item === active ? 'page' : undefined}
                      className={`nav-link group relative flex items-center gap-x-3 rounded-md py-1.5 pl-3 pr-2 text-sm font-medium transition ${
                        item === active
                          ? 'active text-white bg-white/[0.07]'
                          : 'text-zinc-400 hover:text-white hover:bg-white/5'
                      }`}
                    >
                      <span className="nav-edge absolute inset-y-1.5 left-0 w-0.5 rounded-r bg-brand-500 opacity-0 transition-opacity"></span>
                      <span className="text-zinc-500 group-hover:text-zinc-300">{item.icon}</span>
                      <span className="truncate">{item.label}</span>
                      {item.badge && counts?.[item.badge] ? (
                        <span className="ml-auto rounded-full bg-brand-500/15 px-1.5 py-px text-xxs font-mono font-medium tabular-nums text-brand-300">
                          {counts[item.badge]}
                        </span>
                      ) : null}
                    </Link>
                  </li>
                ))}
              </ul>
            </li>
          ))}

          <li className="-mx-5 mt-auto">
            <div className="border-t border-white/5 px-5 py-4 bg-black/20">
              {user ? (
                <div className="mb-3">
                  <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">Signed in</div>
                  <div className="mt-1 truncate text-sm font-medium text-zinc-200" title={user.email}>{user.email}</div>
                </div>
              ) : null}
              <button
                type="button"
                onClick={onSignOut}
                className="flex w-full items-center gap-3 rounded-md px-2 py-1.5 text-sm font-medium text-zinc-400 hover:bg-white/5 hover:text-white transition"
              >
                <span className="text-zinc-500">{Icon.Logout}</span>
                <span>Sign out</span>
              </button>
            </div>
          </li>
        </ul>
      </nav>
    </div>
  )
}

export default function AdminLayout() {
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)
  const location = useLocation()

  return (
    <div className="min-h-screen bg-zinc-50">
      <ClientSearch open={searchOpen} setOpen={setSearchOpen} />
      {sidebarOpen ? (
        <div className="fixed inset-0 z-50 lg:hidden" role="dialog" aria-modal="true">
          <div className="fixed inset-0 bg-zinc-950/70 backdrop-blur-sm" aria-hidden="true" onClick={() => setSidebarOpen(false)}></div>
          <div className="relative flex h-full w-full max-w-xs">
            <div className="relative mr-16 flex w-full max-w-xs flex-1">
              <div className="absolute top-0 left-full flex w-16 justify-center pt-5">
                <button type="button" onClick={() => setSidebarOpen(false)} className="-m-2.5 p-2.5">
                  <span className="sr-only">Close sidebar</span>
                  {Icon.Close}
                </button>
              </div>
              <SidebarBody onNavigate={() => setSidebarOpen(false)} onSearch={() => setSearchOpen(true)} />
            </div>
          </div>
        </div>
      ) : null}

      <div className="hidden lg:fixed lg:inset-y-0 lg:z-50 lg:flex lg:w-64 lg:flex-col">
        <SidebarBody onSearch={() => setSearchOpen(true)} />
      </div>

      <div className="sticky top-0 z-40 flex items-center gap-x-4 bg-zinc-950 px-4 py-3 ring-1 ring-white/5 sm:px-6 lg:hidden">
        <button type="button" onClick={() => setSidebarOpen(true)} className="-m-2.5 p-2.5 text-zinc-400 hover:text-white">
          <span className="sr-only">Open sidebar</span>
          {Icon.Menu}
        </button>
        <div className="flex-1 flex items-center gap-2.5">
          <Wordmark size="sm" />
          <OpsBadge />
        </div>
      </div>

      <main className="lg:pl-64">
        <div key={location.pathname}>
          <div className="fade-in">
            <Outlet />
          </div>
        </div>
      </main>
    </div>
  )
}
