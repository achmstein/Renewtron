// The payment step's logic, apart from React and Stripe's widgets so it can be tested.
import type { BatchRenewalResponse, BusinessNameDto, LeadDto, PricingResponse } from '../api/client'

/** What the select step passed on: the chosen names (?ids=a,b) and the term (?years=1|3, default 1). */
export function parseSelection(params: URLSearchParams): { ids: string[]; years: 1 | 3 } {
  const ids = (params.get('ids') ?? '').split(',').filter(Boolean)
  const years = (Number(params.get('years')) || 1) as 1 | 3
  return { ids, years }
}

export interface OrderSummary {
  /** The chosen names the lead still lists, in the lead's order. */
  selectedNames: BusinessNameDto[]
  /** Chosen names ASIC has since said it won't renew (only counted once the lead is verified). */
  droppedCount: number
  /** One name's price for the term; 0 until pricing loads. */
  pricePerItem: number
  total: number
}

export function orderSummary(lead: LeadDto | null | undefined, ids: string[], years: 1 | 3, pricing: PricingResponse | null | undefined): OrderSummary {
  const selectedNames = (lead?.businessNames ?? []).filter((b) => ids.includes(b.id))
  // Names from our copy of the register that ASIC then said it won't renew.
  const droppedCount = lead?.verified ? ids.length - selectedNames.length : 0
  const pricePerItem = pricing ? (years === 1 ? pricing.oneYearFee : pricing.threeYearFee) : 0
  return { selectedNames, droppedCount, pricePerItem, total: pricePerItem * selectedNames.length }
}

/** Test customers pay with Stripe's test key; everyone else with the live one. */
export function stripeKeyFor(lead: Pick<LeadDto, 'testMode' | 'testStripePublishableKey'> | null | undefined, liveKey: string | null): string | null {
  return lead?.testMode ? (lead.testStripePublishableKey || null) : liveKey
}

/** ASIC still renews every chosen name: the lead's outcome allows it and each id is still listed. */
export function namesStillRenewable(confirmed: LeadDto, ids: string[]): boolean {
  const stillAvailable = new Set(confirmed.businessNames.map((b) => b.id))
  return confirmed.outcome === 'RenewalAvailable' && ids.every((id) => stillAvailable.has(id))
}

export const NOT_RENEWABLE_MESSAGE =
  "ASIC can't renew one or more of these business names right now, so you haven't been charged. " +
  'Please go back to your selection to review it.'

export interface PaymentRequest {
  leadId: string
  ids: string[]
  years: 1 | 3
  cardholderName: string
}

export type CreateBatchRenewalBody = { leadId: string; searchResultIds: string[]; renewalYears: 1 | 3; paymentMethodId: string; cardholderName?: string }
export type CompleteBatchRenewalBody = { leadId: string; searchResultIds: string[]; renewalYears: 1 | 3; paymentIntentId: string; cardholderName?: string }

/** The outside world the payment talks to: Stripe, the lead check and Renewtron's API. */
export interface PaymentSteps {
  /** Creates the card's payment method and returns its id; throws the card's error. */
  createPaymentMethod: () => Promise<string>
  /** Resolves once ASIC has confirmed the names; payment must not be taken before. */
  waitUntilVerified: () => Promise<LeadDto>
  createBatchRenewal: (body: CreateBatchRenewalBody) => Promise<BatchRenewalResponse>
  /** Stripe's 3D Secure challenge. */
  handleNextAction: (clientSecret: string) => Promise<{ error?: { message?: string }; paymentIntent?: { id: string; status: string } }>
  completeBatchRenewal: (body: CompleteBatchRenewalBody) => Promise<{ renewalIds: string[]; portalSignInUrl?: string | null }>
  /** What to tell the customer while it works; the first call means the card was accepted. */
  onStatus: (status: string) => void
  /** Just before the charge is asked for. */
  onCharging: () => void
  sleep?: (ms: number) => Promise<void>
}

const wait = (ms: number) => new Promise<void>((r) => setTimeout(r, ms))

/**
 * Takes the payment and creates the renewals: card → ASIC confirms the names → charge →
 * (3D Secure, then completion, retried) → the renewal ids. Throws a customer-facing message.
 */
export async function payForRenewals(req: PaymentRequest, steps: PaymentSteps): Promise<{ renewalIds: string[]; portalSignInUrl?: string | null }> {
  const sleep = steps.sleep ?? wait
  const paymentMethodId = await steps.createPaymentMethod()

  // Names listed from our copy of the register are confirmed with ASIC in the
  // background; usually done by now, otherwise wait here before charging.
  steps.onStatus('Confirming your business names with ASIC...')
  const confirmed = await steps.waitUntilVerified()
  if (!namesStillRenewable(confirmed, req.ids)) throw new Error(NOT_RENEWABLE_MESSAGE)

  steps.onStatus('Creating renewal requests...')
  steps.onCharging()
  const result = await steps.createBatchRenewal({
    leadId: req.leadId,
    searchResultIds: req.ids,
    renewalYears: req.years,
    paymentMethodId,
    cardholderName: req.cardholderName,
  })

  if (result.requiresAction && result.clientSecret) {
    // The bank wants 3D Secure. Stripe pops its challenge over this page; nothing
    // has been charged or recorded until it passes and the server verifies it.
    steps.onStatus('Verifying your card with your bank...')
    const next = await steps.handleNextAction(result.clientSecret)
    if (next.error) throw new Error(next.error.message ?? 'Card authentication failed.')
    if (next.paymentIntent?.status !== 'succeeded')
      throw new Error('Card authentication was not completed. You have not been charged — please try again.')

    steps.onStatus('Creating renewal requests...')
    // The card is charged at this point. The completion call is idempotent
    // server-side, so retry transient failures instead of telling the customer
    // to pay again.
    let completed: { renewalIds: string[]; portalSignInUrl?: string | null } | null = null
    let completeError: unknown = null
    for (let attempt = 0; attempt < 3 && !completed; attempt++) {
      try {
        completed = await steps.completeBatchRenewal({
          leadId: req.leadId,
          searchResultIds: req.ids,
          renewalYears: req.years,
          paymentIntentId: next.paymentIntent.id,
          cardholderName: req.cardholderName,
        })
      } catch (e) {
        completeError = e
        await sleep(1500)
      }
    }
    if (!completed) {
      const reason = completeError instanceof Error ? ` (${completeError.message})` : ''
      throw new Error(
        `Your payment went through, but we couldn't finish setting up the renewal${reason}. ` +
        `Please do not pay again — contact support and quote payment reference ${next.paymentIntent.id}.`,
      )
    }
    steps.onStatus('Scheduling renewal processing...')
    return { renewalIds: completed.renewalIds, portalSignInUrl: completed.portalSignInUrl }
  }

  steps.onStatus('Scheduling renewal processing...')
  return { renewalIds: result.renewalIds, portalSignInUrl: result.portalSignInUrl }
}
