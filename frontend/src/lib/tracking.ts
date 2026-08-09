/**
 * Funnel tracking.
 *
 * Every step reports twice: to our own /api/track (the numbers we trust — ad blockers
 * can't touch them) and to whichever marketing tags are loaded. Keep the step names in
 * sync with FunnelSteps in Renewtron.Server/Data/FunnelEvent.cs.
 */

import { getPrefill } from './prefill'

export const FunnelStep = {
  AbnViewed: 'abn_viewed',
  AbnSubmitted: 'abn_submitted',
  DetailsViewed: 'details_viewed',
  DetailsSubmitted: 'details_submitted',
  CheckStarted: 'check_started',
  CheckAvailable: 'check_available',
  CheckUnavailable: 'check_unavailable',
  CheckFailed: 'check_failed',
  SelectViewed: 'select_viewed',
  SelectSubmitted: 'select_submitted',
  PaymentViewed: 'payment_viewed',
  PaymentSubmitted: 'payment_submitted',
  PaymentFailed: 'payment_failed',
  RenewalComplete: 'renewal_complete',
} as const

export type FunnelStepName = (typeof FunnelStep)[keyof typeof FunnelStep]

export interface TrackProps {
  leadId?: string
  abn?: string
  /** Step-specific extra — the check outcome, a decline message, the item count. */
  detail?: string
  /** Order value, sent to the ad platforms as a conversion value. */
  value?: number
}

declare global {
  interface Window {
    dataLayer?: unknown[]
    gtag?: (...args: unknown[]) => void
    fbq?: (...args: unknown[]) => void
  }
}

const VISITOR_KEY = 'renewtron.vid'
const SESSION_KEY = 'renewtron.sid'

function randomId() {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID()
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}

function stableId(key: string, storage: () => Storage): string {
  try {
    const store = storage()
    const existing = store.getItem(key)
    if (existing) return existing
    const created = randomId()
    store.setItem(key, created)
    return created
  } catch {
    // Private mode / storage disabled — the visit still gets counted, it just can't
    // be joined to their next one.
    return randomId()
  }
}

/** Stable per browser, so repeat visits count as one person. */
export function getVisitorId() {
  return stableId(VISITOR_KEY, () => localStorage)
}

/** Per visit. */
export function getSessionId() {
  return stableId(SESSION_KEY, () => sessionStorage)
}

function postToServer(payload: Record<string, unknown>) {
  const body = JSON.stringify(payload)
  try {
    if (navigator.sendBeacon) {
      // sendBeacon survives the page unloading mid-navigation, which is exactly when
      // the interesting drop-offs happen.
      navigator.sendBeacon('/api/track', new Blob([body], { type: 'application/json' }))
      return
    }
  } catch {
    /* fall through to fetch */
  }
  void fetch('/api/track', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body,
    keepalive: true,
  }).catch(() => {})
}

function forwardToTags(step: FunnelStepName, props: TrackProps) {
  const params: Record<string, unknown> = { funnel_step: step }
  if (props.detail) params.detail = props.detail
  if (props.value !== undefined) {
    params.value = props.value
    params.currency = 'AUD'
  }

  try {
    window.dataLayer?.push({ event: `renewtron_${step}`, ...params })
  } catch { /* ignore */ }

  try {
    window.gtag?.('event', step, params)
  } catch { /* ignore */ }

  try {
    if (step === FunnelStep.RenewalComplete) {
      window.fbq?.('track', 'Purchase', { value: props.value ?? 0, currency: 'AUD' })
    } else if (step === FunnelStep.PaymentViewed) {
      window.fbq?.('track', 'InitiateCheckout', params)
    } else if (step === FunnelStep.DetailsSubmitted) {
      window.fbq?.('track', 'Lead', params)
    } else {
      window.fbq?.('trackCustom', step, params)
    }
  } catch { /* ignore */ }
}

export function trackStep(step: FunnelStepName, props: TrackProps = {}) {
  const prefill = getPrefill()

  postToServer({
    step,
    visitorId: getVisitorId(),
    sessionId: getSessionId(),
    leadId: props.leadId ?? null,
    abn: props.abn ?? prefill.abn ?? null,
    source: prefill.source || (prefill.contactId ? 'ontraport' : null),
    detail: props.detail ?? null,
    path: window.location.pathname,
    referrer: document.referrer || null,
  })

  forwardToTags(step, props)
}
