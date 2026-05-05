import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import AdminPage from './AdminPage'
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
    <AdminPage title="Search Logs" subtitle={`${totalCount.toLocaleString()} ABN searches recorded.`} classification="Searches · Live">
      <div className="space-y-6">
          {/* Toolbar */}
          <div className="bureau-card">
            <div className="px-4 py-3 sm:px-5 flex items-center justify-between gap-3 border-b border-[var(--hairline)]" style={{ background: 'var(--paper-deep)' }}>
              <div className="bureau-label">Section · Search Toolbar</div>
              <div className="flex items-center gap-2">
                <ViewToggle<ViewMode> value={viewMode} onChange={setViewMode} options={[
                  { value: 'Table', icon: <IconTable />, label: 'Table' },
                  { value: 'Cards', icon: <IconCards />, label: 'Cards' },
                ]} />

                <div className="relative">
                  <button onClick={() => (showPopover ? setShowPopover(false) : openPopover())} className={`bureau-btn ${showPopover ? 'bureau-btn-active' : ''}`}>
                    <IconFilter />
                    Filters {hasActiveFilters ? `(${activeFilterCount})` : ''}
                  </button>
                  <FilterPopover open={showPopover} onClose={() => setShowPopover(false)} title="Searches">
                    <div className="space-y-3">
                      <div>
                        <label className="bureau-label mb-1.5 block">ABN</label>
                        <input type="text" value={tempAbn} onChange={(e) => setTempAbn(e.target.value)} placeholder="Search ABN" className="bureau-input" />
                      </div>
                      <div>
                        <label className="bureau-label mb-1.5 block">Status</label>
                        <select value={tempSuccess} onChange={(e) => setTempSuccess(e.target.value)} className="bureau-select">
                          <option value="">All</option>
                          <option value="true">Success</option>
                          <option value="false">Failed</option>
                        </select>
                      </div>
                      <div>
                        <label className="bureau-label mb-1.5 block">Initiated By</label>
                        <select value={tempInitiatedBy} onChange={(e) => setTempInitiatedBy(e.target.value)} className="bureau-select">
                          <option value="">All</option>
                          <option value="Customer">Customer</option>
                          <option value="Admin">Admin</option>
                          <option value="System">System (Ontraport/Bulk)</option>
                        </select>
                      </div>
                      <div className="grid grid-cols-2 gap-2">
                        <div>
                          <label className="bureau-label mb-1.5 block">From Date</label>
                          <input type="date" value={tempDateFrom} onChange={(e) => setTempDateFrom(e.target.value)} className="bureau-input" />
                        </div>
                        <div>
                          <label className="bureau-label mb-1.5 block">To Date</label>
                          <input type="date" value={tempDateTo} onChange={(e) => setTempDateTo(e.target.value)} className="bureau-input" />
                        </div>
                      </div>
                    </div>
                    <div className="mt-4 flex gap-2">
                      <button onClick={applyAndClose} className="bureau-btn bureau-btn-primary flex-1 justify-center">Apply</button>
                      <button onClick={clearAndClose} className="bureau-btn">Clear</button>
                    </div>
                  </FilterPopover>
                </div>

                <button onClick={() => void load()} disabled={isRefreshing} className="bureau-btn">
                  <IconRefresh spinning={isRefreshing} />
                </button>
              </div>
            </div>

            {hasActiveFilters ? (
              <div className="flex flex-wrap gap-2 px-4 py-3 sm:px-5">
                {filterAbn ? <FilterChip label={`ABN ${filterAbn}`} onRemove={() => { setPage(1); setFilterAbn('') }} /> : null}
                {filterSuccess ? <FilterChip label={`Status ${filterSuccess === 'true' ? 'Success' : 'Failed'}`} onRemove={() => { setPage(1); setFilterSuccess('') }} /> : null}
                {filterInitiatedBy ? <FilterChip label={`By ${filterInitiatedBy}`} onRemove={() => { setPage(1); setFilterInitiatedBy('') }} /> : null}
                {filterDateFrom ? <FilterChip label={`From ${fmtDateRange(filterDateFrom)}`} onRemove={() => { setPage(1); setFilterDateFrom('') }} /> : null}
                {filterDateTo ? <FilterChip label={`To ${fmtDateRange(filterDateTo)}`} onRemove={() => { setPage(1); setFilterDateTo('') }} /> : null}
                <button onClick={clearAll} className="bureau-chip" style={{ color: 'var(--stamp)', borderColor: 'var(--stamp)' }}>
                  Clear all
                </button>
              </div>
            ) : null}
          </div>

          {viewMode === 'Cards' ? (
            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              {searches.map((s) => (
                <div key={s.id} className="bureau-card relative">
                  <div
                    className="absolute left-0 top-0 h-full w-[3px]"
                    style={{ background: s.success ? 'var(--verdict)' : 'var(--stamp)' }}
                  />
                  <div className="px-5 py-5">
                    <div className="mb-4 flex items-start justify-between gap-4">
                      <div className="min-w-0 flex-1">
                        <div className="bureau-mono text-[15px] font-medium" style={{ color: 'var(--ink)', letterSpacing: '0.02em' }}>
                          ABN {s.abn}
                        </div>
                        <div className="bureau-meta mt-1 truncate">Session {s.sessionId ?? '—'}</div>
                      </div>
                      <div className="flex items-center gap-1.5">
                        <span className={s.success ? 'bureau-stamp bureau-stamp-ok' : 'bureau-stamp bureau-stamp-fail'}>
                          {s.success ? 'OK' : 'Failed'}
                        </span>
                        {!s.success && s.errorMessage ? (
                          <button
                            onClick={() => setErrorModal(s.errorMessage ?? '')}
                            className="bureau-btn"
                            style={{ padding: '4px 8px' }}
                            title="View error details"
                          >
                            ⚠
                          </button>
                        ) : null}
                      </div>
                    </div>

                    <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm">
                      <Cell label="Date">
                        <div style={{ color: 'var(--ink)' }}>{fmtDate(s.searchedAt)}</div>
                        <div className="bureau-mono text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtTime(s.searchedAt)}</div>
                      </Cell>
                      <Cell label="Results">
                        <span className="bureau-mono" style={{ color: 'var(--ink)', fontSize: 14 }}>{s.resultsCount}</span>
                      </Cell>
                      <Cell label="Initiated by">
                        <span style={{ color: s.initiatedBy === 'Admin' ? 'var(--stamp)' : 'var(--ink)' }}>{s.initiatedBy}</span>
                      </Cell>
                      <Cell label="IP">
                        <span className="bureau-mono text-[12px]" style={{ color: 'var(--ink-soft)' }}>{s.ipAddress ?? '—'}</span>
                      </Cell>
                      <Cell label="Has renewal">
                        {s.hasRenewal ? (
                          <span style={{ color: 'var(--verdict)' }}>● Yes</span>
                        ) : (
                          <span style={{ color: 'var(--ledger-soft)' }}>○ No</span>
                        )}
                      </Cell>
                    </dl>

                    <div className="mt-4 flex items-center justify-end border-t border-[var(--hairline)] pt-3">
                      <Link to={`/admin/searches/${s.id}`} className="bureau-btn">
                        Open dossier →
                      </Link>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="bureau-card">
              <div className="overflow-x-auto">
                <table className="bureau-table">
                  <thead>
                    <tr>
                      <th>ABN</th>
                      <th>Status</th>
                      <th>Results</th>
                      <th>Initiated</th>
                      <th>Renewal</th>
                      <th>Filed</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {searches.map((s) => (
                      <tr key={s.id}>
                        <td>
                          <div className="bureau-mono text-[13px] font-medium" style={{ color: 'var(--ink)' }}>{s.abn}</div>
                          {s.sessionId ? <div className="bureau-meta" style={{ marginTop: 2 }}>SID {s.sessionId}</div> : null}
                        </td>
                        <td className="whitespace-nowrap">
                          <div className="flex items-center gap-1.5">
                            <span className={s.success ? 'bureau-stamp bureau-stamp-ok' : 'bureau-stamp bureau-stamp-fail'}>
                              {s.success ? 'OK' : 'Failed'}
                            </span>
                            {!s.success && s.errorMessage ? (
                              <button
                                onClick={() => setErrorModal(s.errorMessage ?? '')}
                                title="View error"
                                className="bureau-mono text-[12px]"
                                style={{ color: 'var(--stamp)' }}
                              >
                                ⓘ
                              </button>
                            ) : null}
                          </div>
                        </td>
                        <td className="bureau-mono whitespace-nowrap" style={{ color: 'var(--ink)' }}>{s.resultsCount}</td>
                        <td className="whitespace-nowrap">
                          <span style={{ color: s.initiatedBy === 'Admin' ? 'var(--stamp)' : 'var(--ink)' }}>{s.initiatedBy}</span>
                        </td>
                        <td className="whitespace-nowrap">
                          {s.hasRenewal ? <span style={{ color: 'var(--verdict)' }}>● Yes</span> : <span style={{ color: 'var(--ledger-soft)' }}>○ No</span>}
                        </td>
                        <td className="whitespace-nowrap">
                          <div style={{ color: 'var(--ink)' }}>{fmtDate(s.searchedAt)}</div>
                          <div className="bureau-mono text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtTime(s.searchedAt)}</div>
                        </td>
                        <td className="whitespace-nowrap text-right">
                          <Link
                            to={`/admin/searches/${s.id}`}
                            className="bureau-mono text-[11px] uppercase tracking-wider"
                            style={{ color: 'var(--stamp)', borderBottom: '1px solid var(--stamp)', paddingBottom: 1 }}
                          >
                            Open
                          </Link>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          <Pagination page={page} pageSize={PAGE_SIZE} total={totalCount} onPage={setPage} />

          <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Search Error Details" />
      </div>
    </AdminPage>
  )
}

function Cell({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="bureau-label" style={{ marginBottom: 3 }}>{label}</dt>
      <dd className="text-[13px]">{children}</dd>
    </div>
  )
}
