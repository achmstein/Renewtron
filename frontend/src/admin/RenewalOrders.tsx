import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, type RenewalOrderRow } from '../api/client'
import { Pagination, useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, Stat, StatStrip } from './_ui'
import { ClientName, DueIn, SentenceRow, When } from './_rows'
import { inSentence, orderStatus } from './_status'
import { fmtMoney0, fmtMoney2 } from './_utils'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'

const PAGE_SIZE = 50

/** The views of paid renewals, as tabs. "to-fix" is the sidebar's "Renewals to fix". */
const VIEWS = [
  { value: 'to-fix', label: 'To fix' },
  { value: 'decide', label: 'Renew or refund' },
  { value: 'Due', label: 'Due' },
  { value: 'Renewing', label: 'Renewing' },
  { value: 'Waiting', label: 'Waiting' },
  { value: 'Renewed', label: 'Renewed' },
  { value: 'Closed', label: 'Closed' },
  { value: 'all', label: 'All' },
]

/** Paid renewals: one row per name a client paid us to renew, with the one status that matters. */
export default function RenewalOrders() {
  const [searchParams, setSearchParams] = useSearchParams()
  const view = searchParams.get('view') ?? 'to-fix'
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const debounced = useDebouncedValue(search, 300)

  const { data, isFetching, refetch } = useQuery({
    queryKey: ['admin-renewal-orders', view, debounced, page],
    queryFn: () => api.admin.renewalOrders({ view: view === 'all' ? undefined : view, search: debounced, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  })
  const stage = (s: string) => data?.byStage.find((b) => b.stage === s)

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        title={view === 'to-fix' ? 'Renewals to fix' : 'Paid renewals'}
        subtitle="Every name a client paid us to renew, with where it is now."
        right={<RefreshButton onClick={() => void refetch()} busy={isFetching} />}
      />

      <StatStrip className="mb-6">
        <Stat label="Needs you" dot="action" value={(stage('NeedsPerson')?.count ?? 0).toLocaleString()} sub={fmtMoney0(stage('NeedsPerson')?.amount ?? 0)} />
        <Stat label="Due or renewing" value={((stage('Due')?.count ?? 0) + (stage('Renewing')?.count ?? 0)).toLocaleString()} sub="Renewtron is on it" />
        <Stat label="Renewed" value={(stage('Renewed')?.count ?? 0).toLocaleString()} sub={fmtMoney0(stage('Renewed')?.amount ?? 0)} />
        <Stat label="Closed" value={(stage('Closed')?.count ?? 0).toLocaleString()} sub="Renewed elsewhere, disputed, cancelled" />
      </StatStrip>

      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <Tabs value={view} onValueChange={(v) => { setPage(1); setSearchParams({ view: v }) }}>
          <TabsList>
            {VIEWS.map((v) => <TabsTrigger key={v.value} value={v.value}>{v.label}</TabsTrigger>)}
          </TabsList>
        </Tabs>
        <Input className="h-8 w-56" placeholder="Business name or ABN" value={search} onChange={(e) => { setPage(1); setSearch(e.target.value) }} />
      </div>

      {data && data.items.length === 0 ? (
        <EmptyState title={view === 'to-fix' ? 'Nothing to fix' : 'Nothing here'} message={view === 'to-fix' ? 'Every paid renewal is on track.' : undefined} />
      ) : (
        <Card className="gap-0 py-0">
          {data?.items.map((o) => <OrderSentence key={o.id} o={o} />)}
          {data ? (
            <div className="border-t px-4 py-2">
              <Pagination page={page} pageSize={PAGE_SIZE} total={data.total} onPage={setPage} />
            </div>
          ) : null}
        </Card>
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
      what={<span title={status.hint}><ClientName id={o.clientId} name={o.clientName || o.payerName} /> paid for <span className="font-medium">{o.businessName}</span>: {inSentence(status.label)}</span>}
      why={<>paid <When at={o.paidAt} /> · {fmtMoney2(o.amountPaid)} · {o.years} year{o.years === 1 ? '' : 's'}{o.dueDate ? <> · due <DueIn at={o.dueDate} /></> : ' · no due date'}</>}
    />
  )
}
