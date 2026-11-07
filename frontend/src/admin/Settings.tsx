import { useEffect, useState, type ReactNode } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Info } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type AirwallexSettings, type AsicKeyInboxSettings, type AsicKeyInboxTestResult, type AsicKeyRequestSettings, type OntraportSettingsBody, type TestModeData, type TestModeSettings } from '../api/client'
import { PageHeader } from './_ui'
import { relativeTime } from './_utils'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { cn } from '@/lib/utils'

type SectionKey = 'sendgrid' | 'winback' | 'stripe' | 'airwallex' | 'pricing' | 'asic' | 'ontraport' | 'asickeys' | 'asickeyrequests' | 'tracking' | 'testmode'

type Settings = Awaited<ReturnType<typeof api.admin.settings>>

type SectionDef = {
  key: SectionKey
  group: string
  title: string
  /** One line under the card title. */
  description: string
  /** The longer explanation, on hover. */
  tip?: string
}

const SECTIONS: SectionDef[] = [
  { key: 'sendgrid',  group: 'Email', title: 'SendGrid', description: 'Sends the site’s emails.' },
  { key: 'winback',   group: 'Email', title: 'Win-back template', description: 'The email sent to leads who didn’t pay.' },
  { key: 'stripe',    group: 'Payments', title: 'Stripe', description: 'Takes card payments on the website.' },
  { key: 'airwallex', group: 'Payments', title: 'Airwallex', description: 'Pays ASIC’s BPAY bills for renewals lodged to pay later.', tip: 'Admin → BPAY to pay. Keys: Airwallex → Settings → Developer → API keys.' },
  { key: 'pricing',   group: 'Payments', title: 'Pricing', description: 'What clients pay for a renewal.' },
  { key: 'asic',      group: 'Integrations', title: 'ASIC credentials', description: 'How Renewtron pays ASIC for each renewal.' },
  { key: 'ontraport', group: 'Integrations', title: 'Ontraport', description: 'Sales sync, contacts and the OTP conversation.' },
  { key: 'asickeys',  group: 'Integrations', title: 'ASIC key inbox', description: 'Reads ASIC key letters from a Gmail inbox.' },
  { key: 'asickeyrequests', group: 'Integrations', title: 'ASIC key requests', description: 'Asks ASIC to email a client’s key.' },
  { key: 'tracking',  group: 'Marketing', title: 'Tracking tags', description: 'Analytics tags on the public site.' },
  {
    key: 'testmode', group: 'Testing', title: 'Test mode',
    description: 'Test emails use the real site; no money moves and nothing reaches ASIC or Ontraport.',
    tip: 'Anyone who enters one of these emails goes through the real site, emails and Business Portal, but: pays with Stripe’s test keys (card 4242 4242 4242 4242), so no money moves; can pick any business name on the ABN, due or not; is never sent to ASIC (the renewal is marked done with a TEST- reference); is never sent to Ontraport, and their ASIC key requests stay out of the key tool; doesn’t count on the dashboard, Payments or the Renewals totals.',
  },
]

