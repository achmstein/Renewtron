import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, type RenewalOrderRow } from '../api/client'
import { Pagination, useDebouncedValue } from './_components'
import { EmptyState, pageClass, PageHeader, RefreshButton, Stat, StatStrip, ViewSwitch } from './_ui'
import { ClientName, DueIn, SentenceRow, When } from './_rows'
import { inSentence, orderStatus } from './_status'
import { fmtMoney0, fmtMoney2 } from './_utils'
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
  { value: 'Due', label: 'Due' },
  { value: 'Renewing', label: 'Renewing' },
  { value: 'Waiting', label: 'Waiting' },
  { value: 'Renewed', label: 'Renewed' },
  { value: 'Closed', label: 'Closed' },
]

/** Paid renewals: one row per name a client paid us to renew, with the one status that matters. */
export default function RenewalOrders() {
  const [searchParams, setSearchParams] = useSearchParams()
  const view = searchParams.get('view') ?? 'all'
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const debounced = useDebouncedValue(search, 300)

  const { data: realData, isFetching, isPending, refetch } = useQuery({
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
        <Input className="h-8 w-full sm:w-56" placeholder="Business name or ABN" value={search} onChange={(e) => { setPage(1); setSearch(e.target.value) }} />
      </div>

      {data && data.items.length === 0 ? (
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
