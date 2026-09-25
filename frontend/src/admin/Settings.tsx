import { useEffect, useState, type ReactNode } from 'react'
import { useMutation } from '@tanstack/react-query'
import { sileo } from 'sileo'
import { api, type AsicKeyInboxSettings, type AsicKeyInboxTestResult, type AsicKeyRequestSettings, type AsicKeyRequestTestResult } from '../api/client'
import { PageHeader } from './_ui'
import { relativeTime } from './_utils'

type SectionKey = 'sendgrid' | 'winback' | 'stripe' | 'pricing' | 'asic' | 'ontraport' | 'asickeys' | 'asickeyrequests' | 'tracking'

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
  { key: 'asickeys',  group: 'INTEGRATIONS',  title: 'ASIC key inbox',    description: 'Gmail inbox scanned for ASIC key notifications.' },
  { key: 'asickeyrequests', group: 'INTEGRATIONS', title: 'ASIC key requests', description: 'Asks ASIC for each sale’s key through its enquiry form.' },
  { key: 'tracking',  group: 'MARKETING',     title: 'Tracking tags',     description: 'GA4, GTM and Meta pixel ids.' },
]

export default function Settings() {
  const [activeKey, setActiveKey] = useState<SectionKey>('sendgrid')
  const [data, setData] = useState<Settings | null>(null)

  // Form state mirrors the original separate models
  const [sg, setSg] = useState({ apiKey: '', fromEmail: '', fromName: '' })
  const [stripe, setStripe] = useState({ secretKey: '', publishableKey: '' })
  const [pricing, setPricing] = useState({ oneYearFee: 0, threeYearFee: 0 })
  const [asic, setAsic] = useState({ forceFallback: false, email: '', cardNumber: '', cardholderName: '', expiryMonth: '', expiryYear: '', cvc: '' })
  // The CVC never comes back from the server; this flag says one is stored.
  const [asicHasCvc, setAsicHasCvc] = useState(false)
  const [ontraport, setOntraport] = useState({ apiAppId: '', apiKey: '', conversationId: '' })
  const [winBack, setWinBack] = useState({ subject: '', bodyPlain: '', bodyHtml: '' })
  const [tracking, setTracking] = useState({ gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '' })
  const [asicKeys, setAsicKeys] = useState<AsicKeyInboxSettings>(defaultAsicKeyInbox())
  const [asicKeysTest, setAsicKeysTest] = useState<AsicKeyInboxTestResult | null>(null)
  const [keyRequests, setKeyRequests] = useState<AsicKeyRequestSettings>(defaultAsicKeyRequest())
  const [keyRequestsTest, setKeyRequestsTest] = useState<AsicKeyRequestTestResult | null>(null)

  const load = async () => {
    const r = await api.admin.settings()
    setData(r)
    setSg(r.sendGrid)
    setStripe(r.stripe)
    setPricing(r.pricing)
    const { hasCvc, ...asicFields } = r.asic
    setAsic(asicFields)
    setAsicHasCvc(hasCvc)
    setOntraport(r.ontraport)
    setWinBack(r.winBack ?? { subject: '', bodyPlain: '', bodyHtml: '' })
    setTracking(r.tracking ?? { gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '' })
    setAsicKeys(r.asicKeyInbox ?? defaultAsicKeyInbox())
    setKeyRequests(r.asicKeyRequest ?? defaultAsicKeyRequest())
  }
  useEffect(() => { void load() }, [])

  const saveMutation = useMutation({
    mutationFn: (action: () => Promise<void>) => action(),
  })
  const save = (label: string, action: () => Promise<void>) => (e: React.FormEvent) => {
    e.preventDefault()
    void sileo.promise(saveMutation.mutateAsync(action), {
      loading: { title: `Saving ${label}…` },
      success: () => ({ title: `${label} saved.` }),
      error: (err) => ({ title: `Error saving ${label}`, description: err instanceof Error ? err.message : undefined }),
    }).catch(() => {})
  }

  const onSendGrid  = save('SendGrid', () => api.admin.updateSendGrid(sg))
  const onStripe    = save('Stripe', () => api.admin.updateStripe(stripe))
  const onPricing   = save('Pricing', () => api.admin.updatePricing(pricing))
  const onAsic      = save('ASIC', () => api.admin.updateAsic(asic).then(() => { if (asic.cvc) setAsicHasCvc(true) }))
  const onOntraport = save('Ontraport', () => api.admin.updateOntraport(ontraport))
  const onWinBack   = save('Win-back template', () => api.admin.updateWinBack(winBack))
  const onTracking  = save('Tracking tags', () => api.admin.updateTracking(tracking))
  const onAsicKeys  = save('ASIC key inbox', () => api.admin.updateAsicKeyInbox(asicKeys))
  const onKeyRequests = save('ASIC key requests', () => api.admin.updateAsicKeyRequest(keyRequests))

  // Starts the server's browser against ASIC's form; the check that the container can mint a token.
  const testRequestsMutation = useMutation({ mutationFn: () => api.admin.testAsicKeyRequest() })
  const testKeyRequests = () => {
    setKeyRequestsTest(null)
    void sileo.promise(testRequestsMutation.mutateAsync(), {
      loading: { title: 'Starting the browser and loading ASIC’s form…' },
      success: (r) => {
        setKeyRequestsTest(r)
        return r.ok
          ? { title: 'Browser got a reCAPTCHA token', description: `${r.browser}${r.egressIp ? ` from ${r.egressIp}` : ''}. Whether ASIC accepts its score shows on the first real request.` }
          : { title: 'Browser check failed', description: r.error ?? undefined }
      },
      error: (err) => ({ title: 'Test failed', description: err instanceof Error ? err.message : undefined }),
    }).catch(() => {})
  }


  // Tests the form's current values without saving them; each check reports on its own line.
  const testMutation = useMutation({ mutationFn: () => api.admin.testAsicKeyInbox(asicKeys) })
  const testAsicKeys = () => {
    setAsicKeysTest(null)
    void sileo.promise(testMutation.mutateAsync(), {
      loading: { title: 'Testing inbox, Ontraport field and pattern…' },
      success: (r) => {
        setAsicKeysTest(r)
        const allOk = r.mailbox.ok && r.ontraportField.ok && r.keyPattern.ok
        return { title: allOk ? 'All checks passed' : 'Some checks failed', description: allOk ? 'Save to keep these values.' : 'See the results under the form.' }
      },
      error: (err) => ({ title: 'Test failed', description: err instanceof Error ? err.message : undefined }),
    }).catch(() => {})
  }

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
      const total = [asic.email, asic.cardNumber, asic.cardholderName, asic.expiryMonth, asic.expiryYear].filter(isFilled).length
        + (isFilled(asic.cvc) || asicHasCvc ? 1 : 0)
      return total === 6 ? 'configured' : total === 0 ? 'empty' : 'partial'
    })(),
    ontraport: (() => {
      const total = [ontraport.apiAppId, ontraport.apiKey, ontraport.conversationId].filter(isFilled).length
      return total === 3 ? 'configured' : total === 0 ? 'empty' : 'partial'
    })(),
    tracking: (() => {
      const total = [tracking.gtmContainerId, tracking.ga4MeasurementId, tracking.metaPixelId].filter(isFilled).length
      return total === 0 ? 'empty' : total === 3 ? 'configured' : 'partial'
    })(),
    asickeys: (() => {
      const total = [asicKeys.username, asicKeys.password, asicKeys.ontraportFieldId].filter(isFilled).length
      return total === 3 ? 'configured' : total === 0 ? 'empty' : 'partial'
    })(),
    asickeyrequests: keyRequests.enabled && isFilled(keyRequests.requestEmail) ? 'configured' : isFilled(keyRequests.requestEmail) ? 'partial' : 'empty',
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
                    <Field label="CVC" hint={asicHasCvc ? 'Stored. Leave blank to keep it.' : undefined}>
                      <input type="password" className={`${inputCls} font-mono tabular-nums`} value={asic.cvc} onChange={(e) => setAsic({ ...asic, cvc: e.target.value })} placeholder={asicHasCvc ? '•••' : ''} />
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

              {activeKey === 'asickeys' ? (
                <form onSubmit={onAsicKeys} className="space-y-4">
                  <label className="flex items-center gap-2 text-sm text-zinc-700">
                    <input type="checkbox" className="rounded border-zinc-300 text-brand-600 focus:ring-brand-500" checked={asicKeys.enabled} onChange={(e) => setAsicKeys({ ...asicKeys, enabled: e.target.checked })} />
                    Scanning enabled
                  </label>
                  <div className="grid grid-cols-1 sm:grid-cols-[1fr_8rem] gap-4">
                    <Field label="IMAP host">
                      <input className={`${inputCls} font-mono`} value={asicKeys.imapHost} onChange={(e) => setAsicKeys({ ...asicKeys, imapHost: e.target.value })} placeholder="imap.gmail.com" />
                    </Field>
                    <Field label="Port">
                      <input type="number" className={`${inputCls} font-mono tabular-nums`} value={asicKeys.imapPort} onChange={(e) => setAsicKeys({ ...asicKeys, imapPort: Number(e.target.value) || 993 })} />
                    </Field>
                  </div>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <Field label="Gmail address">
                      <input type="email" className={inputCls} value={asicKeys.username} onChange={(e) => setAsicKeys({ ...asicKeys, username: e.target.value })} />
                    </Field>
                    <Field label="App password" hint="A Google app password, not the account password.">
                      <input type="password" className={`${inputCls} font-mono`} value={asicKeys.password} onChange={(e) => setAsicKeys({ ...asicKeys, password: e.target.value })} />
                    </Field>
                  </div>
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                    <Field label="Folder / label">
                      <input className={`${inputCls} font-mono`} value={asicKeys.folder} onChange={(e) => setAsicKeys({ ...asicKeys, folder: e.target.value })} placeholder="INBOX" />
                    </Field>
                    <Field label="Subject contains">
                      <input className={inputCls} value={asicKeys.subjectFilter} onChange={(e) => setAsicKeys({ ...asicKeys, subjectFilter: e.target.value })} />
                    </Field>
                    <Field label="Lookback (days)" hint="ASIC download links expire after 30 days.">
                      <input type="number" min={1} max={365} className={`${inputCls} font-mono tabular-nums`} value={asicKeys.lookbackDays} onChange={(e) => setAsicKeys({ ...asicKeys, lookbackDays: Number(e.target.value) || 30 })} />
                    </Field>
                  </div>
                  <Field label="Ontraport ASIC key field" hint="Contact custom field ID, e.g. f5xxx.">
                    <input className={`${inputCls} font-mono`} value={asicKeys.ontraportFieldId} onChange={(e) => setAsicKeys({ ...asicKeys, ontraportFieldId: e.target.value })} placeholder="f5xxx" />
                  </Field>
                  <Field label="ASIC key pattern" hint="Regex over the PDF text; group 1 is the key.">
                    <input className={`${inputCls} font-mono text-xs`} value={asicKeys.asicKeyPattern} onChange={(e) => setAsicKeys({ ...asicKeys, asicKeyPattern: e.target.value })} />
                    {asicKeys.defaultAsicKeyPattern && asicKeys.asicKeyPattern !== asicKeys.defaultAsicKeyPattern ? (
                      <button
                        type="button"
                        onClick={() => setAsicKeys({ ...asicKeys, asicKeyPattern: asicKeys.defaultAsicKeyPattern! })}
                        className="mt-1 text-xxs font-mono text-brand-700 hover:underline"
                      >
                        Use the current default pattern
                      </button>
                    ) : null}
                  </Field>
                  <div className="flex flex-wrap items-center gap-2">
                    <button type="submit" className={submitBtnCls}>Save</button>
                    <button
                      type="button"
                      onClick={testAsicKeys}
                      disabled={testMutation.isPending}
                      className="inline-flex justify-center rounded-md bg-white text-zinc-800 px-3 py-2 text-sm font-medium shadow-sm ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 disabled:opacity-50 disabled:cursor-not-allowed transition"
                    >
                      {testMutation.isPending ? 'Testing…' : 'Test connection'}
                    </button>
                  </div>
                  {asicKeysTest ? <AsicKeyTestResults result={asicKeysTest} /> : null}
                </form>
              ) : null}

              {activeKey === 'asickeyrequests' ? (
                <form onSubmit={onKeyRequests} className="space-y-4">
                  <label className="flex items-center gap-2 text-sm text-zinc-700">
                    <input type="checkbox" className="rounded border-zinc-300 text-brand-600 focus:ring-brand-500" checked={keyRequests.enabled} onChange={(e) => setKeyRequests({ ...keyRequests, enabled: e.target.checked })} />
                    Requests enabled
                  </label>
                  <label className="flex items-center gap-2 text-sm text-zinc-700">
                    <input type="checkbox" className="rounded border-zinc-300 text-brand-600 focus:ring-brand-500" checked={keyRequests.autoRequestOnSync} onChange={(e) => setKeyRequests({ ...keyRequests, autoRequestOnSync: e.target.checked })} />
                    Queue a request for every eligible sale the Ontraport sync brings in
                  </label>
                  <Field label="Email the key to">
                    <input type="email" className={inputCls} value={keyRequests.requestEmail} onChange={(e) => setKeyRequests({ ...keyRequests, requestEmail: e.target.value })} />
                  </Field>
                  <div className="grid grid-cols-1 sm:grid-cols-[10rem_1fr] gap-4">
                    <Field label="Area code">
                      <input className={`${inputCls} font-mono tabular-nums`} value={keyRequests.defaultPhonePrefix} onChange={(e) => setKeyRequests({ ...keyRequests, defaultPhonePrefix: e.target.value })} placeholder="02" />
                    </Field>
                    <Field label="Fallback phone">
                      <input className={`${inputCls} font-mono tabular-nums`} value={keyRequests.defaultPhoneNumber} onChange={(e) => setKeyRequests({ ...keyRequests, defaultPhoneNumber: e.target.value })} />
                    </Field>
                  </div>
                  <Field label="Enquiry text">
                    <textarea rows={3} className={`${inputCls} font-mono text-xs`} value={keyRequests.messageTemplate} onChange={(e) => setKeyRequests({ ...keyRequests, messageTemplate: e.target.value })} />
                    {keyRequests.defaultMessageTemplate && keyRequests.messageTemplate !== keyRequests.defaultMessageTemplate ? (
                      <button
                        type="button"
                        onClick={() => setKeyRequests({ ...keyRequests, messageTemplate: keyRequests.defaultMessageTemplate! })}
                        className="mt-1 text-xxs font-mono text-brand-700 hover:underline"
                      >
                        Use the default wording
                      </button>
                    ) : null}
                  </Field>
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                    <Field label="Max per run">
                      <input type="number" min={1} max={200} className={`${inputCls} font-mono tabular-nums`} value={keyRequests.maxPerRun} onChange={(e) => setKeyRequests({ ...keyRequests, maxPerRun: Number(e.target.value) || 25 })} />
                    </Field>
                    <Field label="Captcha attempts">
                      <input type="number" min={1} max={5} className={`${inputCls} font-mono tabular-nums`} value={keyRequests.maxCaptchaAttempts} onChange={(e) => setKeyRequests({ ...keyRequests, maxCaptchaAttempts: Number(e.target.value) || 3 })} />
                    </Field>
                    <Field label="Auto-retries">
                      <input type="number" min={1} max={20} className={`${inputCls} font-mono tabular-nums`} value={keyRequests.maxAutoAttempts} onChange={(e) => setKeyRequests({ ...keyRequests, maxAutoAttempts: Number(e.target.value) || 5 })} />
                    </Field>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <button type="submit" className={submitBtnCls}>Save</button>
                    <button
                      type="button"
                      onClick={testKeyRequests}
                      disabled={testRequestsMutation.isPending}
                      className="inline-flex justify-center rounded-md bg-white text-zinc-800 px-3 py-2 text-sm font-medium shadow-sm ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 disabled:opacity-50 disabled:cursor-not-allowed transition"
                    >
                      {testRequestsMutation.isPending ? 'Testing…' : `Test browser${keyRequests.browser ? ` (${keyRequests.browser})` : ''}`}
                    </button>
                  </div>
                  {keyRequestsTest ? (
                    <div className={`rounded-md px-3 py-2 text-xs font-mono ring-1 ${keyRequestsTest.ok ? 'bg-emerald-50 text-emerald-800 ring-emerald-100' : 'bg-red-50 text-red-800 ring-red-100'}`}>
                      {keyRequestsTest.ok
                        ? `✔ ${keyRequestsTest.browser} loaded ASIC’s form and got a reCAPTCHA token${keyRequestsTest.egressIp ? ` · egress IP ${keyRequestsTest.egressIp}` : ''}`
                        : `✘ ${keyRequestsTest.error ?? 'failed'}${keyRequestsTest.egressIp ? ` · egress IP ${keyRequestsTest.egressIp}` : ''}`}
                    </div>
                  ) : null}
                </form>
              ) : null}

              {activeKey === 'tracking' ? (
                <form onSubmit={onTracking} className="space-y-4">
                  <Field label="Google Tag Manager container">
                    <input className={`${inputCls} font-mono`} value={tracking.gtmContainerId} onChange={(e) => setTracking({ ...tracking, gtmContainerId: e.target.value })} placeholder="GTM-XXXXXXX" />
                  </Field>
                  <Field label="GA4 measurement ID">
                    <input className={`${inputCls} font-mono`} value={tracking.ga4MeasurementId} onChange={(e) => setTracking({ ...tracking, ga4MeasurementId: e.target.value })} placeholder="G-XXXXXXXXXX" />
                  </Field>
                  <Field label="Meta pixel ID">
                    <input className={`${inputCls} font-mono`} value={tracking.metaPixelId} onChange={(e) => setTracking({ ...tracking, metaPixelId: e.target.value })} placeholder="1234567890" />
                  </Field>
                  <button type="submit" className={submitBtnCls}>Save</button>
                </form>
              ) : null}
            </Section>
          )}
        </div>
      </div>
    </div>
  )
}

