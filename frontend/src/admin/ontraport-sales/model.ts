import type { api } from '../../api/client'
import { sample } from '@/components/skeleton-samples'
import { sampleRows } from '@/components/skeletonizer'

export type SalesResponse = Awaited<ReturnType<typeof api.admin.ontraportSales>>
export type Sale = SalesResponse['items'][number]
export type Stats = SalesResponse['stats']
export type Facets = SalesResponse['facets']

/** A sale annotated with days until its due date (null when no due date is known). */
export type SaleRow = Sale & { _daysUntil: number | null }

const SAMPLE_STATUSES = ['WaitingForRenewalWindow', 'RenewalCompleted', 'RenewalFailed', 'AsicNotYetDue']
const sampleSale = (i: number): Sale => ({
  id: sample.id(i), contactName: sample.name(i), email: sample.email(i), abn: sample.abn(i), businessName: sample.business(i),
  renewalYears: i % 3 === 0 ? 3 : 1, amountPaid: sample.money(i), status: SAMPLE_STATUSES[i % SAMPLE_STATUSES.length],
  syncedAt: sample.daysAgo(i), renewalDueDate: sample.daysAhead(3 + i * 4), errorMessage: null, renewalRequestId: null,
  renewalStatus: null, renewalFailedAtStep: null, renewalErrorMessage: null, clientId: null, handledAt: null, orderId: null,
})
// withDaysUntil is a function declaration further down, so it's hoisted and callable here.
export const SKELETON_ROWS = sampleRows<SaleRow>(8, (i) => withDaysUntil(sampleSale(i)))
export const SKELETON_RESPONSE: SalesResponse = {
  filteredCount: 0, page: 1, pageSize: 0, view: 'all',
  buckets: { overdue: 12, overdueValue: 2480, soon7: 38, soon30: 214, all: 6120 },
  upNext: sampleRows(5, sampleSale), totalCount: 0, waitingCount: 186, queuedCount: 24, completedCount: 0, failedCount: 14,
  ineligibleCount: 3, asicNotYetDueCount: 5, renewalInProgressCount: 2, blockedCount: 10, items: [], facets: { status: [] },
  stats: { pipelineValueNext30d: 31240, lastSyncAt: sample.daysAgo(0), nextSyncAt: sample.daysAhead(0), today: 42, yesterday: 51, deltaPct: null, daily14d: [] },
}

export type DueFilter = 'overdue' | '7d' | '30d' | 'all'

export const PAGE_SIZE = 25

export function withDaysUntil(s: Sale): SaleRow {
  return { ...s, _daysUntil: s.renewalDueDate ? Math.floor((new Date(s.renewalDueDate).getTime() - Date.now()) / 86400000) : null }
}

// Statuses that aren't real failures — they'll retry automatically (AsicNotYetDue, RenewalInProgress)
// or have been deliberately skipped (IneligibleForRenewal). Render with a yellow/info treatment, not red.
export const blockedStatuses = ['IneligibleForRenewal', 'AsicNotYetDue', 'RenewalInProgress']

/** Operator-friendly names for the raw OntraportSaleStatus enum values. */
export function statusLabel(status: string): string {
  switch (status) {
    case 'RenewalFailed':           return 'Failed'
    case 'WaitingForRenewalWindow': return 'Waiting'
    case 'AsicNotYetDue':           return 'ASIC not due'
    case 'RenewalInProgress':       return 'In progress'
    case 'IneligibleForRenewal':    return 'Ineligible'
    case 'RenewalCompleted':        return 'Completed'
    case 'Synced':                  return 'Synced'
    case 'RenewalQueued':           return 'Queued'
    case 'NotDueForRenewal':        return 'Not due'
    default:                        return status
  }
}

export function emptyMessage(f: DueFilter, hasFilters: boolean) {
  if (hasFilters) {
    return f === 'all'
      ? 'Nothing matches these filters'
      : 'Nothing due in this window matches. Try All.'
  }
  switch (f) {
    case 'overdue': return 'Nothing overdue. All on track'
    case '7d':      return 'Nothing due in the next 7 days'
    case '30d':     return 'Nothing due in the next 30 days'
    case 'all':     return 'No Ontraport sales yet'
  }
}

export function defaultStats(): Stats {
  return {
    pipelineValueNext30d: 0, lastSyncAt: null, nextSyncAt: null,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [],
  }
}

export function prettyStatus(status: string): string {
  switch (status) {
    case 'IneligibleForRenewal': return 'Ineligible'
    case 'AsicNotYetDue':        return 'ASIC not yet due'
    case 'RenewalInProgress':    return 'Renewal in progress'
    default:                     return status
  }
}

export function retryBanner(status: string): string {
  switch (status) {
    case 'AsicNotYetDue':
      return 'ASIC says this business name is not due for renewal yet. The next 07:00 process run will retry — no action needed.'
    case 'RenewalInProgress':
      return 'ASIC reports an existing renewal session for this ABN. Will be retried on the next 07:00 run once the prior session clears.'
    case 'IneligibleForRenewal':
      return 'Skipped — the customer paid a cancellation fee (below the renewal price), filed a dispute/refund, or the contact is flagged for cancellation in Ontraport. Will not be retried automatically.'
    default:
      return ''
  }
}

export function retryHint(status: string): string {
  switch (status) {
    case 'AsicNotYetDue':     return 'Retries at 07:00'
    case 'RenewalInProgress': return 'Retries at 07:00'
    case 'IneligibleForRenewal': return 'Skipped, won’t retry'
    default:                  return ''
  }
}
