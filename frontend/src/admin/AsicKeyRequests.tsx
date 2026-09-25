import { useMemo, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ClipboardCheck, Copy, ExternalLink, RotateCw, X } from 'lucide-react'
import { sileo } from 'sileo'
import { api, type AsicKeyRequestDto } from '../api/client'
import { useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, type Tone } from './_ui'
import { fmtDateTime, relativeTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Dialog, DialogBody, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

type RequestsResponse = Awaited<ReturnType<typeof api.admin.asicKeyRequests>>
type Row = AsicKeyRequestDto
type Stats = RequestsResponse['stats']
type Facets = RequestsResponse['facets']

const ASIC_FORM_URL = 'https://www.edge.asic.gov.au/008/inquiryV001?start/landingPage'

const STATUS: Record<string, { label: string; tone: Tone }> = {
  Pending:     { label: 'To do',         tone: 'amber' },
  Manual:      { label: 'With a person', tone: 'indigo' },
  Submitted:   { label: 'Sent to ASIC',  tone: 'indigo' },
  KeyReceived: { label: 'Key received',  tone: 'emerald' },
  Failed:      { label: 'Not sent',      tone: 'red' },
}
const statusLabel = (s: string) => STATUS[s]?.label ?? s

export default function AsicKeyRequests() {
  const [statuses, setStatuses] = useState<string[]>([])
  const [searchInput, setSearchInput] = useState('')
  const search = useDebouncedValue(searchInput, 300)
  const [detail, setDetail] = useState<Row | null>(null)
  const [record, setRecord] = useState<Row | null>(null)

  const queryClient = useQueryClient()
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['admin-asic-key-requests', { statuses, search }],
    queryFn: () => api.admin.asicKeyRequests({ status: statuses.join(',') || undefined, search: search || undefined }),
    placeholderData: keepPreviousData,
  })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-asic-key-requests'] })

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
                className={`mt-1 block text-left text-xxs font-mono truncate max-w-[14rem] hover:underline ${r.status === 'Failed' ? 'text-red-700' : 'text-zinc-500'}`}
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
          <div className="text-xxs font-mono tabular-nums text-zinc-400">{row.original.source.toLowerCase()}</div>
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
            {r.status === 'Failed' || r.status === 'Manual' ? (
              <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" disabled={requeueMutation.isPending} onClick={(e) => { e.stopPropagation(); requeue(r) }} title="Back on the to-do list">
                <RotateCw className="h-3.5 w-3.5" /> Re-queue
              </Button>
            ) : null}
            <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={(e) => { e.stopPropagation(); setRecord(r) }} title="Fill in ASIC's form with these details and record the reference number">
              <ClipboardCheck className="h-3.5 w-3.5" /> {r.status === 'Submitted' ? 'Update' : 'Do it'}
            </Button>
          </div>
        )
      },
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [requeueMutation.isPending])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="PIPELINE"
        title="ASIC key requests"
        subtitle="Each paid sale without a key → a request to fill in on ASIC's enquiry form, in the client's name, asking for the key to be emailed to the inbox. Record the reference and the inbox scanner does the rest."
        right={
          <>
            <button
              onClick={runNow}
              disabled={runMutation.isPending}
              className="inline-flex items-center gap-2 whitespace-nowrap rounded-md bg-white text-zinc-800 px-3 py-2 text-sm font-medium ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 shadow-sm disabled:opacity-50 transition"
              title="Match keys that have arrived in the inbox against sent requests"
            >
              <RotateCw className={`h-4 w-4 ${runMutation.isPending ? 'animate-spin' : ''}`} />
              Match arrived keys
            </button>
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatTile
          kicker="TO DO"
          label="Waiting for a person"
          value={(pendingCount + manualCount).toLocaleString()}
          sub={manualCount > 0 ? `${manualCount} being handled now` : 'fill in ASIC’s form, record the reference'}
          tone={pendingCount + manualCount > 0 ? 'amber' : 'zinc'}
        />
        <StatTile
          kicker="SENT"
          label="Awaiting the key email"
          value={submittedCount.toLocaleString()}
          sub="matched when ASIC’s email lands"
          tone="indigo"
        />
        <StatTile
          kicker="DONE"
          label="Keys received"
          value={keyReceivedCount.toLocaleString()}
          sub={failedCount > 0 ? `${failedCount} not sent — see notes` : `${(data?.totalCount ?? 0).toLocaleString()} requests total`}
          tone="emerald"
        />
        <SparklineTile
          kicker="14D"
          label="Sent today"
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
  const s = STATUS[status]
  return <StatusPill tone={s?.tone ?? 'zinc'}>{(s?.label ?? status).toUpperCase()}</StatusPill>
}

