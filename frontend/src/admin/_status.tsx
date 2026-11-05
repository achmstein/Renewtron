import { StatusPill, type Tone } from './_ui'

/**
 * The admin's one status vocabulary. Every page that shows the status of a renewal, sale, bulk
 * upload, key request, key letter, BPAY bill, wizard visit, email or business name takes its
 * words and colour from here, so the same state reads the same everywhere.
 *
 * The colour carries the meaning, not the record type:
 *   action  (red)    a person needs to do something
 *   waiting (amber)  waiting on someone else: ASIC, the client, a renewal window, a scheduled retry
 *   working (indigo) under way on our side; nobody needs to act
 *   done    (green)  finished
 *   quiet   (grey)   nothing to do, nothing pending
 */
export type Meaning = 'action' | 'waiting' | 'working' | 'done' | 'quiet'

export const MEANING_TONE: Record<Meaning, Tone> = {
  action: 'red',
  waiting: 'amber',
  working: 'indigo',
  done: 'emerald',
  quiet: 'zinc',
}

export const MEANING_WORDS: Record<Meaning, string> = {
  action: 'Needs action',
  waiting: 'Waiting',
  working: 'Under way',
  done: 'Done',
  quiet: 'Nothing to do',
}

export type StatusEntry = { label: string; meaning: Meaning; hint?: string }

export type StatusKind =
  | 'renewal' | 'sale' | 'bulk' | 'keyRequest' | 'keyLetter' | 'bpay'
  | 'lead' | 'email' | 'payment' | 'businessName'

const VOCABULARY: Record<StatusKind, Record<string, StatusEntry>> = {
  // RenewalStatus. A failed renewal is refined by renewalStatus() below.
  renewal: {
    Pending:    { label: 'Queued',            meaning: 'working', hint: 'Waiting for the renewal worker to pick it up.' },
    Processing: { label: 'Lodging',           meaning: 'working', hint: 'The renewal is going through ASIC now.' },
    Completed:  { label: 'Renewed',           meaning: 'done' },
    Failed:     { label: 'Failed',            meaning: 'action', hint: 'Nothing will retry it on its own.' },
  },
  // OntraportSaleStatus
  sale: {
    Synced:                  { label: 'New sale',          meaning: 'working', hint: 'Pulled from Ontraport; the next run starts the renewal.' },
    WaitingForRenewalWindow: { label: 'Waiting for window', meaning: 'waiting', hint: 'Renews automatically once the name is inside ASIC’s renewal window.' },
    RenewalQueued:           { label: 'Queued',            meaning: 'working' },
    RenewalCompleted:        { label: 'Renewed',           meaning: 'done' },
    RenewalFailed:           { label: 'Failed',            meaning: 'action', hint: 'Paid, not renewed.' },
    NotDueForRenewal:        { label: 'Not due',           meaning: 'quiet' },
    IneligibleForRenewal:    { label: 'Not eligible',      meaning: 'action', hint: 'Paid, but this name can’t be renewed with us. Decide: refund or contact the client.' },
    AsicNotYetDue:           { label: 'ASIC: not due yet', meaning: 'waiting', hint: 'ASIC said it isn’t due; the next run tries again.' },
    RenewalInProgress:       { label: 'Open at ASIC',      meaning: 'waiting', hint: 'ASIC has a renewal already open for this ABN; the next run tries again.' },
  },
  // BulkRenewalStatus
  bulk: {
    WaitingForRenewalWindow: { label: 'Waiting for window', meaning: 'waiting' },
    RenewalQueued:           { label: 'Queued',             meaning: 'working' },
    RenewalCompleted:        { label: 'Renewed',            meaning: 'done' },
    RenewalFailed:           { label: 'Failed',             meaning: 'action' },
    NotDueForRenewal:        { label: 'Not due',            meaning: 'quiet' },
    Skipped:                 { label: 'Skipped',            meaning: 'quiet' },
  },
  // AsicKeyRequestStatus
  keyRequest: {
    Pending:     { label: 'To send',       meaning: 'action',  hint: 'Send it from the key tool.' },
    Manual:      { label: 'With a person', meaning: 'working', hint: 'Someone is filling in ASIC’s form now.' },
    Submitted:   { label: 'Sent to ASIC',  meaning: 'waiting', hint: 'Waiting for ASIC to email the key.' },
    KeyReceived: { label: 'Key received',  meaning: 'done' },
    Failed:      { label: 'Not sent',      meaning: 'action',  hint: 'A person couldn’t send it; the note says why.' },
  },
  // AsicKeyNotificationStatus: the key letters ASIC emails us.
  keyLetter: {
    Pending:               { label: 'Reading',            meaning: 'working' },
    Completed:             { label: 'Saved to Ontraport', meaning: 'done' },
    SavedToClient:         { label: 'Saved to client',    meaning: 'done' },
    LinkNotFound:          { label: 'No link',            meaning: 'action', hint: 'The email had no download link.' },
    DownloadFailed:        { label: 'Download failed',    meaning: 'action', hint: 'Usually expired: ASIC keeps letters 30 days.' },
    KeyNotFound:           { label: 'Key not found',      meaning: 'action', hint: 'The PDF didn’t contain a key we could read.' },
    ContactNotFound:       { label: 'No client found',    meaning: 'action', hint: 'Choose the client this key belongs to.' },
    OntraportUpdateFailed: { label: 'Ontraport failed',   meaning: 'action', hint: 'Retry to write the key to Ontraport.' },
  },
  // Airwallex transfer status of an ASIC BPAY bill; TO_PAY is ours: no transfer yet.
  bpay: {
    TO_PAY:            { label: 'To pay',             meaning: 'action' },
    UNKNOWN:           { label: 'No answer: check',   meaning: 'action', hint: 'Airwallex didn’t confirm the transfer. Check it there before paying again.' },
    REJECTED:          { label: 'Refused',            meaning: 'action' },
    IN_APPROVAL:       { label: 'Awaiting approval',  meaning: 'waiting' },
    SCHEDULED:         { label: 'Scheduled',          meaning: 'working' },
    OVERDUE:           { label: 'Overdue',            meaning: 'action' },
    PROCESSING:        { label: 'Processing',         meaning: 'working' },
    SENT:              { label: 'Sent',               meaning: 'working' },
    PAID:              { label: 'Paid',               meaning: 'done' },
    FAILED:            { label: 'Failed',             meaning: 'action' },
    CANCELLED:         { label: 'Cancelled',          meaning: 'action' },
    APPROVAL_REJECTED: { label: 'Approval rejected',  meaning: 'action' },
    APPROVAL_RECALLED: { label: 'Approval recalled',  meaning: 'action' },
    APPROVAL_BLOCKED:  { label: 'Approval blocked',   meaning: 'action' },
  },
  // LeadOutcome: what the wizard visit's ASIC lookup found.
  lead: {
    Pending:           { label: 'Not checked',  meaning: 'quiet' },
    RenewalAvailable:  { label: 'Due, not paid', meaning: 'waiting', hint: 'A name is due; the client hasn’t paid yet.' },
    NotDueForRenewal:  { label: 'Not due',      meaning: 'quiet' },
    RenewalInProgress: { label: 'Paying',       meaning: 'working' },
    NoBusinessNames:   { label: 'No names',     meaning: 'quiet' },
    RenewalCompleted:  { label: 'Renewed',      meaning: 'done' },
  },
  // OutboundStatus
  email: {
    Queued: { label: 'Queued', meaning: 'working' },
    Sent:   { label: 'Sent',   meaning: 'done' },
    Failed: { label: 'Failed', meaning: 'action' },
  },
  // Stripe PaymentIntent status as stored on a payment.
  payment: {
    succeeded:               { label: 'Paid',          meaning: 'done' },
    processing:              { label: 'Processing',    meaning: 'working' },
    requires_payment_method: { label: 'Card declined', meaning: 'waiting', hint: 'Waiting for the client to try another card.' },
    requires_action:         { label: 'Needs 3-D Secure', meaning: 'waiting' },
    canceled:                { label: 'Cancelled',     meaning: 'quiet' },
    failed:                  { label: 'Card declined', meaning: 'waiting', hint: 'Waiting for the client to try another card.' },
  },
  // ClientBusinessNameStatus
  businessName: {
    Active:    { label: 'Active',    meaning: 'done' },
    Cancelled: { label: 'Cancelled', meaning: 'quiet' },
    Excluded:  { label: 'Excluded',  meaning: 'quiet', hint: 'Left out of reminders.' },
  },
}

