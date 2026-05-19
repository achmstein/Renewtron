import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { loadStripe, type Stripe } from '@stripe/stripe-js'
import { Elements, CardElement, useElements, useStripe } from '@stripe/react-stripe-js'
import { api, type LeadDto, type PricingResponse } from '../api/client'
import GridBackground from '../components/GridBackground'
import UserDetailsSummary from '../components/UserDetailsSummary'
import WizardProgress from '../components/WizardProgress'

const steps = [
  { label: 'ABN' }, { label: 'Details' }, { label: 'Check' }, { label: 'Select' }, { label: 'Pay' },
]

export default function PaymentPage() {
  const { leadId } = useParams<{ leadId: string }>()
  const [params] = useSearchParams()
  const ids = (params.get('ids') ?? '').split(',').filter(Boolean)
  const years = (Number(params.get('years')) || 1) as 1 | 3
  const navigate = useNavigate()

  const [lead, setLead] = useState<LeadDto | null>(null)
  const [pricing, setPricing] = useState<PricingResponse | null>(null)
  const [stripeKey, setStripeKey] = useState<string | null>(null)

  useEffect(() => {
    if (!leadId) return
    void api.getLead(leadId).then(setLead).catch(() => navigate('/'))
    void api.pricing().then(setPricing).catch(() => {})
    void api.admin.settings()
      .then((s) => setStripeKey(s.stripe.publishableKey))
      .catch(() => {
        // Public Stripe key isn't admin-only in practice; fall back to env override
        const k = (import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined) ?? null
        setStripeKey(k)
      })
  }, [leadId, navigate])

  const stripePromise = useMemo<Promise<Stripe | null> | null>(
    () => (stripeKey ? loadStripe(stripeKey) : null),
    [stripeKey],
  )

  const selectedNames = (lead?.businessNames ?? []).filter((b) => ids.includes(b.id))
  const pricePerItem = pricing ? (years === 1 ? pricing.oneYearFee : pricing.threeYearFee) : 0
  const total = pricePerItem * selectedNames.length

  if (!leadId || !lead) {
    return (
      <div className="relative isolate overflow-auto flex-1">
        <GridBackground />
        <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
          <div className="text-center py-12">
            <svg className="mx-auto h-12 w-12 animate-spin text-brand" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
            <p className="mt-4 text-sm text-gray-600">Loading...</p>
          </div>
        </div>
      </div>
    )
  }

  if (selectedNames.length === 0) {
    return (
      <div className="relative isolate overflow-auto flex-1">
        <GridBackground />
        <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
          <div className="text-center py-12">
            <h3 className="text-lg font-semibold text-gray-900">No business names selected</h3>
            <p className="mt-2 text-sm text-gray-600">Please go back and select the business names you want to renew.</p>
            <a href="/" className="mt-4 inline-block text-brand hover:text-brand-dark">Start over</a>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="relative isolate overflow-auto flex-1">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
        <WizardProgress
          currentStep={5}
          steps={steps}
          onStepClick={(s) => {
            if (s === 1) navigate('/')
            else if (s === 2) navigate(`/details?abn=${lead.abn}`)
            else if (s === 4) navigate(`/select-names/${leadId}`)
          }}
        />

        <div className="mx-auto max-w-2xl">
          <UserDetailsSummary abn={lead.abn} fullName={lead.fullName} email={lead.email} mobileNumber={lead.mobileNumber} dateOfBirth={lead.dateOfBirth} />
        </div>

        <div className="mx-auto max-w-2xl">
          <div className="text-center mb-8">
            <h1 className="text-2xl font-bold tracking-tight text-gray-900 sm:text-3xl">Complete your payment</h1>
            <p className="mt-2 text-sm text-gray-600">Secure payment powered by Stripe</p>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-5 gap-6">
            <div className="lg:col-span-2 order-2 lg:order-1">
              <div className="rounded-lg bg-gray-50 border border-gray-200 p-5 sticky top-6">
                <h3 className="text-sm font-semibold text-gray-900 mb-4">Order Summary</h3>
                <div className="space-y-3 mb-4">
                  {selectedNames.map((s) => (
                    <div key={s.id} className="flex justify-between items-start text-sm">
                      <div className="flex-1 min-w-0 pr-2">
                        <p className="font-medium text-gray-900 truncate">{s.businessName}</p>
                        <p className="text-xs text-gray-500">{years} year{years > 1 ? 's' : ''}</p>
                      </div>
                      <span className="text-gray-700 flex-shrink-0">${pricePerItem.toFixed(2)}</span>
                    </div>
                  ))}
                </div>
                <div className="border-t border-gray-200 pt-4 space-y-2">
                  <div className="flex justify-between text-sm text-gray-600">
                    <span>Subtotal ({selectedNames.length} item{selectedNames.length > 1 ? 's' : ''})</span>
                    <span>${total.toFixed(2)}</span>
                  </div>
                  <div className="flex justify-between font-semibold text-gray-900">
                    <span>Total</span>
                    <span>${total.toFixed(2)} AUD</span>
                  </div>
                </div>
              </div>
            </div>

            <div className="lg:col-span-3 order-1 lg:order-2">
              <div className="rounded-lg bg-white shadow ring-1 ring-gray-200 p-6">
                {stripePromise ? (
                  <Elements stripe={stripePromise}>
                    <PaymentForm
                      leadId={leadId}
                      ids={ids}
                      years={years}
                      total={total}
                      cardholderDefault={lead.fullName}
                      onComplete={(renewalIds) => navigate(`/confirmation/${leadId}?ids=${renewalIds.join(',')}`)}
                    />
                  </Elements>
                ) : (
                  <div className="rounded-md bg-amber-50 border border-amber-200 p-4 text-sm text-amber-800">
                    Stripe publishable key isn't configured. Set it under Admin → Settings → Stripe.
                  </div>
                )}
              </div>

              <div className="text-center mt-4">
                <button type="button" onClick={() => navigate(`/select-names/${leadId}`)} className="text-sm font-medium text-gray-600 hover:text-gray-500">
                  <svg className="inline-block mr-1 h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M10.5 19.5L3 12m0 0l7.5-7.5M3 12h18" />
                  </svg>
                  Back to selection
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

interface FormProps {
  leadId: string
  ids: string[]
  years: 1 | 3
  total: number
  cardholderDefault: string
  onComplete: (renewalIds: string[]) => void
}

function PaymentForm({ leadId, ids, years, total, cardholderDefault, onComplete }: FormProps) {
  const stripe = useStripe()
  const elements = useElements()
  const [cardHolder, setCardHolder] = useState(cardholderDefault)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [processing, setProcessing] = useState(false)
  const [processingStatus, setProcessingStatus] = useState('Processing payment...')

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!stripe || !elements) return
    setError('')
    setSubmitting(true)

    try {
      const card = elements.getElement(CardElement)
      if (!card) throw new Error('Card element not ready.')

      const pmResult = await stripe.createPaymentMethod({
        type: 'card',
        card,
        billing_details: { name: cardHolder },
      })
      if (pmResult.error) throw new Error(pmResult.error.message ?? 'Card error.')

      setProcessing(true)
      setProcessingStatus('Creating renewal requests...')
      const result = await api.createBatchRenewal({
        leadId,
        searchResultIds: ids,
        renewalYears: years,
        paymentMethodId: pmResult.paymentMethod.id,
        cardholderName: cardHolder,
      })

      setProcessingStatus('Scheduling renewal processing...')
      onComplete(result.renewalIds)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred.')
      setProcessing(false)
      setSubmitting(false)
    }
  }

  if (processing) {
    return (
      <div className="mx-auto max-w-sm pt-2">
        <div className="text-center">
          <div className="mx-auto mb-8 flex h-16 w-16 items-center justify-center">
            <svg className="h-16 w-16 animate-spin text-brand" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
          </div>
          <h2 className="text-lg font-semibold text-gray-900">{processingStatus}</h2>
          <p className="mt-2 text-sm text-gray-600">Please do not close this page.</p>
        </div>
      </div>
    )
  }

  return (
    <form onSubmit={submit}>
      <div className="mb-4">
        <label className="block text-sm font-medium text-gray-700 mb-1">Card Number</label>
        <div className="mt-1 block w-full rounded-md border-0 py-3 px-3 shadow-sm ring-1 ring-inset ring-gray-300 bg-white">
          <CardElement options={{ hidePostalCode: true, style: { base: { fontSize: '16px', color: '#1f2937', fontFamily: 'Inter, system-ui, sans-serif' } } }} />
        </div>
      </div>

      <div className="mb-6">
        <label htmlFor="cardHolder" className="block text-sm font-medium text-gray-700">Name on Card</label>
        <input
          id="cardHolder"
          type="text"
          value={cardHolder}
          onChange={(e) => setCardHolder(e.target.value)}
          required
          placeholder="Name as it appears on card"
          className="mt-1 block w-full rounded-md border-0 py-2.5 px-3 text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 placeholder:text-gray-400 focus:ring-2 focus:ring-inset focus:ring-brand sm:text-sm"
        />
      </div>

      {error ? (
        <div className="mb-4 rounded-md bg-red-50 p-4">
          <div className="flex">
            <svg className="h-5 w-5 text-red-400" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z" />
            </svg>
            <div className="ml-3"><p className="text-sm text-red-800">{error}</p></div>
          </div>
        </div>
      ) : null}

      <button
        type="submit"
        disabled={!stripe || !cardHolder || submitting}
        className="w-full flex justify-center items-center rounded-md bg-brand px-4 py-3 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
      >
        <svg className="mr-2 h-5 w-5" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z" />
        </svg>
        <span>Pay ${total.toFixed(2)}</span>
      </button>

      <div className="mt-4 flex items-center justify-center gap-2 text-xs text-gray-500">
        <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75m-3-7.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285z" />
        </svg>
        <span>Secure payment via Stripe</span>
      </div>
    </form>
  )
}
