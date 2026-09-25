import { useEffect, useMemo, useRef, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, Play, RotateCw, Send, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type AsicKeyRequestDto, type AsicKeyRequestJobStatus } from '../api/client'
import { useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, type Tone } from './_ui'
import { fmtDateTime, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Dialog, DialogBody, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'

type RequestsResponse = Awaited<ReturnType<typeof api.admin.asicKeyRequests>>
type Row = AsicKeyRequestDto
type Stats = RequestsResponse['stats']
type Facets = RequestsResponse['facets']

const STATUS: Record<string, { label: string; tone: Tone }> = {
  Pending:     { label: 'Pending',      tone: 'amber' },
  Submitted:   { label: 'Submitted',    tone: 'indigo' },
  KeyReceived: { label: 'Key received', tone: 'emerald' },
  Failed:      { label: 'Failed',       tone: 'red' },
}
const statusLabel = (s: string) => STATUS[s]?.label ?? s

export default function AsicKeyRequests() {
  const [statuses, setStatuses] = useState<string[]>([])
  const [searchInput, setSearchInput] = useState('')
  const search = useDebouncedValue(searchInput, 300)
  const [detail, setDetail] = useState<Row | null>(null)

  const queryClient = useQueryClient()
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['admin-asic-key-requests', { statuses, search }],
    queryFn: () => api.admin.asicKeyRequests({ status: statuses.join(',') || undefined, search: search || undefined }),
    placeholderData: keepPreviousData,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-asic-key-requests'] })

  // Every submission is a browser session of a minute or so, run as a Hangfire job. While
  // one is in flight we poll its state and refresh the table on every tick.
  const [jobId, setJobId] = useState<string | null>(null)
  const jobQuery = useQuery({
    queryKey: ['admin-asic-key-requests-job', jobId],
    queryFn: () => api.admin.asicKeyRequestJob(jobId!),
    enabled: !!jobId,
    refetchInterval: (q) => (q.state.data?.done ? false : 2000),
  })
  const jobDone = useRef<((s: AsicKeyRequestJobStatus) => void) | null>(null)
  useEffect(() => {
    if (!jobId) return
    void invalidate()
    const s = jobQuery.data
    if (s?.done) {
      setJobId(null)
      jobDone.current?.(s)
      jobDone.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [jobId, jobQuery.dataUpdatedAt])

  const describeResult = (s: AsicKeyRequestJobStatus): { title: string; description?: string } => {
    if (s.state === 'Failed') return { title: 'Job failed', description: s.error ?? 'See the Hangfire dashboard.' }
    const r = s.result
    if (!r) return { title: 'Done', description: 'Table refreshed.' }
    if ('message' in r) return { title: r.skipped ? 'Run skipped' : 'Run complete', description: r.message }
    if (r.status === 'Submitted') return { title: `ASIC accepted ${r.businessName}`, description: `Reference ${r.asicReferenceNumber}` }
    return { title: `${r.businessName}: ${statusLabel(r.status).toLowerCase()}`, description: r.errorMessage ?? undefined }
  }

  const trackJob = (start: Promise<{ jobId: string | null }>, loadingTitle: string) => {
    const finished = new Promise<AsicKeyRequestJobStatus>((resolve, reject) => {
      start.then((r) => {
        if (!r.jobId) { resolve({ jobId: '', state: 'Succeeded', done: true, result: null, error: null }); return }
        jobDone.current = resolve
        setJobId(r.jobId)
      }).catch(reject)
    })
    void sileo.promise(finished, {
      loading: { title: loadingTitle },
      success: describeResult,
      error: (e) => ({ title: 'Could not start', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }
  const busy = !!jobId
  const runNow = () => trackJob(api.admin.runAsicKeyRequests(), 'Sending queued requests to ASIC…')
  const submit = (row: Row) => trackJob(api.admin.submitAsicKeyRequest(row.id), `Sending ${row.businessName} to ASIC…`)

  const requeueMutation = useMutation({
    mutationFn: (id: string) => api.admin.requeueAsicKeyRequest(id),
    onSettled: () => void invalidate(),
  })
  const requeue = (row: Row) => {
    void sileo.promise(requeueMutation.mutateAsync(row.id), {
      loading: { title: `Re-queuing ${row.businessName}…` },
      success: () => ({ title: `${row.businessName} will go out on the next run` }),
      error: (e) => ({ title: 'Re-queue failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const items = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const facets: Facets = data?.facets ?? { status: [] }
  const pendingCount = data?.pendingCount ?? 0
  const submittedCount = data?.submittedCount ?? 0
  const keyReceivedCount = data?.keyReceivedCount ?? 0
  const failedCount = data?.failedCount ?? 0
  const stuckCount = data?.stuckCount ?? 0

  const hasActiveFilters = !!searchInput || statuses.length > 0
  const resetFilters = () => { setSearchInput(''); setStatuses([]) }
  const statusOptions: FacetOption[] = facets.status.map((f) => ({ value: f.value, label: statusLabel(f.value), count: f.count }))

  const columns = useMemo<DataTableColumn<Row>[]>(() => [
    {
      id: 'business',
      accessorFn: (r: Row) => r.businessName,
      header: 'Business',
      meta: { sticky: true, className: 'min-w-[7rem] max-w-[38vw] sm:min-w-[14rem] sm:max-w-[18rem]' },
      cell: ({ row }) => {
        const r = row.original
        return (
          <div>
            <div className="text-sm font-medium truncate text-zinc-900">{r.businessName}</div>
            <div className="hidden sm:block text-xxs font-mono tabular-nums text-zinc-500">{r.abn}</div>
          </div>
        )
      },
    },
    {
      id: 'contact',
      accessorFn: (r: Row) => r.contactName,
      header: 'Contact',
      cell: ({ row }) => {
        const r = row.original
        return (
          <div>
            <div className="text-sm text-zinc-800 truncate max-w-[12rem]">{r.contactName}</div>
            <div className="text-xxs font-mono text-zinc-500 truncate max-w-[12rem]" title={r.email}>{r.email || '—'}</div>
          </div>
        )
      },
    },
    {
      accessorKey: 'status',
      header: 'Status',
      enableSorting: false,
      cell: ({ row }) => {
        const r = row.original
        return (
          <div>
            <RequestStatusPill status={r.status} />
            {r.asicReferenceNumber ? (
              <div className="mt-1 text-xxs font-mono tabular-nums text-zinc-500">ref {r.asicReferenceNumber}</div>
            ) : null}
            {r.errorMessage ? (
              <button
                type="button"
                onClick={(e) => { e.stopPropagation(); setDetail(r) }}
                className="mt-1 block text-left text-xxs font-mono text-red-700 truncate max-w-[14rem] hover:underline"
                title="Show details"
              >
                {r.errorMessage}
              </button>
            ) : null}
          </div>
        )
      },
    },
    {
      accessorKey: 'createdAt',
      header: 'Queued',
      cell: ({ row }) => (
        <div>
          <div className="text-xs text-zinc-700 tabular-nums">{relativeTime(row.original.createdAt)}</div>
          <div className="text-xxs font-mono tabular-nums text-zinc-400">
            {row.original.source.toLowerCase()} · {row.original.attemptCount} attempt{row.original.attemptCount === 1 ? '' : 's'}
          </div>
        </div>
      ),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      meta: { className: 'text-right whitespace-nowrap', headerClassName: 'text-right' },
      cell: ({ row }) => {
        const r = row.original
        if (r.status === 'KeyReceived') return null
        return (
          <div className="flex items-center justify-end gap-1">
            {r.status === 'Failed' ? (
              <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" disabled={requeueMutation.isPending || busy} onClick={(e) => { e.stopPropagation(); requeue(r) }} title="Back in the queue for the next run">
                <RotateCw className="h-3.5 w-3.5" /> Re-queue
              </Button>
            ) : null}
            <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" disabled={busy} onClick={(e) => { e.stopPropagation(); submit(r) }} title="Send this one to ASIC now">
              <Send className="h-3.5 w-3.5" /> {r.status === 'Submitted' ? 'Resend' : 'Send now'}
            </Button>
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [requeueMutation.isPending, busy])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="PIPELINE"
        title="ASIC key requests"
        subtitle="Each paid sale → ASIC's online enquiry form, filled in by the server's own browser in the client's name, asking for the key to be emailed to the inbox. Runs twice an hour."
        right={
          <>
            <button
              onClick={runNow}
              disabled={busy}
              className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-brand-600 text-white px-3 py-2 text-sm font-medium hover:bg-brand-700 shadow-sm disabled:opacity-50 transition"
            >
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
              {busy ? 'Working…' : `Send ${pendingCount} queued`}
            </button>
            <RefreshButton onClick={() => void refetch()} busy={isFetching || busy} />
          </>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatTile
          kicker="QUEUE"
          label="Waiting to send"
          value={pendingCount.toLocaleString()}
          sub={`${submittedCount.toLocaleString()} sent, awaiting key`}
          tone={pendingCount > 0 ? 'amber' : 'zinc'}
        />
        <StatTile
          kicker="DONE"
          label="Keys received"
          value={keyReceivedCount.toLocaleString()}
          sub={`${(data?.totalCount ?? 0).toLocaleString()} requests total`}
          tone="emerald"
        />
        <StatTile
          kicker="FAILED"
          label="Needs attention"
          value={failedCount.toLocaleString()}
          sub={stuckCount > 0 ? `${stuckCount} need a fix before retry` : failedCount > 0 ? 'auto-retry on next run' : 'all clear'}
          tone={failedCount > 0 ? 'red' : 'zinc'}
        />
        <SparklineTile
          kicker="14D"
          label="Sent today"
          value={stats.today.toLocaleString()}
          sub={stats.lastRunAt
            ? `last run ${relativeTime(stats.lastRunAt)}${stats.lastRunState === 'Failed' ? ' (failed)' : ''}`
            : 'no run yet'}
          deltaPct={stats.deltaPct}
          data={stats.daily14d.map((d) => d.count)}
        />
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search business, ABN, contact, reference…"
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
        <DataTable
          columns={columns}
          data={items}
          onRowClick={(r) => setDetail(r.original)}
          empty={<EmptyState
            title={hasActiveFilters ? 'No requests match the current filters.' : 'No ASIC key requests yet.'}
            message={hasActiveFilters ? undefined : 'Turn on Settings → ASIC key requests, then sync Ontraport or use “Request key” on a sale.'}
          />}
        />
      </div>

      <DetailDialog row={detail} onClose={() => setDetail(null)} />
    </div>
  )
}

function defaultStats(): Stats {
  return {
    lastRunAt: null, lastRunState: null, lastRunError: null, nextRunAt: null, runCron: null,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [],
  }
}

function RequestStatusPill({ status }: { status: string }) {
  const s = STATUS[status]
  return <StatusPill tone={s?.tone ?? 'zinc'}>{(s?.label ?? status).toUpperCase()}</StatusPill>
}

function DetailDialog({ row, onClose }: { row: Row | null; onClose: () => void }) {
  return (
    <Dialog open={!!row} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
        {row ? (
          <>
            <DialogHeader>
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">ASIC KEY REQUEST</div>
              <DialogTitle>{row.businessName}</DialogTitle>
            </DialogHeader>
            <DialogBody className="space-y-3">
              <div className="flex flex-wrap items-center gap-2">
                <RequestStatusPill status={row.status} />
                <span className="text-xs text-zinc-500">
                  queued {fmtDateTime(row.createdAt)}{row.processedAt ? ` · last attempt ${fmtDateTime(row.processedAt)}` : ''} · {row.attemptCount} attempt{row.attemptCount === 1 ? '' : 's'}
                </span>
              </div>

              <dl className="grid grid-cols-[7rem_1fr] gap-y-1.5 text-sm">
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Reference</dt>
                <dd className="font-mono tabular-nums text-zinc-900">{row.asicReferenceNumber ?? '—'}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">ABN</dt>
                <dd className="font-mono tabular-nums text-zinc-700">{row.abn}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Contact</dt>
                <dd className="text-zinc-700">{row.contactName}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Reply to</dt>
                <dd className="text-zinc-700 break-all">{row.email || '—'}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Phone</dt>
                <dd className="font-mono tabular-nums text-zinc-700">{row.phone || '—'}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Ontraport</dt>
                <dd className="font-mono text-zinc-700">{row.ontraportContactId ?? '—'}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Submitted</dt>
                <dd className="text-zinc-700">{row.submittedAt ? fmtDateTime(row.submittedAt) : '—'}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Key received</dt>
                <dd className="text-zinc-700">{row.keyReceivedAt ? fmtDateTime(row.keyReceivedAt) : '—'}</dd>
              </dl>

              {row.errorMessage ? (
                <div className="rounded-md bg-red-50 ring-1 ring-red-100 px-3 py-2 text-xs font-mono text-red-800 whitespace-pre-wrap break-words">
                  {row.errorMessage}
                  {!row.canAutoRetry ? <div className="mt-1 text-red-600">Won't retry on its own — fix the data or the settings, then re-queue.</div> : null}
                </div>
              ) : null}

              {row.question ? (
                <div>
                  <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 mb-1">Enquiry text sent</div>
                  <pre className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-3 py-2 text-xxs font-mono text-zinc-700 whitespace-pre-wrap break-words">{row.question}</pre>
                </div>
              ) : null}
            </DialogBody>
            <DialogFooter>
              <Button onClick={onClose} className="bg-zinc-900 hover:bg-zinc-800">Close</Button>
            </DialogFooter>
          </>
        ) : null}
      </DialogContent>
    </Dialog>
  )
}
