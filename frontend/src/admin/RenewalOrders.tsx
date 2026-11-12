import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, type RenewalOrderRow } from '../api/client'
import { Pagination, useDebouncedValue } from './_components'
import { EmptyState, pageClass, PageHeader, RefreshButton, Stat, StatStrip, ViewSwitch, ViewToggle } from './_ui'
import { ClientName, DueIn, SentenceRow, When } from './_rows'
import { inSentence, orderStatus, Status } from './_status'
import { fmtMoney0, fmtMoney2 } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { sample } from '@/components/skeleton-samples'
import { sampleRows, Skeletonizer } from '@/components/skeletonizer'
import { Card } from '@/components/ui/card'
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
}

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

/** The table view's columns: the same facts as a sentence, one per column. */
const COLUMNS: DataTableColumn<RenewalOrderRow>[] = [
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
          <Link to={`/admin/renewal-orders/${o.id}`} onClick={(e) => e.stopPropagation()} className="block truncate font-medium hover:underline">{o.businessName}</Link>
          <div className="hidden truncate text-caption text-muted-foreground sm:block">
            <span className="font-mono tabular-nums">{o.abn}</span>
            {' · '}
            <span onClick={(e) => e.stopPropagation()}><ClientName id={o.clientId} name={o.clientName || o.payerName} /></span>
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
    meta: { className: 'whitespace-nowrap tabular-nums', hideBelow: 'md' },
    cell: ({ row }) => <When at={row.original.paidAt} />,
  },
  {
    id: 'amount',
    header: 'Amount',
    enableSorting: false,
    meta: { className: 'text-right font-mono tabular-nums', headerClassName: 'text-right' },
    cell: ({ row }) => fmtMoney2(row.original.amountPaid),
  },
  {
    id: 'years',
    header: 'Years',
    enableSorting: false,
    meta: { className: 'text-right tabular-nums', headerClassName: 'text-right', hideBelow: 'md' },
    cell: ({ row }) => row.original.years,
  },
  {
    id: 'source',
    header: 'From',
    enableSorting: false,
    meta: { hideBelow: 'lg' },
    cell: ({ row }) => <span className="text-muted-foreground">{row.original.source}</span>,
  },
]

type Layout = 'Sentences' | 'Table'
const LAYOUT_KEY = 'renewtron.renewalOrders.layout'

/** Paid renewals: one row per name a client paid us to renew, with the one status that matters. */
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

  const { data: realData, isFetching, isPending, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-renewal-orders', view, debounced, page],
    queryFn: () => api.admin.renewalOrders({ view: view === 'all' ? undefined : view, search: debounced, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  })
  // Sample counts and rows drawn as a skeleton until the first answer arrives.
  const data = realData ?? (isPending ? SKELETON : undefined)
  const stage = (s: string) => data?.byStage.find((b) => b.stage === s)

  return (
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
        <ViewSwitch label="Show" className="min-w-0" value={view} options={VIEWS} onChange={(v) => { setPage(1); setSearchParams(v === 'all' ? {} : { view: v }) }} />
        <div className="flex w-full items-center gap-2 sm:w-auto">
          <Input className="h-8 w-full sm:w-56" placeholder="Business name or ABN" value={search} onChange={(e) => { setPage(1); setSearch(e.target.value) }} />
          <ViewToggle<Layout> value={layout} options={[{ value: 'Sentences', label: 'Sentence view' }, { value: 'Table', label: 'Table view' }]} onChange={setLayout} />
        </div>
      </div>

      {layout === 'Table' ? (
        <>
          <DataTable
            columns={COLUMNS}
            data={realData?.items ?? []}
            loading={isPending || (isFetching && isPlaceholderData)}
            skeleton={SKELETON.items}
            onRowClick={(row) => navigate(`/admin/renewal-orders/${row.original.id}`)}
            empty={<EmptyState title={view === 'to-fix' ? 'Nothing to fix' : 'Nothing here'} message={view === 'to-fix' ? 'Every paid renewal is on track.' : undefined} />}
          />
          {realData && realData.total > PAGE_SIZE ? (
            <div className="mt-3"><Pagination page={page} pageSize={PAGE_SIZE} total={realData.total} onPage={setPage} /></div>
          ) : null}
        </>
      ) : data && data.items.length === 0 ? (
        <EmptyState title={view === 'to-fix' ? 'Nothing to fix' : 'Nothing here'} message={view === 'to-fix' ? 'Every paid renewal is on track.' : undefined} />
      ) : (
        <Skeletonizer loading={isPending}>
        <Card className="gap-0 py-0">
          {data?.items.map((o) => <OrderSentence key={o.id} o={o} />)}
          {realData ? (
            <div className="border-t px-4 py-2">
              <Pagination page={page} pageSize={PAGE_SIZE} total={realData.total} onPage={setPage} />
            </div>
          ) : null}
        </Card>
        </Skeletonizer>
      )}
    </div>
  )
}

export function OrderSentence({ o }: { o: RenewalOrderRow }) {
  const status = orderStatus(o.stage, o.reason)
  return (
    <SentenceRow
      meaning={status.meaning}
      to={`/admin/renewal-orders/${o.id}`}
      hint={status.hint}
      what={<span><ClientName id={o.clientId} name={o.clientName || o.payerName} /> paid for <span className="font-medium">{o.businessName}</span>: {inSentence(status.label)}</span>}
      why={<>paid <When at={o.paidAt} /> · {fmtMoney2(o.amountPaid)} · {o.years} year{o.years === 1 ? '' : 's'}{o.dueDate ? <> · due <DueIn at={o.dueDate} /></> : ' · no due date'}</>}
    />
  )
}
