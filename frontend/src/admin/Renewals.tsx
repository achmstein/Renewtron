import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { ErrorModal, FilterPopover, Pagination } from './_components'
import { Chip, EmptyState, Field, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, Th, Toast, ViewToggle, type Tone } from './_ui'
import { durationShort, fmtDate, fmtMoney0, fmtMoney2, fmtTime } from './_utils'

type RenewalsResponse = Awaited<ReturnType<typeof api.admin.renewals>>
type Renewal = RenewalsResponse['items'][number]
type Stats = RenewalsResponse['stats']

type ViewMode = 'Table' | 'Cards'
const PAGE_SIZE = 10

function fmtDateRange(s: string) { return new Date(s).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) }

function canRetry(r: Renewal) {
  return r.status === 'Failed' && ((r.paymentType === 'Stripe' && r.stripePaymentSucceeded) || r.paymentType === 'External')
}

export default function Renewals() {
  const [viewMode, setViewMode] = useState<ViewMode>('Table')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<RenewalsResponse | null>(null)
  const [isRefreshing, setIsRefreshing] = useState(false)

  const [filterAbn, setFilterAbn] = useState('')
  const [filterStatus, setFilterStatus] = useState('')
  const [filterInitiatedBy, setFilterInitiatedBy] = useState('')
  const [filterSource, setFilterSource] = useState('')
  const [filterFailedAtStep, setFilterFailedAtStep] = useState('')
  const [filterDateFrom, setFilterDateFrom] = useState('')
  const [filterDateTo, setFilterDateTo] = useState('')

  const [showPopover, setShowPopover] = useState(false)
  const [tempAbn, setTempAbn] = useState('')
  const [tempStatus, setTempStatus] = useState('')
  const [tempInitiatedBy, setTempInitiatedBy] = useState('')
  const [tempSource, setTempSource] = useState('')
  const [tempDateFrom, setTempDateFrom] = useState('')
  const [tempDateTo, setTempDateTo] = useState('')

  const [retryingId, setRetryingId] = useState<string | null>(null)
  const [bulkRetryConfirm, setBulkRetryConfirm] = useState(false)
  const [bulkRetrying, setBulkRetrying] = useState(false)
  const [toast, setToast] = useState<{ tone: Tone; message: string } | null>(null)
  const [errorModal, setErrorModal] = useState<string | null>(null)

  const showToast = (tone: Tone, message: string) => {
    setToast({ tone, message })
    setTimeout(() => setToast(null), 4000)
  }

  const load = useMemo(() => async () => {
    setIsRefreshing(true)
    try {
      const r = await api.admin.renewals({
        abn: filterAbn || undefined,
        status: filterStatus || undefined,
        initiatedBy: filterInitiatedBy || undefined,
        source: filterSource || undefined,
        failedAtStep: filterFailedAtStep || undefined,
        dateFrom: filterDateFrom || undefined,
        dateTo: filterDateTo || undefined,
        page,
        pageSize: PAGE_SIZE,
      })
      setData(r)
    } finally {
      setIsRefreshing(false)
    }
  }, [filterAbn, filterStatus, filterInitiatedBy, filterSource, filterFailedAtStep, filterDateFrom, filterDateTo, page])

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
  const stats: Stats = data?.stats ?? defaultStats()

  const activeFilterCount = [filterAbn, filterStatus, filterInitiatedBy, filterSource, filterFailedAtStep, filterDateFrom, filterDateTo].filter(Boolean).length
  const hasActiveFilters = activeFilterCount > 0

  const openPopover = () => {
    setTempAbn(filterAbn); setTempStatus(filterStatus); setTempInitiatedBy(filterInitiatedBy); setTempSource(filterSource); setTempDateFrom(filterDateFrom); setTempDateTo(filterDateTo)
    setShowPopover(true)
  }
  const applyAndClose = () => {
    setPage(1)
    setFilterAbn(tempAbn); setFilterStatus(tempStatus); setFilterInitiatedBy(tempInitiatedBy); setFilterSource(tempSource); setFilterDateFrom(tempDateFrom); setFilterDateTo(tempDateTo)
    setShowPopover(false)
  }
  const clearAndClose = () => {
    setPage(1)
    setTempAbn(''); setTempStatus(''); setTempInitiatedBy(''); setTempSource(''); setTempDateFrom(''); setTempDateTo('')
    setFilterAbn(''); setFilterStatus(''); setFilterInitiatedBy(''); setFilterSource(''); setFilterDateFrom(''); setFilterDateTo('')
    setShowPopover(false)
  }
  const clearAll = () => {
    setPage(1)
    setFilterAbn(''); setFilterStatus(''); setFilterInitiatedBy(''); setFilterSource(''); setFilterFailedAtStep(''); setFilterDateFrom(''); setFilterDateTo('')
  }

  const retryOne = async (id: string) => {
    setRetryingId(id)
    try {
      const r = await api.admin.retryRenewal(id)
      showToast('emerald', r.message ?? 'Renewal queued for retry.')
      await load()
    } catch (e) {
      showToast('red', e instanceof Error ? e.message : 'Retry failed')
    } finally {
      setRetryingId(null)
    }
  }

  const retryAllFailed = async () => {
    setBulkRetrying(true)
    try {
      const r = await api.admin.retryRenewalsBulk({
        dateFrom: filterDateFrom || undefined,
        dateTo: filterDateTo || undefined,
        source: filterSource || undefined,
      })
      showToast('emerald', `Queued ${r.retried} for retry${r.skipped > 0 ? ` (${r.skipped} skipped — Stripe payment didn't succeed)` : ''}.`)
      setBulkRetryConfirm(false)
      await load()
    } catch (e) {
      showToast('red', e instanceof Error ? e.message : 'Bulk retry failed')
    } finally {
      setBulkRetrying(false)
    }
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Renewal requests"
        subtitle="All renewals across origins — customer checkout, admin, Ontraport, bulk."
        right={
          <>
            {stats.failedValue30d > 0 ? (
              <button
                type="button"
                onClick={() => setBulkRetryConfirm(true)}
                className="inline-flex items-center gap-2 rounded-md bg-amber-600 text-white px-3 py-2 text-sm font-medium hover:bg-amber-700 shadow-sm transition"
              >
                <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
                </svg>
                Retry all failed
                <span className="ml-0.5 inline-flex items-center justify-center rounded bg-white/20 text-white text-xxs font-mono tabular-nums px-1.5 py-0.5 leading-none">{fmtMoney0(stats.failedValue30d)}</span>
              </button>
            ) : null}
            <ViewToggle<ViewMode> value={viewMode} options={[{ value: 'Table', label: 'Table view' }, { value: 'Cards', label: 'Cards view' }]} onChange={setViewMode} />
            <div className="relative">
              <button
                onClick={() => (showPopover ? setShowPopover(false) : openPopover())}
                className={`inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium ring-1 ring-inset transition ${
                  hasActiveFilters
                    ? 'bg-brand-50 text-brand-800 ring-brand-200 hover:bg-brand-100'
                    : 'bg-white text-zinc-700 ring-zinc-300 hover:bg-zinc-50'
                }`}
              >
                <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 3c2.755 0 5.455.232 8.083.678.533.09.917.556.917 1.096v1.044a2.25 2.25 0 01-.659 1.591l-5.432 5.432a2.25 2.25 0 00-.659 1.591v2.927a2.25 2.25 0 01-1.244 2.013L9.75 21v-6.568a2.25 2.25 0 00-.659-1.591L3.659 7.409A2.25 2.25 0 013 5.818V4.774c0-.54.384-1.006.917-1.096A48.32 48.32 0 0112 3z" />
                </svg>
                Filters
                {hasActiveFilters ? <span className="ml-0.5 inline-flex items-center justify-center rounded bg-brand-600 text-white text-xxs font-mono tabular-nums px-1.5 py-0.5 leading-none">{activeFilterCount}</span> : null}
              </button>
              <FilterPopover open={showPopover} onClose={() => setShowPopover(false)} title="Filter renewals">
                <div className="space-y-3">
                  <Field label="ABN">
                    <input type="text" value={tempAbn} onChange={(e) => setTempAbn(e.target.value)} placeholder="11 digits" className="block w-full rounded-md border-zinc-300 text-sm font-mono tabular-nums shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2" />
                  </Field>
                  <Field label="Status">
                    <select value={tempStatus} onChange={(e) => setTempStatus(e.target.value)} className="block w-full rounded-md border-zinc-300 text-sm shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2">
                      <option value="">All</option>
                      <option value="Pending">Pending</option>
                      <option value="Processing">Processing</option>
                      <option value="Completed">Completed</option>
                      <option value="Failed">Failed</option>
                    </select>
                  </Field>
                  <Field label="Source">
                    <select value={tempSource} onChange={(e) => setTempSource(e.target.value)} className="block w-full rounded-md border-zinc-300 text-sm shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2">
                      <option value="">All sources</option>
                      <option value="Renewtron">Renewtron direct</option>
                      <option value="Ontraport">Ontraport</option>
                      <option value="BulkUpload">Bulk upload</option>
                    </select>
                  </Field>
                  <Field label="Initiated by">
                    <select value={tempInitiatedBy} onChange={(e) => setTempInitiatedBy(e.target.value)} className="block w-full rounded-md border-zinc-300 text-sm shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2">
                      <option value="">All</option>
                      <option value="Customer">Customer</option>
                      <option value="Admin">Admin</option>
                      <option value="System">System</option>
                    </select>
                  </Field>
                  <div className="grid grid-cols-2 gap-2">
                    <Field label="From">
                      <input type="date" value={tempDateFrom} onChange={(e) => setTempDateFrom(e.target.value)} className="block w-full rounded-md border-zinc-300 text-sm font-mono tabular-nums shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2" />
                    </Field>
                    <Field label="To">
                      <input type="date" value={tempDateTo} onChange={(e) => setTempDateTo(e.target.value)} className="block w-full rounded-md border-zinc-300 text-sm font-mono tabular-nums shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2" />
                    </Field>
                  </div>
                </div>
                <div className="mt-4 flex gap-2">
                  <button onClick={applyAndClose} className="flex-1 inline-flex justify-center items-center rounded-md bg-zinc-900 text-white px-3 py-2 text-sm font-medium hover:bg-zinc-800 transition">Apply</button>
                  <button onClick={clearAndClose} className="inline-flex items-center rounded-md bg-white px-3 py-2 text-sm font-medium text-zinc-700 ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 transition">Clear</button>
                </div>
              </FilterPopover>
            </div>
            <RefreshButton onClick={() => void load()} busy={isRefreshing} />
          </>
        }
      />

      <StatsStrip stats={stats} />

      {/* Top failure reasons */}
      {stats.failedAtStepBreakdown.length > 0 ? (
        <div className="mt-6 rounded-xl border border-zinc-200 bg-white p-4 shadow-sm">
          <div className="flex items-center justify-between gap-3">
            <div>
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">FAILURE REASONS · 30D</div>
              <div className="text-sm text-zinc-500">Click a reason to filter the list.</div>
            </div>
          </div>
          <div className="mt-3 flex flex-wrap gap-2">
            {stats.failedAtStepBreakdown.map((s) => (
              <button
                key={s.step}
                onClick={() => { setPage(1); setFilterFailedAtStep(filterFailedAtStep === s.step ? '' : s.step); setFilterStatus('Failed') }}
                className={`inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium ring-1 ring-inset transition ${
                  filterFailedAtStep === s.step
                    ? 'bg-red-50 text-red-800 ring-red-200'
                    : 'bg-white text-zinc-700 ring-zinc-200 hover:bg-zinc-50'
                }`}
              >
                <span className="font-mono">{s.step}</span>
                <span className="font-mono tabular-nums text-zinc-500">{s.count}</span>
              </button>
            ))}
          </div>
        </div>
      ) : null}

      {/* Active filter chips */}
      {hasActiveFilters ? (
        <div className="mt-6 flex flex-wrap items-center gap-2">
          {filterAbn ? <Chip label={`ABN ${filterAbn}`} onRemove={() => { setPage(1); setFilterAbn('') }} /> : null}
          {filterStatus ? <Chip label={`Status ${filterStatus}`} onRemove={() => { setPage(1); setFilterStatus('') }} /> : null}
          {filterSource ? <Chip label={`Source ${filterSource}`} onRemove={() => { setPage(1); setFilterSource('') }} /> : null}
          {filterFailedAtStep ? <Chip label={`Failed at ${filterFailedAtStep}`} onRemove={() => { setPage(1); setFilterFailedAtStep('') }} /> : null}
          {filterInitiatedBy ? <Chip label={`By ${filterInitiatedBy}`} onRemove={() => { setPage(1); setFilterInitiatedBy('') }} /> : null}
          {filterDateFrom ? <Chip label={`From ${fmtDateRange(filterDateFrom)}`} onRemove={() => { setPage(1); setFilterDateFrom('') }} /> : null}
          {filterDateTo ? <Chip label={`To ${fmtDateRange(filterDateTo)}`} onRemove={() => { setPage(1); setFilterDateTo('') }} /> : null}
          <button onClick={clearAll} className="text-xs font-medium text-zinc-600 hover:text-zinc-900 px-2">Clear all</button>
        </div>
      ) : null}

      <div className="mt-6">
        {viewMode === 'Cards' ? (
          renewals.length === 0 ? (
            <EmptyState title="No renewals match the current filters." />
          ) : (
            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              {renewals.map((r) => (
                <RenewalCard key={r.id} r={r} onError={setErrorModal} onRetry={retryOne} retrying={retryingId === r.id} />
              ))}
            </div>
          )
        ) : (
          <RenewalsTable renewals={renewals} onError={setErrorModal} onRetry={retryOne} retryingId={retryingId} />
        )}

        <Pagination page={page} pageSize={PAGE_SIZE} total={totalCount} onPage={setPage} />
      </div>

      {bulkRetryConfirm ? (
        <BulkRetryModal
          stats={stats}
          dateFrom={filterDateFrom}
          dateTo={filterDateTo}
          source={filterSource}
          busy={bulkRetrying}
          onClose={() => setBulkRetryConfirm(false)}
          onConfirm={retryAllFailed}
        />
      ) : null}

      {toast ? (
        <div className="fixed bottom-6 right-6 z-50 fade-in"><Toast tone={toast.tone} message={toast.message} /></div>
      ) : null}

      <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Renewal error details" />
    </div>
  )
}

function defaultStats(): Stats {
  return {
    successRate30d: 0, completed30d: 0, total30d: 0, avgCompletionMinutes: null,
    revenueMtd: 0, pipelineValue: 0, failedValue30d: 0,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [], failedAtStepBreakdown: [],
  }
}

function StatsStrip({ stats }: { stats: Stats }) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <StatTile
        kicker="30D"
        label="Success rate"
        value={`${stats.successRate30d}%`}
        sub={`${stats.completed30d.toLocaleString()} of ${stats.total30d.toLocaleString()}`}
        tone={stats.successRate30d >= 90 ? 'emerald' : stats.successRate30d >= 70 ? 'amber' : 'red'}
      />
      <StatTile
        kicker="MTD"
        label="Revenue"
        value={fmtMoney0(stats.revenueMtd)}
        sub={stats.avgCompletionMinutes != null ? `avg ${durationShort(Number(stats.avgCompletionMinutes) / 60)} to complete` : '—'}
        tone="emerald"
      />
      <StatTile
        kicker="$ AT RISK"
        label="Pipeline + failed"
        value={fmtMoney0(stats.pipelineValue + stats.failedValue30d)}
        sub={`${fmtMoney0(stats.pipelineValue)} in flight · ${fmtMoney0(stats.failedValue30d)} failed`}
        tone={stats.failedValue30d > 0 ? 'amber' : 'zinc'}
      />
      <SparklineTile
        kicker="14D"
        label="Today"
        value={stats.today.toLocaleString()}
        sub={`${stats.yesterday.toLocaleString()} yesterday`}
        deltaPct={stats.deltaPct}
        data={stats.daily14d.map((d) => d.count)}
      />
    </div>
  )
}

