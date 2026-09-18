import { useEffect, useState, type ReactNode } from 'react'
import { useMutation } from '@tanstack/react-query'
import { sileo } from 'sileo'
import { api, type AsicKeyInboxSettings, type AsicKeyInboxTestResult, type AsicKeyRequestSettings, type AsicKeyRequestTestResult } from '../api/client'
import { PageHeader } from './_ui'
import { relativeTime } from './_utils'

type SectionKey = 'sendgrid' | 'winback' | 'stripe' | 'pricing' | 'asic' | 'ontraport' | 'asickeys' | 'asickeyreq' | 'tracking'

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
  { key: 'asickeyreq', group: 'INTEGRATIONS', title: 'ASIC key requests', description: 'Ask ASIC for each sale\'s key via its enquiry form (2Captcha).' },
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
  const [ontraport, setOntraport] = useState({ apiAppId: '', apiKey: '', conversationId: '' })
  const [winBack, setWinBack] = useState({ subject: '', bodyPlain: '', bodyHtml: '' })
  const [tracking, setTracking] = useState({ gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '' })
  const [asicKeys, setAsicKeys] = useState<AsicKeyInboxSettings>(defaultAsicKeyInbox())
  const [asicKeysTest, setAsicKeysTest] = useState<AsicKeyInboxTestResult | null>(null)
  const [keyReq, setKeyReq] = useState<AsicKeyRequestSettings>(defaultAsicKeyRequest())
  const [keyReqTest, setKeyReqTest] = useState<AsicKeyRequestTestResult | null>(null)

  const load = async () => {
    const r = await api.admin.settings()
    setData(r)
    setSg(r.sendGrid)
    setStripe(r.stripe)
    setPricing(r.pricing)
    setAsic(r.asic)
    setOntraport(r.ontraport)
    setWinBack(r.winBack ?? { subject: '', bodyPlain: '', bodyHtml: '' })
    setTracking(r.tracking ?? { gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '' })
    setAsicKeys(r.asicKeyInbox ?? defaultAsicKeyInbox())
    setKeyReq(r.asicKeyRequest ?? defaultAsicKeyRequest())
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
  const onAsic      = save('ASIC', () => api.admin.updateAsic(asic))
  const onOntraport = save('Ontraport', () => api.admin.updateOntraport(ontraport))
  const onWinBack   = save('Win-back template', () => api.admin.updateWinBack(winBack))
  const onTracking  = save('Tracking tags', () => api.admin.updateTracking(tracking))
  const onAsicKeys  = save('ASIC key inbox', () => api.admin.updateAsicKeyInbox(asicKeys))
  const onKeyReq    = save('ASIC key requests', () => api.admin.updateAsicKeyRequest(keyReq))

  const keyReqTestMutation = useMutation({ mutationFn: () => api.admin.testAsicKeyRequest(keyReq) })
  const testKeyReq = () => {
    setKeyReqTest(null)
    void sileo.promise(keyReqTestMutation.mutateAsync(), {
      loading: { title: 'Checking 2Captcha, score, template and inbox…' },
      success: (r) => {
        setKeyReqTest(r)
        const allOk = r.captcha.ok && r.score.ok && r.template.ok && r.email.ok
        return { title: allOk ? 'All checks passed' : 'Some checks failed', description: allOk ? 'Save to keep these values.' : 'See the results under the form.' }
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
      const total = [asic.email, asic.cardNumber, asic.cardholderName, asic.expiryMonth, asic.expiryYear, asic.cvc].filter(isFilled).length
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
    asickeyreq: (() => {
      if (!isFilled(keyReq.twoCaptchaApiKey)) return 'empty'
      return keyReq.enabled && isFilled(keyReq.requestEmail) ? 'configured' : 'partial'
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

              {activeKey === 'asickeyreq' ? (
                <form onSubmit={onKeyReq} className="space-y-4">
                  <div className="flex flex-wrap gap-x-6 gap-y-2">
                    <label className="flex items-center gap-2 text-sm text-zinc-700">
                      <input type="checkbox" className="rounded border-zinc-300 text-brand-600 focus:ring-brand-500" checked={keyReq.enabled} onChange={(e) => setKeyReq({ ...keyReq, enabled: e.target.checked })} />
                      Requests enabled
                    </label>
                    <label className="flex items-center gap-2 text-sm text-zinc-700">
                      <input type="checkbox" className="rounded border-zinc-300 text-brand-600 focus:ring-brand-500" checked={keyReq.autoRequestOnSync} onChange={(e) => setKeyReq({ ...keyReq, autoRequestOnSync: e.target.checked })} />
                      Queue one for every new sale
                    </label>
                  </div>
                  <Field label="2Captcha API key">
                    <input type="password" className={`${inputCls} font-mono`} value={keyReq.twoCaptchaApiKey} onChange={(e) => setKeyReq({ ...keyReq, twoCaptchaApiKey: e.target.value })} />
                  </Field>
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                    <Field label="Captcha score" hint="ASIC rejects tokens under 0.5.">
                      <select className={inputCls} value={String(keyReq.minCaptchaScore)} onChange={(e) => setKeyReq({ ...keyReq, minCaptchaScore: Number(e.target.value) })}>
                        <option value="0.3">0.3</option>
                        <option value="0.7">0.7</option>
                        <option value="0.9">0.9 (recommended)</option>
                      </select>
                    </Field>
                    <Field label="Captcha attempts">
                      <input type="number" min={1} max={5} className={`${inputCls} font-mono tabular-nums`} value={keyReq.maxCaptchaAttempts} onChange={(e) => setKeyReq({ ...keyReq, maxCaptchaAttempts: Number(e.target.value) || 3 })} />
                    </Field>
                    <Field label="Max per run">
                      <input type="number" min={1} max={500} className={`${inputCls} font-mono tabular-nums`} value={keyReq.maxPerRun} onChange={(e) => setKeyReq({ ...keyReq, maxPerRun: Number(e.target.value) || 25 })} />
                    </Field>
                  </div>
                  <Field label="Send key to" hint="Must be the inbox configured under ASIC key inbox.">
                    <input type="email" className={inputCls} value={keyReq.requestEmail} onChange={(e) => setKeyReq({ ...keyReq, requestEmail: e.target.value })} />
                  </Field>
                  <div className="grid grid-cols-[8rem_1fr] gap-4">
                    <Field label="Phone prefix">
                      <input className={`${inputCls} font-mono tabular-nums`} value={keyReq.defaultPhonePrefix} onChange={(e) => setKeyReq({ ...keyReq, defaultPhonePrefix: e.target.value })} placeholder="02" />
                    </Field>
                    <Field label="Fallback phone number" hint="Used when the sale has no mobile number.">
                      <input className={`${inputCls} font-mono tabular-nums`} value={keyReq.defaultPhoneNumber} onChange={(e) => setKeyReq({ ...keyReq, defaultPhoneNumber: e.target.value })} placeholder="12345678" />
                    </Field>
                  </div>
                  <Field label="Enquiry text" hint="Placeholders: {FirstName} {LastName} {Abn} {BusinessName} {Email}.">
                    <textarea
                      rows={3}
                      className={`${inputCls} font-mono text-xs leading-relaxed`}
                      value={keyReq.messageTemplate}
                      onChange={(e) => setKeyReq({ ...keyReq, messageTemplate: e.target.value })}
                    />
                    {keyReq.defaultMessageTemplate && keyReq.messageTemplate !== keyReq.defaultMessageTemplate ? (
                      <button
                        type="button"
                        onClick={() => setKeyReq({ ...keyReq, messageTemplate: keyReq.defaultMessageTemplate! })}
                        className="mt-1 text-xxs font-mono text-brand-700 hover:underline"
                      >
                        Use the default text
                      </button>
                    ) : null}
                  </Field>
                  <div className="flex flex-wrap items-center gap-2">
                    <button type="submit" className={submitBtnCls}>Save</button>
                    <button
                      type="button"
                      onClick={testKeyReq}
                      disabled={keyReqTestMutation.isPending}
                      className="inline-flex justify-center rounded-md bg-white text-zinc-800 px-3 py-2 text-sm font-medium shadow-sm ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 disabled:opacity-50 disabled:cursor-not-allowed transition"
                    >
                      {keyReqTestMutation.isPending ? 'Testing…' : 'Test settings'}
                    </button>
                  </div>
                  {keyReqTest ? <AsicKeyRequestTestResults result={keyReqTest} /> : null}
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

function defaultAsicKeyInbox(): AsicKeyInboxSettings {
  return {
    enabled: true, imapHost: 'imap.gmail.com', imapPort: 993, username: '', password: '',
    folder: 'INBOX', subjectFilter: 'Notification request', lookbackDays: 30,
    ontraportFieldId: '', asicKeyPattern: '',
  }
}

function defaultAsicKeyRequest(): AsicKeyRequestSettings {
  return {
    enabled: false, autoRequestOnSync: true, twoCaptchaApiKey: '', minCaptchaScore: 0.9, maxCaptchaAttempts: 3,
    requestEmail: 'businessnames@idealbusiness.com.au', defaultPhonePrefix: '02', defaultPhoneNumber: '',
    messageTemplate: '', maxPerRun: 25,
  }
}

function AsicKeyRequestTestResults({ result }: { result: AsicKeyRequestTestResult }) {
  const { captcha, score, template, email } = result
  return (
    <div className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 divide-y divide-zinc-200 text-sm">
      <TestRow ok={captcha.ok} label="2Captcha">
        {captcha.ok ? <>Key accepted; balance <span className="font-mono tabular-nums">${captcha.balance.toFixed(2)}</span>.</> : captcha.error}
      </TestRow>
      <TestRow ok={score.ok} label="Captcha score">
        {score.ok ? <>Requesting <span className="font-mono">{score.minScore}</span>, above ASIC's 0.5 minimum.</> : score.error}
      </TestRow>
      <TestRow ok={template.ok} label="Enquiry text">
        {template.ok ? <>“{template.sample}”</> : template.error}
      </TestRow>
      <TestRow ok={email.ok} label="Reply-to inbox">
        {email.ok ? <>Keys go to <span className="font-mono">{email.email}</span>, which the inbox scanner reads.</> : email.error}
      </TestRow>
    </div>
  )
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