/** "AwaitingPayment" → "Awaiting payment", for a status the vocabulary doesn't know yet. */
function words(status: string): string {
  const s = status.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
  return s.charAt(0).toUpperCase() + s.slice(1)
}

/** The words and meaning of a status. Unknown statuses show their own words, in grey. */
export function statusOf(kind: StatusKind, status: string | null | undefined): StatusEntry {
  if (!status) return { label: '—', meaning: 'quiet' }
  return VOCABULARY[kind][status] ?? { label: words(status), meaning: 'quiet' }
}

/**
 * A renewal's status, refined: a failure with a retry scheduled, or one ASIC turned away because
 * the name isn't in its renewal window yet, is waiting, not waiting on us.
 * Keep in step with RenewalQueues.NeedsPerson on the server.
 */
export function renewalStatus(r: { status: string; errorCategory?: string | null; nextRetryAt?: string | null }): StatusEntry {
  if (r.status === 'Failed') {
    if (r.nextRetryAt) return { label: 'Retry scheduled', meaning: 'waiting', hint: 'Renewtron will try again on its own.' }
    if (r.errorCategory === 'NotDueYet') return { label: 'ASIC: not due yet', meaning: 'waiting', hint: 'Try again once the name is inside ASIC’s renewal window.' }
  }
  return statusOf('renewal', r.status)
}

/** The badge for a status, in the shared words and colours; the hint shows on hover. */
export function Status({ kind, status, entry, size = 'sm', className }: {
  kind?: StatusKind
  status?: string | null
  /** A pre-resolved entry, e.g. from renewalStatus(); wins over kind + status. */
  entry?: StatusEntry
  /** lg: the headline status at the top of a record page. */
  size?: 'sm' | 'lg'
  className?: string
}) {
  const e = entry ?? (kind ? statusOf(kind, status) : { label: status ?? '—', meaning: 'quiet' as Meaning })
  return (
    <span title={e.hint} className={className}>
      <StatusPill tone={MEANING_TONE[e.meaning]} className={size === 'lg' ? 'h-7 px-3 text-sm' : undefined}>{e.label}</StatusPill>
    </span>
  )
}

/** Facet options for a filter: the raw value with its shared label. */
export function statusLabel(kind: StatusKind, status: string): string {
  return statusOf(kind, status).label
}
