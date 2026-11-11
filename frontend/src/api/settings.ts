import { apiFetch } from './http'
import { type AsicDeclareAs } from './asicPortal'

// Admin → Settings: every section and its save/test calls.
export interface OntraportSettingsBody {
  apiAppId: string
  apiKey: string
  conversationId: string
  pushWizardContacts: boolean
  wizardLeadTagId: string | null
  wizardPaidTagId: string | null
}

export interface TestModeSettings {
  testEmails: string
  stripeSecretKey: string
  stripePublishableKey: string
}

export interface AirwallexSettings {
  clientId: string
  apiKey: string
  useSandbox: boolean
  /** Pay ASIC's BPAY bills on their own (renewals and new names). */
  autoPayBpay?: boolean
  /** The most sent in one day (AEST), counting manual payments. */
  autoPayDailyLimit?: number
}

export interface RegistrationSettings {
  enabled: boolean
  lodge: boolean
  oneYearPrice: number
  threeYearPrice: number
  allowManualDecision: boolean
}

export interface AsicPortalSettings {
  enabled: boolean
  username: string
  password: string
  businessEmail: string
  keepRepresentatives: string
  declareAs: AsicDeclareAs
  ontraportLinkFieldId: string
  wrongKeyCampaignId: string
}

export interface TestModeData { leads: number; renewals: number; asicKeyRequests: number }

export interface AsicKeyInboxSettings {
  enabled: boolean
  imapHost: string
  imapPort: number
  username: string
  password: string
  folder: string
  subjectFilter: string
  lookbackDays: number
  ontraportFieldId: string
  asicKeyPattern: string
  /** Read-only: the code default, so the UI can offer a way back to it. */
  defaultAsicKeyPattern?: string
}

export interface AsicKeyInboxTestResult {
  mailbox:
    | { ok: true; host: string; username: string; folder: string; messagesInFolder: number; matchingInLookback: number; latestSubject: string | null; latestReceivedAt: string | null }
    | { ok: false; error: string }
  ontraportField:
    | { ok: true; fieldId: string; alias: string }
    | { ok: false; error: string }
  keyPattern:
    | { ok: true; sampleKey: string }
    | { ok: false; error: string }
}

export interface AsicKeyRequestSettings {
  enabled: boolean
  autoRequestOnSync: boolean
  requestEmail: string
  defaultPhonePrefix: string
  defaultPhoneNumber: string
  messageTemplate: string
  /** Read-only: the code default, so the UI can offer a way back to it. */
  defaultMessageTemplate?: string
}

export interface TrackingSettings {
  gtmContainerId: string
  ga4MeasurementId: string
  metaPixelId: string
  clarityProjectId: string
}

/** Nightly due-date lookups on ASIC's business name register. Masked secrets ("••••abcd")
 *  are sent back unchanged to keep the stored value. */
export interface AsicRegistrySettings {
  enabled: boolean
  maxLookupsPerRun: number
  recheckAfterDays: number
  recheckWhenDueWithinDays: number
  consecutiveFailureLimit: number
  twoCaptchaApiKey: string
  proxyUrl: string
  proxyUsername: string
  proxyPassword: string
}

export interface AsicRegistryTestResult { ok: boolean; balance: number | null; error: string | null }

