import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Eye, EyeOff, TriangleAlert, X } from 'lucide-react'
import { api } from '../api/client'
import { DateRangeFilter, ErrorModal, Pagination, useDebouncedValue } from './_components'
import { SearchSentence, SentenceList } from './_sentences'
import { Cell, EmptyState, FunnelPills, PageHeader, RefreshButton, searchFunnelStages, Stat, StatStrip, ViewToggle } from './_ui'
import { Status } from './_status'
import { fmtDate, fmtMoney0, fmtMoney2, fmtTime, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

type SearchesResponse = Awaited<ReturnType<typeof api.admin.searches>>
type Search = SearchesResponse['items'][number]
type Stats = SearchesResponse['stats']
type Facets = SearchesResponse['facets']

type ViewMode = 'Sentences' | 'Table' | 'Cards'
const PAGE_SIZE = 10

export default function Searches() {
  // Cards are the usable default on phones; the wide table needs a desktop viewport.
  const [viewMode, setViewMode] = useState<ViewMode>('Sentences')
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
      header: 'ABN',
      // Compact on phones: the searched ABN only; names + lead return at sm.
      meta: { sticky: true, className: 'min-w-28 max-w-[38vw] sm:min-w-56 sm:max-w-72' },
      cell: ({ row }) => {
        const s = row.original
        return (
          <div>
            <div className="flex items-center gap-1.5">
              <span className="font-mono tabular-nums">{s.abn}</span>
              <RepeatBadge n={s.repeatCount7d} />
            </div>
            <div className="hidden truncate text-caption text-muted-foreground sm:block">
              {s.resultsCount === 0 ? 'no names found' : <>{s.firstBusinessName ?? '—'}{s.resultsCount > 1 ? ` +${s.resultsCount - 1}` : ''}</>}
            </div>
            <div className="hidden truncate text-caption text-muted-foreground sm:block">
              {s.lead ? <Link to={`/admin/leads/${s.lead.id}`} className="hover:underline" title={s.lead.email}>{s.lead.fullName}</Link> : 'no lead'}
            </div>
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
      meta: { className: 'text-right tabular-nums', headerClassName: 'text-right' },
      cell: ({ row }) => row.original.resultsCount === 0
        ? <span className="text-muted-foreground">—</span>
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
        <div className="flex flex-col items-end gap-1">
          <span className="font-mono tabular-nums">{fmtMoney0(row.original.renewal.amount)}</span>
          <RenewalStatusPill status={row.original.renewal.status} />
        </div>
      ) : (
        <span className="text-muted-foreground">—</span>
      ),
    },
    {
      id: 'source',
      header: 'By',
      enableSorting: false,
      cell: ({ row }) => <InitiatedByBadge value={row.original.initiatedBy} />,
    },
    {
      accessorKey: 'searchedAt',
      header: 'When',
      cell: ({ row }) => (
        <div className="whitespace-nowrap">
          <div className="tabular-nums">{relativeTime(row.original.searchedAt)}</div>
          <div className="text-caption text-muted-foreground tabular-nums">{fmtDate(row.original.searchedAt)} · {fmtTime(row.original.searchedAt)}</div>
        </div>
      ),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      meta: { className: 'text-right' },
      cell: ({ row }) => (
        <Button asChild size="sm" variant="ghost"><Link to={`/admin/searches/${row.original.id}`}>Open</Link></Button>
      ),
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader title="Searches" subtitle="ABN lookups from the wizard, staff and Renewtron itself." />

      <StatsStrip stats={stats} />

      {/* Filters, resolved on the server with live counts; the view and refresh sit at the end. */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={abnInput}
          onChange={(e) => { setPage(1); setAbnInput(e.target.value) }}
          placeholder="Filter ABN…"
          className="h-8 w-40 font-mono tabular-nums placeholder:font-sans"
        />
        <FacetedFilter title="Result" options={resultOptions} selected={results} onChange={withPageReset(setResults)} />
        <FacetedFilter title="Started by" options={initiatorOptions} selected={initiators} onChange={withPageReset(setInitiators)} />
        <DateRangeFilter dateFrom={dateFrom} dateTo={dateTo} onFrom={withPageReset(setDateFrom)} onTo={withPageReset(setDateTo)} />
        <Button
          variant="outline"
          size="sm"
          onClick={() => { setPage(1); setIncludeSystem((v) => !v) }}
          aria-pressed={includeSystem}
          className={includeSystem ? 'bg-accent' : 'border-dashed'}
          title="Renewtron's own background lookups (Ontraport sync, bulk renewals). Hidden by default."
        >
          {includeSystem ? <Eye /> : <EyeOff />}
          {includeSystem ? 'System shown' : 'System hidden'}
        </Button>
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" onClick={resetFilters}>
            Reset
            <X />
          </Button>
        ) : null}
        <div className="ml-auto flex items-center gap-2">
          <ViewToggle<ViewMode> value={viewMode} options={[{ value: 'Sentences', label: 'Sentence view' }, { value: 'Table', label: 'Table view' }, { value: 'Cards', label: 'Cards view' }]} onChange={setViewMode} />
          <RefreshButton onClick={() => void refetch()} busy={isFetching} />
        </div>
      </div>

      <div className="mt-4">
        {viewMode === 'Sentences' ? (
          <SentenceList empty={<EmptyState title="Nothing matches these filters" />}>
            {searches.map((s) => <SearchSentence key={s.id} s={s} />)}
          </SentenceList>
        ) : viewMode === 'Cards' ? (
          <CardsView searches={searches} onShowError={setErrorModal} />
        ) : (
          <DataTable columns={columns} data={searches} empty={<EmptyState title="Nothing matches these filters" />} />
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

/* ---------------- Stats ---------------- */

function StatsStrip({ stats }: { stats: Stats }) {
  return (
    <StatStrip>
      <Stat
        label="Searches"
        period="All time"
        value={stats.totalAllTime.toLocaleString()}
        sub={`${stats.total30d.toLocaleString()} in the last 30 days`}
      />
      <Stat
        label="Answered by ASIC"
        period="Last 30 days"
        value={`${stats.successRate30d}%`}
        sub={`${stats.success30d.toLocaleString()} of ${stats.total30d.toLocaleString()}`}
        dot={stats.total30d === 0 ? null : stats.successRate30d < 50 ? 'action' : stats.successRate30d < 80 ? 'waiting' : null}
      />
      <Stat
        label="Became a renewal"
        period="Last 30 days"
        value={`${stats.conversionRate30d}%`}
        sub={`${stats.conversions30d.toLocaleString()} paid renewals`}
      />
      <Stat
        label="Today"
        period={stats.deltaPct != null ? `${stats.deltaPct >= 0 ? '+' : ''}${stats.deltaPct}%` : undefined}
        value={stats.today.toLocaleString()}
        sub={`${stats.yesterday.toLocaleString()} yesterday`}
        trend={stats.daily14d.map((d) => ({ date: d.date, count: d.count }))}
        trendLabel="searches"
        hint="Searches today; the line is the last 14 days"
      />
    </StatStrip>
  )
}

/* ---------------- CARDS VIEW (mobile default) ---------------- */

function CardsView({ searches, onShowError }: { searches: Search[]; onShowError: (m: string) => void }) {
  if (searches.length === 0) {
    return <EmptyState title="Nothing matches these filters" />
  }
  return (
    <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
      {searches.map((s) => (
        <Card key={s.id} className="gap-4 p-5">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="flex items-center gap-1.5">
                <span className="font-mono tabular-nums">{s.abn}</span>
                <RepeatBadge n={s.repeatCount7d} />
              </div>
              <div className="truncate text-caption text-muted-foreground">
                {s.lead ? <>{s.lead.fullName} · {s.lead.email}</> : 'no lead'}
              </div>
            </div>
            <div className="flex shrink-0 items-center gap-1.5">
              <InitiatedByBadge value={s.initiatedBy} />
              <OutcomeCell s={s} onError={onShowError} />
            </div>
          </div>

          <FunnelPills stages={searchFunnelStages(s.funnel)} />

          <dl className="grid grid-cols-2 gap-x-4 gap-y-3">
            <Cell label="Names found" value={
              s.resultsCount === 0
                ? 'None'
                : <span className="block truncate">{s.firstBusinessName ?? '—'}{s.resultsCount > 1 ? <span className="text-muted-foreground"> +{s.resultsCount - 1}</span> : null}</span>
            } />
            <Cell label="Renewal" value={
              s.renewal
                ? <span className="flex items-center gap-1.5"><span className="font-mono tabular-nums">{fmtMoney2(s.renewal.amount)}</span><RenewalStatusPill status={s.renewal.status} /></span>
                : '—'
            } />
            <Cell label="When" value={<span title={`${fmtDate(s.searchedAt)} · ${fmtTime(s.searchedAt)}`}>{relativeTime(s.searchedAt)}</span>} />
            <Cell label="IP" value={s.ipAddress ?? '—'} mono />
          </dl>

          <div className="flex justify-end border-t pt-4">
            <Button asChild size="sm" variant="ghost"><Link to={`/admin/searches/${s.id}`}>Open</Link></Button>
          </div>
        </Card>
      ))}
    </div>
  )
}

/* ---------------- SMALL UI BITS ---------------- */

function OutcomeCell({ s, onError }: { s: Search; onError: (m: string) => void }) {
  if (!s.success) {
    return (
      <div className="flex items-center gap-1">
        <Status entry={{ label: 'No answer', meaning: 'quiet', hint: 'ASIC didn’t answer the lookup.' }} />
        {s.errorMessage ? (
          <Button
            variant="ghost"
            size="icon-xs"
            onClick={(e) => { e.preventDefault(); onError(s.errorMessage ?? '') }}
            title="See the error"
            aria-label="See the error"
          >
            <TriangleAlert className="text-destructive" />
          </Button>
        ) : null}
      </div>
    )
  }
  if (s.lead?.outcome) return <OutcomePill outcome={s.lead.outcome} />
  return <Status entry={{ label: 'Answered', meaning: 'quiet' }} />
}

/** "3× this week" when the same ABN keeps being searched. */
function RepeatBadge({ n }: { n: number }) {
  if (n < 3) return null
  return <Badge variant="secondary" className="tabular-nums" title={`Searched ${n} times in the last 7 days`}>{n}× this week</Badge>
}

function OutcomePill({ outcome }: { outcome: string }) {
  return <Status kind="lead" status={outcome} />
}

function RenewalStatusPill({ status }: { status: string }) {
  return <Status kind="renewal" status={status} />
}

function InitiatedByBadge({ value }: { value: string }) {
  const words: Record<string, string> = { Admin: 'Staff', Customer: 'Visitor', System: 'Renewtron' }
  return <Badge variant="outline">{words[value] ?? value}</Badge>
}
