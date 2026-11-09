import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, TriangleAlert, Wrench, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { DateRangeFilter, ErrorModal, Pagination, useDebouncedValue } from './_components'
import { RenewalSentence, SentenceList } from './_sentences'
import { Cell, EmptyState, PageHeader, RefreshButton, Stat, StatStrip, ViewToggle } from './_ui'
import { renewalStatus, Status, statusLabel } from './_status'
import { durationShort, fmtDate, fmtMoney0, fmtMoney2, fmtTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { sample } from '@/components/skeleton-samples'
import { sampleRows, Skeletonizer } from '@/components/skeletonizer'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

type RenewalsResponse = Awaited<ReturnType<typeof api.admin.renewals>>
type Renewal = RenewalsResponse['items'][number]
type Stats = RenewalsResponse['stats']
type Facets = RenewalsResponse['facets']

type ViewMode = 'Sentences' | 'Table' | 'Cards'

const SAMPLE_STATUSES: Renewal['status'][] = ['Completed', 'Failed', 'Completed', 'Processing']
const SKELETON_ROWS = sampleRows<Renewal>(8, (i) => ({
  id: sample.id(i), abn: sample.abn(i), businessName: sample.business(i), renewalYears: i % 3 === 0 ? 3 : 1,
  amount: sample.money(i), status: SAMPLE_STATUSES[i % SAMPLE_STATUSES.length], source: 'Renewtron', paymentType: 'Stripe',
  initiatedAt: sample.daysAgo(i), completedAt: i % 2 === 0 ? sample.daysAgo(i) : null, email: sample.email(i),
  errorMessage: i % 4 === 1 ? sample.text(0) : null, errorCategory: i % 4 === 1 ? 'CardDeclined' : null, attemptCount: 1,
  initiatedByLabel: 'Customer', stripePaymentSucceeded: true, cardBrand: 'visa', cardLast4: '4242',
  lead: { id: sample.id(100 + i), fullName: sample.name(i), email: sample.email(i) }, timeInStatusHours: 2 + i,
}))
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
    case 'CardDeclined':      return 'Card declined'
    case 'Terminal':          return 'Bad data'
    default:                  return category
  }
}

