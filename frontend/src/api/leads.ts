import { apiFetch, type Facet } from './http'

// Leads (wizard visitors) and win-back emails.
export const leadsAdminApi = {
  leads: (params: { outcome?: string; reminder?: string; search?: string; dateFrom?: string; dateTo?: string; hasRenewal?: string; take?: number; page?: number } = {}) => {
    const qs = new URLSearchParams()
    if (params.page) qs.set('page', String(params.page))
    if (params.outcome) qs.set('outcome', params.outcome)
    if (params.reminder) qs.set('reminder', params.reminder)
    if (params.search) qs.set('search', params.search)
    if (params.dateFrom) qs.set('dateFrom', params.dateFrom)
    if (params.dateTo) qs.set('dateTo', params.dateTo)
    if (params.hasRenewal) qs.set('hasRenewal', params.hasRenewal)
    if (params.take) qs.set('take', String(params.take))
    const suffix = qs.toString() ? `?${qs}` : ''
    return apiFetch<{
      totalCount: number
      items: Array<{
        id: string
        abn: string
        fullName: string
        email: string
        mobileNumber: string
        createdAt: string
        outcome: string
        progress?: string | null
        reminderOptIn: boolean
        convertedToRenewal: boolean
        convertedAt?: string | null
        searchLog?: { id: string; searchedAt: string; success: boolean; resultsCount: number } | null
        firstBusinessName?: string | null
        renewal?: { id: string; status: string; amount: number } | null
      }>
      facets: {
        outcome: Facet
        reminder: Facet
      }
      stats: {
        totalAllTime: number
        total30d: number
        converted30d: number
        conversionRate30d: number
        avgHoursToConvert: number | null
        today: number
        yesterday: number
        deltaPct: number | null
        daily14d: Array<{ date: string; count: number }>
        outcomeBreakdown: Record<string, number>
        winBackEligible: number
        winBackRecoverableValue: number
        avgBasket: number
      }
    }>(`/api/admin/leads${suffix}`)
  },
  winBackPreview: (body: { outcome?: string; reminderOptIn?: boolean; search?: string; createdFrom?: string; createdTo?: string }) =>
    apiFetch<{
      recipientCount: number
      recoverableValue: number
      sampleNames: string[]
      subject: string
      bodyPreview: string
      avgBasket: number
    }>('/api/admin/win-back/preview', { method: 'POST', body: JSON.stringify(body) }),
  winBackSend: (body: { outcome?: string; reminderOptIn?: boolean; search?: string; createdFrom?: string; createdTo?: string }) =>
    apiFetch<{ enqueued: number; batchId: string }>('/api/admin/win-back/send', { method: 'POST', body: JSON.stringify(body) }),
  updateWinBack: (body: { subject: string; bodyPlain: string; bodyHtml: string }) =>
    apiFetch<void>('/api/admin/settings/win-back', { method: 'PUT', body: JSON.stringify(body) }),
  lead: (id: string) => apiFetch<{
    id: string
    client: { id: string; fullName: string } | null
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
    /** How far they got: Details, NamesShown, PaymentPage, Paid. Apart from what ASIC said (outcome). */
    progress: 'Details' | 'NamesShown' | 'PaymentPage' | 'Paid'
    namesShownAt?: string | null
    reachedPaymentAt?: string | null
    paymentFollowUpSentAt?: string | null
    reminderOptIn: boolean
    convertedToRenewal: boolean
    convertedAt?: string | null
    searchLog?: {
      id: string
      searchedAt: string
      success: boolean
      errorMessage?: string | null
      resultsCount: number
      results: Array<{ id: string; businessName: string; accountNumber: string; registrationDate: string; isAvailable: boolean }>
    } | null
    renewalRequests: Array<{ id: string; status: string; amount: number; renewalYears: number; initiatedAt: string; businessName?: string | null; orderId?: string | null }>
  }>(`/api/admin/leads/${id}`),
}
