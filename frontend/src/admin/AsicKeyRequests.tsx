import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, RotateCw, Send, X } from 'lucide-react'
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
  Submitted:   { label: 'Awaiting key', tone: 'indigo' },
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

  // Runs are Hangfire jobs: each submission buys a captcha and walks three ASIC pages, so a
  // batch outlasts a proxy timeout. Poll the job and refresh the table as rows change.
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

  const trackJob = (start: Promise<{ jobId: string }>, loadingTitle: string) => {
    const finished = new Promise<AsicKeyRequestJobStatus>((resolve, reject) => {
      start.then((r) => { jobDone.current = resolve; setJobId(r.jobId) }).catch(reject)
    })
    void sileo.promise(finished, {
      loading: { title: loadingTitle },
      success: (s) => s.state === 'Failed'
        ? { title: 'Run failed', description: s.error ?? 'See the Hangfire dashboard.' }
        : { title: s.result?.skipped ? 'Run skipped' : 'Run complete', description: s.result?.message ?? 'Table refreshed.' },
      error: (e) => ({ title: 'Could not start run', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }
  const running = !!jobId
  const run = () => trackJob(api.admin.runAsicKeyRequests(), 'Submitting queued requests to ASIC…')
  const retryAllFailed = () => trackJob(api.admin.retryFailedAsicKeyRequests(), 'Re-submitting failed requests…')

  const submitMutation = useMutation({
    mutationFn: (id: string) => api.admin.submitAsicKeyRequest(id),
    onSettled: () => void invalidate(),
  })
  const submit = (row: Row) => {
    void sileo.promise(submitMutation.mutateAsync(row.id), {
      loading: { title: `Submitting ${row.businessName} to ASIC…` },
      success: (r) => r.status === 'Submitted'
        ? { title: `Submitted — ASIC reference ${r.asicReferenceNumber}` }
        : { title: 'Submission failed', description: r.errorMessage ?? undefined },
      error: (e) => ({ title: 'Submit failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const requeueMutation = useMutation({
    mutationFn: (id: string) => api.admin.requeueAsicKeyRequest(id),
    onSettled: () => void invalidate(),
  })
  const requeue = (row: Row) => {
    void sileo.promise(requeueMutation.mutateAsync(row.id), {
      loading: { title: 'Re-queuing…' },
      success: () => ({ title: `${row.businessName} queued for the next run` }),
      error: (e) => ({ title: 'Requeue failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const items = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const facets: Facets = data?.facets ?? { status: [] }
  const totalCount = data?.totalCount ?? 0
  const submittedCount = data?.submittedCount ?? 0
  const keyReceivedCount = data?.keyReceivedCount ?? 0
  const failedCount = data?.failedCount ?? 0
  const pendingCount = data?.pendingCount ?? 0

  const hasActiveFilters = !!searchInput || statuses.length > 0
  const resetFilters = () => { setSearchInput(''); setStatuses([]) }
  const statusOptions: FacetOption[] = facets.status.map((f) => ({ value: f.value, label: statusLabel(f.value), count: f.count }))

  const busy = submitMutation.isPending || requeueMutation.isPending
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
            <div className="text-sm font-medium text-zinc-900 truncate">{r.businessName}</div>
            <div className="hidden sm:block text-xxs font-mono tabular-nums text-zinc-500">{r.abn}</div>
            <div className="hidden sm:block text-xxs font-mono text-zinc-400 truncate">{r.contactName || '—'}{r.ontraportContactId ? ` · #${r.ontraportContactId}` : ''}</div>
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
            {r.errorMessage ? (
              <button
                type="button"
                onClick={(e) => { e.stopPropagation(); setDetail(r) }}
                className="mt-1 block text-left text-xxs font-mono text-red-700 truncate max-w-[12rem] hover:underline"
                title="Show details"
              >
                {r.errorMessage}
              </button>
            ) : r.status === 'Failed' ? null : r.status === 'Submitted' && r.submittedAt ? (
              <div className="mt-1 text-xxs font-mono text-zinc-500">sent {relativeTime(r.submittedAt)}</div>
            ) : null}
          </div>
        )
      },
    },
    {
      id: 'reference',
      accessorFn: (r: Row) => r.asicReferenceNumber ?? '',
      header: 'ASIC ref',
      meta: { className: 'font-mono tabular-nums text-sm' },
      cell: ({ row }) => row.original.asicReferenceNumber
        ? <span className="text-zinc-900">{row.original.asicReferenceNumber}</span>
        : <span className="text-zinc-400">—</span>,
    },
    {
      id: 'asicKey',
      accessorFn: (r: Row) => r.asicKey ?? '',
      header: 'ASIC key',
      meta: { className: 'font-mono tabular-nums text-sm' },
      cell: ({ row }) => {
        const r = row.original
        if (!r.asicKey) return <span className="text-zinc-400">—</span>
        return (
          <div>
            <Link to="/admin/asic-keys" className="text-zinc-900 hover:underline">{r.asicKey}</Link>
            {r.keyReceivedAt ? <div className="text-xxs font-mono text-zinc-500">{relativeTime(r.keyReceivedAt)}</div> : null}
          </div>
        )
      },
    },
    {
      accessorKey: 'createdAt',
      header: 'Created',
      cell: ({ row }) => {
        const r = row.original
        return (
          <div>
            <div className="text-xs text-zinc-700 tabular-nums">{relativeTime(r.createdAt)}</div>
            <div className="text-xxs font-mono tabular-nums text-zinc-400">
              {r.source.toLowerCase()} · {r.attemptCount} attempt{r.attemptCount === 1 ? '' : 's'}{r.captchaSolves > 0 ? ` · ${r.captchaSolves} captcha` : ''}
            </div>
          </div>
        )
      },
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      meta: { className: 'text-right whitespace-nowrap', headerClassName: 'text-right' },
      cell: ({ row }) => {
        const r = row.original
        const canSubmit = r.status === 'Pending' || r.status === 'Failed'
        return (
          <div className="flex items-center justify-end gap-1">
            {r.status === 'Failed' ? (
              <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" disabled={busy} onClick={(e) => { e.stopPropagation(); requeue(r) }} title="Back to Pending for the next scheduled run">
                <RotateCw className="h-3.5 w-3.5" /> Requeue
              </Button>
            ) : null}
            {canSubmit ? (
              <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" disabled={busy || running} onClick={(e) => { e.stopPropagation(); submit(r) }} title="Submit to ASIC now (buys a captcha token)">
                <Send className="h-3.5 w-3.5" /> Submit now
              </Button>
            ) : null}
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [busy, running])

  const lastRunTone: Tone = stats.lastRunState === 'Failed' ? 'red' : stats.lastRunState === 'Succeeded' ? 'emerald' : 'zinc'

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="PIPELINE"
        title="ASIC key requests"
        subtitle="After each Ontraport sale, Renewtron asks ASIC (via its online enquiry form) to email the business name's ASIC key to the scanned inbox. Runs every 30 minutes."
        right={
          <>
            <button
              onClick={run}
              disabled={running}
              className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-brand-600 text-white px-3 py-2 text-sm font-medium hover:bg-brand-700 shadow-sm disabled:opacity-50 transition"
              title={pendingCount > 0 ? `Submit the ${pendingCount} pending request(s) now` : 'Match arrived keys and submit anything pending'}
            >
              {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
              {running ? 'Running…' : pendingCount > 0 ? `Send ${pendingCount} pending` : 'Run now'}
            </button>
            {failedCount > 0 ? (
              <button
                onClick={retryAllFailed}
                disabled={running}
                className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-amber-600 text-white px-3 py-2 text-sm font-medium hover:bg-amber-700 shadow-sm disabled:opacity-50 transition"
                title="Re-queue every failed request and run"
              >
                <RotateCw className="h-4 w-4" />
                {`Retry ${failedCount} failed`}
              </button>
            ) : null}
            <RefreshButton onClick={() => void refetch()} busy={isFetching || running} />
          </>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatTile
          kicker="WAITING"
          label="Awaiting key from ASIC"
          value={submittedCount.toLocaleString()}
          sub={pendingCount > 0 ? `${pendingCount.toLocaleString()} not yet sent` : `${totalCount.toLocaleString()} requests total`}
          tone={submittedCount > 0 ? 'indigo' : 'zinc'}
        />
        <StatTile
          kicker="DONE"
          label="Keys received"
          value={keyReceivedCount.toLocaleString()}
          sub={stats.avgTurnaroundHours != null ? `ASIC turnaround ~${formatHours(stats.avgTurnaroundHours)}` : 'no turnaround data yet'}
          tone="emerald"
        />
        <StatTile
          kicker="STATUS"
          label="Failed"
          value={failedCount.toLocaleString()}
          sub={failedCount > 0 ? 'retry, or check the error' : `${stats.captchaSolves.toLocaleString()} captcha solves so far`}
          tone={failedCount > 0 ? 'red' : 'zinc'}
        />
        <SparklineTile
          kicker="14D"
          label={stats.lastRunAt ? `last run ${relativeTime(stats.lastRunAt)}` : 'no run yet'}
          value={stats.today.toLocaleString()}
          sub={stats.lastRunState === 'Failed'
            ? (stats.lastRunError ?? 'last run failed')
            : stats.nextRunAt ? `next ${relativeTime(stats.nextRunAt)} · ${stats.yesterday.toLocaleString()} yesterday` : `${stats.yesterday.toLocaleString()} yesterday`}
          deltaPct={stats.deltaPct}
          data={stats.daily14d.map((d) => d.count)}
        />
      </div>
      {stats.lastRunState === 'Failed' && stats.lastRunError ? (
        <div className={`mt-3 rounded-md px-3 py-2 text-xs font-mono ring-1 ${lastRunTone === 'red' ? 'bg-red-50 ring-red-100 text-red-800' : 'bg-zinc-50 ring-zinc-200 text-zinc-700'}`}>
          Last scheduled run failed: {stats.lastRunError}
        </div>
      ) : null}

      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search business, ABN, contact, ASIC ref…"
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
            message={hasActiveFilters ? undefined : 'Enable requests under Settings → ASIC key requests. New sales queue automatically, or use “Request key” on an Ontraport sale.'}
          />}
        />
      </div>

      <DetailDialog row={detail} onClose={() => setDetail(null)} />
    </div>
  )
}

function formatHours(h: number) {
  if (h < 48) return `${Math.round(h)}h`
  return `${(h / 24).toFixed(1)}d`
}

function defaultStats(): Stats {
  return {
    lastRunAt: null, lastRunState: null, lastRunError: null, nextRunAt: null, runCron: null,
    avgTurnaroundHours: null, captchaSolves: 0, today: 0, yesterday: 0, deltaPct: null, daily14d: [],
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
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">KEY REQUEST</div>
              <DialogTitle>{row.businessName}</DialogTitle>
            </DialogHeader>
            <DialogBody className="space-y-3">
              <div className="flex flex-wrap items-center gap-2">
                <RequestStatusPill status={row.status} />
                <span className="text-xs text-zinc-500">
                  created {fmtDateTime(row.createdAt)}{row.processedAt ? ` · last attempt ${fmtDateTime(row.processedAt)}` : ''} · {row.attemptCount} attempt{row.attemptCount === 1 ? '' : 's'}
                </span>
              </div>

              <dl className="grid grid-cols-[7rem_1fr] gap-y-1.5 text-sm">
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">ASIC ref</dt>
                <dd className="font-mono tabular-nums text-zinc-900">{row.asicReferenceNumber ?? '—'}{row.submittedAt ? <span className="text-zinc-500 font-sans"> · submitted {fmtDateTime(row.submittedAt)}</span> : null}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">ASIC key</dt>
                <dd className="font-mono tabular-nums text-zinc-900">{row.asicKey ?? '—'}{row.keyReceivedAt ? <span className="text-zinc-500 font-sans"> · received {fmtDateTime(row.keyReceivedAt)}</span> : null}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">ABN</dt>
                <dd className="font-mono tabular-nums text-zinc-700">{row.abn}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Contact</dt>
                <dd className="text-zinc-700">{row.contactName || '—'}{row.phone ? ` · ${row.phone}` : ''}{row.ontraportContactId ? ` · Ontraport #${row.ontraportContactId}` : ''}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Source</dt>
                <dd className="text-zinc-700">{row.source}{row.captchaSolves > 0 ? ` · ${row.captchaSolves} captcha solve${row.captchaSolves === 1 ? '' : 's'}` : ''}</dd>
                {row.ontraportSaleId ? (
                  <>
                    <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Sale</dt>
                    <dd><Link to="/admin/ontraport-sales" className="text-brand-700 hover:underline">view in Ontraport sales</Link></dd>
                  </>
                ) : null}
              </dl>

              {row.errorMessage ? (
                <div className="rounded-md bg-red-50 ring-1 ring-red-100 px-3 py-2 text-xs font-mono text-red-800 whitespace-pre-wrap break-words">
                  {row.errorMessage}
                  {row.status === 'Failed' ? <div className="mt-1 text-red-600">{row.canAutoRetry ? 'Will be retried automatically.' : 'Needs attention — not retried automatically.'}</div> : null}
                </div>
              ) : null}

              {row.question ? (
                <div>
                  <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 mb-1">Enquiry text sent to ASIC</div>
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
