import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import GridBackground from '../components/GridBackground'
import WizardProgress from '../components/WizardProgress'
import { createLead } from '../lib/leads'
import { capturePrefill, isAdult, parsePrefill } from '../lib/prefill'
import { FunnelStep, trackStep } from '../lib/tracking'

const steps = [
  { label: 'ABN' }, { label: 'Details' }, { label: 'Check' }, { label: 'Select' }, { label: 'Pay' },
]

function formatAbn(abn: string) {
  return abn.length === 11 ? `${abn.slice(0, 2)} ${abn.slice(2, 5)} ${abn.slice(5, 8)} ${abn.slice(8)}` : abn
}

export default function StartPage() {
  const navigate = useNavigate()
  const { search } = useLocation()
  const [abn, setAbn] = useState('')
  const [error, setError] = useState('')
  const [prefilled, setPrefilled] = useState(false)
  const [skipping, setSkipping] = useState(false)
  const skipStarted = useRef(false)

  useEffect(() => {
    const prefill = capturePrefill(search)
    trackStep(FunnelStep.AbnViewed, { abn: prefill.abn || undefined })
    if (!prefill.abn) return
    setAbn(formatAbn(prefill.abn))
    setPrefilled(true)

    // From the portal's Renew button we already know everything the forms ask: go straight
    // to the check (and from there to the card). Only for a link carrying it — not when the
    // customer comes back here — and anything missing or failing shows the normal form.
    const linked = parsePrefill(search)
    const complete = linked.source.toLowerCase() === 'portal'
      && prefill.fullName.trim().length >= 2 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(prefill.email)
      && prefill.mobile.length >= 10 && isAdult(prefill.dob)
    if (!complete || skipStarted.current) return
    skipStarted.current = true
    setSkipping(true)
    void api.prefetchSearch(prefill.abn).catch(() => {})
    createLead({
      abn: prefill.abn, fullName: prefill.fullName, email: prefill.email,
      mobileNumber: prefill.mobile, dateOfBirth: prefill.dob,
    }, prefill)
      .then((leadId) => {
        trackStep(FunnelStep.DetailsSubmitted, { abn: prefill.abn, leadId })
        navigate(`/checking/${leadId}`, { replace: true })
      })
      .catch(() => setSkipping(false))
  }, [search, navigate])

  const isValid = () => {
    const clean = abn.replace(/\s+/g, '')
    return clean.length === 11 && /^\d+$/.test(clean)
  }

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    if (!isValid()) {
      setError('Please enter a valid 11-digit ABN')
      return
    }
    const clean = abn.replace(/\s+/g, '')
    trackStep(FunnelStep.AbnSubmitted, { abn: clean })
    // ASIC takes ~20s; start it now so it's done by the time the details are filled in.
    void api.prefetchSearch(clean).catch(() => {})
    navigate(`/details?abn=${clean}`)
  }

  return (
    <div className="relative isolate overflow-auto flex-1">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
        <WizardProgress currentStep={1} steps={steps} allowNavigation={false} />

        <div className="mx-auto max-w-2xl text-center">
          <h1 className="text-3xl font-bold tracking-tight text-gray-900 sm:text-4xl">Renew your business name</h1>
          <p className="mt-4 text-lg leading-8 text-gray-600">
            Enter your ABN to get started with your business name renewal.
          </p>
        </div>

        {skipping ? (
          <div className="mx-auto mt-12 max-w-md text-center">
            <svg className="mx-auto h-10 w-10 animate-spin text-brand" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
            <p className="mt-4 text-sm text-gray-600">Getting your renewal ready…</p>
          </div>
        ) : (
        <div className="mx-auto mt-12 max-w-md">
          <form onSubmit={submit}>
            <div className="space-y-6">
              <div>
                <label htmlFor="abn" className="block text-sm font-medium leading-6 text-gray-900">
                  Australian Business Number (ABN)
                </label>
                <div className="mt-2">
                  <input
                    type="text"
                    id="abn"
                    value={abn}
                    onChange={(e) => setAbn(e.target.value)}
                    placeholder="12 345 678 901"
                    maxLength={14}
                    className="block w-full rounded-md border-0 py-3.5 text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 placeholder:text-gray-400 focus:ring-2 focus:ring-inset focus-ring-brand sm:text-lg px-4 text-center tracking-wider"
                    required
                  />
                </div>
                {error ? <p className="mt-2 text-sm text-red-600">{error}</p> : null}
                {prefilled && !error ? (
                  <p className="mt-2 text-sm text-brand">We've filled in the ABN from your renewal notice — check it's right and continue.</p>
                ) : (
                  <p className="mt-2 text-sm text-gray-500">Your 11-digit Australian Business Number</p>
                )}
              </div>

              <button
                type="submit"
                disabled={!isValid()}
                className="btn-primary flex w-full justify-center rounded-md px-4 py-3 text-sm font-semibold shadow-sm transition-colors"
              >
                Continue
                <svg className="ml-2 h-5 w-5" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
                </svg>
              </button>
            </div>
          </form>
        </div>
        )}
      </div>
    </div>
  )
}
