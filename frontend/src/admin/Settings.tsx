import { useEffect, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import { PageHeader, Toast, type Tone } from './_ui'

type SectionKey = 'sendgrid' | 'winback' | 'stripe' | 'pricing' | 'asic' | 'ontraport' | 'atoagent' | 'tracking'

const inputCls = 'mt-1 block w-full rounded-md border-zinc-300 shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 sm:text-sm px-3 py-2 border'
const labelCls = 'block text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1'
const submitBtnCls = 'inline-flex justify-center rounded-md bg-zinc-900 text-white px-3 py-2 text-sm font-medium shadow-sm hover:bg-zinc-800 disabled:opacity-50 disabled:cursor-not-allowed transition'

type Settings = Awaited<ReturnType<typeof api.admin.settings>>

type SectionDef = {
  key: SectionKey
  group: string
  title: string
  description: string
}

const SECTIONS: SectionDef[] = [
  { key: 'sendgrid',  group: 'EMAIL & COMMS', title: 'SendGrid',          description: 'Outbound transactional email.' },
  { key: 'winback',   group: 'EMAIL & COMMS', title: 'Win-back template', description: 'Subject + body for the lead win-back email.' },
  { key: 'stripe',    group: 'PAYMENTS',      title: 'Stripe',            description: 'Customer payment processing.' },
  { key: 'pricing',   group: 'PAYMENTS',      title: 'Pricing',           description: 'Customer-facing renewal prices.' },
  { key: 'asic',      group: 'INTEGRATIONS',  title: 'ASIC credentials',  description: 'Card details used at ASIC checkout.' },
  { key: 'ontraport', group: 'INTEGRATIONS',  title: 'Ontraport',         description: 'API credentials for sales sync + OTP SMS.' },
  { key: 'atoagent',  group: 'COMPLIANCE',    title: 'ATO agent',         description: 'Default tax agent for ATO onboarding.' },
  { key: 'tracking',  group: 'MARKETING',     title: 'Tracking tags',     description: 'GA4, GTM and Meta pixel ids.' },
]

export default function Settings() {
  const [activeKey, setActiveKey] = useState<SectionKey>('sendgrid')
  const [data, setData] = useState<Settings | null>(null)
  const [toast, setToast] = useState<{ tone: Tone; message: string } | null>(null)

  const showToast = (tone: Tone, message: string) => {
    setToast({ tone, message })
    setTimeout(() => setToast(null), 4000)
  }

  // Form state mirrors the original separate models
  const [sg, setSg] = useState({ apiKey: '', fromEmail: '', fromName: '' })
  const [stripe, setStripe] = useState({ secretKey: '', publishableKey: '' })
  const [pricing, setPricing] = useState({ oneYearFee: 0, threeYearFee: 0 })
  const [asic, setAsic] = useState({ forceFallback: false, email: '', cardNumber: '', cardholderName: '', expiryMonth: '', expiryYear: '', cvc: '' })
  const [ontraport, setOntraport] = useState({ apiAppId: '', apiKey: '', conversationId: '' })
  const [atoAgent, setAtoAgent] = useState({ defaultAgentAbn: '', defaultAgentName: '' })
  const [winBack, setWinBack] = useState({ subject: '', bodyPlain: '', bodyHtml: '' })
  const [tracking, setTracking] = useState({ gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '' })

  // Live ATO session — populates the agent dropdown
  const [agentList, setAgentList] = useState<Array<{ abn: string; name: string }>>([])
  const [agentSession, setAgentSession] = useState<{ authenticated: boolean; phase: string; loaded: boolean; error?: string }>({ authenticated: false, phase: '', loaded: false })

  const load = async () => {
    const r = await api.admin.settings()
    setData(r)
    setSg(r.sendGrid)
    setStripe(r.stripe)
    setPricing(r.pricing)
    setAsic(r.asic)
    setOntraport(r.ontraport)
    setAtoAgent(r.atoAgent)
    setWinBack(r.winBack ?? { subject: '', bodyPlain: '', bodyHtml: '' })
    setTracking(r.tracking ?? { gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '' })
  }
  useEffect(() => { void load() }, [])

  const loadAgents = async () => {
    setAgentSession((s) => ({ ...s, loaded: false, error: undefined }))
    try {
      const r = await api.admin.atoAgents()
      setAgentList(r.agents)
      setAgentSession({ authenticated: r.authenticated, phase: r.phase, loaded: true })
    } catch (e) {
      setAgentSession({ authenticated: false, phase: '', loaded: true, error: e instanceof Error ? e.message : 'Failed to load agents' })
    }
  }
  useEffect(() => {
    if (activeKey === 'atoagent' && !agentSession.loaded) void loadAgents()
  }, [activeKey])  // eslint-disable-line react-hooks/exhaustive-deps

  const flash = (label: string, action: () => Promise<void>) => async () => {
    try {
      await action()
      showToast('emerald', `${label} saved.`)
    } catch (e) {
      showToast('red', `Error saving ${label}: ${e instanceof Error ? e.message : 'unknown'}`)
    }
  }

  const onSendGrid  = (e: React.FormEvent) => { e.preventDefault(); void flash('SendGrid', () => api.admin.updateSendGrid(sg))() }
  const onStripe    = (e: React.FormEvent) => { e.preventDefault(); void flash('Stripe', () => api.admin.updateStripe(stripe))() }
  const onPricing   = (e: React.FormEvent) => { e.preventDefault(); void flash('Pricing', () => api.admin.updatePricing(pricing))() }
  const onAsic      = (e: React.FormEvent) => { e.preventDefault(); void flash('ASIC', () => api.admin.updateAsic(asic))() }
  const onOntraport = (e: React.FormEvent) => { e.preventDefault(); void flash('Ontraport', () => api.admin.updateOntraport(ontraport))() }
  const onAtoAgent  = (e: React.FormEvent) => { e.preventDefault(); void flash('ATO Agent', () => api.admin.updateAtoAgent(atoAgent))() }
  const onWinBack   = (e: React.FormEvent) => { e.preventDefault(); void flash('Win-back template', () => api.admin.updateWinBack(winBack))() }
  const onTracking  = (e: React.FormEvent) => { e.preventDefault(); void flash('Tracking tags', () => api.admin.updateTracking(tracking))() }

  // Configured-status per section (derived from current state in form, which mirrors what the server returned)
  const isFilled = (s: string | undefined | null) => !!s && s.trim().length > 0
  const status: Record<SectionKey, 'configured' | 'partial' | 'empty'> = {
    sendgrid: (() => {
      const total = [sg.apiKey, sg.fromEmail, sg.fromName].filter(isFilled).length
      return total === 3 ? 'configured' : total === 0 ? 'empty' : 'partial'
    })(),
    winback: isFilled(winBack.subject) && isFilled(winBack.bodyPlain) ? 'configured' : 'partial',
    stripe: (() => {
      const total = [stripe.secretKey, stripe.publishableKey].filter(isFilled).length
      return total === 2 ? 'configured' : total === 0 ? 'empty' : 'partial'
    })(),
    pricing: pricing.oneYearFee > 0 && pricing.threeYearFee > 0 ? 'configured' : pricing.oneYearFee > 0 || pricing.threeYearFee > 0 ? 'partial' : 'empty',
    asic: (() => {
      const total = [asic.email, asic.cardNumber, asic.cardholderName, asic.expiryMonth, asic.expiryYear, asic.cvc].filter(isFilled).length
      return total === 6 ? 'configured' : total === 0 ? 'empty' : 'partial'
    })(),
    ontraport: (() => {
      const total = [ontraport.apiAppId, ontraport.apiKey, ontraport.conversationId].filter(isFilled).length
      return total === 3 ? 'configured' : total === 0 ? 'empty' : 'partial'
    })(),
    atoagent: isFilled(atoAgent.defaultAgentAbn) ? 'configured' : 'empty',
    tracking: (() => {
      const total = [tracking.gtmContainerId, tracking.ga4MeasurementId, tracking.metaPixelId].filter(isFilled).length
      return total === 0 ? 'empty' : total === 3 ? 'configured' : 'partial'
    })(),
  }

  // Group sections for sidebar nav
  const groups = SECTIONS.reduce<Record<string, SectionDef[]>>((acc, s) => {
    if (!acc[s.group]) acc[s.group] = []
    acc[s.group].push(s)
    return acc
  }, {})

  const active = SECTIONS.find((s) => s.key === activeKey)!

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="SYSTEM"
        title="Settings"
        subtitle="Integrations, payments, pricing, and the ATO agent that drives onboarding."
      />

      <div className="grid grid-cols-1 lg:grid-cols-[18rem_1fr] gap-6 items-start">
        {/* Sidebar nav */}
        <nav className="rounded-xl border border-zinc-200 bg-white shadow-sm overflow-hidden">
          {Object.entries(groups).map(([group, sections], i) => (
            <div key={group} className={i > 0 ? 'border-t border-zinc-100' : ''}>
              <div className="px-4 pt-4 pb-2 text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-400">{group}</div>
              <ul>
                {sections.map((s) => {
                  const isActive = s.key === activeKey
                  return (
                    <li key={s.key}>
                      <button
                        type="button"
                        onClick={() => setActiveKey(s.key)}
                        className={`group w-full text-left flex items-start justify-between gap-3 px-4 py-2.5 transition relative ${
                          isActive ? 'bg-zinc-50' : 'hover:bg-zinc-50'
                        }`}
                      >
                        {isActive ? <span className="absolute inset-y-2 left-0 w-0.5 rounded-r bg-brand-500" /> : null}
                        <div className="min-w-0 flex-1">
                          <div className={`text-sm font-medium ${isActive ? 'text-zinc-900' : 'text-zinc-700 group-hover:text-zinc-900'}`}>{s.title}</div>
                          <div className="text-xxs font-mono text-zinc-400 truncate">{s.description}</div>
                        </div>
                        <ConfiguredDot status={status[s.key]} />
                      </button>
                    </li>
                  )
                })}
              </ul>
            </div>
          ))}
        </nav>

        {/* Active section */}
        <div className="min-w-0">
          {!data ? (
            <p className="text-sm text-zinc-500">Loading…</p>
          ) : (
            <Section title={active.title} subtitle={active.description} status={status[active.key]}>
              {activeKey === 'sendgrid' ? (
                <form onSubmit={onSendGrid} className="space-y-4">
                  <Field label="API key">
                    <input className={inputCls} value={sg.apiKey} onChange={(e) => setSg({ ...sg, apiKey: e.target.value })} />
                  </Field>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <Field label="From email">
                      <input className={inputCls} value={sg.fromEmail} onChange={(e) => setSg({ ...sg, fromEmail: e.target.value })} />
                    </Field>
                    <Field label="From name">
                      <input className={inputCls} value={sg.fromName} onChange={(e) => setSg({ ...sg, fromName: e.target.value })} />
                    </Field>
                  </div>
                  <button type="submit" className={submitBtnCls}>Save</button>
                </form>
              ) : null}

              {activeKey === 'winback' ? (
                <form onSubmit={onWinBack} className="space-y-4">
                  <Field label="Subject" hint="Merge tags: {{FullName}}, {{Abn}}, {{Email}}, {{BusinessName}}.">
                    <input className={inputCls} value={winBack.subject} onChange={(e) => setWinBack({ ...winBack, subject: e.target.value })} />
                  </Field>
                  <Field label="Body (plain text)">
                    <textarea
                      rows={10}
                      className={`${inputCls} font-mono text-xs leading-relaxed`}
                      value={winBack.bodyPlain}
                      onChange={(e) => setWinBack({ ...winBack, bodyPlain: e.target.value })}
                    />
                  </Field>
                  <Field label="Body (HTML, optional)" hint="Leave blank to auto-wrap the plain-text body.">
                    <textarea
                      rows={6}
                      className={`${inputCls} font-mono text-xs leading-relaxed`}
                      value={winBack.bodyHtml}
                      onChange={(e) => setWinBack({ ...winBack, bodyHtml: e.target.value })}
                    />
                  </Field>
                  <button type="submit" className={submitBtnCls}>Save template</button>
                </form>
              ) : null}

              {activeKey === 'stripe' ? (
                <form onSubmit={onStripe} className="space-y-4">
                  <Field label="Secret key">
                    <input type="password" className={`${inputCls} font-mono`} value={stripe.secretKey} onChange={(e) => setStripe({ ...stripe, secretKey: e.target.value })} />
                  </Field>
                  <Field label="Publishable key">
                    <input className={`${inputCls} font-mono`} value={stripe.publishableKey} onChange={(e) => setStripe({ ...stripe, publishableKey: e.target.value })} />
                  </Field>
                  <button type="submit" className={submitBtnCls}>Save</button>
                </form>
              ) : null}

              {activeKey === 'pricing' ? (
                <form onSubmit={onPricing} className="space-y-4">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <Field label="1-year renewal price">
                      <div className="relative mt-1">
                        <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-sm text-zinc-400">$</span>
                        <input type="number" step="0.01" value={pricing.oneYearFee} onChange={(e) => setPricing({ ...pricing, oneYearFee: Number(e.target.value) })}
                          className="block w-full rounded-md border-zinc-300 pl-7 shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 sm:text-sm px-3 py-2 border font-mono tabular-nums" />
                      </div>
                    </Field>
                    <Field label="3-year renewal price">
                      <div className="relative mt-1">
                        <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-sm text-zinc-400">$</span>
                        <input type="number" step="0.01" value={pricing.threeYearFee} onChange={(e) => setPricing({ ...pricing, threeYearFee: Number(e.target.value) })}
                          className="block w-full rounded-md border-zinc-300 pl-7 shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 sm:text-sm px-3 py-2 border font-mono tabular-nums" />
                      </div>
                    </Field>
                  </div>
                  <button type="submit" className={submitBtnCls}>Save</button>
                </form>
              ) : null}

              {activeKey === 'asic' ? (
                <form onSubmit={onAsic} className="space-y-4">
                  <Field label="Email">
                    <input type="email" className={inputCls} value={asic.email} onChange={(e) => setAsic({ ...asic, email: e.target.value })} />
                  </Field>
                  <Field label="Card number">
                    <input className={`${inputCls} font-mono tabular-nums`} value={asic.cardNumber} onChange={(e) => setAsic({ ...asic, cardNumber: e.target.value })} />
                  </Field>
                  <Field label="Cardholder name">
                    <input className={inputCls} value={asic.cardholderName} onChange={(e) => setAsic({ ...asic, cardholderName: e.target.value })} />
                  </Field>
                  <div className="grid grid-cols-3 gap-4">
                    <Field label="Expiry month">
                      <input className={`${inputCls} font-mono tabular-nums`} value={asic.expiryMonth} onChange={(e) => setAsic({ ...asic, expiryMonth: e.target.value })} />
                    </Field>
                    <Field label="Expiry year">
                      <input className={`${inputCls} font-mono tabular-nums`} value={asic.expiryYear} onChange={(e) => setAsic({ ...asic, expiryYear: e.target.value })} />
                    </Field>
                    <Field label="CVC">
                      <input type="password" className={`${inputCls} font-mono tabular-nums`} value={asic.cvc} onChange={(e) => setAsic({ ...asic, cvc: e.target.value })} />
                    </Field>
                  </div>
                  <button type="submit" className={submitBtnCls}>Save</button>
                </form>
              ) : null}

              {activeKey === 'ontraport' ? (
                <form onSubmit={onOntraport} className="space-y-4">
                  <Field label="API app ID">
                    <input className={`${inputCls} font-mono`} value={ontraport.apiAppId} onChange={(e) => setOntraport({ ...ontraport, apiAppId: e.target.value })} />
                  </Field>
                  <Field label="API key">
                    <input className={`${inputCls} font-mono`} value={ontraport.apiKey} onChange={(e) => setOntraport({ ...ontraport, apiKey: e.target.value })} />
                  </Field>
                  <Field label="Conversation ID" hint="Used to retrieve OTP SMS messages from ASIC.">
                    <input className={`${inputCls} font-mono`} value={ontraport.conversationId} onChange={(e) => setOntraport({ ...ontraport, conversationId: e.target.value })} />
                  </Field>
                  <button type="submit" className={submitBtnCls}>Save</button>
                </form>
              ) : null}

              {activeKey === 'atoagent' ? (
                <form onSubmit={onAtoAgent} className="space-y-5">
                  {!agentSession.loaded ? (
                    <p className="text-sm text-zinc-500">Loading agents from the ATO session…</p>
                  ) : agentSession.error ? (
                    <div className="rounded-md bg-red-50 ring-1 ring-red-100 p-3">
                      <p className="text-sm text-red-800">{agentSession.error}</p>
                      <button type="button" onClick={() => void loadAgents()} className="mt-2 text-sm font-medium text-red-700 underline hover:text-red-900">Retry</button>
                    </div>
                  ) : !agentSession.authenticated ? (
                    <div className="rounded-md bg-amber-50 ring-1 ring-amber-100 p-3 text-sm text-amber-800">
                      <div className="font-medium">Ato.Api session is not authenticated ({agentSession.phase || 'unknown'}).</div>
                      <div className="mt-1">Sign in to myID via the Ato.Api host first, then come back to pick a default agent.</div>
                      <button type="button" onClick={() => void loadAgents()} className="mt-2 text-sm font-medium text-amber-700 underline hover:text-amber-900">Refresh</button>
                    </div>
                  ) : (
                    <>
                      <Field label="Default agent">
                        <select
                          className={inputCls}
                          value={atoAgent.defaultAgentAbn}
                          onChange={(e) => {
                            const abn = e.target.value
                            const found = agentList.find((a) => a.abn === abn)
                            setAtoAgent({ defaultAgentAbn: abn, defaultAgentName: found?.name ?? '' })
                          }}
                        >
                          <option value="">— Select an agent —</option>
                          {agentList.map((a) => (
                            <option key={a.abn} value={a.abn}>{a.name} · {a.abn}</option>
                          ))}
                        </select>
                        <div className="mt-2 flex items-center justify-between">
                          <p className="text-xxs font-mono text-zinc-500">{agentList.length} agent(s) available in this session.</p>
                          <button type="button" onClick={() => void loadAgents()} className="text-xs font-medium text-brand-700 hover:text-brand-800">Refresh list</button>
                        </div>
                      </Field>

                      {atoAgent.defaultAgentAbn ? (
                        <div className="rounded-md border border-zinc-200 bg-zinc-50 px-3 py-2 text-xs text-zinc-600">
                          <div><span className="font-medium text-zinc-700">Selected:</span> {atoAgent.defaultAgentName}</div>
                          <div className="font-mono tabular-nums">ABN {atoAgent.defaultAgentAbn}</div>
                        </div>
                      ) : null}

                      <button type="submit" disabled={!atoAgent.defaultAgentAbn} className={submitBtnCls}>Save</button>
                    </>
                  )}
                </form>
              ) : null}

              {activeKey === 'tracking' ? (
                <form onSubmit={onTracking} className="space-y-4">
                  <p className="text-sm text-zinc-600">
                    These load on the public site at runtime — save here and the tags apply on the next page
                    load, no redeploy. Leave a field blank to skip that tag. Wizard steps are recorded
                    server-side regardless, and show up under <span className="font-medium">Funnel</span>.
                  </p>
                  <Field label="Google Tag Manager container" hint="GTM-XXXXXXX. Use this if you'd rather manage tags inside GTM.">
                    <input className={`${inputCls} font-mono`} value={tracking.gtmContainerId} onChange={(e) => setTracking({ ...tracking, gtmContainerId: e.target.value })} placeholder="GTM-XXXXXXX" />
                  </Field>
                  <Field label="GA4 measurement ID" hint="G-XXXXXXXXXX.">
                    <input className={`${inputCls} font-mono`} value={tracking.ga4MeasurementId} onChange={(e) => setTracking({ ...tracking, ga4MeasurementId: e.target.value })} placeholder="G-XXXXXXXXXX" />
                  </Field>
                  <Field label="Meta pixel ID" hint="Fires Lead on details, InitiateCheckout on payment, Purchase on completion.">
                    <input className={`${inputCls} font-mono`} value={tracking.metaPixelId} onChange={(e) => setTracking({ ...tracking, metaPixelId: e.target.value })} placeholder="1234567890" />
                  </Field>
                  <button type="submit" className={submitBtnCls}>Save</button>
                </form>
              ) : null}
            </Section>
          )}
        </div>
      </div>

      {toast ? (
        <div className="fixed bottom-6 right-6 z-50 fade-in"><Toast tone={toast.tone} message={toast.message} /></div>
      ) : null}
    </div>
  )
}

