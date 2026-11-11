import { apiFetch } from './http'

// The Today queue and the sidebar badges.
/** One thing on the Today page; `reason` is the record's own status or error category. */
export interface TodayItem {
  kind: 'order' | 'renewal' | 'sale' | 'bpay' | 'keyRequests' | 'keyRequest' | 'keyLetter' | 'duplicate'
  id: string
  reason: string
  clientId: string | null
  clientName: string | null
  businessName: string | null
  abn: string | null
  amount: number | null
  years: number | null
  at: string
  dueAt: string | null
  detail: string | null
  failedAtStep: string | null
  canRetry: boolean
  count: number | null
  otherClientId: string | null
  otherClientName: string | null
}
export interface TodayGroup { key: 'retry' | 'check' | 'refund' | 'bpay' | 'keys' | 'decide'; count: number; total: number | null; items: TodayItem[] }

export interface TodayResponse { generatedAt: string; total: number; otpProblem: string | null; cardProblem?: string | null; emailProblem?: string | null; groups: TodayGroup[] }

/** Work waiting on staff, shown as sidebar badges. */
export interface SidebarCounts { today: number; renewalsToFix: number; bpayToPay: number; asicKeyRequests: number; keysToSort: number; clientDuplicates: number; registrations: number }

export const todayAdminApi = {
  sidebarCounts: () => apiFetch<SidebarCounts>('/api/admin/sidebar-counts'),
  /** Every queue that needs a person; with clientId, only that client's items, uncapped. */
  today: (clientId?: string) => apiFetch<TodayResponse>(`/api/admin/today${clientId ? `?clientId=${clientId}` : ''}`),
}
