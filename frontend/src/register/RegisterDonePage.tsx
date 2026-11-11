import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { registrationsApi, type PublicRegistration } from '../api/registrations'
import { Shell } from './fields'
import { clearDraft, DONE_STEP } from './registerForm'

/** After payment: follows the application while it's lodged (the ASIC job takes a minute or two). */
export default function RegisterDonePage() {
  const { id } = useParams<{ id: string }>()
  const [reg, setReg] = useState<PublicRegistration | null>(null)
  const [error, setError] = useState('')

  // Paid: the next name registered in this tab starts from a blank form.
  useEffect(() => clearDraft(), [])

  useEffect(() => {
    if (!id) return
    let stop = false
    let timer: number | undefined
    const load = async () => {
      try {
        const r = await registrationsApi.get(id)
        if (stop) return
        setReg(r)
        if (r.stage === 'lodging' || r.stage === 'unpaid') timer = window.setTimeout(load, 5000)
      } catch {
        if (!stop) setError('We couldn’t find this application.')
      }
    }
    void load()
    return () => { stop = true; window.clearTimeout(timer) }
  }, [id])

  return (
    <Shell step={DONE_STEP}>
      <div className="mx-auto max-w-md">
        {error ? <p className="rounded-md bg-red-50 p-4 text-sm text-red-800">{error}</p> : null}
        {reg ? <Status reg={reg} /> : null}
      </div>
    </Shell>
  )
}

function Status({ reg }: { reg: PublicRegistration }) {
  if (reg.stage === 'unpaid') {
    return (
      <Card title="Not paid yet" tone="neutral">
        <p>This application hasn’t been paid for.</p>
        <Link to={`/register/${reg.id}/pay`} className="mt-4 inline-block font-semibold text-brand">Go to payment →</Link>
      </Card>
    )
  }
  if (reg.stage === 'refused') {
    return (
      <Card title="ASIC couldn’t accept this name" tone="bad">
        <p>We tried to lodge <strong>{reg.businessName}</strong>, but ASIC wouldn’t accept it. Nothing was lodged, and we’ll refund your payment in full.</p>
        <p className="mt-2">We’ve emailed {reg.email} with the details.</p>
        <Link to="/register" className="mt-4 inline-block font-semibold text-brand">Try a different name →</Link>
      </Card>
    )
  }
  if (reg.stage === 'lodged') {
    return (
      <Card title="Lodged with ASIC" tone="good">
        <p>We’ve lodged your application to register <strong>{reg.businessName}</strong>.</p>
        {reg.asicReference ? <p className="mt-2">ASIC reference: <strong>{reg.asicReference}</strong></p> : null}
        <p className="mt-2">ASIC will email the record of registration and the name’s ASIC key to {reg.email}.</p>
      </Card>
    )
  }
  return (
    <Card title="Payment received" tone="good">
      <p>Thank you, {reg.firstName}. We’re lodging <strong>{reg.businessName}</strong> with ASIC now.</p>
      <div className="mt-4 flex items-center gap-3 text-gray-600">
        <svg className="h-5 w-5 animate-spin text-brand" fill="none" viewBox="0 0 24 24">
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
        </svg>
        <span>This usually takes a few minutes. You can close this page; we’ll email {reg.email}.</span>
      </div>
    </Card>
  )
}

function Card({ title, tone, children }: { title: string; tone: 'good' | 'bad' | 'neutral'; children: React.ReactNode }) {
  const ring = tone === 'good' ? 'ring-green-200' : tone === 'bad' ? 'ring-red-200' : 'ring-gray-200'
  return (
    <div className={`rounded-xl bg-white p-6 text-sm text-gray-700 shadow-sm ring-1 ${ring}`}>
      <h1 className="mb-3 text-xl font-bold text-gray-900">{title}</h1>
      {children}
    </div>
  )
}
