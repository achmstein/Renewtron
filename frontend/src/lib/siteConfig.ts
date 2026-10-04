/**
 * Public runtime config, fetched once per page load. Tag ids live in admin settings
 * rather than in the bundle so David can swap a pixel without a redeploy.
 */

export interface SiteConfig {
  stripePublishableKey: string
  pricing: { oneYearFee: number; threeYearFee: number }
  tracking: {
    gtmContainerId: string
    ga4MeasurementId: string
    metaPixelId: string
    clarityProjectId?: string
  }
  /** Business Portal root for "back to your portal" links; null when not configured. */
  portalUrl?: string | null
}

const EMPTY: SiteConfig = {
  stripePublishableKey: '',
  pricing: { oneYearFee: 0, threeYearFee: 0 },
  tracking: { gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '' },
}

let pending: Promise<SiteConfig> | null = null

export function loadSiteConfig(): Promise<SiteConfig> {
  pending ??= fetch('/api/site-config')
    .then((r) => (r.ok ? (r.json() as Promise<SiteConfig>) : EMPTY))
    .catch(() => EMPTY)
  return pending
}

function addScript(src: string) {
  const script = document.createElement('script')
  script.async = true
  script.src = src
  document.head.appendChild(script)
}

let installed = false

/** Admin and login pages show customer PII, so Clarity must never record them. */
export const isPrivatePath = (pathname: string) => pathname === '/login' || pathname.startsWith('/admin')

type Clarity = ((...args: unknown[]) => void) & { q?: unknown[] }
let clarityLoaded = false

/**
 * Pauses Clarity while on admin/login pages and resumes on the public ones. The SPA shares
 * one page load, so an admin who signs in after visiting the wizard would otherwise be recorded.
 */
export function syncClarityWithPath(pathname: string) {
  if (clarityLoaded) window.clarity?.(isPrivatePath(pathname) ? 'stop' : 'start')
}

/** Injects whichever marketing tags are configured. Safe to call more than once. */
export function installTrackingTags(config: SiteConfig) {
  if (installed) return
  installed = true

  const { gtmContainerId, ga4MeasurementId, metaPixelId, clarityProjectId } = config.tracking

  if (clarityProjectId && /^[a-z0-9]+$/i.test(clarityProjectId) && !isPrivatePath(window.location.pathname)) {
    // Clarity's own stub: queue calls until the tag loads.
    const clarity: Clarity = function (...args: unknown[]) {
      (clarity.q = clarity.q ?? []).push(args)
    }
    window.clarity = window.clarity ?? clarity
    addScript(`https://www.clarity.ms/tag/${clarityProjectId}`)
    clarityLoaded = true
  }

  if (gtmContainerId) {
    window.dataLayer = window.dataLayer ?? []
    window.dataLayer.push({ 'gtm.start': Date.now(), event: 'gtm.js' })
    addScript(`https://www.googletagmanager.com/gtm.js?id=${encodeURIComponent(gtmContainerId)}`)
  }

  if (ga4MeasurementId) {
    window.dataLayer = window.dataLayer ?? []
    window.gtag = function gtag(...args: unknown[]) {
      window.dataLayer!.push(args)
    }
    window.gtag('js', new Date())
    window.gtag('config', ga4MeasurementId)
    addScript(`https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(ga4MeasurementId)}`)
  }

  if (metaPixelId) {
    // Meta's own stub: queue calls until fbevents.js loads and swaps in callMethod.
    interface Fbq {
      (...args: unknown[]): void
      callMethod?: (...args: unknown[]) => void
      queue: unknown[]
      push: unknown
      loaded: boolean
      version: string
    }
    const fbq = function (...args: unknown[]) {
      if (fbq.callMethod) fbq.callMethod(...args)
      else fbq.queue.push(args)
    } as Fbq
    fbq.queue = []
    fbq.push = fbq
    fbq.loaded = true
    fbq.version = '2.0'

    window.fbq = fbq
    addScript('https://connect.facebook.net/en_US/fbevents.js')
    fbq('init', metaPixelId)
    fbq('track', 'PageView')
  }
}
