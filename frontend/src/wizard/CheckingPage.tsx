import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type LeadDto } from '../api/client'
import GridBackground from '../components/GridBackground'
import UserDetailsSummary from '../components/UserDetailsSummary'
import WizardProgress from '../components/WizardProgress'

const steps = [
  { label: 'ABN' }, { label: 'Details' }, { label: 'Check' }, { label: 'Select' }, { label: 'Pay' },
]

const loadingMessages = [
  'Connecting to ASIC database...',
  'Verifying your ABN...',
  'Retrieving business names...',
  'Processing registration details...',
  'Finalizing results...',
]

export default function CheckingPage() {
  const navigate = useNavigate()
  const { leadId } = useParams<{ leadId: string }>()
  const [lead, setLead] = useState<LeadDto | null>(null)
  const [loadingMessage, setLoadingMessage] = useState(loadingMessages[0])
  const [progress, setProgress] = useState(0)
  const [error, setError] = useState('')
  const startedRef = useRef(false)

  useEffect(() => {
    if (!leadId) return
    api.getLead(leadId).then(setLead).catch((e) => setError(e instanceof Error ? e.message : 'Lead not found.'))
  }, [leadId])

  useEffect(() => {
    if (!leadId || !lead || startedRef.current) return
    startedRef.current = true

    let messageIdx = 0
    const messageTimer = window.setInterval(() => {
      messageIdx = (messageIdx + 1) % loadingMessages.length
      setLoadingMessage(loadingMessages[messageIdx])
    }, 4000)

    const progressTimer = window.setInterval(() => {
      setProgress((p) => (p < 95 ? p + 5 : p))
    }, 1000)

    const run = async () => {
      try {
        const result = await api.checkLead(leadId)
        setProgress(100)
        if (result.outcome === 'RenewalAvailable' && result.businessNames.length > 0) {
          navigate(`/select-names/${leadId}`)
        } else {
          navigate(`/not-available/${leadId}`)
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Search failed. Please try again.')
      } finally {
        window.clearInterval(messageTimer)
        window.clearInterval(progressTimer)
      }
    }
    void run()

    return () => {
      window.clearInterval(messageTimer)
      window.clearInterval(progressTimer)
    }
  }, [leadId, lead, navigate])

  const retry = () => {
    setError('')
    setProgress(0)
    startedRef.current = false
    setLead((l) => (l ? { ...l } : l))
  }

  return (
    <div className="relative isolate overflow-auto flex-1">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
        <WizardProgress currentStep={3} steps={steps} allowNavigation={false} />

        {lead ? (
          <div className="mx-auto max-w-md">
            <UserDetailsSummary
              abn={lead.abn}
              fullName={lead.fullName}
              email={lead.email}
              mobileNumber={lead.mobileNumber}
              dateOfBirth={lead.dateOfBirth}
            />
          </div>
        ) : null}

        <div className="mx-auto max-w-sm pt-6">
          <div className="text-center">
            <div className="mx-auto mb-8 flex h-20 w-20 items-center justify-center">
              <svg className="h-20 w-20 animate-spin text-brand" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
            </div>

            <h2 className="text-xl font-semibold text-gray-900">Checking your renewal status</h2>
            <p className="mt-2 text-sm text-gray-600">This typically takes 20-25 seconds. Please don't close this page.</p>

            <div className="mt-8">
              <div className="flex items-center justify-center gap-2 text-sm text-gray-600">
                <div className="h-2 w-2 animate-pulse rounded-full bg-brand"></div>
                <span>{loadingMessage}</span>
              </div>
            </div>

            <div className="mt-4">
              <div className="h-2 w-full overflow-hidden rounded-full bg-gray-200">
                <div className="h-full rounded-full bg-brand transition-all duration-500" style={{ width: `${progress}%` }} />
              </div>
            </div>

            {error ? (
              <>
                <div className="mt-8 rounded-md bg-red-50 p-4">
                  <div className="flex">
                    <div className="flex-shrink-0">
                      <svg className="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
                        <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.28 7.22a.75.75 0 00-1.06 1.06L8.94 10l-1.72 1.72a.75.75 0 101.06 1.06L10 11.06l1.72 1.72a.75.75 0 101.06-1.06L11.06 10l1.72-1.72a.75.75 0 00-1.06-1.06L10 8.94 8.28 7.22z" clipRule="evenodd" />
                      </svg>
                    </div>
                    <div className="ml-3"><p className="text-sm text-red-800">{error}</p></div>
                  </div>
                </div>
                <div className="mt-4">
                  <button onClick={retry} className="text-sm font-medium text-brand hover:text-brand-dark">Try again</button>
                </div>
              </>
            ) : null}
          </div>
        </div>
      </div>
    </div>
  )
}
