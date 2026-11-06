import { useMemo, useRef, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { RotateCw, Upload as UploadIcon, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { EmptyState, PageHeader, RefreshButton, SectionTitle, Stat, StatStrip } from './_ui'
import { Status, statusLabel } from './_status'
import { fmtMoney0, fmtMoney2, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

type BulkResponse = Awaited<ReturnType<typeof api.admin.bulkRenewals>>
type Upload = BulkResponse['items'][number]
type Stats = BulkResponse['stats']
type Facets = BulkResponse['facets']

export default function BulkRenewals() {
  const [statuses, setStatuses] = useState<string[]>([])
  const [batches, setBatches] = useState<string[]>([])
  const inputRef = useRef<HTMLInputElement>(null)

  const queryClient = useQueryClient()
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['admin-bulk-renewals', { statuses, batches }],
    queryFn: () => api.admin.bulkRenewals({
      status: statuses.join(',') || undefined,
      batch: batches.join(',') || undefined,
    }),
    placeholderData: keepPreviousData,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-bulk-renewals'] })

  const uploadMutation = useMutation({
    mutationFn: (file: File) => api.admin.uploadBulkRenewals(file),
    onSettled: () => {
      void invalidate()
      if (inputRef.current) inputRef.current.value = ''
    },
  })
  const upload = (file: File) => {
    if (!file.name.toLowerCase().endsWith('.xlsx')) {
      sileo.error({ title: 'Only .xlsx files are supported.' })
      if (inputRef.current) inputRef.current.value = ''
      return
    }
    void sileo.promise(uploadMutation.mutateAsync(file), {
      loading: { title: 'Uploading spreadsheet…' },
      success: (r) => ({
        title: `Processed ${r.totalRows} rows`,
        description: `${r.importedCount} imported, ${r.skippedDuplicates} duplicates skipped, ${r.skippedInvalid} invalid.`
          + (r.errors.length > 0 ? ' First errors: ' + r.errors.slice(0, 3).join('; ') : ''),
      }),
      error: (e) => ({ title: 'Upload failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const processMutation = useMutation({
    mutationFn: () => api.admin.processEligibleBulk(),
    onSettled: () => void invalidate(),
  })
  const processEligible = () => {
    void sileo.promise(processMutation.mutateAsync(), {
      loading: { title: 'Queuing eligible renewals…' },
      success: (r) => ({ title: r.message }),
      error: (e) => ({ title: 'Process eligible failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const retryMutation = useMutation({
    mutationFn: () => api.admin.retryFailedBulk(),
    onSettled: () => void invalidate(),
  })
  const retryFailed = () => {
    void sileo.promise(retryMutation.mutateAsync(), {
      loading: { title: 'Retrying failed renewals…' },
      success: (r) => ({ title: `Re-queued ${r.retried} failed bulk renewals.` }),
      error: (e) => ({ title: 'Retry failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const items = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const failedCount = data?.failedCount ?? 0
  const facets: Facets = data?.facets ?? { status: [], batch: [] }

  const hasActiveFilters = statuses.length > 0 || batches.length > 0
  const resetFilters = () => { setStatuses([]); setBatches([]) }
  const toggleBatch = (sourceFile: string) =>
    setBatches((prev) => (prev.includes(sourceFile) ? prev.filter((b) => b !== sourceFile) : [...prev, sourceFile]))

  const statusOptions: FacetOption[] = facets.status.map((f) => ({ value: f.value, label: statusLabel('bulk', f.value), count: f.count }))
  const batchOptions: FacetOption[] = facets.batch.map((f) => ({ value: f.value, label: f.value, count: f.count }))

  const columns = useMemo<DataTableColumn<Upload>[]>(() => [
    {
      id: 'business',
      accessorKey: 'businessName',
      header: 'Business',
      // Compact on phones: business name only; ABN + owner return at sm.
      meta: { sticky: true, className: 'min-w-28 max-w-[38vw] sm:min-w-56 sm:max-w-72' },
      cell: ({ row }) => {
        const u = row.original
        return (
          <div className="min-w-0">
            <div className="truncate text-sm font-medium">{u.businessName}</div>
            <div className="hidden truncate text-caption text-muted-foreground sm:block">
              <span className="font-mono tabular-nums">{u.abn}</span> · {u.ownerName ?? 'no owner'}
            </div>
          </div>
        )
      },
    },
    {
      id: 'renewalDueDate',
      // Nullable ISO date — sort on '' so undated rows group together instead of jittering.
      accessorFn: (u: Upload) => u.renewalDueDate ?? '',
      header: 'Due date',
      cell: ({ row }) => <DueDateCell u={row.original} />,
    },
    {
      accessorKey: 'amount',
      header: 'Amount',
      meta: { className: 'text-right font-mono tabular-nums text-sm', headerClassName: 'text-right' },
      cell: ({ row }) => fmtMoney2(row.original.amount),
    },
    {
      accessorKey: 'status',
      header: 'Status',
      enableSorting: false,
      cell: ({ row }) => {
        const u = row.original
        return (
          <div>
            <BulkStatusPill status={u.status} />
            {u.errorMessage ? (
              <div className="mt-1 max-w-48 truncate text-caption text-muted-foreground" title={u.errorMessage}>{u.errorMessage}</div>
            ) : null}
          </div>
        )
      },
    },
    {
      accessorKey: 'uploadedAt',
      header: 'Uploaded',
      meta: { hideBelow: 'md' },
      cell: ({ row }) => (
        <div>
          <div className="text-sm tabular-nums">{relativeTime(row.original.uploadedAt)}</div>
          <div className="max-w-40 truncate text-caption text-muted-foreground" title={row.original.sourceFile ?? undefined}>{row.original.sourceFile ?? 'Added directly'}</div>
        </div>
      ),
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <PageHeader
        title="Bulk renewals"
        subtitle="Renewals from spreadsheet uploads; each starts once it’s due."
        right={
          <>
            <input ref={inputRef} type="file" accept=".xlsx" className="hidden" onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])} />
            <Button variant="outline" size="sm" onClick={() => inputRef.current?.click()} disabled={uploadMutation.isPending}>
              <UploadIcon />
              {uploadMutation.isPending ? 'Uploading…' : 'Upload .xlsx'}
            </Button>
            {failedCount > 0 ? (
              <Button variant="outline" size="sm" onClick={retryFailed} disabled={retryMutation.isPending}>
                <RotateCw />
                {retryMutation.isPending ? 'Retrying…' : `Retry ${failedCount} failed`}
              </Button>
            ) : null}
            <Button size="sm" onClick={processEligible} disabled={processMutation.isPending} title="Queue every row that is inside ASIC’s renewal window">
              {processMutation.isPending ? 'Queueing…' : 'Process eligible'}
            </Button>
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      <StatStrip>
        <Stat
          label="Due in the next 30 days"
          value={fmtMoney0(stats.pipelineValueNext30d)}
          sub={`${data?.waitingCount ?? 0} waiting · ${data?.queuedCount ?? 0} queued`}
        />
        <Stat
          label="Failed"
          value={failedCount.toLocaleString()}
          sub={failedCount > 0 ? 'Retry to queue them again' : 'All clear'}
          dot={failedCount > 0 ? 'action' : null}
        />
        <Stat
          label="Last upload"
          value={stats.lastUploadAt ? relativeTime(stats.lastUploadAt) : '—'}
          sub={`${data?.totalCount ?? 0} rows in all`}
        />
        <Stat
          label="Added today"
          value={stats.today.toLocaleString()}
          sub={`${stats.yesterday.toLocaleString()} yesterday · last 14 days`}
          trend={stats.daily14d}
          trendLabel="rows"
        />
      </StatStrip>

      {/* Batches: clicking one toggles it in the server-side batch facet */}
      {stats.byBatch.length > 0 ? (
        <div className="mt-8 space-y-3">
          <SectionTitle
            title="Batches"
            right={batches.length > 0
              ? <Button variant="ghost" size="sm" onClick={() => setBatches([])}>Show all</Button>
              : 'Tap or click one to filter'}
          />
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {stats.byBatch.map((b) => {
              // "(direct)" rows have no sourceFile, so the server can't facet on them.
              const filterable = b.sourceFile !== '(direct)'
              const active = batches.includes(b.sourceFile)
              return (
                <button
                  key={b.sourceFile}
                  type="button"
                  onClick={() => filterable && toggleBatch(b.sourceFile)}
                  disabled={!filterable}
                  aria-pressed={active}
                  className={cn(
                    'rounded-xl border bg-card p-4 text-left shadow-sm outline-none transition-colors focus-visible:ring-2 focus-visible:ring-ring',
                    filterable ? 'hover:bg-muted/50' : 'cursor-default',
                    active && 'bg-muted ring-1 ring-ring',
                  )}
                >
                  <div className="truncate text-sm text-muted-foreground">{b.sourceFile === '(direct)' ? 'Added directly' : b.sourceFile}</div>
                  <div className="mt-1 text-2xl font-semibold tracking-tight tabular-nums">
                    {b.total.toLocaleString()} <span className="text-sm font-normal text-muted-foreground">rows</span>
                  </div>
                  <div className="text-caption text-muted-foreground tabular-nums">
                    {b.completed} done · {b.failed} failed · {fmtMoney0(b.pipelineValue)} to come
                  </div>
                </button>
              )
            })}
          </div>
        </div>
      ) : null}

      {/* Filter toolbar — facet options and counts are resolved server-side */}
      <div className="mt-8 flex flex-wrap items-center gap-2">
        <FacetedFilter title="Status" options={statusOptions} selected={statuses} onChange={setStatuses} />
        <FacetedFilter title="Batch" options={batchOptions} selected={batches} onChange={setBatches} />
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" onClick={resetFilters}>
            Reset
            <X />
          </Button>
        ) : null}
      </div>

      <div className="mt-4">
        <DataTable
          columns={columns}
          data={items}
          empty={<EmptyState
            title={hasActiveFilters ? 'Nothing matches these filters' : 'No bulk renewals yet'}
            message={hasActiveFilters ? undefined : 'Upload an .xlsx to get started.'}
          />}
        />
      </div>
    </div>
  )
}


function defaultStats(): Stats {
  return {
    pipelineValueNext30d: 0, lastUploadAt: null,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [], byBatch: [],
  }
}

function DueDateCell({ u }: { u: Upload }) {
  const days = u.renewalDueDate ? Math.floor((new Date(u.renewalDueDate).getTime() - Date.now()) / 86400000) : null
  const words = days == null ? '' : days === 0 ? 'today' : days < 0 ? `${-days} days ago` : `in ${days} days`
  return (
    <div className="tabular-nums">
      <div className="text-sm">{u.renewalDueDate ? new Date(u.renewalDueDate).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) : '—'}</div>
      <div className={cn('text-caption text-muted-foreground', days != null && days <= 30 && 'font-medium text-foreground')}>{words}</div>
    </div>
  )
}

function BulkStatusPill({ status }: { status: string }) {
  return <Status kind="bulk" status={status} />
}
