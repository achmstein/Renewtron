import { apiFetch } from './http'
import { type AsicPortalLinkStatus } from './asicPortal'

// Clients: one per person, with every ABN and business name.
export interface ClientListItem {
  id: string
  fullName: string
  email: string | null
  emailCount: number
  mobileNumber: string | null
  abns: string[]
  activeNames: number
  firstName: string | null
  nextDue: string | null
  keys: number
  /** On an ABN where someone else gets the reminders. */
  sharesAbn: boolean
  hasPurchased: boolean
  isTest: boolean
  possibleDuplicateOfId: string | null
  possibleDuplicateOfName: string | null
  lastSeenAt: string
}

export interface ClientBusinessNameDto {
  id: string
  name: string
  abn: string
  registrationDate: string | null
  asicAccountNumber: string | null
  renewalDueDate: string | null
  dueDateSource: string | null
  asicKey: string | null
  asicKeyReceivedAt: string | null
  lastRenewedAt: string | null
  lastRenewalYears: number | null
  status: 'Active' | 'Cancelled' | 'Excluded'
  source: string
  /** When the name was linked to our ASIC Connect account. */
  portalLinkedAt: string | null
  /** The latest "link to our ASIC account" run for the name. */
  portalLink: { id: string; status: AsicPortalLinkStatus; createdAt: string; finishedAt: string | null } | null
}

export interface ClientAbnDto {
  abn: string
  /** This client gets the ABN's reminders. */
  isReminderContact: boolean
  registerCheckedAt: string | null
  /** The ABN's other contacts. */
  contacts: Array<{ clientId: string; fullName: string; isReminderContact: boolean }>
  businessNames: ClientBusinessNameDto[]
}

export interface ClientDetail {
  id: string
  fullName: string
  mobileNumber: string | null
  dateOfBirth: string | null
  address: string | null
  hasTfn: boolean
  hasPurchased: boolean
  reminderOptIn: boolean
  unsubscribedAt: string | null
  isTest: boolean
  notes: string | null
  createdAt: string
  lastSeenAt: string
  possibleDuplicateOf: { id: string; fullName: string } | null
  duplicates: Array<{ id: string; fullName: string }>
  emails: Array<{ id: string; email: string; isPrimary: boolean; ontraportContactId: string | null; source: string }>
  abns: ClientAbnDto[]
  sales: Array<{ id: string; businessName: string; status: string; syncedAt: string; amountPaid: number; renewalDueDate: string | null; ontraportContactId: string }>
  leads: Array<{ id: string; createdAt: string; outcome: string; abn: string; email: string }>
  renewals: Array<{ id: string; businessName: string; status: string; initiatedAt: string; completedAt: string | null; amount: number; renewalYears: number; source: string; bpayReference: string | null; bpayPaidAt: string | null }>
  keyRequests: Array<{ id: string; businessName: string; status: string; createdAt: string; asicReferenceNumber: string | null }>
  messages: Array<{ id: string; template: string; subject: string; status: string; createdAt: string; recipient: string }>
}

export interface ClientLinkResult {
  dryRun: boolean
  startedAt: string
  finishedAt: string
  error: string | null
  clientsCreated: number
  possibleDuplicates: number
  emailsAdded: number
  abnsAdded: number
  sharedAbnContacts: number
  namesAdded: number
  namesCancelled: number
  abnsChecked: number
  dueDatesSet: number
  keysSet: number
  salesLinked: number
  leadsLinked: number
  keyRequestsLinked: number
  renewalsLinked: number
  renewalsCompletedLater: number
  messagesLinked: number
  keyLettersLinked: number
  keyLettersResolved: number
  skipped: number
}

export interface ClientLinkStatus {
  hasClients: boolean
  running: boolean
  runningSince: string | null
  runningDry: boolean
  last: ClientLinkResult | null
}

export const clientsAdminApi = {
  /** The client a record belongs to; 404 (thrown) when it isn't on one yet. */
  clientLookup: (kind: 'lead' | 'renewal' | 'sale' | 'keyRequest' | 'keyLetter', id: string) =>
    apiFetch<{ id: string; fullName: string; possibleDuplicate: boolean }>(`/api/admin/clients/lookup?kind=${kind}&id=${encodeURIComponent(id)}`),
  clients: (params: { search?: string; filter?: string; page?: number; pageSize?: number; take?: number } = {}) => {
    const qs = new URLSearchParams()
    if (params.search) qs.set('search', params.search)
    if (params.filter) qs.set('filter', params.filter)
    if (params.page) qs.set('page', String(params.page))
    if (params.pageSize) qs.set('pageSize', String(params.pageSize))
    if (params.take) qs.set('take', String(params.take))
    const suffix = qs.toString() ? `?${qs}` : ''
    return apiFetch<{ totalCount: number; page: number; pageSize: number; items: ClientListItem[] }>(`/api/admin/clients${suffix}`)
  },
  clientStats: () => apiFetch<{ clients: number; purchased: number; severalNames: number; names: number; possibleDuplicates: number }>('/api/admin/clients/stats'),
  client: (id: string) => apiFetch<ClientDetail>(`/api/admin/clients/${id}`),
  clientLinkStatus: () => apiFetch<ClientLinkStatus>('/api/admin/clients/link-status'),
  runClientLink: (dryRun: boolean) => apiFetch<void>('/api/admin/clients/link', { method: 'POST', body: JSON.stringify({ dryRun }) }),
  mergeClient: (id: string, targetId: string) => apiFetch<{ id: string }>(`/api/admin/clients/${id}/merge-into/${targetId}`, { method: 'POST' }),
  dismissClientDuplicate: (id: string) => apiFetch<void>(`/api/admin/clients/${id}/not-duplicate`, { method: 'POST' }),
  updateClientNotes: (id: string, notes: string) => apiFetch<void>(`/api/admin/clients/${id}/notes`, { method: 'PUT', body: JSON.stringify({ notes }) }),
  setAbnReminderContact: (abn: string, clientId: string) =>
    apiFetch<void>(`/api/admin/clients/abns/${abn}/reminder-contact`, { method: 'POST', body: JSON.stringify({ clientId }) }),
  setClientBusinessNameStatus: (nameId: string, status: ClientBusinessNameDto['status']) =>
    apiFetch<void>(`/api/admin/clients/business-names/${nameId}/status`, { method: 'POST', body: JSON.stringify({ status }) }),
}
