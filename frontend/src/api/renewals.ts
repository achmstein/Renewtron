import { apiFetch } from './http'

// ASIC renewal runs and manual renewals.
export const renewalsAdminApi = {
  renewals: (params: { abn?: string; status?: string; initiatedBy?: string; source?: string; failedAtStep?: string; errorCategory?: string; dateFrom?: string; dateTo?: string; view?: 'toFix'; page?: number; pageSize?: number } = {}) => {
    const qs = new URLSearchParams()
    if (params.abn) qs.set('abn', params.abn)
    if (params.status) qs.set('status', params.status)
    if (params.initiatedBy) qs.set('initiatedBy', params.initiatedBy)
    if (params.source) qs.set('source', params.source)
    if (params.failedAtStep) qs.set('failedAtStep', params.failedAtStep)
    if (params.errorCategory) qs.set('errorCategory', params.errorCategory)
    if (params.dateFrom) qs.set('dateFrom', params.dateFrom)
    if (params.dateTo) qs.set('dateTo', params.dateTo)
    if (params.view) qs.set('view', params.view)
    if (params.page) qs.set('page', String(params.page))
    if (params.pageSize) qs.set('pageSize', String(params.pageSize))
    const suffix = qs.toString() ? `?${qs}` : ''
    return apiFetch<{
      totalCount: number
      page: number
      pageSize: number
      items: Array<{
        id: string
        abn: string
        businessName: string
        renewalYears: number
        amount: number
        status: 'Pending' | 'Processing' | 'Completed' | 'Failed'
        source: string
        /** A test customer's renewal — Stripe test payment, not sent to ASIC. */
        isTest?: boolean
        paymentType: 'Stripe' | 'External'
        initiatedAt: string
        completedAt?: string | null
        email?: string
        errorMessage?: string | null
        failedAtStep?: string | null
        errorCategory?: string | null
        attemptCount: number
        nextRetryAt?: string | null
        handledAt?: string | null
        transactionReference?: string | null
        bpayReference?: string | null
        bpayPaidAt?: string | null
        initiatedByLabel: string
        stripePaymentSucceeded: boolean
        cardBrand?: string | null
        cardLast4?: string | null
        lead?: { id: string; fullName: string; email: string } | null
        timeInStatusHours: number
      }>
      facets: {
        status: Array<{ value: string; count: number }>
        source: Array<{ value: string; count: number }>
        initiatedBy: Array<{ value: string; count: number }>
        errorCategory: Array<{ value: string; count: number }>
      }
      stats: {
        successRate30d: number
        completed30d: number
        decided30d: number
        total30d: number
        avgCompletionMinutes: number | null
        revenueMtd: number
        liveValue: number
        stuckValue: number
        stuckCount: number
        scheduledRetryValue: number
        needsReviewCount: number
        needsReviewValue: number
        failedValue30d: number
        today: number
        yesterday: number
        deltaPct: number | null
        daily14d: Array<{ date: string; count: number }>
        errorCategoryBreakdown: Array<{ category: string; count: number; retryable: boolean }>
      }
    }>(`/api/admin/renewals${suffix}`)
  },
  retryRenewalsBulk: (params: { dateFrom?: string; dateTo?: string; source?: string } = {}) => {
    const qs = new URLSearchParams()
    if (params.dateFrom) qs.set('dateFrom', params.dateFrom)
    if (params.dateTo) qs.set('dateTo', params.dateTo)
    if (params.source) qs.set('source', params.source)
    const suffix = qs.toString() ? `?${qs}` : ''
    return apiFetch<{ retried: number; skipped: number; skippedPaymentRisk: number }>(`/api/admin/renewals/retry-bulk${suffix}`, { method: 'POST' })
  },
  reconcileRenewals: (params: { dryRun: boolean; maxRequeue?: number }) => {
    const qs = new URLSearchParams({ dryRun: String(params.dryRun) })
    if (params.maxRequeue != null) qs.set('maxRequeue', String(params.maxRequeue))
    return apiFetch<{
      dryRun: boolean
      scanned: number
      skippedLiveJob: number
      requeued: number
      needsVerification: number
      markedStale: number
      requeueCapped: number
      items: Array<{ renewalId: string; businessName?: string | null; abn?: string | null; amount: number; status: string; ageHours: number; action: string }>
    }>(`/api/admin/renewals/reconcile?${qs}`, { method: 'POST' })
  },
  renewal: (id: string) => apiFetch<{
    id: string
    /** The paid renewal this ASIC run is an attempt at. */
    order: { id: string; stage: string; reason: string | null; paidAt: string } | null
    client: { id: string; fullName: string } | null
    clientBusinessNameId: string | null
    searchLogId: string
    searchedAt: string
    sale: { id: string; syncedAt: string; amountPaid: number; renewalDueDate: string | null } | null
    dueDate: string | null
    stuck: boolean
    status: 'Pending' | 'Processing' | 'Completed' | 'Failed'
    source: string
    paymentType: 'Stripe' | 'External'
    businessName: string
    accountNumber: string
    abn: string
    ipAddress?: string | null
    initiatedByLabel: string
    renewalYears: number
    amount: number
    email?: string | null
    mobileNumber?: string | null
    dateOfBirth?: string | null
    initiatedAt: string
    completedAt?: string | null
    transactionReference?: string | null
    hostedTokenizationId?: string | null
    bpayBillerCode?: string | null
    bpayReference?: string | null
    bpayPaidAt?: string | null
    /** Airwallex is paying the bill now (or its outcome isn't known yet): don't pay or mark it paid. */
    bpayTransferUnderWay?: boolean
    bpayTransferStatus?: string | null
    bpayPaymentError?: string | null
    errorMessage?: string | null
    failedAtStep?: string | null
    errorCategory?: string | null
    attemptCount: number
    cardFailureCount: number
    lastAttemptAt?: string | null
    nextRetryAt?: string | null
    handledAt?: string | null
    handledNote?: string | null
    stripePayment?: {
      paymentIntentId: string
      paymentStatus: string
      paidAt?: string | null
      cardholderName?: string | null
      cardLast4?: string | null
      cardBrand?: string | null
      cardExpMonth?: string | null
      cardExpYear?: string | null
    } | null
    lead?: { id: string; fullName: string; email: string } | null
  }>(`/api/admin/renewals/${id}`),
  retryRenewal: (id: string) => apiFetch<{ message: string }>(`/api/admin/renewals/${id}/retry`, { method: 'POST' }),
  /** Puts back on the work queues a renewal the old-records cleanup marked handled. */
  unmarkRenewalHandled: (id: string) =>
    apiFetch<{ handledAt: string | null }>(`/api/admin/renewals/${id}/handled`, { method: 'DELETE' }),
  markBpayPaid: (id: string, paid: boolean) =>
    apiFetch<{ bpayPaidAt: string | null }>(`/api/admin/renewals/${id}/bpay-paid?paid=${paid}`, { method: 'POST' }),
  manualRenewal: (input: { abn: string; businessName: string; accountNumber: string; registrationDate?: string; renewalYears: 1 | 3; email?: string }) =>
    apiFetch<{ renewalId: string; status: string; message?: string }>('/api/admin/manual-renewal', { method: 'POST', body: JSON.stringify(input) }),
  manualSearch: (abn: string) => apiFetch<{
    success: boolean
    errorMessage?: string
    results: Array<{ id: string; businessName: string; accountNumber: string; registrationDate: string }>
  }>('/api/admin/manual-search', { method: 'POST', body: JSON.stringify({ abn }) }),
  submitManualRenewal: (input: { searchResultId: string; renewalYears: 1 | 3; email: string; mobileNumber?: string; dateOfBirth?: string; amount: number }) =>
    apiFetch<{ renewalId: string; businessName: string; message: string }>('/api/admin/manual-renewal/submit', { method: 'POST', body: JSON.stringify(input) }),
}
