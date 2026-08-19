import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleAlert, ListRestart, Loader2, RefreshCw, TriangleAlert, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { ErrorModal, useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, type Tone } from './_ui'
import { fmtMoney0, fmtMoney2, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

type SalesResponse = Awaited<ReturnType<typeof api.admin.ontraportSales>>
type Sale = SalesResponse['items'][number]
type Stats = SalesResponse['stats']
type Facets = SalesResponse['facets']

/** A sale annotated with days until its due date (null when no due date is known). */
type SaleRow = Sale & { _daysUntil: number | null }

type DueFilter = 'overdue' | '7d' | '30d' | 'all'

const filledStatuses = [
  'Synced',
  'WaitingForRenewalWindow',
  'RenewalQueued',
  'RenewalFailed',
  'IneligibleForRenewal',
  'AsicNotYetDue',
  'RenewalInProgress',
]

// Statuses that aren't real failures — they'll retry automatically (AsicNotYetDue, RenewalInProgress)
// or have been deliberately skipped (IneligibleForRenewal). Render with a yellow/info treatment, not red.
const blockedStatuses = ['IneligibleForRenewal', 'AsicNotYetDue', 'RenewalInProgress']

/** Operator-friendly names for the raw OntraportSaleStatus enum values. */
function statusLabel(status: string): string {
  switch (status) {
    case 'RenewalFailed':           return 'Failed'
    case 'WaitingForRenewalWindow': return 'Waiting'
    case 'AsicNotYetDue':           return 'ASIC not due'
    case 'RenewalInProgress':       return 'In progress'
    case 'IneligibleForRenewal':    return 'Ineligible'
    case 'RenewalCompleted':        return 'Completed'
    case 'Synced':                  return 'Synced'
    case 'RenewalQueued':           return 'Queued'
    case 'NotDueForRenewal':        return 'Not due'
    default:                        return status
  }
}

export default function OntraportSales() {
  const [dueFilter, setDueFilter] = useState<DueFilter>('30d')
  const [searchInput, setSearchInput] = useState('')
  const search = useDebouncedValue(searchInput, 300)
  const [statuses, setStatuses] = useState<string[]>([])
  const [requeueOpen, setRequeueOpen] = useState(false)
  const [errorModal, setErrorModal] = useState<{ title: string; body: string } | null>(null)

  const queryClient = useQueryClient()
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['admin-ontraport-sales', { statuses, search }],
    queryFn: () => api.admin.ontraportSales({
      status: statuses.join(',') || undefined,
      search: search || undefined,
    }),
    placeholderData: keepPreviousData,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-ontraport-sales'] })

  const stats: Stats = data?.stats ?? defaultStats()
  const facets: Facets = data?.facets ?? { status: [] }
  const failedCount = data?.failedCount ?? 0
  const blockedCount = data?.blockedCount ?? 0
  const asicNotYetDueCount = data?.asicNotYetDueCount ?? 0
  const renewalInProgressCount = data?.renewalInProgressCount ?? 0
  const ineligibleCount = data?.ineligibleCount ?? 0

  const hasActiveFilters = !!searchInput || statuses.length > 0
  const resetFilters = () => { setSearchInput(''); setStatuses([]) }

  const statusOptions: FacetOption[] = facets.status.map((f) => ({ value: f.value, label: statusLabel(f.value), count: f.count }))

  // Server-filtered items, annotated with days until the due date.
  const items = useMemo<SaleRow[]>(() => {
    const now = Date.now()
    return (data?.items ?? []).map((s) => ({
      ...s,
      _daysUntil: s.renewalDueDate ? Math.floor((new Date(s.renewalDueDate).getTime() - now) / 86400000) : null,
    }))
  }, [data])

  // Sales that are still in-flight (not Completed) and have a due date — the actionable "pipeline".
  const pipeline = useMemo(
    () => items
      .filter((s): s is SaleRow & { _daysUntil: number } => filledStatuses.includes(s.status) && s._daysUntil !== null)
      .sort((a, b) => a._daysUntil - b._daysUntil),
    [items],
  )

  const byBucket = useMemo(() => ({
    overdue: pipeline.filter((s) => s._daysUntil < 0),
    soon7:   pipeline.filter((s) => s._daysUntil >= 0 && s._daysUntil <= 7),
    soon30:  pipeline.filter((s) => s._daysUntil >= 0 && s._daysUntil <= 30),
  }), [pipeline])

  // The All view shows every fetched row — Completed and no-due-date included — urgency first.
  const allRows = useMemo(
    () => items.slice().sort((a, b) => (a._daysUntil ?? Number.POSITIVE_INFINITY) - (b._daysUntil ?? Number.POSITIVE_INFINITY)),
    [items],
  )

  const tableRows = useMemo<SaleRow[]>(() => {
    switch (dueFilter) {
      case 'overdue': return byBucket.overdue
      case '7d':      return byBucket.soon7
      case '30d':     return byBucket.soon30
      case 'all':     return allRows
    }
  }, [dueFilter, byBucket, allRows])

  const upNext = pipeline.slice(0, 5)
  const upNextValue = upNext.reduce((sum, s) => sum + s.amountPaid, 0)

  const syncMutation = useMutation({
    mutationFn: () => api.admin.syncOntraport(),
    onSettled: () => void invalidate(),
  })
  const sync = () => {
    void sileo.promise(syncMutation.mutateAsync(), {
      loading: { title: 'Syncing Ontraport sales…' },
      success: (r) => ({ title: r.message }),
      error: (e) => ({ title: 'Sync failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const processMutation = useMutation({
    mutationFn: () => api.admin.processEligibleOntraport(),
    onSettled: () => void invalidate(),
  })
  const processEligible = () => {
    void sileo.promise(processMutation.mutateAsync(), {
      loading: { title: 'Queueing eligible sales…' },
      success: (r) => ({ title: r.message }),
      error: (e) => ({ title: 'Failed to enqueue', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const columns = useMemo<DataTableColumn<SaleRow>[]>(() => [
    {
      id: 'business',
      accessorKey: 'businessName',
      header: 'Business · ABN · Contact',
      meta: { sticky: true, className: 'min-w-[15rem] max-w-[19rem]' },
      cell: ({ row }) => {
        const s = row.original
        return (
          <div>
            <div className="text-sm font-medium text-zinc-900 truncate">{s.businessName}</div>
            <div className="text-xxs font-mono tabular-nums text-zinc-500">{s.abn}</div>
            <div className="mt-0.5 text-xxs font-mono text-zinc-400 truncate">
              {s.contactName ? `${s.contactName} · ${s.email}` : s.email}
            </div>
          </div>
        )
      },
    },
    {
      id: 'renewalDueDate',
      accessorFn: (s) => s._daysUntil ?? Number.POSITIVE_INFINITY,
      header: 'Due',
      cell: ({ row }) => <DueCell s={row.original} />,
    },
    {
      accessorKey: 'amountPaid',
      header: 'Amount paid',
      meta: { className: 'text-right font-mono tabular-nums text-sm text-zinc-900', headerClassName: 'text-right' },
      cell: ({ row }) => fmtMoney2(row.original.amountPaid),
    },
    {
      accessorKey: 'status',
      header: 'Status',
      enableSorting: false,
      cell: ({ row }) => <SaleStatusCell s={row.original} onShowError={(title, body) => setErrorModal({ title, body })} />,
    },
    {
      id: 'syncedAt',
      accessorFn: (s) => new Date(s.syncedAt).getTime(),
      header: 'Synced',
      meta: { className: 'text-sm text-zinc-700 tabular-nums whitespace-nowrap' },
      cell: ({ row }) => relativeTime(row.original.syncedAt),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      meta: { className: 'text-right' },
      cell: ({ row }) => {
        const s = row.original
        if (!s.renewalRequestId) return <span className="text-xxs font-mono text-zinc-400">—</span>
        const isFailed = s.status === 'RenewalFailed'
        return (
          <Link
            to={`/admin/renewals/${s.renewalRequestId}`}
            className={`inline-flex items-center text-sm font-medium whitespace-nowrap ${isFailed ? 'text-red-700 hover:text-red-800' : 'text-brand-700 hover:text-brand-800'}`}
          >
            {isFailed ? 'Investigate →' : 'View renewal →'}
          </Link>
        )
      },
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="PIPELINE"
        title="Ontraport sales"
        subtitle="Sales paid in Ontraport — fired at ASIC when the due date enters the renewal window."
        right={
          <>
            <button
              onClick={sync}
              disabled={syncMutation.isPending}
              className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-zinc-900 text-white px-3 py-2 text-sm font-medium hover:bg-zinc-800 shadow-sm disabled:opacity-50 transition"
            >
              {syncMutation.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <RefreshCw className="h-4 w-4" />
              )}
              Sync now
            </button>
            <button
              onClick={processEligible}
              disabled={processMutation.isPending}
              className="inline-flex items-center whitespace-nowrap rounded-md bg-brand-600 text-white px-3 py-2 text-sm font-medium hover:bg-brand-700 shadow-sm disabled:opacity-50 transition"
            >
              {processMutation.isPending ? 'Queueing…' : 'Process eligible'}
            </button>
            {failedCount > 0 ? (
              <Button variant="outline" className="h-9 whitespace-nowrap border-amber-300 text-amber-800 hover:bg-amber-50" onClick={() => setRequeueOpen(true)}>
                <ListRestart className="h-4 w-4" />
                <span className="hidden sm:inline">Requeue failed</span>
                <span className="sm:hidden">Requeue</span>
              </Button>
            ) : null}
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      {/* Stats strip — counts come from the server and describe ALL sales, not the filter */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-4">
        <StatTile
          kicker="30D"
          label="Pipeline next 30d"
          value={fmtMoney0(stats.pipelineValueNext30d)}
          sub={`${data?.waitingCount ?? 0} waiting · ${data?.queuedCount ?? 0} queued`}
          tone="emerald"
        />
        <StatTile
          kicker="OVERDUE"
          label="Past their due date"
          value={byBucket.overdue.length.toLocaleString()}
          sub={byBucket.overdue.length > 0 ? `${fmtMoney0(byBucket.overdue.reduce((s, x) => s + x.amountPaid, 0))} unrecovered` : 'all on track'}
          tone={byBucket.overdue.length > 0 ? 'red' : 'zinc'}
        />
        <StatTile
          kicker="FAILED"
          label="Real failures"
          value={failedCount.toLocaleString()}
          sub={failedCount > 0 ? 'review the table' : 'all clear'}
          tone={failedCount > 0 ? 'red' : 'zinc'}
        />
        <StatTile
          kicker="BLOCKED"
          label="Transient / skipped"
          value={blockedCount.toLocaleString()}
          sub={blockedCount > 0
            ? `${asicNotYetDueCount} not yet due · ${renewalInProgressCount} in progress · ${ineligibleCount} ineligible`
            : 'none'}
          tone={blockedCount > 0 ? 'amber' : 'zinc'}
        />
        <SparklineTile
          kicker="SYNC · 14D"
          label={stats.lastSyncAt ? relativeTime(stats.lastSyncAt) : 'No sync yet'}
          value={stats.today.toLocaleString()}
          sub={stats.nextSyncAt ? `next at ${new Date(stats.nextSyncAt).toLocaleString(undefined, { hour: '2-digit', minute: '2-digit' })} · ${stats.yesterday.toLocaleString()} yesterday` : `${stats.yesterday.toLocaleString()} yesterday`}
          deltaPct={stats.deltaPct}
          data={stats.daily14d.map((d) => d.count)}
        />
      </div>

      {/* Up next hero */}
      {upNext.length > 0 ? (
        <div className="mt-8 rounded-2xl bg-zinc-950 ring-1 ring-white/5 px-6 py-6 sm:px-8 sm:py-8 relative overflow-hidden">
          <div className="absolute inset-0 pointer-events-none" aria-hidden="true">
            <div className="absolute -right-24 -top-24 h-64 w-64 rounded-full bg-brand-500/15 blur-3xl" />
            <div className="absolute -left-24 -bottom-24 h-64 w-64 rounded-full bg-brand-400/10 blur-3xl" />
          </div>
          <div className="relative">
            <div className="flex items-end justify-between gap-4 flex-wrap mb-5">
              <div>
                <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-400">UP NEXT</div>
                <h2 className="mt-1 text-xl font-semibold text-white tracking-tight">Closest renewals due</h2>
                <p className="mt-1 text-sm text-zinc-300">{upNext.length} sales · {fmtMoney0(upNextValue)} in pipeline</p>
              </div>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3">
              {upNext.map((s) => (
                <UpNextCard key={s.id} s={s} />
              ))}
            </div>
          </div>
        </div>
      ) : null}

      {/* Heading + due-date urgency chips (a client-side view over the server-filtered rows) */}
      <div className="mt-8 flex items-end justify-between gap-3 flex-wrap">
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">VIEW</div>
          <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">All pipeline</h2>
        </div>
        <div className="inline-flex rounded-md bg-white ring-1 ring-zinc-200 shadow-sm" role="group">
          <FilterTab active={dueFilter === 'overdue'} onClick={() => setDueFilter('overdue')} count={byBucket.overdue.length} tone="red">Overdue</FilterTab>
          <FilterTab active={dueFilter === '7d'}      onClick={() => setDueFilter('7d')}      count={byBucket.soon7.length}    tone="amber">Next 7d</FilterTab>
          <FilterTab active={dueFilter === '30d'}     onClick={() => setDueFilter('30d')}     count={byBucket.soon30.length}   tone="emerald">Next 30d</FilterTab>
          <FilterTab active={dueFilter === 'all'}     onClick={() => setDueFilter('all')}     count={items.length}             tone="zinc">All</FilterTab>
        </div>
      </div>

      {/* Filter toolbar — status facet is resolved server-side with live counts */}
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search business, contact, ABN, email…"
          className="h-9 w-64"
        />
        <FacetedFilter title="Status" options={statusOptions} selected={statuses} onChange={setStatuses} />
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" className="h-9" onClick={resetFilters}>
            Reset
            <X className="h-4 w-4" />
          </Button>
        ) : null}
      </div>

      <div className="mt-4">
        <DataTable columns={columns} data={tableRows} empty={<EmptyState title={emptyMessage(dueFilter, hasActiveFilters)} />} />
      </div>

      <RequeueFailedDialog open={requeueOpen} onClose={() => setRequeueOpen(false)} onDone={() => void invalidate()} />

      <ErrorModal
        open={errorModal !== null}
        title={errorModal?.title ?? 'Renewal error details'}
        message={errorModal?.body ?? ''}
        onClose={() => setErrorModal(null)}
      />
    </div>
  )
}

/* ─────── Requeue-failed dialog ─────── */

/**
 * Puts failed sales back into the daily processor's pool in controlled batches.
 * Opens straight into a dry-run preview — nothing changes until "Requeue" runs.
 */
function RequeueFailedDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const [max, setMax] = useState(50)
  const [includePastDue, setIncludePastDue] = useState(false)
  const [errorInput, setErrorInput] = useState('')
  const errorContains = useDebouncedValue(errorInput, 300)

  const { data: preview, isFetching } = useQuery({
    queryKey: ['ontraport-requeue-preview', { max, includePastDue, errorContains }],
    queryFn: () => api.admin.requeueFailedOntraport({ dryRun: true, max, includePastDue, errorContains: errorContains || undefined }),
    enabled: open,
    staleTime: 0,
  })

  const liveMutation = useMutation({
    mutationFn: () => api.admin.requeueFailedOntraport({ dryRun: false, max, includePastDue, errorContains: errorContains || undefined }),
  })
  const runLive = () => {
    void sileo.promise(liveMutation.mutateAsync(), {
      loading: { title: 'Re-queuing failed sales…' },
      success: (r) => ({
        title: `Re-queued ${r.requeued} of ${r.totalMatching} failed sales`,
        description: 'They will process on the next 07:00 run, or immediately via Process eligible.',
      }),
      error: (e) => ({ title: 'Requeue failed', description: e instanceof Error ? e.message : undefined }),
    }).then(() => { onDone(); onClose() }).catch(() => {})
  }

  const nothingToDo = preview && preview.batchSize === 0

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o && !liveMutation.isPending) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-amber-700">RECOVERY</div>
          <DialogTitle>Requeue failed sales</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <p className="text-sm text-zinc-700">
            Puts failed sales back into the eligible pool in a controlled batch. Start small,
            watch the completion rate, then run again — sales past the renewal window are excluded.
          </p>

          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <div className="flex items-center gap-2">
              <Label htmlFor="requeue-max" className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">Batch size</Label>
              <Input
                id="requeue-max" type="number" min={1} max={500} value={max}
                onChange={(e) => setMax(Math.max(1, Math.min(500, Number(e.target.value) || 1)))}
                className="h-8 w-20 font-mono tabular-nums"
              />
            </div>
            <label className="flex items-center gap-2 text-xs text-zinc-600">
              <Checkbox checked={includePastDue} onCheckedChange={(v) => setIncludePastDue(v === true)} />
              include past-due
            </label>
          </div>
          <Input
            value={errorInput}
            onChange={(e) => setErrorInput(e.target.value)}
            placeholder="Only errors containing… (optional)"
            className="h-8 text-xs"
          />

          {isFetching && !preview ? (
            <div className="flex items-center gap-2 py-4 justify-center text-sm text-zinc-500">
              <Loader2 className="h-4 w-4 animate-spin" /> Running dry-run…
            </div>
          ) : preview ? (
            <>
              <div className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-3 py-2 text-xs font-mono text-zinc-700">
                <span className="text-zinc-900 font-semibold tabular-nums">{preview.batchSize}</span> of{' '}
                <span className="tabular-nums">{preview.totalMatching}</span> matching failed sales in this batch
              </div>
              {preview.items.length > 0 ? (
                <div className="max-h-36 overflow-y-auto rounded-md ring-1 ring-zinc-200 divide-y divide-zinc-100">
                  {preview.items.slice(0, 25).map((i) => (
                    <div key={i.id} className="flex items-center justify-between gap-2 px-3 py-1.5 text-xs">
                      <span className="min-w-0 truncate text-zinc-700">{i.businessName} · {i.abn}</span>
                      <span className="shrink-0 font-mono tabular-nums text-zinc-400">{fmtMoney0(i.amountPaid)}</span>
                    </div>
                  ))}
                </div>
              ) : null}
            </>
          ) : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={liveMutation.isPending}>Close</Button>
          <Button onClick={runLive} disabled={liveMutation.isPending || !preview || nothingToDo} className="bg-amber-600 hover:bg-amber-700">
            {liveMutation.isPending ? 'Re-queuing…' : nothingToDo ? 'Nothing matches' : `Requeue ${preview?.batchSize ?? ''}`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function emptyMessage(f: DueFilter, hasFilters: boolean) {
  if (hasFilters) {
    return f === 'all'
      ? 'No Ontraport sales match the current filters.'
      : 'No matches due in this window — try the All tab or reset the filters.'
  }
  switch (f) {
    case 'overdue': return 'No overdue sales — everything\'s on schedule.'
    case '7d':      return 'Nothing due in the next 7 days.'
    case '30d':     return 'Nothing due in the next 30 days.'
    case 'all':     return 'No Ontraport sales in the pipeline.'
  }
}

function defaultStats(): Stats {
  return {
    pipelineValueNext30d: 0, lastSyncAt: null, nextSyncAt: null,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [],
  }
}

function FilterTab({ active, count, onClick, tone, children }: { active: boolean; count: number; onClick: () => void; tone: Tone; children: React.ReactNode }) {
  const dotMap = { emerald: 'bg-emerald-500', amber: 'bg-amber-500', red: 'bg-red-500', indigo: 'bg-indigo-500', zinc: 'bg-zinc-400' }
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-2 px-3 py-2 text-sm font-medium transition first:rounded-l-md last:rounded-r-md ${
        active ? 'bg-zinc-900 text-white' : 'text-zinc-700 hover:bg-zinc-50'
      }`}
    >
      <span className={`h-1.5 w-1.5 rounded-full ${dotMap[tone]}`} />
      <span>{children}</span>
      <span className={`text-xxs font-mono tabular-nums ${active ? 'text-white/70' : 'text-zinc-400'}`}>{count.toLocaleString()}</span>
    </button>
  )
}

function UpNextCard({ s }: { s: Sale & { _daysUntil: number } }) {
  const days = s._daysUntil
  const tone: Tone = days < 0 ? 'red' : days <= 7 ? 'amber' : 'emerald'
  const toneTextMap = { emerald: 'text-emerald-300', amber: 'text-amber-300', red: 'text-red-300', indigo: 'text-indigo-300', zinc: 'text-zinc-300' }
  const toneRingMap = { emerald: 'ring-emerald-400/30', amber: 'ring-amber-400/30', red: 'ring-red-400/30', indigo: 'ring-indigo-400/30', zinc: 'ring-zinc-400/30' }

  const inner = (
    <>
      <div className={`text-xxs font-mono font-medium uppercase tracking-[0.14em] ${toneTextMap[tone]} tabular-nums`}>
        {days < 0 ? `${Math.abs(days)}D OVERDUE` : days === 0 ? 'TODAY' : `${days}D`}
      </div>
      <div className="mt-1 text-sm font-medium text-white truncate" title={s.businessName}>{s.businessName}</div>
      <div className="text-xxs font-mono text-zinc-400 truncate">{s.contactName || s.email}</div>
      <div className="mt-2 flex items-center justify-between">
        <span className="text-xs font-mono tabular-nums text-zinc-300">{fmtMoney0(s.amountPaid)}</span>
        <OntraportStatusPillDark status={s.status} />
      </div>
    </>
  )

  // When a renewal exists, the whole card deep-links into it. Otherwise it's a static tile.
  if (s.renewalRequestId) {
    return (
      <Link
        to={`/admin/renewals/${s.renewalRequestId}`}
        className={`block rounded-xl bg-white/5 ring-1 ${toneRingMap[tone]} px-3 py-3 hover:bg-white/10 hover:ring-white/30 transition`}
      >
        {inner}
      </Link>
    )
  }
  return (
    <div className={`rounded-xl bg-white/5 ring-1 ${toneRingMap[tone]} px-3 py-3`}>
      {inner}
    </div>
  )
}

function DueCell({ s }: { s: SaleRow }) {
  if (!s.renewalDueDate || s._daysUntil === null) {
    return <span className="text-xxs font-mono text-zinc-400">—</span>
  }
  const days = s._daysUntil
  const daysClass = days < 0 ? 'text-red-700 font-medium' : days <= 7 ? 'text-amber-700 font-medium' : days <= 30 ? 'text-emerald-700 font-medium' : 'text-zinc-500'
  return (
    <div>
      <div className="text-sm text-zinc-700 tabular-nums whitespace-nowrap">{new Date(s.renewalDueDate).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })}</div>
      <div className={`text-xxs font-mono tabular-nums ${daysClass}`}>{days < 0 ? `${Math.abs(days)} days overdue` : days === 0 ? 'today' : `in ${days} days`}</div>
    </div>
  )
}

function SaleStatusCell({ s, onShowError }: { s: SaleRow; onShowError: (title: string, body: string) => void }) {
  const isFailed = s.status === 'RenewalFailed'
  const isBlocked = blockedStatuses.includes(s.status)

  // Compose the error body from either the OntraportSale.errorMessage or the
  // linked RenewalRequest's full failure context. Renewal step + message is the
  // most useful for debugging — show it first if available.
  const errorBody = (() => {
    const parts: string[] = []
    if (isBlocked) parts.push(retryBanner(s.status))
    if (s.renewalFailedAtStep) parts.push(`Failed at: ${s.renewalFailedAtStep}`)
    if (s.renewalErrorMessage) parts.push(s.renewalErrorMessage)
    if (s.errorMessage && !parts.some(p => p.includes(s.errorMessage!))) parts.push(s.errorMessage)
    return parts.join('\n\n') || 'No error details recorded.'
  })()

  const modalTitle = isFailed
    ? `Why ${s.businessName || 'this sale'} failed`
    : isBlocked
      ? `${s.businessName || 'This sale'} — ${prettyStatus(s.status).toLowerCase()}`
      : 'Sale details'

  return (
    <div>
      <div className="flex items-center gap-1.5">
        <OntraportStatusPill status={s.status} />
        {isFailed ? (
          <button
            type="button"
            onClick={() => onShowError(modalTitle, errorBody)}
            className="inline-flex items-center justify-center h-5 w-5 rounded-full bg-red-50 text-red-600 hover:bg-red-100 transition"
            title="Why did this fail?"
          >
            <CircleAlert className="h-3 w-3" />
          </button>
        ) : isBlocked ? (
          <button
            type="button"
            onClick={() => onShowError(modalTitle, errorBody)}
            className="inline-flex items-center justify-center h-5 w-5 rounded-full bg-amber-50 text-amber-600 hover:bg-amber-100 transition"
            title={retryBanner(s.status)}
          >
            <TriangleAlert className="h-3 w-3" />
          </button>
        ) : null}
      </div>
      {isFailed && s.renewalFailedAtStep ? (
        <div className="mt-1 text-xxs font-mono text-red-700">Failed: {s.renewalFailedAtStep}</div>
      ) : null}
      {isBlocked ? (
        <div className="mt-1 text-xxs font-mono text-amber-700">{retryHint(s.status)}</div>
      ) : null}
    </div>
  )
}

function prettyStatus(status: string): string {
  switch (status) {
    case 'IneligibleForRenewal': return 'Ineligible'
    case 'AsicNotYetDue':        return 'ASIC not yet due'
    case 'RenewalInProgress':    return 'Renewal in progress'
    default:                     return status
  }
}

function retryBanner(status: string): string {
  switch (status) {
    case 'AsicNotYetDue':
      return 'ASIC says this business name is not due for renewal yet. The next 07:00 process run will retry — no action needed.'
    case 'RenewalInProgress':
      return 'ASIC reports an existing renewal session for this ABN. Will be retried on the next 07:00 run once the prior session clears.'
    case 'IneligibleForRenewal':
      return 'Skipped — the customer paid a cancellation fee (below the renewal price), filed a dispute/refund, or the contact is flagged for cancellation in Ontraport. Will not be retried automatically.'
    default:
      return ''
  }
}

function retryHint(status: string): string {
  switch (status) {
    case 'AsicNotYetDue':     return 'Auto-retry: next 07:00 run'
    case 'RenewalInProgress': return 'Auto-retry: next 07:00 run'
    case 'IneligibleForRenewal': return 'Skipped — no retry'
    default:                  return ''
  }
}

function OntraportStatusPill({ status }: { status: string }) {
  switch (status) {
    case 'Synced':                   return <StatusPill tone="zinc">SYNCED</StatusPill>
    case 'WaitingForRenewalWindow':  return <StatusPill tone="amber">WAITING</StatusPill>
    case 'RenewalQueued':            return <StatusPill tone="indigo">QUEUED</StatusPill>
    case 'RenewalCompleted':         return <StatusPill tone="emerald">COMPLETED</StatusPill>
    case 'RenewalFailed':            return <StatusPill tone="red">FAILED</StatusPill>
    case 'NotDueForRenewal':         return <StatusPill tone="zinc">NOT DUE</StatusPill>
    case 'IneligibleForRenewal':     return <StatusPill tone="amber">INELIGIBLE</StatusPill>
    case 'AsicNotYetDue':            return <StatusPill tone="amber">ASIC NOT YET DUE</StatusPill>
    case 'RenewalInProgress':        return <StatusPill tone="amber">IN PROGRESS</StatusPill>
    default:                         return <StatusPill tone="zinc">{status.toUpperCase()}</StatusPill>
  }
}

// Dark-background variant for the Up Next hero card
function OntraportStatusPillDark({ status }: { status: string }) {
  const map: Record<string, [string, string]> = {
    Synced:                   ['SYNCED',    'bg-zinc-700/40 text-zinc-200 ring-zinc-500/30'],
    WaitingForRenewalWindow:  ['WAITING',   'bg-amber-500/20 text-amber-200 ring-amber-400/30'],
    RenewalQueued:            ['QUEUED',    'bg-indigo-500/20 text-indigo-200 ring-indigo-400/30'],
    RenewalCompleted:         ['COMPLETED', 'bg-emerald-500/20 text-emerald-200 ring-emerald-400/30'],
    RenewalFailed:            ['FAILED',    'bg-red-500/20 text-red-200 ring-red-400/30'],
    NotDueForRenewal:         ['NOT DUE',   'bg-zinc-700/40 text-zinc-300 ring-zinc-500/30'],
    IneligibleForRenewal:     ['INELIGIBLE','bg-amber-500/20 text-amber-200 ring-amber-400/30'],
    AsicNotYetDue:            ['ASIC WAIT', 'bg-amber-500/20 text-amber-200 ring-amber-400/30'],
    RenewalInProgress:        ['IN PROG',   'bg-amber-500/20 text-amber-200 ring-amber-400/30'],
  }
  const [label, cls] = map[status] ?? [status.toUpperCase(), 'bg-zinc-700/40 text-zinc-200 ring-zinc-500/30']
  return <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-xxs font-mono font-medium tracking-[0.12em] ring-1 ring-inset ${cls}`}>{label}</span>
}
