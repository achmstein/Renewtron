import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

interface NavItem { to: string; label: string; end?: boolean }

const navItems: NavItem[] = [
  { to: '/admin', label: 'Overview', end: true },
  { to: '/admin/searches', label: 'Searches' },
  { to: '/admin/renewals', label: 'Renewals' },
  { to: '/admin/leads', label: 'Leads' },
  { to: '/admin/ontraport-sales', label: 'Ontraport' },
  { to: '/admin/bulk-renewals', label: 'Bulk' },
  { to: '/admin/ato-onboarding', label: 'ATO Onboard' },
  { to: '/admin/manual-renewal', label: 'Manual' },
  { to: '/admin/settings', label: 'Settings' },
]

const pad2 = (n: number) => String(n).padStart(2, '0')

export default function AdminLayout() {
  const [mobileOpen, setMobileOpen] = useState(false)
  const navigate = useNavigate()
  const { user, logout } = useAuth()

  // Live console clock — operator console feel
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000)
    return () => window.clearInterval(id)
  }, [])

  const stamp = `${now.toISOString().slice(0, 10)} · ${pad2(now.getHours())}:${pad2(now.getMinutes())}:${pad2(now.getSeconds())} AEDT`
  const operator = user?.email ? user.email.split('@')[0].toUpperCase() : 'ANONYMOUS'

  return (
    <div className="bureau flex min-h-screen flex-1 flex-col">
      {/* Top stamp strip — registry classification line */}
      <div
        className="border-b border-[var(--hairline-deep)] bg-[var(--paper-deep)]"
        style={{ position: 'relative', zIndex: 2 }}
      >
        <div className="mx-auto flex max-w-7xl items-center justify-between px-4 py-1.5 sm:px-6 lg:px-8 bureau-meta">
          <span>Registry of Business Names · Internal · Restricted</span>
          <span className="bureau-mono" style={{ color: 'var(--ink-soft)', letterSpacing: '0.06em' }}>{stamp}</span>
        </div>
      </div>

      {/* Masthead */}
      <header className="bureau-masthead">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="flex h-20 items-center justify-between gap-6">
            {/* Wordmark */}
            <Link to="/admin" className="flex items-baseline gap-3 group" aria-label="Registry home">
              <div>
                <div className="bureau-wordmark">
                  REGI<span className="bureau-wordmark-stamp">/</span>STRY
                </div>
                <div className="bureau-wordmark-sub">
                  Renewtron · Bureau of Business Names
                </div>
              </div>
            </Link>

            {/* Operator block */}
            <div className="hidden items-center gap-6 lg:flex">
              <div className="text-right">
                <div className="bureau-label" style={{ marginBottom: 1 }}>Operator</div>
                <div className="bureau-mono text-[12px] font-medium" style={{ color: 'var(--ink)', letterSpacing: '0.04em' }}>
                  {operator}
                </div>
              </div>
              <div className="h-9 w-px bg-[var(--hairline-deep)]" />
              <button
                type="button"
                onClick={async () => { await logout(); navigate('/login') }}
                className="bureau-btn"
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="h-3.5 w-3.5">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 9V5.25A2.25 2.25 0 0013.5 3h-6a2.25 2.25 0 00-2.25 2.25v13.5A2.25 2.25 0 007.5 21h6a2.25 2.25 0 002.25-2.25V15M12 9l-3 3m0 0l3 3m-3-3h12.75" />
                </svg>
                Sign out
              </button>
            </div>

            {/* Mobile toggle */}
            <button
              type="button"
              className="lg:hidden bureau-btn"
              onClick={() => setMobileOpen((v) => !v)}
              aria-expanded={mobileOpen}
            >
              <span className="sr-only">Toggle navigation</span>
              {mobileOpen ? '✕ Close' : '≡ Menu'}
            </button>
          </div>

          {/* Section index */}
          <nav className="hidden lg:block">
            <ul className="flex items-baseline gap-7 border-t border-[var(--hairline)] pt-3 pb-3">
              {navItems.map((n, i) => (
                <li key={n.to}>
                  <NavLink to={n.to} end={n.end} className="inline-block">
                    {({ isActive }) => (
                      <span className="bureau-nav-link" data-active={isActive}>
                        <span className="bureau-nav-num">{pad2(i + 1)}</span>
                        <span>{n.label}</span>
                      </span>
                    )}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
        </div>

        {/* Mobile menu */}
        {mobileOpen ? (
          <div className="lg:hidden border-t border-[var(--hairline-deep)] bg-[var(--card)]">
            <ul className="mx-auto max-w-7xl px-4 py-3 sm:px-6 space-y-1">
              {navItems.map((n, i) => (
                <li key={n.to}>
                  <NavLink
                    to={n.to}
                    end={n.end}
                    onClick={() => setMobileOpen(false)}
                    className="block py-2"
                  >
                    {({ isActive }) => (
                      <span className="bureau-nav-link" data-active={isActive}>
                        <span className="bureau-nav-num">{pad2(i + 1)}</span>
                        <span>{n.label}</span>
                      </span>
                    )}
                  </NavLink>
                </li>
              ))}
              <li className="pt-2 border-t border-[var(--hairline)]">
                <button
                  type="button"
                  onClick={async () => { await logout(); navigate('/login') }}
                  className="bureau-btn w-full justify-center"
                >
                  Sign out
                </button>
              </li>
            </ul>
          </div>
        ) : null}
      </header>

      <Outlet />
    </div>
  )
}