export default function Renewals() {
  // Sentences read on any screen (phones included); the table is there for digging on a desktop.
  const [viewMode, setViewMode] = useState<ViewMode>('Sentences')
  const [page, setPage] = useState(1)

  // ?view=to-fix is the sidebar's "Renewals to fix" queue: the same list, narrowed on the server.
  const [searchParams] = useSearchParams()
  const toFix = searchParams.get('view') === 'to-fix'
  const [shownView, setShownView] = useState(toFix)
  if (shownView !== toFix) { setShownView(toFix); setPage(1) }

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
  const { data, isFetching, isPending, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-renewals', { toFix, abn, statuses, sources, categories, initiators, dateFrom, dateTo, page }],
    queryFn: () => api.admin.renewals({
      view: toFix ? 'toFix' : undefined,
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
  // Sentence and card views draw the sample renewals as a skeleton on the first load; the table does it itself.
  const shown = isPending ? SKELETON_ROWS : renewals
  const stats: Stats = data?.stats ?? (isPending ? SKELETON_STATS : defaultStats())
  const facets: Facets = data?.facets ?? { status: [], source: [], initiatedBy: [], errorCategory: [] }

  const hasActiveFilters = !!abnInput || statuses.length > 0 || sources.length > 0 || categories.length > 0 || initiators.length > 0 || !!dateFrom || !!dateTo
  const resetFilters = () => {
    setPage(1)
    setAbnInput(''); setStatuses([]); setSources([]); setCategories([]); setInitiators([]); setDateFrom(''); setDateTo('')
  }
  const withPageReset = <T,>(setter: (v: T) => void) => (v: T) => { setPage(1); setter(v) }

  const sourceLabels: Record<string, string> = { Renewtron: 'Direct', Ontraport: 'Ontraport', BulkUpload: 'Bulk upload' }
  const statusOptions: FacetOption[] = facets.status.map((f) => ({ value: f.value, label: statusLabel('renewal', f.value), count: f.count }))
  const sourceOptions: FacetOption[] = facets.source.map((f) => ({ value: f.value, label: sourceLabels[f.value] ?? f.value, count: f.count }))
  const categoryOptions: FacetOption[] = facets.errorCategory.map((f) => ({ value: f.value, label: categoryLabel(f.value), count: f.count }))
  const initiatorOptions: FacetOption[] = facets.initiatedBy.map((f) => ({ value: f.value, label: f.value, count: f.count }))

  const columns = useMemo<DataTableColumn<Renewal>[]>(() => [
    {
      id: 'business',
      accessorKey: 'businessName',
      header: 'Business',
      // Compact on phones: name only, with the pinned column capped narrow so the
      // scrolling columns stay usable. Full ABN + customer detail returns at sm.
      meta: { sticky: true, className: 'min-w-28 max-w-[38vw] sm:min-w-60 sm:max-w-76' },
      cell: ({ row }) => {
        const r = row.original
        return (
          <div>
            <div className="truncate font-medium">{r.businessName}</div>
            <div className="hidden truncate text-caption text-muted-foreground sm:block">
              <span className="font-mono tabular-nums">{r.abn}</span>
              {' · '}
              {r.lead ? (
                <Link to={`/admin/leads/${r.lead.id}`} className="hover:underline" title={r.lead.email}>{r.lead.fullName}</Link>
              ) : (r.email ?? 'no lead')}
            </div>
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
      meta: { className: 'text-right font-mono tabular-nums', headerClassName: 'text-right' },
      cell: ({ row }) => fmtMoney2(row.original.amount),
    },
    {
      accessorKey: 'renewalYears',
      header: 'Years',
      meta: { className: 'tabular-nums', hideBelow: 'md' },
    },
    {
      id: 'sourcePayment',
      header: 'Source and payment',
      enableSorting: false,
      meta: { hideBelow: 'md' },
      cell: ({ row }) => (
        <div>
          <div className="flex flex-wrap items-center gap-1">
            <SourcePill source={row.original.source} />
            {row.original.isTest ? <Badge variant="outline">Test</Badge> : null}
            <BpayPill r={row.original} />
          </div>
          <div className="mt-1"><PaymentPill r={row.original} /></div>
        </div>
      ),
    },
    {
      accessorKey: 'timeInStatusHours',
      header: 'Time in status',
      meta: { hideBelow: 'sm' },
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
          <div className="flex items-center justify-end gap-1.5 whitespace-nowrap">
            {canRetry(r) ? (
              <Button
                size="sm"
                variant="outline"
                onClick={() => retryOne(r.id)}
                disabled={retryOneMutation.isPending && retryOneMutation.variables === r.id}
              >
                {retryOneMutation.isPending && retryOneMutation.variables === r.id ? 'Retrying…' : 'Retry'}
              </Button>
            ) : null}
            <Button asChild size="sm" variant="ghost"><Link to={`/admin/renewals/${r.id}`}>Open</Link></Button>
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [retryOneMutation.isPending, retryOneMutation.variables])

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <PageHeader
        title={toFix ? 'Renewals to fix' : 'Renewals'}
        subtitle={toFix ? 'Failed, and nothing will retry them on their own.' : 'Every renewal, from every channel.'}
        right={
          <>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setReconcileOpen(true)}
              title="Find renewals whose record and background job disagree"
            >
              <Wrench />
              Reconcile
            </Button>
            {(data?.stats.failedValue30d ?? 0) > 0 ? (
              <Button size="sm" onClick={() => setBulkRetryConfirm(true)} title="Re-queue every failed renewal in the current filter">
                Retry all failed
                <span className="font-normal tabular-nums opacity-70">{fmtMoney0(stats.failedValue30d)}</span>
              </Button>
            ) : null}
          </>
        }
      />

      <Skeletonizer loading={isPending}><StatsStrip stats={stats} /></Skeletonizer>

      {/* Filters, resolved on the server with live counts; the view and refresh sit at the end. */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={abnInput}
          onChange={(e) => { setPage(1); setAbnInput(e.target.value) }}
          placeholder="Filter ABN…"
          className="h-8 w-full font-mono tabular-nums placeholder:font-sans sm:w-40"
        />
        <FacetedFilter title="Status" options={statusOptions} selected={statuses} onChange={withPageReset(setStatuses)} />
        <FacetedFilter title="Source" options={sourceOptions} selected={sources} onChange={withPageReset(setSources)} />
        <FacetedFilter title="Category" options={categoryOptions} selected={categories} onChange={withPageReset(setCategories)} />
        <FacetedFilter title="Started by" options={initiatorOptions} selected={initiators} onChange={withPageReset(setInitiators)} />
        <DateRangeFilter dateFrom={dateFrom} dateTo={dateTo} onFrom={withPageReset(setDateFrom)} onTo={withPageReset(setDateTo)} />
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" onClick={resetFilters}>
            Reset
            <X />
          </Button>
        ) : null}
        <div className="flex w-full items-center justify-end gap-2 sm:ml-auto sm:w-auto">
          <ViewToggle<ViewMode> value={viewMode} options={[{ value: 'Sentences', label: 'Sentence view' }, { value: 'Table', label: 'Table view' }, { value: 'Cards', label: 'Cards view' }]} onChange={setViewMode} />
          <RefreshButton onClick={() => void refetch()} busy={isFetching} />
        </div>
      </div>

      <div className="mt-4">
        {viewMode === 'Sentences' ? (
          <Skeletonizer loading={isPending}>
          <SentenceList empty={<EmptyState title="Nothing matches these filters" />}>
            {shown.map((r) => (
              <RenewalSentence key={r.id} r={r} actions={canRetry(r) ? (
                <Button size="sm" variant="outline" disabled={retryOneMutation.isPending && retryOneMutation.variables === r.id} onClick={() => retryOne(r.id)}>Retry</Button>
              ) : null} />
            ))}
          </SentenceList>
          </Skeletonizer>
        ) : viewMode === 'Cards' ? (
          shown.length === 0 ? (
            <EmptyState title="Nothing matches these filters" />
          ) : (
            <Skeletonizer loading={isPending}>
            <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
              {shown.map((r) => (
                <RenewalCard key={r.id} r={r} onError={setErrorModal} onRetry={retryOne} retrying={retryOneMutation.isPending && retryOneMutation.variables === r.id} />
              ))}
            </div>
            </Skeletonizer>
          )
        ) : (
          <DataTable columns={columns} data={renewals} loading={isPending || (isFetching && isPlaceholderData)} skeleton={SKELETON_ROWS} empty={<EmptyState title="Nothing matches these filters" />} />
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
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Reconcile renewals</DialogTitle>
          <DialogDescription>
            Safe renewals are re-queued; any that may already hold an ASIC payment are flagged for you.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-3">
          {isFetching && !preview ? (
            <div className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground">
              <Loader2 className="size-4 animate-spin" /> Checking what would change…
            </div>
          ) : preview ? (
            <>
              <dl className="grid grid-cols-2 gap-x-4 gap-y-3 rounded-lg border p-3 sm:grid-cols-3">
                <Cell label="Checked" value={preview.scanned.toLocaleString()} />
                <Cell label="Re-queue" value={preview.requeued.toLocaleString()} />
                <Cell label="To verify" value={preview.needsVerification.toLocaleString()} />
                <Cell label="Stale" value={preview.markedStale.toLocaleString()} />
                <Cell label="Over the cap" value={preview.requeueCapped.toLocaleString()} />
                <Cell label="Job running" value={preview.skippedLiveJob.toLocaleString()} />
              </dl>
              {preview.items.length > 0 ? (
                <div className="max-h-40 divide-y overflow-y-auto rounded-lg border">
                  {preview.items.slice(0, 25).map((i) => (
                    <div key={i.renewalId} className="flex flex-col gap-0.5 px-3 py-1.5 text-caption sm:flex-row sm:items-center sm:justify-between sm:gap-2">
                      <span className="min-w-0 truncate">{i.businessName ?? i.abn ?? i.renewalId}</span>
                      <span className="text-muted-foreground tabular-nums sm:shrink-0">{fmtMoney0(i.amount)} · {i.ageHours}h · {i.action}</span>
                    </div>
                  ))}
                </div>
              ) : null}
              <div className="flex flex-wrap items-center gap-2" title="Keeps the ASIC queue manageable">
                <Label htmlFor="reconcile-cap" className="text-caption font-normal text-muted-foreground">Re-queue at most</Label>
                <Input
                  id="reconcile-cap"
                  type="number" min={0} max={500} value={maxRequeue}
                  onChange={(e) => setMaxRequeue(Math.max(0, Math.min(500, Number(e.target.value) || 0)))}
                  className="h-8 w-20 tabular-nums"
                />
                <span className="text-caption text-muted-foreground">per run</span>
              </div>
            </>
          ) : null}
        </DialogBody>
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

const SKELETON_STATS: Stats = {
  successRate30d: 94, completed30d: 1204, decided30d: 1281, total30d: 1320, avgCompletionMinutes: 4,
  revenueMtd: 84210, liveValue: 2380, stuckValue: 598, stuckCount: 3, scheduledRetryValue: 897,
  needsReviewCount: 4, needsReviewValue: 1196, failedValue30d: 4120,
  today: 61, yesterday: 58, deltaPct: 5, daily14d: [], errorCategoryBreakdown: [],
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
  const attention = stats.stuckValue + stats.needsReviewValue
  return (
    <StatStrip>
      <Stat
        label="Success rate"
        period="Last 30 days"
        value={`${stats.successRate30d}%`}
        sub={`${stats.completed30d.toLocaleString()} of ${stats.decided30d.toLocaleString()} finished`}
        dot={stats.decided30d === 0 ? null : stats.successRate30d < 70 ? 'action' : stats.successRate30d < 90 ? 'waiting' : null}
      />
      <Stat
        label="Revenue"
        period="This month"
        value={fmtMoney0(stats.revenueMtd)}
        sub={stats.avgCompletionMinutes != null ? `${durationShort(Number(stats.avgCompletionMinutes) / 60)} to renew, on average` : undefined}
      />
      <Stat
        label="Needs attention"
        value={fmtMoney0(attention)}
        sub={`${fmtMoney0(stats.liveValue)} under way · ${fmtMoney0(stats.scheduledRetryValue)} retrying`}
        dot={attention > 0 ? 'waiting' : null}
        hint="Stuck or failed renewals waiting on a person"
      />
      <Stat
        label="Today"
        period={stats.deltaPct != null ? `${stats.deltaPct >= 0 ? '+' : ''}${stats.deltaPct}%` : undefined}
        value={stats.today.toLocaleString()}
        sub={`${stats.yesterday.toLocaleString()} yesterday`}
        trend={stats.daily14d.map((d) => ({ date: d.date, count: d.count }))}
        hint="Renewals started today; the line is the last 14 days"
      />
    </StatStrip>
  )
}

/* ─────── Cards view ─────── */

function RenewalCard({ r, onError, onRetry, retrying }: { r: Renewal; onError: (m: string) => void; onRetry: (id: string) => void; retrying: boolean }) {
  return (
    <Card className="gap-4 p-4 sm:p-5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="truncate font-medium">{r.businessName}</div>
          <div className="truncate text-caption text-muted-foreground">
            <span className="font-mono tabular-nums">{r.abn}</span>
            {r.lead ? <> · {r.lead.fullName}</> : null}
          </div>
        </div>
        <StatusCell r={r} onError={onError} />
      </div>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-3">
        <Cell label="Amount" mono value={fmtMoney2(r.amount)} />
        <Cell label="Years" value={r.renewalYears} />
        <Cell label="Source" value={<div className="flex flex-wrap gap-1"><SourcePill source={r.source} /><BpayPill r={r} /></div>} />
        <Cell label="Payment" value={<PaymentPill r={r} />} />
        <Cell label="Time in status" value={<TimeInStatus r={r} />} />
        <Cell label="Started" value={`${fmtDate(r.initiatedAt)} · ${fmtTime(r.initiatedAt)}`} />
      </dl>
      <div className="flex items-center justify-end gap-1.5 border-t pt-4">
        {canRetry(r) ? (
          <Button size="sm" variant="outline" onClick={() => onRetry(r.id)} disabled={retrying}>
            {retrying ? 'Retrying…' : 'Retry'}
          </Button>
        ) : null}
        <Button asChild size="sm" variant="ghost"><Link to={`/admin/renewals/${r.id}`}>Open</Link></Button>
      </div>
    </Card>
  )
}

/* ─────── Cell components ─────── */

function StatusCell({ r, onError }: { r: Renewal; onError: (m: string) => void }) {
  return (
    <div className="flex shrink-0 items-center gap-1">
      <Status entry={renewalStatus(r)} />
      {r.status === 'Failed' && r.errorMessage ? (
        <Button variant="ghost" size="icon-sm" className="sm:size-6" onClick={() => onError(r.errorMessage ?? '')} title="See the error" aria-label="See the error">
          <TriangleAlert className="text-destructive" />
        </Button>
      ) : null}
    </div>
  )
}

const SOURCE_WORDS: Record<string, string> = { Renewtron: 'Direct', Ontraport: 'Ontraport', BulkUpload: 'Bulk upload' }

function SourcePill({ source }: { source: string }) {
  return <Badge variant="outline">{SOURCE_WORDS[source] ?? source}</Badge>
}

/** ASIC's BPAY bill for the renewal, when there is one. */
function BpayPill({ r }: { r: Renewal }) {
  if (!r.bpayReference) return null
  return r.bpayPaidAt
    ? <Status entry={{ label: 'BPAY paid', meaning: 'done' }} />
    : <Status entry={{ label: 'BPAY to pay', meaning: 'action', hint: 'ASIC holds the renewal until it’s paid.' }} />
}

function PaymentPill({ r }: { r: Renewal }) {
  if (r.paymentType === 'External') return <span className="text-caption text-muted-foreground">Paid elsewhere</span>
  if (!r.stripePaymentSucceeded) return <span className="text-caption text-muted-foreground">Card not charged</span>
  const brand = r.cardBrand ? r.cardBrand.charAt(0).toUpperCase() + r.cardBrand.slice(1) : 'Card'
  return <span className="text-caption text-muted-foreground">{brand}{r.cardLast4 ? <> ending <span className="font-mono tabular-nums">{r.cardLast4}</span></> : null}</span>
}

function TimeInStatus({ r }: { r: Renewal }) {
  const isInflight = r.status === 'Pending' || r.status === 'Processing'
  const slow = isInflight && r.timeInStatusHours > 2
  const facts = [
    !isInflight && r.completedAt ? `closed ${fmtDate(r.completedAt)}` : null,
    r.status === 'Failed' && r.nextRetryAt ? `retries ${fmtDate(r.nextRetryAt)} ${fmtTime(r.nextRetryAt)}` : null,
    r.attemptCount > 1 ? `attempt ${r.attemptCount}` : null,
  ].filter(Boolean)
  return (
    <div>
      <div className={cn('flex items-center gap-1.5 tabular-nums', slow && 'font-medium')} title={slow ? 'In flight for over 2 hours' : undefined}>
        {slow ? <span aria-hidden className="size-2 rounded-full bg-warning" /> : null}
        {durationShort(r.timeInStatusHours)}{isInflight ? ' in flight' : ''}
      </div>
      {facts.length ? <div className="text-caption text-muted-foreground tabular-nums">{facts.join(' · ')}</div> : null}
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
      <DialogContent className="max-h-[85dvh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Retry all failed renewals?</DialogTitle>
          <DialogDescription>
            Every failed renewal in the current filter goes back in the queue. Unpaid ones, and any ASIC may already
            have taken payment for, are skipped.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-3">
          <dl className="grid grid-cols-2 gap-x-4 gap-y-3 rounded-lg border p-3">
            <Cell label="Failed, last 30 days" value={fmtMoney0(stats.failedValue30d)} />
            {source ? <Cell label="Source" value={SOURCE_WORDS[source] ?? source} /> : null}
            {dateFrom ? <Cell label="From" value={dateFrom} /> : null}
            {dateTo ? <Cell label="To" value={dateTo} /> : null}
          </dl>
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy}>Cancel</Button>
          <Button onClick={onConfirm} disabled={busy}>
            {busy ? 'Retrying…' : 'Retry all'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
