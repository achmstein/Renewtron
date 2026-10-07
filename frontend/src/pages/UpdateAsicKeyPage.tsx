import { useEffect, useState } from 'react'
import { useLocation } from 'react-router-dom'
import GridBackground from '../components/GridBackground'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '../components/ui/dialog'
import { api } from '../api/client'

// An ASIC key is "1-" and eleven digits. ASIC's notices also carry a reference number
// ("1-TG355") that clients mistake for the key — anything with letters in it is that.
const KEY_FORMAT = /^1-\d{11}$/

type KeyProblem = 'reference' | 'format' | null

function cleanKey(value: string) {
  return value.replace(/\s+/g, '').replace(/[–—]/g, '-')
}

function checkKey(value: string): KeyProblem {
  const key = cleanKey(value)
  if (KEY_FORMAT.test(key)) return null
  return /[a-z]/i.test(key) ? 'reference' : 'format'
}

function formatAbn(abn: string) {
  return abn.length === 11 ? `${abn.slice(0, 2)} ${abn.slice(2, 5)} ${abn.slice(5, 8)} ${abn.slice(8)}` : abn
}

/** Ontraport merge fields render as "[Business Name]" when the contact has no value. */
function param(search: URLSearchParams, ...names: string[]) {
  for (const name of names) {
    const value = search.get(name)?.trim()
    if (value && !/^\[.*\]$/.test(value)) return value
  }
  return ''
}

