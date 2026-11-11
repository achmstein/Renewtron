import { apiFetch } from './client'

export interface RegistrationAddress {
  unit: string
  streetNumber: string
  streetName: string
  streetType: string
  suburb: string
  state: string
  postcode: string
}

export interface RegistrationConfig {
  enabled: boolean
  oneYearPrice: number
  threeYearPrice: number
  stripePublishableKey: string
}

export interface RegistrationInput {
  businessName: string
  abn: string
  years: 1 | 3
  firstName: string
  lastName: string
  email: string
  phone: string
  serviceAddress: RegistrationAddress
  principalAddress: RegistrationAddress | null
  visitorId?: string | null
  source?: string | null
}

export type RegistrationStage = 'unpaid' | 'lodging' | 'lodged' | 'refused'

export interface PublicRegistration {
  id: string
  businessName: string
  abn: string
  years: 1 | 3
  amount: number
  firstName: string
  email: string
  paid: boolean
  stage: RegistrationStage
  asicReference: string | null
  isTest: boolean
  testStripePublishableKey: string | null
}

export interface RegistrationPayResponse { paid: boolean; requiresAction: boolean; clientSecret: string | null }

export interface AdminRegistration {
  id: string
  createdAt: string
  status: RegistrationStatus
  businessName: string
  abn: string
  years: number
  firstName: string
  lastName: string
  email: string
  phone: string
  serviceAddress: string
  principalAddress: string
  amount: number
  paidAt: string | null
  stripePaymentIntentId: string | null
  attemptCount: number
  startedAt: string | null
  finishedAt: string | null
  asicTransactionReference: string | null
  abrEntityName: string | null
  abrEntityType: string | null
  nameVerdict: string | null
  nameVerdictText: string | null
  asicPayBy: string | null
  asicFee: number | null
  lodgedAt: string | null
  payBy: string | null
  bpayBillerCode: string | null
  bpayReference: string | null
  airwallexTransferId: string | null
  airwallexTransferStatus: string | null
  bpayPaymentError: string | null
  asicPaidAt: string | null
  asicTransactionStatus: string | null
  asicPaymentStatus: string | null
  asicCheckedAt: string | null
  failedAtStep: string | null
  error: string | null
  isTest: boolean
}

export type RegistrationStatus =
  | 'AwaitingPayment' | 'Queued' | 'Running' | 'AwaitingAsicPayment' | 'Lodged'
  | 'Refused' | 'NeedsLook' | 'Failed' | 'Refunded'

export interface AdminRegistrationDetail {
  registration: AdminRegistration
  pageText: string | null
  asicFeeNow: number | null
}

const json = (body: unknown): RequestInit => ({ method: 'POST', body: JSON.stringify(body) })

export const registrationsApi = {
  config: () => apiFetch<RegistrationConfig>('/api/registrations/config'),
  nameCheck: (name: string) =>
    apiFetch<{ name: string; taken: boolean }>(`/api/registrations/name-check?name=${encodeURIComponent(name)}`),
  create: (input: RegistrationInput) => apiFetch<{ id: string }>('/api/registrations', json(input)),
  get: (id: string) => apiFetch<PublicRegistration>(`/api/registrations/${id}`),
  pay: (id: string, paymentMethodId: string) =>
    apiFetch<RegistrationPayResponse>(`/api/registrations/${id}/pay`, json({ paymentMethodId })),
  completePayment: (id: string, paymentIntentId: string) =>
    apiFetch<RegistrationPayResponse>(`/api/registrations/${id}/pay/complete`, json({ paymentIntentId })),

  admin: {
    list: (params: { status?: string; search?: string; page?: number; pageSize?: number }) => {
      const q = new URLSearchParams()
      if (params.status) q.set('status', params.status)
      if (params.search) q.set('search', params.search)
      q.set('page', String(params.page ?? 1))
      q.set('pageSize', String(params.pageSize ?? 25))
      return apiFetch<{ total: number; items: AdminRegistration[] }>(`/api/admin/registrations?${q}`)
    },
    get: (id: string) => apiFetch<AdminRegistrationDetail>(`/api/admin/registrations/${id}`),
    retry: (id: string) => apiFetch<{ message: string }>(`/api/admin/registrations/${id}/retry`, { method: 'POST' }),
    payAsic: (id: string, expectedAmount: number) =>
      apiFetch<{ message: string }>(`/api/admin/registrations/${id}/pay-asic`, json({ expectedAmount })),
    refunded: (id: string) => apiFetch<{ message: string }>(`/api/admin/registrations/${id}/refunded`, { method: 'POST' }),
  },
}

/** ASIC's street type codes, most common first (labels are what people type). */
export const STREET_TYPES: [code: string, label: string][] = [
  ['St', 'Street'], ['Rd', 'Road'], ['Ave', 'Avenue'], ['Dr', 'Drive'], ['Pl', 'Place'], ['Ct', 'Court'],
  ['Cres', 'Crescent'], ['Cl', 'Close'], ['Way', 'Way'], ['Pde', 'Parade'], ['Tce', 'Terrace'], ['Lane', 'Lane'],
  ['Hwy', 'Highway'], ['Bvd', 'Boulevard'], ['Cct', 'Circuit'], ['Gr', 'Grove'], ['Sq', 'Square'], ['Esp', 'Esplanade'],
  ['Rise', 'Rise'], ['Row', 'Row'], ['Pkwy', 'Parkway'], ['Mews', 'Mews'], ['Walk', 'Walk'], ['Trk', 'Track'],
  ['Loop', 'Loop'], ['View', 'View'], ['Rdge', 'Ridge'], ['Grn', 'Green'], ['Gdns', 'Gardens'], ['Hts', 'Heights'],
  ['Mall', 'Mall'], ['Arc', 'Arcade'], ['Bend', 'Bend'], ['Brae', 'Brae'], ['Pass', 'Pass'], ['Path', 'Path'],
  ['Wynd', 'Wynd'], ['Ally', 'Alley'], ['Cove', 'Cove'], ['Glen', 'Glen'], ['Hill', 'Hill'], ['Crst', 'Crest'],
  ['Dell', 'Dell'], ['Link', 'Link'], ['Rtt', 'Retreat'], ['Run', 'Run'], ['Prom', 'Promenade'], ['Qy', 'Quay'],
  ['Pnt', 'Point'], ['Plza', 'Plaza'], ['Cnr', 'Corner'], ['Fwy', 'Freeway'], ['Mwy', 'Motorway'], ['Otlk', 'Outlook'],
  ['Vsta', 'Vista'], ['Whrf', 'Wharf'], ['Con', 'Concourse'], ['Cir', 'Circle'], ['Park', 'Park'], ['Vale', 'Vale'],
]

export const STATES = ['ACT', 'NSW', 'NT', 'QLD', 'SA', 'TAS', 'VIC', 'WA'] as const

export const emptyAddress = (): RegistrationAddress => ({
  unit: '', streetNumber: '', streetName: '', streetType: 'St', suburb: '', state: '', postcode: '',
})
