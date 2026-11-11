import { apiFetch, type Facet } from './http'

// Stripe payments.
export const paymentsAdminApi = {
  payments: (params: { status?: string; cardBrand?: string; search?: string; dateFrom?: string; dateTo?: string; page?: number; pageSize?: number } = {}) => {
    const qs = new URLSearchParams()
    if (params.status) qs.set('status', params.status)
    if (params.cardBrand) qs.set('cardBrand', params.cardBrand)
    if (params.search) qs.set('search', params.search)
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
        kind: 'succeeded' | 'failed'
        when: string
        paymentIntentId?: string | null
        amount?: number | null
        abn?: string | null
        businessNames: string[]
        leadId?: string | null
        customerName?: string | null
        email?: string | null
        cardBrand?: string | null
        cardLast4?: string | null
        cardholderName?: string | null
        error?: string | null
        renewals: Array<{ id: string; status: string }>
      }>
      facets: {
        result: Facet
        cardBrand: Facet
      }
      stats: {
        succeededCount: number
        succeededValue: number
        failedCount: number
        succeeded30d: number
        succeededValue30d: number
        failed30d: number
      }
    }>(`/api/admin/payments${suffix}`)
  },
}
