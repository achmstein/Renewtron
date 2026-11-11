import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { registrationsApi, type RegistrationConfig } from '../api/registrations'
import { Skeletonizer } from '../components/skeletonizer'
import { capturePrefill, normalizeAbn } from '../lib/prefill'
import { getVisitorId } from '../lib/tracking'
import {
  cleanBusinessName, emptyDraft, firstIncompleteStep, FORM_STEPS, loadDraft, saveDraft, toInput, validateStep,
  type FieldErrors, type RegisterDraft,
} from './registerForm'
import { AddressFields, Field, Note, Section, Shell, StepActions, StepHeading } from './fields'
import { inputClass } from './styles'

type NameCheck = { state: 'idle' } | { state: 'checking' } | { state: 'done'; name: string; taken: boolean } | { state: 'error'; message: string }

/** Shown under the skeleton while the config loads, so the page never flashes a form it then hides. */
const SAMPLE_CONFIG: RegistrationConfig = { enabled: true, oneYearPrice: 0, threeYearPrice: 0, stripePublishableKey: '' }

/**
 * Registering a new business name, one short step at a time (name → holder → you → address),
 * then the pay and done pages. The step is in the URL so the browser's Back button works, and
 * the answers are kept for the tab so coming back from payment loses nothing.
 */
