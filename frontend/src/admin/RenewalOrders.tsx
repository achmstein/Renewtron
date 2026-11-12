import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, type RenewalOrderRow } from '../api/client'
import { Pagination, useDebouncedValue } from './_components'
import { EmptyState, pageClass, PageHeader, RefreshButton, Stat, StatStrip, ViewSwitch, ViewToggle } from './_ui'
import { ClientName, DueIn, SentenceRow, When } from './_rows'
import { inSentence, orderStatus, Status } from './_status'
import { fmtMoney0, fmtMoney2 } from './_utils'
import { rememberQueue } from './_queue'
import { BulkBar, OrderActionButtons, OrderActionsProvider, TabGuide } from './_orderActions'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { sample } from '@/components/skeleton-samples'
import { sampleRows, Skeletonizer } from '@/components/skeletonizer'
import { Card } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Button } from '@/components/ui/button'
import { FacetedFilter } from '@/components/faceted-filter'
import { X } from 'lucide-react'
import { Input } from '@/components/ui/input'

const PAGE_SIZE = 50

type OrdersResponse = Awaited<ReturnType<typeof api.admin.renewalOrders>>
const SAMPLE_STAGES: RenewalOrderRow['stage'][] = ['NeedsPerson', 'Due', 'Renewing', 'NeedsPerson']
const SKELETON: OrdersResponse = {
  total: 0, page: 1, pageSize: PAGE_SIZE,
  items: sampleRows<RenewalOrderRow>(8, (i) => ({
    id: sample.id(i), source: 'Ontraport', sourceId: sample.id(100 + i), clientId: null, payerName: sample.name(i), clientName: sample.name(i),
    businessName: sample.business(i), abn: sample.abn(i), dueDate: sample.daysAhead(5 + i * 6), years: i % 3 === 0 ? 3 : 1,
    amountPaid: sample.money(i), paidAt: sample.daysAgo(i * 2), stage: SAMPLE_STAGES[i % SAMPLE_STAGES.length], reason: null, note: null,
    nextActionAt: null, stageChangedAt: sample.daysAgo(i), attemptCount: 1, currentAttemptId: null, renewedAt: null, canRetry: false,
  })),
  byStage: [
    { stage: 'NeedsPerson', count: 14, amount: 4186 }, { stage: 'Due', count: 212, amount: 0 }, { stage: 'Renewing', count: 9, amount: 0 },
    { stage: 'Renewed', count: 3820, amount: 512400 }, { stage: 'Closed', count: 96, amount: 0 },
  ],
  byReason: [],
  bySource: [],
}

/** Where a renewal was paid, as staff say it. */
const SOURCE_LABEL: Record<RenewalOrderRow['source'], string> = { Wizard: 'Direct', Ontraport: 'Ontraport', Manual: 'Manual' }

/**
 * The views of paid renewals, as tabs. "All" (the latest paid first) is where the sidebar's
 * "Renewals" opens; "to-fix" is the sidebar's "Renewals to fix". The rest are worked soonest due first.
 */
const VIEWS = [
  { value: 'all', label: 'All' },
  { value: 'to-fix', label: 'To fix' },
  { value: 'decide', label: 'Renew or refund' },
  { value: 'TooLate', label: 'Too late' },
  { value: 'NoDueDate', label: 'No due date' },
  { value: 'Due', label: 'Due' },
  { value: 'Renewing', label: 'Renewing' },
  { value: 'Waiting', label: 'Waiting' },
  { value: 'Renewed', label: 'Renewed' },
  { value: 'Closed', label: 'Closed' },
]

/** The table view's columns: a tick box, the same facts as a sentence, and the row's actions. */
function columns(picked: Set<string>, toggle: (o: RenewalOrderRow) => void): DataTableColumn<RenewalOrderRow>[] {
  return [
    {
      id: 'pick',
      header: () => <span className="sr-only">Select</span>,
      enableSorting: false,
      meta: { className: 'w-8 pr-0' },
      cell: ({ row }) => <Pick o={row.original} picked={picked.has(row.original.id)} onToggle={toggle} />,
    },
    {
      id: 'business',
      accessorKey: 'businessName',
      header: 'Business name',
      enableSorting: false,
      meta: { sticky: true, className: 'min-w-28 max-w-[38vw] sm:min-w-60 sm:max-w-76' },
      cell: ({ row }) => {
        const o = row.original
        return (
          <div>
            <Link to={`/admin/renewal-orders/${o.id}`} className="block truncate font-medium hover:underline">{o.businessName}</Link>
            <div className="hidden truncate text-caption text-muted-foreground sm:block">
              <span className="font-mono tabular-nums">{o.abn}</span>
              {' · '}
              <ClientName id={o.clientId} name={o.clientName || o.payerName} />
            </div>
          </div>
        )
      },
    },
    {
      id: 'status',
      header: 'Status',
      enableSorting: false,
      cell: ({ row }) => <Status entry={orderStatus(row.original.stage, row.original.reason)} />,
    },
    {
      id: 'due',
      header: 'Due',
      enableSorting: false,
      meta: { className: 'whitespace-nowrap tabular-nums' },
      cell: ({ row }) => row.original.dueDate ? <DueIn at={row.original.dueDate} /> : <span className="text-muted-foreground">No due date</span>,
    },
    {
      id: 'paid',
      header: 'Paid',
      enableSorting: false,
      meta: { className: 'whitespace-nowrap tabular-nums', hideBelow: 'lg' },
      cell: ({ row }) => <When at={row.original.paidAt} />,
    },
    {
      id: 'amount',
      header: 'Amount',
      enableSorting: false,
      meta: { className: 'text-right font-mono tabular-nums', headerClassName: 'text-right', hideBelow: 'md' },
      cell: ({ row }) => fmtMoney2(row.original.amountPaid),
    },
    {
      id: 'source',
      header: 'From',
      enableSorting: false,
      meta: { hideBelow: 'lg' },
      cell: ({ row }) => <span className="text-muted-foreground">{SOURCE_LABEL[row.original.source]}</span>,
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      meta: { className: 'text-right' },
      cell: ({ row }) => <div className="flex justify-end"><OrderActionButtons order={row.original} /></div>,
    },
  ]
}