function ConfiguredDot({ status }: { status: 'configured' | 'partial' | 'empty' }) {
  const map = { configured: 'bg-emerald-500', partial: 'bg-amber-500', empty: 'bg-zinc-300' }
  const labels = { configured: 'Configured', partial: 'Incomplete', empty: 'Not configured' }
  return (
    <span className="shrink-0 mt-1.5 inline-flex items-center" title={labels[status]}>
      <span className={`h-1.5 w-1.5 rounded-full ${map[status]}`} />
    </span>
  )
}

function Section({ title, subtitle, status, children }: { title: string; subtitle?: string; status: 'configured' | 'partial' | 'empty'; children: ReactNode }) {
  const tone = status === 'configured' ? 'emerald' : status === 'partial' ? 'amber' : 'zinc'
  const label = status === 'configured' ? 'CONFIGURED' : status === 'partial' ? 'INCOMPLETE' : 'NOT SET'
  const statusClass = tone === 'emerald' ? 'text-emerald-700' : tone === 'amber' ? 'text-amber-700' : 'text-zinc-500'
  return (
    <div className="rounded-xl bg-white p-6 ring-1 ring-zinc-200 shadow-sm">
      <div className="flex items-center justify-between gap-3 mb-4">
        <div>
          <h3 className="text-base font-semibold text-zinc-900 tracking-tight">{title}</h3>
          {subtitle ? <p className="mt-0.5 text-sm text-zinc-500">{subtitle}</p> : null}
        </div>
        <span className={`text-xxs font-mono font-medium uppercase tracking-[0.16em] ${statusClass}`}>{label}</span>
      </div>
      {children}
    </div>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <div>
      <label className={labelCls}>{label}</label>
      {children}
      {hint ? <p className="mt-1 text-xxs font-mono text-zinc-500">{hint}</p> : null}
    </div>
  )
}
