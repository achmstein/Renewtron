import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ExternalLink, Inbox, Loader2, RotateCw, TriangleAlert, UserPlus, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type AsicKeyJobStatus, type AsicKeyNotificationListItem } from '../api/client'
import { ClientLink, Pagination, useDebouncedValue } from './_components'
import { Cell, EmptyState, PageHeader, RefreshButton } from './_ui'
import { Status, statusLabel as vocabLabel } from './_status'
import { fmtDateTime, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { TrendChart } from '@/components/trend-chart'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

type KeysResponse = Awaited<ReturnType<typeof api.admin.asicKeys>>
type Row = AsicKeyNotificationListItem
type Stats = KeysResponse['stats']

const PAGE_SIZE = 25
type Facets = KeysResponse['facets']

const statusLabel = (s: string) => vocabLabel('keyLetter', s)

/** The "Keys to sort" queue (?view=to-sort): letters a person has to deal with. Keep in step with WorkQueues.KeyLetterSettled. */
const TO_SORT = ['LinkNotFound', 'DownloadFailed', 'KeyNotFound', 'ContactNotFound', 'OntraportUpdateFailed']

export default function AsicKeys() {
  const [searchParams] = useSearchParams()
  const toSort = searchParams.get('view') === 'to-sort'
  const [statuses, setStatusesRaw] = useState<string[]>(() => (toSort ? TO_SORT : []))
  const [searchInput, setSearchInputRaw] = useState('')
  const [page, setPage] = useState(1)
  // The sidebar links "Keys to sort" and "Key letters" to this same page: switch the filter with it.
  const [shownView, setShownView] = useState(toSort)
  if (shownView !== toSort) { setShownView(toSort); setStatusesRaw(toSort ? TO_SORT : []); setPage(1) }
  const setStatuses = (v: string[]) => { setPage(1); setStatusesRaw(v) }
  const setSearchInput = (v: string) => { setPage(1); setSearchInputRaw(v) }
  const search = useDebouncedValue(searchInput, 300)
  const [detail, setDetail] = useState<Row | null>(null)
  const [assign, setAssign] = useState<Row | null>(null)

  const queryClient = useQueryClient()
  const { data, isFetching, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-asic-keys', { statuses, search, page }],
    queryFn: () => api.admin.asicKeys({ status: statuses.join(',') || undefined, search: search || undefined, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-asic-keys'] })

  // Scans run as Hangfire jobs (a PDF backlog can outlast a proxy timeout). While one is
  // in flight we poll its state every 2s and refresh the table on every tick so rows
  // land progressively; the toast resolves when the job does.
  const [jobId, setJobId] = useState<string | null>(null)
  const jobQuery = useQuery({
    queryKey: ['admin-asic-keys-job', jobId],
    queryFn: () => api.admin.asicKeyJob(jobId!),
    enabled: !!jobId,
    refetchInterval: (q) => (q.state.data?.done ? false : 2000),
  })
  const jobDone = useRef<((s: AsicKeyJobStatus) => void) | null>(null)
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
    const finished = new Promise<AsicKeyJobStatus>((resolve, reject) => {
      start.then((r) => { jobDone.current = resolve; setJobId(r.jobId) }).catch(reject)
    })
    void sileo.promise(finished, {
      loading: { title: loadingTitle },
      success: (s) => s.state === 'Failed'
        ? { title: 'Scan failed', description: s.error ?? 'See the Hangfire dashboard.' }
        : { title: s.result?.skipped ? 'Scan skipped' : 'Scan complete', description: s.result?.message ?? 'Table refreshed.' },
      error: (e) => ({ title: 'Could not start scan', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }
  const scanning = !!jobId
  const scan = () => trackJob(api.admin.scanAsicKeys(), 'Scanning inbox…')
  const retryAllFailed = () => trackJob(api.admin.retryFailedAsicKeys(), 'Re-running failed notifications…')

  const retryMutation = useMutation({
    mutationFn: (id: string) => api.admin.retryAsicKey(id),
    onSettled: () => void invalidate(),
  })
  const retry = (row: Row) => {
    void sileo.promise(retryMutation.mutateAsync(row.id), {
      loading: { title: `Retrying ${row.businessName ?? 'notification'}…` },
      success: (r) => r.status === 'Completed'
        ? { title: `Key ${r.asicKey} written to contact ${r.ontraportContactIds}` }
        : { title: `Still ${statusLabel(r.status).toLowerCase()}`, description: r.errorMessage ?? undefined },
      error: (e) => ({ title: 'Retry failed', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const items = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const facets: Facets = data?.facets ?? { status: [] }
  const attentionCount = data?.attentionCount ?? 0
  const completedCount = data?.completedCount ?? 0
  const totalCount = data?.totalCount ?? 0

  const hasActiveFilters = !!searchInput || statuses.length > 0
  const resetFilters = () => { setSearchInput(''); setStatuses([]) }
  const statusOptions: FacetOption[] = facets.status.map((f) => ({ value: f.value, label: statusLabel(f.value), count: f.count }))

  const columns = useMemo<DataTableColumn<Row>[]>(() => [
    {
      id: 'business',
      accessorFn: (r: Row) => r.businessName ?? '',
      header: 'Business',
      meta: { sticky: true, className: 'min-w-28 max-w-[38vw] sm:min-w-56 sm:max-w-72' },
      cell: ({ row }) => {
        const r = row.original
        return (
          <div className="min-w-0">
            <Link to={`/admin/asic-keys/${r.id}`} onClick={(e) => e.stopPropagation()} className={cn('block truncate text-sm font-medium hover:text-link hover:underline', !r.businessName && 'text-muted-foreground')}>
              {r.businessName ?? 'Name not read'}
            </Link>
            <div className="hidden truncate text-caption text-muted-foreground sm:block" title={r.subject ?? ''}>
              {r.abn ? <span className="font-mono tabular-nums">{r.abn}</span> : null}
              {r.abn && r.subject ? ' · ' : null}
              {r.subject}
            </div>
          </div>
        )
      },
    },
    {
      id: 'asicKey',
      accessorFn: (r: Row) => r.asicKey ?? '',
      header: 'ASIC key',
      meta: { className: 'font-mono tabular-nums text-sm' },
      cell: ({ row }) => row.original.asicKey ?? <span className="text-muted-foreground">—</span>,
    },
    {
      id: 'ontraport',
      accessorFn: (r: Row) => r.ontraportContactIds ?? '',
      header: 'Ontraport',
      cell: ({ row }) => {
        const r = row.original
        if (!r.ontraportContactIds) return <span className="text-muted-foreground">—</span>
        return (
          <div>
            <div className="max-w-40 truncate font-mono text-sm" title={r.ontraportContactIds}>{r.ontraportContactIds}</div>
            <div className="text-caption text-muted-foreground tabular-nums">{r.ontraportContactsUpdated} updated</div>
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
            <KeyStatusPill status={r.status} />
            {r.errorMessage ? (
              <div className="mt-1 max-w-48 truncate text-caption text-muted-foreground" title={r.errorMessage}>{r.errorMessage}</div>
            ) : null}
          </div>
        )
      },
    },
    {
      accessorKey: 'receivedAt',
      header: 'Received',
      cell: ({ row }) => (
        <div className="tabular-nums">
          <div className="text-sm">{relativeTime(row.original.receivedAt)}</div>
          <div className="text-caption text-muted-foreground">
            {row.original.attemptCount} attempt{row.original.attemptCount === 1 ? '' : 's'}
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
        // A key saved on the client's name is done; it can still be pushed onto a contact by hand.
        const done = r.status === 'Completed' || r.status === 'SavedToClient'
        const canAssign = !!r.asicKey && r.status !== 'Completed'
        return (
          <div className="flex items-center justify-end gap-1.5">
            {canAssign ? (
              <Button variant="outline" size="sm" onClick={(e) => { e.stopPropagation(); setAssign(r) }} title="Write this key onto a contact you pick">
                <UserPlus /> Assign
              </Button>
            ) : null}
            {!done ? (
              <Button
                variant="outline" size="sm"
                disabled={retryMutation.isPending}
                onClick={(e) => { e.stopPropagation(); retry(r) }}
              >
                <RotateCw /> Retry
              </Button>
            ) : null}
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [retryMutation.isPending])

  const scanFailed = stats.lastScanState === 'Failed'

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        title={toSort ? 'Keys to sort' : 'ASIC key letters'}
        subtitle={toSort ? 'Key letters we couldn’t read or place.' : 'Keys from ASIC’s emails, written onto the client.'}
        right={
          <>
            {attentionCount > 0 ? (
              <Button variant="outline" size="sm" onClick={retryAllFailed} disabled={scanning} title="Re-run every letter that isn’t done">
                <RotateCw />
                {`Retry ${attentionCount} failed`}
              </Button>
            ) : null}
            <Button size="sm" onClick={scan} disabled={scanning} title="Also runs every 15 minutes">
              {scanning ? <Loader2 className="animate-spin" /> : <Inbox />}
              {scanning ? 'Scanning…' : 'Scan inbox'}
            </Button>
            <RefreshButton onClick={() => void refetch()} busy={isFetching || scanning} />
          </>
        }
      />

      <Card className="grid gap-0 divide-y py-0 sm:grid-cols-4 sm:divide-x sm:divide-y-0">
        <Stat
          label="Keys written"
          value={completedCount.toLocaleString()}
          sub={`${data?.savedToClientCount ? `+${data.savedToClientCount.toLocaleString()} saved to client · ` : ''}${totalCount.toLocaleString()} letters`}
        />
        <Stat
          label="Needs attention"
          value={attentionCount.toLocaleString()}
          sub={attentionCount > 0 ? 'Retry, or assign a contact' : 'All clear'}
          dot={attentionCount > 0 ? 'bg-destructive' : undefined}
        />
        <Stat
          label="Last scan"
          value={stats.lastScanAt ? relativeTime(stats.lastScanAt) : '—'}
          sub={scanFailed
            ? (stats.lastScanError ?? 'Last run failed')
            : stats.nextScanAt ? `Next ${relativeTime(stats.nextScanAt)}` : 'Not scheduled'}
          dot={scanFailed ? 'bg-destructive' : undefined}
        />
        <Stat
          label="Received today"
          value={stats.today.toLocaleString()}
          sub={`${stats.yesterday.toLocaleString()} yesterday · last 14 days`}
          trend={stats.daily14d}
          trendLabel="letters"
        />
      </Card>

      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search business, ABN, key, contact…"
          className="h-8 w-64"
        />
        <FacetedFilter title="Status" options={statusOptions} selected={statuses} onChange={setStatuses} />
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
          onRowClick={(r) => setDetail(r.original)}
          loading={isFetching && isPlaceholderData}
          empty={<EmptyState
            title={hasActiveFilters ? (toSort ? 'All clear' : 'Nothing matches these filters') : 'No key letters yet'}
            message={hasActiveFilters ? undefined : 'Set up the inbox in Settings, then scan.'}
          />}
        />
        {(data?.filteredCount ?? 0) > PAGE_SIZE ? (
          <Pagination page={page} pageSize={PAGE_SIZE} total={data?.filteredCount ?? 0} onPage={setPage} />
        ) : null}
      </div>

      <DetailDialog row={detail} onClose={() => setDetail(null)} />
      <AssignDialog row={assign} onClose={() => setAssign(null)} onDone={() => void invalidate()} />
    </div>
  )
}

/** One number in the page's stat strip: black, a muted label, one line under it, an optional trend. */
function Stat({ label, value, sub, dot, trend, trendLabel }: {
  label: string
  value: string
  sub?: string
  /** A status dot beside the label, e.g. bg-destructive. */
  dot?: string
  trend?: Array<{ date?: string; count: number }>
  trendLabel?: string
}) {
  return (
    <div className="flex min-w-0 flex-col px-5 py-4">
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        {dot ? <span aria-hidden className={cn('size-2 rounded-full', dot)} /> : null}
        {label}
      </div>
      <div className="mt-1 text-2xl font-semibold tracking-tight tabular-nums">{value}</div>
      {sub ? <div className="truncate text-caption text-muted-foreground" title={sub}>{sub}</div> : null}
      {trend?.length ? <div className="mt-2 h-8"><TrendChart data={trend} valueLabel={trendLabel} /></div> : null}
    </div>
  )
}

function defaultStats(): Stats {
  return {
    lastScanAt: null, lastScanState: null, lastScanError: null, nextScanAt: null, scanCron: null,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [],
  }
}

function KeyStatusPill({ status }: { status: string }) {
  return <Status kind="keyLetter" status={status} />
}

function DetailDialog({ row: listRow, onClose }: { row: Row | null; onClose: () => void }) {
  // The list row shows straight away; the PDF text, link and sender arrive a moment later.
  const { data: full, isFetching } = useQuery({
    queryKey: ['admin-asic-key', listRow?.id],
    queryFn: () => api.admin.asicKey(listRow!.id),
    enabled: !!listRow,
  })
  const row = listRow ? { ...listRow, from: null, downloadUrl: null, pdfTextExcerpt: null, ...(full?.id === listRow.id ? full : {}) } : null
  return (
    <Dialog open={!!row} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
        {row ? (
          <>
            <DialogHeader>
              <DialogTitle>{row.businessName ?? 'Name not read'}</DialogTitle>
              <DialogDescription>
                Received {fmtDateTime(row.receivedAt)}{row.processedAt ? ` · last try ${fmtDateTime(row.processedAt)}` : ''} · {row.attemptCount} attempt{row.attemptCount === 1 ? '' : 's'}
              </DialogDescription>
            </DialogHeader>
            <DialogBody className="space-y-4">
              <div className="flex flex-wrap items-center gap-3">
                <KeyStatusPill status={row.status} />
                <ClientLink kind="keyLetter" id={row.id} />
              </div>

              <dl className="grid grid-cols-2 gap-x-6 gap-y-3">
                <Cell label="ASIC key" value={row.asicKey ?? '—'} mono />
                <Cell label="ABN" value={row.abn ?? '—'} mono />
                <Cell label="Contacts" value={<span className="break-all">{row.ontraportContactIds ?? '—'}{row.ontraportContactIds ? ` (${row.ontraportContactsUpdated} updated)` : ''}</span>} mono />
                <Cell label="From" value={<span className="break-all">{row.from ?? '—'}</span>} />
                <Cell label="Subject" value={<span className="break-words">{row.subject ?? '—'}</span>} />
                <Cell label="PDF" value={row.downloadUrl ? (
                  <a href={row.downloadUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-link hover:underline">
                    Open <ExternalLink className="size-3 shrink-0" />
                  </a>
                ) : '—'} />
              </dl>

              {row.errorMessage ? (
                <Alert variant="destructive">
                  <TriangleAlert />
                  <AlertDescription className="whitespace-pre-wrap break-words">{row.errorMessage}</AlertDescription>
                </Alert>
              ) : null}

              {isFetching && !full ? (
                <div className="flex items-center gap-2 text-caption text-muted-foreground"><Loader2 className="size-3 animate-spin" /> Loading the PDF…</div>
              ) : null}

              {row.pdfTextExcerpt ? (
                <div className="grid gap-1.5">
                  <div className="text-caption text-muted-foreground">PDF text</div>
                  <pre className="max-h-48 overflow-auto rounded-md border bg-muted/50 px-3 py-2 font-sans text-caption whitespace-pre-wrap break-words">{row.pdfTextExcerpt}</pre>
                </div>
              ) : null}
            </DialogBody>
            <DialogFooter>
              <Button variant="outline" onClick={onClose}>Close</Button>
            </DialogFooter>
          </>
        ) : null}
      </DialogContent>
    </Dialog>
  )
}

function AssignDialog({ row, onClose, onDone }: { row: Row | null; onClose: () => void; onDone: () => void }) {
  const [contactId, setContactId] = useState('')
  const mutation = useMutation({
    mutationFn: (input: { id: string; contactId: string }) => api.admin.applyAsicKey(input.id, input.contactId),
  })
  const apply = () => {
    if (!row || !contactId.trim()) return
    void sileo.promise(mutation.mutateAsync({ id: row.id, contactId: contactId.trim() }), {
      loading: { title: 'Writing key to Ontraport…' },
      success: (r) => r.status === 'Completed'
        ? { title: `Key ${r.asicKey} written to contact ${contactId.trim()}` }
        : { title: 'Ontraport rejected the write', description: r.errorMessage ?? undefined },
      error: (e) => ({ title: 'Assign failed', description: e instanceof Error ? e.message : undefined }),
    }).then(() => { onDone(); onClose(); setContactId('') }).catch(() => {})
  }

  return (
    <Dialog open={!!row} onOpenChange={(o) => { if (!o && !mutation.isPending) { onClose(); setContactId('') } }}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Assign to an Ontraport contact</DialogTitle>
          <DialogDescription>
            Write key <span className="font-mono">{row?.asicKey}</span> for {row?.businessName ?? 'this name'} onto a contact.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <div className="grid gap-1.5">
            <Label htmlFor="assign-contact">Contact ID</Label>
            <Input
              id="assign-contact"
              value={contactId}
              onChange={(e) => setContactId(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') apply() }}
              placeholder="e.g. 12345"
              className="font-mono tabular-nums"
              autoFocus
            />
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={() => { onClose(); setContactId('') }} disabled={mutation.isPending}>Cancel</Button>
          <Button onClick={apply} disabled={mutation.isPending || !contactId.trim()}>
            {mutation.isPending ? 'Writing…' : 'Write key'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
