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

/** The Overview's charts (Sydney weeks starting Monday; test customers left out). */
export interface OverviewCharts {
  /** The last 26 weeks, by the week the client paid: by channel, and what became of that money. */
  weeks: Array<{
    week: string; direct: number; ontraport: number; manual: number
    renewed: number; underWay: number; needsPerson: number; closed: number; refunded: number
  }>
  /** The last 60 days at ASIC: lodged by card, lodged by BPAY, runs that ended failed. */
  asic: Array<{ date: string; card: number; bpay: number; failed: number }>
  /** Clients' names due in the next 12 weeks: already paid, or not yet. */
  ahead: Array<{ week: string; paid: number; notPaid: number }>
  headline: { paidThisMonth: number; paidLastMonthToDate: number; waitingCount: number; waitingAmount: number }
}

export const dashboardAdminApi = {
  dashboard: () => apiFetch<DashboardResponse>('/api/admin/dashboard'),
  overviewCharts: () => apiFetch<OverviewCharts>('/api/admin/overview/charts'),
  funnel: (params: { dateFrom?: string; dateTo?: string; source?: string } = {}) => {
    const qs = new URLSearchParams()
    if (params.dateFrom) qs.set('dateFrom', params.dateFrom)
    if (params.dateTo) qs.set('dateTo', params.dateTo)
    if (params.source) qs.set('source', params.source)
    const suffix = qs.toString() ? `?${qs}` : ''
    return apiFetch<FunnelResponse>(`/api/admin/funnel${suffix}`)
  },
}