/* ─────── Table view ─────── */

function RenewalsTable({ renewals, onError, onRetry, retryingId }: {
  renewals: Renewal[]
  onError: (m: string) => void
  onRetry: (id: string) => void
  retryingId: string | null
}) {
  return (
    <div className="overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
      <div className="overflow-x-auto">
        <table className="min-w-full">
          <thead className="bg-zinc-50/80 backdrop-blur">
            <tr>
              <Th>Business · ABN · Customer</Th>
              <Th>Status</Th>
              <Th className="text-right">Amount</Th>
              <Th>Years</Th>
              <Th>Source · Payment</Th>
              <Th>Time in status</Th>
              <Th>ATO</Th>
              <Th><span className="sr-only">Actions</span></Th>
            </tr>
          </thead>
          <tbody className="divide-y divide-zinc-100">
            {renewals.length === 0 ? (
              <tr><td colSpan={8} className="px-4 py-12"><EmptyState title="No renewals match the current filters." /></td></tr>
            ) : renewals.map((r) => (
              <tr key={r.id} className="hover:bg-zinc-50 transition-colors">
                <td className="px-4 py-3 align-top">
                  <div className="text-sm font-medium text-zinc-900 truncate max-w-[18rem]">{r.businessName}</div>
                  <div className="text-xxs font-mono tabular-nums text-zinc-500">{r.abn}</div>
                  {r.lead ? (
                    <Link to={`/admin/leads/${r.lead.id}`} className="block mt-0.5 text-xxs font-mono text-zinc-400 hover:underline truncate max-w-[18rem]">{r.lead.fullName} · {r.lead.email}</Link>
                  ) : (
                    <div className="text-xxs font-mono text-zinc-400">{r.email ?? 'no lead'}</div>
                  )}
                </td>
                <td className="px-4 py-3 align-top"><StatusCell r={r} onError={onError} /></td>
                <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-900">{fmtMoney2(r.amount)}</td>
                <td className="px-4 py-3 align-top text-sm tabular-nums text-zinc-700">{r.renewalYears}</td>
                <td className="px-4 py-3 align-top">
                  <SourcePill source={r.source} />
                  <div className="mt-1"><PaymentPill r={r} /></div>
                </td>
                <td className="px-4 py-3 align-top">
                  <TimeInStatus r={r} />
                </td>
                <td className="px-4 py-3 align-top">
                  {r.atoStatus ? <AtoStatusPill status={r.atoStatus} /> : <span className="text-xxs font-mono text-zinc-400">—</span>}
                </td>
                <td className="px-4 py-3 align-top text-right">
                  <div className="flex items-center justify-end gap-3">
                    {canRetry(r) ? (
                      <button onClick={() => onRetry(r.id)} disabled={retryingId === r.id} className="text-sm font-medium text-amber-700 hover:text-amber-800 disabled:opacity-50">
                        {retryingId === r.id ? 'Retrying…' : 'Retry'}
                      </button>
                    ) : null}
                    <Link to={`/admin/renewals/${r.id}`} className="text-sm font-medium text-brand-700 hover:text-brand-800">View →</Link>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

/* ─────── Cards view ─────── */

function RenewalCard({ r, onError, onRetry, retrying }: { r: Renewal; onError: (m: string) => void; onRetry: (id: string) => void; retrying: boolean }) {
  return (
    <div className="rounded-xl bg-white p-5 ring-1 ring-zinc-200 hover:ring-brand-200 hover:shadow-md transition-all">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="text-sm font-medium text-zinc-900 truncate">{r.businessName}</div>
          <div className="text-xxs font-mono tabular-nums text-zinc-500">{r.abn}</div>
          {r.lead ? <div className="text-xxs font-mono text-zinc-400 mt-0.5 truncate">{r.lead.fullName} · {r.lead.email}</div> : null}
        </div>
        <StatusCell r={r} onError={onError} />
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
        <Cell label="Amount" value={<span className="font-mono tabular-nums">{fmtMoney2(r.amount)}</span>} />
        <Cell label="Years" value={<span className="font-mono tabular-nums">{r.renewalYears}</span>} />
        <Cell label="Source" value={<SourcePill source={r.source} />} />
        <Cell label="Payment" value={<PaymentPill r={r} />} />
        <Cell label="Time in status" value={<TimeInStatus r={r} />} />
        <Cell label="Initiated" value={<span className="text-xs font-mono text-zinc-500">{fmtDate(r.initiatedAt)} · {fmtTime(r.initiatedAt)}</span>} />
      </dl>
      <div className="mt-4 pt-4 border-t border-zinc-100 flex items-center justify-end gap-3">
        {canRetry(r) ? (
          <button onClick={() => onRetry(r.id)} disabled={retrying} className="inline-flex items-center rounded-md bg-amber-50 px-3 py-1.5 text-sm font-medium text-amber-800 ring-1 ring-amber-200 hover:bg-amber-100 disabled:opacity-50 transition">
            {retrying ? 'Retrying…' : 'Retry'}
          </button>
        ) : null}
        <Link to={`/admin/renewals/${r.id}`} className="inline-flex items-center text-sm font-medium text-brand-700 hover:text-brand-800">View details →</Link>
      </div>
    </div>
  )
}

function Cell({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div>
      <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">{label}</dt>
      <dd className="mt-1 text-sm text-zinc-900">{value}</dd>
    </div>
  )
}

/* ─────── Cell components ─────── */

function StatusCell({ r, onError }: { r: Renewal; onError: (m: string) => void }) {
  const pill = (() => {
    switch (r.status) {
      case 'Completed':  return <StatusPill tone="emerald">COMPLETED</StatusPill>
      case 'Processing': return <StatusPill tone="indigo">PROCESSING</StatusPill>
      case 'Pending':    return <StatusPill tone="amber">PENDING</StatusPill>
      case 'Failed':     return <StatusPill tone="red">FAILED</StatusPill>
    }
  })()
  return (
    <div className="flex items-center gap-1.5">
      {pill}
      {r.status === 'Failed' && r.errorMessage ? (
        <button onClick={() => onError(r.errorMessage ?? '')} className="inline-flex items-center justify-center h-5 w-5 rounded-full bg-red-50 text-red-600 hover:bg-red-100" title="View error">
          <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
          </svg>
        </button>
      ) : null}
    </div>
  )
}

function SourcePill({ source }: { source: string }) {
  switch (source) {
    case 'Renewtron':  return <StatusPill tone="emerald">DIRECT</StatusPill>
    case 'Ontraport':  return <StatusPill tone="indigo">ONTRAPORT</StatusPill>
    case 'BulkUpload': return <StatusPill tone="zinc">BULK</StatusPill>
    default:           return <StatusPill tone="zinc">{source.toUpperCase()}</StatusPill>
  }
}

function PaymentPill({ r }: { r: Renewal }) {
  if (r.paymentType === 'External') {
    return <span className="text-xxs font-mono text-zinc-500">EXTERNAL</span>
  }
  if (!r.stripePaymentSucceeded) {
    return <span className="text-xxs font-mono text-red-700">STRIPE/UNPAID</span>
  }
  const last4 = r.cardLast4 ? ` •••• ${r.cardLast4}` : ''
  const brand = r.cardBrand ? ` ${r.cardBrand.toUpperCase()}` : ''
  return <span className="text-xxs font-mono text-zinc-600">STRIPE{brand}{last4}</span>
}

function TimeInStatus({ r }: { r: Renewal }) {
  const isInflight = r.status === 'Pending' || r.status === 'Processing'
  const slow = isInflight && r.timeInStatusHours > 2
  const cls = slow ? 'text-amber-700 font-medium' : 'text-zinc-700'
  return (
    <div>
      <div className={`text-sm tabular-nums ${cls}`}>{durationShort(r.timeInStatusHours)}{isInflight ? ' in flight' : ''}</div>
      {!isInflight && r.completedAt ? (
        <div className="text-xxs font-mono text-zinc-400 tabular-nums">closed {fmtDate(r.completedAt)}</div>
      ) : null}
    </div>
  )
}

function AtoStatusPill({ status }: { status: string }) {
  switch (status) {
    case 'Completed':    return <StatusPill tone="emerald">ATO/OK</StatusPill>
    case 'Failed':       return <StatusPill tone="red">ATO/FAIL</StatusPill>
    case 'AwaitingAuth': return <StatusPill tone="amber">ATO/AUTH</StatusPill>
    case 'InProgress':   return <StatusPill tone="indigo">ATO/WIP</StatusPill>
    case 'Pending':      return <StatusPill tone="amber">ATO/PEND.</StatusPill>
    default:             return <StatusPill tone="zinc">{`ATO/${status.toUpperCase()}`}</StatusPill>
  }
}

/* ─────── Bulk retry modal ─────── */

function BulkRetryModal({ stats, dateFrom, dateTo, source, busy, onClose, onConfirm }: {
  stats: Stats
  dateFrom: string
  dateTo: string
  source: string
  busy: boolean
  onClose: () => void
  onConfirm: () => void
}) {
  return createPortal(
    <div className="fixed inset-0 z-[100]" role="dialog" aria-modal="true">
      <div className="absolute inset-0 bg-zinc-950/70 backdrop-blur-sm" onClick={onClose} />
      <div className="absolute inset-0 overflow-y-auto">
        <div className="flex min-h-full items-center justify-center p-4">
          <div className="relative w-full max-w-md rounded-xl bg-white shadow-xl">
            <div className="px-6 py-5 border-b border-zinc-100">
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-amber-700">RETRY</div>
              <h2 className="mt-0.5 text-lg font-semibold text-zinc-900 tracking-tight">Retry all failed renewals</h2>
            </div>
            <div className="px-6 py-5 space-y-3">
              <p className="text-sm text-zinc-700">
                This will re-queue every <span className="font-medium">Failed</span> renewal in your current filter.
                Stripe-paid renewals where the payment didn't succeed will be skipped automatically.
              </p>
              <div className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-3 py-2 text-xs font-mono text-zinc-700 space-y-1">
                <div>Failed (30d): <span className="text-zinc-900 tabular-nums">{fmtMoney0(stats.failedValue30d)}</span></div>
                {dateFrom ? <div>From: <span className="text-zinc-900 tabular-nums">{dateFrom}</span></div> : null}
                {dateTo ? <div>To: <span className="text-zinc-900 tabular-nums">{dateTo}</span></div> : null}
                {source ? <div>Source: <span className="text-zinc-900">{source}</span></div> : null}
              </div>
            </div>
            <div className="px-6 py-4 border-t border-zinc-100 flex items-center justify-end gap-2">
              <button onClick={onClose} disabled={busy} className="inline-flex items-center rounded-md bg-white px-3 py-2 text-sm font-medium text-zinc-700 ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 transition disabled:opacity-50">Cancel</button>
              <button onClick={onConfirm} disabled={busy} className="inline-flex items-center rounded-md bg-amber-600 text-white px-4 py-2 text-sm font-medium hover:bg-amber-700 disabled:opacity-50 transition">
                {busy ? 'Retrying…' : 'Retry all'}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  )
}
