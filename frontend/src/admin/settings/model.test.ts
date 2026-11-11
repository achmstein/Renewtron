import { describe, expect, it } from 'vitest'
import { cardStatus, combinedStatus, defaultForms, formsFromSettings, sectionStatus, type SettingsData } from './model'

describe('settings model', () => {
  it('shows every card as not set before anything is filled in (win-back always configured)', () => {
    const cards = cardStatus(defaultForms())
    expect(cards.sendgrid).toBe('empty')
    expect(cards.winback).toBe('configured')
    expect(cards.asicregistry).toBe('empty')
  })

  it('counts a stored CVC towards the ASIC payment card', () => {
    const forms = defaultForms()
    forms.asic = { ...forms.asic, email: 'a@b.c', cardNumber: '4111', cardholderName: 'X', expiryMonth: '01', expiryYear: '30' }
    expect(cardStatus(forms).asicpayment).toBe('partial')
    expect(cardStatus({ ...forms, asicHasCvc: true }).asicpayment).toBe('configured')
  })

  it('combines several cards: all configured, none set, or incomplete', () => {
    expect(combinedStatus(['configured', 'configured'])).toBe('configured')
    expect(combinedStatus(['empty', 'empty'])).toBe('empty')
    expect(combinedStatus(['configured', 'empty'])).toBe('partial')
    // Key requests start with a default email, so the ASIC area starts incomplete rather than empty.
    expect(sectionStatus(cardStatus(defaultForms())).asic).toBe('partial')
  })

  it('fills the forms from the server, defaulting sections it leaves out', () => {
    const data = {
      sendGrid: { apiKey: 'k', fromEmail: 'f', fromName: 'n' },
      stripe: { secretKey: 's', publishableKey: 'p' },
      pricing: { oneYearFee: 1, threeYearFee: 3 },
      asic: { ...defaultForms().asic, hasCvc: true },
      ontraport: defaultForms().ontraport,
    } as unknown as SettingsData
    const forms = formsFromSettings(data)
    expect(forms.sendGrid.eventWebhookPublicKey).toBe('')
    expect(forms.stripe.webhookSecret).toBe('')
    expect(forms.asicHasCvc).toBe(true)
    expect('hasCvc' in forms.asic).toBe(false)
    expect(forms.asicKeyInbox.imapHost).toBe('imap.gmail.com')
    expect(forms.asicRegistry.enabled).toBe(false)
  })
})