function defaultAsicKeyRequest(): AsicKeyRequestSettings {
  return {
    enabled: false, autoRequestOnSync: true, requestEmail: 'businessnamerenewals@gmail.com',
    defaultPhonePrefix: '', defaultPhoneNumber: '', messageTemplate: '',
    maxPerRun: 25, maxCaptchaAttempts: 3, maxAutoAttempts: 5,
  }
}

function defaultAsicKeyInbox(): AsicKeyInboxSettings {
  return {
    enabled: true, imapHost: 'imap.gmail.com', imapPort: 993, username: '', password: '',
    folder: 'INBOX', subjectFilter: 'Notification request', lookbackDays: 30,
    ontraportFieldId: '', asicKeyPattern: '',
  }
}

function AsicKeyTestResults({ result }: { result: AsicKeyInboxTestResult }) {
  const { mailbox, ontraportField, keyPattern } = result
  return (
    <div className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 divide-y divide-zinc-200 text-sm">
      <TestRow ok={mailbox.ok} label="Mailbox">
        {mailbox.ok ? (
          <>
            Signed in to <span className="font-mono">{mailbox.host}</span> as <span className="font-mono">{mailbox.username}</span>.{' '}
            <span className="font-mono">{mailbox.folder}</span> holds {mailbox.messagesInFolder.toLocaleString()} messages;{' '}
            <span className="font-semibold tabular-nums">{mailbox.matchingInLookback}</span> match the subject filter in the lookback window
            {mailbox.latestSubject ? <> — latest: “{mailbox.latestSubject}”{mailbox.latestReceivedAt ? ` (${relativeTime(mailbox.latestReceivedAt)})` : ''}</> : null}.
          </>
        ) : mailbox.error}
      </TestRow>
      <TestRow ok={ontraportField.ok} label="Ontraport field">
        {ontraportField.ok
          ? <>Field <span className="font-mono">{ontraportField.fieldId}</span> exists on Contacts as “{ontraportField.alias}”.</>
          : ontraportField.error}
      </TestRow>
      <TestRow ok={keyPattern.ok} label="Key pattern">
        {keyPattern.ok
          ? <>Extracts <span className="font-mono">{keyPattern.sampleKey}</span> from the sample letter wording.</>
          : keyPattern.error}
      </TestRow>
    </div>
  )
}

function TestRow({ ok, label, children }: { ok: boolean; label: string; children: ReactNode }) {
  return (
    <div className="flex items-start gap-3 px-3 py-2">
      <span className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${ok ? 'bg-emerald-500' : 'bg-red-500'}`} />
      <div className="min-w-0">
        <div className={`text-xxs font-mono font-medium uppercase tracking-[0.14em] ${ok ? 'text-emerald-700' : 'text-red-700'}`}>{label} · {ok ? 'OK' : 'FAILED'}</div>
        <div className={`mt-0.5 break-words ${ok ? 'text-zinc-700' : 'text-red-800'}`}>{children}</div>
      </div>
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
