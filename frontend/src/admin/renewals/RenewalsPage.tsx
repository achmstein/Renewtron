import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Wrench, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../../api/client'
import { DateRangeFilter, ErrorModal, Pagination, useDebouncedValue } from '../_components'
import { RenewalSentence, SentenceList } from '../_sentences'
import { EmptyState, pageClass, PageHeader, RefreshButton, ViewToggle } from '../_ui'
import { statusLabel } from '../_status'
import { fmtMoney0, fmtMoney2 } from '../_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { Skeletonizer } from '@/components/skeletonizer'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { BulkRetryModal } from './BulkRetryModal'
import { BpayPill, PaymentPill, SourcePill, StatusCell, TimeInStatus } from './cells'
import { canRetry, categoryLabel, defaultStats, PAGE_SIZE, SKELETON_ROWS, SKELETON_STATS, type Facets, type Renewal, type Stats, type ViewMode } from './model'
import { ReconcileDialog } from './ReconcileDialog'
import { RenewalCard } from './RenewalCard'
import { StatsStrip } from './StatsStrip'

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
    <div className={pageClass()}>
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
