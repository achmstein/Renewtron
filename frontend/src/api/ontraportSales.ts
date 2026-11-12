import { apiFetch, type Facet } from './http'

// Ontraport sales and their processing.
export const ontraportSalesAdminApi = {
  requeueFailedOntraport: (params: { dryRun: boolean; max?: number; includePastDue?: boolean; errorContains?: string }) => {
    const qs = new URLSearchParams({ dryRun: String(params.dryRun) })
    if (params.max != null) qs.set('max', String(params.max))
    if (params.includePastDue) qs.set('includePastDue', 'true')
    if (params.errorContains) qs.set('errorContains', params.errorContains)
    return apiFetch<{
      dryRun: boolean
      totalMatching: number
      batchSize: number
      requeued: number
      note: string
      items: Array<{ id: string; businessName: string; abn: string; amountPaid: number; renewalDueDate?: string | null; error?: string | null }>
    }>(`/api/admin/ontraport-sales/requeue-failed?${qs}`, { method: 'POST' })
  },
  ontraportSales: (params: { status?: string; search?: string; due?: string; page?: number; pageSize?: number } = {}) => {
    const qs = new URLSearchParams()
    if (params.status) qs.set('status', params.status)
    if (params.search) qs.set('search', params.search)
    if (params.due) qs.set('due', params.due)
    if (params.page) qs.set('page', String(params.page))
    if (params.pageSize) qs.set('pageSize', String(params.pageSize))
    const suffix = qs.toString() ? `?${qs}` : ''
    type SaleItem = { id: string; contactName: string; email: string; abn: string; businessName: string; renewalYears: number; amountPaid: number; status: string; syncedAt: string; renewalDueDate?: string | null; errorMessage?: string | null; renewalRequestId?: string | null; renewalStatus?: string | null; renewalFailedAtStep?: string | null; renewalErrorMessage?: string | null; clientId?: string | null; handledAt?: string | null; orderId?: string | null }
    return apiFetch<{
      filteredCount: number
      page: number
      pageSize: number
      view: string
      buckets: { overdue: number; overdueValue: number; soon7: number; soon30: number; all: number }
      upNext: SaleItem[]
      totalCount: number
      waitingCount: number
      queuedCount: number
      completedCount: number
      failedCount: number
      ineligibleCount: number
      asicNotYetDueCount: number
      renewalInProgressCount: number
      blockedCount: number
      items: SaleItem[]
      facets: {
        status: Facet
      }
      stats: {
        pipelineValueNext30d: number
        lastSyncAt: string | null
        nextSyncAt: string | null
        today: number
        yesterday: number
        deltaPct: number | null
        daily14d: Array<{ date: string; count: number }>
      }
    }>(`/api/admin/ontraport-sales${suffix}`)
  },
  ontraportSale: (id: string) => apiFetch<{
    id: string
    client: { id: string; fullName: string } | null
    contactName: string
    email: string
    mobileNumber: string
    dateOfBirth: string | null
    businessName: string
    abn: string
    businessNameOwner: string | null
    address: string
    renewalYears: number
    amountPaid: number
    renewalDueDate: string | null
    status: string
    syncedAt: string
    processedAt: string | null
    errorMessage: string | null
    handledAt: string | null
    handledNote: string | null
    ontraportContactId: string
    ontraportTransactionId: string
    renewal: {
      id: string; status: string; errorCategory: string | null; nextRetryAt: string | null; initiatedAt: string
      completedAt: string | null; bpayReference: string | null; bpayPaidAt: string | null; transactionReference: string | null
    } | null
    keyRequests: Array<{ id: string; status: string; createdAt: string; referenceNumber: string | null; keyReceivedAt: string | null }>
    keyLetters: Array<{ id: string; status: string; receivedAt: string; hasKey: boolean }>
    /** The paid renewal this sale became: where it's worked. */
    orderId: string | null
  }>(`/api/admin/ontraport-sales/${id}`),
  /** Puts back on the work queues a sale the old-records cleanup marked handled. */
  unmarkSaleHandled: (id: string) =>
    apiFetch<{ handledAt: string | null }>(`/api/admin/ontraport-sales/${id}/handled`, { method: 'DELETE' }),
  requeueOntraportSale: (id: string) => apiFetch<{ message: string }>(`/api/admin/ontraport-sales/${id}/requeue`, { method: 'POST' }),
  syncOntraport: () => apiFetch<{ syncedCount: number; message: string }>('/api/admin/ontraport-sales/sync', { method: 'POST' }),
  processEligibleOntraport: () => apiFetch<{ jobId: string; message: string }>('/api/admin/ontraport-sales/process-eligible', { method: 'POST' }),
}
