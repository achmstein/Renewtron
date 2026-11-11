import { apiFetch, type Facet } from './http'

// ASIC searches log.
export const searchesAdminApi = {
  searches: (params: { abn?: string; success?: string; initiatedBy?: string; dateFrom?: string; dateTo?: string; includeSystem?: boolean; hasLead?: boolean; page?: number; pageSize?: number } = {}) => {
    const qs = new URLSearchParams()
    if (params.abn) qs.set('abn', params.abn)
    if (params.success) qs.set('success', params.success)
    if (params.initiatedBy) qs.set('initiatedBy', params.initiatedBy)
    if (params.dateFrom) qs.set('dateFrom', params.dateFrom)
    if (params.dateTo) qs.set('dateTo', params.dateTo)
    if (params.includeSystem) qs.set('includeSystem', 'true')
    if (params.hasLead != null) qs.set('hasLead', String(params.hasLead))
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
        sessionId?: string | null
        searchedAt: string
        success: boolean
        errorMessage?: string | null
        resultsCount: number
        initiatedBy: string
        ipAddress?: string | null
        hasRenewal: boolean
        firstBusinessName?: string | null
        lead?: { id: string; fullName: string; email: string; outcome: string; converted: boolean } | null
        renewal?: { id: string; amount: number; status: string } | null
        funnel: { hasLead: boolean; anyPaid: boolean; anyInflight: boolean; anyFailed: boolean }
        repeatCount7d: number
      }>
      facets: {
        result: Facet
        initiatedBy: Facet
      }
      stats: {
        totalAllTime: number
        total30d: number
        success30d: number
        successRate30d: number
        conversions30d: number
        conversionRate30d: number
        today: number
        yesterday: number
        deltaPct: number | null
        daily14d: Array<{ date: string; count: number }>
      }
    }>(`/api/admin/searches${suffix}`)
  },
  search: (id: string) => apiFetch<{
    id: string
    client: { id: string; fullName: string } | null
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
    funnel: { hasLead: boolean; anyPaid: boolean; anyInflight: boolean; anyFailed: boolean }
  }>(`/api/admin/searches/${id}`),
}
