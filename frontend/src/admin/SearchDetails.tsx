import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'

type Detail = Awaited<ReturnType<typeof api.admin.search>>

function formatAbn(abn: string) {
  const clean = (abn ?? '').replace(/\s+/g, '')
  if (clean.length === 11) return `${clean.slice(0, 2)} ${clean.slice(2, 5)} ${clean.slice(5, 8)} ${clean.slice(8)}`
  return abn
}
function fmtDateTime(s: string) {
  const d = new Date(s)
  const date = d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
  const time = d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
  return `${date} ${time}`
}
function fmtShortDateTime(s: string) {
  const d = new Date(s)
  const date = d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
  const time = d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })
  return `${date} ${time}`
}

function OutcomeBadge({ outcome }: { outcome: string }) {
  switch (outcome) {
    case 'RenewalCompleted':
      return <span className="inline-flex rounded-full bg-green-100 px-2 text-xs font-semibold leading-5 text-green-800">Converted</span>
    case 'RenewalAvailable':
      return <span className="inline-flex rounded-full bg-brand-100 px-2 text-xs font-semibold leading-5 text-brand-800">Available</span>
    case 'NotDueForRenewal':
      return <span className="inline-flex rounded-full bg-amber-100 px-2 text-xs font-semibold leading-5 text-amber-800">Not Due</span>
    case 'RenewalInProgress':
      return <span className="inline-flex rounded-full bg-purple-100 px-2 text-xs font-semibold leading-5 text-purple-800">In Progress</span>
    case 'NoBusinessNames':
      return <span className="inline-flex rounded-full bg-gray-100 px-2 text-xs font-semibold leading-5 text-gray-800">No Names</span>
    default:
      return <span className="inline-flex rounded-full bg-gray-100 px-2 text-xs font-semibold leading-5 text-gray-600">Pending</span>
  }
}

function RenewalStatusBadge({ status }: { status: string }) {
  switch (status) {
    case 'Completed': return <span className="inline-flex rounded-full bg-green-100 px-2 text-xs font-semibold leading-5 text-green-800">Completed</span>
    case 'Processing': return <span className="inline-flex rounded-full bg-brand-100 px-2 text-xs font-semibold leading-5 text-brand-800">Processing</span>
    case 'Pending': return <span className="inline-flex rounded-full bg-yellow-100 px-2 text-xs font-semibold leading-5 text-yellow-800">Pending</span>
    case 'Failed': return <span className="inline-flex rounded-full bg-red-100 px-2 text-xs font-semibold leading-5 text-red-800">Failed</span>
    default: return <span className="inline-flex rounded-full bg-gray-100 px-2 text-xs font-semibold leading-5 text-gray-700">{status}</span>
  }
}