export default function UpdateAsicKeyPage() {
  const { search } = useLocation()
  const [businessName, setBusinessName] = useState('')
  const [abn, setAbn] = useState('')
  const [asicKey, setAsicKey] = useState('')
  const [keyError, setKeyError] = useState('')
  const [error, setError] = useState('')
  const [popup, setPopup] = useState<KeyProblem>(null)
  const [submitting, setSubmitting] = useState(false)
  const [done, setDone] = useState(false)

  useEffect(() => {
    const qs = new URLSearchParams(search)
    const name = param(qs, 'business', 'businessname', 'business_name', 'bn')
    const abnDigits = param(qs, 'abn').replace(/\D/g, '')
    if (name) setBusinessName(name)
    if (abnDigits.length === 11) setAbn(formatAbn(abnDigits))
  }, [search])

  const abnDigits = abn.replace(/\D/g, '')

  const validateKey = () => {
    if (!asicKey.trim()) return true
    const problem = checkKey(asicKey)
    if (problem === 'reference') {
      setKeyError('This looks like an ASIC reference number, not your ASIC key.')
      setPopup('reference')
      return false
    }
    if (problem === 'format') {
      setKeyError('An ASIC key is 1- followed by 11 numbers, e.g. 1-80129865318.')
      return false
    }
    setKeyError('')
    return true
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    if (!businessName.trim()) return setError('Please enter your business name.')
    if (abnDigits.length !== 11) return setError('Please enter your 11-digit ABN.')
    const problem = checkKey(asicKey)
    if (problem) {
      validateKey()
      if (problem === 'format') setPopup('format')
      return
    }

    setSubmitting(true)
    try {
      await api.submitAsicKey({ businessName: businessName.trim(), abn: abnDigits, asicKey: cleanKey(asicKey) })
      setDone(true)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong. Please try again.')
    } finally {
      setSubmitting(false)
    }
  }

  const inputClass =
    'block w-full rounded-md border-0 py-3 px-4 text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 placeholder:text-gray-400 focus:ring-2 focus:ring-inset focus-ring-brand sm:text-base'

  return (
    <div className="relative isolate overflow-auto flex-1">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
        <div className="mx-auto max-w-2xl text-center">
          <h1 className="text-3xl font-bold tracking-tight text-gray-900 sm:text-4xl">Update ASIC Key</h1>
          <p className="mt-4 text-lg leading-8 text-gray-600">
            We need your ASIC key to finish renewing your business name.
          </p>
        </div>

        <div className="mx-auto mt-10 max-w-lg">
          {done ? (
            <div className="rounded-xl bg-white p-8 text-center shadow-sm ring-1 ring-gray-200">
              <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-green-100">
                <svg className="h-6 w-6 text-green-600" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
                </svg>
              </div>
              <h2 className="mt-4 text-xl font-semibold text-gray-900">Thank you — we've got your ASIC key</h2>
              <p className="mt-2 text-gray-600">
                Your ASIC key for <span className="font-medium text-gray-900">{businessName.trim()}</span> has been
                received. We'll use it to complete your business name renewal — there's nothing more you need to do.
              </p>
            </div>
          ) : (
            <>
              <div className="rounded-lg border border-amber-300 bg-amber-50 p-5">
                <p className="text-sm font-bold uppercase tracking-wide text-amber-800">Important</p>
                <p className="mt-2 text-sm font-medium text-amber-900">
                  You did not complete your ASIC key during the renewal process.
                </p>
                <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-amber-900">
                  <li>A copy has been emailed to the email address attached to your business name.</li>
                  <li>
                    If you have not received the ASIC key within 10 minutes, email{' '}
                    <a href="mailto:asickey@asicconnect.com.au" className="font-medium underline">asickey@asicconnect.com.au</a>.
                  </li>
                </ul>
              </div>

              <form onSubmit={submit} noValidate className="mt-8 rounded-xl bg-white p-6 shadow-sm ring-1 ring-gray-200 sm:p-8">
                <h2 className="text-base font-semibold text-gray-900">Business name details</h2>
                <div className="mt-6 space-y-6">
                  <div>
                    <label htmlFor="businessName" className="block text-sm font-medium leading-6 text-gray-900">Business name</label>
                    <input
                      id="businessName"
                      type="text"
                      autoComplete="organization"
                      value={businessName}
                      onChange={(e) => setBusinessName(e.target.value)}
                      maxLength={200}
                      className={`${inputClass} mt-2`}
                      required
                    />
                  </div>

                  <div>
                    <label htmlFor="abn" className="block text-sm font-medium leading-6 text-gray-900">Business name ABN</label>
                    <input
                      id="abn"
                      type="text"
                      inputMode="numeric"
                      value={abn}
                      onChange={(e) => setAbn(e.target.value)}
                      placeholder="12 345 678 901"
                      maxLength={14}
                      className={`${inputClass} mt-2 tracking-wider`}
                      required
                    />
                  </div>

                  <div>
                    <label htmlFor="asicKey" className="block text-sm font-medium leading-6 text-gray-900">ASIC key</label>
                    <input
                      id="asicKey"
                      type="text"
                      autoComplete="off"
                      spellCheck={false}
                      value={asicKey}
                      onChange={(e) => { setAsicKey(e.target.value); if (keyError && !checkKey(e.target.value)) setKeyError('') }}
                      onBlur={validateKey}
                      placeholder="1-12345678901"
                      maxLength={20}
                      aria-invalid={keyError ? true : undefined}
                      aria-describedby="asicKey-help"
                      className={`${inputClass} mt-2 font-mono tracking-wider ${keyError ? 'ring-red-400' : ''}`}
                      required
                    />
                    {keyError ? <p className="mt-2 text-sm text-red-600">{keyError}</p> : null}
                    <p id="asicKey-help" className="mt-2 text-sm text-gray-500">
                      Your ASIC key is <span className="font-medium text-gray-700">1-</span> followed by{' '}
                      <span className="font-medium text-gray-700">11 numbers</span>, e.g.{' '}
                      <span className="font-mono text-gray-700">1-80129865318</span>. It only contains numbers — if
                      yours has letters, it's a reference number, not your ASIC key.
                    </p>
                  </div>

                  {error ? <p className="text-sm text-red-600">{error}</p> : null}

                  <button
                    type="submit"
                    disabled={submitting}
                    className="btn-primary flex w-full justify-center rounded-md px-4 py-3 text-sm font-semibold shadow-sm transition-colors"
                  >
                    {submitting ? 'Saving…' : 'Update'}
                  </button>
                </div>
              </form>

              <p className="mt-6 text-center text-xs text-gray-500">
                This is not an ASIC website but a third party service. We are not connected with or affiliated with ASIC.
              </p>
            </>
          )}
        </div>
      </div>

      <Dialog open={popup !== null} onOpenChange={(open) => { if (!open) setPopup(null) }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {popup === 'reference' ? 'This is not your ASIC key' : 'Please check your ASIC key'}
            </DialogTitle>
            <DialogDescription>
              {popup === 'reference'
                ? `"${cleanKey(asicKey)}" is an ASIC reference number, not an ASIC key.`
                : `"${cleanKey(asicKey)}" doesn't look like an ASIC key.`}
            </DialogDescription>
          </DialogHeader>
          <DialogBody className="space-y-3 text-sm text-gray-700">
            <p>
              Your ASIC key is <span className="font-semibold">1-</span> followed by{' '}
              <span className="font-semibold">11 numbers</span> and has no letters, for example:
            </p>
            <p className="rounded-md bg-gray-50 px-4 py-3 text-center font-mono text-lg tracking-wider text-gray-900 ring-1 ring-gray-200">
              1-80129865318
            </p>
            <p>
              Open the email or letter from ASIC that you got this number from and look for the{' '}
              <span className="font-semibold">ASIC key</span> — it reads like
              "Here is the ASIC Key for YOUR BUSINESS NAME: 1-…". If the email has a link, click it to open
              the letter with your ASIC key.
            </p>
          </DialogBody>
          <DialogFooter>
            <button
              type="button"
              onClick={() => { setPopup(null); setAsicKey(''); document.getElementById('asicKey')?.focus() }}
              className="btn-primary rounded-md px-4 py-2 text-sm font-semibold"
            >
              OK, I'll find my ASIC key
            </button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
