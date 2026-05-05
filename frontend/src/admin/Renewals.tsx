import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { Banner } from './_shared'
import { ErrorModal, FilterChip, FilterPopover, IconCards, IconFilter, IconRefresh, IconTable, Pagination, ViewToggle } from './_components'

type Renewal = Awaited<ReturnType<typeof api.admin.renewals>>['items'][number]

type ViewMode = 'Table' | 'Cards'
const PAGE_SIZE = 10

function fmtDate(s: string) { return new Date(s).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) }
function fmtTime(s: string) { return new Date(s).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }) }
function fmtDateRange(s: string) { return new Date(s).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) }

function StatusStamp({ r, onError }: { r: Renewal; onError: (msg: string) => void }) {
  if (r.status === 'Completed') return <span className="bureau-stamp bureau-stamp-ok">Completed</span>
  if (r.status === 'Processing') return <span className="bureau-stamp bureau-stamp-info">Processing</span>
  if (r.status === 'Pending') return <span className="bureau-stamp bureau-stamp-warn">Pending</span>
  return (
    <div className="flex items-center gap-1.5">
      <span className="bureau-stamp bureau-stamp-fail">Failed</span>
      {r.errorMessage ? (
        <button
          onClick={() => onError(r.errorMessage ?? '')}
          title="View incident report"
          className="bureau-mono text-[12px]"
          style={{ color: 'var(--stamp)' }}
        >
          ⓘ
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
    <AdminPage title="Renewal Requests" subtitle={`${totalCount.toLocaleString()} renewals across all origins.`} classification="Renewals · Live">
      <div className="space-y-6">
        {successMessage ? <Banner kind="ok" message={successMessage} /> : null}
        {errorMessage ? <Banner kind="fail" message={errorMessage} /> : null}

        {/* Toolbar */}
        <div className="bureau-card">
          <div className="flex items-center justify-between gap-3 border-b border-[var(--hairline)] px-4 py-3 sm:px-5" style={{ background: 'var(--paper-deep)' }}>
            <div className="bureau-label">Section · Renewals Toolbar</div>
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
                <FilterPopover open={showPopover} onClose={() => setShowPopover(false)} title="Renewals">
                  <div className="space-y-3">
                    <div>
                      <label className="bureau-label mb-1.5 block">ABN</label>
                      <input type="text" value={tempAbn} onChange={(e) => setTempAbn(e.target.value)} placeholder="Search ABN" className="bureau-input" />
                    </div>
                    <div>
                      <label className="bureau-label mb-1.5 block">Status</label>
                      <select value={tempStatus} onChange={(e) => setTempStatus(e.target.value)} className="bureau-select">
                        <option value="">All Statuses</option>
                        <option value="Pending">Pending</option>
                        <option value="Processing">Processing</option>
                        <option value="Completed">Completed</option>
                        <option value="Failed">Failed</option>
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
              {filterStatus ? <FilterChip label={`Status ${filterStatus}`} onRemove={() => { setPage(1); setFilterStatus('') }} /> : null}
              {filterInitiatedBy ? <FilterChip label={`By ${filterInitiatedBy}`} onRemove={() => { setPage(1); setFilterInitiatedBy('') }} /> : null}
              {filterDateFrom ? <FilterChip label={`From ${fmtDateRange(filterDateFrom)}`} onRemove={() => { setPage(1); setFilterDateFrom('') }} /> : null}
              {filterDateTo ? <FilterChip label={`To ${fmtDateRange(filterDateTo)}`} onRemove={() => { setPage(1); setFilterDateTo('') }} /> : null}
              <button onClick={clearAll} className="bureau-chip" style={{ color: 'var(--stamp)', borderColor: 'var(--stamp)' }}>Clear all</button>
            </div>
          ) : null}
        </div>

        {/* Body */}
        {viewMode === 'Cards' ? (
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            {renewals.map((r) => {
              const accent = r.status === 'Completed' ? 'var(--verdict)' : r.status === 'Failed' ? 'var(--stamp)' : 'var(--caution)'
              return (
                <div key={r.id} className="bureau-card relative">
                  <div className="absolute left-0 top-0 h-full w-[3px]" style={{ background: accent }} />
                  <div className="px-5 py-5">
                    <div className="mb-4 flex items-start justify-between gap-4">
                      <div className="min-w-0 flex-1">
                        <h3 className="bureau-display text-[17px] font-semibold leading-tight" style={{ color: 'var(--ink)' }}>
                          {r.businessName}
                        </h3>
                        <p className="bureau-mono mt-1 text-[12px]" style={{ color: 'var(--ledger)' }}>ABN {r.abn}</p>
                      </div>
                      <StatusStamp r={r} onError={setErrorModal} />
                    </div>

                    <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm">
                      <Cell label="Filed">
                        <div style={{ color: 'var(--ink)' }}>{fmtDate(r.initiatedAt)}</div>
                        <div className="bureau-mono text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtTime(r.initiatedAt)}</div>
                      </Cell>
                      <Cell label="Amount">
                        <span className="bureau-mono" style={{ color: 'var(--ink)', fontSize: 14 }}>${r.amount.toFixed(2)}</span>
                      </Cell>
                      <Cell label="Initiated by">
                        <span style={{ color: r.initiatedByLabel === 'Admin' ? 'var(--stamp)' : 'var(--ink)' }}>{r.initiatedByLabel}</span>
                      </Cell>
                      <Cell label="Period">
                        {r.renewalYears} {r.renewalYears === 1 ? 'year' : 'years'}
                      </Cell>
                      <Cell label="Email">
                        <span className="truncate block" style={{ color: 'var(--ink)' }}>{r.email ?? '—'}</span>
                      </Cell>
                      <Cell label="Reference">
                        <span className="bureau-mono text-[11px]" style={{ color: 'var(--ink-soft)' }}>{r.transactionReference ?? '—'}</span>
                      </Cell>
                    </dl>

                    <div className="mt-4 flex items-center justify-end gap-2 border-t border-[var(--hairline)] pt-3">
                      <Link to={`/admin/renewals/${r.id}`} className="bureau-btn">Open dossier →</Link>
                      {canRetry(r) ? (
                        <button onClick={() => retry(r.id)} disabled={isRetrying} className="bureau-btn bureau-btn-stamp">
                          {retryingId === r.id ? <span className="bureau-spin inline-block">↻</span> : '↻'} Retry
                        </button>
                      ) : null}
                    </div>
                  </div>
                </div>
              )
            })}
          </div>
        ) : (
          <div className="bureau-card">
            <div className="overflow-x-auto">
              <table className="bureau-table">
                <thead>
                  <tr>
                    <th>Business / ABN</th>
                    <th>Status</th>
                    <th>Amount</th>
                    <th>Period</th>
                    <th>Initiated</th>
                    <th>Filed</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {renewals.map((r) => (
                    <tr key={r.id}>
                      <td>
                        <div className="text-[13px] font-medium" style={{ color: 'var(--ink)' }}>{r.businessName}</div>
                        <div className="bureau-meta">ABN {r.abn}</div>
                      </td>
                      <td className="whitespace-nowrap"><StatusStamp r={r} onError={setErrorModal} /></td>
                      <td className="bureau-mono whitespace-nowrap" style={{ color: 'var(--ink)' }}>${r.amount.toFixed(2)}</td>
                      <td className="whitespace-nowrap">{r.renewalYears} {r.renewalYears === 1 ? 'yr' : 'yrs'}</td>
                      <td className="whitespace-nowrap">
                        <span style={{ color: r.initiatedByLabel === 'Admin' ? 'var(--stamp)' : 'var(--ink)' }}>{r.initiatedByLabel}</span>
                      </td>
                      <td className="whitespace-nowrap">
                        <div style={{ color: 'var(--ink)' }}>{fmtDate(r.initiatedAt)}</div>
                        <div className="bureau-mono text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtTime(r.initiatedAt)}</div>
                      </td>
                      <td className="whitespace-nowrap text-right">
                        <div className="flex items-center justify-end gap-3">
                          <Link
                            to={`/admin/renewals/${r.id}`}
                            className="bureau-mono text-[11px] uppercase tracking-wider"
                            style={{ color: 'var(--stamp)', borderBottom: '1px solid var(--stamp)', paddingBottom: 1 }}
                          >
                            Open
                          </Link>
                          {canRetry(r) ? (
                            <button
                              onClick={() => retry(r.id)}
                              disabled={isRetrying}
                              className="bureau-mono text-[11px] uppercase tracking-wider"
                              style={{ color: 'var(--ink)', borderBottom: '1px solid var(--ink)', paddingBottom: 1 }}
                            >
                              {retryingId === r.id ? '↻…' : 'Retry'}
                            </button>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        <Pagination page={page} pageSize={PAGE_SIZE} total={totalCount} onPage={setPage} />

        <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Renewal Incident" />
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
