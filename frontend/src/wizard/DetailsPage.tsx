import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import GridBackground from '../components/GridBackground'
import WizardProgress from '../components/WizardProgress'

const steps = [
  { label: 'ABN' }, { label: 'Details' }, { label: 'Check' }, { label: 'Select' }, { label: 'Pay' },
]

function formatAbn(abn: string) {
  const clean = (abn ?? '').replace(/\s+/g, '')
  if (clean.length === 11) return `${clean.slice(0, 2)} ${clean.slice(2, 5)} ${clean.slice(5, 8)} ${clean.slice(8)}`
  return abn
}

function isValidEmail(value: string) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
}

// TFN: 9 digits, weighted-mod-11 checksum (ATO standard)
function isValidTfn(value: string) {
  const digits = value.replace(/\s/g, '')
  if (!/^\d{9}$/.test(digits)) return false
  const weights = [1, 4, 3, 7, 5, 8, 6, 9, 10]
  const sum = digits.split('').reduce((acc, d, i) => acc + parseInt(d, 10) * weights[i], 0)
  return sum % 11 === 0
}

export default function DetailsPage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const abn = params.get('abn') ?? ''

  useEffect(() => { if (!abn) navigate('/') }, [abn, navigate])

  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [mobileNumber, setMobileNumber] = useState('')
  const [dob, setDob] = useState('')
  const [tfn, setTfn] = useState('')
  const [errors, setErrors] = useState({ fullName: '', email: '', mobile: '', dob: '', tfn: '', general: '' })
  const [isSubmitting, setIsSubmitting] = useState(false)

  const inputClass = (err: string) =>
    `block w-full rounded-md border-0 py-2.5 px-3 text-gray-900 shadow-sm ring-1 ring-inset placeholder:text-gray-400 focus:ring-2 focus:ring-inset sm:text-sm ${
      err ? 'ring-red-300 focus:ring-red-500' : 'ring-gray-300 focus-ring-brand'
    }`

  const validate = () => {
    const next = { fullName: '', email: '', mobile: '', dob: '', tfn: '', general: '' }
    if (!fullName.trim()) next.fullName = 'Please enter your full name'
    else if (fullName.trim().length < 2) next.fullName = 'Name must be at least 2 characters'
    if (!email.trim()) next.email = 'Please enter your email address'
    else if (!isValidEmail(email)) next.email = 'Please enter a valid email address'
    const cleanMobile = mobileNumber.replace(/[\s-]/g, '')
    if (!cleanMobile) next.mobile = 'Please enter your mobile number'
    else if (cleanMobile.length < 10) next.mobile = 'Please enter a valid Australian mobile number'
    if (!dob) next.dob = 'Please enter your date of birth'
    else {
      const eighteenAgo = new Date()
      eighteenAgo.setFullYear(eighteenAgo.getFullYear() - 18)
      if (new Date(dob) > eighteenAgo) next.dob = 'You must be at least 18 years old'
    }
    // TFN is optional, but if provided must be valid
    if (tfn.trim() && !isValidTfn(tfn)) next.tfn = 'Please enter a valid 9-digit TFN'
    setErrors(next)
    return !next.fullName && !next.email && !next.mobile && !next.dob && !next.tfn
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!validate()) return
    setIsSubmitting(true)
    try {
      const cleanTfn = tfn.replace(/\s/g, '')
      const result = await api.createLead({
        abn: abn.replace(/\s+/g, ''),
        fullName: fullName.trim(),
        email: email.trim().toLowerCase(),
        mobileNumber: mobileNumber.replace(/[\s-]/g, ''),
        dateOfBirth: dob,
        tfn: cleanTfn || undefined,
      })
      navigate(`/checking/${result.leadId}`)
    } catch (err) {
      setErrors((e2) => ({ ...e2, general: err instanceof Error ? err.message : 'An error occurred. Please try again.' }))
      setIsSubmitting(false)
    }
  }

  const maxDob = (() => {
    const d = new Date()
    d.setFullYear(d.getFullYear() - 18)
    return d.toISOString().split('T')[0]
  })()

  return (
    <div className="relative isolate overflow-auto flex-1">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
        <WizardProgress currentStep={2} steps={steps} onStepClick={(s) => s === 1 && navigate('/')} />

        <div className="mx-auto max-w-2xl text-center">
          <h1 className="text-2xl font-bold tracking-tight text-gray-900 sm:text-3xl">Confirm your details</h1>
          <p className="mt-3 text-base leading-7 text-gray-600">
            We'll use these details to process your renewal and keep you informed.
          </p>
        </div>

        <div className="mx-auto mt-6 max-w-md text-center">
          <span className="inline-flex items-center rounded-full bg-brand-light px-4 py-2 text-sm font-medium text-brand">
            <svg className="mr-2 h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            ABN: {formatAbn(abn)}
          </span>
        </div>

        <div className="mx-auto mt-10 max-w-md">
          <form onSubmit={submit}>
            <div className="space-y-5">
              <div>
                <label htmlFor="fullName" className="block text-sm font-medium leading-6 text-gray-900">
                  Full Name <span className="text-red-500">*</span>
                </label>
                <div className="mt-2">
                  <input id="fullName" type="text" value={fullName} onChange={(e) => setFullName(e.target.value)} placeholder="John Smith" className={inputClass(errors.fullName)} required />
                </div>
                {errors.fullName ? <p className="mt-1 text-sm text-red-600">{errors.fullName}</p> : null}
              </div>

              <div>
                <label htmlFor="email" className="block text-sm font-medium leading-6 text-gray-900">
                  Email Address <span className="text-red-500">*</span>
                </label>
                <div className="mt-2">
                  <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="john@example.com" className={inputClass(errors.email)} required />
                </div>
                {errors.email ? <p className="mt-1 text-sm text-red-600">{errors.email}</p> : null}
                <p className="mt-1 text-xs text-gray-500">We'll send confirmation to this email</p>
              </div>

              <div>
                <label htmlFor="mobile" className="block text-sm font-medium leading-6 text-gray-900">
                  Mobile Number <span className="text-red-500">*</span>
                </label>
                <div className="mt-2">
                  <input id="mobile" type="tel" value={mobileNumber} onChange={(e) => setMobileNumber(e.target.value)} placeholder="0412 345 678" maxLength={14} className={inputClass(errors.mobile)} required />
                </div>
                {errors.mobile ? <p className="mt-1 text-sm text-red-600">{errors.mobile}</p> : null}
              </div>

              <div>
                <label htmlFor="dob" className="block text-sm font-medium leading-6 text-gray-900">
                  Date of Birth <span className="text-red-500">*</span>
                </label>
                <div className="mt-2">
                  <input id="dob" type="date" value={dob} onChange={(e) => setDob(e.target.value)} max={maxDob} className={inputClass(errors.dob)} required />
                </div>
                {errors.dob ? <p className="mt-1 text-sm text-red-600">{errors.dob}</p> : null}
                <p className="mt-1 text-xs text-gray-500">You must be at least 18 years old</p>
              </div>

              <div>
                <label htmlFor="tfn" className="block text-sm font-medium leading-6 text-gray-900">
                  Tax File Number <span className="text-gray-400 text-xs">(optional)</span>
                </label>
                <div className="mt-2">
                  <input id="tfn" type="text" inputMode="numeric" value={tfn} onChange={(e) => setTfn(e.target.value)} placeholder="123 456 789" maxLength={11} className={inputClass(errors.tfn)} />
                </div>
                {errors.tfn ? <p className="mt-1 text-sm text-red-600">{errors.tfn}</p> : null}
                <p className="mt-1 text-xs text-gray-500">Provide your TFN to auto-fill your tax details after renewal</p>
              </div>

              {errors.general ? (
                <div className="rounded-md bg-red-50 p-4">
                  <div className="flex">
                    <div className="flex-shrink-0">
                      <svg className="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
                        <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.28 7.22a.75.75 0 00-1.06 1.06L8.94 10l-1.72 1.72a.75.75 0 101.06 1.06L10 11.06l1.72 1.72a.75.75 0 101.06-1.06L11.06 10l1.72-1.72a.75.75 0 00-1.06-1.06L10 8.94 8.28 7.22z" clipRule="evenodd" />
                      </svg>
                    </div>
                    <div className="ml-3"><p className="text-sm text-red-800">{errors.general}</p></div>
                  </div>
                </div>
              ) : null}

              <div className="pt-4">
                <button type="submit" disabled={isSubmitting} className="flex w-full justify-center items-center rounded-md bg-brand px-4 py-3 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark disabled:opacity-50 disabled:cursor-not-allowed transition-colors">
                  {isSubmitting ? (
                    <>
                      <svg className="animate-spin -ml-1 mr-2 h-5 w-5 text-white" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                      </svg>
                      <span>Processing...</span>
                    </>
                  ) : (
                    <>
                      <span>Check Renewal Status</span>
                      <svg className="ml-2 h-5 w-5" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
                      </svg>
                    </>
                  )}
                </button>
              </div>

              <div className="text-center">
                <button type="button" onClick={() => navigate('/')} className="text-sm font-medium text-gray-600 hover:text-gray-500">
                  <svg className="inline-block mr-1 h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M10.5 19.5L3 12m0 0l7.5-7.5M3 12h18" />
                  </svg>
                  Back to ABN entry
                </button>
              </div>
            </div>
          </form>
        </div>
      </div>
    </div>
  )
}
