import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import GridBackground from '../components/GridBackground'
import WizardProgress from '../components/WizardProgress'
import {
  emptyAddress, registrationsApi, STATES, STREET_TYPES,
  type RegistrationAddress, type RegistrationConfig,
} from '../api/registrations'
import { capturePrefill, normalizeAbn } from '../lib/prefill'
import { getVisitorId } from '../lib/tracking'

const registerSteps = [{ label: 'Name' }, { label: 'Details' }, { label: 'Pay' }, { label: 'Done' }]

const inputBase =
  'block w-full rounded-md border-0 py-2.5 px-3 text-gray-900 shadow-sm ring-1 ring-inset placeholder:text-gray-400 focus:ring-2 focus:ring-inset sm:text-sm'
const inputClass = (err?: string) => `${inputBase} ${err ? 'ring-red-300 focus:ring-red-500' : 'ring-gray-300 focus-ring-brand'}`

/** The ABR's ABN check (weights 10,1,3…19 over the digits, first minus one; sum divisible by 89). */
function abnOk(abn: string) {
  if (!/^\d{11}$/.test(abn)) return false
  const w = [10, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19]
  const sum = abn.split('').reduce((s, d, i) => s + (Number(d) - (i === 0 ? 1 : 0)) * w[i], 0)
  return sum % 89 === 0
}

type NameCheck = { state: 'idle' } | { state: 'checking' } | { state: 'done'; name: string; taken: boolean } | { state: 'error'; message: string }

