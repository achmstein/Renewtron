import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { CalendarRange, CircleAlert, X } from 'lucide-react'
import { api } from '../api/client'
import { ErrorModal, Pagination, useDebouncedValue } from './_components'
import { EmptyState, Field, PageHeader, RefreshButton, Stat, StatStrip } from './_ui'
import { Status } from './_status'
import { fmtDate, fmtMoney0, fmtMoney2, fmtTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { sample } from '@/components/skeleton-samples'
import { sampleRows, Skeletonizer } from '@/components/skeletonizer'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'

type PaymentsResponse = Awaited<ReturnType<typeof api.admin.payments>>
type Payment = PaymentsResponse['items'][number]
type Stats = PaymentsResponse['stats']
type Facets = PaymentsResponse['facets']

const PAGE_SIZE = 20

const SKELETON_ROWS = sampleRows<Payment>(8, (i) => ({
  kind: i % 4 === 2 ? 'failed' : 'succeeded',
  when: sample.daysAgo(i),
  paymentIntentId: `pi_${sample.id(i).slice(-12)}`,
  amount: sample.money(i),
  abn: sample.abn(i),
  businessNames: [sample.business(i)],
  leadId: sample.id(i),
  customerName: sample.name(i),
  email: sample.email(i),
  cardBrand: i % 2 ? 'mastercard' : 'visa',
  cardLast4: '4242',
  cardholderName: sample.name(i),
  error: i % 4 === 2 ? sample.text(0) : null,
  renewals: [{ id: sample.id(i), status: 'Completed' }],
}))
const SKELETON_STATS: Stats = { succeededCount: 1240, succeededValue: 312400, failedCount: 86, succeeded30d: 112, succeededValue30d: 28400, failed30d: 9 }

export default function Payments() {
  const [page, setPage] = useState(1)
  const [searchInput, setSearchInput] = useState('')
  const search = useDebouncedValue(searchInput, 300)
  const [results, setResults] = useState<string[]>([])
  const [brands, setBrands] = useState<string[]>([])
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [errorModal, setErrorModal] = useState<string | null>(null)

  const { data, isFetching, isPending, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-payments', { search, results, brands, dateFrom, dateTo, page }],
    queryFn: () => api.admin.payments({
      status: results.join(',') || undefined,
      cardBrand: brands.join(',') || undefined,
      search: search.trim() || undefined,
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
    placeholderData: keepPreviousData,
  })

  const items = data?.items ?? []
  const stats: Stats = data?.stats ?? (isPending ? SKELETON_STATS : { succeededCount: 0, succeededValue: 0, failedCount: 0, succeeded30d: 0, succeededValue30d: 0, failed30d: 0 })
  const facets: Facets = data?.facets ?? { result: [], cardBrand: [] }

  const hasActiveFilters = !!searchInput || results.length > 0 || brands.length > 0 || !!dateFrom || !!dateTo
  const resetFilters = () => {
    setPage(1)
    setSearchInput(''); setResults([]); setBrands([]); setDateFrom(''); setDateTo('')
  }
  const withPageReset = <T,>(setter: (v: T) => void) => (v: T) => { setPage(1); setter(v) }

  const resultOptions: FacetOption[] = facets.result.map((f) => ({ value: f.value, label: f.value, count: f.count }))
  const brandOptions: FacetOption[] = facets.cardBrand.map((f) => ({ value: f.value, label: f.value, count: f.count }))

  const columns = useMemo<DataTableColumn<Payment>[]>(() => [
    {
      id: 'customer',
      accessorKey: 'customerName',
      header: 'Customer',
      enableSorting: false,
      // Compact on phones: customer name only; email + business names return at sm.
      meta: { sticky: true, className: 'min-w-28 max-w-[38vw] sm:min-w-60 sm:max-w-80' },
      cell: ({ row }) => {
        const p = row.original
        const second = [p.email, p.businessNames.join(', ')].filter(Boolean).join(' · ')
        return (
          <div className="min-w-0">
            {p.leadId ? (
              <Link to={`/admin/leads/${p.leadId}`} className="block truncate text-sm font-medium hover:text-link hover:underline">{p.customerName ?? 'Lead'}</Link>
            ) : (
              <div className="truncate text-sm font-medium">{p.customerName ?? '—'}</div>
            )}
            {second ? <div className="hidden truncate text-caption text-muted-foreground sm:block" title={second}>{second}</div> : null}
          </div>
        )
      },
    },
    {
      accessorKey: 'when',
      header: 'When',
      cell: ({ row }) => (
        <div className="tabular-nums">
          <div className="text-sm">{fmtDate(row.original.when)}</div>
          <div className="hidden text-caption text-muted-foreground sm:block">{fmtTime(row.original.when)}</div>
        </div>
      ),
    },
    {
      id: 'result',
      header: 'Result',
      enableSorting: false,
      cell: ({ row }) => <ResultCell p={row.original} onError={setErrorModal} />,
    },
    {
      id: 'amount',
      accessorFn: (p) => p.amount ?? 0,
      header: 'Amount',
      meta: { className: 'text-right font-mono tabular-nums text-sm', headerClassName: 'text-right' },
      cell: ({ row }) => row.original.amount != null ? fmtMoney2(row.original.amount) : <span className="text-muted-foreground">—</span>,
    },
    {
      id: 'abn',
      header: 'ABN',
      enableSorting: false,
      meta: { hideBelow: 'md' },
      cell: ({ row }) => row.original.abn
        ? <span className="font-mono text-sm tabular-nums text-muted-foreground">{row.original.abn}</span>
        : <span className="text-sm text-muted-foreground">—</span>,
    },
    {
      id: 'card',
      header: 'Card',
      enableSorting: false,
      meta: { hideBelow: 'md' },
      cell: ({ row }) => <CardCell p={row.original} />,
    },
    {
      id: 'renewal',
      header: () => <span className="sr-only">Renewal</span>,
      enableSorting: false,
      meta: { className: 'text-right' },
      cell: ({ row }) => {
        const p = row.original
        return p.renewals.length > 0 ? (
          <Link to={`/admin/renewals/${p.renewals[0].id}`} className="whitespace-nowrap text-sm font-medium text-link hover:underline">
            {p.renewals.length > 1 ? `${p.renewals.length} renewals →` : 'Renewal →'}
          </Link>
        ) : null
      },
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <PageHeader
        title="Payments"
        subtitle="Every Stripe attempt, paid or declined."
        right={<RefreshButton onClick={() => void refetch()} busy={isFetching} />}
      />

      <Skeletonizer loading={isPending}>
      <StatStrip>
        <Stat label="Collected" value={fmtMoney0(stats.succeededValue30d)} sub={`Last 30 days · ${stats.succeeded30d} payment${stats.succeeded30d === 1 ? '' : 's'}`} />
        <Stat label="Failed attempts" value={String(stats.failed30d)} sub="Last 30 days" dot={stats.failed30d > 0 ? 'waiting' : null} />
        <Stat label="Collected" value={fmtMoney0(stats.succeededValue)} sub={`All time · ${stats.succeededCount} payment${stats.succeededCount === 1 ? '' : 's'}`} />
      </StatStrip>
      </Skeletonizer>

      {/* Filter toolbar — every facet is resolved server-side with live counts */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => { setPage(1); setSearchInput(e.target.value) }}
          placeholder="ABN, email, name…"
          className="h-8 w-full sm:w-56"
        />
        <FacetedFilter title="Result" options={resultOptions} selected={results} onChange={withPageReset(setResults)} />
        <FacetedFilter title="Card brand" options={brandOptions} selected={brands} onChange={withPageReset(setBrands)} />
        <DateRangeFilter dateFrom={dateFrom} dateTo={dateTo} onFrom={withPageReset(setDateFrom)} onTo={withPageReset(setDateTo)} />
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" onClick={resetFilters}>
            Reset
            <X />
          </Button>
        ) : null}
      </div>

      <div className="mt-4">
        <DataTable columns={columns} data={items} loading={isPending || (isFetching && isPlaceholderData)} skeleton={SKELETON_ROWS} empty={<EmptyState title={hasActiveFilters ? 'Nothing matches these filters' : 'No payments yet'} />} />
        {data ? <Pagination page={page} pageSize={PAGE_SIZE} total={data.totalCount} onPage={setPage} /> : null}
      </div>

      <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Payment error details" />
    </div>
  )
}


function DateRangeFilter({ dateFrom, dateTo, onFrom, onTo }: {
  dateFrom: string
  dateTo: string
  onFrom: (v: string) => void
  onTo: (v: string) => void
}) {
  const active = dateFrom || dateTo
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="border-dashed">
          <CalendarRange />
          Dates
          {active ? <span className="hidden text-caption text-muted-foreground tabular-nums sm:inline">{dateFrom || '…'} → {dateTo || '…'}</span> : null}
          {/* Phones: the range would widen the button past the screen; a dot says dates are set. */}
          {active ? <span aria-label="Dates set" className="size-1.5 rounded-full bg-link sm:hidden" /> : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-64 space-y-3" align="start">
        <Field label="From">
          <Input type="date" value={dateFrom} onChange={(e) => onFrom(e.target.value)} className="tabular-nums" />
        </Field>
        <Field label="To">
          <Input type="date" value={dateTo} onChange={(e) => onTo(e.target.value)} className="tabular-nums" />
        </Field>
        {active ? (
          <Button variant="ghost" size="sm" className="w-full" onClick={() => { onFrom(''); onTo('') }}>Clear dates</Button>
        ) : null}
      </PopoverContent>
    </Popover>
  )
}

/* ─────── Cell components ─────── */

function ResultCell({ p, onError }: { p: Payment; onError: (m: string) => void }) {
  return (
    <div className="max-w-64">
      <div className="flex items-center gap-1">
        <Status kind="payment" status={p.kind} />
        {p.error ? (
          <Button variant="ghost" size="icon-sm" className="text-muted-foreground sm:size-6" onClick={() => onError(p.error ?? '')} title="View error" aria-label="View error">
            <CircleAlert />
          </Button>
        ) : null}
      </div>
      {p.error ? <div className="mt-1 truncate text-caption text-muted-foreground" title={p.error}>{p.error}</div> : null}
    </div>
  )
}

function CardCell({ p }: { p: Payment }) {
  if (p.cardLast4) {
    return <span className="whitespace-nowrap text-sm text-muted-foreground"><span className="capitalize">{p.cardBrand ?? 'card'}</span> •••• <span className="font-mono">{p.cardLast4}</span></span>
  }
  if (p.cardholderName) {
    return <span className="inline-block max-w-40 truncate text-sm text-muted-foreground" title={p.cardholderName}>{p.cardholderName}</span>
  }
  return <span className="text-sm text-muted-foreground">—</span>
}
