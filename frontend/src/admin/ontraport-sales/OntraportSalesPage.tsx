import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, ListRestart, Loader2, RefreshCw, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../../api/client'
import { ErrorModal, Pagination, useDebouncedValue } from '../_components'
import { EmptyState, pageClass, PageHeader, RefreshButton, Stat, StatStrip, ViewSwitch, ViewToggle } from '../_ui'
import { SaleSentence, SentenceList } from '../_sentences'
import { fmtMoney0, fmtMoney2, relativeTime } from '../_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { Skeletonizer } from '@/components/skeletonizer'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { DueCell, SaleStatusCell } from './cells'
import { defaultStats, emptyMessage, PAGE_SIZE, SKELETON_RESPONSE, SKELETON_ROWS, statusLabel, withDaysUntil, type DueFilter, type Facets, type SaleRow, type Stats } from './model'
import { RequeueFailedDialog } from './RequeueFailedDialog'
import { UpNextCard } from './UpNextCard'

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
  const { data: realData, isFetching, isPending, isPlaceholderData, refetch } = useQuery({
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
  // Sample counts and up-next cards drawn as a skeleton until the first answer arrives.
  const data = realData ?? (isPending ? SKELETON_RESPONSE : undefined)

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
  // The tabs and header sit outside the skeleton, so they only ever show real counts.
  const tabCounts = realData?.buckets ?? { overdue: 0, overdueValue: 0, soon7: 0, soon30: 0, all: 0 }
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
                <Link to={s.orderId ? `/admin/renewal-orders/${s.orderId}` : `/admin/renewals/${s.renewalRequestId}`}>{isFailed ? 'Investigate' : 'Open renewal'}</Link>
              </Button>
            ) : null}
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [keyRequestMutation.isPending])

  return (
    <div className={pageClass()}>
      <PageHeader
        title="Ontraport sales"
        subtitle="Paid in Ontraport, renewed once the name is inside ASIC’s window."
        right={
          <>
            {(realData?.failedCount ?? 0) > 0 ? (
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
      <Skeletonizer loading={isPending}>
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
      </Skeletonizer>

      {upNext.length > 0 ? (
        <Skeletonizer loading={isPending} className="mt-8">
        <section className="space-y-3">
          <h2 className="flex flex-wrap items-baseline gap-x-2 text-heading">
            Up next
            <span className="text-sm font-normal text-muted-foreground tabular-nums">{upNext.length} sale{upNext.length === 1 ? '' : 's'} · {fmtMoney0(upNextValue)}</span>
          </h2>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5">
            {upNext.map((s) => <UpNextCard key={s.id} s={s} />)}
          </div>
        </section>
        </Skeletonizer>
      ) : null}

      {/* Due-date tabs and filters, resolved on the server; the view and refresh sit at the end. */}
      <div className="mt-8 flex flex-wrap items-center gap-2">
        <ViewSwitch<DueFilter> label="Due" className="min-w-0" value={dueFilter} onChange={setDueFilter} options={[
          { value: 'overdue', label: 'Overdue', count: tabCounts.overdue },
          { value: '7d', label: <><span className="sm:hidden">7 days</span><span className="hidden sm:inline">Next 7 days</span></>, count: tabCounts.soon7 },
          { value: '30d', label: <><span className="sm:hidden">30 days</span><span className="hidden sm:inline">Next 30 days</span></>, count: tabCounts.soon30 },
          { value: 'all', label: 'All', count: tabCounts.all },
        ]} />
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
          <Skeletonizer loading={isPending}>
            <SentenceList empty={<EmptyState title={emptyMessage(dueFilter, hasActiveFilters)} />}>
              {(isPending ? SKELETON_ROWS : tableRows).map((s) => <SaleSentence key={s.id} s={s} />)}
            </SentenceList>
          </Skeletonizer>
        ) : (
          <DataTable columns={columns} data={tableRows} loading={isPending || (isFetching && isPlaceholderData)} skeleton={SKELETON_ROWS} empty={<EmptyState title={emptyMessage(dueFilter, hasActiveFilters)} />} />
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
