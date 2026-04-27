import { lazy, Suspense } from 'react'
import { Route, Routes, useLocation } from 'react-router-dom'
import SiteHeader from './components/SiteHeader'
import SiteFooter from './components/SiteFooter'
import LoginPage from './pages/LoginPage'
import StartPage from './wizard/StartPage'
import DetailsPage from './wizard/DetailsPage'
import CheckingPage from './wizard/CheckingPage'
import SelectNamesPage from './wizard/SelectNamesPage'
import PaymentPage from './wizard/PaymentPage'
import ConfirmationPage from './wizard/ConfirmationPage'
import NotAvailablePage from './wizard/NotAvailablePage'
import RequireAuth from './auth/RequireAuth'

const AdminRoutes = lazy(() => import('./admin/AdminRoutes'))

function AdminFallback() {
  return (
    <div className="flex flex-1 min-h-screen flex-col items-center justify-center bg-gray-100 px-6">
      <img src="/logo.svg" alt="Renewtron" className="h-12 w-auto" />
      <div className="mt-6 flex items-center gap-3 text-gray-600">
        <svg className="h-6 w-6 animate-spin text-blue-600" fill="none" viewBox="0 0 24 24">
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
        </svg>
        <span className="text-sm font-medium">Loading…</span>
      </div>
    </div>
  )
}

export default function App() {
  const { pathname } = useLocation()
  // The original Blazor /Account/Login used EmptyLayout — no header, no footer.
  // Admin uses its own AdminLayout (gray-800 nav), so no public chrome there either.
  const chromeless = pathname === '/login' || pathname.startsWith('/admin')

  return (
    <div className="min-h-screen flex flex-col bg-[#F7F9FC]">
      {chromeless ? null : <SiteHeader />}

      <main className="flex-1 flex flex-col">
        <Routes>
          <Route path="/" element={<StartPage />} />
          <Route path="/start" element={<StartPage />} />
          <Route path="/details" element={<DetailsPage />} />
          <Route path="/checking/:leadId" element={<CheckingPage />} />
          <Route path="/select-names/:leadId" element={<SelectNamesPage />} />
          <Route path="/payment/:leadId" element={<PaymentPage />} />
          <Route path="/confirmation/:leadId" element={<ConfirmationPage />} />
          <Route path="/not-available/:leadId" element={<NotAvailablePage />} />

          <Route path="/login" element={<LoginPage />} />

          <Route
            path="/admin/*"
            element={
              <RequireAuth>
                <Suspense fallback={<AdminFallback />}>
                  <AdminRoutes />
                </Suspense>
              </RequireAuth>
            }
          />
        </Routes>
      </main>

      {chromeless ? null : <SiteFooter />}
    </div>
  )
}
