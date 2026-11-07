import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Route, Routes } from 'react-router-dom'
import { Toaster } from 'sileo'
import 'sileo/styles.css'
import AdminLayout from './AdminLayout'
import Dashboard from './Dashboard'
import Today from './Today'
import RenewalOrders from './RenewalOrders'
import RenewalOrderDetails from './RenewalOrderDetails'
import Searches from './Searches'
import SearchDetails from './SearchDetails'
import Clients from './Clients'
import ClientDetails from './ClientDetails'
import ClientDuplicates from './ClientDuplicates'
import Leads from './Leads'
import LeadDetails from './LeadDetails'
import Funnel from './Funnel'
import Renewals from './Renewals'
import RenewalDetails from './RenewalDetails'
import Payments from './Payments'
import BpayPayments from './BpayPayments'
import ManualRenewal from './ManualRenewal'
import OntraportSales from './OntraportSales'
import SaleDetails from './SaleDetails'
import BulkRenewals from './BulkRenewals'
import AsicKeys from './AsicKeys'
import AsicKeyRequests from './AsicKeyRequests'
import { KeyLetterDetails, KeyRequestDetails } from './KeyDetails'
import AsicPortalLinkDetails from './AsicPortalLinkDetails'
import Emails from './Emails'
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
          <Route index element={<Today />} />
          <Route path="overview" element={<Dashboard />} />
          <Route path="searches" element={<Searches />} />
          <Route path="searches/:id" element={<SearchDetails />} />
          <Route path="clients" element={<Clients />} />
          <Route path="clients/duplicates" element={<ClientDuplicates />} />
          <Route path="clients/:id" element={<ClientDetails />} />
          <Route path="leads" element={<Leads />} />
          <Route path="leads/:id" element={<LeadDetails />} />
          <Route path="funnel" element={<Funnel />} />
          <Route path="renewal-orders" element={<RenewalOrders />} />
          <Route path="renewal-orders/:id" element={<RenewalOrderDetails />} />
          <Route path="renewals" element={<Renewals />} />
          <Route path="renewals/:id" element={<RenewalDetails />} />
          <Route path="payments" element={<Payments />} />
          <Route path="bpay" element={<BpayPayments />} />
          <Route path="manual-renewal" element={<ManualRenewal />} />
          <Route path="ontraport-sales" element={<OntraportSales />} />
          <Route path="ontraport-sales/:id" element={<SaleDetails />} />
          <Route path="bulk-renewals" element={<BulkRenewals />} />
          <Route path="asic-keys" element={<AsicKeys />} />
          <Route path="asic-key-requests" element={<AsicKeyRequests />} />
          <Route path="asic-key-requests/:id" element={<KeyRequestDetails />} />
          <Route path="asic-keys/:id" element={<KeyLetterDetails />} />
          <Route path="asic-portal/:id" element={<AsicPortalLinkDetails />} />
          <Route path="emails" element={<Emails />} />
          <Route path="emails/:id" element={<Emails />} />
          <Route path="settings" element={<Settings />} />
        </Route>
      </Routes>
      <Toaster position="bottom-right" theme="light" />
    </QueryClientProvider>
  )
}
