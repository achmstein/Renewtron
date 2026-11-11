import type { api } from '../../api/client'
import { sample } from '@/components/skeleton-samples'
import { sampleRows } from '@/components/skeletonizer'

export type RenewalsResponse = Awaited<ReturnType<typeof api.admin.renewals>>
export type Renewal = RenewalsResponse['items'][number]
export type Stats = RenewalsResponse['stats']
export type Facets = RenewalsResponse['facets']

export type ViewMode = 'Sentences' | 'Table' | 'Cards'

const SAMPLE_STATUSES: Renewal['status'][] = ['Completed', 'Failed', 'Completed', 'Processing']
export const SKELETON_ROWS = sampleRows<Renewal>(8, (i) => ({
  id: sample.id(i), abn: sample.abn(i), businessName: sample.business(i), renewalYears: i % 3 === 0 ? 3 : 1,
  amount: sample.money(i), status: SAMPLE_STATUSES[i % SAMPLE_STATUSES.length], source: 'Renewtron', paymentType: 'Stripe',
  initiatedAt: sample.daysAgo(i), completedAt: i % 2 === 0 ? sample.daysAgo(i) : null, email: sample.email(i),
  errorMessage: i % 4 === 1 ? sample.text(0) : null, errorCategory: i % 4 === 1 ? 'CardDeclined' : null, attemptCount: 1,
  initiatedByLabel: 'Customer', stripePaymentSucceeded: true, cardBrand: 'visa', cardLast4: '4242',
  lead: { id: sample.id(100 + i), fullName: sample.name(i), email: sample.email(i) }, timeInStatusHours: 2 + i,
}))
export const PAGE_SIZE = 10

export function canRetry(r: Renewal) {
  return r.status === 'Failed' && ((r.paymentType === 'Stripe' && r.stripePaymentSucceeded) || r.paymentType === 'External')
}

/** Operator-friendly names for RenewalErrorCategories values. */
export function categoryLabel(category: string): string {
  switch (category) {
    case 'Transient':         return 'Network/ASIC'
    case 'NotDueYet':         return 'Not due yet'
    case 'AlreadyInProgress': return 'In progress'
    case 'PaymentRisk':       return 'Verify payment'
    case 'CardDeclined':      return 'Card declined'
    case 'Terminal':          return 'Bad data'
    default:                  return category
  }
}

export const SKELETON_STATS: Stats = {
  successRate30d: 94, completed30d: 1204, decided30d: 1281, total30d: 1320, avgCompletionMinutes: 4,
  revenueMtd: 84210, liveValue: 2380, stuckValue: 598, stuckCount: 3, scheduledRetryValue: 897,
  needsReviewCount: 4, needsReviewValue: 1196, failedValue30d: 4120,
  today: 61, yesterday: 58, deltaPct: 5, daily14d: [], errorCategoryBreakdown: [],
}

export function defaultStats(): Stats {
  return {
    successRate30d: 0, completed30d: 0, decided30d: 0, total30d: 0, avgCompletionMinutes: null,
    revenueMtd: 0, liveValue: 0, stuckValue: 0, stuckCount: 0, scheduledRetryValue: 0,
    needsReviewCount: 0, needsReviewValue: 0, failedValue30d: 0,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [], errorCategoryBreakdown: [],
  }
}

export const SOURCE_WORDS: Record<string, string> = { Renewtron: 'Direct', Ontraport: 'Ontraport', BulkUpload: 'Bulk upload' }
