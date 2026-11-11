import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { loadStripe, type Stripe } from '@stripe/stripe-js'
import { CardElement, Elements, useElements, useStripe } from '@stripe/react-stripe-js'
import { registrationsApi, type PublicRegistration } from '../api/registrations'
import { Shell } from './RegisterPage'

const formatAbn = (abn: string) =>
  abn.length === 11 ? `${abn.slice(0, 2)} ${abn.slice(2, 5)} ${abn.slice(5, 8)} ${abn.slice(8)}` : abn

export default function RegisterPayPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [reg, setReg] = useState<PublicRegistration | null>(null)
  const [liveKey, setLiveKey] = useState<string | null>(null)
  const [loadError, setLoadError] = useState('')

  useEffect(() => {
    if (!id) return
    registrationsApi.get(id)
      .then((r) => {
        if (r.paid) navigate(`/register/${r.id}/done`, { replace: true })
        else setReg(r)
      })
      .catch(() => setLoadError('We couldn’t find this application.'))
    registrationsApi.config().then((c) => setLiveKey(c.stripePublishableKey || null)).catch(() => {})
  }, [id, navigate])

  // A test customer pays with Stripe's test keys; Elements mounts once the application is loaded.
  const key = reg ? (reg.isTest ? reg.testStripePublishableKey : liveKey) : null
  const stripePromise = useMemo<Promise<Stripe | null> | null>(() => (key ? loadStripe(key) : null), [key])

  return (
    <Shell step={3}>
      <div className="mx-auto max-w-2xl text-center">
        <h1 className="text-2xl font-bold tracking-tight text-gray-900 sm:text-3xl">Payment</h1>
        <p className="mt-3 text-base leading-7 text-gray-600">We lodge your application with ASIC as soon as it’s paid.</p>
      </div>

      <div className="mx-auto mt-8 max-w-md space-y-6">
        {loadError ? <p className="rounded-md bg-red-50 p-4 text-sm text-red-800">{loadError}</p> : null}
        {reg ? (
          <div className="rounded-xl bg-white p-5 shadow-sm ring-1 ring-gray-200">
            <dl className="space-y-2 text-sm">
              <Row label="Business name" value={reg.businessName} />
              <Row label="ABN" value={formatAbn(reg.abn)} />
              <Row label="Period" value={`${reg.years} year${reg.years === 1 ? '' : 's'}`} />
              <div className="border-t border-gray-100 pt-2">
                <Row label="Total (incl. ASIC fee)" value={`$${reg.amount.toFixed(2)}`} strong />
              </div>
            </dl>
            {reg.isTest ? <p className="mt-3 rounded bg-amber-50 p-2 text-xs text-amber-800">Test customer: Stripe test card, and nothing is lodged with ASIC.</p> : null}
          </div>
        ) : null}

        {reg && stripePromise ? (
          <div className="rounded-xl bg-white p-5 shadow-sm ring-1 ring-gray-200">
            <Elements stripe={stripePromise}>
              <PayForm reg={reg} onPaid={() => navigate(`/register/${reg.id}/done`)} />
            </Elements>
          </div>
        ) : null}

        <button type="button" onClick={() => navigate('/register')} className="w-full text-center text-sm text-gray-500 hover:text-gray-700">
          ← Change the application
        </button>
      </div>
    </Shell>
  )
}

function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-gray-500">{label}</dt>
      <dd className={`text-right text-gray-900 ${strong ? 'font-semibold' : ''}`}>{value}</dd>
    </div>
  )
}

function PayForm({ reg, onPaid }: { reg: PublicRegistration; onPaid: () => void }) {
  const stripe = useStripe()
  const elements = useElements()
  const [name, setName] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState('')

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!stripe || !elements) return
    setError('')
    setBusy(true)
    try {
      const card = elements.getElement(CardElement)
      if (!card) throw new Error('Card element not ready.')
      const pm = await stripe.createPaymentMethod({ type: 'card', card, billing_details: { name } })
      if (pm.error) throw new Error(pm.error.message ?? 'Card error.')

      setStatus('Processing payment…')
      const result = await registrationsApi.pay(reg.id, pm.paymentMethod.id)
      if (result.requiresAction && result.clientSecret) {
        setStatus('Verifying your card with your bank…')
        const next = await stripe.handleNextAction({ clientSecret: result.clientSecret })
        if (next.error) throw new Error(next.error.message ?? 'Card authentication failed.')
        if (next.paymentIntent?.status !== 'succeeded')
          throw new Error('Card authentication wasn’t completed. You haven’t been charged — please try again.')
        // Charged now. Completing is safe to repeat, so retry a hiccup instead of asking them to pay again.
        let done = false
        let lastError: unknown = null
        for (let attempt = 0; attempt < 3 && !done; attempt++) {
          try {
            await registrationsApi.completePayment(reg.id, next.paymentIntent.id)
            done = true
          } catch (err) {
            lastError = err
            await new Promise((r) => setTimeout(r, 1500))
          }
        }
        if (!done) {
          const reason = lastError instanceof Error ? ` (${lastError.message})` : ''
          throw new Error(`Your payment went through, but we couldn’t finish your application${reason}. ` +
            `Please don’t pay again — contact us and quote ${next.paymentIntent.id}.`)
        }
      }
      onPaid()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong.')
      setBusy(false)
      setStatus('')
    }
  }

  return (
    <form onSubmit={submit}>
      <label className="mb-1 block text-sm font-medium text-gray-700">Card</label>
      <div className="mb-4 rounded-md bg-white px-3 py-3 shadow-sm ring-1 ring-inset ring-gray-300">
        <CardElement options={{ hidePostalCode: true, style: { base: { fontSize: '16px', color: '#1f2937', fontFamily: 'Geist, system-ui, sans-serif' } } }} />
      </div>
      <label htmlFor="cardName" className="block text-sm font-medium text-gray-700">Name on card</label>
      <input id="cardName" value={name} onChange={(e) => setName(e.target.value)} required data-clarity-mask="true"
             className="mb-5 mt-1 block w-full rounded-md border-0 px-3 py-2.5 text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 focus:ring-2 focus:ring-inset focus:ring-brand sm:text-sm" />
      {error ? <p className="mb-4 rounded-md bg-red-50 p-3 text-sm text-red-800">{error}</p> : null}
      <button type="submit" disabled={!stripe || !name || busy}
              className="w-full rounded-md bg-brand px-4 py-3 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark disabled:cursor-not-allowed disabled:opacity-50">
        {busy ? status || 'Processing…' : `Pay $${reg.amount.toFixed(2)}`}
      </button>
      <p className="mt-3 text-center text-xs text-gray-500">Secure payment via Stripe</p>
    </form>
  )
}
