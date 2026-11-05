import type { ReactNode } from 'react'
import { ClientName, DueIn, SentenceRow, When } from './_rows'
import { HANDLED, renewalStatus, statusOf } from './_status'
import { fmtMoney2, formatAbn } from './_utils'
import { Card } from '@/components/ui/card'

/*
 * The record lists' rows written as sentences: who, what happened, and the facts that matter
 * (when, how much, when it's due). The table view stays one click away for digging.
 */

const truncate = (s: string, n: number) => (s.length > n ? s.slice(0, n - 1) + '…' : s)
const B = ({ children }: { children: ReactNode }) => <span className="font-medium">{children}</span>

/** A list of sentence rows in the admin's card frame, or the empty state. */
export function SentenceList({ children, empty }: { children: ReactNode[]; empty: ReactNode }) {
  if (children.length === 0) return <>{empty}</>
  return <Card className="gap-0 overflow-hidden py-0">{children}</Card>
}

const ERROR_WORDS: Record<string, string> = {
  Transient: 'network or ASIC error',
  NotDueYet: 'ASIC says not due yet',
  AlreadyInProgress: 'ASIC has a renewal open',
  PaymentRisk: 'stopped at payment: check ASIC',
  Terminal: 'refused by ASIC',
}

export function RenewalSentence({ r, actions }: {
  r: {
    id: string; businessName: string; status: string; amount: number; renewalYears: number; initiatedAt: string
    completedAt?: string | null; errorCategory?: string | null; nextRetryAt?: string | null; errorMessage?: string | null
    email?: string | null; bpayReference?: string | null; bpayPaidAt?: string | null; source: string
    lead?: { fullName: string } | null
  }
  actions?: ReactNode
}) {
  const s = renewalStatus(r)
  const who = r.lead?.fullName || r.email || 'Someone'
  const years = `${r.renewalYears} year${r.renewalYears === 1 ? '' : 's'}`
  const bpayDue = r.bpayReference && !r.bpayPaidAt
  const what = r.status === 'Completed'
    ? <>{who} renewed <B>{r.businessName}</B> for {years}{bpayDue ? '; ASIC’s BPAY bill still to pay' : ''}</>
    : r.status === 'Failed'
      ? <>{who}: renewal of <B>{r.businessName}</B> {s.meaning === 'waiting' ? `failed, ${s.label.toLowerCase()}` : 'failed'}</>
      : <>{who}: renewal of <B>{r.businessName}</B> is {s.label.toLowerCase()}</>
  const extra = r.status === 'Failed'
    ? (r.nextRetryAt ? <> · retries <DueIn at={r.nextRetryAt} /></> : r.errorCategory ? ` · ${ERROR_WORDS[r.errorCategory] ?? r.errorCategory}` : r.errorMessage ? ` · ${truncate(r.errorMessage, 80)}` : '')
    : null
  return (
    <SentenceRow
      meaning={bpayDue ? 'action' : s.meaning}
      to={`/admin/renewals/${r.id}`}
      what={what}
      why={<><When at={r.completedAt ?? r.initiatedAt} /> · {fmtMoney2(r.amount)} · {r.source === 'Renewtron' ? 'direct' : r.source === 'BulkUpload' ? 'bulk upload' : r.source}{extra}</>}
      actions={actions}
    />
  )
}

export function SaleSentence({ s }: {
  s: {
    id: string; contactName: string; email: string; businessName: string; amountPaid: number; renewalYears: number
    status: string; syncedAt: string; renewalDueDate?: string | null; errorMessage?: string | null; clientId?: string | null
    renewalStatus?: string | null; handledAt?: string | null
  }
}) {
  const st = s.handledAt && s.status !== 'RenewalCompleted' ? HANDLED
    : s.renewalStatus && s.status !== 'IneligibleForRenewal' ? statusOf('renewal', s.renewalStatus) : statusOf('sale', s.status)
  return (
    <SentenceRow
      meaning={st.meaning}
      to={`/admin/ontraport-sales/${s.id}`}
      what={<><ClientName id={s.clientId} name={s.contactName || s.email} /> paid {fmtMoney2(s.amountPaid)} for <B>{s.businessName}</B>: {st.label.toLowerCase()}</>}
      why={<>{s.renewalDueDate ? <>due <DueIn at={s.renewalDueDate} /> · </> : 'no due date · '}synced <When at={s.syncedAt} /> · {s.renewalYears} year{s.renewalYears === 1 ? '' : 's'}{st.meaning === 'action' && s.errorMessage ? ` · ${truncate(s.errorMessage, 90)}` : ''}</>}
    />
  )
}

export function LeadSentence({ l }: {
  l: {
    id: string; abn: string; fullName: string; email: string; createdAt: string; outcome: string; reminderOptIn: boolean
    firstBusinessName?: string | null; renewal?: { status: string; amount: number } | null
  }
}) {
  const st = l.renewal ? statusOf('renewal', l.renewal.status) : statusOf('lead', l.outcome)
  const name = l.firstBusinessName ? <B>{l.firstBusinessName}</B> : <>ABN {formatAbn(l.abn)}</>
  const what = l.renewal
    ? <>{l.fullName} paid {fmtMoney2(l.renewal.amount)} to renew {name}: {st.label.toLowerCase()}</>
    : l.outcome === 'RenewalAvailable'
      ? <>{l.fullName} found {name} due and didn’t pay</>
      : <>{l.fullName} looked up {name}: {st.label.toLowerCase()}</>
  return (
    <SentenceRow
      meaning={st.meaning}
      to={`/admin/leads/${l.id}`}
      what={what}
      why={<><When at={l.createdAt} /> · {l.email}{l.reminderOptIn ? ' · asked for reminders' : ''}</>}
    />
  )
}

const BY: Record<string, string> = { Customer: 'A visitor', Admin: 'Staff', System: 'Renewtron' }

export function SearchSentence({ s }: {
  s: {
    id: string; abn: string; searchedAt: string; success: boolean; resultsCount: number; initiatedBy: string
    errorMessage?: string | null; repeatCount7d: number
    lead?: { fullName: string; outcome: string } | null; renewal?: { status: string; amount: number } | null
  }
}) {
  const by = s.lead?.fullName ?? BY[s.initiatedBy] ?? s.initiatedBy
  const meaning = s.renewal ? statusOf('renewal', s.renewal.status).meaning : s.success ? (s.lead ? statusOf('lead', s.lead.outcome).meaning : 'quiet') : 'quiet'
  return (
    <SentenceRow
      meaning={meaning}
      to={`/admin/searches/${s.id}`}
      what={s.success
        ? <>{by} looked up ABN {formatAbn(s.abn)}: {s.resultsCount} name{s.resultsCount === 1 ? '' : 's'}{s.renewal ? `, renewal ${statusOf('renewal', s.renewal.status).label.toLowerCase()}` : s.lead ? '' : ', left without details'}</>
        : <>{by} looked up ABN {formatAbn(s.abn)}; ASIC didn’t answer</>}
      why={<><When at={s.searchedAt} />{s.repeatCount7d >= 3 ? ` · searched ${s.repeatCount7d}× this week` : ''}{!s.success && s.errorMessage ? ` · ${truncate(s.errorMessage, 80)}` : ''}</>}
    />
  )
}
