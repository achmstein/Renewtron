import { useState } from 'react'
import { Link, NavLink } from 'react-router-dom'
import { siteLinks } from './siteLinks'

// Our own pages, then the main site's (cancel, news, contact) so the menu matches applyforanabn.au.
const navItems = [
  { label: 'Renew Business Name', to: '/', end: true },
  { label: 'Register Business Name', to: '/register' },
  { label: 'Cancel', href: siteLinks.cancel },
  { label: 'News & Guides', href: siteLinks.news },
  { label: 'Contact Us', href: siteLinks.contact },
]

export default function SiteHeader() {
  const [mobileOpen, setMobileOpen] = useState(false)

  const renderItems = (base: string, onClick?: () => void) =>
    navItems.map((item) =>
      item.to ? (
        <NavLink
          key={item.label}
          to={item.to}
          end={item.end}
          onClick={onClick}
          className={({ isActive }) => `${isActive ? 'nav-link-active' : 'nav-link-inactive'} ${base}`}
        >
          {item.label}
        </NavLink>
      ) : (
        <a key={item.label} href={item.href} className={`nav-link-inactive ${base}`}>
          {item.label}
        </a>
      ),
    )

  return (
    <>
      <div className="gov-banner">
        <div className="max-w-[1200px] mx-auto px-6 min-h-[10px]"></div>
      </div>

      <header className="site-header">
        <div className="max-w-[1200px] mx-auto px-6 flex justify-between items-center min-h-[80px]">
          <Link to="/" className="flex items-center gap-3 mr-8 shrink-0">
            <span className="text-lg font-bold brand-text tracking-tight whitespace-nowrap">
              Business Name Services
            </span>
          </Link>

          <nav className="hidden md:flex items-center gap-2 pl-6">
            {renderItems('rounded-md px-4 py-2.5 text-sm font-medium transition-all')}
          </nav>

          <div className="md:hidden">
            <button
              type="button"
              onClick={() => setMobileOpen((v) => !v)}
              className="inline-flex items-center justify-center rounded-md p-2 text-gray-600 hover:bg-gray-100 focus:outline-none"
              aria-expanded={mobileOpen}
            >
              <span className="sr-only">Open main menu</span>
              {!mobileOpen ? (
                <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5" />
                </svg>
              ) : (
                <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              )}
            </button>
          </div>
        </div>

        {mobileOpen ? (
          <div className="md:hidden border-t border-gray-200">
            <div className="space-y-1 px-4 pb-3 pt-2">
              {renderItems('block rounded-md px-3 py-2 text-base font-medium', () => setMobileOpen(false))}
            </div>
          </div>
        ) : null}
      </header>
    </>
  )
}
