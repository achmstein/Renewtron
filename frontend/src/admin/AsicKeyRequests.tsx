import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ClipboardCheck, Copy, ExternalLink, RotateCw, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type AsicKeyRequestDto } from '../api/client'
import { ClientLink, Pagination, useDebouncedValue } from './_components'
import { Cell, EmptyState, PageHeader, RefreshButton, Stat, StatStrip } from './_ui'
import { Status, statusLabel as vocabLabel } from './_status'
import { fmtDateTime, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

type RequestsResponse = Awaited<ReturnType<typeof api.admin.asicKeyRequests>>
type Row = AsicKeyRequestDto
type Stats = RequestsResponse['stats']
type Facets = RequestsResponse['facets']

const PAGE_SIZE = 25

const ASIC_FORM_URL = 'https://www.edge.asic.gov.au/008/inquiryV001?start/landingPage'

const statusLabel = (s: string) => vocabLabel('keyRequest', s)

export default function AsicKeyRequests() {
  const [statuses, setStatusesRaw] = useState<string[]>([])
  const [searchInput, setSearchInputRaw] = useState('')
  const [page, setPage] = useState(1)
  const setStatuses = (v: string[]) => { setPage(1); setStatusesRaw(v) }
  const setSearchInput = (v: string) => { setPage(1); setSearchInputRaw(v) }
  const search = useDebouncedValue(searchInput, 300)
  const [detail, setDetail] = useState<Row | null>(null)
  const [record, setRecord] = useState<Row | null>(null)

  const queryClient = useQueryClient()
  const { data, isFetching, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-asic-key-requests', { statuses, search, page }],
    queryFn: () => api.admin.asicKeyRequests({ status: statuses.join(',') || undefined, search: search || undefined, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  })
  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-asic-key-requests'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-sidebar-counts'] })
  }

  const runMutation = useMutation({ mutationFn: () => api.admin.runAsicKeyRequests(), onSettled: () => void invalidate() })
  const runNow = () => {
    void sileo.promise(runMutation.mutateAsync(), {
      loading: { title: 'Checking the inbox for arrived keys…' },
      success: (r) => ({ title: r.skipped ? 'Skipped' : 'Done', description: r.message }),
      error: (e) => ({ title: 'Could not run', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const requeueMutation = useMutation({
    mutationFn: (id: string) => api.admin.requeueAsicKeyRequest(id),
    onSettled: () => void invalidate(),
  })
  const requeue = (row: Row) => {
    void sileo.promise(requeueMutation.mutateAsync(row.id), {
      loading: { title: `Putting ${row.businessName} back on the list…` },
      success: () => ({ title: `${row.businessName} is back on the to-do list` }),
      error: (e) => ({ title: 'Could not re-queue', description: e instanceof Error ? e.message : undefined }),
    }).catch(() => {})
  }

  const items = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const facets: Facets = data?.facets ?? { status: [] }
  const pendingCount = data?.pendingCount ?? 0
  const manualCount = data?.manualCount ?? 0
  const submittedCount = data?.submittedCount ?? 0
  const keyReceivedCount = data?.keyReceivedCount ?? 0
  const failedCount = data?.failedCount ?? 0

  const hasActiveFilters = !!searchInput || statuses.length > 0
  const resetFilters = () => { setSearchInput(''); setStatuses([]) }
  const statusOptions: FacetOption[] = facets.status.map((f) => ({ value: f.value, label: statusLabel(f.value), count: f.count }))

  const columns = useMemo<DataTableColumn<Row>[]>(() => [
    {
      id: 'business',
      accessorFn: (r: Row) => r.businessName,
      header: 'Business',
      meta: { sticky: true, className: 'min-w-28 max-w-[38vw] sm:min-w-56 sm:max-w-72' },
      cell: ({ row }) => {
        const r = row.original
        return (
          <div className="min-w-0">
            <Link to={`/admin/asic-key-requests/${r.id}`} onClick={(e) => e.stopPropagation()} className="block truncate text-sm font-medium hover:text-link hover:underline">{r.businessName}</Link>
            <div className="hidden truncate text-caption text-muted-foreground sm:block" title={r.otherBusinessNames?.join('\n')}>
              <span className="font-mono tabular-nums">{r.abn}</span>
              {r.otherBusinessNames?.length
                ? ` · + ${r.otherBusinessNames.length === 1 ? r.otherBusinessNames[0] : `${r.otherBusinessNames.length} more names`}`
                : null}
            </div>
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
          <div className="max-w-48">
            <div className="truncate text-sm">{r.contactName}</div>
            <div className="truncate text-caption text-muted-foreground" title={r.email}>{r.email || '—'}</div>
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
              <div className="mt-1 text-caption text-muted-foreground">Ref <span className="font-mono tabular-nums">{r.asicReferenceNumber}</span></div>
            ) : null}
            {r.errorMessage ? (
              <div className="mt-1 max-w-56 truncate text-caption text-muted-foreground" title={r.errorMessage}>{r.errorMessage}</div>
            ) : null}
          </div>
        )
      },
    },
    {
      accessorKey: 'createdAt',
      header: 'Queued',
      cell: ({ row }) => (
        <div className="tabular-nums">
          <div className="text-sm">{relativeTime(row.original.createdAt)}</div>
          <div className="flex items-center gap-1.5 text-caption text-muted-foreground">
            {row.original.source.toLowerCase()}
            {row.original.isTest ? <Badge variant="outline">Test</Badge> : null}
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
          <div className="flex items-center justify-end gap-1.5">
            {r.status === 'Failed' || r.status === 'Manual' ? (
              <Button variant="outline" size="sm" disabled={requeueMutation.isPending} onClick={(e) => { e.stopPropagation(); requeue(r) }} title="Back on the to-do list" aria-label="Re-queue">
                <RotateCw /><span className="hidden sm:inline">Re-queue</span>
              </Button>
            ) : null}
            <Button variant="outline" size="sm" onClick={(e) => { e.stopPropagation(); setRecord(r) }} title="Fill in ASIC's form with these details and record the reference number">
              <ClipboardCheck /> {r.status === 'Submitted' ? 'Update' : 'Do it'}
            </Button>
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [requeueMutation.isPending])

  const toDo = pendingCount + manualCount

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <PageHeader
        title="ASIC key requests"
        subtitle="Ask ASIC to email a client’s key, then record the reference."
        right={
          <>
            <Button variant="outline" size="sm" onClick={runNow} disabled={runMutation.isPending} title="Match keys that have arrived in the inbox against sent requests">
              <RotateCw className={cn(runMutation.isPending && 'animate-spin')} />
              Match arrived keys
            </Button>
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      <StatStrip>
        <Stat
          label="To send"
          value={toDo.toLocaleString()}
          sub={manualCount > 0 ? `${manualCount} being sent now` : toDo > 0 ? 'Fill in ASIC’s form' : 'All clear'}
          dot={toDo > 0 ? 'action' : null}
        />
        <Stat label="Waiting for ASIC" value={submittedCount.toLocaleString()} sub="Matched when the key email lands" />
        <Stat
          label="Keys received"
          value={keyReceivedCount.toLocaleString()}
          sub={failedCount > 0 ? `${failedCount} not sent` : `${(data?.totalCount ?? 0).toLocaleString()} requests`}
        />
        <Stat
          label="Sent today"
          value={stats.today.toLocaleString()}
          sub={`${stats.yesterday.toLocaleString()} yesterday · last 14 days`}
          trend={stats.daily14d}
          trendLabel="requests"
        />
      </StatStrip>

      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search business, ABN, contact, reference…"
          className="h-8 w-full sm:w-64"
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
            title={hasActiveFilters ? 'Nothing matches these filters' : 'No key requests yet'}
            message={hasActiveFilters ? undefined : 'Turn them on in Settings, or use “Request key” on a sale.'}
          />}
        />
        {(data?.filteredCount ?? 0) > PAGE_SIZE ? (
          <Pagination page={page} pageSize={PAGE_SIZE} total={data?.filteredCount ?? 0} onPage={setPage} />
        ) : null}
      </div>

      <DetailDialog row={detail} onClose={() => setDetail(null)} />
      <RecordDialog row={record} onClose={() => setRecord(null)} onDone={() => void invalidate()} />
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
  return <Status kind="keyRequest" status={status} />
}

function copyText(value: string) {
  void navigator.clipboard?.writeText(value).then(
    () => sileo.success({ title: 'Copied' }),
    () => sileo.error({ title: 'Could not copy' }),
  )
}

function CopyValue({ value }: { value: string }) {
  return (
    <span className="inline-flex min-w-0 items-start gap-1">
      <span className="break-words">{value || '—'}</span>
      {value ? (
        <Button type="button" variant="ghost" size="icon-sm" onClick={() => copyText(value)} className="shrink-0 text-muted-foreground sm:size-6" title="Copy" aria-label="Copy">
          <Copy />
        </Button>
      ) : null}
    </span>
  )
}

/** Everything for ASIC's form, box by box, with copy buttons. */
function FormValues({ row }: { row: Row }) {
  const rows: Array<[string, string]> = [
    ['My question is about a', 'Business Name'],
    ['I would like to know how to', 'Maintain information'],
    ['My question is', row.question ?? ''],
    ['Entity number', row.abn],
    ['Entity name', row.businessName],
    ['Your given names', row.givenNames],
    ['Your family name', row.familyName],
    ['Telephone (area / number)', `${row.phonePrefix} ${row.phoneNumber}`.trim()],
    ['Your email address', row.email],
    ['Attach documents', 'No — then tick both declarations'],
  ]
  return (
    <dl className="divide-y rounded-lg border text-sm">
      {rows.map(([k, v]) => (
        <div key={k} className="grid gap-1 px-3 py-2 sm:grid-cols-5 sm:gap-3">
          <dt className="text-caption text-muted-foreground sm:col-span-2">{k}</dt>
          <dd className="min-w-0 sm:col-span-3"><CopyValue value={v} /></dd>
        </div>
      ))}
    </dl>
  )
}

/** Fill in ASIC's form by hand from these details, then record what the receipt said. */
function RecordDialog({ row, onClose, onDone }: { row: Row | null; onClose: () => void; onDone: () => void }) {
  const [reference, setReference] = useState('')
  const [note, setNote] = useState('')
  const reset = () => { setReference(''); setNote('') }
  const mutation = useMutation({
    mutationFn: (input: { id: string; referenceNumber: string; note: string }) => api.admin.recordAsicKeyRequestOutcome(input.id, input),
  })
  const save = () => {
    if (!row) return
    void sileo.promise(mutation.mutateAsync({ id: row.id, referenceNumber: reference.trim(), note: note.trim() }), {
      loading: { title: 'Recording…' },
      success: (r) => r.status === 'Submitted'
        ? { title: `${r.businessName} recorded as sent`, description: `Reference ${r.asicReferenceNumber}` }
        : { title: `${r.businessName} marked as not sent`, description: r.errorMessage ?? undefined },
      error: (e) => ({ title: 'Could not record', description: e instanceof Error ? e.message : undefined }),
    }).then(() => { onDone(); onClose(); reset() }).catch(() => {})
  }

  return (
    <Dialog open={!!row} onOpenChange={(o) => { if (!o && !mutation.isPending) { onClose(); reset() } }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
        {row ? (
          <>
            <DialogHeader>
              <DialogTitle>{row.businessName}</DialogTitle>
              <DialogDescription>
                Fill in{' '}
                <a href={ASIC_FORM_URL} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-link hover:underline">
                  ASIC’s enquiry form <ExternalLink className="size-3" />
                </a>
                {' '}with these details, then record the reference.
              </DialogDescription>
            </DialogHeader>
            <DialogBody className="space-y-4">
              <FormValues row={row} />
              <div className="grid gap-1.5">
                <Label htmlFor="record-ref">Reference number from the receipt</Label>
                <Input id="record-ref" value={reference} onChange={(e) => setReference(e.target.value)} placeholder="e.g. 238824183, or blank if not sent" className="font-mono tabular-nums" autoFocus />
              </div>
              <div className="grid gap-1.5">
                <Label htmlFor="record-note">Note (optional)</Label>
                <Input id="record-note" value={note} onChange={(e) => setNote(e.target.value)} placeholder="Why it couldn't be sent, or anything worth remembering" />
              </div>
            </DialogBody>
            <DialogFooter>
              <Button variant="outline" onClick={() => { onClose(); reset() }} disabled={mutation.isPending}>Cancel</Button>
              <Button onClick={save} disabled={mutation.isPending}>
                {mutation.isPending ? 'Saving…' : reference.trim() ? 'Record as sent' : 'Mark as not sent'}
              </Button>
            </DialogFooter>
          </>
        ) : null}
      </DialogContent>
    </Dialog>
  )
}

function DetailDialog({ row, onClose }: { row: Row | null; onClose: () => void }) {
  return (
    <Dialog open={!!row} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
        {row ? (
          <>
            <DialogHeader>
              <DialogTitle>{row.businessName}</DialogTitle>
              <DialogDescription>
                Queued {fmtDateTime(row.createdAt)}{row.submittedAt ? ` · sent ${fmtDateTime(row.submittedAt)}` : ''}{row.keyReceivedAt ? ` · key ${fmtDateTime(row.keyReceivedAt)}` : ''}
              </DialogDescription>
            </DialogHeader>
            <DialogBody className="space-y-4">
              <div className="flex flex-wrap items-center gap-3">
                <RequestStatusPill status={row.status} />
                <ClientLink kind="keyRequest" id={row.id} />
              </div>
              <dl className="grid grid-cols-2 gap-x-6 gap-y-3 [overflow-wrap:anywhere] sm:grid-cols-3">
                <Cell label="Reference" value={row.asicReferenceNumber ?? '—'} mono />
                <Cell label="Ontraport" value={row.ontraportContactId ?? '—'} mono />
                <Cell label="Source" value={row.source} />
              </dl>
              {row.errorMessage ? (
                <Alert variant={row.status === 'Failed' ? 'destructive' : 'default'}>
                  <AlertDescription className="whitespace-pre-wrap break-words">{row.errorMessage}</AlertDescription>
                </Alert>
              ) : null}
              <FormValues row={row} />
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
