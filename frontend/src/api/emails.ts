import { apiFetch } from './http'

// The Emails log.
/** One email Renewtron sent or tried to (the admin's Emails page). */
export interface EmailRow {
  id: string
  kind: string
  subject: string
  to: string
  status: 'Queued' | 'Sent' | 'Failed'
  /** What SendGrid said happened at the inbox: delivered, deferred, bounce, dropped, spamreport. */
  deliveryStatus: string | null
  openedAt: string | null
  error: string | null
  createdAt: string
  sentAt: string | null
  attemptCount: number
  clientId: string | null
}

export interface EmailListResponse {
  total: number
  page: number
  pageSize: number
  counts: Record<'all' | 'problem' | 'sent' | 'delivered' | 'opened', number>
  kinds: string[]
  items: EmailRow[]
  /** Email isn't going out (SendGrid refusing our key, or every recent send failing). */
  problem: string | null
}

export interface EmailDetail extends Omit<EmailRow, 'clientId'> {
  lastAttemptAt: string | null
  providerMessageId: string | null
  deliveryAt: string | null
  deliveryDetail: string | null
  bodyHtml: string | null
  bodyText: string | null
  leadId: string | null
  renewalRequestId: string | null
  batchId: string | null
  resendOfId: string | null
  client: { id: string; fullName: string } | null
  resends: Array<{ id: string; status: string; createdAt: string }>
  /** The template it was written from, and its version (0 = the default wording). */
  templateKey: string | null
  templateVersion: number | null
  templateName: string | null
}

export const emailsAdminApi = {
  emails: (params: { tab?: string; kind?: string; search?: string; clientId?: string; page?: number; pageSize?: number } = {}) =>
    apiFetch<EmailListResponse>(`/api/admin/emails/?${new URLSearchParams(Object.entries(params).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => [k, String(v)]))}`),
  email: (id: string) => apiFetch<EmailDetail>(`/api/admin/emails/${id}`),
  resendEmail: (id: string) => apiFetch<{ id: string; message: string }>(`/api/admin/emails/${id}/resend`, { method: 'POST' }),
}
