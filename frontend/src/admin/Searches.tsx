import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { ErrorModal, FilterChip, FilterPopover, IconCards, IconFilter, IconRefresh, IconTable, Pagination, ViewToggle } from './_components'

type Search = {
  id: string
  abn: string
  sessionId?: string | null
  searchedAt: string
  success: boolean
  errorMessage?: string | null
  resultsCount: number
  initiatedBy: string
  ipAddress?: string | null
  hasRenewal: boolean
}

type ViewMode = 'Table' | 'Cards'

const PAGE_SIZE = 10

function fmtDate(s: string) {
  const d = new Date(s)
  return d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
}
function fmtTime(s: string) {
  const d = new Date(s)
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
}
function fmtDateRange(s: string) {
  const d = new Date(s)
  return d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
}

export default function Searches() {
  const [viewMode, setViewMode] = useState<ViewMode>('Table')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<{ totalCount: number; items: Search[] } | null>(null)
  const [isRefreshing, setIsRefreshing] = useState(false)

  const [filterAbn, setFilterAbn] = useState('')
  const [filterSuccess, setFilterSuccess] = useState('')
  const [filterInitiatedBy, setFilterInitiatedBy] = useState('')
  const [filterDateFrom, setFilterDateFrom] = useState('')
  const [filterDateTo, setFilterDateTo] = useState('')

  const [showPopover, setShowPopover] = useState(false)
  const [tempAbn, setTempAbn] = useState('')
  const [tempSuccess, setTempSuccess] = useState('')
  const [tempInitiatedBy, setTempInitiatedBy] = useState('')
  const [tempDateFrom, setTempDateFrom] = useState('')
  const [tempDateTo, setTempDateTo] = useState('')

  const [errorModal, setErrorModal] = useState<string | null>(null)

  const load = useMemo(() => async () => {
    setIsRefreshing(true)
    try {
      const r = await api.admin.searches({
        abn: filterAbn || undefined,
        success: filterSuccess || undefined,
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
  }, [filterAbn, filterSuccess, filterInitiatedBy, filterDateFrom, filterDateTo, page])

  useEffect(() => { void load() }, [load])

  const totalCount = data?.totalCount ?? 0
  const searches = data?.items ?? []

  const activeFilterCount = [filterAbn, filterSuccess, filterInitiatedBy, filterDateFrom, filterDateTo].filter(Boolean).length
  const hasActiveFilters = activeFilterCount > 0

  const openPopover = () => {
    setTempAbn(filterAbn); setTempSuccess(filterSuccess); setTempInitiatedBy(filterInitiatedBy); setTempDateFrom(filterDateFrom); setTempDateTo(filterDateTo)
    setShowPopover(true)
  }
  const applyAndClose = () => {
    setPage(1)
    setFilterAbn(tempAbn); setFilterSuccess(tempSuccess); setFilterInitiatedBy(tempInitiatedBy); setFilterDateFrom(tempDateFrom); setFilterDateTo(tempDateTo)
    setShowPopover(false)
  }
  const clearAndClose = () => {
    setPage(1)
    setTempAbn(''); setTempSuccess(''); setTempInitiatedBy(''); setTempDateFrom(''); setTempDateTo('')
    setFilterAbn(''); setFilterSuccess(''); setFilterInitiatedBy(''); setFilterDateFrom(''); setFilterDateTo('')
    setShowPopover(false)
  }
  const clearAll = () => {
    setPage(1)
    setFilterAbn(''); setFilterSuccess(''); setFilterInitiatedBy(''); setFilterDateFrom(''); setFilterDateTo('')
  }

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <h1 className="text-lg/6 font-semibold text-gray-900">Search Logs</h1>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {/* Header */}
          <div className="mb-6 rounded-lg bg-white shadow">
            <div className="px-4 py-5 sm:px-6 flex items-center justify-between">
              <div>
                <h3 className="text-lg font-medium leading-6 text-gray-900">Search Logs</h3>
                <p className="mt-1 text-sm text-gray-500">{totalCount} total searches</p>
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
                  <FilterPopover open={showPopover} onClose={() => setShowPopover(false)} title="Filter Searches">
                    <div className="space-y-3">
                      <div>
                        <label className="block text-xs font-medium text-gray-700 mb-1">ABN</label>
                        <input type="text" value={tempAbn} onChange={(e) => setTempAbn(e.target.value)} placeholder="Search ABN" className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm px-3 py-2" />
                      </div>
                      <div>
                        <label className="block text-xs font-medium text-gray-700 mb-1">Status</label>
                        <select value={tempSuccess} onChange={(e) => setTempSuccess(e.target.value)} className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm px-3 py-2">
                          <option value="">All</option>
                          <option value="true">Success</option>
                          <option value="false">Failed</option>
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
                {filterSuccess ? <FilterChip label={`Status: ${filterSuccess === 'true' ? 'Success' : 'Failed'}`} onRemove={() => { setPage(1); setFilterSuccess('') }} /> : null}
                {filterInitiatedBy ? <FilterChip label={`Initiated By: ${filterInitiatedBy}`} onRemove={() => { setPage(1); setFilterInitiatedBy('') }} /> : null}
                {filterDateFrom ? <FilterChip label={`From: ${fmtDateRange(filterDateFrom)}`} onRemove={() => { setPage(1); setFilterDateFrom('') }} /> : null}
                {filterDateTo ? <FilterChip label={`To: ${fmtDateRange(filterDateTo)}`} onRemove={() => { setPage(1); setFilterDateTo('') }} /> : null}
                <button onClick={clearAll} className="inline-flex items-center gap-x-1 rounded-full bg-gray-100 px-3 py-1 text-xs font-medium text-gray-700 hover:bg-gray-200">Clear all</button>
              </div>
            ) : null}
          </div>

          {viewMode === 'Cards' ? (
            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              {searches.map((s) => (
                <div key={s.id} className={`overflow-hidden rounded-lg bg-white shadow hover:shadow-lg transition-shadow border-l-4 ${s.success ? 'border-green-500' : 'border-red-500'}`}>
                  <div className="px-4 py-5 sm:p-6">
                    <div className="flex items-start justify-between mb-4">
                      <div className="flex-1">
                        <h3 className="text-base font-semibold text-gray-900">ABN: {s.abn}</h3>
                        <p className="text-xs text-gray-500 mt-1 font-mono">Session: {s.sessionId ?? '—'}</p>
                      </div>
                      <div>
                        {s.success ? (
                          <span className="inline-flex rounded-full bg-green-100 px-2.5 py-1 text-xs font-semibold text-green-800">Success</span>
                        ) : (
                          <div className="flex items-center gap-1.5">
                            <span className="inline-flex rounded-full bg-red-100 px-2.5 py-1 text-xs font-semibold text-red-800">Failed</span>
                            {s.errorMessage ? (
                              <button onClick={() => setErrorModal(s.errorMessage ?? '')} className="inline-flex items-center justify-center h-6 w-6 rounded-full bg-red-100 text-red-600 hover:bg-red-200" title="View error details">
                                <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
                                </svg>
                              </button>
                            ) : null}
                          </div>
                        )}
                      </div>
                    </div>

                    <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
                      <div>
                        <dt className="text-gray-500">Date &amp; Time</dt>
                        <dd className="mt-1 font-medium text-gray-900">{fmtDate(s.searchedAt)}</dd>
                        <dd className="text-xs text-gray-500">{fmtTime(s.searchedAt)}</dd>
                      </div>
                      <div>
                        <dt className="text-gray-500">Results Count</dt>
                        <dd className="mt-1 font-medium text-gray-900">{s.resultsCount}</dd>
                      </div>
                      <div>
                        <dt className="text-gray-500">Initiated By</dt>
                        <dd className="mt-1 text-gray-900">{s.initiatedBy}</dd>
                      </div>
                      <div>
                        <dt className="text-gray-500">IP Address</dt>
                        <dd className="mt-1 text-gray-900">{s.ipAddress ?? '—'}</dd>
                      </div>
                      <div>
                        <dt className="text-gray-500">Has Renewal</dt>
                        <dd className="mt-1 text-gray-900">
                          {s.hasRenewal ? (
                            <span className="inline-flex items-center gap-1 text-green-600 font-medium">
                              <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                              </svg>
                              Yes
                            </span>
                          ) : <span className="text-gray-500">No</span>}
                        </dd>
                      </div>
                    </dl>

                    <div className="mt-4 flex items-center justify-end gap-2 pt-4 border-t border-gray-200">
                      <Link to={`/admin/searches/${s.id}`} className="inline-flex items-center rounded-md bg-white px-3 py-1.5 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50">
                        <svg className="h-4 w-4 mr-1.5" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M2.036 12.322a1.012 1.012 0 010-.639C3.423 7.51 7.36 4.5 12 4.5c4.638 0 8.573 3.007 9.963 7.178.07.207.07.431 0 .639C20.577 16.49 16.64 19.5 12 19.5c-4.638 0-8.573-3.007-9.963-7.178z" />
                          <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                        </svg>
                        View Details
                      </Link>
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
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">ABN</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Status</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Results</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Initiated By</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Has Renewal</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Date</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200 bg-white">
                  {searches.map((s) => (
                    <tr key={s.id} className="hover:bg-gray-50">
                      <td className="px-6 py-4">
                        <div className="text-sm font-medium text-gray-900">{s.abn}</div>
                        <div className="text-xs text-gray-500 font-mono">{s.sessionId ?? ''}</div>
                      </td>
                      <td className="whitespace-nowrap px-6 py-4">
                        {s.success ? (
                          <span className="inline-flex rounded-full bg-green-100 px-2.5 py-1 text-xs font-semibold text-green-800">Success</span>
                        ) : (
                          <div className="flex items-center gap-1.5">
                            <span className="inline-flex rounded-full bg-red-100 px-2.5 py-1 text-xs font-semibold text-red-800">Failed</span>
                            {s.errorMessage ? (
                              <button onClick={() => setErrorModal(s.errorMessage ?? '')} className="inline-flex items-center justify-center h-6 w-6 rounded-full bg-red-100 text-red-600 hover:bg-red-200" title="View error details">
                                <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
                                </svg>
                              </button>
                            ) : null}
                          </div>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-900">{s.resultsCount}</td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-900">
                        {s.initiatedBy === 'Admin' ? <span className="text-blue-600 font-medium">Admin</span> : <span className="text-gray-900">{s.initiatedBy}</span>}
                      </td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm">
                        {s.hasRenewal ? (
                          <span className="inline-flex items-center gap-1 text-green-600 font-medium">
                            <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                              <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                            </svg>
                            Yes
                          </span>
                        ) : <span className="text-gray-500">No</span>}
                      </td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-500">
                        <div>{fmtDate(s.searchedAt)}</div>
                        <div className="text-xs">{fmtTime(s.searchedAt)}</div>
                      </td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm">
                        <Link to={`/admin/searches/${s.id}`} className="inline-flex items-center text-blue-600 hover:text-blue-900">View</Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <Pagination page={page} pageSize={PAGE_SIZE} total={totalCount} onPage={setPage} />

          <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Search Error Details" />
        </div>
      </main>
    </>
  )
}
