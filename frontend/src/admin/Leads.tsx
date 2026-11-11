import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Mail, PlusCircle, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { DateRangeFilter, Pagination, useDebouncedValue } from './_components'
import { Cell, EmptyState, FunnelPills, PageHeader, RefreshButton, Stat, StatStrip, ViewToggle, type Tone } from './_ui'
import { LeadSentence, SentenceList } from './_sentences'
import { Status, visitStatus } from './_status'
import { durationShort, fmtDate, fmtMoney0, fmtTime, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { sample } from '@/components/skeleton-samples'
import { sampleRows, Skeletonizer } from '@/components/skeletonizer'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Command, CommandGroup, CommandItem, CommandList, CommandSeparator,
} from '@/components/ui/command'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'

type LeadsResponse = Awaited<ReturnType<typeof api.admin.leads>>
type Lead = LeadsResponse['items'][number]
type Stats = LeadsResponse['stats']
type Facets = LeadsResponse['facets']

const SAMPLE_OUTCOMES = ['RenewalAvailable', 'RenewalCompleted', 'NotDueForRenewal', 'RenewalInProgress']
const SKELETON_ROWS = sampleRows<Lead>(8, (i) => ({
  id: sample.id(i), abn: sample.abn(i), fullName: sample.name(i), email: sample.email(i), mobileNumber: sample.mobile(),
  createdAt: sample.daysAgo(i), outcome: SAMPLE_OUTCOMES[i % SAMPLE_OUTCOMES.length], reminderOptIn: i % 2 === 0,
  convertedToRenewal: i % 4 === 1, convertedAt: i % 4 === 1 ? sample.daysAgo(i) : null,
  searchLog: { id: sample.id(100 + i), searchedAt: sample.daysAgo(i), success: true, resultsCount: 1 + (i % 3) },
  firstBusinessName: sample.business(i), renewal: i % 4 === 1 ? { id: sample.id(200 + i), status: 'Completed', amount: sample.money(i) } : null,
}))
const SKELETON_STATS: Stats = {
  totalAllTime: 48210, total30d: 3120, converted30d: 412, conversionRate30d: 13, avgHoursToConvert: 6,
  today: 104, yesterday: 98, deltaPct: 6, daily14d: [], outcomeBreakdown: {}, winBackEligible: 1840,
  winBackRecoverableValue: 254000, avgBasket: 138,
}

const PAGE_SIZE = 25

/** Operator-friendly names for LeadOutcome values. */
const outcomeLabels: Record<string, string> = {
  RenewalAvailable: 'Available',
  RenewalCompleted: 'Converted',
  NotDueForRenewal: 'Not due',
  RenewalInProgress: 'In progress',
  NoBusinessNames: 'No names',
  Pending: 'Pending',
}

const reminderLabels: Record<string, string> = {
  OptedIn: 'Opted in',
  NotOptedIn: 'Not opted in',
}