export const settingsAdminApi = {
  settings: () => apiFetch<{
    sendGrid: { apiKey: string; fromEmail: string; fromName: string; eventWebhookPublicKey?: string }
    stripe: { secretKey: string; publishableKey: string; webhookSecret?: string }
    pricing: { oneYearFee: number; threeYearFee: number }
    asic: { forceFallback: boolean; allowPayEarly: boolean; email: string; cardNumber: string; cardholderName: string; expiryMonth: string; expiryYear: string; cvc: string; hasCvc: boolean; paymentMethod: 'Card' | 'Bpay'; bpayAfterCardFailures: number; oneYearFee: number; threeYearFee: number }
    airwallex: AirwallexSettings
    ontraport: OntraportSettingsBody
    winBack: { subject: string; bodyPlain: string; bodyHtml: string }
    tracking: TrackingSettings
    asicKeyInbox: AsicKeyInboxSettings
    asicKeyRequest: AsicKeyRequestSettings
    asicPortal: AsicPortalSettings
    registration: RegistrationSettings
    testMode: TestModeSettings & { stripeReady: boolean }
    asicRegistry: AsicRegistrySettings
  }>('/api/admin/settings'),
  updateAsicRegistry: (body: AsicRegistrySettings) => apiFetch<void>('/api/admin/settings/asic-registry', { method: 'PUT', body: JSON.stringify(body) }),
  /** Checks a 2Captcha key (a masked value checks the stored one) and returns the account balance. */
  testAsicRegistry: (twoCaptchaApiKey: string) =>
    apiFetch<AsicRegistryTestResult>('/api/admin/settings/asic-registry/test', { method: 'POST', body: JSON.stringify({ twoCaptchaApiKey }) }),
  updateAsicPortalSettings: (body: AsicPortalSettings) => apiFetch<void>('/api/admin/settings/asic-portal', { method: 'PUT', body: JSON.stringify(body) }),
  updateRegistrationSettings: (body: RegistrationSettings) => apiFetch<void>('/api/admin/settings/registration', { method: 'PUT', body: JSON.stringify(body) }),
  testAsicPortalLogin: () => apiFetch<{ ok: boolean; message: string }>('/api/admin/settings/asic-portal/test', { method: 'POST' }),
  updatePricing: (body: { oneYearFee: number; threeYearFee: number }) => apiFetch<void>('/api/admin/settings/pricing', { method: 'PUT', body: JSON.stringify(body) }),
  testSendGridEmail: (to?: string) => apiFetch<{ ok: boolean; message: string }>('/api/admin/settings/sendgrid/test-email', { method: 'POST', body: JSON.stringify({ to: to ?? null }) }),
  updateSendGrid: (body: { apiKey: string; fromEmail: string; fromName: string; eventWebhookPublicKey?: string }) => apiFetch<void>('/api/admin/settings/sendgrid', { method: 'PUT', body: JSON.stringify(body) }),
  updateStripe: (body: { secretKey: string; publishableKey: string; webhookSecret: string }) => apiFetch<void>('/api/admin/settings/stripe', { method: 'PUT', body: JSON.stringify(body) }),
  updateAsic: (body: { forceFallback: boolean; allowPayEarly: boolean; email: string; cardNumber: string; cardholderName: string; expiryMonth: string; expiryYear: string; cvc: string; paymentMethod: 'Card' | 'Bpay'; bpayAfterCardFailures: number; oneYearFee: number; threeYearFee: number }) => apiFetch<void>('/api/admin/settings/asic', { method: 'PUT', body: JSON.stringify(body) }),
  updateOntraport: (body: OntraportSettingsBody) => apiFetch<void>('/api/admin/settings/ontraport', { method: 'PUT', body: JSON.stringify(body) }),
  updateTracking: (body: TrackingSettings) => apiFetch<void>('/api/admin/settings/tracking', { method: 'PUT', body: JSON.stringify(body) }),
  updateAsicKeyInbox: (body: AsicKeyInboxSettings) => apiFetch<void>('/api/admin/settings/asic-key-inbox', { method: 'PUT', body: JSON.stringify(body) }),
  testAsicKeyInbox: (body: AsicKeyInboxSettings) => apiFetch<AsicKeyInboxTestResult>('/api/admin/settings/asic-key-inbox/test', { method: 'POST', body: JSON.stringify(body) }),
  updateAsicKeyRequest: (body: AsicKeyRequestSettings) => apiFetch<void>('/api/admin/settings/asic-key-request', { method: 'PUT', body: JSON.stringify(body) }),
  updateAirwallex: (body: AirwallexSettings) => apiFetch<void>('/api/admin/settings/airwallex', { method: 'PUT', body: JSON.stringify(body) }),
  testAirwallex: () => apiFetch<{ ok: boolean }>('/api/admin/settings/airwallex/test', { method: 'POST' }),
  updateTestMode: (body: TestModeSettings) => apiFetch<void>('/api/admin/settings/test-mode', { method: 'PUT', body: JSON.stringify(body) }),
  testModeData: () => apiFetch<TestModeData>('/api/admin/test-mode/data'),
  deleteTestModeData: () => apiFetch<TestModeData>('/api/admin/test-mode/data', { method: 'DELETE' }),
}
