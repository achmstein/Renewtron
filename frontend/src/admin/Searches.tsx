import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { ErrorModal, FilterPopover, Pagination } from './_components'
import { Cell, Chip, EmptyState, Field, FunnelPills, PageHeader, RefreshButton, searchFunnelStages, SparklineTile, StatTile, StatusPill, Th, ViewToggle } from './_ui'
import { fmtDate, fmtMoney0, fmtMoney2, fmtTime, relativeTime } from './_utils'

type SearchesResponse = Awaited<ReturnType<typeof api.admin.searches>>
type Search = SearchesResponse['items'][number]
type Stats = SearchesResponse['stats']

type ViewMode = 'Table' | 'Cards'

const PAGE_SIZE = 10

function fmtDateRange(s: string) {
  return new Date(s).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
}

export default function Searches() {
  const [viewMode, setViewMode] = useState<ViewMode>('Table')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<SearchesResponse | null>(null)
  const [isRefreshing, setIsRefreshing] = useState(false)

  const [filterAbn, setFilterAbn] = useState('')
  const [filterSuccess, setFilterSuccess] = useState('')
  const [filterInitiatedBy, setFilterInitiatedBy] = useState('')
  const [filterDateFrom, setFilterDateFrom] = useState('')
  const [filterDateTo, setFilterDateTo] = useState('')
  const [includeSystem, setIncludeSystem] = useState(false)

  const [showPopover, setShowPopover] = useState(false)
  const [tempAbn, setTempAbn] = useState('')
  const [tempSuccess, setTempSuccess] = useState('')
  const [tempInitiatedBy, setTempInitiatedBy] = useState('')
  const [tempDateFrom, setTempDateFrom] = useState('')
  const [tempDateTo, setTempDateTo] = useState('')
  const [tempIncludeSystem, setTempIncludeSystem] = useState(false)

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
        includeSystem: includeSystem || undefined,
        page,
        pageSize: PAGE_SIZE,
      })
      setData(r)
    } finally {
      setIsRefreshing(false)
    }
  }, [filterAbn, filterSuccess, filterInitiatedBy, filterDateFrom, filterDateTo, includeSystem, page])

  useEffect(() => { void load() }, [load])

  const totalCount = data?.totalCount ?? 0
  const searches = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()

  const activeFilterCount = [filterAbn, filterSuccess, filterInitiatedBy, filterDateFrom, filterDateTo].filter(Boolean).length + (includeSystem ? 1 : 0)
  const hasActiveFilters = activeFilterCount > 0

  const openPopover = () => {
    setTempAbn(filterAbn); setTempSuccess(filterSuccess); setTempInitiatedBy(filterInitiatedBy); setTempDateFrom(filterDateFrom); setTempDateTo(filterDateTo)
    setTempIncludeSystem(includeSystem)
    setShowPopover(true)
  }
  const applyAndClose = () => {
    setPage(1)
    setFilterAbn(tempAbn); setFilterSuccess(tempSuccess); setFilterInitiatedBy(tempInitiatedBy); setFilterDateFrom(tempDateFrom); setFilterDateTo(tempDateTo)
    setIncludeSystem(tempIncludeSystem)
    setShowPopover(false)
  }
  const clearAndClose = () => {
    setPage(1)
    setTempAbn(''); setTempSuccess(''); setTempInitiatedBy(''); setTempDateFrom(''); setTempDateTo(''); setTempIncludeSystem(false)
    setFilterAbn(''); setFilterSuccess(''); setFilterInitiatedBy(''); setFilterDateFrom(''); setFilterDateTo(''); setIncludeSystem(false)
    setShowPopover(false)
  }
  const clearAll = () => {
    setPage(1)
    setFilterAbn(''); setFilterSuccess(''); setFilterInitiatedBy(''); setFilterDateFrom(''); setFilterDateTo(''); setIncludeSystem(false)
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Search logs"
        subtitle="ABN searches recorded by customers, admins, and the bulk pipelines."
        right={
          <>
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
              <FilterPopover open={showPopover} onClose={() => setShowPopover(false)} title="Filter searches">
                <div className="space-y-3">
                  <Field label="ABN">
                    <input type="text" value={tempAbn} onChange={(e) => setTempAbn(e.target.value)} placeholder="11 digits" className="block w-full rounded-md border-zinc-300 text-sm font-mono tabular-nums shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2" />
                  </Field>
                  <Field label="Status">
                    <select value={tempSuccess} onChange={(e) => setTempSuccess(e.target.value)} className="block w-full rounded-md border-zinc-300 text-sm shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2">
                      <option value="">All</option>
                      <option value="true">Success</option>
                      <option value="false">Failed</option>
                    </select>
                  </Field>
                  <Field label="Initiated by">
                    <select value={tempInitiatedBy} onChange={(e) => setTempInitiatedBy(e.target.value)} className="block w-full rounded-md border-zinc-300 text-sm shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2">
                      <option value="">All</option>
                      <option value="Customer">Customer</option>
                      <option value="Admin">Admin</option>
                      <option value="System">System (Ontraport / Bulk)</option>
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
                  <label className="mt-1 flex items-start gap-2.5 cursor-pointer p-2 -mx-2 rounded hover:bg-zinc-50">
                    <input
                      type="checkbox"
                      checked={tempIncludeSystem}
                      onChange={(e) => setTempIncludeSystem(e.target.checked)}
                      className="mt-0.5 h-4 w-4 rounded border-zinc-300 text-brand-600 focus:ring-brand-500"
                    />
                    <span className="flex-1">
                      <span className="block text-sm font-medium text-zinc-900">Include system searches</span>
                      <span className="block text-xs text-zinc-500 leading-snug">Hidden by default. System searches are background lookups for Ontraport sync and bulk renewals — useful for debugging, noisy for daily ops.</span>
                    </span>
                  </label>
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

      {/* Active filter chips + system-hidden hint */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        {filterAbn ? <Chip label={`ABN ${filterAbn}`} onRemove={() => { setPage(1); setFilterAbn('') }} /> : null}
        {filterSuccess ? <Chip label={`Status ${filterSuccess === 'true' ? 'Success' : 'Failed'}`} onRemove={() => { setPage(1); setFilterSuccess('') }} /> : null}
        {filterInitiatedBy ? <Chip label={`By ${filterInitiatedBy}`} onRemove={() => { setPage(1); setFilterInitiatedBy('') }} /> : null}
        {filterDateFrom ? <Chip label={`From ${fmtDateRange(filterDateFrom)}`} onRemove={() => { setPage(1); setFilterDateFrom('') }} /> : null}
        {filterDateTo ? <Chip label={`To ${fmtDateRange(filterDateTo)}`} onRemove={() => { setPage(1); setFilterDateTo('') }} /> : null}
        {includeSystem ? <Chip label="System included" onRemove={() => { setPage(1); setIncludeSystem(false) }} /> : null}
        {hasActiveFilters ? (
          <button onClick={clearAll} className="inline-flex items-center gap-x-1 text-xs font-medium text-zinc-600 hover:text-zinc-900 px-2">Clear all</button>
        ) : null}
        {!includeSystem && !filterInitiatedBy ? (
          <div className="inline-flex items-center gap-1.5 text-xxs font-mono uppercase tracking-[0.14em] text-zinc-400">
            <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M3.98 8.223A10.477 10.477 0 001.934 12C3.226 16.338 7.244 19.5 12 19.5c.993 0 1.953-.138 2.863-.395M6.228 6.228A10.45 10.45 0 0112 4.5c4.756 0 8.773 3.162 10.065 7.498a10.523 10.523 0 01-4.293 5.774M6.228 6.228L3 3m3.228 3.228l3.65 3.65m7.894 7.894L21 21m-3.228-3.228l-3.65-3.65m0 0a3 3 0 10-4.243-4.243m4.242 4.242L9.88 9.88" />
            </svg>
            System searches hidden
          </div>
        ) : null}
      </div>

      <div className="mt-6">
        {viewMode === 'Cards' ? (
          <CardsView searches={searches} onShowError={setErrorModal} />
        ) : (
          <TableView searches={searches} onShowError={setErrorModal} />
        )}

        <Pagination page={page} pageSize={PAGE_SIZE} total={totalCount} onPage={setPage} />
      </div>

      <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Search error details" />
    </div>
  )
}

function defaultStats(): Stats {
  return {
    totalAllTime: 0, total30d: 0, success30d: 0, successRate30d: 0,
    conversions30d: 0, conversionRate30d: 0, today: 0, yesterday: 0,
    deltaPct: null, daily14d: [],
  }
}

/* ---------------- STATS STRIP ---------------- */

function StatsStrip({ stats }: { stats: Stats }) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <StatTile
        kicker="ALL TIME"
        label="Total searches"
        value={stats.totalAllTime.toLocaleString()}
        sub={`${stats.total30d.toLocaleString()} in last 30d`}
      />
      <StatTile
        kicker="30D"
        label="Success rate"
        value={`${stats.successRate30d}%`}
        sub={`${stats.success30d.toLocaleString()} of ${stats.total30d.toLocaleString()}`}
        tone={stats.successRate30d >= 80 ? 'emerald' : stats.successRate30d >= 50 ? 'amber' : 'red'}
      />
      <StatTile
        kicker="30D"
        label="Conversion rate"
        value={`${stats.conversionRate30d}%`}
        sub={`${stats.conversions30d.toLocaleString()} paid renewals`}
        tone={stats.conversionRate30d >= 30 ? 'emerald' : stats.conversionRate30d >= 10 ? 'amber' : 'zinc'}
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

/* ---------------- TABLE VIEW ---------------- */

function TableView({ searches, onShowError }: { searches: Search[]; onShowError: (m: string) => void }) {
  return (
    <div className="overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
      <div className="overflow-x-auto">
        <table className="min-w-full">
          <thead className="bg-zinc-50/80 backdrop-blur">
            <tr>
              <Th>ABN · Customer</Th>
              <Th>Outcome</Th>
              <Th>Names found</Th>
              <Th>Funnel</Th>
              <Th className="text-right">Renewal</Th>
              <Th>Source</Th>
              <Th>When</Th>
              <Th><span className="sr-only">Actions</span></Th>
            </tr>
          </thead>
          <tbody className="divide-y divide-zinc-100">
            {searches.length === 0 ? (
              <tr><td colSpan={8} className="px-4 py-12 text-center text-sm text-zinc-500">No searches match the current filters.</td></tr>
            ) : searches.map((s) => (
              <tr key={s.id} className="hover:bg-zinc-50 transition-colors">
                <td className="px-4 py-3 align-top">
                  <div className="flex items-center gap-1.5">
                    <span className="text-sm font-mono tabular-nums text-zinc-900">{s.abn}</span>
                    {s.repeatCount7d >= 3 ? (
                      <StatusPill tone="amber">×{s.repeatCount7d} 7D</StatusPill>
                    ) : null}
                  </div>
                  {s.lead ? (
                    <Link to={`/admin/leads/${s.lead.id}`} className="block mt-0.5 hover:underline">
                      <div className="text-sm text-zinc-700 truncate max-w-[18rem]">{s.lead.fullName}</div>
                      <div className="text-xxs font-mono text-zinc-400 truncate max-w-[18rem]">{s.lead.email}</div>
                    </Link>
                  ) : (
                    <div className="mt-0.5 text-xxs font-mono text-zinc-400">no lead</div>
                  )}
                </td>
                <td className="px-4 py-3 align-top">
                  {!s.success ? (
                    <div className="flex items-center gap-1.5">
                      <StatusPill tone="red">FAILED</StatusPill>
                      {s.errorMessage ? (
                        <button onClick={() => onShowError(s.errorMessage ?? '')} className="inline-flex items-center justify-center h-5 w-5 rounded-full bg-red-50 text-red-600 hover:bg-red-100" title="View error">
                          <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
                          </svg>
                        </button>
                      ) : null}
                    </div>
                  ) : s.lead?.outcome ? (
                    <OutcomePill outcome={s.lead.outcome} />
                  ) : (
                    <StatusPill tone="emerald">SUCCESS</StatusPill>
                  )}
                </td>
                <td className="px-4 py-3 align-top">
                  {s.resultsCount === 0 ? (
                    <span className="text-xxs font-mono text-zinc-400">none</span>
                  ) : (
                    <div>
                      <div className="text-sm text-zinc-900 truncate max-w-[16rem]">{s.firstBusinessName ?? '—'}</div>
                      {s.resultsCount > 1 ? <div className="text-xxs font-mono text-zinc-400 tabular-nums">+{s.resultsCount - 1} more</div> : null}
                    </div>
                  )}
                </td>
                <td className="px-4 py-3 align-top"><FunnelPills stages={searchFunnelStages(s.funnel)} /></td>
                <td className="px-4 py-3 align-top text-right">
                  {s.renewal ? (
                    <div>
                      <div className="text-sm font-mono tabular-nums text-zinc-900">{fmtMoney0(s.renewal.amount)}</div>
                      <RenewalStatusPill status={s.renewal.status} />
                    </div>
                  ) : (
                    <span className="text-xxs font-mono text-zinc-400">—</span>
                  )}
                </td>
                <td className="px-4 py-3 align-top"><InitiatedByBadge value={s.initiatedBy} /></td>
                <td className="px-4 py-3 align-top">
                  <div className="text-sm text-zinc-700 tabular-nums">{relativeTime(s.searchedAt)}</div>
                  <div className="text-xxs font-mono text-zinc-400 tabular-nums">{fmtDate(s.searchedAt)} · {fmtTime(s.searchedAt)}</div>
                </td>
                <td className="px-4 py-3 align-top text-right">
                  <Link to={`/admin/searches/${s.id}`} className="inline-flex items-center text-sm font-medium text-brand-700 hover:text-brand-800">View →</Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

/* ---------------- CARDS VIEW ---------------- */

function CardsView({ searches, onShowError }: { searches: Search[]; onShowError: (m: string) => void }) {
  if (searches.length === 0) {
    return <EmptyState title="No searches match the current filters." />
  }
  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      {searches.map((s) => (
        <Link
          key={s.id}
          to={`/admin/searches/${s.id}`}
          className="group relative rounded-xl bg-white p-5 ring-1 ring-zinc-200 hover:ring-brand-200 hover:shadow-md transition-all flex flex-col"
        >
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="flex items-center gap-1.5">
                <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">ABN</div>
                {s.repeatCount7d >= 3 ? <StatusPill tone="amber">×{s.repeatCount7d} 7D</StatusPill> : null}
              </div>
              <div className="mt-0.5 font-mono text-sm tabular-nums text-zinc-900">{s.abn}</div>
              {s.lead ? (
                <div className="mt-2">
                  <div className="text-sm text-zinc-700">{s.lead.fullName}</div>
                  <div className="text-xxs font-mono text-zinc-400">{s.lead.email}</div>
                </div>
              ) : (
                <div className="mt-2 text-xxs font-mono text-zinc-400">no lead captured</div>
              )}
            </div>
            <div className="flex flex-col items-end gap-1.5">
              {!s.success ? (
                <div className="flex items-center gap-1.5">
                  <StatusPill tone="red">FAILED</StatusPill>
                  {s.errorMessage ? (
                    <button onClick={(e) => { e.preventDefault(); onShowError(s.errorMessage ?? '') }} className="inline-flex items-center justify-center h-5 w-5 rounded-full bg-red-50 text-red-600 hover:bg-red-100" title="View error">
                      <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
                      </svg>
                    </button>
                  ) : null}
                </div>
              ) : s.lead?.outcome ? <OutcomePill outcome={s.lead.outcome} /> : <StatusPill tone="emerald">SUCCESS</StatusPill>}
              <InitiatedByBadge value={s.initiatedBy} />
            </div>
          </div>

          <div className="mt-4 pt-4 border-t border-zinc-100">
            <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">Funnel</div>
            <div className="mt-1.5"><FunnelPills stages={searchFunnelStages(s.funnel)} /></div>
          </div>

          <dl className="mt-4 grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <Cell label="Names found" value={
              s.resultsCount === 0
                ? <span className="text-xxs font-mono text-zinc-400">none</span>
                : <>
                    <div className="text-zinc-900 truncate">{s.firstBusinessName ?? '—'}</div>
                    {s.resultsCount > 1 ? <div className="text-xxs font-mono text-zinc-400">+{s.resultsCount - 1} more</div> : null}
                  </>
            } />
            <Cell label="Renewal" value={
              s.renewal
                ? <div>
                    <div className="font-mono tabular-nums">{fmtMoney2(s.renewal.amount)}</div>
                    <RenewalStatusPill status={s.renewal.status} />
                  </div>
                : <span className="text-xxs font-mono text-zinc-400">—</span>
            } />
            <Cell label="When" value={
              <div>
                <div>{relativeTime(s.searchedAt)}</div>
                <div className="text-xxs font-mono text-zinc-400">{fmtDate(s.searchedAt)} · {fmtTime(s.searchedAt)}</div>
              </div>
            } />
            <Cell label="IP" value={s.ipAddress ?? '—'} mono />
          </dl>

          <div className="mt-4 pt-4 border-t border-zinc-100 text-sm font-medium text-brand-700 group-hover:text-brand-800">
            View details →
          </div>
        </Link>
      ))}
    </div>
  )
}

/* ---------------- SMALL UI BITS ---------------- */

function OutcomePill({ outcome }: { outcome: string }) {
  switch (outcome) {
    case 'RenewalAvailable':  return <StatusPill tone="emerald">RENEW. AVAIL.</StatusPill>
    case 'RenewalCompleted':  return <StatusPill tone="emerald">COMPLETED</StatusPill>
    case 'NotDueForRenewal':  return <StatusPill tone="zinc">NOT DUE</StatusPill>
    case 'RenewalInProgress': return <StatusPill tone="indigo">IN PROGRESS</StatusPill>
    case 'NoBusinessNames':   return <StatusPill tone="amber">NO NAMES</StatusPill>
    case 'Pending':           return <StatusPill tone="zinc">PENDING</StatusPill>
    default:                  return <StatusPill tone="zinc">{outcome.toUpperCase()}</StatusPill>
  }
}

function RenewalStatusPill({ status }: { status: string }) {
  switch (status) {
    case 'Completed':  return <StatusPill tone="emerald">PAID</StatusPill>
    case 'Processing': return <StatusPill tone="indigo">PROC.</StatusPill>
    case 'Pending':    return <StatusPill tone="amber">PEND.</StatusPill>
    case 'Failed':     return <StatusPill tone="red">FAILED</StatusPill>
    default:           return <StatusPill tone="zinc">{status.toUpperCase()}</StatusPill>
  }
}

function InitiatedByBadge({ value }: { value: string }) {
  switch (value) {
    case 'Admin':    return <StatusPill tone="indigo">ADMIN</StatusPill>
    case 'Customer': return <StatusPill tone="zinc">CUSTOMER</StatusPill>
    case 'System':   return <StatusPill tone="amber">SYSTEM</StatusPill>
    default:         return <StatusPill tone="zinc">{value.toUpperCase()}</StatusPill>
  }
}

