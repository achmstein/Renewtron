import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ExternalLink, Inbox, Loader2, RotateCw, UserPlus, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type AsicKeyJobStatus, type AsicKeyNotificationListItem } from '../api/client'
import { ClientLink, Pagination, useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, type Tone } from './_ui'
import { Status, statusLabel as vocabLabel } from './_status'
import { fmtDateTime, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Dialog, DialogBody, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

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
      meta: { sticky: true, className: 'min-w-[7rem] max-w-[38vw] sm:min-w-[14rem] sm:max-w-[18rem]' },
      cell: ({ row }) => {
        const r = row.original
        return (
          <div>
            <Link to={`/admin/asic-keys/${r.id}`} onClick={(e) => e.stopPropagation()} className={`block text-sm font-medium truncate hover:text-link hover:underline ${r.businessName ? 'text-foreground' : 'text-muted-foreground/70 italic'}`}>
              {r.businessName ?? 'name not parsed'}
            </Link>
            {r.abn ? <div className="hidden sm:block text-xxs font-mono tabular-nums text-muted-foreground">{r.abn}</div> : null}
            <div className="hidden sm:block text-xxs font-mono text-muted-foreground/70 truncate" title={r.subject ?? ''}>{r.subject ?? '—'}</div>
          </div>
        )
      },
    },
    {
      id: 'asicKey',
      accessorFn: (r: Row) => r.asicKey ?? '',
      header: 'ASIC key',
      meta: { className: 'font-mono tabular-nums text-sm' },
      cell: ({ row }) => row.original.asicKey
        ? <span className="text-foreground">{row.original.asicKey}</span>
        : <span className="text-muted-foreground/70">—</span>,
    },
    {
      id: 'ontraport',
      accessorFn: (r: Row) => r.ontraportContactIds ?? '',
      header: 'Ontraport',
      cell: ({ row }) => {
        const r = row.original
        if (!r.ontraportContactIds) return <span className="text-muted-foreground/70">—</span>
        return (
          <div>
            <div className="text-xs font-mono text-foreground truncate max-w-[10rem]" title={r.ontraportContactIds}>{r.ontraportContactIds}</div>
            <div className="text-xxs font-mono tabular-nums text-muted-foreground">{r.ontraportContactsUpdated} updated</div>
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
              <button
                type="button"
                onClick={(e) => { e.stopPropagation(); setDetail(r) }}
                className="mt-1 block text-left text-xxs font-mono text-destructive truncate max-w-[12rem] hover:underline"
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
      accessorKey: 'receivedAt',
      header: 'Received',
      cell: ({ row }) => (
        <div>
          <div className="text-xs text-foreground tabular-nums">{relativeTime(row.original.receivedAt)}</div>
          <div className="text-xxs font-mono tabular-nums text-muted-foreground/70">
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
          <div className="flex items-center justify-end gap-1">
            {canAssign ? (
              <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={(e) => { e.stopPropagation(); setAssign(r) }} title="Write this key onto a contact you pick">
                <UserPlus className="h-3.5 w-3.5" /> Assign
              </Button>
            ) : null}
            {!done ? (
              <Button
                variant="ghost" size="sm" className="h-7 px-2 text-xs"
                disabled={retryMutation.isPending}
                onClick={(e) => { e.stopPropagation(); retry(r) }}
              >
                <RotateCw className="h-3.5 w-3.5" /> Retry
              </Button>
            ) : null}
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [retryMutation.isPending])

  const lastScanTone: Tone = stats.lastScanState === 'Failed' ? 'red' : stats.lastScanState === 'Succeeded' ? 'emerald' : 'zinc'

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker={toSort ? 'WORK' : 'RECORDS'}
        title={toSort ? 'Keys to sort' : 'ASIC key letters'}
        subtitle={toSort
          ? 'Key letters we couldn’t read or place. Retry them, or choose the client a key belongs to.'
          : 'ASIC “Notification request” emails → linked PDF → ASIC key → written onto the Ontraport contact. Scans every 15 minutes.'}
        right={
          <>
            <button
              onClick={scan}
              disabled={scanning}
              className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-primary text-white px-3 py-2 text-sm font-medium hover:bg-primary shadow-sm disabled:opacity-50 transition"
            >
              {scanning ? <Loader2 className="h-4 w-4 animate-spin" /> : <Inbox className="h-4 w-4" />}
              {scanning ? 'Scanning…' : 'Scan inbox now'}
            </button>
            {attentionCount > 0 ? (
              <button
                onClick={retryAllFailed}
                disabled={scanning}
                className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-amber-600 text-white px-3 py-2 text-sm font-medium hover:bg-amber-700 shadow-sm disabled:opacity-50 transition"
                title="Re-run every non-completed notification through the pipeline"
              >
                <RotateCw className="h-4 w-4" />
                {`Retry ${attentionCount} failed`}
              </button>
            ) : null}
            <RefreshButton onClick={() => void refetch()} busy={isFetching || scanning} />
          </>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatTile
          kicker="DONE"
          label="Keys written"
          value={completedCount.toLocaleString()}
          sub={`${data?.savedToClientCount ? `+${data.savedToClientCount.toLocaleString()} saved to client · ` : ''}${totalCount.toLocaleString()} notifications total`}
          tone="emerald"
        />
        <StatTile
          kicker="STATUS"
          label="Needs attention"
          value={attentionCount.toLocaleString()}
          sub={attentionCount > 0 ? 'retry, or assign a contact' : 'all clear'}
          tone={attentionCount > 0 ? 'red' : 'zinc'}
        />
        <StatTile
          kicker="SCAN"
          label="Last scan"
          value={stats.lastScanAt ? relativeTime(stats.lastScanAt) : '—'}
          sub={stats.lastScanState === 'Failed'
            ? (stats.lastScanError ?? 'last run failed')
            : stats.nextScanAt ? `next ${relativeTime(stats.nextScanAt)}` : 'not scheduled'}
          tone={lastScanTone}
        />
        <SparklineTile
          kicker="14D"
          label="Received today"
          value={stats.today.toLocaleString()}
          sub={`${stats.yesterday.toLocaleString()} yesterday`}
          deltaPct={stats.deltaPct}
          data={stats.daily14d.map((d) => d.count)}
        />
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search business, ABN, key, contact…"
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
          loading={isFetching && isPlaceholderData}
          empty={<EmptyState
            title={hasActiveFilters ? 'No notifications match the current filters.' : 'No ASIC key notifications yet.'}
            message={hasActiveFilters ? undefined : 'Configure the inbox under Settings → ASIC key inbox, then run a scan.'}
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
              <div className="text-xs font-medium text-muted-foreground">NOTIFICATION</div>
              <DialogTitle>{row.businessName ?? 'Business name not parsed'}</DialogTitle>
            </DialogHeader>
            <DialogBody className="space-y-3">
              <ClientLink kind="keyLetter" id={row.id} />
              <div className="flex flex-wrap items-center gap-2">
                <KeyStatusPill status={row.status} />
                <span className="text-xs text-muted-foreground">
                  received {fmtDateTime(row.receivedAt)}{row.processedAt ? ` · last attempt ${fmtDateTime(row.processedAt)}` : ''} · {row.attemptCount} attempt{row.attemptCount === 1 ? '' : 's'}
                </span>
              </div>

              <dl className="grid grid-cols-[7rem_1fr] gap-y-1.5 text-sm">
                <dt className="text-xs text-muted-foreground pt-0.5">ASIC key</dt>
                <dd className="font-mono tabular-nums text-foreground">{row.asicKey ?? '—'}</dd>
                <dt className="text-xs text-muted-foreground pt-0.5">ABN</dt>
                <dd className="font-mono tabular-nums text-foreground">{row.abn ?? '—'}</dd>
                <dt className="text-xs text-muted-foreground pt-0.5">Contacts</dt>
                <dd className="font-mono text-foreground break-all">{row.ontraportContactIds ?? '—'}{row.ontraportContactIds ? ` (${row.ontraportContactsUpdated} updated)` : ''}</dd>
                <dt className="text-xs text-muted-foreground pt-0.5">From</dt>
                <dd className="text-foreground break-all">{row.from ?? '—'}</dd>
                <dt className="text-xs text-muted-foreground pt-0.5">Subject</dt>
                <dd className="text-foreground break-words">{row.subject ?? '—'}</dd>
                <dt className="text-xs text-muted-foreground pt-0.5">PDF link</dt>
                <dd className="min-w-0">
                  {row.downloadUrl ? (
                    <a href={row.downloadUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-link hover:underline break-all">
                      open <ExternalLink className="h-3 w-3 shrink-0" />
                    </a>
                  ) : '—'}
                </dd>
              </dl>

              {row.errorMessage ? (
                <div className="rounded-md bg-destructive/5 ring-1 ring-destructive/20 px-3 py-2 text-xs font-mono text-destructive whitespace-pre-wrap break-words">{row.errorMessage}</div>
              ) : null}

              {isFetching && !full ? (
                <div className="flex items-center gap-2 text-xs text-muted-foreground"><Loader2 className="h-3 w-3 animate-spin" /> Loading the PDF details…</div>
              ) : null}

              {row.pdfTextExcerpt ? (
                <div>
                  <div className="text-xs text-muted-foreground mb-1">PDF text (first 2,000 chars)</div>
                  <pre className="max-h-48 overflow-auto rounded-md bg-muted/50 ring-1 ring-border px-3 py-2 text-xxs font-mono text-foreground whitespace-pre-wrap break-words">{row.pdfTextExcerpt}</pre>
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
          <div className="text-xs font-medium text-warning">MANUAL MATCH</div>
          <DialogTitle>Assign to an Ontraport contact</DialogTitle>
        </DialogHeader>
        <DialogBody className="space-y-3">
          <p className="text-sm text-foreground">
            No contact was found with business name <span className="font-medium">{row?.businessName ?? '—'}</span>.
            Enter the Ontraport contact ID to write key <span className="font-mono">{row?.asicKey}</span> onto it.
          </p>
          <div>
            <Label htmlFor="assign-contact" className="text-xs font-medium text-muted-foreground">Contact ID</Label>
            <Input
              id="assign-contact"
              value={contactId}
              onChange={(e) => setContactId(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') apply() }}
              placeholder="e.g. 12345"
              className="mt-1 h-9 font-mono tabular-nums"
              autoFocus
            />
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" onClick={() => { onClose(); setContactId('') }} disabled={mutation.isPending}>Cancel</Button>
          <Button onClick={apply} disabled={mutation.isPending || !contactId.trim()} className="bg-amber-600 hover:bg-amber-700">
            {mutation.isPending ? 'Writing…' : 'Write key'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
