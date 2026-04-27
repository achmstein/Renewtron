import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import GridBackground from '../components/GridBackground'
import WizardProgress from '../components/WizardProgress'

const steps = [
  { label: 'ABN' }, { label: 'Details' }, { label: 'Check' }, { label: 'Select' }, { label: 'Pay' },
]

export default function StartPage() {
  const navigate = useNavigate()
  const [abn, setAbn] = useState('')
  const [error, setError] = useState('')

  const isValid = () => {
    const clean = abn.replace(/\s+/g, '')
    return clean.length === 11 && /^\d+$/.test(clean)
  }

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    if (!isValid()) {
      setError('Please enter a valid 11-digit ABN')
      return
    }
    navigate(`/details?abn=${abn.replace(/\s+/g, '')}`)
  }

  return (
    <div className="relative isolate overflow-auto flex-1">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
        <WizardProgress currentStep={1} steps={steps} allowNavigation={false} />

        <div className="mx-auto max-w-2xl text-center">
          <h1 className="text-3xl font-bold tracking-tight text-gray-900 sm:text-4xl">Renew your business name</h1>
          <p className="mt-4 text-lg leading-8 text-gray-600">
            Enter your ABN to get started with your business name renewal.
          </p>
        </div>

        <div className="mx-auto mt-12 max-w-md">
          <form onSubmit={submit}>
            <div className="space-y-6">
              <div>
                <label htmlFor="abn" className="block text-sm font-medium leading-6 text-gray-900">
                  Australian Business Number (ABN)
                </label>
                <div className="mt-2">
                  <input
                    type="text"
                    id="abn"
                    value={abn}
                    onChange={(e) => setAbn(e.target.value)}
                    placeholder="12 345 678 901"
                    maxLength={14}
                    className="block w-full rounded-md border-0 py-3.5 text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 placeholder:text-gray-400 focus:ring-2 focus:ring-inset focus-ring-brand sm:text-lg px-4 text-center tracking-wider"
                    required
                  />
                </div>
                {error ? <p className="mt-2 text-sm text-red-600">{error}</p> : null}
                <p className="mt-2 text-sm text-gray-500">Your 11-digit Australian Business Number</p>
              </div>

              <button
                type="submit"
                disabled={!isValid()}
                className="btn-primary flex w-full justify-center rounded-md px-4 py-3 text-sm font-semibold shadow-sm transition-colors"
              >
                Continue
                <svg className="ml-2 h-5 w-5" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
                </svg>
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  )
}
