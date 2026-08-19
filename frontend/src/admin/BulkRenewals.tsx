import { useMemo, useRef, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Upload as UploadIcon, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api } from '../api/client'
import { EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill } from './_ui'
import { fmtMoney0, fmtMoney2, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'

type BulkResponse = Awaited<ReturnType<typeof api.admin.bulkRenewals>>
type Upload = BulkResponse['items'][number]
type Stats = BulkResponse['stats']
type Facets = BulkResponse['facets']

/** Operator-friendly names for BulkRenewalStatus values (matches the pill wording). */
function bulkStatusLabel(status: string): string {
  switch (status) {
    case 'WaitingForRenewalWindow': return 'Waiting'
    case 'RenewalQueued':           return 'Queued'
    case 'RenewalCompleted':        return 'Completed'
    case 'RenewalFailed':           return 'Failed'
    case 'NotDueForRenewal':        return 'Not due'
    case 'Skipped':                 return 'Skipped'
    default:                        return status
  }
}

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

  const statusOptions: FacetOption[] = facets.status.map((f) => ({ value: f.value, label: bulkStatusLabel(f.value), count: f.count }))
  const batchOptions: FacetOption[] = facets.batch.map((f) => ({ value: f.value, label: f.value, count: f.count }))

  const columns = useMemo<DataTableColumn<Upload>[]>(() => [
    {
      id: 'business',
      accessorKey: 'businessName',
      header: 'Business',
      // Compact on phones: business name only; ABN + owner return at sm.
      meta: { sticky: true, className: 'min-w-[7rem] max-w-[38vw] sm:min-w-[14rem] sm:max-w-[18rem]' },
      cell: ({ row }) => {
        const u = row.original
        return (
          <div>
            <div className="text-sm font-medium text-zinc-900 truncate">{u.businessName}</div>
            <div className="hidden sm:block text-xxs font-mono tabular-nums text-zinc-500">{u.abn}</div>
            <div className="hidden sm:block text-xxs font-mono text-zinc-400 truncate">{u.ownerName ?? 'no owner'}</div>
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
      meta: { className: 'text-right font-mono tabular-nums text-sm text-zinc-900', headerClassName: 'text-right' },
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
              <div className="mt-1 text-xxs font-mono text-red-700 truncate max-w-[12rem]" title={u.errorMessage}>{u.errorMessage}</div>
            ) : null}
          </div>
        )
      },
    },
    {
      accessorKey: 'uploadedAt',
      header: 'Source · Uploaded',
      cell: ({ row }) => (
        <div>
          <div className="text-xxs font-mono text-zinc-500 truncate max-w-[10rem]">{row.original.sourceFile ?? '(direct)'}</div>
          <div className="text-xs text-zinc-700 tabular-nums">{relativeTime(row.original.uploadedAt)}</div>
        </div>
      ),
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="PIPELINE"
        title="Bulk renewals"
        subtitle="Renewals imported from spreadsheet uploads — auto-fire when the due date enters the renewal window."
        right={
          <>
            <input ref={inputRef} type="file" accept=".xlsx" className="hidden" onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])} />
            <button
              onClick={() => inputRef.current?.click()}
              disabled={uploadMutation.isPending}
              className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-zinc-900 text-white px-3 py-2 text-sm font-medium hover:bg-zinc-800 shadow-sm disabled:opacity-50 transition"
            >
              <UploadIcon className="h-4 w-4" />
              {uploadMutation.isPending ? 'Uploading…' : 'Upload .xlsx'}
            </button>
            <button
              onClick={processEligible}
              disabled={processMutation.isPending}
              className="inline-flex items-center whitespace-nowrap rounded-md bg-brand-600 text-white px-3 py-2 text-sm font-medium hover:bg-brand-700 shadow-sm disabled:opacity-50 transition"
            >
              {processMutation.isPending ? 'Queueing…' : 'Process eligible'}
            </button>
            {failedCount > 0 ? (
              <button
                onClick={retryFailed}
                disabled={retryMutation.isPending}
                className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-amber-600 text-white px-3 py-2 text-sm font-medium hover:bg-amber-700 shadow-sm disabled:opacity-50 transition"
              >
                {retryMutation.isPending ? 'Retrying…' : `Retry ${failedCount} failed`}
              </button>
            ) : null}
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatTile
          kicker="30D"
          label="Pipeline next 30d"
          value={fmtMoney0(stats.pipelineValueNext30d)}
          sub={`${data?.waitingCount ?? 0} waiting · ${data?.queuedCount ?? 0} queued`}
          tone="emerald"
        />
        <StatTile
          kicker="STATUS"
          label="Failed"
          value={failedCount.toLocaleString()}
          sub={failedCount > 0 ? 'click "Retry" to re-queue' : 'all clear'}
          tone={failedCount > 0 ? 'red' : 'zinc'}
        />
        <StatTile
          kicker="UPLOAD"
          label="Last upload"
          value={stats.lastUploadAt ? relativeTime(stats.lastUploadAt) : '—'}
          sub={`${data?.totalCount ?? 0} total rows`}
        />
        <SparklineTile
          kicker="14D"
          label="Today"
          value={stats.today.toLocaleString()}
          sub={`${stats.yesterday.toLocaleString()} yesterday`}
          deltaPct={stats.deltaPct}
          data={stats.daily14d.map((d) => d.count)}
        />
      </div>

      {/* Batches strip — clicking a batch toggles it in the server-side batch facet */}
      {stats.byBatch.length > 0 ? (
        <div className="mt-6">
          <div className="flex items-end justify-between mb-3">
            <div>
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">BATCHES</div>
              <div className="text-sm text-zinc-500">Click a batch to filter the table.</div>
            </div>
            {batches.length > 0 ? (
              <button onClick={() => setBatches([])} className="text-xs font-medium text-zinc-600 hover:text-zinc-900 px-2">Show all</button>
            ) : null}
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
            {stats.byBatch.map((b) => {
              // "(direct)" rows have no sourceFile, so the server can't facet on them.
              const filterable = b.sourceFile !== '(direct)'
              const active = batches.includes(b.sourceFile)
              return (
                <button
                  key={b.sourceFile}
                  onClick={() => filterable && toggleBatch(b.sourceFile)}
                  disabled={!filterable}
                  className={`text-left rounded-xl border p-4 transition ${
                    active
                      ? 'border-brand-300 bg-brand-50 ring-1 ring-brand-200'
                      : filterable
                        ? 'border-zinc-200 bg-white hover:border-zinc-300'
                        : 'border-zinc-200 bg-white cursor-default'
                  }`}
                >
                  <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 truncate">{b.sourceFile}</div>
                  <div className="mt-1 flex items-baseline gap-2">
                    <div className="text-lg font-semibold tabular-nums text-zinc-900">{b.total.toLocaleString()}</div>
                    <div className="text-xs text-zinc-500">rows</div>
                  </div>
                  <div className="mt-1 text-xxs font-mono tabular-nums">
                    <span className="text-brand-700">{b.completed} done</span>
                    {' · '}
                    <span className={b.failed > 0 ? 'text-red-700' : 'text-zinc-400'}>{b.failed} failed</span>
                    {' · '}
                    <span className="text-zinc-700">{fmtMoney0(b.pipelineValue)} pipeline</span>
                  </div>
                </button>
              )
            })}
          </div>
        </div>
      ) : null}

      {/* Filter toolbar — facet options and counts are resolved server-side */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        <FacetedFilter title="Status" options={statusOptions} selected={statuses} onChange={setStatuses} />
        <FacetedFilter title="Batch" options={batchOptions} selected={batches} onChange={setBatches} />
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" className="h-9" onClick={resetFilters}>
            Reset
            <X className="h-4 w-4" />
          </Button>
        ) : null}
      </div>

      <div className="mt-4">
        <DataTable
          columns={columns}
          data={items}
          empty={<EmptyState title={hasActiveFilters ? 'No rows match the current filters.' : 'No bulk renewals yet. Upload an .xlsx to get started.'} />}
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
  const daysClass = days == null ? 'text-zinc-400' : days <= 0 ? 'text-red-700 font-medium' : days <= 30 ? 'text-brand-700 font-medium' : 'text-zinc-500'
  return (
    <div>
      <div className="text-sm text-zinc-700 tabular-nums">{u.renewalDueDate ? new Date(u.renewalDueDate).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) : '—'}</div>
      <div className={`text-xxs font-mono tabular-nums ${daysClass}`}>{days == null ? '' : `${days} days`}</div>
    </div>
  )
}

function BulkStatusPill({ status }: { status: string }) {
  switch (status) {
    case 'WaitingForRenewalWindow': return <StatusPill tone="amber">WAITING</StatusPill>
    case 'RenewalQueued':           return <StatusPill tone="indigo">QUEUED</StatusPill>
    case 'RenewalCompleted':        return <StatusPill tone="emerald">COMPLETED</StatusPill>
    case 'RenewalFailed':           return <StatusPill tone="red">FAILED</StatusPill>
    case 'NotDueForRenewal':        return <StatusPill tone="zinc">NOT DUE</StatusPill>
    case 'Skipped':                 return <StatusPill tone="zinc">SKIPPED</StatusPill>
    default:                        return <StatusPill tone="zinc">{status.toUpperCase()}</StatusPill>
  }
}