export default function Settings() {
  const [activeKey, setActiveKey] = useState<SectionKey>('sendgrid')
  const [data, setData] = useState<Settings | null>(null)

  // Form state mirrors the original separate models
  const [sg, setSg] = useState({ apiKey: '', fromEmail: '', fromName: '' })
  const [stripe, setStripe] = useState({ secretKey: '', publishableKey: '', webhookSecret: '' })
  const [pricing, setPricing] = useState({ oneYearFee: 0, threeYearFee: 0 })
  const [asic, setAsic] = useState<{ forceFallback: boolean; allowPayEarly: boolean; email: string; cardNumber: string; cardholderName: string; expiryMonth: string; expiryYear: string; cvc: string; paymentMethod: 'Card' | 'Bpay'; bpayAfterCardFailures: number; oneYearFee: number; threeYearFee: number }>({ forceFallback: false, allowPayEarly: true, email: '', cardNumber: '', cardholderName: '', expiryMonth: '', expiryYear: '', cvc: '', paymentMethod: 'Card', bpayAfterCardFailures: 0, oneYearFee: 0, threeYearFee: 0 })
  const [airwallex, setAirwallex] = useState<AirwallexSettings>({ clientId: '', apiKey: '', useSandbox: true })
  // The CVC never comes back from the server; this flag says one is stored.
  const [asicHasCvc, setAsicHasCvc] = useState(false)
  const [ontraport, setOntraport] = useState<OntraportSettingsBody>({ apiAppId: '', apiKey: '', conversationId: '', pushWizardContacts: true, wizardLeadTagId: '', wizardPaidTagId: '' })
  const [winBack, setWinBack] = useState({ subject: '', bodyPlain: '', bodyHtml: '' })
  const [tracking, setTracking] = useState({ gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '', clarityProjectId: '' })
  const [asicKeys, setAsicKeys] = useState<AsicKeyInboxSettings>(defaultAsicKeyInbox())
  const [asicKeysTest, setAsicKeysTest] = useState<AsicKeyInboxTestResult | null>(null)
  const [keyRequests, setKeyRequests] = useState<AsicKeyRequestSettings>(defaultAsicKeyRequest())
  const [testMode, setTestMode] = useState<TestModeSettings>({ testEmails: '', stripeSecretKey: '', stripePublishableKey: '' })
  const [testData, setTestData] = useState<TestModeData | null>(null)

  const load = async () => {
    const r = await api.admin.settings()
    setData(r)
    setSg(r.sendGrid)
    setStripe({ ...r.stripe, webhookSecret: r.stripe.webhookSecret ?? '' })
    setPricing(r.pricing)
    const { hasCvc, ...asicFields } = r.asic
    setAsic(asicFields)
    setAsicHasCvc(hasCvc)
    setAirwallex(r.airwallex ?? { clientId: '', apiKey: '', useSandbox: true })
    setOntraport(r.ontraport)
    setWinBack(r.winBack ?? { subject: '', bodyPlain: '', bodyHtml: '' })
    setTracking(r.tracking ?? { gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '', clarityProjectId: '' })
    setAsicKeys(r.asicKeyInbox ?? defaultAsicKeyInbox())
    setKeyRequests(r.asicKeyRequest ?? defaultAsicKeyRequest())
    if (r.testMode) setTestMode({ testEmails: r.testMode.testEmails, stripeSecretKey: r.testMode.stripeSecretKey, stripePublishableKey: r.testMode.stripePublishableKey })
    void api.admin.testModeData().then(setTestData).catch(() => {})
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
  const onAirwallex = save('Airwallex', () => api.admin.updateAirwallex(airwallex))
  const testAirwallex = () => {
    void sileo.promise(api.admin.testAirwallex(), {
      loading: { title: 'Logging in to Airwallex…' },
      success: () => ({ title: 'Airwallex keys work' }),
      error: (e) => ({ title: 'Airwallex login failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }
  const onAsic      = save('ASIC', () => api.admin.updateAsic(asic).then(() => { if (asic.cvc) setAsicHasCvc(true) }))
  const onOntraport = save('Ontraport', () => api.admin.updateOntraport(ontraport))
  const onWinBack   = save('Win-back template', () => api.admin.updateWinBack(winBack))
  const onTracking  = save('Tracking tags', () => api.admin.updateTracking(tracking))
  const onAsicKeys  = save('ASIC key inbox', () => api.admin.updateAsicKeyInbox(asicKeys))
  const onKeyRequests = save('ASIC key requests', () => api.admin.updateAsicKeyRequest(keyRequests))
  const onTestMode  = save('Test mode', () => api.admin.updateTestMode(testMode))

  const deleteTestData = () => {
    if (!window.confirm('Delete every test lead, renewal and ASIC key request? Real customers are not touched.')) return
    void sileo.promise(api.admin.deleteTestModeData(), {
      loading: { title: 'Deleting test data…' },
      success: (r) => {
        void api.admin.testModeData().then(setTestData).catch(() => {})
        return { title: 'Test data deleted', description: `${r.leads} lead(s), ${r.renewals} renewal(s), ${r.asicKeyRequests} key request(s).` }
      },
      error: (err) => ({ title: 'Could not delete test data', description: err instanceof Error ? err.message : undefined }),
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
      const total = [tracking.gtmContainerId, tracking.ga4MeasurementId, tracking.metaPixelId, tracking.clarityProjectId].filter(isFilled).length
      return total === 0 ? 'empty' : total === 4 ? 'configured' : 'partial'
    })(),
    asickeys: (() => {
      const total = [asicKeys.username, asicKeys.password, asicKeys.ontraportFieldId].filter(isFilled).length
      return total === 3 ? 'configured' : total === 0 ? 'empty' : 'partial'
    })(),
    testmode: (() => {
      const keys = testMode.stripeSecretKey.trim() !== '' && testMode.stripePublishableKey.trim() !== ''
      return keys && isFilled(testMode.testEmails) ? 'configured' : keys || isFilled(testMode.testEmails) ? 'partial' : 'empty'
    })(),
    airwallex: isFilled(airwallex.clientId) && isFilled(airwallex.apiKey) ? 'configured' : isFilled(airwallex.clientId) || isFilled(airwallex.apiKey) ? 'partial' : 'empty',
    asickeyrequests: keyRequests.enabled && isFilled(keyRequests.requestEmail) ? 'configured' : isFilled(keyRequests.requestEmail) ? 'partial' : 'empty',
  }

  // Group sections for sidebar nav
  const groups = SECTIONS.reduce<Record<string, SectionDef[]>>((acc, s) => {
    if (!acc[s.group]) acc[s.group] = []
    acc[s.group].push(s)
    return acc
  }, {})

  const active = SECTIONS.find((s) => s.key === activeKey)!
  const card = { title: active.title, description: active.description, tip: active.tip, status: status[active.key] }

  return (
    <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <PageHeader title="Settings" subtitle="Integrations, payments and pricing." />

      <div className="grid grid-cols-1 items-start gap-6 lg:grid-cols-4">
        {/* Phones and tablets: the section list is a dropdown, so the form stays at the top of the screen. */}
        <Select value={activeKey} onValueChange={(v) => setActiveKey(v as SectionKey)}>
          <SelectTrigger aria-label="Settings section" className="w-full bg-card lg:hidden">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {Object.entries(groups).map(([group, sections]) => (
              <SelectGroup key={group}>
                <SelectLabel>{group}</SelectLabel>
                {sections.map((s) => (
                  <SelectItem key={s.key} value={s.key}>
                    {s.title}
                    <ConfiguredDot status={status[s.key]} />
                  </SelectItem>
                ))}
              </SelectGroup>
            ))}
          </SelectContent>
        </Select>

        {/* Sidebar nav */}
        <Card className="hidden gap-0 py-2 lg:flex">
          <nav className="space-y-3">
            {Object.entries(groups).map(([group, sections]) => (
              <div key={group} className="px-2">
                <div className="px-2 py-1 text-caption text-muted-foreground">{group}</div>
                {sections.map((s) => {
                  const isActive = s.key === activeKey
                  return (
                    <Button
                      key={s.key}
                      type="button"
                      variant="ghost"
                      size="sm"
                      aria-current={isActive ? 'page' : undefined}
                      onClick={() => setActiveKey(s.key)}
                      className={cn('w-full justify-between font-normal', isActive && 'bg-muted font-medium')}
                    >
                      <span className="truncate">{s.title}</span>
                      <ConfiguredDot status={status[s.key]} />
                    </Button>
                  )
                })}
              </div>
            ))}
          </nav>
        </Card>

        {/* Active section */}
        <div className="min-w-0 space-y-6 lg:col-span-3">
          {!data ? (
            <Skeleton className="h-80 rounded-xl" />
          ) : null}

          {data && activeKey === 'sendgrid' ? (
            <SettingsCard {...card} onSubmit={onSendGrid}>
              <Field label="API key">
                <Input value={sg.apiKey} onChange={(e) => setSg({ ...sg, apiKey: e.target.value })} />
              </Field>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Field label="From email">
                  <Input value={sg.fromEmail} onChange={(e) => setSg({ ...sg, fromEmail: e.target.value })} />
                </Field>
                <Field label="From name">
                  <Input value={sg.fromName} onChange={(e) => setSg({ ...sg, fromName: e.target.value })} />
                </Field>
              </div>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'winback' ? (
            <SettingsCard {...card} onSubmit={onWinBack} saveLabel="Save template">
              <Field label="Subject" hint="Merge tags: {{FullName}}, {{Abn}}, {{Email}}, {{BusinessName}}">
                <Input value={winBack.subject} onChange={(e) => setWinBack({ ...winBack, subject: e.target.value })} />
              </Field>
              <Field label="Body (plain text)">
                <Textarea rows={10} className="font-mono" value={winBack.bodyPlain} onChange={(e) => setWinBack({ ...winBack, bodyPlain: e.target.value })} />
              </Field>
              <Field label="Body (HTML, optional)">
                <Textarea rows={6} className="font-mono" value={winBack.bodyHtml} onChange={(e) => setWinBack({ ...winBack, bodyHtml: e.target.value })} />
              </Field>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'stripe' ? (
            <SettingsCard {...card} onSubmit={onStripe}>
              <Field label="Secret key">
                <Input type="password" className="font-mono" value={stripe.secretKey} onChange={(e) => setStripe({ ...stripe, secretKey: e.target.value })} />
              </Field>
              <Field label="Publishable key">
                <Input className="font-mono" value={stripe.publishableKey} onChange={(e) => setStripe({ ...stripe, publishableKey: e.target.value })} />
              </Field>
              <Field
                label="Dispute webhook signing secret"
                hint="Starts whsec_. New disputes get Renewtron’s evidence staged."
                tip="Stripe dashboard → Developers → Webhooks: add an endpoint at this site's /api/stripe/webhook for the charge.dispute.created event, then paste its signing secret (whsec_…). New disputes get Renewtron's evidence staged for Smart Disputes."
              >
                <Input type="password" className="font-mono" value={stripe.webhookSecret} onChange={(e) => setStripe({ ...stripe, webhookSecret: e.target.value })} />
              </Field>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'airwallex' ? (
            <SettingsCard
              {...card}
              onSubmit={onAirwallex}
              extra={<Button type="button" variant="outline" onClick={testAirwallex}>Test saved keys</Button>}
            >
              <Field label="Environment" hint={airwallex.useSandbox ? 'Payments are simulated; use sandbox keys.' : 'Payments really leave your Airwallex AUD balance; use live keys.'}>
                <Select value={airwallex.useSandbox ? 'sandbox' : 'live'} onValueChange={(v) => setAirwallex({ ...airwallex, useSandbox: v === 'sandbox' })}>
                  <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="sandbox">Sandbox (no money moves)</SelectItem>
                    <SelectItem value="live">Live</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
              <Field label="Client ID">
                <Input className="font-mono" value={airwallex.clientId} onChange={(e) => setAirwallex({ ...airwallex, clientId: e.target.value })} />
              </Field>
              <Field label="API key">
                <Input type="password" className="font-mono" value={airwallex.apiKey} onChange={(e) => setAirwallex({ ...airwallex, apiKey: e.target.value })} />
              </Field>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'pricing' ? (
            <SettingsCard {...card} onSubmit={onPricing}>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Field label="1-year renewal price">
                  <MoneyInput value={pricing.oneYearFee} onChange={(n) => setPricing({ ...pricing, oneYearFee: n })} />
                </Field>
                <Field label="3-year renewal price">
                  <MoneyInput value={pricing.threeYearFee} onChange={(n) => setPricing({ ...pricing, threeYearFee: n })} />
                </Field>
              </div>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'asic' ? (
            <SettingsCard {...card} onSubmit={onAsic}>
              <Field
                label="Pay ASIC by"
                hint={asic.paymentMethod === 'Bpay' ? 'Lodged to pay later; pay the BPAY bill, then mark it paid.' : 'Paid at ASIC straight away with the card below.'}
                tip={asic.paymentMethod === 'Bpay'
                  ? 'Each renewal is lodged with ASIC to pay later. Its BPAY biller code and reference are saved on the renewal — pay them at the bank, then mark it paid.'
                  : 'Each renewal is paid at ASIC straight away with the card below (needs the SMS code).'}
              >
                <Select value={asic.paymentMethod} onValueChange={(v) => setAsic({ ...asic, paymentMethod: v as 'Card' | 'Bpay' })}>
                  <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Card">Credit card now</SelectItem>
                    <SelectItem value="Bpay">Pay later by BPAY</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
              {asic.paymentMethod === 'Card' ? (
                <Field
                  label="Switch to BPAY after"
                  hint={asic.bpayAfterCardFailures > 0 ? 'Then the renewal is lodged to pay later by BPAY.' : 'Renewals are only ever paid by card.'}
                  tip={asic.bpayAfterCardFailures > 0
                    ? `After ${asic.bpayAfterCardFailures} failed card ${asic.bpayAfterCardFailures === 1 ? 'attempt' : 'attempts'} on a renewal (declined, or no SMS code), it's lodged to pay later by BPAY instead. Card retries are an hour apart.`
                    : undefined}
                >
                  <Select value={String(asic.bpayAfterCardFailures)} onValueChange={(v) => setAsic({ ...asic, bpayAfterCardFailures: Number(v) })}>
                    <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="0">Never</SelectItem>
                      {[1, 2, 3, 4, 5].map((n) => <SelectItem key={n} value={String(n)}>{n} failed card {n === 1 ? 'attempt' : 'attempts'}</SelectItem>)}
                    </SelectContent>
                  </Select>
                </Field>
              ) : null}
              <Field
                label="Names not due yet"
                hint={asic.allowPayEarly ? 'Customers can pay now; Renewtron lodges it when ASIC opens the renewal.' : 'The renewal site stops at “not due for renewal yet”.'}
                tip="ASIC only takes a renewal in the 29 days before the name is due. With paying early on, the renewal site lists the names anyway and takes payment; each morning Renewtron asks ASIC again and lodges the renewal as soon as ASIC lists the name. The customer is never told the name isn't due."
              >
                <Select value={asic.allowPayEarly ? 'yes' : 'no'} onValueChange={(v) => setAsic({ ...asic, allowPayEarly: v === 'yes' })}>
                  <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="yes">Let customers pay early</SelectItem>
                    <SelectItem value="no">Stop at “not due yet”</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
              <Field label="Email">
                <Input type="email" value={asic.email} onChange={(e) => setAsic({ ...asic, email: e.target.value })} />
              </Field>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Field label="Card number">
                  <Input className="font-mono tabular-nums" value={asic.cardNumber} onChange={(e) => setAsic({ ...asic, cardNumber: e.target.value })} />
                </Field>
                <Field label="Cardholder name">
                  <Input value={asic.cardholderName} onChange={(e) => setAsic({ ...asic, cardholderName: e.target.value })} />
                </Field>
              </div>
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <Field label="Expiry month">
                  <Input className="font-mono tabular-nums" value={asic.expiryMonth} onChange={(e) => setAsic({ ...asic, expiryMonth: e.target.value })} />
                </Field>
                <Field label="Expiry year">
                  <Input className="font-mono tabular-nums" value={asic.expiryYear} onChange={(e) => setAsic({ ...asic, expiryYear: e.target.value })} />
                </Field>
                <Field label="CVC" hint={asicHasCvc ? 'Stored. Leave blank to keep it.' : undefined}>
                  <Input type="password" className="font-mono tabular-nums" value={asic.cvc} onChange={(e) => setAsic({ ...asic, cvc: e.target.value })} placeholder={asicHasCvc ? '•••' : ''} />
                </Field>
              </div>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Field
                  label="ASIC fee, 1 year (A$)"
                  hint="Update when ASIC’s fees change (usually 1 July)."
                  tip="What ASIC charges for a 1-year renewal. BPAY bills are paid this amount through Airwallex. Update it when ASIC's fees change (usually 1 July)."
                >
                  <Input type="number" min="0" step="0.01" className="font-mono tabular-nums" value={asic.oneYearFee || ''} onChange={(e) => setAsic({ ...asic, oneYearFee: Number(e.target.value) || 0 })} />
                </Field>
                <Field label="ASIC fee, 3 years (A$)" tip="What ASIC charges for a 3-year renewal.">
                  <Input type="number" min="0" step="0.01" className="font-mono tabular-nums" value={asic.threeYearFee || ''} onChange={(e) => setAsic({ ...asic, threeYearFee: Number(e.target.value) || 0 })} />
                </Field>
              </div>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'ontraport' ? (
            <SettingsCard {...card} onSubmit={onOntraport}>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Field label="API app ID">
                  <Input className="font-mono" value={ontraport.apiAppId} onChange={(e) => setOntraport({ ...ontraport, apiAppId: e.target.value })} />
                </Field>
                <Field label="API key">
                  <Input className="font-mono" value={ontraport.apiKey} onChange={(e) => setOntraport({ ...ontraport, apiKey: e.target.value })} />
                </Field>
              </div>
              <Field label="Conversation ID">
                <Input className="font-mono" value={ontraport.conversationId} onChange={(e) => setOntraport({ ...ontraport, conversationId: e.target.value })} />
              </Field>
              <SwitchField id="op-push" label="Add wizard users to Ontraport" description="Create or update a contact for everyone who uses the renewal wizard." checked={ontraport.pushWizardContacts} onCheckedChange={(v) => setOntraport({ ...ontraport, pushWizardContacts: v })} />
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Field label="Wizard form tag ID">
                  <Input className="font-mono" value={ontraport.wizardLeadTagId ?? ''} onChange={(e) => setOntraport({ ...ontraport, wizardLeadTagId: e.target.value })} placeholder="1908" />
                </Field>
                <Field label="Wizard paid tag ID">
                  <Input className="font-mono" value={ontraport.wizardPaidTagId ?? ''} onChange={(e) => setOntraport({ ...ontraport, wizardPaidTagId: e.target.value })} placeholder="1737" />
                </Field>
              </div>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'asickeys' ? (
            <SettingsCard
              {...card}
              onSubmit={onAsicKeys}
              extra={
                <Button type="button" variant="outline" onClick={testAsicKeys} disabled={testMutation.isPending}>
                  {testMutation.isPending ? 'Testing…' : 'Test connection'}
                </Button>
              }
              after={asicKeysTest ? <AsicKeyTestResults result={asicKeysTest} /> : null}
            >
              <SwitchField id="ak-enabled" label="Scanning enabled" description="Read new ASIC key letters from this inbox." checked={asicKeys.enabled} onCheckedChange={(v) => setAsicKeys({ ...asicKeys, enabled: v })} />
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-4">
                <div className="sm:col-span-3">
                  <Field label="IMAP host">
                    <Input className="font-mono" value={asicKeys.imapHost} onChange={(e) => setAsicKeys({ ...asicKeys, imapHost: e.target.value })} placeholder="imap.gmail.com" />
                  </Field>
                </div>
                <Field label="Port">
                  <Input type="number" className="font-mono tabular-nums" value={asicKeys.imapPort} onChange={(e) => setAsicKeys({ ...asicKeys, imapPort: Number(e.target.value) || 993 })} />
                </Field>
              </div>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Field label="Gmail address">
                  <Input type="email" value={asicKeys.username} onChange={(e) => setAsicKeys({ ...asicKeys, username: e.target.value })} />
                </Field>
                <Field label="App password">
                  <Input type="password" className="font-mono" value={asicKeys.password} onChange={(e) => setAsicKeys({ ...asicKeys, password: e.target.value })} />
                </Field>
              </div>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <Field label="Folder or label">
                  <Input className="font-mono" value={asicKeys.folder} onChange={(e) => setAsicKeys({ ...asicKeys, folder: e.target.value })} placeholder="INBOX" />
                </Field>
                <Field label="Subject contains">
                  <Input value={asicKeys.subjectFilter} onChange={(e) => setAsicKeys({ ...asicKeys, subjectFilter: e.target.value })} />
                </Field>
                <Field label="Lookback (days)">
                  <Input type="number" min={1} max={365} className="font-mono tabular-nums" value={asicKeys.lookbackDays} onChange={(e) => setAsicKeys({ ...asicKeys, lookbackDays: Number(e.target.value) || 30 })} />
                </Field>
              </div>
              <Field label="Ontraport ASIC key field">
                <Input className="font-mono" value={asicKeys.ontraportFieldId} onChange={(e) => setAsicKeys({ ...asicKeys, ontraportFieldId: e.target.value })} placeholder="f5xxx" />
              </Field>
              <Field label="ASIC key pattern">
                <Input className="font-mono" value={asicKeys.asicKeyPattern} onChange={(e) => setAsicKeys({ ...asicKeys, asicKeyPattern: e.target.value })} />
                {asicKeys.defaultAsicKeyPattern && asicKeys.asicKeyPattern !== asicKeys.defaultAsicKeyPattern ? (
                  <LinkAction onClick={() => setAsicKeys({ ...asicKeys, asicKeyPattern: asicKeys.defaultAsicKeyPattern! })}>Use the current default pattern</LinkAction>
                ) : null}
              </Field>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'asickeyrequests' ? (
            <SettingsCard {...card} onSubmit={onKeyRequests}>
              <SwitchField id="kr-enabled" label="Requests enabled" description="Queue ASIC key requests for the key tool." checked={keyRequests.enabled} onCheckedChange={(v) => setKeyRequests({ ...keyRequests, enabled: v })} />
              <SwitchField id="kr-auto" label="Request on Ontraport sync" description="Queue a request for every eligible sale the sync brings in." checked={keyRequests.autoRequestOnSync} onCheckedChange={(v) => setKeyRequests({ ...keyRequests, autoRequestOnSync: v })} />
              <Field label="Email the key to">
                <Input type="email" value={keyRequests.requestEmail} onChange={(e) => setKeyRequests({ ...keyRequests, requestEmail: e.target.value })} />
              </Field>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <Field label="Area code">
                  <Input className="font-mono tabular-nums" value={keyRequests.defaultPhonePrefix} onChange={(e) => setKeyRequests({ ...keyRequests, defaultPhonePrefix: e.target.value })} placeholder="02" />
                </Field>
                <div className="sm:col-span-2">
                  <Field label="Fallback phone">
                    <Input className="font-mono tabular-nums" value={keyRequests.defaultPhoneNumber} onChange={(e) => setKeyRequests({ ...keyRequests, defaultPhoneNumber: e.target.value })} />
                  </Field>
                </div>
              </div>
              <Field label="Enquiry text">
                <Textarea rows={3} className="font-mono" value={keyRequests.messageTemplate} onChange={(e) => setKeyRequests({ ...keyRequests, messageTemplate: e.target.value })} />
                {keyRequests.defaultMessageTemplate && keyRequests.messageTemplate !== keyRequests.defaultMessageTemplate ? (
                  <LinkAction onClick={() => setKeyRequests({ ...keyRequests, messageTemplate: keyRequests.defaultMessageTemplate! })}>Use the default wording</LinkAction>
                ) : null}
              </Field>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'tracking' ? (
            <SettingsCard {...card} onSubmit={onTracking}>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <Field label="Google Tag Manager container">
                  <Input className="font-mono" value={tracking.gtmContainerId} onChange={(e) => setTracking({ ...tracking, gtmContainerId: e.target.value })} placeholder="GTM-XXXXXXX" />
                </Field>
                <Field label="GA4 measurement ID">
                  <Input className="font-mono" value={tracking.ga4MeasurementId} onChange={(e) => setTracking({ ...tracking, ga4MeasurementId: e.target.value })} placeholder="G-XXXXXXXXXX" />
                </Field>
                <Field label="Meta pixel ID">
                  <Input className="font-mono" value={tracking.metaPixelId} onChange={(e) => setTracking({ ...tracking, metaPixelId: e.target.value })} placeholder="1234567890" />
                </Field>
                <Field label="Microsoft Clarity project ID">
                  <Input className="font-mono" value={tracking.clarityProjectId} onChange={(e) => setTracking({ ...tracking, clarityProjectId: e.target.value.trim() })} placeholder="abcd1234ef" />
                </Field>
              </div>
            </SettingsCard>
          ) : null}

          {data && activeKey === 'testmode' ? (
            <>
              <SettingsCard {...card} onSubmit={onTestMode}>
                <Field label="Test customer emails" hint="One per line, e.g. you+test@gmail.com. Never a real client’s address.">
                  <Textarea rows={3} className="font-mono" value={testMode.testEmails} onChange={(e) => setTestMode({ ...testMode, testEmails: e.target.value })} />
                </Field>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <Field label="Stripe test secret key" hint="Starts sk_test_." tip="Stripe dashboard → Developers → API keys, with 'Test mode' switched on. Starts sk_test_.">
                    <Input type="password" className="font-mono" value={testMode.stripeSecretKey} onChange={(e) => setTestMode({ ...testMode, stripeSecretKey: e.target.value })} />
                  </Field>
                  <Field label="Stripe test publishable key" hint="Starts pk_test_.">
                    <Input className="font-mono" value={testMode.stripePublishableKey} onChange={(e) => setTestMode({ ...testMode, stripePublishableKey: e.target.value })} />
                  </Field>
                </div>
              </SettingsCard>
              <Card>
                <CardHeader>
                  <CardTitle>Test data</CardTitle>
                  <CardDescription title="The Business Portal keeps its own copy of the test account.">
                    {testData
                      ? `${testData.leads} lead(s), ${testData.renewals} renewal(s), ${testData.asicKeyRequests} key request(s) on file.`
                      : 'Loading test data…'}
                  </CardDescription>
                  <CardAction>
                    <Button type="button" variant="outline" size="sm" onClick={deleteTestData}>Delete test data</Button>
                  </CardAction>
                </CardHeader>
              </Card>
            </>
          ) : null}
        </div>
      </div>
    </div>
  )
}

function defaultAsicKeyRequest(): AsicKeyRequestSettings {
  return {
    enabled: false, autoRequestOnSync: true, requestEmail: 'businessnamerenewals@gmail.com',
    defaultPhonePrefix: '', defaultPhoneNumber: '', messageTemplate: '',
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
    <div className="divide-y rounded-lg border text-sm">
      <TestRow ok={mailbox.ok} label="Mailbox">
        {mailbox.ok ? (
          <>
            Signed in to <span className="font-mono">{mailbox.host}</span> as <span className="font-mono">{mailbox.username}</span>.{' '}
            <span className="font-mono">{mailbox.folder}</span> holds {mailbox.messagesInFolder.toLocaleString()} messages;{' '}
            <span className="font-medium tabular-nums">{mailbox.matchingInLookback}</span> match the subject filter in the lookback window
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
    <div className="flex items-start gap-3 px-4 py-3">
      <span aria-hidden className={cn('mt-1.5 size-2 shrink-0 rounded-full', ok ? 'bg-success' : 'bg-destructive')} />
      <div className="min-w-0">
        <div className="font-medium">{label} <span className="font-normal text-muted-foreground">· {ok ? 'passed' : 'failed'}</span></div>
        <div className="mt-0.5 break-words text-caption text-muted-foreground">{children}</div>
      </div>
    </div>
  )
}

type ConfigStatus = 'configured' | 'partial' | 'empty'

const STATUS_DOT: Record<ConfigStatus, string> = { configured: 'bg-success', partial: 'bg-warning', empty: 'bg-muted-foreground/40' }
const STATUS_WORD: Record<ConfigStatus, string> = { configured: 'Configured', partial: 'Incomplete', empty: 'Not set' }

function ConfiguredDot({ status }: { status: ConfigStatus }) {
  return <span aria-label={STATUS_WORD[status]} title={STATUS_WORD[status]} className={cn('size-1.5 shrink-0 rounded-full', STATUS_DOT[status])} />
}

/** One settings section: title, one line, the fields, and Save on the right. */
function SettingsCard({ title, description, tip, status, onSubmit, saveLabel = 'Save', extra, after, children }: {
  title: string
  description: string
  tip?: string
  status: ConfigStatus
  onSubmit: (e: React.FormEvent) => void
  saveLabel?: string
  extra?: ReactNode
  after?: ReactNode
  children: ReactNode
}) {
  return (
    <form onSubmit={onSubmit}>
      <Card>
        <CardHeader>
          <CardTitle>{title}</CardTitle>
          <CardDescription title={tip}>
            {description}
            {tip ? <Info aria-hidden className="ml-1 inline size-3.5 -translate-y-px" /> : null}
          </CardDescription>
          <CardAction>
            <span className="flex items-center gap-1.5 text-caption text-muted-foreground">
              <span aria-hidden className={cn('size-2 rounded-full', STATUS_DOT[status])} />
              {STATUS_WORD[status]}
            </span>
          </CardAction>
        </CardHeader>
        <CardContent className="space-y-4">
          {children}
          {after}
        </CardContent>
        <CardFooter className="justify-end gap-2 border-t">
          {extra}
          <Button type="submit">{saveLabel}</Button>
        </CardFooter>
      </Card>
    </form>
  )
}

/** A label, its input, and at most one muted line under it; the longer explanation on hover. */
function Field({ label, hint, tip, children }: { label: string; hint?: string; tip?: string; children: ReactNode }) {
  return (
    <div className="grid content-start gap-1.5">
      <Label title={tip}>
        {label}
        {tip ? <Info aria-hidden className="size-3.5 text-muted-foreground" /> : null}
      </Label>
      {children}
      {hint ? <p className="text-caption text-muted-foreground">{hint}</p> : null}
    </div>
  )
}

/** An on/off setting: label and one line on the left, the switch on the right. */
function SwitchField({ id, label, description, checked, onCheckedChange }: {
  id: string
  label: string
  description: string
  checked: boolean
  onCheckedChange: (v: boolean) => void
}) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-lg border px-4 py-3">
      <div className="grid gap-0.5">
        <Label htmlFor={id}>{label}</Label>
        <p className="text-caption text-muted-foreground">{description}</p>
      </div>
      <Switch id={id} checked={checked} onCheckedChange={onCheckedChange} />
    </div>
  )
}

/** A dollar amount input. */
function MoneyInput({ value, onChange }: { value: number; onChange: (n: number) => void }) {
  return (
    <div className="relative">
      <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-sm text-muted-foreground">$</span>
      <Input type="number" step="0.01" className="pl-7 font-mono tabular-nums" value={value} onChange={(e) => onChange(Number(e.target.value))} />
    </div>
  )
}

/** A small text action under a field (e.g. reset to the default). */
function LinkAction({ onClick, children }: { onClick: () => void; children: ReactNode }) {
  return (
    <Button type="button" variant="link" onClick={onClick} className="h-auto justify-self-start p-0 text-caption font-normal text-link">
      {children}
    </Button>
  )
}
