import { useState, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

interface NavItem { to: string; label: string; end?: boolean; icon: ReactNode }
interface NavSection { kicker: string; items: NavItem[] }

const Icon = {
  Dashboard: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 12 12 2.25 21.75 12M4.5 9.75v10.125A1.125 1.125 0 0 0 5.625 21H9.75v-6h4.5v6h4.125A1.125 1.125 0 0 0 19.5 19.875V9.75" />
    </svg>
  ),
  Search: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
    </svg>
  ),
  Refresh: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0 3.181 3.183a8.25 8.25 0 0 0 13.803-3.7M4.031 9.865a8.25 8.25 0 0 1 13.803-3.7l3.181 3.182m0-4.991v4.99" />
    </svg>
  ),
  Users: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z" />
    </svg>
  ),
  Cart: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 3h1.386c.51 0 .955.343 1.087.835l.383 1.437M7.5 14.25a3 3 0 0 0-3 3h15.75m-12.75-3h11.218c1.121-2.3 2.1-4.684 2.924-7.138a60.114 60.114 0 0 0-16.536-1.84M7.5 14.25 5.106 5.272M6 20.25a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Zm12.75 0a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Z" />
    </svg>
  ),
  Bulk: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 9.776c.112-.017.227-.026.344-.026h15.812c.117 0 .232.009.344.026m-16.5 0a2.25 2.25 0 0 0-1.883 2.542l.857 6a2.25 2.25 0 0 0 2.227 1.932H19.05a2.25 2.25 0 0 0 2.227-1.932l.857-6a2.25 2.25 0 0 0-1.883-2.542m-16.5 0V6A2.25 2.25 0 0 1 6 3.75h3.879a1.5 1.5 0 0 1 1.06.44l2.122 2.12a1.5 1.5 0 0 0 1.06.44H18A2.25 2.25 0 0 1 20.25 9v.776" />
    </svg>
  ),
  Briefcase: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="M20.25 14.15v4.25c0 1.094-.787 2.036-1.872 2.18-2.087.277-4.216.42-6.378.42s-4.291-.143-6.378-.42c-1.085-.144-1.872-1.086-1.872-2.18v-4.25m16.5 0a2.18 2.18 0 0 0 .75-1.661V8.706c0-1.081-.768-2.015-1.837-2.175a48.114 48.114 0 0 0-3.413-.387m4.5 8.006c-.194.165-.42.295-.673.38A23.978 23.978 0 0 1 12 15.75c-2.648 0-5.195-.429-7.577-1.22a2.16 2.16 0 0 1-.673-.38m0 0A2.18 2.18 0 0 1 3 12.489V8.706c0-1.081.768-2.015 1.837-2.175a48.111 48.111 0 0 1 3.413-.387m7.5 0V5.25A2.25 2.25 0 0 0 13.5 3h-3a2.25 2.25 0 0 0-2.25 2.25v.894m7.5 0a48.667 48.667 0 0 0-7.5 0M12 12.75h.008v.008H12v-.008Z" />
    </svg>
  ),
  Pencil: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0 1 15.75 21H5.25A2.25 2.25 0 0 1 3 18.75V8.25A2.25 2.25 0 0 1 5.25 6H10" />
    </svg>
  ),
  Cog: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.324.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 0 1 1.37.49l1.296 2.247a1.125 1.125 0 0 1-.26 1.431l-1.003.827c-.293.24-.438.613-.431.992.007.378-.138.75-.43.99l1.005.828c.424.35.534.954.26 1.43l-1.298 2.247a1.125 1.125 0 0 1-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.57 6.57 0 0 1-.22.128c-.331.183-.581.495-.644.869l-.213 1.28c-.09.543-.56.941-1.11.941h-2.594c-.55 0-1.02-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 0 1-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 0 0 1-1.369-.49l-1.297-2.247a1.125 1.125 0 0 1 .26-1.431l1.004-.827c.292-.24.437-.613.43-.992-.007-.378.138-.75.43-.99l-1.004-.828a1.125 1.125 0 0 1-.26-1.43l1.297-2.247a1.125 1.125 0 0 1 1.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.087.22-.128.332-.183.582-.495.644-.869l.214-1.281Z" />
      <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
    </svg>
  ),
  Logout: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-4 shrink-0">
      <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 9V5.25A2.25 2.25 0 0 0 13.5 3h-6a2.25 2.25 0 0 0-2.25 2.25v13.5A2.25 2.25 0 0 0 7.5 21h6a2.25 2.25 0 0 0 2.25-2.25V15M12 9l-3 3m0 0 3 3m-3-3h12.75" />
    </svg>
  ),
  Menu: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-5">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5" />
    </svg>
  ),
  Close: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="size-6 text-white">
      <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
    </svg>
  ),
}

const navSections: NavSection[] = [
  {
    kicker: 'Operations',
    items: [
      { to: '/admin', label: 'Dashboard', end: true, icon: Icon.Dashboard },
      { to: '/admin/searches', label: 'Searches', icon: Icon.Search },
      { to: '/admin/leads', label: 'Leads', icon: Icon.Users },
    ],
  },
  {
    kicker: 'Pipeline',
    items: [
      { to: '/admin/renewals', label: 'Renewals', icon: Icon.Refresh },
      { to: '/admin/ontraport-sales', label: 'Ontraport', icon: Icon.Cart },
      { to: '/admin/bulk-renewals', label: 'Bulk Renewals', icon: Icon.Bulk },
      { to: '/admin/manual-renewal', label: 'Manual Renewal', icon: Icon.Pencil },
    ],
  },
  {
    kicker: 'Compliance',
    items: [
      { to: '/admin/ato-onboarding', label: 'ATO Onboarding', icon: Icon.Briefcase },
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