export default function RegisterPage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const prefill = useMemo(() => capturePrefill(params.toString()), [params])

  const [config, setConfig] = useState<RegistrationConfig | null>(null)
  const [configError, setConfigError] = useState('')
  useEffect(() => {
    registrationsApi.config().then(setConfig).catch(() => setConfigError('We couldn’t load this page. Please refresh and try again.'))
  }, [])

  const [businessName, setBusinessName] = useState(params.get('name') ?? '')
  const [check, setCheck] = useState<NameCheck>({ state: 'idle' })
  const [abn, setAbn] = useState(normalizeAbn(params.get('abn') ?? '') || prefill.abn)
  const [years, setYears] = useState<1 | 3>(1)
  const [firstName, setFirstName] = useState(prefill.fullName.split(' ')[0] ?? '')
  const [lastName, setLastName] = useState(prefill.fullName.split(' ').slice(1).join(' '))
  const [email, setEmail] = useState(prefill.email)
  const [phone, setPhone] = useState(prefill.mobile)
  const [service, setService] = useState<RegistrationAddress>(emptyAddress)
  const [samePrincipal, setSamePrincipal] = useState(true)
  const [principal, setPrincipal] = useState<RegistrationAddress>(emptyAddress)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [submitting, setSubmitting] = useState(false)

  const cleanName = businessName.replace(/\s+/g, ' ').trim()
  const nameChecked = check.state === 'done' && check.name === cleanName.toUpperCase()

  const runCheck = async () => {
    if (cleanName.length < 2) {
      setErrors((e) => ({ ...e, businessName: 'Enter the business name you’d like to register' }))
      return
    }
    setErrors((e) => ({ ...e, businessName: '' }))
    setCheck({ state: 'checking' })
    try {
      const r = await registrationsApi.nameCheck(cleanName)
      setCheck({ state: 'done', name: r.name, taken: r.taken })
    } catch (err) {
      setCheck({ state: 'error', message: err instanceof Error ? err.message : 'Couldn’t check the name.' })
    }
  }

  const addressErrors = (a: RegistrationAddress, prefix: string, out: Record<string, string>) => {
    if (!a.streetNumber.trim()) out[`${prefix}.streetNumber`] = 'Required'
    if (!a.streetName.trim()) out[`${prefix}.streetName`] = 'Required'
    if (!a.suburb.trim()) out[`${prefix}.suburb`] = 'Required'
    if (!a.state) out[`${prefix}.state`] = 'Required'
    if (!/^\d{4}$/.test(a.postcode.trim())) out[`${prefix}.postcode`] = '4 digits'
    if ((a.streetName + a.suburb).includes('"')) out[`${prefix}.streetName`] = 'No double quotes'
  }

  const validate = () => {
    const next: Record<string, string> = {}
    if (cleanName.length < 2) next.businessName = 'Enter the business name you’d like to register'
    else if (nameChecked && check.taken) next.businessName = 'This name is already registered. Try a different name.'
    if (!abnOk(normalizeAbn(abn))) next.abn = 'Enter a valid 11-digit ABN'
    if (!firstName.trim()) next.firstName = 'Required'
    if (!lastName.trim()) next.lastName = 'Required'
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) next.email = 'Enter a valid email address'
    if (phone.replace(/\D/g, '').length < 8) next.phone = 'Enter a phone number'
    addressErrors(service, 'service', next)
    if (!samePrincipal) addressErrors(principal, 'principal', next)
    setErrors(next)
    return Object.keys(next).length === 0
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!validate()) return
    setSubmitting(true)
    try {
      const { id } = await registrationsApi.create({
        businessName: cleanName,
        abn: normalizeAbn(abn),
        years,
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: email.trim(),
        phone: phone.trim(),
        serviceAddress: service,
        principalAddress: samePrincipal ? null : principal,
        visitorId: getVisitorId(),
        source: params.get('utm_source') ?? params.get('source'),
      })
      navigate(`/register/${id}/pay`)
    } catch (err) {
      setErrors((x) => ({ ...x, general: err instanceof Error ? err.message : 'Something went wrong. Please try again.' }))
      setSubmitting(false)
    }
  }

  const price = config ? (years === 1 ? config.oneYearPrice : config.threeYearPrice) : null

  if (configError || (config && !config.enabled)) {
    return (
      <Shell step={1}>
        <div className="mx-auto max-w-md rounded-lg bg-white p-6 text-center shadow-sm ring-1 ring-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">Registering a new business name</h2>
          <p className="mt-2 text-sm text-gray-600">
            {configError || 'This service isn’t open just yet. Please check back soon.'}
          </p>
        </div>
      </Shell>
    )
  }

  return (
    <Shell step={1}>
      <div className="mx-auto max-w-2xl text-center">
        <h1 className="text-2xl font-bold tracking-tight text-gray-900 sm:text-3xl">Register a business name</h1>
        <p className="mt-3 text-base leading-7 text-gray-600">
          Tell us the name you want and who it’s for. We lodge it with ASIC for you.
        </p>
      </div>

      <form onSubmit={submit} className="mx-auto mt-10 max-w-xl space-y-8" data-clarity-mask="true">
        <Section title="The business name">
          <Field label="Business name" error={errors.businessName} htmlFor="businessName">
            <div className="flex gap-2">
              <input
                id="businessName"
                value={businessName}
                onChange={(e) => { setBusinessName(e.target.value); setCheck({ state: 'idle' }) }}
                onBlur={() => { if (cleanName.length >= 2 && !nameChecked) void runCheck() }}
                placeholder="e.g. Harbour Light Plumbing"
                maxLength={200}
                className={inputClass(errors.businessName)}
              />
              <button
                type="button"
                onClick={() => void runCheck()}
                disabled={check.state === 'checking'}
                className="shrink-0 rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 disabled:opacity-50"
              >
                {check.state === 'checking' ? 'Checking…' : 'Check'}
              </button>
            </div>
          </Field>
          {nameChecked && check.state === 'done' ? (
            check.taken ? (
              <Note tone="bad">“{check.name}” is already registered. Try a different name.</Note>
            ) : (
              <Note tone="good">
                “{check.name}” isn’t on the register. ASIC makes the final check when we lodge it; if ASIC won’t accept it, we refund you in full.
              </Note>
            )
          ) : null}
          {check.state === 'error' ? <Note tone="bad">{check.message}</Note> : null}

          <Field label="ABN of the business name holder" error={errors.abn} htmlFor="abn"
                 hint="The person or company that will hold the name. You need an ABN to register a business name.">
            <input id="abn" inputMode="numeric" value={abn} onChange={(e) => setAbn(e.target.value)}
                   placeholder="12 345 678 901" className={inputClass(errors.abn)} />
          </Field>

          <div>
            <span className="block text-sm font-medium leading-6 text-gray-900">Registration period</span>
            <div className="mt-2 grid grid-cols-2 gap-3">
              {([1, 3] as const).map((y) => (
                <button
                  key={y}
                  type="button"
                  onClick={() => setYears(y)}
                  className={`rounded-lg px-4 py-3 text-left ring-1 ring-inset transition-colors ${
                    years === y ? 'bg-brand-light ring-brand' : 'bg-white ring-gray-300 hover:bg-gray-50'
                  }`}
                >
                  <span className="block text-sm font-semibold text-gray-900">{y} year{y === 1 ? '' : 's'}</span>
                  <span className="block text-sm text-gray-600">
                    {config ? `$${(y === 1 ? config.oneYearPrice : config.threeYearPrice).toFixed(2)}` : '…'}
                  </span>
                </button>
              ))}
            </div>
          </div>
        </Section>

        <Section title="Your details">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="First name" error={errors.firstName} htmlFor="firstName">
              <input id="firstName" value={firstName} onChange={(e) => setFirstName(e.target.value)} className={inputClass(errors.firstName)} />
            </Field>
            <Field label="Last name" error={errors.lastName} htmlFor="lastName">
              <input id="lastName" value={lastName} onChange={(e) => setLastName(e.target.value)} className={inputClass(errors.lastName)} />
            </Field>
          </div>
          <Field label="Email" error={errors.email} htmlFor="email" hint="ASIC sends its notices for the name here.">
            <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={inputClass(errors.email)} />
          </Field>
          <Field label="Mobile" error={errors.phone} htmlFor="phone">
            <input id="phone" type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="0412 345 678" className={inputClass(errors.phone)} />
          </Field>
        </Section>

        <Section title="Address for service" hint="Where ASIC can deliver official documents. It appears on the public register.">
          <AddressFields value={service} onChange={setService} errors={errors} prefix="service" />
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" checked={samePrincipal} onChange={(e) => setSamePrincipal(e.target.checked)}
                   className="h-4 w-4 rounded border-gray-300 text-brand" />
            This is also the principal place of business
          </label>
        </Section>

        {!samePrincipal ? (
          <Section title="Principal place of business" hint="A street address (not a PO box). It appears on the public register.">
            <AddressFields value={principal} onChange={setPrincipal} errors={errors} prefix="principal" />
          </Section>
        ) : null}

        {errors.general ? <Note tone="bad">{errors.general}</Note> : null}

        <button
          type="submit"
          disabled={submitting || !config || (nameChecked && check.state === 'done' && check.taken)}
          className="w-full rounded-md bg-brand px-4 py-3 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark disabled:cursor-not-allowed disabled:opacity-50"
        >
          {submitting ? 'Saving…' : price != null ? `Continue to payment · $${price.toFixed(2)}` : 'Continue to payment'}
        </button>
        <p className="text-center text-xs text-gray-500">
          The price includes ASIC’s registration fee. We lodge on behalf of the applicant, who must be eligible to hold a business name.
        </p>
      </form>
    </Shell>
  )
}

