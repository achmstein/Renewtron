import GridBackground from '../components/GridBackground'
import WizardProgress from '../components/WizardProgress'
import { STATES, STREET_TYPES, type RegistrationAddress } from '../api/registrations'
import { registerSteps } from './registerForm'

import { inputClass } from './styles'

export function Shell({ step, onStepClick, children }: {
  step: number
  onStepClick?: (step: number) => void
  children: React.ReactNode
}) {
  return (
    <div className="relative isolate flex-1 overflow-auto">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-4 pb-24 pt-10 sm:px-6 sm:pb-32 lg:px-8 lg:py-12">
        <WizardProgress currentStep={step} steps={registerSteps} onStepClick={onStepClick} />
        {children}
      </div>
    </div>
  )
}

export function StepHeading({ title, children }: { title: string; children?: React.ReactNode }) {
  return (
    <div className="mx-auto max-w-2xl text-center">
      <h1 className="text-2xl font-bold tracking-tight text-gray-900 sm:text-3xl">{title}</h1>
      {children ? <p className="mt-3 text-base leading-7 text-gray-600">{children}</p> : null}
    </div>
  )
}

export function Section({ title, hint, children }: { title?: string; hint?: string; children: React.ReactNode }) {
  return (
    <fieldset className="space-y-4 rounded-xl bg-white p-5 shadow-sm ring-1 ring-gray-200 sm:p-6">
      {title ? <legend className="sr-only">{title}</legend> : null}
      {title || hint ? (
        <div>
          {title ? <h2 className="text-base font-semibold text-gray-900">{title}</h2> : null}
          {hint ? <p className="mt-1 text-sm text-gray-500">{hint}</p> : null}
        </div>
      ) : null}
      {children}
    </fieldset>
  )
}

export function Field({ label, error, hint, htmlFor, children }: {
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

export function Note({ tone, children }: { tone: 'good' | 'bad'; children: React.ReactNode }) {
  return (
    <div className={`rounded-md p-3 text-sm ${tone === 'good' ? 'bg-green-50 text-green-800' : 'bg-red-50 text-red-800'}`}>{children}</div>
  )
}

/** Continue (submit) with Back underneath, as on the renewal wizard. */
export function StepActions({ label, busy, disabled, onBack }: {
  label: string; busy?: boolean; disabled?: boolean; onBack?: () => void
}) {
  return (
    <div className="space-y-4 pt-2">
      <button
        type="submit"
        disabled={busy || disabled}
        className="flex w-full items-center justify-center rounded-md bg-brand px-4 py-3 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-brand-dark disabled:cursor-not-allowed disabled:opacity-50"
      >
        {label}
        {!busy ? (
          <svg className="ml-2 h-5 w-5" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor" aria-hidden="true">
            <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
          </svg>
        ) : null}
      </button>
      {onBack ? (
        <div className="text-center">
          <button type="button" onClick={onBack} className="text-sm font-medium text-gray-600 hover:text-gray-500">
            ← Back
          </button>
        </div>
      ) : null}
    </div>
  )
}

export function AddressFields({ value, onChange, errors, prefix }: {
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
