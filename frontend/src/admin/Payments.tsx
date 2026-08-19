import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { CalendarRange, X } from 'lucide-react'
import { api } from '../api/client'
import { ErrorModal, Pagination, useDebouncedValue } from './_components'
import { EmptyState, PageHeader, RefreshButton, StatTile, StatusPill } from './_ui'
import { fmtDate, fmtMoney0, fmtMoney2, fmtTime } from './_utils'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { FacetedFilter, type FacetOption } from '@/components/faceted-filter'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'

type PaymentsResponse = Awaited<ReturnType<typeof api.admin.payments>>
type Payment = PaymentsResponse['items'][number]
type Stats = PaymentsResponse['stats']
type Facets = PaymentsResponse['facets']

const PAGE_SIZE = 20

export default function Payments() {
  const [page, setPage] = useState(1)
  const [searchInput, setSearchInput] = useState('')
  const search = useDebouncedValue(searchInput, 300)
  const [results, setResults] = useState<string[]>([])
  const [brands, setBrands] = useState<string[]>([])
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [errorModal, setErrorModal] = useState<string | null>(null)

  const { data, isFetching, refetch } = useQuery({
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
  const stats: Stats = data?.stats ?? { succeededCount: 0, succeededValue: 0, failedCount: 0, succeeded30d: 0, succeededValue30d: 0, failed30d: 0 }
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
      header: 'Customer · Names',
      enableSorting: false,
      meta: { sticky: true, className: 'min-w-[15rem] max-w-[19rem]' },
      cell: ({ row }) => {
        const p = row.original
        return (
          <div>
            {p.leadId ? (
              <Link to={`/admin/leads/${p.leadId}`} className="block text-sm font-medium text-zinc-900 hover:underline truncate">{p.customerName ?? 'Lead'}</Link>
            ) : (
              <div className="text-sm font-medium text-zinc-900 truncate">{p.customerName ?? '—'}</div>
            )}
            {p.email ? <div className="text-xxs font-mono text-zinc-400 truncate">{p.email}</div> : null}
            {p.businessNames.length > 0 ? (
              <div className="mt-0.5 text-xxs text-zinc-500 truncate" title={p.businessNames.join(', ')}>{p.businessNames.join(', ')}</div>
            ) : null}
          </div>
        )
      },
    },
    {
      accessorKey: 'when',
      header: 'When',
      cell: ({ row }) => (
        <div>
          <div className="text-sm tabular-nums text-zinc-900">{fmtDate(row.original.when)}</div>
          <div className="text-xxs font-mono tabular-nums text-zinc-400">{fmtTime(row.original.when)}</div>
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
      meta: { className: 'text-right font-mono tabular-nums text-sm text-zinc-900', headerClassName: 'text-right' },
      cell: ({ row }) => row.original.amount != null ? fmtMoney2(row.original.amount) : <span className="text-zinc-400">—</span>,
    },
    {
      id: 'abn',
      header: 'ABN',
      enableSorting: false,
      cell: ({ row }) => row.original.abn
        ? <span className="text-xxs font-mono tabular-nums text-zinc-500">{row.original.abn}</span>
        : <span className="text-sm text-zinc-400">—</span>,
    },
    {
      id: 'card',
      header: 'Card',
      enableSorting: false,
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
          <Link to={`/admin/renewals/${p.renewals[0].id}`} className="text-sm font-medium text-brand-700 hover:text-brand-800 whitespace-nowrap">
            {p.renewals.length > 1 ? `${p.renewals.length} renewals →` : 'Renewal →'}
          </Link>
        ) : null
      },
    },
  ], [])

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Payments"
        subtitle="Every Stripe attempt — succeeded charges and the declines customers hit at checkout."
        right={<RefreshButton onClick={() => void refetch()} busy={isFetching} />}
      />

      <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
        <StatTile kicker="30D" label="Collected" value={fmtMoney0(stats.succeededValue30d)} sub={`${stats.succeeded30d} payment${stats.succeeded30d === 1 ? '' : 's'}`} tone="emerald" />
        <StatTile kicker="30D" label="Failed attempts" value={String(stats.failed30d)} tone={stats.failed30d > 0 ? 'red' : 'zinc'} />
        <StatTile kicker="ALL TIME" label="Collected" value={fmtMoney0(stats.succeededValue)} sub={`${stats.succeededCount} payment${stats.succeededCount === 1 ? '' : 's'}`} />
      </div>

      {/* Filter toolbar — every facet is resolved server-side with live counts */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => { setPage(1); setSearchInput(e.target.value) }}
          placeholder="ABN, email, name…"
          className="h-9 w-56"
        />
        <FacetedFilter title="Result" options={resultOptions} selected={results} onChange={withPageReset(setResults)} />
        <FacetedFilter title="Card brand" options={brandOptions} selected={brands} onChange={withPageReset(setBrands)} />
        <DateRangeFilter dateFrom={dateFrom} dateTo={dateTo} onFrom={withPageReset(setDateFrom)} onTo={withPageReset(setDateTo)} />
        {hasActiveFilters ? (
          <Button variant="ghost" size="sm" className="h-9" onClick={resetFilters}>
            Reset
            <X className="h-4 w-4" />
          </Button>
        ) : null}
      </div>

      <div className="mt-4">
        <DataTable columns={columns} data={items} empty={<EmptyState title="No payments match the current filters." />} />
        <Pagination page={page} pageSize={PAGE_SIZE} total={data?.totalCount ?? 0} onPage={setPage} />
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
        <Button variant="outline" size="sm" className="h-9 border-dashed whitespace-nowrap">
          <CalendarRange className="h-4 w-4" />
          Dates
          {active ? <span className="font-mono text-xs text-muted-foreground">{dateFrom || '…'} → {dateTo || '…'}</span> : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-64 space-y-3" align="start">
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">From</div>
          <Input type="date" value={dateFrom} onChange={(e) => onFrom(e.target.value)} className="font-mono tabular-nums" />
        </div>
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">To</div>
          <Input type="date" value={dateTo} onChange={(e) => onTo(e.target.value)} className="font-mono tabular-nums" />
        </div>
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
    <div>
      <div className="flex items-center gap-1.5">
        {p.kind === 'succeeded'
          ? <StatusPill tone="emerald">SUCCEEDED</StatusPill>
          : <StatusPill tone="red">FAILED</StatusPill>}
        {p.error ? (
          <button onClick={() => onError(p.error ?? '')} className="inline-flex items-center justify-center h-5 w-5 rounded-full bg-red-50 text-red-600 hover:bg-red-100" title="View error">
            <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
            </svg>
          </button>
        ) : null}
      </div>
      {p.error ? <div className="mt-1 text-xxs text-red-700 max-w-[16rem] truncate" title={p.error}>{p.error}</div> : null}
    </div>
  )
}

function CardCell({ p }: { p: Payment }) {
  if (p.cardLast4) {
    return <span className="text-xxs font-mono text-zinc-600 whitespace-nowrap">{(p.cardBrand ?? 'card').toUpperCase()} •••• {p.cardLast4}</span>
  }
  if (p.cardholderName) {
    return <span className="text-xxs font-mono text-zinc-500 truncate max-w-[10rem] inline-block" title={p.cardholderName}>{p.cardholderName}</span>
  }
  return <span className="text-xxs font-mono text-zinc-400">—</span>
}