export function Shell({ step, children }: { step: number; children: React.ReactNode }) {
  return (
    <div className="relative isolate flex-1 overflow-auto">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-4 pb-24 pt-10 sm:px-6 sm:pb-32 lg:px-8 lg:py-12">
        <WizardProgress currentStep={step} steps={registerSteps} />
        {children}
      </div>
    </div>
  )
}

function Section({ title, hint, children }: { title: string; hint?: string; children: React.ReactNode }) {
  return (
    <fieldset className="space-y-4 rounded-xl bg-white p-5 shadow-sm ring-1 ring-gray-200 sm:p-6">
      <legend className="sr-only">{title}</legend>
      <div>
        <h2 className="text-base font-semibold text-gray-900">{title}</h2>
        {hint ? <p className="mt-1 text-sm text-gray-500">{hint}</p> : null}
      </div>
      {children}
    </fieldset>
  )
}

function Field({ label, error, hint, htmlFor, children }: {
  label: string; error?: string; hint?: string; htmlFor?: string; children: React.ReactNode
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="block text-sm font-medium leading-6 text-gray-900">{label}</label>
      <div className="mt-1.5">{children}</div>
      {error ? <p className="mt-1 text-sm text-red-600">{error}</p> : hint ? <p className="mt-1 text-xs text-gray-500">{hint}</p> : null}
    </div>
  )
}

