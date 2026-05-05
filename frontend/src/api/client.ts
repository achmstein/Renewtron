export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const isForm = init?.body instanceof FormData
  const response = await fetch(path, {
    credentials: 'include',
    headers: {
      ...(isForm ? {} : { 'Content-Type': 'application/json' }),
      ...(init?.headers ?? {}),
    },
    ...init,
  })

  if (!response.ok) {
    const text = await response.text().catch(() => '')
    let message = `Request failed: ${response.status}`
    try {
      const parsed = JSON.parse(text)
      message = parsed.error || parsed.detail || parsed.title || message
    } catch {
      if (text) message = text
    }
    throw new Error(message)
  }

  if (response.status === 204) return undefined as T
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('application/json')) return undefined as T
  return (await response.json()) as T
}

export interface MeResponse { email: string; name: string }
export interface PricingResponse { oneYearFee: number; threeYearFee: number }
export interface BusinessNameDto { id: string; businessName: string; accountNumber: string; registrationDate: string }

export interface LeadDto {
  id: string
  abn: string
  fullName: string
  email: string
  mobileNumber: string
  dateOfBirth: string
  outcome: string
  outcomeMessage?: string | null
  businessNames: BusinessNameDto[]
}

export interface CheckResponse {
  outcome: 'RenewalAvailable' | 'NotDueForRenewal' | 'RenewalInProgress' | 'NoBusinessNames'
  message?: string
  businessNames: BusinessNameDto[]
}

export interface BatchRenewalResponse { renewalIds: string[]; total: number }
export interface RenewalStatusItem {
  id: string
  status: 'Pending' | 'Processing' | 'Completed' | 'Failed'
  amount: number
  renewalYears: number
  businessName: string
  accountNumber: string
  transactionReference?: string | null
  errorMessage?: string | null
}

export interface DashboardResponse {
  stats: {
    totalSearches: number
    successfulSearches: number
    renewalsInitiated: number
    renewalsCompleted: number
    renewalsPending: number
    renewalsFailed: number
    totalLeads: number
    convertedLeads: number
    notDueLeads: number
    renewtronDirectCount: number
    renewtronDirectRevenue: number
    ontraportCount: number
    ontraportRevenue: number
  }
  recentSearches: Array<{ id: string; abn: string; searchedAt: string; success: boolean; resultsCount: number }>
  recentRenewals: Array<{ id: string; businessName: string | null; initiatedAt: string; status: string }>
}

