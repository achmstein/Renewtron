import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleAlert, Info, KeyRound, ListRestart, Loader2, RefreshCw, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { ErrorModal, Pagination, useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, Stat, StatStrip, ViewToggle } from './_ui'
import { SaleSentence, SentenceList } from './_sentences'
import { HANDLED, Status } from './_status'
import { fmtMoney0, fmtMoney2, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { cn } from '@/lib/utils'

type SalesResponse = Awaited<ReturnType<typeof api.admin.ontraportSales>>
type Sale = SalesResponse['items'][number]
type Stats = SalesResponse['stats']
type Facets = SalesResponse['facets']

/** A sale annotated with days until its due date (null when no due date is known). */
type SaleRow = Sale & { _daysUntil: number | null }

type DueFilter = 'overdue' | '7d' | '30d' | 'all'

const PAGE_SIZE = 25

function withDaysUntil(s: Sale): SaleRow {
  return { ...s, _daysUntil: s.renewalDueDate ? Math.floor((new Date(s.renewalDueDate).getTime() - Date.now()) / 86400000) : null }
}

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
  // Links from Today and the client page arrive with ?search= or ?status=; those show every
  // due date, not just the next 30 days.
  const [searchParams] = useSearchParams()
  const linked = searchParams.has('search') || searchParams.has('status')
  const [page, setPage] = useState(1)
  const [dueFilter, setDueFilterRaw] = useState<DueFilter>(linked ? 'all' : '30d')
  const [searchInput, setSearchInputRaw] = useState(() => searchParams.get('search') ?? '')
  const search = useDebouncedValue(searchInput, 300)
  const [statuses, setStatusesRaw] = useState<string[]>(() => searchParams.get('status')?.split(',').filter(Boolean) ?? [])
  const setDueFilter = (v: DueFilter) => { setPage(1); setDueFilterRaw(v) }
  const setSearchInput = (v: string) => { setPage(1); setSearchInputRaw(v) }
  const setStatuses = (v: string[]) => { setPage(1); setStatusesRaw(v) }
  const [requeueOpen, setRequeueOpen] = useState(false)
  const [errorModal, setErrorModal] = useState<{ title: string; body: string } | null>(null)

  const queryClient = useQueryClient()
  const { data, isFetching, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-ontraport-sales', { statuses, search, dueFilter, page }],
    queryFn: () => api.admin.ontraportSales({
      status: statuses.join(',') || undefined,
      search: search || undefined,
      due: dueFilter,
      page,
      pageSize: PAGE_SIZE,
    }),
    placeholderData: keepPreviousData,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-ontraport-sales'] })

  // Puts the sale on the ASIC key to-do list; the form is filled in by hand from the
  // ASIC Key Requests page, where the reference number is recorded.
  const keyRequestMutation = useMutation({
    mutationFn: (saleId: string) => api.admin.requestAsicKeyForSale(saleId),
  })
  const requestKey = (saleId: string, businessName: string) => {
    void sileo.promise(keyRequestMutation.mutateAsync(saleId), {
      loading: { title: `Adding ${businessName} to the ASIC key list…` },
      success: () => ({ title: `${businessName} is on the ASIC key to-do list`, description: 'Fill it in from the ASIC Key Requests page.' }),
      error: (e) => ({ title: 'Could not queue the request', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

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

  // One page of the chosen due-date view, already filtered and sorted (most urgent first) by
  // the server, annotated with days until the due date.
  const tableRows = useMemo<SaleRow[]>(() => (data?.items ?? []).map(withDaysUntil), [data])
  const [viewMode, setViewMode] = useState<'Sentences' | 'Table'>('Sentences')
  const upNext = useMemo<SaleRow[]>(() => (data?.upNext ?? []).map(withDaysUntil), [data])
  const buckets = data?.buckets ?? { overdue: 0, overdueValue: 0, soon7: 0, soon30: 0, all: 0 }
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
      header: 'Business',
      // Compact on phones: business name only; ABN + contact return at sm.
      meta: { sticky: true, className: 'min-w-28 max-w-[38vw] sm:min-w-60 sm:max-w-76' },
      cell: ({ row }) => {
        const s = row.original
        return (
          <div>
            <Link to={`/admin/ontraport-sales/${s.id}`} className="block truncate font-medium hover:underline">{s.businessName}</Link>
            <div className="hidden truncate text-caption text-muted-foreground sm:block">
              <span className="font-mono tabular-nums">{s.abn}</span>
              {' · '}
              {s.clientId ? (
                <Link to={`/admin/clients/${s.clientId}`} className="text-link hover:underline" title={s.email}>
                  {s.contactName || s.email}
                </Link>
              ) : <span title={s.email}>{s.contactName || s.email}</span>}
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
      header: 'Paid',
      meta: { className: 'text-right font-mono tabular-nums', headerClassName: 'text-right' },
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
      meta: { className: 'tabular-nums whitespace-nowrap', hideBelow: 'md' },
      cell: ({ row }) => relativeTime(row.original.syncedAt),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      meta: { className: 'text-right' },
      cell: ({ row }) => {
        const s = row.original
        const isFailed = s.status === 'RenewalFailed'
        return (
          <div className="flex items-center justify-end gap-1.5 whitespace-nowrap">
            <Button
              variant="ghost" size="sm"
              disabled={keyRequestMutation.isPending}
              onClick={(e) => { e.stopPropagation(); requestKey(s.id, s.businessName) }}
              title="Add this business name to the ASIC key to-do list"
              aria-label="Request key"
            >
              <KeyRound /><span className="hidden sm:inline">Request key</span>
            </Button>
            {s.renewalRequestId ? (
              <Button asChild size="sm" variant="outline">
                <Link to={`/admin/renewals/${s.renewalRequestId}`}>{isFailed ? 'Investigate' : 'Open renewal'}</Link>
              </Button>
            ) : null}
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [keyRequestMutation.isPending])

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <PageHeader
        title="Ontraport sales"
        subtitle="Paid in Ontraport, renewed once the name is inside ASIC’s window."
        right={
          <>
            {failedCount > 0 ? (
              <Button variant="outline" size="sm" onClick={() => setRequeueOpen(true)} title="Put failed sales back in the pool, in batches">
                <ListRestart />
                Requeue failed
              </Button>
            ) : null}
            <Button variant="outline" size="sm" onClick={sync} disabled={syncMutation.isPending} title="Pull new sales from Ontraport now">
              {syncMutation.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}
              Sync now
            </Button>
            <Button size="sm" onClick={processEligible} disabled={processMutation.isPending} title="Start renewals for every sale inside ASIC’s window">
              {processMutation.isPending ? 'Queueing…' : 'Process eligible'}
            </Button>
          </>
        }
      />

      {/* Counts come from the server and describe all sales, not the filter. */}
      <StatStrip>
        <Stat
          label="Due in the next 30 days"
          value={fmtMoney0(stats.pipelineValueNext30d)}
          sub={`${data?.waitingCount ?? 0} waiting · ${data?.queuedCount ?? 0} queued`}
        />
        <Stat
          label="Overdue"
          value={buckets.overdue.toLocaleString()}
          sub={buckets.overdue > 0 ? `${fmtMoney0(buckets.overdueValue)} not renewed` : 'All on track'}
          dot={buckets.overdue > 0 ? 'action' : null}
        />
        <Stat
          label="Failed"
          value={failedCount.toLocaleString()}
          sub={blockedCount > 0 ? `${blockedCount.toLocaleString()} more waiting on ASIC or skipped` : failedCount > 0 ? 'Need a look' : 'All clear'}
          dot={failedCount > 0 ? 'action' : null}
          hint={blockedCount > 0 ? `${asicNotYetDueCount} not due at ASIC yet · ${renewalInProgressCount} open at ASIC · ${ineligibleCount} not eligible` : undefined}
        />
        <Stat
          label="Synced today"
          period={stats.lastSyncAt ? `Last ${relativeTime(stats.lastSyncAt)}` : 'No sync yet'}
          value={stats.today.toLocaleString()}
          sub={stats.nextSyncAt
            ? `${stats.yesterday.toLocaleString()} yesterday · next at ${new Date(stats.nextSyncAt).toLocaleString(undefined, { hour: '2-digit', minute: '2-digit' })}`
            : `${stats.yesterday.toLocaleString()} yesterday`}
          trend={stats.daily14d.map((d) => ({ count: d.count }))}
          trendLabel="sales"
          hint="Sales pulled from Ontraport; the line is the last 14 days"
        />
      </StatStrip>

      {upNext.length > 0 ? (
        <section className="mt-8 space-y-3">
          <h2 className="flex flex-wrap items-baseline gap-x-2 text-heading">
            Up next
            <span className="text-sm font-normal text-muted-foreground tabular-nums">{upNext.length} sale{upNext.length === 1 ? '' : 's'} · {fmtMoney0(upNextValue)}</span>
          </h2>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5">
            {upNext.map((s) => <UpNextCard key={s.id} s={s} />)}
          </div>
        </section>
      ) : null}

      {/* Due-date tabs and filters, resolved on the server; the view and refresh sit at the end. */}
      <div className="mt-8 flex flex-wrap items-center gap-2">
        <Tabs className="min-w-0 max-w-full" value={dueFilter} onValueChange={(v) => setDueFilter(v as DueFilter)}>
          <TabsList className="h-8">
            <DueTab value="overdue" count={buckets.overdue}>Overdue</DueTab>
            <DueTab value="7d" count={buckets.soon7}><span className="sm:hidden">7 days</span><span className="hidden sm:inline">Next 7 days</span></DueTab>
            <DueTab value="30d" count={buckets.soon30}><span className="sm:hidden">30 days</span><span className="hidden sm:inline">Next 30 days</span></DueTab>
            <DueTab value="all" count={buckets.all}>All</DueTab>
          </TabsList>
        </Tabs>
        <Input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search name, contact, ABN or email…"
          className="h-8 w-full sm:w-64"
        />
        <FacetedFilter title="Status" options={statusOptions} selected={statuses} onChange={setStatuses} />
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
          <SentenceList empty={<EmptyState title={emptyMessage(dueFilter, hasActiveFilters)} />}>
            {tableRows.map((s) => <SaleSentence key={s.id} s={s} />)}
          </SentenceList>
        ) : (
          <DataTable columns={columns} data={tableRows} loading={isFetching && isPlaceholderData} empty={<EmptyState title={emptyMessage(dueFilter, hasActiveFilters)} />} />
        )}
        {(data?.filteredCount ?? 0) > PAGE_SIZE ? (
          <Pagination page={page} pageSize={PAGE_SIZE} total={data?.filteredCount ?? 0} onPage={setPage} />
        ) : null}
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
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Requeue failed sales</DialogTitle>
          <DialogDescription>Start with a small batch, watch how it goes, then run again.</DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-3">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <div className="flex items-center gap-2">
              <Label htmlFor="requeue-max" className="text-caption font-normal text-muted-foreground">Batch size</Label>
              <Input
                id="requeue-max" type="number" min={1} max={500} value={max}
                onChange={(e) => setMax(Math.max(1, Math.min(500, Number(e.target.value) || 1)))}
                className="h-8 w-20 tabular-nums"
              />
            </div>
            <Label className="text-caption font-normal text-muted-foreground" title="Sales past ASIC’s renewal window are left out unless you tick this">
              <Checkbox checked={includePastDue} onCheckedChange={(v) => setIncludePastDue(v === true)} />
              Include past due
            </Label>
          </div>
          <Input
            value={errorInput}
            onChange={(e) => setErrorInput(e.target.value)}
            placeholder="Only errors containing… (optional)"
            className="h-8"
          />

          {isFetching && !preview ? (
            <div className="flex items-center justify-center gap-2 py-4 text-sm text-muted-foreground">
              <Loader2 className="size-4 animate-spin" /> Checking what would change…
            </div>
          ) : preview ? (
            <>
              <p className="text-sm">
                <span className="font-medium tabular-nums">{preview.batchSize}</span> of{' '}
                <span className="tabular-nums">{preview.totalMatching}</span> matching failed sales in this batch
              </p>
              {preview.items.length > 0 ? (
                <div className="max-h-36 divide-y overflow-y-auto rounded-lg border">
                  {preview.items.slice(0, 25).map((i) => (
                    <div key={i.id} className="flex items-center justify-between gap-2 px-3 py-1.5 text-caption">
                      <span className="min-w-0 truncate">{i.businessName} · <span className="font-mono tabular-nums text-muted-foreground">{i.abn}</span></span>
                      <span className="shrink-0 text-muted-foreground tabular-nums">{fmtMoney0(i.amountPaid)}</span>
                    </div>
                  ))}
                </div>
              ) : null}
            </>
          ) : null}
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={liveMutation.isPending}>Close</Button>
          <Button onClick={runLive} disabled={liveMutation.isPending || !preview || nothingToDo}>
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
      ? 'Nothing matches these filters'
      : 'Nothing due in this window matches. Try All.'
  }
  switch (f) {
    case 'overdue': return 'Nothing overdue. All on track'
    case '7d':      return 'Nothing due in the next 7 days'
    case '30d':     return 'Nothing due in the next 30 days'
    case 'all':     return 'No Ontraport sales yet'
  }
}

function defaultStats(): Stats {
  return {
    pipelineValueNext30d: 0, lastSyncAt: null, nextSyncAt: null,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [],
  }
}

function DueTab({ value, count, children }: { value: DueFilter; count: number; children: React.ReactNode }) {
  return (
    <TabsTrigger value={value}>
      {children}
      <span className="text-muted-foreground tabular-nums">{count.toLocaleString()}</span>
    </TabsTrigger>
  )
}

/** "3 days overdue", "today", "in 9 days", with a dot when it's close. */
function DueWords({ days }: { days: number }) {
  const dot = days < 0 ? 'bg-destructive' : days <= 7 ? 'bg-warning' : null
  return (
    <span className="inline-flex items-center gap-1.5">
      {dot ? <span aria-hidden className={cn('size-2 rounded-full', dot)} /> : null}
      {days < 0 ? `${Math.abs(days)} day${days === -1 ? '' : 's'} overdue` : days === 0 ? 'Due today' : `Due in ${days} day${days === 1 ? '' : 's'}`}
    </span>
  )
}

function UpNextCard({ s }: { s: SaleRow }) {
  // Up-next rows all have a due date (the server only picks those).
  const days = s._daysUntil ?? 0
  const body = (
    <>
      <div className="text-caption text-muted-foreground tabular-nums"><DueWords days={days} /></div>
      <div className="truncate font-medium" title={s.businessName}>{s.businessName}</div>
      <div className="truncate text-caption text-muted-foreground">{s.contactName || s.email}</div>
      <div className="mt-auto flex items-center justify-between gap-2 pt-2">
        <span className="font-mono text-sm tabular-nums">{fmtMoney0(s.amountPaid)}</span>
        <OntraportStatusPill status={s.status} />
      </div>
    </>
  )
  // When a renewal exists, the whole card opens it. Otherwise it's a plain tile.
  return (
    <Card className={cn('relative gap-0.5 p-4', s.renewalRequestId && 'transition-colors hover:bg-muted/50')}>
      {s.renewalRequestId ? (
        <Link to={`/admin/renewals/${s.renewalRequestId}`} className="absolute inset-0 rounded-xl" aria-label={`Open the renewal for ${s.businessName}`} />
      ) : null}
      {body}
    </Card>
  )
}

function DueCell({ s }: { s: SaleRow }) {
  if (!s.renewalDueDate || s._daysUntil === null) {
    return <span className="text-muted-foreground">—</span>
  }
  return (
    <div className="whitespace-nowrap">
      <div className="tabular-nums">{new Date(s.renewalDueDate).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })}</div>
      <div className="text-caption text-muted-foreground tabular-nums"><DueWords days={s._daysUntil} /></div>
    </div>
  )
}

function SaleStatusCell({ s, onShowError }: { s: SaleRow; onShowError: (title: string, body: string) => void }) {
  if (s.handledAt && s.status !== 'RenewalCompleted') return <Status entry={HANDLED} />
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
      <div className="flex items-center gap-1">
        <OntraportStatusPill status={s.status} />
        {isFailed ? (
          <Button variant="ghost" size="icon-sm" className="sm:size-6" onClick={() => onShowError(modalTitle, errorBody)} title="Why did this fail?" aria-label="Why did this fail?">
            <CircleAlert className="text-destructive" />
          </Button>
        ) : isBlocked ? (
          <Button variant="ghost" size="icon-sm" className="sm:size-6" onClick={() => onShowError(modalTitle, errorBody)} title={retryBanner(s.status)} aria-label="More about this status">
            <Info className="text-muted-foreground" />
          </Button>
        ) : null}
      </div>
      {isFailed && s.renewalFailedAtStep ? (
        <div className="mt-1 text-caption text-muted-foreground">Stopped at {s.renewalFailedAtStep}</div>
      ) : null}
      {isBlocked ? (
        <div className="mt-1 text-caption text-muted-foreground">{retryHint(s.status)}</div>
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
    case 'AsicNotYetDue':     return 'Retries at 07:00'
    case 'RenewalInProgress': return 'Retries at 07:00'
    case 'IneligibleForRenewal': return 'Skipped, won’t retry'
    default:                  return ''
  }
}

function OntraportStatusPill({ status }: { status: string }) {
  return <Status kind="sale" status={status} />
}
