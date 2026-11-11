import { emptyAddress, type RegistrationAddress, type RegistrationInput } from '../api/registrations'
import { normalizeAbn } from '../lib/prefill'

/** The steps on /register, then the pay and done pages. Numbers are 1-based, as WizardProgress counts. */
export const registerSteps = [
  { label: 'Name' }, { label: 'Holder' }, { label: 'Details' }, { label: 'Address' }, { label: 'Pay' }, { label: 'Done' },
]
export const FORM_STEPS = 4
export const PAY_STEP = 5
export const DONE_STEP = 6

/** Everything the customer types, kept for the tab so going back from payment loses nothing. */
export interface RegisterDraft {
  businessName: string
  abn: string
  years: 1 | 3
  firstName: string
  lastName: string
  email: string
  phone: string
  service: RegistrationAddress
  samePrincipal: boolean
  principal: RegistrationAddress
}

export type FieldErrors = Record<string, string>

export const emptyDraft = (): RegisterDraft => ({
  businessName: '', abn: '', years: 1, firstName: '', lastName: '', email: '', phone: '',
  service: emptyAddress(), samePrincipal: true, principal: emptyAddress(),
})

export const cleanBusinessName = (name: string) => name.replace(/\s+/g, ' ').trim()

/** The ABR's ABN check (weights 10,1,3…19 over the digits, first minus one; sum divisible by 89). */
export function abnOk(abn: string) {
  if (!/^\d{11}$/.test(abn)) return false
  const w = [10, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19]
  const sum = abn.split('').reduce((s, d, i) => s + (Number(d) - (i === 0 ? 1 : 0)) * w[i], 0)
  return sum % 89 === 0
}

function addressErrors(a: RegistrationAddress, prefix: string, out: FieldErrors) {
  if (!a.streetNumber.trim()) out[`${prefix}.streetNumber`] = 'Required'
  if (!a.streetName.trim()) out[`${prefix}.streetName`] = 'Required'
  if (!a.suburb.trim()) out[`${prefix}.suburb`] = 'Required'
  if (!a.state) out[`${prefix}.state`] = 'Required'
  if (!/^\d{4}$/.test(a.postcode.trim())) out[`${prefix}.postcode`] = '4 digits'
  if ((a.streetName + a.suburb).includes('"')) out[`${prefix}.streetName`] = 'No double quotes'
}

/** The errors on one step (1–4). The name's register check is the page's job, not this. */
export function validateStep(step: number, d: RegisterDraft): FieldErrors {
  const out: FieldErrors = {}
  switch (step) {
    case 1:
      if (cleanBusinessName(d.businessName).length < 2) out.businessName = 'Enter the business name you’d like to register'
      break
    case 2:
      if (!abnOk(normalizeAbn(d.abn))) out.abn = 'Enter a valid 11-digit ABN'
      break
    case 3:
      if (!d.firstName.trim()) out.firstName = 'Required'
      if (!d.lastName.trim()) out.lastName = 'Required'
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(d.email.trim())) out.email = 'Enter a valid email address'
      if (d.phone.replace(/\D/g, '').length < 8) out.phone = 'Enter a phone number'
      break
    case 4:
      addressErrors(d.service, 'service', out)
      if (!d.samePrincipal) addressErrors(d.principal, 'principal', out)
      break
  }
  return out
}

/** The furthest step the draft may open on: the first one with errors, else the last. */
export function firstIncompleteStep(d: RegisterDraft, upTo = FORM_STEPS): number {
  for (let s = 1; s < upTo; s++) if (Object.keys(validateStep(s, d)).length > 0) return s
  return upTo
}

export function toInput(d: RegisterDraft, extra: Pick<RegistrationInput, 'visitorId' | 'source'>): RegistrationInput {
  return {
    businessName: cleanBusinessName(d.businessName),
    abn: normalizeAbn(d.abn),
    years: d.years,
    firstName: d.firstName.trim(),
    lastName: d.lastName.trim(),
    email: d.email.trim(),
    phone: d.phone.trim(),
    serviceAddress: d.service,
    principalAddress: d.samePrincipal ? null : d.principal,
    ...extra,
  }
}

const DRAFT_KEY = 'renewtron.register-draft'

export function loadDraft(): RegisterDraft | null {
  try {
    const raw = sessionStorage.getItem(DRAFT_KEY)
    return raw ? { ...emptyDraft(), ...(JSON.parse(raw) as Partial<RegisterDraft>) } : null
  } catch {
    return null
  }
}

export function saveDraft(d: RegisterDraft) {
  try { sessionStorage.setItem(DRAFT_KEY, JSON.stringify(d)) } catch { /* private mode: the form still works */ }
}

export function clearDraft() {
  try { sessionStorage.removeItem(DRAFT_KEY) } catch { /* nothing to clear */ }
}
