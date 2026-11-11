import { apiFetch } from './http'

// ASIC's BPAY bills and paying them through Airwallex.
export interface BpayBill {
  id: string
  businessName: string | null
  abn: string | null
  renewalYears: number
  lodgedAt: string
  transactionReference: string | null
  billerCode: string | null
  customerReference: string | null
  /** What was sent to Airwallex, or what would be sent now (ASIC's fee); null when the fee isn't set. */
  amount: number | null
  /** Can be ticked and paid now. */
  payable: boolean
  transferId: string | null
  /** Airwallex's status (PROCESSING, SENT, PAID, FAILED…), or REJECTED / UNKNOWN from Renewtron. */
  transferStatus: string | null
  error: string | null
  paidAt: string | null
}

export interface BpayPayOutcome { renewalId: string; sent: boolean; status: string | null; error: string | null }

export const bpayAdminApi = {
  bpayBills: (includePaid = false) => apiFetch<{
    items: BpayBill[]
    airwallexReady: boolean
    sandbox: boolean
    oneYearFee: number | null
    threeYearFee: number | null
  }>(`/api/admin/bpay${includePaid ? '?includePaid=true' : ''}`),
  payBpayBills: (renewalIds: string[], expectedTotal: number) =>
    apiFetch<{ outcomes: BpayPayOutcome[] }>('/api/admin/bpay/pay', { method: 'POST', body: JSON.stringify({ renewalIds, expectedTotal }) }),
  syncBpayPayments: () => apiFetch<{ changed: number }>('/api/admin/bpay/sync', { method: 'POST' }),
}
