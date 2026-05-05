import { useState } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

interface NavItem { to: string; label: string; end?: boolean }

const navItems: NavItem[] = [
  { to: '/admin', label: 'Dashboard', end: true },
  { to: '/admin/searches', label: 'Searches' },
  { to: '/admin/renewals', label: 'Renewals' },
  { to: '/admin/leads', label: 'Leads' },
  { to: '/admin/ontraport-sales', label: 'Ontraport' },
  { to: '/admin/bulk-renewals', label: 'Bulk Renewals' },
  { to: '/admin/ato-onboarding', label: 'ATO Onboarding' },
  { to: '/admin/manual-renewal', label: 'Manual Renewal' },
  { to: '/admin/settings', label: 'Settings' },
]

const linkClass = (active: boolean) =>
  active
    ? 'rounded-md bg-gray-900 px-3 py-2 text-sm font-medium text-white'
    : 'rounded-md px-3 py-2 text-sm font-medium text-gray-300 hover:bg-white/5 hover:text-white'

export default function AdminLayout() {
  const [mobileOpen, setMobileOpen] = useState(false)
  const navigate = useNavigate()
  const { logout } = useAuth()

  return (
    <div className="min-h-screen bg-gray-100 flex flex-col flex-1">
      <nav className="bg-gray-800">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="flex h-16 items-center justify-between">
            <div className="flex items-center">
              <div className="shrink-0">
                <Link to="/admin" className="flex items-center">
                  <img src="/logo.svg" alt="Renewtron" className="size-8 w-auto" />
                  <span className="ml-3 text-white text-xl font-bold">Renewtron</span>
                </Link>
              </div>
              <div className="hidden md:block">
                <div className="ml-10 flex items-baseline space-x-4">
                  {navItems.map((n) => (
                    <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => linkClass(isActive)}>
                      {n.label}
                    </NavLink>
                  ))}
                </div>
              </div>
            </div>
            <div className="hidden md:block">
              <button
                type="button"
                onClick={async () => { await logout(); navigate('/login') }}
                className="rounded-md px-3 py-2 text-sm font-medium text-gray-300 hover:bg-white/5 hover:text-white"
              >
                Sign out
              </button>
            </div>
            <div className="-mr-2 flex md:hidden">
              <button
                type="button"
                onClick={() => setMobileOpen((v) => !v)}
                className="inline-flex items-center justify-center rounded-md bg-gray-800 p-2 text-gray-400 hover:bg-gray-700 hover:text-white"
                aria-expanded={mobileOpen}
              >
                <span className="sr-only">Open main menu</span>
                {mobileOpen ? (
                  <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                ) : (
                  <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5" />
                  </svg>
                )}
              </button>
            </div>
          </div>
        </div>
        {mobileOpen ? (
          <div className="md:hidden">
            <div className="space-y-1 px-2 pt-2 pb-3 sm:px-3">
              {navItems.map((n) => (
                <NavLink
                  key={n.to}
                  to={n.to}
                  end={n.end}
                  onClick={() => setMobileOpen(false)}
                  className={({ isActive }) =>
                    isActive
                      ? 'block rounded-md bg-gray-900 px-3 py-2 text-base font-medium text-white'
                      : 'block rounded-md px-3 py-2 text-base font-medium text-gray-300 hover:bg-white/5 hover:text-white'
                  }
                >
                  {n.label}
                </NavLink>
              ))}
              <button
                type="button"
                onClick={async () => { await logout(); navigate('/login') }}
                className="block w-full text-left rounded-md px-3 py-2 text-base font-medium text-gray-300 hover:bg-white/5 hover:text-white"
              >
                Sign out
              </button>
            </div>
          </div>
        ) : null}
      </nav>

      <Outlet />
    </div>
  )
}
