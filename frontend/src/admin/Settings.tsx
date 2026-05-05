import { useEffect, useState } from 'react'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { Banner, Section } from './_shared'

type Tab = 'SendGrid' | 'Stripe' | 'Pricing' | 'Asic' | 'Ontraport'

const tabs: { key: Tab; label: string; kicker: string }[] = [
  { key: 'SendGrid',  label: 'SendGrid',  kicker: '01' },
  { key: 'Stripe',    label: 'Stripe',    kicker: '02' },
  { key: 'Pricing',   label: 'Pricing',   kicker: '03' },
  { key: 'Asic',      label: 'ASIC',      kicker: '04' },
  { key: 'Ontraport', label: 'Ontraport', kicker: '05' },
]

type Settings = Awaited<ReturnType<typeof api.admin.settings>>

export default function Settings() {
  const [activeTab, setActiveTab] = useState<Tab>('SendGrid')
  const [data, setData] = useState<Settings | null>(null)
  const [successMessage, setSuccessMessage] = useState('')
  const [errorMessage, setErrorMessage] = useState('')

  const [sg, setSg] = useState({ apiKey: '', fromEmail: '', fromName: '' })
  const [stripe, setStripe] = useState({ secretKey: '', publishableKey: '' })
  const [pricing, setPricing] = useState({ oneYearFee: 0, threeYearFee: 0 })
  const [asic, setAsic] = useState({ forceFallback: false, email: '', cardNumber: '', cardholderName: '', expiryMonth: '', expiryYear: '', cvc: '' })
  const [ontraport, setOntraport] = useState({ apiAppId: '', apiKey: '', conversationId: '' })

  const load = async () => {
    const r = await api.admin.settings()
    setData(r)
    setSg(r.sendGrid)
    setStripe(r.stripe)
    setPricing(r.pricing)
    setAsic(r.asic)
    setOntraport(r.ontraport)
  }
  useEffect(() => { void load() }, [])

  const flash = (label: string, action: () => Promise<void>) => async () => {
    setSuccessMessage(''); setErrorMessage('')
    try {
      await action()
      setSuccessMessage(`${label} settings saved.`)
    } catch (e) {
      setErrorMessage(`Error saving ${label}: ${e instanceof Error ? e.message : 'unknown'}`)
    }
  }

  const onSendGrid = (e: React.FormEvent) => { e.preventDefault(); void flash('SendGrid', () => api.admin.updateSendGrid(sg))() }
  const onStripe = (e: React.FormEvent) => { e.preventDefault(); void flash('Stripe', () => api.admin.updateStripe(stripe))() }
  const onPricing = (e: React.FormEvent) => { e.preventDefault(); void flash('Pricing', () => api.admin.updatePricing(pricing))() }
  const onAsic = (e: React.FormEvent) => { e.preventDefault(); void flash('ASIC', () => api.admin.updateAsic(asic))() }
  const onOntraport = (e: React.FormEvent) => { e.preventDefault(); void flash('Ontraport', () => api.admin.updateOntraport(ontraport))() }

  return (
    <AdminPage title="Settings" subtitle="Configure integrations, pricing, and credentials." classification="Configuration">
      <div className="space-y-6">
        {successMessage ? <Banner kind="ok" message={successMessage} /> : null}
        {errorMessage ? <Banner kind="fail" message={errorMessage} /> : null}

        {/* Bureau-style tabs */}
        <div className="border-b-2 border-[var(--ink)]">
          <nav className="flex flex-wrap gap-x-7">
            {tabs.map((t) => {
              const active = activeTab === t.key
              return (
                <button
                  key={t.key}
                  onClick={() => setActiveTab(t.key)}
                  className="bureau-nav-link"
                  data-active={active}
                >
                  <span className="bureau-nav-num">{t.kicker}</span>
                  <span>{t.label}</span>
                </button>
              )
            })}
          </nav>
        </div>

        {!data ? null : (
          <>
            {activeTab === 'SendGrid' ? (
              <Section kicker="01" title="SendGrid Email">
                <form onSubmit={onSendGrid} className="space-y-4">
                  <Field label="API Key">
                    <input type="password" className="bureau-input" value={sg.apiKey} onChange={(e) => setSg({ ...sg, apiKey: e.target.value })} />
                  </Field>
                  <Field label="From Email">
                    <input type="email" className="bureau-input" value={sg.fromEmail} onChange={(e) => setSg({ ...sg, fromEmail: e.target.value })} />
                  </Field>
                  <Field label="From Name">
                    <input className="bureau-input" value={sg.fromName} onChange={(e) => setSg({ ...sg, fromName: e.target.value })} />
                  </Field>
                  <SubmitRow label="Save SendGrid" />
                </form>
              </Section>
            ) : null}

            {activeTab === 'Stripe' ? (
              <Section kicker="02" title="Stripe Payments">
                <form onSubmit={onStripe} className="space-y-4">
                  <Field label="Secret Key">
                    <input type="password" className="bureau-input" value={stripe.secretKey} onChange={(e) => setStripe({ ...stripe, secretKey: e.target.value })} />
                  </Field>
                  <Field label="Publishable Key">
                    <input className="bureau-input" value={stripe.publishableKey} onChange={(e) => setStripe({ ...stripe, publishableKey: e.target.value })} />
                  </Field>
                  <SubmitRow label="Save Stripe" />
                </form>
              </Section>
            ) : null}

            {activeTab === 'Pricing' ? (
              <Section kicker="03" title="Renewal Pricing">
                <p className="bureau-meta mb-5" style={{ textTransform: 'none', fontSize: 12 }}>
                  Prices charged to customers for business name renewals.
                </p>
                <form onSubmit={onPricing} className="space-y-4">
                  <Field label="1 Year Renewal · AUD" hint="Total price charged for a 1 year renewal.">
                    <PriceInput value={pricing.oneYearFee} onChange={(v) => setPricing({ ...pricing, oneYearFee: v })} />
                  </Field>
                  <Field label="3 Year Renewal · AUD" hint="Total price charged for a 3 year renewal.">
                    <PriceInput value={pricing.threeYearFee} onChange={(v) => setPricing({ ...pricing, threeYearFee: v })} />
                  </Field>
                  <SubmitRow label="Save Pricing" />
                </form>
              </Section>
            ) : null}

            {activeTab === 'Asic' ? (
              <Section kicker="04" title="ASIC Settings">
                <form onSubmit={onAsic} className="space-y-4">
                  <Field label="Email" hint="Used for ASIC renewal applications.">
                    <input type="email" className="bureau-input" value={asic.email} onChange={(e) => setAsic({ ...asic, email: e.target.value })} />
                  </Field>
                  <Field label="Card Number">
                    <input className="bureau-input bureau-mono" value={asic.cardNumber} onChange={(e) => setAsic({ ...asic, cardNumber: e.target.value })} />
                  </Field>
                  <Field label="Cardholder Name">
                    <input className="bureau-input" value={asic.cardholderName} onChange={(e) => setAsic({ ...asic, cardholderName: e.target.value })} />
                  </Field>
                  <div className="grid grid-cols-2 gap-4">
                    <Field label="Expiry Month">
                      <input className="bureau-input bureau-mono" value={asic.expiryMonth} onChange={(e) => setAsic({ ...asic, expiryMonth: e.target.value })} placeholder="MM" />
                    </Field>
                    <Field label="Expiry Year">
                      <input className="bureau-input bureau-mono" value={asic.expiryYear} onChange={(e) => setAsic({ ...asic, expiryYear: e.target.value })} placeholder="YYYY" />
                    </Field>
                  </div>
                  <Field label="CVC">
                    <input type="password" className="bureau-input bureau-mono" value={asic.cvc} onChange={(e) => setAsic({ ...asic, cvc: e.target.value })} />
                  </Field>
                  <SubmitRow label="Save ASIC" />
                </form>
              </Section>
            ) : null}

            {activeTab === 'Ontraport' ? (
              <Section kicker="05" title="Ontraport Integration">
                <form onSubmit={onOntraport} className="space-y-4">
                  <Field label="API App ID">
                    <input className="bureau-input bureau-mono" value={ontraport.apiAppId} onChange={(e) => setOntraport({ ...ontraport, apiAppId: e.target.value })} />
                  </Field>
                  <Field label="API Key">
                    <input type="password" className="bureau-input bureau-mono" value={ontraport.apiKey} onChange={(e) => setOntraport({ ...ontraport, apiKey: e.target.value })} />
                  </Field>
                  <Field label="Conversation ID" hint="Ontraport conversation ID for retrieving SMS messages.">
                    <input className="bureau-input bureau-mono" value={ontraport.conversationId} onChange={(e) => setOntraport({ ...ontraport, conversationId: e.target.value })} />
                  </Field>
                  <SubmitRow label="Save Ontraport" />
                </form>
              </Section>
            ) : null}
          </>
        )}
      </div>
    </AdminPage>
  )
}

function Field({ label, children, hint }: { label: string; children: React.ReactNode; hint?: string }) {
  return (
    <div>
      <label className="bureau-label mb-1.5 block">{label}</label>
      {children}
      {hint ? <p className="bureau-meta mt-1.5" style={{ textTransform: 'none', fontSize: 11 }}>{hint}</p> : null}
    </div>
  )
}

function SubmitRow({ label }: { label: string }) {
  return (
    <div className="border-t border-[var(--hairline)] pt-4">
      <button type="submit" className="bureau-btn bureau-btn-primary">
        ✓ {label}
      </button>
    </div>
  )
}

function PriceInput({ value, onChange }: { value: number; onChange: (v: number) => void }) {
  return (
    <div className="relative">
      <span className="bureau-mono pointer-events-none absolute inset-y-0 left-3 flex items-center text-[13px]" style={{ color: 'var(--ledger)' }}>$</span>
      <input
        type="number"
        step="0.01"
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className="bureau-input pl-6"
      />
    </div>
  )
}
