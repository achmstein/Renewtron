import { apiFetch } from './http'

// Paid renewals (orders): one name, one term, one outcome.
/** One paid renewal (RenewalOrder): one name, one term, one status. */
export interface RenewalOrderRow {
  id: string
  source: 'Ontraport' | 'Wizard' | 'Manual'
  sourceId: string
  clientId: string | null
  payerName: string | null
  clientName: string | null
  businessName: string
  abn: string
  dueDate: string | null
  years: number
  amountPaid: number
  paidAt: string
  stage: 'Waiting' | 'Due' | 'Renewing' | 'Renewed' | 'NeedsPerson' | 'Closed'
  reason: string | null
  note: string | null
  nextActionAt: string | null
  stageChangedAt: string
  attemptCount: number
  currentAttemptId: string | null
  renewedAt: string | null
  canRetry: boolean
}
/** How a paid renewal ended (or why it stopped), when a person or Renewtron's ASIC check decided it. */
export interface RenewalOrderDecision {
  outcome: string | null
  at: string | null
  /** The admin's email, or "ASIC check". */
  by: string | null
  evidence: string | null
  evidenceSource: 'AsicRegister' | 'AsicRenewalPage' | 'Staff' | 'Ontraport' | null
  evidenceAt: string | null
  reference: string | null
  asicCheckedAt: string | null
}

export interface RenewalOrderAttempt {
  id: string; status: string; errorCategory: string | null; failedAtStep: string | null; errorMessage: string | null
  initiatedAt: string; lastAttemptAt: string | null; completedAt: string | null; nextRetryAt: string | null
  attemptCount: number; paysBy: 'Card' | 'BPAY'; bpayPaidAt: string | null; transactionReference: string | null; current: boolean
}

export const renewalOrdersAdminApi = {
  renewalOrders: (params: { view?: string; search?: string; page?: number; pageSize?: number } = {}) => {
    const qs = new URLSearchParams(Object.entries(params).filter(([, v]) => v != null && v !== '').map(([k, v]) => [k, String(v)]))
    return apiFetch<{ total: number; page: number; pageSize: number; items: RenewalOrderRow[]; byStage: Array<{ stage: string; count: number; amount: number }> }>(`/api/admin/renewal-orders?${qs}`)
  },
  renewalOrder: (id: string) => apiFetch<{
    order: RenewalOrderRow
    client: { id: string; fullName: string } | null
    source: { label: string; to: string }
    attempts: RenewalOrderAttempt[]
    decision: RenewalOrderDecision
    visit: { id: string; at: string } | null
    canFixName: boolean
  }>(`/api/admin/renewal-orders/${id}`),
  retryRenewalOrder: (id: string) => apiFetch<{ message: string }>(`/api/admin/renewal-orders/${id}/retry`, { method: 'POST' }),
  /** Asks ASIC about one paid renewal now (Renewtron's own check) and records what it says. */
  checkRenewalOrderAtAsic: (id: string) =>
    apiFetch<{ orderId: string; businessName: string; outcome: string | null; evidence: string }>(`/api/admin/renewal-orders/${id}/check-asic`, { method: 'POST' }),
  /** A person's decision on how a paid renewal ends, with what it rests on. */
  setRenewalOrderOutcome: (id: string, body: { outcome: string; note?: string; reference?: string }) =>
    apiFetch<{ message: string }>(`/api/admin/renewal-orders/${id}/outcome`, { method: 'POST', body: JSON.stringify(body) }),
  reopenRenewalOrder: (id: string) => apiFetch<{ message: string }>(`/api/admin/renewal-orders/${id}/reopen`, { method: 'POST' }),
  fixRenewalOrderName: (id: string, businessName: string) =>
    apiFetch<{ message: string }>(`/api/admin/renewal-orders/${id}/fix-name`, { method: 'POST', body: JSON.stringify({ businessName }) }),
}
