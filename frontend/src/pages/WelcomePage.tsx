import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import GridBackground from '../components/GridBackground'

/*
 * After paying on an Ontraport order form, the form sends the customer here
 * (/welcome?cid=[Contact ID]). We ask the server every few seconds until Ontraport has recorded
 * the payment, then send them straight into the Business Portal, signed in. If that can't happen
 * (the link was already used, or it took too long), they get the portal's sign-in page instead.
 */

const ASK_EVERY_MS = 3000
const GIVE_UP_AFTER_MS = 60_000

/** Ontraport's merge field may arrive under any of these names. */
function contactIdFrom(params: URLSearchParams): string {
  for (const key of ['cid', 'contact_id', 'contactid', 'contactId', 'id']) {
    const value = (params.get(key) ?? '').trim()
    if (/^\d{1,12}$/.test(value)) return value
  }
  return ''
}

type View = { kind: 'working' } | { kind: 'signIn'; loginUrl: string | null }

export default function WelcomePage() {
  const [params] = useSearchParams()
  const contactId = contactIdFrom(params)
  const [view, setView] = useState<View>(contactId ? { kind: 'working' } : { kind: 'signIn', loginUrl: null })

  useEffect(() => {
    if (!contactId) return
    let stop = false
    let timer: number | undefined
    let loginUrl: string | null = null
    const startedAt = Date.now()
    const ask = async () => {
      try {
        const r = await api.welcome(contactId)
        if (stop) return
        if (r.status === 'ready') { window.location.replace(r.url); return }
        loginUrl = r.loginUrl ?? loginUrl
        if (r.status === 'signIn') { setView({ kind: 'signIn', loginUrl }); return }
      } catch {
        // A hiccup (or too many asks): keep trying until the time is up.
      }
      if (stop) return
      if (Date.now() - startedAt >= GIVE_UP_AFTER_MS) setView({ kind: 'signIn', loginUrl })
      else timer = window.setTimeout(ask, ASK_EVERY_MS)
    }
    void ask()
    return () => { stop = true; window.clearTimeout(timer) }
  }, [contactId])

  return (
    <div className="relative isolate flex-1 overflow-auto">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-16 sm:pb-32 lg:px-8">
        <div className="mx-auto max-w-md rounded-xl bg-white p-8 text-center shadow-sm ring-1 ring-gray-200">
          <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-green-100">
            <svg className="h-6 w-6 text-green-600" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor" aria-hidden>
              <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
            </svg>
          </div>
          <h1 className="mt-4 text-2xl font-bold tracking-tight text-gray-900">Thank you for your payment</h1>

          {view.kind === 'working' ? (
            <>
              <p className="mt-3 text-base text-gray-600">We’re setting up your account. This only takes a moment.</p>
              <svg className="mx-auto mt-6 h-8 w-8 animate-spin text-brand" fill="none" viewBox="0 0 24 24" aria-label="Setting up your account">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              <p className="mt-4 text-sm text-gray-500">Please don’t close this page.</p>
            </>
          ) : (
            <>
              <p className="mt-3 text-base text-gray-600">
                Your order is in. You can follow your business name and renewal in your portal.
              </p>
              {view.loginUrl ? (
                <a href={view.loginUrl} className="btn-primary mt-6 flex w-full justify-center rounded-md px-4 py-3 text-sm font-semibold shadow-sm">
                  Sign in to your portal
                </a>
              ) : null}
              <p className="mt-4 text-sm text-gray-500">Sign in with the email address you paid with.</p>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
