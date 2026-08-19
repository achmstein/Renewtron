import { useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarRange, Loader2, RefreshCw, Wrench, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { ErrorModal, Pagination, useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, ViewToggle } from './_ui'
import { durationShort, fmtDate, fmtMoney0, fmtMoney2, fmtTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'

type RenewalsResponse = Awaited<ReturnType<typeof api.admin.renewals>>
type Renewal = RenewalsResponse['items'][number]
type Stats = RenewalsResponse['stats']
type Facets = RenewalsResponse['facets']

type ViewMode = 'Table' | 'Cards'
const PAGE_SIZE = 10

function canRetry(r: Renewal) {
  return r.status === 'Failed' && ((r.paymentType === 'Stripe' && r.stripePaymentSucceeded) || r.paymentType === 'External')
}

/** Operator-friendly names for RenewalErrorCategories values. */
function categoryLabel(category: string): string {
  switch (category) {
    case 'Transient':         return 'Network/ASIC'
    case 'NotDueYet':         return 'Not due yet'
    case 'AlreadyInProgress': return 'In progress'
    case 'PaymentRisk':       return 'Verify payment'
    case 'Terminal':          return 'Bad data'
    default:                  return category
  }
}

export default function Renewals() {
  // Cards are the usable default on phones; the wide table needs a desktop viewport.
  const [viewMode, setViewMode] = useState<ViewMode>(() =>
    window.matchMedia('(min-width: 1024px)').matches ? 'Table' : 'Cards')
  const [page, setPage] = useState(1)

  const [abnInput, setAbnInput] = useState('')
  const abn = useDebouncedValue(abnInput, 300)
  const [statuses, setStatuses] = useState<string[]>([])
  const [sources, setSources] = useState<string[]>([])
  const [categories, setCategories] = useState<string[]>([])
  const [initiators, setInitiators] = useState<string[]>([])
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')

  const [bulkRetryConfirm, setBulkRetryConfirm] = useState(false)
  const [reconcileOpen, setReconcileOpen] = useState(false)
  const [errorModal, setErrorModal] = useState<string | null>(null)

  const queryClient = useQueryClient()
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['admin-renewals', { abn, statuses, sources, categories, initiators, dateFrom, dateTo, page }],
    queryFn: () => api.admin.renewals({
      abn: abn || undefined,
      status: statuses.join(',') || undefined,
      source: sources.join(',') || undefined,
      errorCategory: categories.join(',') || undefined,
      initiatedBy: initiators.join(',') || undefined,
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
    placeholderData: keepPreviousData,
    // Auto-refresh while there are Processing/Pending renewals in view
    refetchInterval: (query) =>
      query.state.data?.items.some((r) => r.status === 'Processing' || r.status === 'Pending') ? 5000 : false,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-renewals'] })

  const retryOneMutation = useMutation({
    mutationFn: (id: string) => api.admin.retryRenewal(id),
    onSettled: () => void invalidate(),
  })
  const retryOne = (id: string) => {
    void sileo.promise(retryOneMutation.mutateAsync(id), {
      loading: { title: 'Retrying renewal…' },
      success: (r) => ({ title: r.message ?? 'Renewal queued for retry.' }),
      error: (e) => ({ title: 'Retry failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const bulkRetryMutation = useMutation({
    mutationFn: () => api.admin.retryRenewalsBulk({
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
      source: sources.length === 1 ? sources[0] : undefined,
    }),
    onSuccess: () => setBulkRetryConfirm(false),
    onSettled: () => void invalidate(),
  })
  const retryAllFailed = () => {
    void sileo.promise(bulkRetryMutation.mutateAsync(), {
      loading: { title: 'Queuing failed renewals…' },
      success: (r) => ({
        title: `Queued ${r.retried} for retry`,
        description: [
          r.skipped > 0 ? `${r.skipped} skipped — Stripe payment didn't succeed.` : null,
          r.skippedPaymentRisk > 0 ? `${r.skippedPaymentRisk} held back — ASIC may already hold payment.` : null,
        ].filter(Boolean).join(' ') || undefined,
      }),
      error: (e) => ({ title: 'Bulk retry failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const totalCount = data?.totalCount ?? 0
  const renewals = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const facets: Facets = data?.facets ?? { status: [], source: [], initiatedBy: [], errorCategory: [] }

  const hasActiveFilters = !!abnInput || statuses.length > 0 || sources.length > 0 || categories.length > 0 || initiators.length > 0 || !!dateFrom || !!dateTo
  const resetFilters = () => {
    setPage(1)
    setAbnInput(''); setStatuses([]); setSources([]); setCategories([]); setInitiators([]); setDateFrom(''); setDateTo('')
  }
  const withPageReset = <T,>(setter: (v: T) => void) => (v: T) => { setPage(1); setter(v) }

  const sourceLabels: Record<string, string> = { Renewtron: 'Direct', Ontraport: 'Ontraport', BulkUpload: 'Bulk upload' }
  const statusOptions: FacetOption[] = facets.status.map((f) => ({ value: f.value, label: f.value, count: f.count }))
  const sourceOptions: FacetOption[] = facets.source.map((f) => ({ value: f.value, label: sourceLabels[f.value] ?? f.value, count: f.count }))
  const categoryOptions: FacetOption[] = facets.errorCategory.map((f) => ({ value: f.value, label: categoryLabel(f.value), count: f.count }))
  const initiatorOptions: FacetOption[] = facets.initiatedBy.map((f) => ({ value: f.value, label: f.value, count: f.count }))

  const columns = useMemo<DataTableColumn<Renewal>[]>(() => [
    {
      id: 'business',
      accessorKey: 'businessName',
      header: 'Business · ABN · Customer',
      meta: { sticky: true, className: 'min-w-[15rem] max-w-[19rem]' },
      cell: ({ row }) => {
        const r = row.original
        return (
          <div>
            <div className="text-sm font-medium text-zinc-900 truncate">{r.businessName}</div>
            <div className="text-xxs font-mono tabular-nums text-zinc-500">{r.abn}</div>
            {r.lead ? (
              <Link to={`/admin/leads/${r.lead.id}`} className="block mt-0.5 text-xxs font-mono text-zinc-400 hover:underline truncate">{r.lead.fullName} · {r.lead.email}</Link>
            ) : (
              <div className="text-xxs font-mono text-zinc-400 truncate">{r.email ?? 'no lead'}</div>
            )}
          </div>
        )
      },
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ row }) => <StatusCell r={row.original} onError={setErrorModal} />,
    },
    {
      accessorKey: 'amount',
      header: 'Amount',
      meta: { className: 'text-right font-mono tabular-nums text-sm text-zinc-900', headerClassName: 'text-right' },
      cell: ({ row }) => fmtMoney2(row.original.amount),
    },
    {
      accessorKey: 'renewalYears',
      header: 'Years',
      meta: { className: 'text-sm tabular-nums text-zinc-700' },
    },
    {
      id: 'sourcePayment',
      header: 'Source · Payment',
      enableSorting: false,
      cell: ({ row }) => (
        <div>
          <SourcePill source={row.original.source} />
          <div className="mt-1"><PaymentPill r={row.original} /></div>
        </div>
      ),
    },
    {
      accessorKey: 'timeInStatusHours',
      header: 'Time in status',
      cell: ({ row }) => <TimeInStatus r={row.original} />,
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      meta: { className: 'text-right' },
      cell: ({ row }) => {
        const r = row.original
        return (
          <div className="flex items-center justify-end gap-3">
            {canRetry(r) ? (
              <button
                onClick={() => retryOne(r.id)}
                disabled={retryOneMutation.isPending && retryOneMutation.variables === r.id}
                className="text-sm font-medium text-amber-700 hover:text-amber-800 disabled:opacity-50"
              >
                {retryOneMutation.isPending && retryOneMutation.variables === r.id ? 'Retrying…' : 'Retry'}
              </button>
            ) : null}
            <Link to={`/admin/renewals/${r.id}`} className="text-sm font-medium text-brand-700 hover:text-brand-800 whitespace-nowrap">View →</Link>
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [retryOneMutation.isPending, retryOneMutation.variables])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Renewal requests"
        subtitle="All renewals across origins — customer checkout, admin, Ontraport, bulk."
        right={
          <>
            {stats.failedValue30d > 0 ? (
              <Button onClick={() => setBulkRetryConfirm(true)} className="h-9 bg-amber-600 hover:bg-amber-700 whitespace-nowrap">
                <RefreshCw className="h-4 w-4" />
                <span className="hidden sm:inline">Retry all failed</span>
                <span className="sm:hidden">Retry failed</span>
                <span className="ml-0.5 inline-flex items-center justify-center rounded bg-white/20 text-white text-xxs font-mono tabular-nums px-1.5 py-0.5 leading-none">{fmtMoney0(stats.failedValue30d)}</span>
              </Button>
            ) : null}
            <Button variant="outline" className="h-9 whitespace-nowrap" onClick={() => setReconcileOpen(true)}>
              <Wrench className="h-4 w-4" />
              <span className="hidden sm:inline">Reconcile</span>
            </Button>
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
        <FacetedFilter title="Status" options={statusOptions} selected={statuses} onChange={withPageReset(setStatuses)} />
        <FacetedFilter title="Source" options={sourceOptions} selected={sources} onChange={withPageReset(setSources)} />
        <FacetedFilter title="Category" options={categoryOptions} selected={categories} onChange={withPageReset(setCategories)} />
        <FacetedFilter title="Initiated by" options={initiatorOptions} selected={initiators} onChange={withPageReset(setInitiators)} />
        <DateRangeFilter dateFrom={dateFrom} dateTo={dateTo} onFrom={withPageReset(setDateFrom)} onTo={withPageReset(setDateTo)} />
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" className="h-9" onClick={resetFilters}>
            Reset
            <X className="h-4 w-4" />
          </Button>
        ) : null}
      </div>

      <div className="mt-4">
        {viewMode === 'Cards' ? (
          renewals.length === 0 ? (
            <EmptyState title="No renewals match the current filters." />
          ) : (
            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              {renewals.map((r) => (
                <RenewalCard key={r.id} r={r} onError={setErrorModal} onRetry={retryOne} retrying={retryOneMutation.isPending && retryOneMutation.variables === r.id} />
              ))}
            </div>
          )
        ) : (
          <DataTable columns={columns} data={renewals} empty={<EmptyState title="No renewals match the current filters." />} />
        )}

        <Pagination page={page} pageSize={PAGE_SIZE} total={totalCount} onPage={setPage} />
      </div>

      {bulkRetryConfirm ? (
        <BulkRetryModal
          stats={stats}
          dateFrom={dateFrom}
          dateTo={dateTo}
          source={sources.length === 1 ? sources[0] : ''}
          busy={bulkRetryMutation.isPending}
          onClose={() => setBulkRetryConfirm(false)}
          onConfirm={retryAllFailed}
        />
      ) : null}

      <ReconcileDialog open={reconcileOpen} onClose={() => setReconcileOpen(false)} onDone={() => void invalidate()} />

      <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Renewal error details" />
    </div>
  )
}

/* ─────── Reconcile dialog ─────── */

/**
 * Runs the queue-vs-DB reconciliation. Opens straight into a dry-run preview —
 * nothing changes until the operator explicitly runs it live.
 */
function ReconcileDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const [maxRequeue, setMaxRequeue] = useState(25)
  const { data: preview, isFetching } = useQuery({
    queryKey: ['reconcile-preview', maxRequeue],
    queryFn: () => api.admin.reconcileRenewals({ dryRun: true, maxRequeue }),
    enabled: open,
    staleTime: 0,
  })

  const liveMutation = useMutation({
    mutationFn: () => api.admin.reconcileRenewals({ dryRun: false, maxRequeue }),
  })
  const runLive = () => {
    void sileo.promise(liveMutation.mutateAsync(), {
      loading: { title: 'Reconciling…' },
      success: (r) => ({
        title: `Reconciled — ${r.requeued} re-queued`,
        description: [
          r.needsVerification > 0 ? `${r.needsVerification} flagged for payment verification` : null,
          r.markedStale > 0 ? `${r.markedStale} stale runs surfaced as failed` : null,
          r.requeueCapped > 0 ? `${r.requeueCapped} over the cap — run again to continue` : null,
        ].filter(Boolean).join(' · ') || 'Queue and database agree.',
      }),
      error: (e) => ({ title: 'Reconcile failed', description: e instanceof Error ? e.message : undefined }),
    }).then(() => { onDone(); onClose() }).catch(() => {})
  }

  const nothingToDo = preview && preview.scanned === 0

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o && !liveMutation.isPending) onClose() }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">RECOVERY</div>
          <DialogTitle>Reconcile queue &amp; database</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <p className="text-sm text-zinc-700">
            Finds renewals whose row and Hangfire job have diverged. Safe rows are re-queued;
            anything that may already hold an ASIC payment is flagged for you instead.
          </p>

          {isFetching && !preview ? (
            <div className="flex items-center gap-2 py-6 justify-center text-sm text-zinc-500">
              <Loader2 className="h-4 w-4 animate-spin" /> Running dry-run…
            </div>
          ) : preview ? (
            <>
              <div className="grid grid-cols-3 gap-2">
                <PreviewStat label="Scanned" value={preview.scanned} />
                <PreviewStat label="Re-queue" value={preview.requeued} tone={preview.requeued > 0 ? 'brand' : undefined} />
                <PreviewStat label="Verify" value={preview.needsVerification} tone={preview.needsVerification > 0 ? 'amber' : undefined} />
                <PreviewStat label="Stale" value={preview.markedStale} tone={preview.markedStale > 0 ? 'amber' : undefined} />
                <PreviewStat label="Capped" value={preview.requeueCapped} />
                <PreviewStat label="Live job" value={preview.skippedLiveJob} />
              </div>
              {preview.items.length > 0 ? (
                <div className="max-h-40 overflow-y-auto rounded-md ring-1 ring-zinc-200 divide-y divide-zinc-100">
                  {preview.items.slice(0, 25).map((i) => (
                    <div key={i.renewalId} className="flex items-center justify-between gap-2 px-3 py-1.5 text-xs">
                      <span className="min-w-0 truncate text-zinc-700">{i.businessName ?? i.abn ?? i.renewalId}</span>
                      <span className="shrink-0 font-mono tabular-nums text-zinc-400">{fmtMoney0(i.amount)} · {i.ageHours}h · {i.action}</span>
                    </div>
                  ))}
                </div>
              ) : null}
              <div className="flex items-center gap-2">
                <span className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">Re-queue cap</span>
                <Input
                  type="number" min={0} max={500} value={maxRequeue}
                  onChange={(e) => setMaxRequeue(Math.max(0, Math.min(500, Number(e.target.value) || 0)))}
                  className="h-8 w-24 font-mono tabular-nums"
                />
                <span className="text-xs text-zinc-500">per run — keeps the ASIC queue manageable</span>
              </div>
            </>
          ) : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={liveMutation.isPending}>Close</Button>
          <Button onClick={runLive} disabled={liveMutation.isPending || !preview || nothingToDo}>
            {liveMutation.isPending ? 'Running…' : nothingToDo ? 'Nothing to reconcile' : 'Run live'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function PreviewStat({ label, value, tone }: { label: string; value: number; tone?: 'brand' | 'amber' }) {
  const color = tone === 'brand' ? 'text-brand-700' : tone === 'amber' ? 'text-amber-700' : 'text-zinc-900'
  return (
    <div className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-2.5 py-1.5">
      <div className="text-xxs font-mono uppercase tracking-[0.12em] text-zinc-500">{label}</div>
      <div className={`text-base font-semibold tabular-nums ${color}`}>{value.toLocaleString()}</div>
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
    successRate30d: 0, completed30d: 0, decided30d: 0, total30d: 0, avgCompletionMinutes: null,
    revenueMtd: 0, liveValue: 0, stuckValue: 0, stuckCount: 0, scheduledRetryValue: 0,
    needsReviewCount: 0, needsReviewValue: 0, failedValue30d: 0,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [], errorCategoryBreakdown: [],
  }
}

function StatsStrip({ stats }: { stats: Stats }) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <StatTile
        kicker="30D"
        label="Success rate"
        value={`${stats.successRate30d}%`}
        sub={`${stats.completed30d.toLocaleString()} of ${stats.decided30d.toLocaleString()} decided`}
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
        kicker="PIPELINE"
        label="Needs attention"
        value={fmtMoney0(stats.stuckValue + stats.needsReviewValue)}
        sub={`${fmtMoney0(stats.liveValue)} live · ${fmtMoney0(stats.scheduledRetryValue)} auto-retry`}
        tone={stats.stuckValue + stats.needsReviewValue > 0 ? 'amber' : 'zinc'}
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

/* ─────── Cards view (mobile default) ─────── */

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
      {r.status === 'Failed' && r.nextRetryAt ? (
        <div className="text-xxs font-mono text-indigo-600 tabular-nums">auto-retry {fmtDate(r.nextRetryAt)} {fmtTime(r.nextRetryAt)}</div>
      ) : null}
      {r.attemptCount > 1 ? (
        <div className="text-xxs font-mono text-zinc-400 tabular-nums">attempt {r.attemptCount}</div>
      ) : null}
    </div>
  )
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
  return (
    <Dialog open onOpenChange={(o) => { if (!o && !busy) onClose() }}>
      <DialogContent>
        <DialogHeader>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-amber-700">RETRY</div>
          <DialogTitle>Retry all failed renewals</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <p className="text-sm text-zinc-700">
            This will re-queue every <span className="font-medium">Failed</span> renewal in your current filter.
            Stripe-unpaid renewals and rows where ASIC may already hold payment are skipped automatically.
          </p>
          <div className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-3 py-2 text-xs font-mono text-zinc-700 space-y-1">
            <div>Failed (30d): <span className="text-zinc-900 tabular-nums">{fmtMoney0(stats.failedValue30d)}</span></div>
            {dateFrom ? <div>From: <span className="text-zinc-900 tabular-nums">{dateFrom}</span></div> : null}
            {dateTo ? <div>To: <span className="text-zinc-900 tabular-nums">{dateTo}</span></div> : null}
            {source ? <div>Source: <span className="text-zinc-900">{source}</span></div> : null}
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy}>Cancel</Button>
          <Button onClick={onConfirm} disabled={busy} className="bg-amber-600 hover:bg-amber-700">
            {busy ? 'Retrying…' : 'Retry all'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
