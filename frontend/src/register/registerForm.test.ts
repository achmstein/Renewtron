import { describe, expect, it } from 'vitest'
import { abnOk, emptyDraft, firstIncompleteStep, toInput, validateStep, type RegisterDraft } from './registerForm'

const filled = (): RegisterDraft => ({
  ...emptyDraft(),
  businessName: '  Harbour   Light Plumbing ',
  abn: '51 824 753 556',
  firstName: 'Jo',
  lastName: 'Citizen',
  email: 'jo@example.com',
  phone: '0412 345 678',
  service: { unit: '', streetNumber: '12', streetName: 'Martin', streetType: 'ST', suburb: 'Sydney', state: 'NSW', postcode: '2000' },
})

describe('abnOk', () => {
  it('accepts a real ABN and rejects a wrong check digit or length', () => {
    expect(abnOk('51824753556')).toBe(true)
    expect(abnOk('51824753557')).toBe(false)
    expect(abnOk('5182475355')).toBe(false)
  })
})

describe('validateStep', () => {
  it('checks only the fields on that step', () => {
    const d = emptyDraft()
    expect(Object.keys(validateStep(1, d))).toEqual(['businessName'])
    expect(Object.keys(validateStep(2, d))).toEqual(['abn'])
    expect(Object.keys(validateStep(3, d)).sort()).toEqual(['email', 'firstName', 'lastName', 'phone'])
  })

  it('only checks the principal address when it differs from the service address', () => {
    const d = filled()
    expect(validateStep(4, d)).toEqual({})
    expect(Object.keys(validateStep(4, { ...d, samePrincipal: false }))).toContain('principal.streetNumber')
  })

  it('wants a four-digit postcode and no double quotes', () => {
    const d = filled()
    expect(validateStep(4, { ...d, service: { ...d.service, postcode: '200' } })['service.postcode']).toBe('4 digits')
    expect(validateStep(4, { ...d, service: { ...d.service, streetName: 'Mar"tin' } })['service.streetName']).toBe('No double quotes')
  })
})

describe('firstIncompleteStep', () => {
  it('stops at the first step with errors', () => {
    expect(firstIncompleteStep(emptyDraft())).toBe(1)
    expect(firstIncompleteStep({ ...filled(), email: 'nope' })).toBe(3)
    expect(firstIncompleteStep(filled())).toBe(4)
  })

  it('never goes past the step asked for', () => {
    expect(firstIncompleteStep(filled(), 2)).toBe(2)
  })
})

describe('toInput', () => {
  it('tidies the name and ABN and drops the principal address when it is the same', () => {
    const input = toInput(filled(), { visitorId: 'v1', source: null })
    expect(input.businessName).toBe('Harbour Light Plumbing')
    expect(input.abn).toBe('51824753556')
    expect(input.principalAddress).toBeNull()
    expect(input.visitorId).toBe('v1')
  })
})
