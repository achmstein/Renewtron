import type {
  AirwallexSettings, AsicKeyInboxSettings, AsicKeyRequestSettings, AsicPortalSettings, AsicRegistrySettings,
  OntraportSettingsBody, RegistrationSettings, TestModeSettings, TrackingSettings, api,
} from '../../api/client'

/** What GET /api/admin/settings returns. */
export type SettingsData = Awaited<ReturnType<typeof api.admin.settings>>

/** The ASIC payment form: the server's values minus `hasCvc` (the CVC itself never comes back). */
export type AsicPaymentForm = Omit<SettingsData['asic'], 'hasCvc'>

/** Every section's form values; one object so the nav can show each section's status as you type. */
export interface SettingsForms {
  sendGrid: { apiKey: string; fromEmail: string; fromName: string; eventWebhookPublicKey: string }
  stripe: { secretKey: string; publishableKey: string; webhookSecret: string }
  pricing: { oneYearFee: number; threeYearFee: number }
  asic: AsicPaymentForm
  /** The CVC never comes back from the server; this flag says one is stored. */
  asicHasCvc: boolean
  airwallex: AirwallexSettings
  ontraport: OntraportSettingsBody
  tracking: TrackingSettings
  asicKeyInbox: AsicKeyInboxSettings
  asicKeyRequest: AsicKeyRequestSettings
  registration: RegistrationSettings
  asicPortal: AsicPortalSettings
  testMode: TestModeSettings
  asicRegistry: AsicRegistrySettings
}

export type ConfigStatus = 'configured' | 'partial' | 'empty'

export const STATUS_DOT: Record<ConfigStatus, string> = { configured: 'bg-success', partial: 'bg-warning', empty: 'bg-muted-foreground/40' }
export const STATUS_WORD: Record<ConfigStatus, string> = { configured: 'Configured', partial: 'Incomplete', empty: 'Not set' }

/** One settings card. */
export type CardKey =
  | 'sendgrid' | 'winback' | 'stripe' | 'airwallex' | 'pricing'
  | 'asicpayment' | 'asicregistry' | 'asicportal' | 'asickeyrequests' | 'asickeys' | 'registration'
  | 'ontraport' | 'tracking' | 'testmode'

/** One entry in the settings nav; 'asic' opens the ASIC area, which has its own tabs. */
export type SectionKey = 'sendgrid' | 'winback' | 'stripe' | 'airwallex' | 'pricing' | 'asic' | 'ontraport' | 'tracking' | 'testmode'

export type CardDef = {
  title: string
  /** One line under the card title. */
  description: string
  /** The longer explanation, on hover. */
  tip?: string
}

