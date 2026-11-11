import { describe, expect, it, vi } from 'vitest'
import type { BusinessNameDto, LeadDto } from '../api/client'
import {
  NOT_RENEWABLE_MESSAGE, namesStillRenewable, orderSummary, parseSelection, payForRenewals, stripeKeyFor,
  type PaymentSteps,
} from './payment'

const name = (id: string): BusinessNameDto => ({ id, businessName: `Name ${id}`, accountNumber: `A${id}`, registrationDate: '2020-01-01' })

const lead = (overrides: Partial<LeadDto> = {}): LeadDto => ({
  id: 'lead-1', abn: '51824753556', fullName: 'Jo Citizen', email: 'j•••@example.com', mobileNumber: '04•••',
  outcome: 'RenewalAvailable', verified: true, businessNames: [name('a'), name('b'), name('c')],
  ...overrides,
})

const pricing = { oneYearFee: 129, threeYearFee: 299 }

describe('parseSelection', () => {
  it('reads the chosen ids and term', () => {
    expect(parseSelection(new URLSearchParams('ids=a,b&years=3'))).toEqual({ ids: ['a', 'b'], years: 3 })
  })

  it('defaults to one year and ignores empty ids', () => {
    expect(parseSelection(new URLSearchParams('ids=a,,b,'))).toEqual({ ids: ['a', 'b'], years: 1 })
    expect(parseSelection(new URLSearchParams('years=abc'))).toEqual({ ids: [], years: 1 })
  })
})

describe('orderSummary', () => {
  it('prices each chosen name for the term', () => {
    const s = orderSummary(lead(), ['a', 'c'], 1, pricing)
    expect(s.selectedNames.map((n) => n.id)).toEqual(['a', 'c'])
    expect(s.pricePerItem).toBe(129)
    expect(s.total).toBe(258)
    expect(s.droppedCount).toBe(0)
  })

  it('uses the three-year fee for a three-year term', () => {
    expect(orderSummary(lead(), ['a', 'b', 'c'], 3, pricing).total).toBe(897)
  })

  it('is free until pricing loads, and empty until the lead loads', () => {
    expect(orderSummary(lead(), ['a'], 1, null)).toMatchObject({ pricePerItem: 0, total: 0 })
    expect(orderSummary(undefined, ['a'], 1, pricing)).toMatchObject({ selectedNames: [], total: 0, droppedCount: 0 })
  })

  it('counts names ASIC dropped only once the lead is verified', () => {
    expect(orderSummary(lead(), ['a', 'gone'], 1, pricing)).toMatchObject({ droppedCount: 1, total: 129 })
    expect(orderSummary(lead({ verified: false }), ['a', 'gone'], 1, pricing).droppedCount).toBe(0)
  })
})

describe('stripeKeyFor', () => {
  it('uses the live key for real customers and the test key for test customers', () => {
    expect(stripeKeyFor(lead(), 'pk_live')).toBe('pk_live')
    expect(stripeKeyFor(lead({ testMode: true, testStripePublishableKey: 'pk_test' }), 'pk_live')).toBe('pk_test')
    expect(stripeKeyFor(lead({ testMode: true, testStripePublishableKey: '' }), 'pk_live')).toBeNull()
    expect(stripeKeyFor(null, null)).toBeNull()
  })
})

describe('namesStillRenewable', () => {
  it('needs the renewal available and every chosen name still listed', () => {
    expect(namesStillRenewable(lead(), ['a', 'b'])).toBe(true)
    expect(namesStillRenewable(lead(), ['a', 'x'])).toBe(false)
    expect(namesStillRenewable(lead({ outcome: 'NotDueForRenewal' }), ['a'])).toBe(false)
  })
})

