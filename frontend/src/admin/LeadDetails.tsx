import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'

type Detail = Awaited<ReturnType<typeof api.admin.lead>>

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
function fmtDob(s: string) {
  const d = new Date(s)
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  return `${dd}/${mm}/${d.getFullYear()}`
}
function fmtShort(s: string) {
  const d = new Date(s)
  return `${d.toLocaleDateString(undefined, { month: 'short', day: '2-digit' })} ${d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })}`
}

function OutcomeBadge({ outcome, big = false }: { outcome: string; big?: boolean }) {
  const cls = big ? 'inline-flex rounded-full px-3 py-1 text-sm font-semibold leading-5' : 'inline-flex rounded-full px-2 text-xs font-semibold leading-5'
  switch (outcome) {
    case 'RenewalCompleted': return <span className={`${cls} bg-green-100 text-green-800`}>Converted</span>
    case 'RenewalAvailable': return <span className={`${cls} bg-blue-100 text-blue-800`}>Available</span>
    case 'NotDueForRenewal': return <span className={`${cls} bg-amber-100 text-amber-800`}>Not Due</span>
    case 'RenewalInProgress': return <span className={`${cls} bg-purple-100 text-purple-800`}>In Progress</span>
    case 'NoBusinessNames': return <span className={`${cls} bg-gray-100 text-gray-800`}>No Names</span>
    default: return <span className={`${cls} bg-gray-100 text-gray-600`}>Pending</span>
  }
}
function RenewalBadge({ status }: { status: string }) {
  switch (status) {
    case 'Completed': return <span className="inline-flex rounded-full bg-green-100 px-2 text-xs font-semibold leading-5 text-green-800">Completed</span>
    case 'Processing': return <span className="inline-flex rounded-full bg-blue-100 px-2 text-xs font-semibold leading-5 text-blue-800">Processing</span>
    case 'Pending': return <span className="inline-flex rounded-full bg-yellow-100 px-2 text-xs font-semibold leading-5 text-yellow-800">Pending</span>
    case 'Failed': return <span className="inline-flex rounded-full bg-red-100 px-2 text-xs font-semibold leading-5 text-red-800">Failed</span>
    default: return <span className="inline-flex rounded-full bg-gray-100 px-2 text-xs font-semibold leading-5 text-gray-700">{status}</span>
  }
}