function copyText(value: string) {
  void navigator.clipboard?.writeText(value).then(
    () => sileo.success({ title: 'Copied' }),
    () => sileo.error({ title: 'Could not copy' }),
  )
}

function CopyValue({ value, small }: { value: string; small?: boolean }) {
  return (
    <span className="inline-flex items-start gap-1 min-w-0">
      <span className={`break-words ${small ? 'text-xs' : ''}`}>{value || '—'}</span>
      {value ? (
        <button type="button" onClick={() => copyText(value)} className="shrink-0 mt-0.5 text-zinc-400 hover:text-zinc-700" title="Copy">
          <Copy className="h-3 w-3" />
        </button>
      ) : null}
    </span>
  )
}

/** Everything for ASIC's form, box by box, with copy buttons. */
function FormValues({ row }: { row: Row }) {
  const rows: Array<[string, string, boolean?]> = [
    ['My question is about a', 'Business Name'],
    ['I would like to know how to', 'Maintain information'],
    ['My question is', row.question ?? '', true],
    ['Entity number', row.abn],
    ['Entity name', row.businessName],
    ['Your given names', row.givenNames],
    ['Your family name', row.familyName],
    ['Telephone (area / number)', `${row.phonePrefix} ${row.phoneNumber}`.trim()],
    ['Your email address', row.email],
    ['Attach documents', 'No — then tick both declarations'],
  ]
  return (
    <dl className="grid grid-cols-[10rem_1fr] gap-y-1 text-sm rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-3 py-2">
      {rows.map(([k, v, small]) => (
        <div key={k} className="contents">
          <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">{k}</dt>
          <dd className="text-zinc-900 min-w-0"><CopyValue value={v} small={small} /></dd>
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
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-indigo-700">FILL IN ASIC’S FORM</div>
              <DialogTitle>{row.businessName}</DialogTitle>
            </DialogHeader>
            <DialogBody className="space-y-3">
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <a href={ASIC_FORM_URL} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-brand-700 hover:underline">
                  Open ASIC’s enquiry form <ExternalLink className="h-3 w-3" />
                </a>
              </div>
              <FormValues row={row} />
              <div>
                <Label htmlFor="record-ref" className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">ASIC reference number from the receipt</Label>
                <Input id="record-ref" value={reference} onChange={(e) => setReference(e.target.value)} placeholder="e.g. 238824183 — leave blank if it couldn't be sent" className="mt-1 h-9 font-mono tabular-nums" autoFocus />
              </div>
              <div>
                <Label htmlFor="record-note" className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">Note (optional)</Label>
                <Input id="record-note" value={note} onChange={(e) => setNote(e.target.value)} placeholder="why it couldn't be sent, or anything worth remembering" className="mt-1 h-9" />
              </div>
            </DialogBody>
            <DialogFooter>
              <Button variant="outline" onClick={() => { onClose(); reset() }} disabled={mutation.isPending}>Cancel</Button>
              <Button onClick={save} disabled={mutation.isPending} className={reference.trim() ? 'bg-indigo-600 hover:bg-indigo-700' : 'bg-zinc-700 hover:bg-zinc-800'}>
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
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">ASIC KEY REQUEST</div>
              <DialogTitle>{row.businessName}</DialogTitle>
            </DialogHeader>
            <DialogBody className="space-y-3">
              <div className="flex flex-wrap items-center gap-2">
                <RequestStatusPill status={row.status} />
                <span className="text-xs text-zinc-500">
                  queued {fmtDateTime(row.createdAt)}{row.submittedAt ? ` · sent ${fmtDateTime(row.submittedAt)}` : ''}{row.keyReceivedAt ? ` · key ${fmtDateTime(row.keyReceivedAt)}` : ''}
                </span>
              </div>
              <dl className="grid grid-cols-[7rem_1fr] gap-y-1.5 text-sm">
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Reference</dt>
                <dd className="font-mono tabular-nums text-zinc-900">{row.asicReferenceNumber ?? '—'}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Ontraport</dt>
                <dd className="font-mono text-zinc-700">{row.ontraportContactId ?? '—'}</dd>
                <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 pt-0.5">Source</dt>
                <dd className="text-zinc-700">{row.source}</dd>
              </dl>
              {row.errorMessage ? (
                <div className={`rounded-md px-3 py-2 text-xs font-mono whitespace-pre-wrap break-words ring-1 ${row.status === 'Failed' ? 'bg-red-50 ring-red-100 text-red-800' : 'bg-zinc-50 ring-zinc-200 text-zinc-700'}`}>{row.errorMessage}</div>
              ) : null}
              <FormValues row={row} />
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
