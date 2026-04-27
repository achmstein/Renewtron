import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { ErrorModal, FilterChip, FilterPopover, IconCards, IconFilter, IconRefresh, IconTable, Pagination, ViewToggle } from './_components'

type Renewal = Awaited<ReturnType<typeof api.admin.renewals>>['items'][number]

type ViewMode = 'Table' | 'Cards'
const PAGE_SIZE = 10

function fmtDate(s: string) { return new Date(s).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) }
function fmtTime(s: string) { return new Date(s).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }) }
function fmtDateRange(s: string) { return new Date(s).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) }

function StatusBadge({ r, onError }: { r: Renewal; onError: (msg: string) => void }) {
  if (r.status === 'Completed') return <span className="inline-flex rounded-full bg-green-100 px-2.5 py-1 text-xs font-semibold text-green-800">Completed</span>
  if (r.status === 'Processing') return <span className="inline-flex rounded-full bg-blue-100 px-2.5 py-1 text-xs font-semibold text-blue-800">Processing</span>
  if (r.status === 'Pending') return <span className="inline-flex rounded-full bg-yellow-100 px-2.5 py-1 text-xs font-semibold text-yellow-800">Pending</span>
  return (
    <div className="flex items-center gap-1.5">
      <span className="inline-flex rounded-full bg-red-100 px-2.5 py-1 text-xs font-semibold text-red-800">Failed</span>
      {r.errorMessage ? (
        <button onClick={() => onError(r.errorMessage ?? '')} className="inline-flex items-center justify-center h-6 w-6 rounded-full bg-red-100 text-red-600 hover:bg-red-200" title="View error details">
          <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
          </svg>
        </button>
      ) : null}
    </div>
  )
}

function canRetry(r: Renewal) {
  return r.status === 'Failed' && ((r.paymentType === 'Stripe' && r.stripePaymentSucceeded) || r.paymentType === 'External')
}

