// The API in one place: every feature module's types, plus `api`, which joins their calls
// under the shape the app has always used (`api.pricing()`, `api.admin.renewals()` …).
// New code may import from the feature module directly.
import { authApi } from './auth'
import { wizardApi } from './wizard'
import { journeyAdminApi } from './journey'
import { dashboardAdminApi } from './dashboard'
import { todayAdminApi } from './today'
import { settingsAdminApi } from './settings'
import { searchesAdminApi } from './searches'
import { leadsAdminApi } from './leads'
import { renewalsAdminApi } from './renewals'
import { paymentsAdminApi } from './payments'
import { renewalOrdersAdminApi } from './renewalOrders'
import { ontraportSalesAdminApi } from './ontraportSales'
import { asicKeysAdminApi } from './asicKeys'
import { asicPortalAdminApi } from './asicPortal'
import { clientsAdminApi } from './clients'
import { emailsAdminApi } from './emails'
import { bpayAdminApi } from './bpay'

export * from './http'
export * from './auth'
export * from './wizard'
export * from './journey'
export * from './dashboard'
export * from './today'
export * from './settings'
export * from './renewalOrders'
export * from './asicKeys'
export * from './asicPortal'
export * from './clients'
export * from './emails'
export * from './bpay'

export const api = {
  ...authApi,
  ...wizardApi,

  admin: {
    ...journeyAdminApi,
    ...dashboardAdminApi,
    ...settingsAdminApi,
    ...searchesAdminApi,
    ...leadsAdminApi,
    ...renewalsAdminApi,
    ...paymentsAdminApi,
    ...ontraportSalesAdminApi,
    ...asicKeysAdminApi,
    ...asicPortalAdminApi,
    ...todayAdminApi,
    ...renewalOrdersAdminApi,
    ...clientsAdminApi,
    ...bpayAdminApi,
    ...emailsAdminApi,
  },
}