export default function SearchDetails() {
  const { id } = useParams()
  const [data, setData] = useState<Detail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)

  useEffect(() => {
    if (!id) return
    setIsLoading(true)
    api.admin.search(id)
      .then(setData)
      .catch(() => setNotFound(true))
      .finally(() => setIsLoading(false))
  }, [id])

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between">
            <h1 className="text-lg/6 font-semibold text-gray-900">Search Details</h1>
            <Link to="/admin/searches" className="text-sm text-gray-600 hover:text-gray-900">← Back to Searches</Link>
          </div>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {isLoading ? (
            <div className="flex items-center justify-center py-12">
              <svg className="animate-spin h-8 w-8 text-brand-600" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
              </svg>
              <span className="ml-3 text-sm text-gray-600">Loading...</span>
            </div>
          ) : notFound || !data ? (
            <div className="rounded-lg bg-yellow-50 p-4">
              <div className="flex">
                <div className="shrink-0">
                  <svg className="h-5 w-5 text-yellow-400" viewBox="0 0 20 20" fill="currentColor">
                    <path fillRule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495zM10 5a.75.75 0 01.75.75v3.5a.75.75 0 01-1.5 0v-3.5A.75.75 0 0110 5zm0 9a1 1 0 100-2 1 1 0 000 2z" clipRule="evenodd" />
                  </svg>
                </div>
                <div className="ml-3"><p className="text-sm font-medium text-yellow-800">Search not found</p></div>
              </div>
            </div>
          ) : (
            <>
              {data.lead ? (
                <div className="mb-6 overflow-hidden rounded-lg bg-white shadow">
                  <div className="border-b border-gray-200 bg-gray-50 px-6 py-4">
                    <div className="flex items-center justify-between">
                      <h2 className="text-lg font-medium text-gray-900">Lead Information</h2>
                      <Link to={`/admin/leads/${data.lead.id}`} className="text-sm font-medium text-brand-600 hover:text-brand-500">View Lead →</Link>
                    </div>
                  </div>
                  <div className="px-6 py-5">
                    <dl className="grid grid-cols-1 gap-x-4 gap-y-6 sm:grid-cols-2 lg:grid-cols-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Name</dt>
                        <dd className="mt-1 text-sm font-semibold text-gray-900">{data.lead.fullName}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Email</dt>
                        <dd className="mt-1 text-sm text-gray-900"><a href={`mailto:${data.lead.email}`} className="text-brand-600 hover:text-brand-500">{data.lead.email}</a></dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Mobile</dt>
                        <dd className="mt-1 text-sm text-gray-900">{data.lead.mobileNumber}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Outcome</dt>
                        <dd className="mt-1"><OutcomeBadge outcome={data.lead.outcome} /></dd>
                      </div>
                    </dl>
                  </div>
                </div>
              ) : null}

              <div className="mb-6 overflow-hidden rounded-lg bg-white shadow">
                <div className="border-b border-gray-200 bg-gray-50 px-6 py-4">
                  <h2 className="text-lg font-medium text-gray-900">Search Information</h2>
                </div>
                <div className="px-6 py-5">
                  <dl className="grid grid-cols-1 gap-x-4 gap-y-6 sm:grid-cols-2 lg:grid-cols-3">
                    <div>
                      <dt className="text-sm font-medium text-gray-500">ABN</dt>
                      <dd className="mt-1 text-sm font-semibold text-gray-900">{formatAbn(data.abn)}</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Searched At</dt>
                      <dd className="mt-1 text-sm text-gray-900">{fmtDateTime(data.searchedAt)}</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">IP Address</dt>
                      <dd className="mt-1 text-sm text-gray-900">
                        <div className="flex items-center gap-2"><span>{data.ipAddress ?? '—'}</span></div>
                      </dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Session ID</dt>
                      <dd className="mt-1 text-sm font-mono text-gray-900">{data.sessionId ?? '—'}</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Results Count</dt>
                      <dd className="mt-1 text-sm text-gray-900">{data.resultsCount} business name(s)</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Initiated By</dt>
                      <dd className="mt-1 text-sm text-gray-900">{data.initiatedBy}</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Status</dt>
                      <dd className="mt-1">
                        {data.success
                          ? <span className="inline-flex rounded-full bg-green-100 px-2 text-xs font-semibold leading-5 text-green-800">Success</span>
                          : <span className="inline-flex rounded-full bg-red-100 px-2 text-xs font-semibold leading-5 text-red-800">Failed</span>}
                      </dd>
                    </div>
                    {data.errorMessage ? (
                      <div className="sm:col-span-3">
                        <dt className="text-sm font-medium text-gray-500">Error Message</dt>
                        <dd className="mt-1 text-sm text-red-600">{data.errorMessage}</dd>
                      </div>
                    ) : null}
                  </dl>
                </div>
              </div>

              <div className="overflow-hidden rounded-lg bg-white shadow">
                <div className="border-b border-gray-200 bg-gray-50 px-6 py-4">
                  <h2 className="text-lg font-medium text-gray-900">Business Names Found ({data.results.length})</h2>
                </div>
                {data.results.length > 0 ? (
                  <div className="divide-y divide-gray-200">
                    {data.results.map((r) => (
                      <div key={r.id} className="px-6 py-5">
                        <div className="flex items-start justify-between">
                          <div className="flex-1">
                            <h3 className="text-base font-semibold text-gray-900">{r.businessName}</h3>
                            <dl className="mt-4 grid grid-cols-1 gap-x-4 gap-y-4 sm:grid-cols-2 lg:grid-cols-3">
                              <div>
                                <dt className="text-xs font-medium text-gray-500">Result ID</dt>
                                <dd className="mt-1 text-sm text-gray-900 break-all">{r.id}</dd>
                              </div>
                              <div>
                                <dt className="text-xs font-medium text-gray-500">Account Number</dt>
                                <dd className="mt-1 text-sm text-gray-900">{r.accountNumber}</dd>
                              </div>
                              <div>
                                <dt className="text-xs font-medium text-gray-500">Registration Date</dt>
                                <dd className="mt-1 text-sm text-gray-900">{r.registrationDate}</dd>
                              </div>
                            </dl>
                            {r.renewalRequest ? (
                              <div className="mt-4">
                                <h4 className="text-sm font-medium text-gray-900 mb-2">Renewal Request</h4>
                                <div className="space-y-2">
                                  <div className="flex items-center justify-between rounded-md bg-gray-50 px-3 py-2">
                                    <div className="flex items-center gap-3">
                                      <RenewalStatusBadge status={r.renewalRequest.status} />
                                      <div className="text-xs text-gray-600">
                                        <span>{fmtShortDateTime(r.renewalRequest.initiatedAt)}</span>
                                        <span className="mx-1">•</span>
                                        <span>{r.renewalRequest.renewalYears} year{r.renewalRequest.renewalYears > 1 ? 's' : ''}</span>
                                        <span className="mx-1">•</span>
                                        <span>${r.renewalRequest.amount.toFixed(2)}</span>
                                      </div>
                                    </div>
                                    <Link to={`/admin/renewals/${r.renewalRequest.id}`} className="text-brand-600 hover:text-brand-900 text-xs font-medium">View Details →</Link>
                                  </div>
                                </div>
                              </div>
                            ) : null}
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="px-6 py-12 text-center">
                    <svg className="mx-auto h-12 w-12 text-gray-400" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M20.25 7.5l-.625 10.632a2.25 2.25 0 01-2.247 2.118H6.622a2.25 2.25 0 01-2.247-2.118L3.75 7.5M10 11.25h4M3.375 7.5h17.25c.621 0 1.125-.504 1.125-1.125v-1.5c0-.621-.504-1.125-1.125-1.125H3.375c-.621 0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125z" />
                    </svg>
                    <p className="mt-2 text-sm text-gray-500">No business names found in this search</p>
                  </div>
                )}
              </div>
            </>
          )}
        </div>
      </main>
    </>
  )
}
