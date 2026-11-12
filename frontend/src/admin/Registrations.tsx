import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { registrationsApi, type AdminRegistration } from '../api/registrations'
import { Pagination, usePageParam, useDebouncedValue } from './_components'
import { EmptyState, pageClass, PageHeader, RefreshButton, ViewSwitch } from './_ui'
import { Status } from './_status'
import { When } from './_rows'
import { fmtMoney2, formatAbn } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { sample } from '@/components/skeleton-samples'
import { sampleRows } from '@/components/skeletonizer'
import { Input } from '@/components/ui/input'

/*
 * New business names from the public /register page (/admin/registrations): who applied, where
 * each one is at ASIC, and which need a person (pay ASIC's BPAY bill, refund a refusal).
 */

const PAGE_SIZE = 25

const VIEWS = {
  todo: { label: 'To do', status: 'AwaitingAsicPayment,Refused,NeedsLook,Failed' },
  inflight: { label: 'In progress', status: 'Queued,Running' },
  all: { label: 'All', status: '' },
} as const
type View = keyof typeof VIEWS

const SAMPLE_STATUSES: AdminRegistration['status'][] = ['AwaitingAsicPayment', 'Lodged', 'Queued', 'Refused']
const SKELETON_ROWS = sampleRows<AdminRegistration>(6, (i) => ({
  id: sample.id(i), createdAt: sample.daysAgo(i), status: SAMPLE_STATUSES[i % 4], businessName: sample.business(i),
  abn: sample.abn(i), years: 1, firstName: sample.name(i).split(' ')[0], lastName: sample.name(i).split(' ')[1] ?? '',
  email: sample.email(i), phone: sample.mobile(), serviceAddress: '1 Martin Pl, Sydney NSW 2000', principalAddress: '1 Martin Pl, Sydney NSW 2000',
  amount: sample.money(i), paidAt: sample.daysAgo(i), stripePaymentIntentId: null, attemptCount: 1, startedAt: null, finishedAt: null,
  asicTransactionReference: '1-11AQR319', abrEntityName: null, abrEntityType: null, nameVerdict: 'Available', nameVerdictText: null,
  asicPayBy: 'Bpay', asicFee: 47, lodgedAt: sample.daysAgo(i), payBy: sample.daysAhead(10), bpayBillerCode: '17301', bpayReference: '8597040845155',
  airwallexTransferId: null, airwallexTransferStatus: null, bpayPaymentError: null, asicPaidAt: null, asicTransactionStatus: null,
  asicPaymentStatus: null, asicCheckedAt: null, failedAtStep: null, error: null, isTest: false,
}))

export default function Registrations() {
  const [view, setView] = useState<View>('todo')
  const [page, setPage] = usePageParam()
  const [search, setSearch] = useState('')
  const debounced = useDebouncedValue(search)

  const { data, isPending, isFetching, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-registrations', view, debounced, page],
    queryFn: () => registrationsApi.admin.list({ status: VIEWS[view].status, search: debounced, page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  })

  const columns = useMemo<DataTableColumn<AdminRegistration>[]>(() => [
    {
      id: 'name',
      accessorKey: 'businessName',
      header: 'Business name',
      meta: { sticky: true, className: 'min-w-36 max-w-[42vw] sm:min-w-56 sm:max-w-80' },
      cell: ({ row }) => {
        const r = row.original
        return (
          <div className="min-w-0">
            <Link to={`/admin/registrations/${r.id}`} className="block truncate font-medium hover:underline">{r.businessName}</Link>
            <div className="hidden truncate text-caption text-muted-foreground sm:block">
              <span className="font-mono tabular-nums">{formatAbn(r.abn)}</span>
              {r.isTest ? <span className="ml-2 rounded bg-amber-100 px-1 text-amber-800">test</span> : null}
            </div>
          </div>
        )
      },
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => <Status kind="registration" status={row.original.status} />,
    },
    {
      id: 'customer',
      header: 'Customer',
      meta: { hideBelow: 'md' },
      cell: ({ row }) => (
        <div className="min-w-0">
          <div className="truncate">{row.original.firstName} {row.original.lastName}</div>
          <div className="truncate text-caption text-muted-foreground">{row.original.email}</div>
        </div>
      ),
    },
    {
      id: 'asic',
      header: 'ASIC',
      meta: { hideBelow: 'lg' },
      cell: ({ row }) => {
        const r = row.original
        return (
          <div className="text-caption">
            <div className="font-mono">{r.asicTransactionReference ?? '—'}</div>
            {r.payBy ? <div className="text-muted-foreground">pay by <When at={r.payBy} /></div> : null}
          </div>
        )
      },
    },
    {
      id: 'amount',
      header: 'Paid',
      meta: { hideBelow: 'sm', className: 'text-right tabular-nums', headerClassName: 'text-right' },
      cell: ({ row }) => (row.original.paidAt ? fmtMoney2(row.original.amount) : '—'),
    },
    {
      id: 'created',
      header: 'Applied',
      meta: { hideBelow: 'sm' },
      cell: ({ row }) => <When at={row.original.createdAt} />,
    },
  ], [])

  return (
    <div className={pageClass()}>
      <PageHeader
        title="New business names"
        subtitle="Applications from the register page, lodged through our ASIC Connect login."
        right={<RefreshButton onClick={() => void refetch()} busy={isFetching} />}
      />
      <div className="mt-4 flex flex-wrap items-center gap-2">
        <ViewSwitch<View> label="Show" value={view} onChange={(v) => { setPage(1); setView(v) }}
          options={(Object.keys(VIEWS) as View[]).map((k) => ({ value: k, label: VIEWS[k].label }))} />
        <Input value={search} onChange={(e) => { setPage(1); setSearch(e.target.value) }} placeholder="Name, email, ABN or ASIC ref"
               className="h-8 w-full sm:w-72" />
      </div>
      <div className="mt-4">
        <DataTable
          columns={columns}
          data={data?.items ?? []}
          loading={isPending || (isFetching && isPlaceholderData)}
          skeleton={SKELETON_ROWS}
          empty={<EmptyState title={view === 'todo' ? 'Nothing to do' : 'No applications'} message={view === 'todo' ? 'Applications that need a person show here.' : undefined} />}
        />
        {(data?.total ?? 0) > PAGE_SIZE ? <Pagination page={page} pageSize={PAGE_SIZE} total={data!.total} onPage={setPage} /> : null}
      </div>
    </div>
  )
}
