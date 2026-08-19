import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { CalendarRange, CircleAlert, Eye, EyeOff, X } from 'lucide-react'
import { api } from '../api/client'
import { ErrorModal, Pagination, useDebouncedValue } from './_components'
import { Cell, EmptyState, FunnelPills, PageHeader, RefreshButton, searchFunnelStages, SparklineTile, StatTile, StatusPill, ViewToggle } from './_ui'
import { fmtDate, fmtMoney0, fmtMoney2, fmtTime, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'

type SearchesResponse = Awaited<ReturnType<typeof api.admin.searches>>
type Search = SearchesResponse['items'][number]
type Stats = SearchesResponse['stats']
type Facets = SearchesResponse['facets']

type ViewMode = 'Table' | 'Cards'
const PAGE_SIZE = 10

export default function Searches() {
  // Cards are the usable default on phones; the wide table needs a desktop viewport.
  const [viewMode, setViewMode] = useState<ViewMode>(() =>
    window.matchMedia('(min-width: 1024px)').matches ? 'Table' : 'Cards')
  const [page, setPage] = useState(1)

  const [abnInput, setAbnInput] = useState('')
  const abn = useDebouncedValue(abnInput, 300)
  const [results, setResults] = useState<string[]>([])
  const [initiators, setInitiators] = useState<string[]>([])
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [includeSystem, setIncludeSystem] = useState(false)

  const [errorModal, setErrorModal] = useState<string | null>(null)

  const { data, isFetching, refetch } = useQuery({
    queryKey: ['admin-searches', { abn, results, initiators, dateFrom, dateTo, includeSystem, page }],
    queryFn: () => api.admin.searches({
      abn: abn || undefined,
      // Both result values selected = no filter; a single one round-trips "Success"/"Failed".
      success: results.length === 1 ? results[0] : undefined,
      initiatedBy: initiators.join(',') || undefined,
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
      includeSystem: includeSystem || undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
    placeholderData: keepPreviousData,
  })

  const totalCount = data?.totalCount ?? 0
  const searches = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const facets: Facets = data?.facets ?? { result: [], initiatedBy: [] }

  const hasActiveFilters = !!abnInput || results.length > 0 || initiators.length > 0 || !!dateFrom || !!dateTo || includeSystem
  const resetFilters = () => {
    setPage(1)
    setAbnInput(''); setResults([]); setInitiators([]); setDateFrom(''); setDateTo(''); setIncludeSystem(false)
  }
  const withPageReset = <T,>(setter: (v: T) => void) => (v: T) => { setPage(1); setter(v) }

  const resultOptions: FacetOption[] = facets.result.map((f) => ({ value: f.value, label: f.value, count: f.count }))
  const initiatorOptions: FacetOption[] = facets.initiatedBy.map((f) => ({ value: f.value, label: f.value, count: f.count }))

  const columns = useMemo<DataTableColumn<Search>[]>(() => [
    {
      id: 'identity',
      accessorKey: 'abn',
      header: 'ABN · Name · Customer',
      meta: { sticky: true, className: 'min-w-[14rem] max-w-[18rem]' },
      cell: ({ row }) => {
        const s = row.original
        return (
          <div>
            <div className="flex items-center gap-1.5">
              <span className="text-sm font-mono tabular-nums text-zinc-900">{s.abn}</span>
              {s.repeatCount7d >= 3 ? <StatusPill tone="amber">×{s.repeatCount7d} 7D</StatusPill> : null}
            </div>
            {s.resultsCount === 0 ? (
              <div className="mt-0.5 text-xxs font-mono text-zinc-400">no names found</div>
            ) : (
              <div className="mt-0.5 text-sm text-zinc-700 truncate">
                {s.firstBusinessName ?? '—'}
                {s.resultsCount > 1 ? <span className="ml-1 text-xxs font-mono text-zinc-400 tabular-nums">+{s.resultsCount - 1}</span> : null}
              </div>
            )}
            {s.lead ? (
              <Link to={`/admin/leads/${s.lead.id}`} className="block mt-0.5 text-xxs font-mono text-zinc-400 hover:underline truncate">
                {s.lead.fullName} · {s.lead.email}
              </Link>
            ) : (
              <div className="mt-0.5 text-xxs font-mono text-zinc-400">no lead</div>
            )}
          </div>
        )
      },
    },
    {
      id: 'outcome',
      header: 'Outcome',
      enableSorting: false,
      cell: ({ row }) => <OutcomeCell s={row.original} onError={setErrorModal} />,
    },
    {
      accessorKey: 'resultsCount',
      header: 'Names',
      meta: { className: 'text-right font-mono tabular-nums text-sm text-zinc-700', headerClassName: 'text-right' },
      cell: ({ row }) => row.original.resultsCount === 0
        ? <span className="text-xxs text-zinc-400">—</span>
        : row.original.resultsCount,
    },
    {
      id: 'funnel',
      header: 'Funnel',
      enableSorting: false,
      cell: ({ row }) => <FunnelPills stages={searchFunnelStages(row.original.funnel)} />,
    },
    {
      id: 'renewal',
      header: 'Renewal',
      enableSorting: false,
      meta: { className: 'text-right', headerClassName: 'text-right' },
      cell: ({ row }) => row.original.renewal ? (
        <div>
          <div className="text-sm font-mono tabular-nums text-zinc-900">{fmtMoney0(row.original.renewal.amount)}</div>
          <RenewalStatusPill status={row.original.renewal.status} />
        </div>
      ) : (
        <span className="text-xxs font-mono text-zinc-400">—</span>
      ),
    },
    {
      id: 'source',
      header: 'Source',
      enableSorting: false,
      cell: ({ row }) => <InitiatedByBadge value={row.original.initiatedBy} />,
    },
    {
      accessorKey: 'searchedAt',
      header: 'When',
      cell: ({ row }) => (
        <div>
          <div className="text-sm text-zinc-700 tabular-nums whitespace-nowrap">{relativeTime(row.original.searchedAt)}</div>
          <div className="text-xxs font-mono text-zinc-400 tabular-nums whitespace-nowrap">{fmtDate(row.original.searchedAt)} · {fmtTime(row.original.searchedAt)}</div>
        </div>
      ),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      meta: { className: 'text-right' },
      cell: ({ row }) => (
        <Link to={`/admin/searches/${row.original.id}`} className="text-sm font-medium text-brand-700 hover:text-brand-800 whitespace-nowrap">View →</Link>
      ),
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Search logs"
        subtitle="ABN searches recorded by customers, admins, and the bulk pipelines."
        right={
          <>
            <ViewToggle<ViewMode> value={viewMode} options={[{ value: 'Table', label: 'Table view' }, { value: 'Cards', label: 'Cards view' }]} onChange={setViewMode} />
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      <StatsStrip stats={stats} />

      {/* Filter toolbar — every facet is resolved server-side with live counts */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={abnInput}
          onChange={(e) => { setPage(1); setAbnInput(e.target.value) }}
          placeholder="Filter ABN…"
          className="h-9 w-40 font-mono tabular-nums"
        />
        <FacetedFilter title="Result" options={resultOptions} selected={results} onChange={withPageReset(setResults)} />
        <FacetedFilter title="Initiated by" options={initiatorOptions} selected={initiators} onChange={withPageReset(setInitiators)} />
        <DateRangeFilter dateFrom={dateFrom} dateTo={dateTo} onFrom={withPageReset(setDateFrom)} onTo={withPageReset(setDateTo)} />
        <Button
          variant="outline"
          size="sm"
          onClick={() => { setPage(1); setIncludeSystem((v) => !v) }}
          className={`h-9 border-dashed whitespace-nowrap ${includeSystem ? 'bg-brand-50 text-brand-800 border-brand-200 hover:bg-brand-100' : 'text-zinc-500'}`}
          title="System searches are background lookups for Ontraport sync and bulk renewals — hidden by default, noisy for daily ops. Selecting System under Initiated by also shows them."
        >
          {includeSystem ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}
          {includeSystem ? 'System shown' : 'System hidden'}
        </Button>
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" className="h-9" onClick={resetFilters}>
            Reset
            <X className="h-4 w-4" />
          </Button>
        ) : null}
      </div>

      <div className="mt-4">
        {viewMode === 'Cards' ? (
          <CardsView searches={searches} onShowError={setErrorModal} />
        ) : (
          <DataTable columns={columns} data={searches} empty={<EmptyState title="No searches match the current filters." />} />
        )}

        <Pagination page={page} pageSize={PAGE_SIZE} total={totalCount} onPage={setPage} />
      </div>

      <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Search error details" />
    </div>
  )
}

function DateRangeFilter({ dateFrom, dateTo, onFrom, onTo }: {
  dateFrom: string
  dateTo: string
  onFrom: (v: string) => void
  onTo: (v: string) => void
}) {
  const active = dateFrom || dateTo
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="h-9 border-dashed whitespace-nowrap">
          <CalendarRange className="h-4 w-4" />
          Dates
          {active ? <span className="font-mono text-xs text-muted-foreground">{dateFrom || '…'} → {dateTo || '…'}</span> : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-64 space-y-3" align="start">
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">From</div>
          <Input type="date" value={dateFrom} onChange={(e) => onFrom(e.target.value)} className="font-mono tabular-nums" />
        </div>
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">To</div>
          <Input type="date" value={dateTo} onChange={(e) => onTo(e.target.value)} className="font-mono tabular-nums" />
        </div>
        {active ? (
          <Button variant="ghost" size="sm" className="w-full" onClick={() => { onFrom(''); onTo('') }}>Clear dates</Button>
        ) : null}
      </PopoverContent>
    </Popover>
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
        labels={stats.daily14d.map((d) => d.date)}
      />
    </div>
  )
}

/* ---------------- CARDS VIEW (mobile default) ---------------- */

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
              <OutcomeCell s={s} onError={onShowError} />
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

function OutcomeCell({ s, onError }: { s: Search; onError: (m: string) => void }) {
  if (!s.success) {
    return (
      <div className="flex items-center gap-1.5">
        <StatusPill tone="red">FAILED</StatusPill>
        {s.errorMessage ? (
          <button
            onClick={(e) => { e.preventDefault(); onError(s.errorMessage ?? '') }}
            className="inline-flex items-center justify-center h-5 w-5 rounded-full bg-red-50 text-red-600 hover:bg-red-100"
            title="View error"
          >
            <CircleAlert className="h-3 w-3" />
          </button>
        ) : null}
      </div>
    )
  }
  if (s.lead?.outcome) return <OutcomePill outcome={s.lead.outcome} />
  return <StatusPill tone="emerald">SUCCESS</StatusPill>
}

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