export default function Leads() {
  // Deep link from the dashboard ("?outcome=RenewalAvailable") seeds the facet on first render.
  const [searchParams] = useSearchParams()
  const [page, setPage] = useState(1)
  // Every filter change goes back to page 1.
  const paged = <T,>(set: (v: T) => void) => (v: T) => { setPage(1); set(v) }
  const [outcomes, setOutcomesRaw] = useState<string[]>(() => searchParams.get('outcome')?.split(',').filter(Boolean) ?? [])
  const [reminders, setRemindersRaw] = useState<string[]>([])
  const [hasRenewal, setHasRenewalRaw] = useState('')
  const [dateFrom, setDateFromRaw] = useState('')
  const [dateTo, setDateToRaw] = useState('')
  const [search, setSearchRaw] = useState('')
  const setOutcomes = paged(setOutcomesRaw)
  const setReminders = paged(setRemindersRaw)
  const setHasRenewal = paged(setHasRenewalRaw)
  const setDateFrom = paged(setDateFromRaw)
  const setDateTo = paged(setDateToRaw)
  const setSearch = paged(setSearchRaw)
  const [winBackOpen, setWinBackOpen] = useState(false)
  const debouncedSearch = useDebouncedValue(search)

  const queryClient = useQueryClient()
  const { data, isFetching, isPending, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-leads', { outcomes, reminders, search: debouncedSearch, hasRenewal, dateFrom, dateTo, page }],
    queryFn: () => api.admin.leads({
      page,
      take: PAGE_SIZE,
      outcome: outcomes.join(',') || undefined,
      reminder: reminders.join(',') || undefined,
      search: debouncedSearch || undefined,
      hasRenewal: hasRenewal || undefined,
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
    }),
    placeholderData: keepPreviousData,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-leads'] })

  const leads = data?.items ?? []
  const [viewMode, setViewMode] = useState<'Sentences' | 'Table'>('Sentences')
  const totalCount = data?.totalCount ?? 0
  const stats: Stats = data?.stats ?? (isPending ? SKELETON_STATS : defaultStats())
  const facets: Facets = data?.facets ?? { outcome: [], reminder: [] }

  const outcomeOptions: FacetOption[] = facets.outcome.map((f) => ({ value: f.value, label: outcomeLabels[f.value] ?? f.value, count: f.count }))
  const reminderOptions: FacetOption[] = facets.reminder.map((f) => ({ value: f.value, label: reminderLabels[f.value] ?? f.value, count: f.count }))

  const hasActiveFilters = !!search || outcomes.length > 0 || reminders.length > 0 || !!hasRenewal || !!dateFrom || !!dateTo
  const resetFilters = () => {
    setSearch(''); setOutcomes([]); setReminders([]); setHasRenewal(''); setDateFrom(''); setDateTo('')
  }

  // The header button sits outside the skeleton, so it reads only real counts.
  const winBackEligible = data?.stats.winBackEligible ?? 0
  const winBackEnabled = winBackEligible > 0

  // Win-back endpoints take a single outcome / a boolean — pass them only when the
  // facet selection is unambiguous (exactly one value picked).
  const winBackFilter = {
    outcome: outcomes.length === 1 ? outcomes[0] : undefined,
    search: search || undefined,
    reminderOptIn: reminders.length === 1 ? reminders[0] === 'OptedIn' : undefined,
  }
  const winBackMutation = useMutation({
    mutationFn: () => api.admin.winBackSend(winBackFilter),
    onSuccess: () => setWinBackOpen(false),
    onSettled: () => void invalidate(),
  })
  const sendWinBack = () => {
    void sileo.promise(winBackMutation.mutateAsync(), {
      loading: { title: 'Sending win-back emails…' },
      success: (r) => ({ title: `Win-back email sent to ${r.enqueued} lead${r.enqueued === 1 ? '' : 's'}.` }),
      error: (e) => ({ title: 'Win-back send failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const toggleOutcome = (value: string) => {
    setOutcomes((prev) => prev.includes(value) ? prev.filter((v) => v !== value) : [...prev, value])
  }

  const columns = useMemo<DataTableColumn<Lead>[]>(() => [
    {
      id: 'customer',
      accessorKey: 'fullName',
      header: 'Lead',
      // Compact on phones: name only (no avatar); email + ABN return at sm.
      meta: { sticky: true, className: 'min-w-28 max-w-[38vw] sm:min-w-56 sm:max-w-76' },
      cell: ({ row }) => {
        const l = row.original
        return (
          <div className="min-w-0">
            <Link to={`/admin/leads/${l.id}`} className="block truncate font-medium hover:underline">{l.fullName || '—'}</Link>
            <div className="hidden truncate text-caption text-muted-foreground sm:block">{l.email}</div>
            <div className="hidden text-caption text-muted-foreground sm:block">
              <button type="button" onClick={() => toggleOutcome(l.outcome)} className="font-mono tabular-nums hover:underline" title="Filter by this lead's outcome">{l.abn}</button>
            </div>
          </div>
        )
      },
    },
    {
      accessorKey: 'outcome',
      header: 'Outcome',
      cell: ({ row }) => <OutcomePill row={row.original} />,
    },
    {
      id: 'names',
      header: 'Names found',
      enableSorting: false,
      meta: { hideBelow: 'sm' },
      cell: ({ row }) => <NamesCell l={row.original} />,
    },
    {
      id: 'funnel',
      header: 'Funnel',
      enableSorting: false,
      meta: { hideBelow: 'md' },
      cell: ({ row }) => <FunnelPills stages={leadFunnelStages(row.original)} />,
    },
    {
      id: 'renewal',
      header: 'Renewal',
      enableSorting: false,
      meta: { className: 'text-right', headerClassName: 'text-right' },
      cell: ({ row }) => {
        const r = row.original.renewal
        return r ? (
          <div className="flex flex-col items-end gap-1">
            <span className="font-mono tabular-nums">{fmtMoney0(r.amount)}</span>
            <RenewalStatusPill status={r.status} />
          </div>
        ) : <span className="text-muted-foreground">—</span>
      },
    },
    {
      accessorKey: 'reminderOptIn',
      header: 'Reminder',
      enableSorting: false,
      meta: { hideBelow: 'md' },
      cell: ({ row }) =>
        row.original.reminderOptIn ? <Badge variant="secondary">Opted in</Badge> : <span className="text-muted-foreground">—</span>,
    },
    {
      accessorKey: 'createdAt',
      header: 'Age',
      cell: ({ row }) => (
        <div className="whitespace-nowrap">
          <div className="tabular-nums">{relativeTime(row.original.createdAt)}</div>
          <div className="hidden text-caption text-muted-foreground tabular-nums sm:block">{fmtDate(row.original.createdAt)} · {fmtTime(row.original.createdAt)}</div>
        </div>
      ),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      meta: { className: 'text-right' },
      cell: ({ row }) => (
        <Button asChild size="sm" variant="ghost"><Link to={`/admin/leads/${row.original.id}`}>Open</Link></Button>
      ),
    },
    // toggleOutcome only calls stable state setters, so the first render's copy stays correct.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <PageHeader
        title="Leads"
        subtitle="People who looked up their ABN and left their details."
        right={
          <Button
            size="sm"
            onClick={() => setWinBackOpen(true)}
            disabled={!winBackEnabled}
            title="Email leads who found a name due but never paid"
          >
            <Mail />
            Send win-back
            {winBackEnabled ? <span className="font-normal tabular-nums opacity-70">{winBackEligible}</span> : null}
          </Button>
        }
      />

      <Skeletonizer loading={isPending}><StatsStrip stats={stats} /></Skeletonizer>

      {/* Filters, resolved on the server with live counts; the view and refresh sit at the end. */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search ABN, name or email…"
          className="h-8 w-full sm:w-56"
        />
        <FacetedFilter title="Outcome" options={outcomeOptions} selected={outcomes} onChange={setOutcomes} />
        <FacetedFilter title="Reminder" options={reminderOptions} selected={reminders} onChange={setReminders} />
        <HasRenewalFilter value={hasRenewal} onChange={setHasRenewal} />
        <DateRangeFilter dateFrom={dateFrom} dateTo={dateTo} onFrom={setDateFrom} onTo={setDateTo} />
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" onClick={resetFilters}>
            Reset
            <X />
          </Button>
        ) : null}
        <div className="flex w-full items-center justify-end gap-2 sm:ml-auto sm:w-auto">
          <ViewToggle<'Sentences' | 'Table'> value={viewMode} options={[{ value: 'Sentences', label: 'Sentence view' }, { value: 'Table', label: 'Table view' }]} onChange={setViewMode} />
          <RefreshButton onClick={() => void refetch()} busy={isFetching} />
        </div>
      </div>

      <div className="mt-4">
        {viewMode === 'Sentences' ? (
          <Skeletonizer loading={isPending}>
            <SentenceList empty={<EmptyState title="Nothing matches these filters" />}>
              {(isPending ? SKELETON_ROWS : leads).map((l) => <LeadSentence key={l.id} l={l} />)}
            </SentenceList>
          </Skeletonizer>
        ) : (
          <DataTable columns={columns} data={leads} loading={isPending || (isFetching && isPlaceholderData)} skeleton={SKELETON_ROWS} empty={<EmptyState title="Nothing matches these filters" />} />
        )}
        {totalCount > PAGE_SIZE ? (
          <Pagination page={page} pageSize={PAGE_SIZE} total={totalCount} onPage={setPage} />
        ) : null}
      </div>

      {/* Win-back modal */}
      {winBackOpen ? (
        <WinBackModal
          filter={winBackFilter}
          busy={winBackMutation.isPending}
          onClose={() => setWinBackOpen(false)}
          onConfirm={sendWinBack}
        />
      ) : null}
    </div>
  )
}

function HasRenewalFilter({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const options = [
    { value: 'true', label: 'Has renewal' },
    { value: 'false', label: 'No renewal' },
  ]
  const current = options.find((o) => o.value === value)
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="border-dashed">
          <PlusCircle />
          Renewal
          {current ? (
            <>
              <Separator orientation="vertical" className="mx-0.5 data-[orientation=vertical]:h-4" />
              <Badge variant="secondary" className="rounded-sm px-1 font-normal">{current.label}</Badge>
            </>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-48 p-0" align="start">
        <Command>
          <CommandList>
            <CommandGroup>
              {options.map((o) => {
                const isSelected = o.value === value
                return (
                  <CommandItem key={o.value} onSelect={() => onChange(isSelected ? '' : o.value)}>
                    <div className={cn(
                      'flex size-4 items-center justify-center rounded-full border',
                      isSelected ? 'border-primary bg-primary text-primary-foreground' : 'border-input [&_svg]:invisible',
                    )}>
                      <Check className="size-3.5" />
                    </div>
                    <span>{o.label}</span>
                  </CommandItem>
                )
              })}
            </CommandGroup>
            {value ? (
              <>
                <CommandSeparator />
                <CommandGroup>
                  <CommandItem onSelect={() => onChange('')} className="justify-center text-center">Clear filter</CommandItem>
                </CommandGroup>
              </>
            ) : null}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}

function defaultStats(): Stats {
  return {
    totalAllTime: 0,
    total30d: 0,
    converted30d: 0,
    conversionRate30d: 0,
    avgHoursToConvert: null,
    today: 0,
    yesterday: 0,
    deltaPct: null,
    daily14d: [],
    outcomeBreakdown: {},
    winBackEligible: 0,
    winBackRecoverableValue: 0,
    avgBasket: 0,
  }
}

function StatsStrip({ stats }: { stats: Stats }) {
  return (
    <StatStrip>
      <Stat
        label="Leads"
        period="All time"
        value={stats.totalAllTime.toLocaleString()}
        sub={`${stats.total30d.toLocaleString()} in the last 30 days`}
      />
      <Stat
        label="Paid to renew"
        period="Last 30 days"
        value={`${stats.conversionRate30d}%`}
        sub={stats.avgHoursToConvert != null
          ? `${stats.converted30d.toLocaleString()} paid · ${durationShort(Number(stats.avgHoursToConvert))} on average`
          : `${stats.converted30d.toLocaleString()} paid`}
      />
      <Stat
        label="Could win back"
        value={fmtMoney0(stats.winBackRecoverableValue)}
        sub={`${stats.winBackEligible.toLocaleString()} leads · ${fmtMoney0(stats.avgBasket)} each, on average`}
        hint="Leads who found a name due and didn't pay"
      />
      <Stat
        label="Today"
        period={stats.deltaPct != null ? `${stats.deltaPct >= 0 ? '+' : ''}${stats.deltaPct}%` : undefined}
        value={stats.today.toLocaleString()}
        sub={`${stats.yesterday.toLocaleString()} yesterday`}
        trend={stats.daily14d.map((d) => ({ date: d.date, count: d.count }))}
        trendLabel="leads"
        hint="Leads today; the line is the last 14 days"
      />
    </StatStrip>
  )
}

/* ─────── Cell components ─────── */

function NamesCell({ l }: { l: Lead }) {
  if (l.searchLog == null) return <span className="text-muted-foreground">No search</span>
  if (l.searchLog.resultsCount === 0) return <span className="text-muted-foreground">None</span>
  return (
    <div className="max-w-64">
      <div className="truncate">{l.firstBusinessName ?? '—'}</div>
      {l.searchLog.resultsCount > 1 ? <div className="text-caption text-muted-foreground tabular-nums">+{l.searchLog.resultsCount - 1} more</div> : null}
    </div>
  )
}

function leadFunnelStages(l: Lead): Array<{ label: string; tone: Tone; active: boolean }> {
  const renewalStatus = l.renewal?.status
  const paid = renewalStatus === 'Completed'
  const inflight = renewalStatus === 'Pending' || renewalStatus === 'Processing'
  const failed = renewalStatus === 'Failed'

  return [
    { label: 'Lead', tone: 'emerald', active: true },
    { label: 'Search', tone: 'emerald', active: !!l.searchLog },
    failed
      ? { label: 'Failed', tone: 'red', active: true }
      : { label: paid ? 'Paid' : inflight ? 'Renewing' : 'Paid', tone: paid ? 'emerald' : inflight ? 'amber' : 'zinc', active: paid || inflight },
  ]
}

function OutcomePill({ row }: { row: { outcome: string; progress?: string | null; convertedToRenewal?: boolean } }) {
  return <Status entry={visitStatus(row)} />
}

function RenewalStatusPill({ status }: { status: string }) {
  return <Status kind="renewal" status={status} />
}

/* ───────── Win-back modal ───────── */

function WinBackModal({ filter, busy, onClose, onConfirm }: {
  filter: { outcome?: string; search?: string; reminderOptIn?: boolean }
  busy: boolean
  onClose: () => void
  onConfirm: () => void
}) {
  const { data: preview, isPending, error } = useQuery({
    queryKey: ['admin-winback-preview', filter],
    queryFn: () => api.admin.winBackPreview(filter),
  })

  return (
    <Dialog open onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>Send win-back email</DialogTitle>
          <DialogDescription>To leads who found a name due but never paid.</DialogDescription>
        </DialogHeader>
        <DialogBody>
          {isPending ? (
            <p className="text-sm text-muted-foreground">Loading who it goes to…</p>
          ) : error ? (
            <p className="text-sm text-destructive">{error instanceof Error ? error.message : 'Preview failed'}</p>
          ) : preview ? (
            <PreviewBody preview={preview} />
          ) : null}
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancel</Button>
          <Button onClick={onConfirm} disabled={!preview || preview.recipientCount === 0 || busy}>
            {busy ? 'Sending…' : preview ? `Send to ${preview.recipientCount}` : 'Send'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function PreviewBody({ preview }: { preview: Awaited<ReturnType<typeof api.admin.winBackPreview>> }) {
  return (
    <div className="space-y-4">
      <dl className="grid grid-cols-2 gap-x-4 gap-y-3 rounded-lg border p-3">
        <Cell
          label="Recipients"
          value={<span title={preview.sampleNames.length > 0 ? `e.g. ${preview.sampleNames.join(' · ')}` : undefined}>{preview.recipientCount.toLocaleString()}</span>}
        />
        <Cell
          label="Could win back"
          value={<span title={`${fmtMoney0(preview.avgBasket)} average basket × recipients`}>{fmtMoney0(preview.recoverableValue)}</span>}
        />
      </dl>

      <div className="grid gap-1.5">
        <div className="text-caption text-muted-foreground">Subject</div>
        <div className="rounded-md border bg-muted/50 px-3 py-2 text-sm font-medium break-words">{preview.subject}</div>
      </div>

      <div className="grid gap-1.5" title={'Edit the template in Settings › Win-back. Merge tags: {{FullName}}, {{Abn}}, {{BusinessName}}, {{Email}}.'}>
        <div className="text-caption text-muted-foreground">Email</div>
        <pre className="max-h-48 overflow-y-auto rounded-md border bg-muted/50 px-3 py-2 font-sans text-caption break-words whitespace-pre-wrap">{preview.bodyPreview}</pre>
      </div>
    </div>
  )
}