export default function Renewals() {
  const [viewMode, setViewMode] = useState<ViewMode>('Table')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<{ totalCount: number; items: Renewal[] } | null>(null)
  const [isRefreshing, setIsRefreshing] = useState(false)

  const [filterAbn, setFilterAbn] = useState('')
  const [filterStatus, setFilterStatus] = useState('')
  const [filterInitiatedBy, setFilterInitiatedBy] = useState('')
  const [filterDateFrom, setFilterDateFrom] = useState('')
  const [filterDateTo, setFilterDateTo] = useState('')

  const [showPopover, setShowPopover] = useState(false)
  const [tempAbn, setTempAbn] = useState('')
  const [tempStatus, setTempStatus] = useState('')
  const [tempInitiatedBy, setTempInitiatedBy] = useState('')
  const [tempDateFrom, setTempDateFrom] = useState('')
  const [tempDateTo, setTempDateTo] = useState('')

  const [retryingId, setRetryingId] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState('')
  const [errorMessage, setErrorMessage] = useState('')
  const [errorModal, setErrorModal] = useState<string | null>(null)

  const load = useMemo(() => async () => {
    setIsRefreshing(true)
    try {
      const r = await api.admin.renewals({
        abn: filterAbn || undefined,
        status: filterStatus || undefined,
        initiatedBy: filterInitiatedBy || undefined,
        dateFrom: filterDateFrom || undefined,
        dateTo: filterDateTo || undefined,
        page,
        pageSize: PAGE_SIZE,
      })
      setData({ totalCount: r.totalCount, items: r.items })
    } finally {
      setIsRefreshing(false)
    }
  }, [filterAbn, filterStatus, filterInitiatedBy, filterDateFrom, filterDateTo, page])

  useEffect(() => { void load() }, [load])

  // Auto-refresh while there are Processing/Pending renewals
  const pollRef = useRef<number | undefined>(undefined)
  useEffect(() => {
    if (!data) return
    const needsPolling = data.items.some((r) => r.status === 'Processing' || r.status === 'Pending')
    if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = undefined }
    if (needsPolling) {
      pollRef.current = window.setInterval(() => { void load() }, 5000)
    }
    return () => {
      if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = undefined }
    }
  }, [data, load])

  const totalCount = data?.totalCount ?? 0
  const renewals = data?.items ?? []

  const activeFilterCount = [filterAbn, filterStatus, filterInitiatedBy, filterDateFrom, filterDateTo].filter(Boolean).length
  const hasActiveFilters = activeFilterCount > 0

  const openPopover = () => {
    setTempAbn(filterAbn); setTempStatus(filterStatus); setTempInitiatedBy(filterInitiatedBy); setTempDateFrom(filterDateFrom); setTempDateTo(filterDateTo)
    setShowPopover(true)
  }
  const applyAndClose = () => {
    setPage(1)
    setFilterAbn(tempAbn); setFilterStatus(tempStatus); setFilterInitiatedBy(tempInitiatedBy); setFilterDateFrom(tempDateFrom); setFilterDateTo(tempDateTo)
    setShowPopover(false)
  }
  const clearAndClose = () => {
    setPage(1)
    setTempAbn(''); setTempStatus(''); setTempInitiatedBy(''); setTempDateFrom(''); setTempDateTo('')
    setFilterAbn(''); setFilterStatus(''); setFilterInitiatedBy(''); setFilterDateFrom(''); setFilterDateTo('')
    setShowPopover(false)
  }
  const clearAll = () => {
    setPage(1)
    setFilterAbn(''); setFilterStatus(''); setFilterInitiatedBy(''); setFilterDateFrom(''); setFilterDateTo('')
  }

  const retry = async (id: string) => {
    setRetryingId(id); setSuccessMessage(''); setErrorMessage('')
    try {
      const r = await api.admin.retryRenewal(id)
      setSuccessMessage(r.message)
      await load()
    } catch (e) {
      setErrorMessage(e instanceof Error ? e.message : 'Retry failed')
    } finally {
      setRetryingId(null)
    }
  }

  const isRetrying = retryingId !== null

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <h1 className="text-lg/6 font-semibold text-gray-900">Renewal Requests</h1>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {successMessage ? (
            <div className="mb-4 rounded-md bg-green-50 p-4">
              <div className="flex">
                <div className="shrink-0">
                  <svg className="h-5 w-5 text-green-400" viewBox="0 0 20 20" fill="currentColor">
                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                  </svg>
                </div>
                <div className="ml-3"><p className="text-sm font-medium text-green-800">{successMessage}</p></div>
              </div>
            </div>
          ) : null}
          {errorMessage ? (
            <div className="mb-4 rounded-md bg-red-50 p-4">
              <div className="flex">
                <div className="shrink-0">
                  <svg className="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.28 7.22a.75.75 0 00-1.06 1.06L8.94 10l-1.72 1.72a.75.75 0 101.06 1.06L10 11.06l1.72 1.72a.75.75 0 101.06-1.06L11.06 10l1.72-1.72a.75.75 0 00-1.06-1.06L10 8.94 8.28 7.22z" clipRule="evenodd" />
                  </svg>
                </div>
                <div className="ml-3"><p className="text-sm font-medium text-red-800">{errorMessage}</p></div>
              </div>
            </div>
          ) : null}

          {/* Header */}
          <div className="mb-6 rounded-lg bg-white shadow">
            <div className="px-4 py-5 sm:px-6 flex items-center justify-between">
              <div>
                <h3 className="text-lg font-medium leading-6 text-gray-900">All Renewal Requests</h3>
                <p className="mt-1 text-sm text-gray-500">{totalCount} total renewals</p>
              </div>
              <div className="flex items-center gap-2">
                <ViewToggle<ViewMode> value={viewMode} onChange={setViewMode} options={[
                  { value: 'Table', icon: <IconTable />, label: 'Table' },
                  { value: 'Cards', icon: <IconCards />, label: 'Cards' },
                ]} />

                <div className="relative">
                  <button onClick={() => (showPopover ? setShowPopover(false) : openPopover())} className={`${showPopover ? 'bg-gray-100' : 'bg-white'} inline-flex items-center rounded-md px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50`}>
                    <IconFilter />
                    Filters {hasActiveFilters ? `(${activeFilterCount})` : ''}
                  </button>
                  <FilterPopover open={showPopover} onClose={() => setShowPopover(false)} title="Filter Renewals">
                    <div className="space-y-3">
                      <div>
                        <label className="block text-xs font-medium text-gray-700 mb-1">ABN</label>
                        <input type="text" value={tempAbn} onChange={(e) => setTempAbn(e.target.value)} placeholder="Search ABN" className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm px-3 py-2" />
                      </div>
                      <div>
                        <label className="block text-xs font-medium text-gray-700 mb-1">Status</label>
                        <select value={tempStatus} onChange={(e) => setTempStatus(e.target.value)} className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm px-3 py-2">
                          <option value="">All Statuses</option>
                          <option value="Pending">Pending</option>
                          <option value="Processing">Processing</option>
                          <option value="Completed">Completed</option>
                          <option value="Failed">Failed</option>
                        </select>
                      </div>
                      <div>
                        <label className="block text-xs font-medium text-gray-700 mb-1">Initiated By</label>
                        <select value={tempInitiatedBy} onChange={(e) => setTempInitiatedBy(e.target.value)} className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm px-3 py-2">
                          <option value="">All</option>
                          <option value="Customer">Customer</option>
                          <option value="Admin">Admin</option>
                          <option value="System">System (Ontraport/Bulk)</option>
                        </select>
                      </div>
                      <div className="grid grid-cols-2 gap-2">
                        <div>
                          <label className="block text-xs font-medium text-gray-700 mb-1">From Date</label>
                          <input type="date" value={tempDateFrom} onChange={(e) => setTempDateFrom(e.target.value)} className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm px-3 py-2" />
                        </div>
                        <div>
                          <label className="block text-xs font-medium text-gray-700 mb-1">To Date</label>
                          <input type="date" value={tempDateTo} onChange={(e) => setTempDateTo(e.target.value)} className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm px-3 py-2" />
                        </div>
                      </div>
                    </div>
                    <div className="mt-4 flex gap-2">
                      <button onClick={applyAndClose} className="flex-1 inline-flex justify-center items-center rounded-md bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-500">Apply</button>
                      <button onClick={clearAndClose} className="inline-flex items-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50">Clear</button>
                    </div>
                  </FilterPopover>
                </div>

                <button onClick={() => void load()} disabled={isRefreshing} className="inline-flex items-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed">
                  <IconRefresh spinning={isRefreshing} />
                </button>
              </div>
            </div>

            {hasActiveFilters ? (
              <div className="px-4 pb-4 sm:px-6 flex flex-wrap gap-2">
                {filterAbn ? <FilterChip label={`ABN: ${filterAbn}`} onRemove={() => { setPage(1); setFilterAbn('') }} /> : null}
                {filterStatus ? <FilterChip label={`Status: ${filterStatus}`} onRemove={() => { setPage(1); setFilterStatus('') }} /> : null}
                {filterInitiatedBy ? <FilterChip label={`Initiated By: ${filterInitiatedBy}`} onRemove={() => { setPage(1); setFilterInitiatedBy('') }} /> : null}
                {filterDateFrom ? <FilterChip label={`From: ${fmtDateRange(filterDateFrom)}`} onRemove={() => { setPage(1); setFilterDateFrom('') }} /> : null}
                {filterDateTo ? <FilterChip label={`To: ${fmtDateRange(filterDateTo)}`} onRemove={() => { setPage(1); setFilterDateTo('') }} /> : null}
                <button onClick={clearAll} className="inline-flex items-center gap-x-1 rounded-full bg-gray-100 px-3 py-1 text-xs font-medium text-gray-700 hover:bg-gray-200">Clear all</button>
              </div>
            ) : null}
          </div>

          {viewMode === 'Cards' ? (
            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              {renewals.map((r) => (
                <div key={r.id} className={`overflow-hidden rounded-lg bg-white shadow hover:shadow-lg transition-shadow border-l-4 ${r.status === 'Completed' ? 'border-green-500' : r.status === 'Failed' ? 'border-red-500' : 'border-yellow-500'}`}>
                  <div className="px-4 py-5 sm:p-6">
                    <div className="flex items-start justify-between mb-4">
                      <div className="flex-1">
                        <h3 className="text-base font-semibold text-gray-900">{r.businessName}</h3>
                        <p className="text-sm text-gray-500 mt-1">ABN: {r.abn}</p>
                      </div>
                      <div><StatusBadge r={r} onError={setErrorModal} /></div>
                    </div>

                    <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
                      <div>
                        <dt className="text-gray-500">Date &amp; Time</dt>
                        <dd className="mt-1 font-medium text-gray-900">{fmtDate(r.initiatedAt)}</dd>
                        <dd className="text-xs text-gray-500">{fmtTime(r.initiatedAt)}</dd>
                      </div>
                      <div>
                        <dt className="text-gray-500">Amount</dt>
                        <dd className="mt-1 font-medium text-gray-900">${r.amount.toFixed(2)}</dd>
                      </div>
                      <div>
                        <dt className="text-gray-500">Initiated By</dt>
                        <dd className="mt-1 text-gray-900">{r.initiatedByLabel}</dd>
                      </div>
                      <div>
                        <dt className="text-gray-500">Renewal Period</dt>
                        <dd className="mt-1 font-medium text-gray-900">{r.renewalYears} {r.renewalYears === 1 ? 'Year' : 'Years'}</dd>
                      </div>
                      <div>
                        <dt className="text-gray-500">Email</dt>
                        <dd className="mt-1 text-gray-900 truncate">{r.email ?? '—'}</dd>
                      </div>
                      <div>
                        <dt className="text-gray-500">Transaction Reference</dt>
                        <dd className="mt-1 font-mono text-xs text-gray-900">{r.transactionReference ?? 'N/A'}</dd>
                      </div>
                    </dl>

                    <div className="mt-4 flex items-center justify-end gap-2 pt-4 border-t border-gray-200">
                      <Link to={`/admin/renewals/${r.id}`} className="inline-flex items-center rounded-md bg-white px-3 py-1.5 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50">
                        <svg className="h-4 w-4 mr-1.5" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M2.036 12.322a1.012 1.012 0 010-.639C3.423 7.51 7.36 4.5 12 4.5c4.638 0 8.573 3.007 9.963 7.178.07.207.07.431 0 .639C20.577 16.49 16.64 19.5 12 19.5c-4.638 0-8.573-3.007-9.963-7.178z" />
                          <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                        </svg>
                        View Details
                      </Link>
                      {canRetry(r) ? (
                        <button onClick={() => retry(r.id)} disabled={isRetrying} className="inline-flex items-center rounded-md bg-blue-600 px-3 py-1.5 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed">
                          {retryingId === r.id ? (
                            <>
                              <svg className="animate-spin h-4 w-4 mr-1.5" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                              </svg>
                              <span>Retrying...</span>
                            </>
                          ) : (
                            <>
                              <svg className="h-4 w-4 mr-1.5" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
                              </svg>
                              <span>Retry</span>
                            </>
                          )}
                        </button>
                      ) : null}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="overflow-hidden rounded-lg bg-white shadow">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Business/ABN</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Status</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Amount</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Period</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Initiated By</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Date</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200 bg-white">
                  {renewals.map((r) => (
                    <tr key={r.id} className="hover:bg-gray-50">
                      <td className="px-6 py-4">
                        <div className="text-sm font-medium text-gray-900">{r.businessName}</div>
                        <div className="text-sm text-gray-500">ABN: {r.abn}</div>
                      </td>
                      <td className="whitespace-nowrap px-6 py-4">
                        <StatusBadge r={r} onError={setErrorModal} />
                      </td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-900">${r.amount.toFixed(2)}</td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-900">{r.renewalYears} {r.renewalYears === 1 ? 'Year' : 'Years'}</td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-900">
                        {r.initiatedByLabel === 'Admin' ? <span className="text-blue-600 font-medium">Admin</span> : <span className="text-gray-900">Customer</span>}
                      </td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-500">
                        <div>{fmtDate(r.initiatedAt)}</div>
                        <div className="text-xs">{fmtTime(r.initiatedAt)}</div>
                      </td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm">
                        <div className="flex items-center gap-2">
                          <Link to={`/admin/renewals/${r.id}`} className="inline-flex items-center text-blue-600 hover:text-blue-900">View</Link>
                          {canRetry(r) ? (
                            <button onClick={() => retry(r.id)} disabled={isRetrying} className="inline-flex items-center text-blue-600 hover:text-blue-900 disabled:opacity-50">Retry</button>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <Pagination page={page} pageSize={PAGE_SIZE} total={totalCount} onPage={setPage} />

          <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Renewal Error Details" />
        </div>
      </main>
    </>
  )
}
