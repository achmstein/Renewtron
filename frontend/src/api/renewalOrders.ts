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
  /** source: comma-separated Ontraport, Wizard, Manual. */
  renewalOrders: (params: { view?: string; search?: string; source?: string; page?: number; pageSize?: number } = {}) => {
    const qs = new URLSearchParams(Object.entries(params).filter(([, v]) => v != null && v !== '').map(([k, v]) => [k, String(v)]))
    return apiFetch<{
      total: number; page: number; pageSize: number; items: RenewalOrderRow[]
      byStage: Array<{ stage: string; count: number; amount: number }>
      /** Stopped for a person, by why. */
      byReason: Array<{ reason: string; count: number; amount: number }>
      /** Where they paid, counted for this view and search. */
      bySource: Array<{ value: RenewalOrderRow['source']; count: number }>
    }>(`/api/admin/renewal-orders?${qs}`)
  },
  /** A due date a person found, for a sale that came without one: it goes back in the queue. */
  setRenewalOrderDueDate: (id: string, dueDate: string) =>
    apiFetch<{ message: string }>(`/api/admin/renewal-orders/${id}/due-date`, { method: 'POST', body: JSON.stringify({ dueDate }) }),
  /** One action on many renewals; the ones it can't apply to come back with why. */
  bulkRenewalOrders: (body: { ids: string[]; action: 'retry' | 'check' | 'outcome' | 'reopen'; outcome?: string; note?: string; reference?: string }) =>
    apiFetch<{ message: string; done: number; skipped: Array<{ id: string; businessName: string; why: string }> }>(
      '/api/admin/renewal-orders/bulk', { method: 'POST', body: JSON.stringify(body) }),
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
  /** The payments behind a paid renewal and which can be refunded (nothing is refunded by this). */
  renewalOrderRefundOptions: (id: string) => apiFetch<{
    payments: Array<{ key: string; label: string; paidAt: string | null; amount: number; refundable: boolean; why: string | null }>
    note: string | null
  }>(`/api/admin/renewal-orders/${id}/refund`),
  /** Refunds the picked payment to the customer's card, through Ontraport or Stripe. Can't be undone. */
  refundRenewalOrder: (id: string, body: { payment: string; note?: string }) =>
    apiFetch<{ message: string; reference: string }>(`/api/admin/renewal-orders/${id}/refund`, { method: 'POST', body: JSON.stringify(body) }),
  reopenRenewalOrder: (id: string) => apiFetch<{ message: string }>(`/api/admin/renewal-orders/${id}/reopen`, { method: 'POST' }),
  fixRenewalOrderName: (id: string, businessName: string) =>
    apiFetch<{ message: string }>(`/api/admin/renewal-orders/${id}/fix-name`, { method: 'POST', body: JSON.stringify({ businessName }) }),
}