export const CARDS: Record<CardKey, CardDef> = {
  sendgrid: { title: 'SendGrid', description: 'Sends the site’s emails.' },
  winback: { title: 'Win-back template', description: 'Moved to Emails → Templates.' },
  stripe: { title: 'Stripe', description: 'Takes card payments on the website.' },
  airwallex: { title: 'Airwallex', description: 'Pays ASIC’s BPAY bills for renewals lodged to pay later.', tip: 'Admin → BPAY to pay. Keys: Airwallex → Settings → Developer → API keys.' },
  pricing: { title: 'Pricing', description: 'What clients pay for a renewal.' },
  asicpayment: { title: 'Payment to ASIC', description: 'How Renewtron pays ASIC for each renewal: by card or BPAY, at ASIC’s fees.' },
  asicregistry: { title: 'Business name register', description: 'Looks up each name’s real renewal due date on ASIC’s register, overnight.' },
  asicportal: { title: 'ASIC Connect login', description: 'Our ASIC Connect account, for linking clients’ business names to it.', tip: 'Used by “Link to our ASIC account” on a business name: links it with the client’s ASIC key, then lodges our business email, the holder’s email and removes every representative but ours.' },
  asickeyrequests: { title: 'ASIC key requests', description: 'Asks ASIC to email a client’s key.' },
  asickeys: { title: 'ASIC key inbox', description: 'Reads ASIC key letters from a Gmail inbox.' },
  registration: { title: 'New business names', description: 'The public register page: prices, and whether it lodges with ASIC.', tip: 'Uses the ASIC Connect login (Business Portal link tab). ASIC’s fee is paid like a renewal’s: by card (Payment to ASIC), or by BPAY when Settings say BPAY, the card is being declined or the OTP can’t be read; a card ASIC declines is switched to BPAY. Pay BPAY bills from Admin → New names (through Airwallex). With lodging off, every paid application is filled in at ASIC up to the Review page and then cancelled.' },
  ontraport: { title: 'Ontraport', description: 'Sales sync, contacts and the OTP conversation.' },
  tracking: { title: 'Tracking tags', description: 'Analytics tags on the public site.' },
  testmode: {
    title: 'Test mode',
    description: 'Test emails use the real site; no money moves and nothing reaches ASIC or Ontraport.',
    tip: 'Anyone who enters one of these emails goes through the real site, emails and Business Portal, but: pays with Stripe’s test keys (card 4242 4242 4242 4242), so no money moves; can pick any business name on the ABN, due or not; is never sent to ASIC (the renewal is marked done with a TEST- reference); is never sent to Ontraport, and their ASIC key requests stay out of the key tool; doesn’t count on the dashboard, Payments or the Renewals totals.',
  },
}

export type SectionDef = { key: SectionKey; group: string; title: string }

export const SECTIONS: SectionDef[] = [
  { key: 'sendgrid', group: 'Email', title: 'SendGrid' },
  { key: 'winback', group: 'Email', title: 'Win-back template' },
  { key: 'stripe', group: 'Payments', title: 'Stripe' },
  { key: 'airwallex', group: 'Payments', title: 'Airwallex' },
  { key: 'pricing', group: 'Payments', title: 'Pricing' },
  { key: 'asic', group: 'Integrations', title: 'ASIC' },
  { key: 'ontraport', group: 'Integrations', title: 'Ontraport' },
  { key: 'tracking', group: 'Marketing', title: 'Tracking tags' },
  { key: 'testmode', group: 'Testing', title: 'Test mode' },
]

export type AsicTabKey = 'payment' | 'register' | 'portal' | 'keys' | 'registration'

/** The ASIC area's tabs, each with the cards it shows. */
export const ASIC_TABS: Array<{ key: AsicTabKey; label: string; cards: CardKey[] }> = [
  { key: 'payment', label: 'Payment to ASIC', cards: ['asicpayment'] },
  { key: 'register', label: 'Business name register (due dates)', cards: ['asicregistry'] },
  { key: 'portal', label: 'Business Portal link', cards: ['asicportal'] },
  { key: 'keys', label: 'Key requests & inbox', cards: ['asickeyrequests', 'asickeys'] },
  { key: 'registration', label: 'Registration', cards: ['registration'] },
]

export function defaultAsicKeyRequest(): AsicKeyRequestSettings {
  return {
    enabled: false, autoRequestOnSync: true, requestEmail: 'businessnamerenewals@gmail.com',
    defaultPhonePrefix: '', defaultPhoneNumber: '', messageTemplate: '',
  }
}

export function defaultAsicKeyInbox(): AsicKeyInboxSettings {
  return {
    enabled: true, imapHost: 'imap.gmail.com', imapPort: 993, username: '', password: '',
    folder: 'INBOX', subjectFilter: 'Notification request', lookbackDays: 30,
    ontraportFieldId: '', asicKeyPattern: '',
  }
}

