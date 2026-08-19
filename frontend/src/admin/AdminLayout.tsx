import { useState, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import {
  Archive, Briefcase, CreditCard, Filter, House, LogOut, Menu as MenuIcon,
  RefreshCw, Search, Settings, ShoppingCart, SquarePen, Users, X,
} from 'lucide-react'
import { useAuth } from '../auth/AuthContext'

interface NavItem { to: string; label: string; end?: boolean; icon: ReactNode }
interface NavSection { kicker: string; items: NavItem[] }

const Icon = {
  Dashboard: <House strokeWidth={1.5} className="size-4 shrink-0" />,
  Search: <Search strokeWidth={1.5} className="size-4 shrink-0" />,
  Refresh: <RefreshCw strokeWidth={1.5} className="size-4 shrink-0" />,
  Users: <Users strokeWidth={1.5} className="size-4 shrink-0" />,
  Cart: <ShoppingCart strokeWidth={1.5} className="size-4 shrink-0" />,
  Card: <CreditCard strokeWidth={1.5} className="size-4 shrink-0" />,
  Bulk: <Archive strokeWidth={1.5} className="size-4 shrink-0" />,
  Briefcase: <Briefcase strokeWidth={1.5} className="size-4 shrink-0" />,
  Pencil: <SquarePen strokeWidth={1.5} className="size-4 shrink-0" />,
  Funnel: <Filter strokeWidth={1.5} className="size-4 shrink-0" />,
  Cog: <Settings strokeWidth={1.5} className="size-4 shrink-0" />,
  Logout: <LogOut strokeWidth={1.5} className="size-4 shrink-0" />,
  Menu: <MenuIcon strokeWidth={1.5} className="size-5" />,
  Close: <X strokeWidth={1.5} className="size-6 text-white" />,
}

const navSections: NavSection[] = [
  {
    kicker: 'Operations',
    items: [
      { to: '/admin', label: 'Dashboard', end: true, icon: Icon.Dashboard },
      { to: '/admin/searches', label: 'Searches', icon: Icon.Search },
      { to: '/admin/leads', label: 'Leads', icon: Icon.Users },
      { to: '/admin/funnel', label: 'Funnel', icon: Icon.Funnel },
    ],
  },
  {
    kicker: 'Pipeline',
    items: [
      { to: '/admin/renewals', label: 'Renewals', icon: Icon.Refresh },
      { to: '/admin/payments', label: 'Payments', icon: Icon.Card },
      { to: '/admin/ontraport-sales', label: 'Ontraport', icon: Icon.Cart },
      { to: '/admin/bulk-renewals', label: 'Bulk Renewals', icon: Icon.Bulk },
      { to: '/admin/manual-renewal', label: 'Manual Renewal', icon: Icon.Pencil },
    ],
  },
  {
    kicker: 'System',
    items: [
      { to: '/admin/settings', label: 'Settings', icon: Icon.Cog },
    ],
  },
]

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

function SidebarBody({ onNavigate }: { onNavigate?: () => void }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

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

      <nav className="flex flex-1 flex-col">
        <ul role="list" className="flex flex-1 flex-col gap-y-7">
          {navSections.map((section) => (
            <li key={section.kicker}>
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500 mb-2 px-2">{section.kicker}</div>
              <ul role="list" className="-mx-1 space-y-0.5">
                {section.items.map((item) => (
                  <li key={item.to}>
                    <NavLink
                      to={item.to}
                      end={item.end}
                      onClick={onNavigate}
                      className={({ isActive }) =>
                        `nav-link group relative flex items-center gap-x-3 rounded-md py-1.5 pl-3 pr-2 text-sm font-medium transition ${
                          isActive
                            ? 'active text-white bg-white/[0.07]'
                            : 'text-zinc-400 hover:text-white hover:bg-white/5'
                        }`
                      }
                    >
                      <span className="nav-edge absolute inset-y-1.5 left-0 w-0.5 rounded-r bg-brand-500 opacity-0 transition-opacity"></span>
                      <span className="text-zinc-500 group-hover:text-zinc-300">{item.icon}</span>
                      <span className="truncate">{item.label}</span>
                    </NavLink>
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
  const location = useLocation()

  return (
    <div className="min-h-screen bg-zinc-50">
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
              <SidebarBody onNavigate={() => setSidebarOpen(false)} />
            </div>
          </div>
        </div>
      ) : null}

      <div className="hidden lg:fixed lg:inset-y-0 lg:z-50 lg:flex lg:w-64 lg:flex-col">
        <SidebarBody />
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