describe('payForRenewals', () => {
  const req = { leadId: 'lead-1', ids: ['a', 'b'], years: 3 as const, cardholderName: 'Jo Citizen' }

  const steps = (overrides: Partial<PaymentSteps> = {}): PaymentSteps & { statuses: string[] } => {
    const statuses: string[] = []
    return {
      statuses,
      createPaymentMethod: vi.fn(async () => 'pm_1'),
      waitUntilVerified: vi.fn(async () => lead()),
      createBatchRenewal: vi.fn(async () => ({ renewalIds: ['r1', 'r2'], total: 598, portalSignInUrl: 'https://portal/x' })),
      handleNextAction: vi.fn(async () => ({ paymentIntent: { id: 'pi_1', status: 'succeeded' } })),
      completeBatchRenewal: vi.fn(async () => ({ renewalIds: ['r1', 'r2'], portalSignInUrl: null })),
      onStatus: (s) => statuses.push(s),
      onCharging: vi.fn(),
      sleep: vi.fn(async () => {}),
      ...overrides,
    }
  }

  it('charges the card and returns the renewals', async () => {
    const s = steps()
    await expect(payForRenewals(req, s)).resolves.toEqual({ renewalIds: ['r1', 'r2'], portalSignInUrl: 'https://portal/x' })
    expect(s.createBatchRenewal).toHaveBeenCalledWith({
      leadId: 'lead-1', searchResultIds: ['a', 'b'], renewalYears: 3, paymentMethodId: 'pm_1', cardholderName: 'Jo Citizen',
    })
    expect(s.onCharging).toHaveBeenCalledOnce()
    expect(s.handleNextAction).not.toHaveBeenCalled()
    expect(s.statuses).toEqual(['Confirming your business names with ASIC...', 'Creating renewal requests...', 'Scheduling renewal processing...'])
  })

  it('stops on a card error before anything else', async () => {
    const s = steps({ createPaymentMethod: async () => { throw new Error('Your card number is incomplete.') } })
    await expect(payForRenewals(req, s)).rejects.toThrow('Your card number is incomplete.')
    expect(s.waitUntilVerified).not.toHaveBeenCalled()
    expect(s.statuses).toEqual([])
  })

  it('never charges when ASIC no longer renews a chosen name', async () => {
    const s = steps({ waitUntilVerified: async () => lead({ businessNames: [name('a')] }) })
    await expect(payForRenewals(req, s)).rejects.toThrow(NOT_RENEWABLE_MESSAGE)
    expect(s.onCharging).not.toHaveBeenCalled()
    expect(s.createBatchRenewal).not.toHaveBeenCalled()
  })

  describe('3D Secure', () => {
    const needsAction = async () => ({ renewalIds: [], total: 598, requiresAction: true, clientSecret: 'cs_1' })

    it('runs the challenge, then completes with the payment intent', async () => {
      const s = steps({ createBatchRenewal: needsAction })
      await expect(payForRenewals(req, s)).resolves.toEqual({ renewalIds: ['r1', 'r2'], portalSignInUrl: null })
      expect(s.handleNextAction).toHaveBeenCalledWith('cs_1')
      expect(s.completeBatchRenewal).toHaveBeenCalledWith({
        leadId: 'lead-1', searchResultIds: ['a', 'b'], renewalYears: 3, paymentIntentId: 'pi_1', cardholderName: 'Jo Citizen',
      })
      expect(s.statuses).toContain('Verifying your card with your bank...')
    })

    it('reports a failed or unfinished challenge without completing', async () => {
      const failed = steps({ createBatchRenewal: needsAction, handleNextAction: async () => ({ error: { message: 'Authentication failed.' } }) })
      await expect(payForRenewals(req, failed)).rejects.toThrow('Authentication failed.')
      const unfinished = steps({ createBatchRenewal: needsAction, handleNextAction: async () => ({ paymentIntent: { id: 'pi_1', status: 'requires_action' } }) })
      await expect(payForRenewals(req, unfinished)).rejects.toThrow('Card authentication was not completed')
      expect(unfinished.completeBatchRenewal).not.toHaveBeenCalled()
    })

    it('retries completion, since the card is already charged', async () => {
      const complete = vi.fn()
        .mockRejectedValueOnce(new Error('timeout'))
        .mockResolvedValueOnce({ renewalIds: ['r9'] })
      const s = steps({ createBatchRenewal: needsAction, completeBatchRenewal: complete })
      await expect(payForRenewals(req, s)).resolves.toEqual({ renewalIds: ['r9'], portalSignInUrl: undefined })
      expect(complete).toHaveBeenCalledTimes(2)
      expect(s.sleep).toHaveBeenCalledWith(1500)
    })

    it('after three failures, tells the customer not to pay again and quotes the payment', async () => {
      const complete = vi.fn().mockRejectedValue(new Error('server down'))
      const s = steps({ createBatchRenewal: needsAction, completeBatchRenewal: complete })
      const error = await payForRenewals(req, s).catch((e: Error) => e)
      expect(complete).toHaveBeenCalledTimes(3)
      expect((error as Error).message).toMatch(/Your payment went through.*\(server down\).*do not pay again.*pi_1/)
    })
  })
})
