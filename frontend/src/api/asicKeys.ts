import { apiFetch, type Facet } from './http'

// ASIC key letters (inbox) and key requests.
export interface AsicKeyJobStatus {
  jobId: string
  state: string
  done: boolean
  result: { skipped: boolean; message: string; discovered: number; processed: number; completed: number; failed: number } | null
  error: string | null
}

export interface AsicKeyRequestDto {
  id: string
  ontraportSaleId: string | null
  ontraportContactId: string | null
  businessName: string
  /** The client's other names under the same ABN; the enquiry text asks for their keys too. */
  otherBusinessNames: string[]
  abn: string
  contactName: string
  givenNames: string
  familyName: string
  email: string
  phone: string | null
  phonePrefix: string
  phoneNumber: string
  question: string | null
  source: string
  /** A test customer's request — never sent to ASIC. */
  isTest?: boolean
  status: string
  asicReferenceNumber: string | null
  errorMessage: string | null
  canAutoRetry: boolean
  asicKeyNotificationId: string | null
  attemptCount: number
  createdAt: string
  processedAt: string | null
  submittedAt: string | null
  keyReceivedAt: string | null
}

export interface AsicKeyNotificationDto {
  id: string
  businessName: string | null
  abn: string | null
  asicKey: string | null
  ontraportContactIds: string | null
  ontraportContactsUpdated: number
  status: string
  errorMessage: string | null
  pdfTextExcerpt: string | null
  subject: string | null
  from: string | null
  downloadUrl: string | null
  receivedAt: string
  processedAt: string | null
  attemptCount: number
}

/** A row of the Keys list: the PDF text, link and sender are fetched when the row is opened. */
export type AsicKeyNotificationListItem = Omit<AsicKeyNotificationDto, 'pdfTextExcerpt' | 'from' | 'downloadUrl'>

export const asicKeysAdminApi = {
  asicKeys: (params: { status?: string; search?: string; page?: number; pageSize?: number } = {}) => {
    const qs = new URLSearchParams()
    if (params.status) qs.set('status', params.status)
    if (params.search) qs.set('search', params.search)
    if (params.page) qs.set('page', String(params.page))
    if (params.pageSize) qs.set('pageSize', String(params.pageSize))
    const suffix = qs.toString() ? `?${qs}` : ''
    return apiFetch<{
      totalCount: number
      completedCount: number
      pendingCount: number
      attentionCount: number
      savedToClientCount: number
      filteredCount: number
      page: number
      pageSize: number
      items: AsicKeyNotificationListItem[]
      facets: {
        status: Facet
      }
      stats: {
        lastScanAt: string | null
        lastScanState: string | null
        lastScanError: string | null
        nextScanAt: string | null
        scanCron: string | null
        today: number
        yesterday: number
        deltaPct: number | null
        daily14d: Array<{ date: string; count: number }>
      }
    }>(`/api/admin/asic-keys${suffix}`)
  },
  asicKey: (id: string) => apiFetch<AsicKeyNotificationDto>(`/api/admin/asic-keys/${id}`),
  asicKeyContext: (id: string) => apiFetch<{
    client: { id: string; fullName: string } | null
    keyRequest: { id: string; status: string; createdAt: string; referenceNumber: string | null } | null
    sale: { id: string; status: string; syncedAt: string } | null
  }>(`/api/admin/asic-keys/${id}/context`),
  asicKeyRequest: (id: string) => apiFetch<{
    request: AsicKeyRequestDto
    client: { id: string; fullName: string } | null
    sale: { id: string; status: string; syncedAt: string } | null
    letter: { id: string; status: string; receivedAt: string; hasKey: boolean } | null
  }>(`/api/admin/asic-key-requests/${id}`),
  scanAsicKeys: () => apiFetch<{ jobId: string; message: string }>('/api/admin/asic-keys/scan', { method: 'POST' }),
  retryFailedAsicKeys: () => apiFetch<{ jobId: string; requeued: number; message: string }>('/api/admin/asic-keys/retry-failed', { method: 'POST' }),
  asicKeyJob: (jobId: string) => apiFetch<AsicKeyJobStatus>(`/api/admin/asic-keys/jobs/${encodeURIComponent(jobId)}`),
  retryAsicKey: (id: string) =>
    apiFetch<Pick<AsicKeyNotificationDto, 'id' | 'businessName' | 'abn' | 'asicKey' | 'ontraportContactIds' | 'ontraportContactsUpdated' | 'status' | 'errorMessage' | 'processedAt' | 'attemptCount'>>(`/api/admin/asic-keys/${id}/retry`, { method: 'POST' }),
  applyAsicKey: (id: string, contactId: string) =>
    apiFetch<Pick<AsicKeyNotificationDto, 'id' | 'businessName' | 'abn' | 'asicKey' | 'ontraportContactIds' | 'ontraportContactsUpdated' | 'status' | 'errorMessage' | 'processedAt' | 'attemptCount'>>(`/api/admin/asic-keys/${id}/apply`, { method: 'POST', body: JSON.stringify({ contactId }) }),
  asicKeyRequests: (params: { status?: string; search?: string; page?: number; pageSize?: number } = {}) => {
    const qs = new URLSearchParams()
    if (params.status) qs.set('status', params.status)
    if (params.search) qs.set('search', params.search)
    if (params.page) qs.set('page', String(params.page))
    if (params.pageSize) qs.set('pageSize', String(params.pageSize))
    const suffix = qs.toString() ? `?${qs}` : ''
    return apiFetch<{
      totalCount: number
      pendingCount: number
      manualCount: number
      submittedCount: number
      keyReceivedCount: number
      failedCount: number
      filteredCount: number
      page: number
      pageSize: number
      items: AsicKeyRequestDto[]
      facets: {
        status: Facet
      }
      stats: {
        lastRunAt: string | null
        lastRunState: string | null
        lastRunError: string | null
        nextRunAt: string | null
        runCron: string | null
        today: number
        yesterday: number
        deltaPct: number | null
        daily14d: Array<{ date: string; count: number }>
      }
    }>(`/api/admin/asic-key-requests${suffix}`)
  },
  runAsicKeyRequests: () => apiFetch<{ skipped: boolean; message: string; keysMatched: number; requeued: number }>('/api/admin/asic-key-requests/run', { method: 'POST' }),
  requeueAsicKeyRequest: (id: string) => apiFetch<AsicKeyRequestDto>(`/api/admin/asic-key-requests/${id}/requeue`, { method: 'POST' }),
  requestAsicKeyForSale: (saleId: string) =>
    apiFetch<{ request: AsicKeyRequestDto }>(`/api/admin/asic-key-requests/for-sale/${saleId}`, { method: 'POST' }),
  recordAsicKeyRequestOutcome: (id: string, body: { referenceNumber: string; note: string }) =>
    apiFetch<AsicKeyRequestDto>(`/api/admin/asic-key-requests/${id}/outcome`, { method: 'POST', body: JSON.stringify(body) }),
}