export default function RegisterPage() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const prefill = useMemo(() => capturePrefill(params.toString()), [params])

  const [config, setConfig] = useState<RegistrationConfig | null>(null)
  const [configError, setConfigError] = useState('')
  useEffect(() => {
    registrationsApi.config().then(setConfig).catch(() => setConfigError('We couldn’t load this page. Please refresh and try again.'))
  }, [])

  const [draft, setDraft] = useState<RegisterDraft>(() => {
    const saved = loadDraft()
    if (saved) return saved
    const d = emptyDraft()
    d.businessName = params.get('name') ?? ''
    d.abn = normalizeAbn(params.get('abn') ?? '') || prefill.abn
    d.firstName = prefill.fullName.split(' ')[0] ?? ''
    d.lastName = prefill.fullName.split(' ').slice(1).join(' ')
    d.email = prefill.email
    d.phone = prefill.mobile
    return d
  })
  useEffect(() => saveDraft(draft), [draft])
  const update = (patch: Partial<RegisterDraft>) => setDraft((d) => ({ ...d, ...patch }))

  const [errors, setErrors] = useState<FieldErrors>({})
  const [check, setCheck] = useState<NameCheck>({ state: 'idle' })
  const [submitting, setSubmitting] = useState(false)

  // Never open past the first step that isn't filled in (a bookmarked ?step=4, a fresh tab).
  const requested = Math.min(Math.max(Number(params.get('step')) || 1, 1), FORM_STEPS)
  const step = Math.min(requested, firstIncompleteStep(draft, requested))
  const goTo = (s: number) => {
    const next = new URLSearchParams(params)
    if (s <= 1) next.delete('step'); else next.set('step', String(s))
    setErrors({})
    setParams(next)
    window.scrollTo({ top: 0 })
  }

  const cleanName = cleanBusinessName(draft.businessName)
  const nameChecked = check.state === 'done' && check.name === cleanName.toUpperCase()
  const nameTaken = nameChecked && check.state === 'done' && check.taken

  const runCheck = async (): Promise<NameCheck> => {
    setCheck({ state: 'checking' })
    let result: NameCheck
    try {
      const r = await registrationsApi.nameCheck(cleanName)
      result = { state: 'done', name: r.name, taken: r.taken }
    } catch (err) {
      result = { state: 'error', message: err instanceof Error ? err.message : 'Couldn’t check the name.' }
    }
    setCheck(result)
    return result
  }

  const next = async (e: React.FormEvent) => {
    e.preventDefault()
    const found = validateStep(step, draft)
    setErrors(found)
    if (Object.keys(found).length > 0) return

    if (step === 1) {
      // A taken name stops here; if the check itself fails, ASIC still checks when we lodge.
      const result = nameChecked ? check : await runCheck()
      if (result.state === 'done' && result.taken) return
      goTo(2)
      return
    }
    if (step < FORM_STEPS) { goTo(step + 1); return }

    setSubmitting(true)
    try {
      const { id } = await registrationsApi.create(toInput(draft, {
        visitorId: getVisitorId(),
        source: params.get('utm_source') ?? params.get('source'),
      }))
      navigate(`/register/${id}/pay`)
    } catch (err) {
      setErrors({ general: err instanceof Error ? err.message : 'Something went wrong. Please try again.' })
      setSubmitting(false)
    }
  }

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

  const loading = config === null
  const shown = config ?? SAMPLE_CONFIG
  const price = (y: 1 | 3) => (y === 1 ? shown.oneYearPrice : shown.threeYearPrice)
  const back = step > 1 ? () => goTo(step - 1) : undefined

  return (
    <Shell step={step} onStepClick={goTo}>
      <Skeletonizer loading={loading}>
        <form onSubmit={next} className="mx-auto max-w-xl" data-clarity-mask="true" noValidate>
          {step === 1 ? (
            <>
              <StepHeading title="Register a business name">What name would you like? We’ll check it’s free first.</StepHeading>
              <div className="mt-10 space-y-6">
                <Section>
                  <Field label="Business name" error={errors.businessName} htmlFor="businessName">
                    <input
                      id="businessName"
                      autoFocus={!loading}
                      value={draft.businessName}
                      onChange={(e) => { update({ businessName: e.target.value }); setCheck({ state: 'idle' }) }}
                      onBlur={() => { if (cleanName.length >= 2 && !nameChecked) void runCheck() }}
                      placeholder="e.g. Harbour Light Plumbing"
                      maxLength={200}
                      className={inputClass(errors.businessName)}
                    />
                  </Field>
                  {check.state === 'checking' ? <p className="text-sm text-gray-500">Checking the register…</p> : null}
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
                </Section>
                <StepActions label={check.state === 'checking' ? 'Checking…' : 'Continue'} busy={check.state === 'checking'} disabled={nameTaken} />
              </div>
            </>
          ) : null}

          {step === 2 ? (
            <>
              <StepHeading title="Who will hold the name?">
                The person or company that holds “{cleanName}”. You need an ABN to register a business name.
              </StepHeading>
              <div className="mt-10 space-y-6">
                <Section>
                  <Field label="ABN of the business name holder" error={errors.abn} htmlFor="abn">
                    <input id="abn" autoFocus inputMode="numeric" value={draft.abn} onChange={(e) => update({ abn: e.target.value })}
                           placeholder="12 345 678 901" className={inputClass(errors.abn)} />
                  </Field>
                  <div>
                    <span className="block text-sm font-medium leading-6 text-gray-900">Registration period</span>
                    <div className="mt-2 grid grid-cols-2 gap-3">
                      {([1, 3] as const).map((y) => (
                        <button
                          key={y}
                          type="button"
                          onClick={() => update({ years: y })}
                          className={`rounded-lg px-4 py-3 text-left ring-1 ring-inset transition-colors ${
                            draft.years === y ? 'bg-brand-light ring-brand' : 'bg-white ring-gray-300 hover:bg-gray-50'
                          }`}
                        >
                          <span className="block text-sm font-semibold text-gray-900">{y} year{y === 1 ? '' : 's'}</span>
                          <span className="block text-sm text-gray-600">${price(y).toFixed(2)}</span>
                        </button>
                      ))}
                    </div>
                    <p className="mt-2 text-xs text-gray-500">The price includes ASIC’s registration fee.</p>
                  </div>
                </Section>
                <StepActions label="Continue" onBack={back} />
              </div>
            </>
          ) : null}

          {step === 3 ? (
            <>
              <StepHeading title="Your details">So we, and ASIC, can reach you about the name.</StepHeading>
              <div className="mt-10 space-y-6">
                <Section>
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <Field label="First name" error={errors.firstName} htmlFor="firstName">
                      <input id="firstName" autoFocus value={draft.firstName} onChange={(e) => update({ firstName: e.target.value })} className={inputClass(errors.firstName)} />
                    </Field>
                    <Field label="Last name" error={errors.lastName} htmlFor="lastName">
                      <input id="lastName" value={draft.lastName} onChange={(e) => update({ lastName: e.target.value })} className={inputClass(errors.lastName)} />
                    </Field>
                  </div>
                  <Field label="Email" error={errors.email} htmlFor="email" hint="ASIC sends its notices for the name here.">
                    <input id="email" type="email" value={draft.email} onChange={(e) => update({ email: e.target.value })} className={inputClass(errors.email)} />
                  </Field>
                  <Field label="Mobile" error={errors.phone} htmlFor="phone">
                    <input id="phone" type="tel" value={draft.phone} onChange={(e) => update({ phone: e.target.value })} placeholder="0412 345 678" className={inputClass(errors.phone)} />
                  </Field>
                </Section>
                <StepActions label="Continue" onBack={back} />
              </div>
            </>
          ) : null}

          {step === 4 ? (
            <>
              <StepHeading title="Business address">Where ASIC can reach the business. Both addresses appear on the public register.</StepHeading>
              <div className="mt-10 space-y-6">
                <Section title="Address for service" hint="Where ASIC can deliver official documents.">
                  <AddressFields value={draft.service} onChange={(service) => update({ service })} errors={errors} prefix="service" />
                  <label className="flex items-center gap-2 text-sm text-gray-700">
                    <input type="checkbox" checked={draft.samePrincipal} onChange={(e) => update({ samePrincipal: e.target.checked })}
                           className="h-4 w-4 rounded border-gray-300 text-brand" />
                    This is also the principal place of business
                  </label>
                </Section>
                {!draft.samePrincipal ? (
                  <Section title="Principal place of business" hint="A street address (not a PO box).">
                    <AddressFields value={draft.principal} onChange={(principal) => update({ principal })} errors={errors} prefix="principal" />
                  </Section>
                ) : null}
                {errors.general ? <Note tone="bad">{errors.general}</Note> : null}
                <StepActions
                  label={submitting ? 'Saving…' : `Continue to payment · $${price(draft.years).toFixed(2)}`}
                  busy={submitting}
                  onBack={back}
                />
                <p className="text-center text-xs text-gray-500">
                  We lodge on behalf of the applicant, who must be eligible to hold a business name.
                </p>
              </div>
            </>
          ) : null}
        </form>
      </Skeletonizer>
    </Shell>
  )
}