export const api = {
  me: () => apiFetch<MeResponse>('/api/me'),
  login: (email: string, password: string) =>
    apiFetch<unknown>('/api/login?useCookies=true', { method: 'POST', body: JSON.stringify({ email, password }) }),
  register: (email: string, password: string) =>
    apiFetch<void>('/api/register', { method: 'POST', body: JSON.stringify({ email, password }) }),
  logout: () => apiFetch<void>('/api/logout', { method: 'POST' }),

  pricing: () => apiFetch<PricingResponse>('/api/pricing'),
  createLead: (input: { abn: string; fullName: string; email: string; mobileNumber: string; dateOfBirth: string; tfn?: string }) =>
    apiFetch<{ leadId: string }>('/api/leads', { method: 'POST', body: JSON.stringify(input) }),
  getLead: (id: string) => apiFetch<LeadDto>(`/api/leads/${id}`),
  checkLead: (id: string) => apiFetch<CheckResponse>(`/api/leads/${id}/check`, { method: 'POST' }),
  createBatchRenewal: (input: { leadId: string; searchResultIds: string[]; renewalYears: 1 | 3; paymentMethodId: string; cardholderName?: string }) =>
    apiFetch<BatchRenewalResponse>('/api/renewals/batch', { method: 'POST', body: JSON.stringify(input) }),
  batchStatus: (ids: string[]) => apiFetch<RenewalStatusItem[]>(`/api/renewals/batch?ids=${ids.join(',')}`),

  admin: {
    dashboard: () => apiFetch<DashboardResponse>('/api/admin/dashboard'),
    settings: () => apiFetch<{
      sendGrid: { apiKey: string; fromEmail: string; fromName: string }
      stripe: { secretKey: string; publishableKey: string }
      pricing: { oneYearFee: number; threeYearFee: number }
      asic: { forceFallback: boolean; email: string; cardNumber: string; cardholderName: string; expiryMonth: string; expiryYear: string; cvc: string }
      ontraport: { apiAppId: string; apiKey: string; conversationId: string }
    }>('/api/admin/settings'),
    searches: (params: { abn?: string; success?: string; initiatedBy?: string; dateFrom?: string; dateTo?: string; page?: number; pageSize?: number } = {}) => {
      const qs = new URLSearchParams()
      if (params.abn) qs.set('abn', params.abn)
      if (params.success) qs.set('success', params.success)
      if (params.initiatedBy) qs.set('initiatedBy', params.initiatedBy)
      if (params.dateFrom) qs.set('dateFrom', params.dateFrom)
      if (params.dateTo) qs.set('dateTo', params.dateTo)
      if (params.page) qs.set('page', String(params.page))
      if (params.pageSize) qs.set('pageSize', String(params.pageSize))
      const suffix = qs.toString() ? `?${qs}` : ''
      return apiFetch<{
        totalCount: number
        page: number
        pageSize: number
        items: Array<{ id: string; abn: string; sessionId?: string | null; searchedAt: string; success: boolean; errorMessage?: string | null; resultsCount: number; initiatedBy: string; ipAddress?: string | null; hasRenewal: boolean }>
      }>(`/api/admin/searches${suffix}`)
    },
    search: (id: string) => apiFetch<{
      id: string
      abn: string
      sessionId?: string | null
      searchedAt: string
      success: boolean
      errorMessage?: string | null
      ipAddress?: string | null
      userAgent?: string | null
      resultsCount: number
      initiatedBy: string
      lead?: { id: string; fullName: string; email: string; mobileNumber: string; outcome: string } | null
      results: Array<{
        id: string
        businessName: string
        accountNumber: string
        registrationDate: string
        renewalRequest?: { id: string; status: string; initiatedAt: string; renewalYears: number; amount: number } | null
      }>
    }>(`/api/admin/searches/${id}`),
    leads: (params: { outcome?: string; reminder?: string; search?: string; take?: number } = {}) => {
      const qs = new URLSearchParams()
      if (params.outcome) qs.set('outcome', params.outcome)
      if (params.reminder) qs.set('reminder', params.reminder)
      if (params.search) qs.set('search', params.search)
      if (params.take) qs.set('take', String(params.take))
      const suffix = qs.toString() ? `?${qs}` : ''
      return apiFetch<{
        totalCount: number
        convertedCount: number
        notDueCount: number
        reminderCount: number
        items: Array<{ id: string; abn: string; fullName: string; email: string; mobileNumber: string; createdAt: string; outcome: string; reminderOptIn: boolean; convertedToRenewal: boolean }>
      }>(`/api/admin/leads${suffix}`)
    },
    lead: (id: string) => apiFetch<{
      id: string
      abn: string
      fullName: string
      email: string
      mobileNumber: string
      dateOfBirth: string
      createdAt: string
      ipAddress?: string | null
      userAgent?: string | null
      sessionId?: string | null
      outcome: string
      outcomeMessage?: string | null
      reminderOptIn: boolean
      convertedToRenewal: boolean
      convertedAt?: string | null
      searchLog?: {
        id: string
        searchedAt: string
        success: boolean
        errorMessage?: string | null
        resultsCount: number
        results: Array<{ id: string; businessName: string; accountNumber: string; registrationDate: string }>
      } | null
      renewalRequests: Array<{ id: string; status: string; amount: number; renewalYears: number; initiatedAt: string; businessName?: string | null }>
    }>(`/api/admin/leads/${id}`),
    renewals: (params: { abn?: string; status?: string; initiatedBy?: string; dateFrom?: string; dateTo?: string; page?: number; pageSize?: number } = {}) => {
      const qs = new URLSearchParams()
      if (params.abn) qs.set('abn', params.abn)
      if (params.status) qs.set('status', params.status)
      if (params.initiatedBy) qs.set('initiatedBy', params.initiatedBy)
      if (params.dateFrom) qs.set('dateFrom', params.dateFrom)
      if (params.dateTo) qs.set('dateTo', params.dateTo)
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
          paymentType: 'Stripe' | 'External'
          initiatedAt: string
          completedAt?: string | null
          email?: string
          errorMessage?: string | null
          transactionReference?: string | null
          initiatedByLabel: string
          stripePaymentSucceeded: boolean
        }>
      }>(`/api/admin/renewals${suffix}`)
    },
    renewal: (id: string) => apiFetch<{
      id: string
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
      errorMessage?: string | null
      failedAtStep?: string | null
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
    manualRenewal: (input: { abn: string; businessName: string; accountNumber: string; registrationDate?: string; renewalYears: 1 | 3; email?: string }) =>
      apiFetch<{ renewalId: string; status: string; transactionReference?: string; errorMessage?: string }>('/api/admin/manual-renewal', { method: 'POST', body: JSON.stringify(input) }),
    manualSearch: (abn: string) => apiFetch<{
      success: boolean
      errorMessage?: string
      results: Array<{ id: string; businessName: string; accountNumber: string; registrationDate: string }>
    }>('/api/admin/manual-search', { method: 'POST', body: JSON.stringify({ abn }) }),
    submitManualRenewal: (input: { searchResultId: string; renewalYears: 1 | 3; email: string; mobileNumber?: string; dateOfBirth?: string; amount: number }) =>
      apiFetch<{ renewalId: string; businessName: string; message: string }>('/api/admin/manual-renewal/submit', { method: 'POST', body: JSON.stringify(input) }),
    ontraportSales: () => apiFetch<{
      totalCount: number
      waitingCount: number
      queuedCount: number
      completedCount: number
      failedCount: number
      items: Array<{ id: string; contactName: string; email: string; abn: string; businessName: string; renewalYears: number; amountPaid: number; status: string; syncedAt: string; renewalDueDate?: string | null; errorMessage?: string | null }>
    }>('/api/admin/ontraport-sales'),
    syncOntraport: () => apiFetch<{ syncedCount: number; message: string }>('/api/admin/ontraport-sales/sync', { method: 'POST' }),
    processEligibleOntraport: () => apiFetch<{ jobId: string; message: string }>('/api/admin/ontraport-sales/process-eligible', { method: 'POST' }),
    bulkRenewals: () => apiFetch<{
      totalCount: number
      waitingCount: number
      queuedCount: number
      completedCount: number
      failedCount: number
      items: Array<{ id: string; businessName: string; abn: string; ownerName?: string; renewalYears: number; amount: number; status: string; uploadedAt: string; renewalDueDate?: string | null; sourceFile?: string; errorMessage?: string | null }>
    }>('/api/admin/bulk-renewals'),
    processEligibleBulk: () => apiFetch<{ jobId: string; message: string }>('/api/admin/bulk-renewals/process-eligible', { method: 'POST' }),
    uploadBulkRenewals: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return apiFetch<{ totalRows: number; importedCount: number; skippedDuplicates: number; skippedInvalid: number; errors: string[] }>('/api/admin/bulk-renewals/upload', { method: 'POST', body: form })
    },
    updatePricing: (body: { oneYearFee: number; threeYearFee: number }) => apiFetch<void>('/api/admin/settings/pricing', { method: 'PUT', body: JSON.stringify(body) }),
    updateSendGrid: (body: { apiKey: string; fromEmail: string; fromName: string }) => apiFetch<void>('/api/admin/settings/sendgrid', { method: 'PUT', body: JSON.stringify(body) }),
    updateStripe: (body: { secretKey: string; publishableKey: string }) => apiFetch<void>('/api/admin/settings/stripe', { method: 'PUT', body: JSON.stringify(body) }),
    updateAsic: (body: { forceFallback: boolean; email: string; cardNumber: string; cardholderName: string; expiryMonth: string; expiryYear: string; cvc: string }) => apiFetch<void>('/api/admin/settings/asic', { method: 'PUT', body: JSON.stringify(body) }),
    updateOntraport: (body: { apiAppId: string; apiKey: string; conversationId: string }) => apiFetch<void>('/api/admin/settings/ontraport', { method: 'PUT', body: JSON.stringify(body) }),

    atoOnboarding: (params: { status?: string; search?: string; take?: number } = {}) => {
      const qs = new URLSearchParams()
      if (params.status) qs.set('status', params.status)
      if (params.search) qs.set('search', params.search)
      if (params.take) qs.set('take', String(params.take))
      const suffix = qs.toString() ? `?${qs}` : ''
      return apiFetch<{
        totalCount: number
        pendingCount: number
        completedCount: number
        failedCount: number
        items: Array<{
          renewalRequestId: string
          leadId?: string | null
          fullName?: string | null
          email?: string | null
          abn: string
          businessName: string
          initiatedAt: string
          completedAt?: string | null
          renewalStatus: string
          atoJobId: string
          atoStatus?: string | null
          atoCompletedAt?: string | null
        }>
      }>(`/api/admin/ato-onboarding${suffix}`)
    },
    atoOnboardingDetail: (renewalId: string) => apiFetch<{
      renewalRequestId: string
      leadId?: string | null
      fullName?: string | null
      email?: string | null
      mobileNumber?: string | null
      dateOfBirth?: string | null
      tfn?: string | null
      abn?: string | null
      businessName?: string | null
      initiatedAt: string
      completedAt?: string | null
      renewalStatus: string
      atoJobId: string
      atoStatus?: string | null
      atoCompletedAt?: string | null
      atoResultJson?: string | null
    }>(`/api/admin/ato-onboarding/${renewalId}`),
  },
}
