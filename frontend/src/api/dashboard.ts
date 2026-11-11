import { apiFetch } from './http'

// Dashboard and funnel numbers.
export interface FunnelResponse {
  totalVisitors: number
  completedVisitors: number
  conversionPct: number
  steps: Array<{
    step: string
    label: string
    visitors: number
    events: number
    droppedFromPrevious: number
    stepConversionPct: number
    overallConversionPct: number
  }>
  exits: Array<{ step: string; label: string; visitors: number }>
  stoppedAt: Array<{ step: string; label: string; visitors: number; rank: number }>
  bySource: Array<{ source: string; visitors: number; completed: number; conversionPct: number }>
  /** One point per day of the selected range (max 90). */
  daily: Array<{ date: string; visitors: number; renewed: number }>
  /** The same headline numbers for the equally long window just before. */
  previous: { totalVisitors: number; completedVisitors: number; conversionPct: number }
}

export type ActivityKind = 'paid' | 'lead-warm'
export interface ActivityItem {
  kind: ActivityKind
  at: string
  label: string | null
  detail: string | null
  amount: number | null
  source: string | null
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
    avgBasket: number
    abandonedAtPaymentCount: number
    abandonedAtPaymentValue: number
    ontraportPipelineCount: number
    ontraportPipelineValue: number
    failedRenewalCount: number
    failedRenewalValue: number
    pastCustomersDueSoonCount: number
    pastCustomersDueSoonValue: number
  }
  activity: ActivityItem[]
  recentSearches: Array<{ id: string; abn: string; searchedAt: string; success: boolean; resultsCount: number }>
  recentRenewals: Array<{ id: string; businessName: string | null; initiatedAt: string; status: string }>
  health?: {
    /** Null when the OTP SMS can be read; otherwise why renewals are being held. */
    otpProblem: string | null
    otpHeldCount: number
    stuckCount: number
    oldestStuckAt: string | null
    outboxPending: number
    outboxDead: number
    queueDepth: number
    recurringJobs: Array<{ id: string; lastExecution: string | null; nextExecution: string | null }> | null
  }
}

export const dashboardAdminApi = {
  dashboard: () => apiFetch<DashboardResponse>('/api/admin/dashboard'),
  funnel: (params: { dateFrom?: string; dateTo?: string; source?: string } = {}) => {
    const qs = new URLSearchParams()
    if (params.dateFrom) qs.set('dateFrom', params.dateFrom)
    if (params.dateTo) qs.set('dateTo', params.dateTo)
    if (params.source) qs.set('source', params.source)
    const suffix = qs.toString() ? `?${qs}` : ''
    return apiFetch<FunnelResponse>(`/api/admin/funnel${suffix}`)
  },
}