const defaultAirwallex = (): AirwallexSettings => ({ clientId: '', apiKey: '', useSandbox: true, autoPayBpay: true, autoPayDailyLimit: 3000 })
const defaultTracking = (): TrackingSettings => ({ gtmContainerId: '', ga4MeasurementId: '', metaPixelId: '', clarityProjectId: '' })
const defaultAsicPortal = (): AsicPortalSettings => ({ enabled: false, username: '', password: '', businessEmail: '', keepRepresentatives: '', declareAs: 'Holder', ontraportLinkFieldId: '', wrongKeyCampaignId: '' })
const defaultRegistration = (): RegistrationSettings => ({ enabled: false, lodge: false, oneYearPrice: 0, threeYearPrice: 0, allowManualDecision: false })
const defaultAsicRegistry = (): AsicRegistrySettings => ({
  enabled: false, maxLookupsPerRun: 0, recheckAfterDays: 0, recheckWhenDueWithinDays: 0, consecutiveFailureLimit: 0,
  twoCaptchaApiKey: '', proxyUrl: '', proxyUsername: '', proxyPassword: '',
})

/** The forms before the settings load (and what a section falls back to when the server leaves it out). */
export function defaultForms(): SettingsForms {
  return {
    sendGrid: { apiKey: '', fromEmail: '', fromName: '', eventWebhookPublicKey: '' },
    stripe: { secretKey: '', publishableKey: '', webhookSecret: '' },
    pricing: { oneYearFee: 0, threeYearFee: 0 },
    asic: { forceFallback: false, allowPayEarly: true, email: '', cardNumber: '', cardholderName: '', expiryMonth: '', expiryYear: '', cvc: '', paymentMethod: 'Card', bpayAfterCardFailures: 0, oneYearFee: 0, threeYearFee: 0 },
    asicHasCvc: false,
    airwallex: defaultAirwallex(),
    ontraport: { apiAppId: '', apiKey: '', conversationId: '', pushWizardContacts: true, wizardLeadTagId: '', wizardPaidTagId: '' },
    tracking: defaultTracking(),
    asicKeyInbox: defaultAsicKeyInbox(),
    asicKeyRequest: defaultAsicKeyRequest(),
    registration: defaultRegistration(),
    asicPortal: defaultAsicPortal(),
    testMode: { testEmails: '', stripeSecretKey: '', stripePublishableKey: '' },
    asicRegistry: defaultAsicRegistry(),
  }
}

/** The forms filled from what the server returned. */
export function formsFromSettings(r: SettingsData): SettingsForms {
  const { hasCvc, ...asic } = r.asic
  return {
    sendGrid: { ...r.sendGrid, eventWebhookPublicKey: r.sendGrid.eventWebhookPublicKey ?? '' },
    stripe: { ...r.stripe, webhookSecret: r.stripe.webhookSecret ?? '' },
    pricing: r.pricing,
    asic,
    asicHasCvc: hasCvc,
    airwallex: r.airwallex ?? defaultAirwallex(),
    ontraport: r.ontraport,
    tracking: r.tracking ?? defaultTracking(),
    asicKeyInbox: r.asicKeyInbox ?? defaultAsicKeyInbox(),
    asicKeyRequest: r.asicKeyRequest ?? defaultAsicKeyRequest(),
    asicPortal: r.asicPortal ?? defaultAsicPortal(),
    registration: r.registration ?? defaultRegistration(),
    testMode: r.testMode
      ? { testEmails: r.testMode.testEmails, stripeSecretKey: r.testMode.stripeSecretKey, stripePublishableKey: r.testMode.stripePublishableKey }
      : { testEmails: '', stripeSecretKey: '', stripePublishableKey: '' },
    asicRegistry: r.asicRegistry ?? defaultAsicRegistry(),
  }
}

const isFilled = (s: string | undefined | null) => !!s && s.trim().length > 0

/** All filled → configured, none → empty, otherwise partial. */
const countStatus = (filled: number, of: number): ConfigStatus => filled === of ? 'configured' : filled === 0 ? 'empty' : 'partial'

