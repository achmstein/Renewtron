import { apiFetch } from './http'

// The customer renewal wizard: lead, ASIC check, payment and status.
export interface PricingResponse { oneYearFee: number; threeYearFee: number }
export interface BusinessNameDto { id: string; businessName: string; accountNumber: string; registrationDate: string }

/** Contact fields are masked by the server for display ("d•••@example.com"); never
 *  send them back. Date of birth is not returned. */
export interface LeadDto {
  id: string
  abn: string
  fullName: string
  email: string
  mobileNumber: string
  dateOfBirth?: string | null
  outcome: string
  outcomeMessage?: string | null
  /** False while names listed from our copy of the register wait on ASIC's confirmation. */
  verified: boolean
  businessNames: BusinessNameDto[]
  /** A test customer (Admin → Settings → Test mode): pays with Stripe's test keys, nothing reaches ASIC. */
  testMode?: boolean
  testStripePublishableKey?: string | null
}

export interface CheckResponse {
  outcome: 'RenewalAvailable' | 'NotDueForRenewal' | 'RenewalInProgress' | 'NoBusinessNames'
  message?: string
  verified: boolean
  businessNames: BusinessNameDto[]
}

export interface BatchRenewalResponse {
  renewalIds: string[]
  total: number
  /** True when the card wants 3D Secure — run stripe.handleNextAction(clientSecret), then completeBatchRenewal. */
  requiresAction?: boolean
  clientSecret?: string | null
  /** One-click Business Portal sign-in for this customer; null when the portal isn't configured. */
  portalSignInUrl?: string | null
}
export interface RenewalStatusItem {
  id: string
  /** Received: paid, and Renewtron lodges it with ASIC as soon as ASIC takes it. */
  status: 'Pending' | 'Processing' | 'Completed' | 'Failed' | 'Received'
  amount: number
  renewalYears: number
  businessName: string
  accountNumber: string
  transactionReference?: string | null
  /** Customer-safe explanation when a renewal is delayed or failed. */
  customerMessage?: string | null
}

export const wizardApi = {
  pricing: () => apiFetch<PricingResponse>('/api/pricing'),
  createLead: (input: { abn: string; fullName: string; email: string; mobileNumber: string; dateOfBirth: string; tfn?: string; source?: string; ontraportContactId?: string; visitorId?: string }) =>
    apiFetch<{ leadId: string }>('/api/leads', { method: 'POST', body: JSON.stringify(input) }),
  getLead: (id: string) => apiFetch<LeadDto>(`/api/leads/${id}`),
  /** Starts ASIC's search for this ABN early; the answer is reused by the check step. */
  prefetchSearch: (abn: string) => apiFetch<void>('/api/search/prefetch', { method: 'POST', body: JSON.stringify({ abn }) }),
  checkLead: (id: string) => apiFetch<CheckResponse>(`/api/leads/${id}/check`, { method: 'POST' }),
  createBatchRenewal: (input: { leadId: string; searchResultIds: string[]; renewalYears: 1 | 3; paymentMethodId: string; cardholderName?: string }) =>
    apiFetch<BatchRenewalResponse>('/api/renewals/batch', { method: 'POST', body: JSON.stringify(input) }),
  completeBatchRenewal: (input: { leadId: string; searchResultIds: string[]; renewalYears: 1 | 3; paymentIntentId: string; cardholderName?: string }) =>
    apiFetch<{ renewalIds: string[]; total: number; portalSignInUrl?: string | null }>('/api/renewals/batch/complete', { method: 'POST', body: JSON.stringify(input) }),
  submitAsicKey: (input: { businessName: string; abn: string; asicKey: string }) =>
    apiFetch<{ ok: boolean }>('/api/asic-key', { method: 'POST', body: JSON.stringify(input) }),
  batchStatus: (ids: string[]) => apiFetch<RenewalStatusItem[]>(`/api/renewals/batch?ids=${ids.join(',')}`),
}