/** A row's tick box: pick it to act on it with others. */
function Pick({ o, picked, onToggle }: { o: RenewalOrderRow; picked: boolean; onToggle: (o: RenewalOrderRow) => void }) {
  return (
    <Checkbox checked={picked} onCheckedChange={() => onToggle(o)} onClick={(e) => e.stopPropagation()}
      aria-label={`Select ${o.businessName}`} className="mt-0.5 shrink-0 self-start" />
  )
}

type Layout = 'Sentences' | 'Table'
const LAYOUT_KEY = 'renewtron.renewalOrders.layout'

/**
 * Paid renewals: one row per name a client paid us to renew, with the one status that matters.
 * Each row carries its next action; tick several to do one thing to all of them. A problem tab
 * opens with what it means and how to clear it.
 */
export default function RenewalOrders() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const view = searchParams.get('view') ?? 'all'
  // Sentences or a table; remembered on this browser.
  const [layout, setLayoutState] = useState<Layout>(() => {
    try { return localStorage.getItem(LAYOUT_KEY) === 'Table' ? 'Table' : 'Sentences' } catch { return 'Sentences' }
  })
  const setLayout = (l: Layout) => {
    setLayoutState(l)
    try { localStorage.setItem(LAYOUT_KEY, l) } catch { /* private window */ }
  }
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const debounced = useDebouncedValue(search, 300)
  // Where they paid: none picked means all.
  const [sources, setSources] = useState<string[]>([])
  // Ticked rows, kept across pages of the same view until acted on or cleared.
  const [picked, setPicked] = useState<Map<string, RenewalOrderRow>>(() => new Map())
  const clear = () => setPicked(new Map())

  const { data: realData, isFetching, isPending, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-renewal-orders', view, debounced, sources, page],
    queryFn: () => api.admin.renewalOrders({ view: view === 'all' ? undefined : view, search: debounced, source: sources.join(',') || undefined, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  })
  // Sample counts and rows drawn as a skeleton until the first answer arrives.
  const data = realData ?? (isPending ? SKELETON : undefined)
  const stage = (s: string) => data?.byStage.find((b) => b.stage === s)

  const items = useMemo(() => realData?.items ?? [], [realData])
  // A renewal opened from here offers the previous and next ones on this page of the list.
  const listUrl = `/admin/renewal-orders${searchParams.toString() ? `?${searchParams}` : ''}`
  const tabLabel = VIEWS.find((v) => v.value === view)?.label ?? 'Renewals'
  useEffect(() => {
    if (items.length > 0) rememberQueue({ label: view === 'all' ? 'Renewals' : tabLabel, list: listUrl, ids: items.map((o) => o.id) })
  }, [items, listUrl, tabLabel, view])
  const toggle = (o: RenewalOrderRow) => setPicked((m) => {
    const next = new Map(m)
    if (next.has(o.id)) next.delete(o.id)
    else next.set(o.id, o)
    return next
  })
  const allOnPage = items.length > 0 && items.every((o) => picked.has(o.id))
  const toggleAll = () => setPicked((m) => {
    const next = new Map(m)
    for (const o of items) {
      if (allOnPage) next.delete(o.id)
      else next.set(o.id, o)
    }
    return next
  })
  const ids = useMemo(() => new Set(picked.keys()), [picked])
  const cols = useMemo(() => columns(ids, toggle), [ids])
  // The reason tabs show how many are waiting.
  const views = VIEWS.map((v) => ({ ...v, count: realData?.byReason.find((b) => b.reason === v.value)?.count }))
  const empty = <EmptyState title={view === 'to-fix' ? 'Nothing to fix' : 'Nothing here'} message={view === 'to-fix' ? 'Every paid renewal is on track.' : undefined} />

  return (
    <OrderActionsProvider onDone={clear}>
    <div className={pageClass()}>
      <PageHeader
        title={view === 'to-fix' ? 'Renewals to fix' : 'Renewals'}
        subtitle={view === 'all' ? 'Every name a client paid us to renew, the latest first.' : 'Every name a client paid us to renew, with where it is now.'}
        right={<RefreshButton onClick={() => void refetch()} busy={isFetching} />}
      />

      <Skeletonizer loading={isPending} className="mb-6">
      <StatStrip>
        <Stat label="Needs you" dot="action" value={(stage('NeedsPerson')?.count ?? 0).toLocaleString()} sub={fmtMoney0(stage('NeedsPerson')?.amount ?? 0)} />
        <Stat label="Due or renewing" value={((stage('Due')?.count ?? 0) + (stage('Renewing')?.count ?? 0)).toLocaleString()} sub="Renewtron is on it" />
        <Stat label="Renewed" value={(stage('Renewed')?.count ?? 0).toLocaleString()} sub={fmtMoney0(stage('Renewed')?.amount ?? 0)} />
        <Stat label="Closed" value={(stage('Closed')?.count ?? 0).toLocaleString()} sub="Renewed elsewhere, disputed, cancelled" />
      </StatStrip>
      </Skeletonizer>

      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <ViewSwitch label="Show" className="min-w-0" value={view} options={views} onChange={(v) => { setPage(1); clear(); setSearchParams(v === 'all' ? {} : { view: v }) }} />
        <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto sm:flex-nowrap">
          <Input className="h-8 w-full sm:w-56" placeholder="Business name or ABN" value={search} onChange={(e) => { setPage(1); setSearch(e.target.value) }} />
          <FacetedFilter title="From" selected={sources} onChange={(v) => { setPage(1); clear(); setSources(v) }}
            options={(['Wizard', 'Ontraport', 'Manual'] as const).map((v) => ({ value: v, label: SOURCE_LABEL[v], count: realData?.bySource.find((b) => b.value === v)?.count ?? 0 }))} />
          {sources.length > 0 || search ? (
            <Button variant="ghost" size="sm" onClick={() => { setPage(1); clear(); setSources([]); setSearch('') }}>Reset<X /></Button>
          ) : null}
          <ViewToggle<Layout> value={layout} options={[{ value: 'Sentences', label: 'Sentence view' }, { value: 'Table', label: 'Table view' }]} onChange={setLayout} />
        </div>
      </div>

      <TabGuide view={view} byReason={realData?.byReason} />

      {items.length > 0 ? (
        <label className="mb-2 inline-flex cursor-pointer items-center gap-2 px-4 text-caption text-muted-foreground">
          <Checkbox checked={allOnPage} onCheckedChange={toggleAll} aria-label="Select all on this page" />
          {allOnPage ? 'All on this page selected' : 'Select all on this page'}
        </label>
      ) : null}

      {layout === 'Table' ? (
        <>
          <DataTable
            columns={cols}
            data={items}
            loading={isPending || (isFetching && isPlaceholderData)}
            skeleton={SKELETON.items}
            onRowClick={(row) => navigate(`/admin/renewal-orders/${row.original.id}`)}
            empty={empty}
          />
          {realData && realData.total > PAGE_SIZE ? (
            <div className="mt-3"><Pagination page={page} pageSize={PAGE_SIZE} total={realData.total} onPage={setPage} /></div>
          ) : null}
        </>
      ) : data && data.items.length === 0 ? empty : (
        <Skeletonizer loading={isPending}>
        <Card className="gap-0 py-0">
          {data?.items.map((o) => <OrderSentence key={o.id} o={o} picked={ids.has(o.id)} onToggle={toggle} />)}
          {realData ? (
            <div className="border-t px-4 py-2">
              <Pagination page={page} pageSize={PAGE_SIZE} total={realData.total} onPage={setPage} />
            </div>
          ) : null}
        </Card>
        </Skeletonizer>
      )}

      <BulkBar orders={[...picked.values()]} onClear={clear} />
    </div>
    </OrderActionsProvider>
  )
}