/** Configured-status per card, from the form's current values (which start as what the server returned). */
export function cardStatus(f: SettingsForms): Record<CardKey, ConfigStatus> {
  const { sendGrid: sg, stripe, pricing, asic, airwallex, ontraport, tracking, asicKeyInbox, testMode, asicPortal, registration, asicKeyRequest, asicRegistry } = f
  return {
    sendgrid: countStatus([sg.apiKey, sg.fromEmail, sg.fromName].filter(isFilled).length, 3),
    winback: 'configured',
    stripe: countStatus([stripe.secretKey, stripe.publishableKey].filter(isFilled).length, 2),
    pricing: pricing.oneYearFee > 0 && pricing.threeYearFee > 0 ? 'configured' : pricing.oneYearFee > 0 || pricing.threeYearFee > 0 ? 'partial' : 'empty',
    asicpayment: countStatus(
      [asic.email, asic.cardNumber, asic.cardholderName, asic.expiryMonth, asic.expiryYear].filter(isFilled).length
        + (isFilled(asic.cvc) || f.asicHasCvc ? 1 : 0),
      6),
    ontraport: countStatus([ontraport.apiAppId, ontraport.apiKey, ontraport.conversationId].filter(isFilled).length, 3),
    tracking: countStatus([tracking.gtmContainerId, tracking.ga4MeasurementId, tracking.metaPixelId, tracking.clarityProjectId].filter(isFilled).length, 4),
    asickeys: countStatus([asicKeyInbox.username, asicKeyInbox.password, asicKeyInbox.ontraportFieldId].filter(isFilled).length, 3),
    testmode: (() => {
      const keys = testMode.stripeSecretKey.trim() !== '' && testMode.stripePublishableKey.trim() !== ''
      return keys && isFilled(testMode.testEmails) ? 'configured' : keys || isFilled(testMode.testEmails) ? 'partial' : 'empty'
    })(),
    airwallex: isFilled(airwallex.clientId) && isFilled(airwallex.apiKey) ? 'configured' : isFilled(airwallex.clientId) || isFilled(airwallex.apiKey) ? 'partial' : 'empty',
    asicportal: asicPortal.enabled && isFilled(asicPortal.username) && isFilled(asicPortal.password) ? 'configured' : isFilled(asicPortal.username) || isFilled(asicPortal.password) ? 'partial' : 'empty',
    registration: registration.enabled && registration.oneYearPrice > 0 && registration.threeYearPrice > 0 ? 'configured' : registration.oneYearPrice > 0 || registration.threeYearPrice > 0 ? 'partial' : 'empty',
    asickeyrequests: asicKeyRequest.enabled && isFilled(asicKeyRequest.requestEmail) ? 'configured' : isFilled(asicKeyRequest.requestEmail) ? 'partial' : 'empty',
    asicregistry: asicRegistry.enabled && isFilled(asicRegistry.twoCaptchaApiKey) ? 'configured' : asicRegistry.enabled || isFilled(asicRegistry.twoCaptchaApiKey) ? 'partial' : 'empty',
  }
}

/** Several cards as one: configured when all are, not set when none are, otherwise incomplete. */
export function combinedStatus(statuses: ConfigStatus[]): ConfigStatus {
  if (statuses.every((s) => s === 'configured')) return 'configured'
  if (statuses.every((s) => s === 'empty')) return 'empty'
  return 'partial'
}

/** Configured-status per nav entry. */
export function sectionStatus(cards: Record<CardKey, ConfigStatus>): Record<SectionKey, ConfigStatus> {
  return {
    sendgrid: cards.sendgrid, winback: cards.winback, stripe: cards.stripe, airwallex: cards.airwallex, pricing: cards.pricing,
    asic: combinedStatus(ASIC_TABS.flatMap((t) => t.cards).map((k) => cards[k])),
    ontraport: cards.ontraport, tracking: cards.tracking, testmode: cards.testmode,
  }
}
