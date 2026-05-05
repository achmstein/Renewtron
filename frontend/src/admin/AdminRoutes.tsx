import { Route, Routes } from 'react-router-dom'
import AdminLayout from './AdminLayout'
import Dashboard from './Dashboard'
import Searches from './Searches'
import SearchDetails from './SearchDetails'
import Leads from './Leads'
import LeadDetails from './LeadDetails'
import Renewals from './Renewals'
import RenewalDetails from './RenewalDetails'
import ManualRenewal from './ManualRenewal'
import OntraportSales from './OntraportSales'
import BulkRenewals from './BulkRenewals'
import AtoOnboarding from './AtoOnboarding'
import AtoOnboardingDetails from './AtoOnboardingDetails'
import Settings from './Settings'

export default function AdminRoutes() {
  return (
    <Routes>
      <Route element={<AdminLayout />}>
        <Route index element={<Dashboard />} />
        <Route path="searches" element={<Searches />} />
        <Route path="searches/:id" element={<SearchDetails />} />
        <Route path="leads" element={<Leads />} />
        <Route path="leads/:id" element={<LeadDetails />} />
        <Route path="renewals" element={<Renewals />} />
        <Route path="renewals/:id" element={<RenewalDetails />} />
        <Route path="manual-renewal" element={<ManualRenewal />} />
        <Route path="ontraport-sales" element={<OntraportSales />} />
        <Route path="bulk-renewals" element={<BulkRenewals />} />
        <Route path="ato-onboarding" element={<AtoOnboarding />} />
        <Route path="ato-onboarding/:id" element={<AtoOnboardingDetails />} />
        <Route path="settings" element={<Settings />} />
      </Route>
    </Routes>
  )
}
