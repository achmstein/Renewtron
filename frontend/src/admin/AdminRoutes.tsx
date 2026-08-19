import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Route, Routes } from 'react-router-dom'
import { Toaster } from 'sileo'
import 'sileo/styles.css'
import AdminLayout from './AdminLayout'
import Dashboard from './Dashboard'
import Searches from './Searches'
import SearchDetails from './SearchDetails'
import Leads from './Leads'
import LeadDetails from './LeadDetails'
import Funnel from './Funnel'
import Renewals from './Renewals'
import RenewalDetails from './RenewalDetails'
import Payments from './Payments'
import ManualRenewal from './ManualRenewal'
import OntraportSales from './OntraportSales'
import BulkRenewals from './BulkRenewals'
import Settings from './Settings'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 15_000, retry: 1, refetchOnWindowFocus: false },
  },
})

export default function AdminRoutes() {
  return (
    <QueryClientProvider client={queryClient}>
      <Routes>
        <Route element={<AdminLayout />}>
          <Route index element={<Dashboard />} />
          <Route path="searches" element={<Searches />} />
          <Route path="searches/:id" element={<SearchDetails />} />
          <Route path="leads" element={<Leads />} />
          <Route path="leads/:id" element={<LeadDetails />} />
          <Route path="funnel" element={<Funnel />} />
          <Route path="renewals" element={<Renewals />} />
          <Route path="renewals/:id" element={<RenewalDetails />} />
          <Route path="payments" element={<Payments />} />
          <Route path="manual-renewal" element={<ManualRenewal />} />
          <Route path="ontraport-sales" element={<OntraportSales />} />
          <Route path="bulk-renewals" element={<BulkRenewals />} />
          <Route path="settings" element={<Settings />} />
        </Route>
      </Routes>
      <Toaster position="bottom-right" theme="light" />
    </QueryClientProvider>
  )
}
