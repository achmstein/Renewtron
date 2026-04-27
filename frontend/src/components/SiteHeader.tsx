import { useState } from 'react'
import { Link, NavLink } from 'react-router-dom'

export default function SiteHeader() {
  const [mobileOpen, setMobileOpen] = useState(false)

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
            <NavLink
              to="/"
              end
              className={({ isActive }) =>
                `${isActive ? 'nav-link-active' : 'nav-link-inactive'} rounded-md px-4 py-2.5 text-sm font-medium transition-all`
              }
            >
              Renew Business Name
            </NavLink>
            <a href="https://applyforanabn.com.au/abnapplication/" className="nav-link-inactive rounded-md px-4 py-2.5 text-sm font-medium transition-all">
              Register an ABN
            </a>
            <a href="https://applyforanabn.com.au/register-a-new-company/" className="nav-link-inactive rounded-md px-4 py-2.5 text-sm font-medium transition-all">
              Register a Company
            </a>
            <a href="https://applyforanabn.com.au/contact-us/" className="nav-link-inactive rounded-md px-4 py-2.5 text-sm font-medium transition-all">
              Contact Us
            </a>
          </nav>

          <a href="tel:1300123456" className="hidden md:flex items-center gap-2 phone-link font-semibold text-[15px] pl-4 ml-4 border-l border-gray-300 whitespace-nowrap shrink-0">
            <svg className="w-[18px] h-[18px] shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6 19.79 19.79 0 01-3.07-8.67A2 2 0 014.11 2h3a2 2 0 012 1.72 12.84 12.84 0 00.7 2.81 2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45 12.84 12.84 0 002.81.7A2 2 0 0122 16.92z" />
            </svg>
            1300 123 456
          </a>

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
              <NavLink to="/" end className={({ isActive }) => `${isActive ? 'nav-link-active' : 'nav-link-inactive'} block rounded-md px-3 py-2 text-base font-medium`}>
                Renew Business Name
              </NavLink>
              <a href="https://applyforanabn.com.au/abnapplication/" className="nav-link-inactive block rounded-md px-3 py-2 text-base font-medium">
                Register an ABN
              </a>
              <a href="https://applyforanabn.com.au/register-a-new-company/" className="nav-link-inactive block rounded-md px-3 py-2 text-base font-medium">
                Register a Company
              </a>
              <a href="https://applyforanabn.com.au/contact-us/" className="nav-link-inactive block rounded-md px-3 py-2 text-base font-medium">
                Contact Us
              </a>
            </div>
            <div className="border-t border-gray-200 px-5 py-4">
              <a href="tel:1300123456" className="flex items-center gap-2 phone-link font-semibold">
                <svg className="w-5 h-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6 19.79 19.79 0 01-3.07-8.67A2 2 0 014.11 2h3a2 2 0 012 1.72 12.84 12.84 0 00.7 2.81 2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45 12.84 12.84 0 002.81.7A2 2 0 0122 16.92z" />
                </svg>
                1300 123 456
              </a>
            </div>
          </div>
        ) : null}
      </header>
    </>
  )
}