/**
 * A paid renewal as a sentence. With `onToggle` it has a tick box and its next action (inside an
 * OrderActionsProvider); without, it's just the sentence.
 */
export function OrderSentence({ o, picked, onToggle }: { o: RenewalOrderRow; picked?: boolean; onToggle?: (o: RenewalOrderRow) => void }) {
  const status = orderStatus(o.stage, o.reason)
  return (
    <SentenceRow
      meaning={status.meaning}
      to={`/admin/renewal-orders/${o.id}`}
      hint={status.hint}
      selected={picked}
      lead={onToggle ? <Pick o={o} picked={!!picked} onToggle={onToggle} /> : undefined}
      what={<span><ClientName id={o.clientId} name={o.clientName || o.payerName} /> paid for <span className="font-medium">{o.businessName}</span>: {inSentence(status.label)}</span>}
      why={<>paid {o.source === 'Ontraport' ? 'in Ontraport' : o.source === 'Wizard' ? 'direct' : 'by hand'} <When at={o.paidAt} /> · {fmtMoney2(o.amountPaid)} · {o.years} year{o.years === 1 ? '' : 's'}{o.dueDate ? <> · due <DueIn at={o.dueDate} /></> : ' · no due date'}</>}
      actions={onToggle ? <OrderActionButtons order={o} /> : undefined}
    />
  )
}