export default function LeadDetails() {
  const { id } = useParams()
  const [data, setData] = useState<Detail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)

  useEffect(() => {
    if (!id) return
    setIsLoading(true)
    api.admin.lead(id).then(setData).catch(() => setNotFound(true)).finally(() => setIsLoading(false))
  }, [id])

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between">
            <h1 className="text-lg/6 font-semibold text-gray-900">Lead Details</h1>
            <Link to="/admin/leads" className="text-sm text-gray-600 hover:text-gray-900">← Back to Leads</Link>
          </div>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {isLoading ? (
            <div className="text-center py-12">
              <svg className="mx-auto h-12 w-12 animate-spin text-blue-600" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
              </svg>
              <p className="mt-4 text-sm text-gray-600">Loading...</p>
            </div>
          ) : notFound || !data ? (
            <div className="text-center py-12">
              <h3 className="text-lg font-semibold text-gray-900">Lead Not Found</h3>
              <p className="mt-2 text-sm text-gray-600">The lead you're looking for doesn't exist.</p>
            </div>
          ) : (
            <>
              <div className="mb-6 overflow-hidden rounded-lg bg-white shadow">
                <div className="px-6 py-5">
                  <div className="flex items-center justify-between">
                    <div>
                      <h2 className="text-2xl font-bold text-gray-900">{data.fullName}</h2>
                      <p className="mt-1 text-sm text-gray-500">ABN: {formatAbn(data.abn)}</p>
                    </div>
                    <div className="text-right flex flex-col items-end gap-2">
                      <OutcomeBadge outcome={data.outcome} big />
                      {data.reminderOptIn ? (
                        <span className="inline-flex items-center gap-1 text-sm text-green-600">
                          <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                          </svg>
                          Reminder Opt-in
                        </span>
                      ) : null}
                    </div>
                  </div>
                </div>
              </div>

              <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="border-b border-gray-200 bg-gray-50 px-6 py-3">
                    <h3 className="text-base font-semibold text-gray-900">Contact Information</h3>
                  </div>
                  <div className="px-6 py-4">
                    <dl className="space-y-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Full Name</dt>
                        <dd className="mt-1 text-sm text-gray-900">{data.fullName}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Email</dt>
                        <dd className="mt-1 text-sm text-gray-900"><a href={`mailto:${data.email}`} className="text-blue-600 hover:text-blue-500">{data.email}</a></dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Mobile Number</dt>
                        <dd className="mt-1 text-sm text-gray-900"><a href={`tel:${data.mobileNumber}`} className="text-blue-600 hover:text-blue-500">{data.mobileNumber}</a></dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Date of Birth</dt>
                        <dd className="mt-1 text-sm text-gray-900">{fmtDob(data.dateOfBirth)}</dd>
                      </div>
                    </dl>
                  </div>
                </div>

                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="border-b border-gray-200 bg-gray-50 px-6 py-3">
                    <h3 className="text-base font-semibold text-gray-900">Lead Information</h3>
                  </div>
                  <div className="px-6 py-4">
                    <dl className="space-y-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Lead ID</dt>
                        <dd className="mt-1 text-sm text-gray-900 font-mono text-xs break-all">{data.id}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">ABN</dt>
                        <dd className="mt-1 text-sm text-gray-900 font-mono">{formatAbn(data.abn)}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Created At</dt>
                        <dd className="mt-1 text-sm text-gray-900">{fmtDateTime(data.createdAt)}</dd>
                      </div>
                      {data.convertedToRenewal && data.convertedAt ? (
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Converted At</dt>
                          <dd className="mt-1 text-sm text-gray-900">{fmtDateTime(data.convertedAt)}</dd>
                        </div>
                      ) : null}
                      {data.outcomeMessage ? (
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Outcome Message</dt>
                          <dd className="mt-1 text-sm text-gray-900">{data.outcomeMessage}</dd>
                        </div>
                      ) : null}
                    </dl>
                  </div>
                </div>

                <div className="overflow-hidden rounded-lg bg-white shadow">
                  <div className="border-b border-gray-200 bg-gray-50 px-6 py-3">
                    <h3 className="text-base font-semibold text-gray-900">Tracking Information</h3>
                  </div>
                  <div className="px-6 py-4">
                    <dl className="space-y-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">IP Address</dt>
                        <dd className="mt-1 text-sm text-gray-900 font-mono">{data.ipAddress ?? 'N/A'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Session ID</dt>
                        <dd className="mt-1 text-sm text-gray-900 font-mono text-xs break-all">{data.sessionId ?? 'N/A'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">User Agent</dt>
                        <dd className="mt-1 text-sm text-gray-900 text-xs break-all">{data.userAgent ?? 'N/A'}</dd>
                      </div>
                    </dl>
                  </div>
                </div>

                {data.searchLog ? (
                  <div className="overflow-hidden rounded-lg bg-white shadow">
                    <div className="border-b border-gray-200 bg-gray-50 px-6 py-3">
                      <h3 className="text-base font-semibold text-gray-900">ASIC Search Results</h3>
                    </div>
                    <div className="px-6 py-4">
                      <dl className="space-y-4">
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Search Date</dt>
                          <dd className="mt-1 text-sm text-gray-900">{fmtDateTime(data.searchLog.searchedAt)}</dd>
                        </div>
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Search Status</dt>
                          <dd className="mt-1">
                            {data.searchLog.success
                              ? <span className="inline-flex rounded-full bg-green-100 px-2 text-xs font-semibold leading-5 text-green-800">Success</span>
                              : <span className="inline-flex rounded-full bg-red-100 px-2 text-xs font-semibold leading-5 text-red-800">Failed</span>}
                          </dd>
                        </div>
                        <div>
                          <dt className="text-sm font-medium text-gray-500">Results Count</dt>
                          <dd className="mt-1 text-sm text-gray-900">{data.searchLog.resultsCount} business name(s)</dd>
                        </div>
                        {data.searchLog.errorMessage ? (
                          <div>
                            <dt className="text-sm font-medium text-gray-500">Error Message</dt>
                            <dd className="mt-1 text-sm text-red-600">{data.searchLog.errorMessage}</dd>
                          </div>
                        ) : null}
                      </dl>

                      {data.searchLog.results.length > 0 ? (
                        <div className="mt-4 pt-4 border-t border-gray-200">
                          <h4 className="text-sm font-medium text-gray-900 mb-3">Business Names Found</h4>
                          <ul className="space-y-2">
                            {data.searchLog.results.map((r) => (
                              <li key={r.id} className="flex items-center justify-between rounded-md bg-gray-50 px-3 py-2">
                                <div>
                                  <span className="text-sm font-medium text-gray-900">{r.businessName}</span>
                                  <span className="ml-2 text-xs text-gray-500">({r.accountNumber})</span>
                                </div>
                                <span className="text-xs text-gray-500">Reg: {r.registrationDate}</span>
                              </li>
                            ))}
                          </ul>
                        </div>
                      ) : null}
                    </div>
                  </div>
                ) : null}
              </div>

              {data.renewalRequests.length > 0 ? (
                <div className="mt-6 overflow-hidden rounded-lg bg-white shadow">
                  <div className="border-b border-gray-200 bg-gray-50 px-6 py-3">
                    <h3 className="text-base font-semibold text-gray-900">Renewal Requests</h3>
                  </div>
                  <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-gray-200">
                      <thead className="bg-gray-50">
                        <tr>
                          <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Business Name</th>
                          <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Status</th>
                          <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Amount</th>
                          <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Years</th>
                          <th scope="col" className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Initiated</th>
                          <th scope="col" className="relative px-6 py-3"><span className="sr-only">View</span></th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-200 bg-white">
                        {data.renewalRequests.map((r) => (
                          <tr key={r.id} className="hover:bg-gray-50">
                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{r.businessName ?? 'N/A'}</td>
                            <td className="px-6 py-4 whitespace-nowrap"><RenewalBadge status={r.status} /></td>
                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">${r.amount.toFixed(2)}</td>
                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{r.renewalYears} yr</td>
                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{fmtShort(r.initiatedAt)}</td>
                            <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                              <Link to={`/admin/renewals/${r.id}`} className="text-blue-600 hover:text-blue-900">View</Link>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ) : null}
            </>
          )}
        </div>
      </main>
    </>
  )
}
