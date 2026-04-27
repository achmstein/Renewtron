import { useEffect, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api, type LeadDto, type RenewalStatusItem } from '../api/client'
import GridBackground from '../components/GridBackground'
import UserDetailsSummary from '../components/UserDetailsSummary'

export default function ConfirmationPage() {
  const { leadId } = useParams<{ leadId: string }>()
  const [params] = useSearchParams()
  const ids = (params.get('ids') ?? '').split(',').filter(Boolean)

  const [lead, setLead] = useState<LeadDto | null>(null)
  const [renewals, setRenewals] = useState<RenewalStatusItem[]>([])
  const [loading, setLoading] = useState(true)
  const pollRef = useRef<number | undefined>(undefined)

  useEffect(() => {
    if (!leadId) return
    void api.getLead(leadId).then(setLead).catch(() => {})
  }, [leadId])

  useEffect(() => {
    if (ids.length === 0) { setLoading(false); return }
    let active = true

    const tick = async () => {
      try {
        const list = await api.batchStatus(ids)
        if (!active) return
        setRenewals(list)
        const allDone = list.every((r) => r.status === 'Completed' || r.status === 'Failed')
        if (allDone && pollRef.current) {
          window.clearInterval(pollRef.current)
          pollRef.current = undefined
        }
      } catch {
        // ignore
      } finally {
        if (active) setLoading(false)
      }
    }
    void tick()
    pollRef.current = window.setInterval(tick, 3000)
    return () => {
      active = false
      if (pollRef.current) window.clearInterval(pollRef.current)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params])

  const allCompleted = renewals.length > 0 && renewals.every((r) => r.status === 'Completed' || r.status === 'Failed')
  const completedCount = renewals.filter((r) => r.status === 'Completed').length
  const totalAmount = renewals.reduce((sum, r) => sum + (r.amount ?? 0), 0)

  return (
    <div className="relative isolate overflow-auto flex-1">
      <GridBackground />
      <div className="mx-auto max-w-7xl px-6 pb-24 pt-10 sm:pb-32 lg:px-8 lg:py-12">
        {loading ? (
          <div className="text-center py-12">
            <svg className="mx-auto h-12 w-12 animate-spin text-brand" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
            <p className="mt-4 text-sm text-gray-600">Loading...</p>
          </div>
        ) : !lead || renewals.length === 0 ? (
          <div className="text-center py-12">
            <h3 className="text-lg font-semibold text-gray-900">Confirmation not found</h3>
            <Link to="/" className="mt-4 inline-block text-brand hover:text-brand-dark">Start a new renewal</Link>
          </div>
        ) : (
          <>
            <div className="mx-auto max-w-2xl">
              <UserDetailsSummary abn={lead.abn} fullName={lead.fullName} email={lead.email} mobileNumber={lead.mobileNumber} dateOfBirth={lead.dateOfBirth} />
            </div>

            <div className="mx-auto max-w-2xl">
              <div className="overflow-hidden rounded-xl bg-white shadow-lg ring-1 ring-gray-200">
                {allCompleted ? (
                  <div className="bg-gradient-to-r from-green-600 to-green-500 px-6 py-8 text-center">
                    <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-full bg-white shadow-lg">
                      <svg className="h-10 w-10 text-green-600" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                      </svg>
                    </div>
                    <h2 className="mt-4 text-2xl font-bold text-white">All Renewals Complete!</h2>
                    <p className="mt-2 text-sm text-green-100">
                      {renewals.length} business name{renewals.length > 1 ? 's have' : ' has'} been successfully renewed
                    </p>
                  </div>
                ) : (
                  <div className="bg-gradient-to-r from-blue-600 to-blue-500 px-6 py-8 text-center">
                    <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-full bg-white shadow-lg">
                      <svg className="h-10 w-10 text-brand" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                      </svg>
                    </div>
                    <h2 className="mt-4 text-2xl font-bold text-white">Payment Successful!</h2>
                    <p className="mt-2 text-sm text-blue-100">
                      Your {renewals.length} renewal{renewals.length > 1 ? 's are' : ' is'} being processed
                    </p>
                  </div>
                )}

                <div className="px-6 py-5 border-b border-gray-100">
                  <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
                    <div className="text-center">
                      <dt className="text-xs font-medium text-gray-500 uppercase tracking-wider">Renewals</dt>
                      <dd className="mt-1 text-xl font-bold text-gray-900">{renewals.length}</dd>
                    </div>
                    <div className="text-center">
                      <dt className="text-xs font-medium text-gray-500 uppercase tracking-wider">Period</dt>
                      <dd className="mt-1 text-xl font-bold text-gray-900">{renewals[0]?.renewalYears ?? 1} Yr</dd>
                    </div>
                    <div className="text-center">
                      <dt className="text-xs font-medium text-gray-500 uppercase tracking-wider">Completed</dt>
                      <dd className="mt-1 text-xl font-bold text-green-600">{completedCount}</dd>
                    </div>
                    <div className="text-center">
                      <dt className="text-xs font-medium text-gray-500 uppercase tracking-wider">Total</dt>
                      <dd className="mt-1 text-xl font-bold text-brand">${totalAmount.toFixed(2)}</dd>
                    </div>
                  </div>
                </div>

                <div className="divide-y divide-gray-100">
                  {renewals.map((r) => (
                    <div key={r.id} className="px-6 py-4">
                      <div className="flex items-center justify-between">
                        <div className="flex-1 min-w-0">
                          <h3 className="font-medium text-gray-900 truncate">{r.businessName}</h3>
                          <p className="text-xs text-gray-500">Account: {r.accountNumber}</p>
                          {r.status === 'Completed' && r.transactionReference ? (
                            <p className="mt-1 text-xs text-gray-500 font-mono">Ref: {r.transactionReference}</p>
                          ) : null}
                        </div>
                        <div className="ml-4 flex items-center gap-3">
                          <span className="text-sm font-medium text-gray-700">${r.amount.toFixed(2)}</span>
                          {r.status === 'Completed' ? (
                            <span className="inline-flex items-center rounded-full bg-green-50 px-2.5 py-1 text-xs font-medium text-green-700 ring-1 ring-inset ring-green-600/20">
                              <svg className="mr-1 h-3 w-3" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                              </svg>
                              Done
                            </span>
                          ) : r.status === 'Failed' ? (
                            <span className="inline-flex items-center rounded-full bg-red-50 px-2.5 py-1 text-xs font-medium text-red-700 ring-1 ring-inset ring-red-600/20">
                              <svg className="mr-1 h-3 w-3" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-8-5a.75.75 0 01.75.75v4.5a.75.75 0 01-1.5 0v-4.5A.75.75 0 0110 5zm0 10a1 1 0 100-2 1 1 0 000 2z" clipRule="evenodd" />
                              </svg>
                              Issue
                            </span>
                          ) : (
                            <span className="inline-flex items-center rounded-full bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-700 ring-1 ring-inset ring-amber-600/20">
                              <svg className="mr-1 h-3 w-3 animate-spin" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                              </svg>
                              Processing
                            </span>
                          )}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>

                <div className="bg-gray-50 px-6 py-4 flex items-center justify-between">
                  <Link to="/" className="text-sm font-medium text-brand hover:text-brand-dark">Start another renewal</Link>
                  <button onClick={() => window.print()} className="inline-flex items-center gap-x-2 rounded-md bg-white px-3 py-2 text-sm font-medium text-gray-700 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50">
                    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M6.72 13.829c-.24.03-.48.062-.72.096m.72-.096a42.415 42.415 0 0110.56 0m-10.56 0L6.34 18m10.94-4.171c.24.03.48.062.72.096m-.72-.096L17.66 18m0 0l.229 2.523a1.125 1.125 0 01-1.12 1.227H7.231c-.662 0-1.18-.568-1.12-1.227L6.34 18m11.318 0h1.091A2.25 2.25 0 0021 15.75V9.456c0-1.081-.768-2.015-1.837-2.175a48.055 48.055 0 00-1.913-.247M6.34 18H5.25A2.25 2.25 0 013 15.75V9.456c0-1.081.768-2.015 1.837-2.175a48.041 48.041 0 011.913-.247m10.5 0a48.536 48.536 0 00-10.5 0m10.5 0V3.375c0-.621-.504-1.125-1.125-1.125h-8.25c-.621 0-1.125.504-1.125 1.125v3.659M18 10.5h.008v.008H18V10.5zm-3 0h.008v.008H15V10.5z" />
                    </svg>
                    Print
                  </button>
                </div>
              </div>

              <div className="mt-6 rounded-xl bg-blue-50 border border-blue-100 p-5">
                <h3 className="text-sm font-semibold text-blue-900">What happens next?</h3>
                <ul className="mt-3 space-y-2 text-sm text-blue-800">
                  {allCompleted ? (
                    <>
                      <li className="flex items-start">
                        <svg className="mt-0.5 h-4 w-4 flex-shrink-0 text-brand" fill="currentColor" viewBox="0 0 20 20">
                          <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                        </svg>
                        <span className="ml-2">Confirmation emails sent to <strong>{lead.email}</strong></span>
                      </li>
                      <li className="flex items-start">
                        <svg className="mt-0.5 h-4 w-4 flex-shrink-0 text-brand" fill="currentColor" viewBox="0 0 20 20">
                          <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                        </svg>
                        <span className="ml-2">Your business name registrations have been extended</span>
                      </li>
                    </>
                  ) : (
                    <>
                      <li className="flex items-start">
                        <svg className="mt-0.5 h-4 w-4 flex-shrink-0 text-brand" fill="currentColor" viewBox="0 0 20 20">
                          <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                        </svg>
                        <span className="ml-2">Payment of <strong>${totalAmount.toFixed(2)}</strong> processed successfully</span>
                      </li>
                      <li className="flex items-start">
                        <svg className="mt-0.5 h-4 w-4 flex-shrink-0 text-brand animate-spin" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
                        </svg>
                        <span className="ml-2">Renewals being processed with ASIC (takes a few minutes)</span>
                      </li>
                    </>
                  )}
                </ul>
              </div>

              <div className="mt-6 text-center">
                <p className="text-sm text-gray-500">
                  Questions? Contact us at{' '}
                  <a href="mailto:businessnames@applyforanabn.com.au" className="font-medium text-brand hover:text-brand-dark">
                    businessnames@applyforanabn.com.au
                  </a>
                </p>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