function Note({ tone, children }: { tone: 'good' | 'bad'; children: React.ReactNode }) {
  return (
    <div className={`rounded-md p-3 text-sm ${tone === 'good' ? 'bg-green-50 text-green-800' : 'bg-red-50 text-red-800'}`}>{children}</div>
  )
}

function AddressFields({ value, onChange, errors, prefix }: {
  value: RegistrationAddress
  onChange: (a: RegistrationAddress) => void
  errors: Record<string, string>
  prefix: string
}) {
  const set = (k: keyof RegistrationAddress) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    onChange({ ...value, [k]: e.target.value })
  const err = (k: string) => errors[`${prefix}.${k}`]
  return (
    <div className="grid grid-cols-6 gap-3">
      <div className="col-span-2">
        <Field label="Unit" htmlFor={`${prefix}-unit`}>
          <input id={`${prefix}-unit`} value={value.unit} onChange={set('unit')} placeholder="Optional" className={inputClass()} />
        </Field>
      </div>
      <div className="col-span-4">
        <Field label="Street number" error={err('streetNumber')} htmlFor={`${prefix}-number`}>
          <input id={`${prefix}-number`} value={value.streetNumber} onChange={set('streetNumber')} placeholder="12" className={inputClass(err('streetNumber'))} />
        </Field>
      </div>
      <div className="col-span-6 sm:col-span-4">
        <Field label="Street name" error={err('streetName')} htmlFor={`${prefix}-street`}>
          <input id={`${prefix}-street`} value={value.streetName} onChange={set('streetName')} placeholder="Martin" className={inputClass(err('streetName'))} />
        </Field>
      </div>
      <div className="col-span-6 sm:col-span-2">
        <Field label="Street type" htmlFor={`${prefix}-type`}>
          <select id={`${prefix}-type`} value={value.streetType} onChange={set('streetType')} className={inputClass()}>
            {STREET_TYPES.map(([code, label]) => <option key={code} value={code}>{label}</option>)}
          </select>
        </Field>
      </div>
      <div className="col-span-6 sm:col-span-3">
        <Field label="Suburb" error={err('suburb')} htmlFor={`${prefix}-suburb`}>
          <input id={`${prefix}-suburb`} value={value.suburb} onChange={set('suburb')} className={inputClass(err('suburb'))} />
        </Field>
      </div>
      <div className="col-span-3 sm:col-span-1">
        <Field label="State" error={err('state')} htmlFor={`${prefix}-state`}>
          <select id={`${prefix}-state`} value={value.state} onChange={set('state')} className={inputClass(err('state'))}>
            <option value="">–</option>
            {STATES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </Field>
      </div>
      <div className="col-span-3 sm:col-span-2">
        <Field label="Postcode" error={err('postcode')} htmlFor={`${prefix}-postcode`}>
          <input id={`${prefix}-postcode`} inputMode="numeric" maxLength={4} value={value.postcode} onChange={set('postcode')} className={inputClass(err('postcode'))} />
        </Field>
      </div>
    </div>
  )
}
