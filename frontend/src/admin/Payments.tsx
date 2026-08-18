import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { ErrorModal, Pagination } from './_components'
import { EmptyState, PageHeader, RefreshButton, StatTile, StatusPill, Th } from './_ui'
import { fmtDate, fmtMoney0, fmtMoney2, fmtTime } from './_utils'

type PaymentsResponse = Awaited<ReturnType<typeof api.admin.payments>>
type Payment = PaymentsResponse['items'][number]
type Stats = PaymentsResponse['stats']

type StatusFilter = '' | 'succeeded' | 'failed'
const PAGE_SIZE = 20

export default function Payments() {
  const [page, setPage] = useState(1)
  const [data, setData] = useState<PaymentsResponse | null>(null)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('')
  const [search, setSearch] = useState('')
  const [searchInput, setSearchInput] = useState('')
  const [errorModal, setErrorModal] = useState<string | null>(null)

  const load = useMemo(() => async () => {
    setIsRefreshing(true)
    try {
      const r = await api.admin.payments({
        status: statusFilter || undefined,
        search: search || undefined,
        page,
        pageSize: PAGE_SIZE,
      })
      setData(r)
    } finally {
      setIsRefreshing(false)
    }
  }, [statusFilter, search, page])

  useEffect(() => { void load() }, [load])

  const items = data?.items ?? []
  const stats: Stats = data?.stats ?? { succeededCount: 0, succeededValue: 0, failedCount: 0, succeeded30d: 0, succeededValue30d: 0, failed30d: 0 }

  const applySearch = () => { setPage(1); setSearch(searchInput.trim()) }

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Payments"
        subtitle="Every Stripe attempt — succeeded charges and the declines customers hit at checkout."
        right={<RefreshButton onClick={() => void load()} busy={isRefreshing} />}
      />

      <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
        <StatTile kicker="30D" label="Collected" value={fmtMoney0(stats.succeededValue30d)} sub={`${stats.succeeded30d} payment${stats.succeeded30d === 1 ? '' : 's'}`} tone="emerald" />
        <StatTile kicker="30D" label="Failed attempts" value={String(stats.failed30d)} tone={stats.failed30d > 0 ? 'red' : 'zinc'} />
        <StatTile kicker="ALL TIME" label="Collected" value={fmtMoney0(stats.succeededValue)} sub={`${stats.succeededCount} payment${stats.succeededCount === 1 ? '' : 's'}`} />
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-2">
        {([['', 'All'], ['succeeded', 'Succeeded'], ['failed', 'Failed']] as Array<[StatusFilter, string]>).map(([value, label]) => (
          <button
            key={label}
            type="button"
            onClick={() => { setPage(1); setStatusFilter(value) }}
            className={`inline-flex items-center rounded-md px-3 py-1.5 text-sm font-medium ring-1 ring-inset transition ${
              statusFilter === value
                ? 'bg-zinc-900 text-white ring-zinc-900'
                : 'bg-white text-zinc-700 ring-zinc-300 hover:bg-zinc-50'
            }`}
          >
            {label}
          </button>
        ))}
        <div className="ml-auto flex items-center gap-2">
          <input
            type="text"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') applySearch() }}
            placeholder="ABN, email, name…"
            className="block w-56 rounded-md border-zinc-300 text-sm shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2"
          />
          <button type="button" onClick={applySearch} className="inline-flex items-center rounded-md bg-white px-3 py-2 text-sm font-medium text-zinc-700 ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 transition">Search</button>
        </div>
      </div>

      <div className="mt-6 overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="min-w-full">
            <thead className="bg-zinc-50/80 backdrop-blur">
              <tr>
                <Th>When</Th>
                <Th>Status</Th>
                <Th className="text-right">Amount</Th>
                <Th>Customer</Th>
                <Th>ABN · Names</Th>
                <Th>Card</Th>
                <Th><span className="sr-only">Details</span></Th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-100">
              {items.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-12"><EmptyState title="No payments match the current filters." /></td></tr>
              ) : items.map((p, i) => <PaymentRow key={`${p.paymentIntentId ?? 'fail'}-${p.when}-${i}`} p={p} onError={setErrorModal} />)}
            </tbody>
          </table>
        </div>
      </div>

      <Pagination page={page} pageSize={PAGE_SIZE} total={data?.totalCount ?? 0} onPage={setPage} />

      <ErrorModal open={errorModal !== null} message={errorModal ?? ''} onClose={() => setErrorModal(null)} title="Payment error details" />
    </div>
  )
}

function PaymentRow({ p, onError }: { p: Payment; onError: (m: string) => void }) {
  return (
    <tr className="hover:bg-zinc-50 transition-colors">
      <td className="px-4 py-3 align-top">
        <div className="text-sm tabular-nums text-zinc-900">{fmtDate(p.when)}</div>
        <div className="text-xxs font-mono tabular-nums text-zinc-400">{fmtTime(p.when)}</div>
      </td>
      <td className="px-4 py-3 align-top">
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
      </td>
      <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-900">
        {p.amount != null ? fmtMoney2(p.amount) : <span className="text-zinc-400">—</span>}
      </td>
      <td className="px-4 py-3 align-top">
        {p.leadId ? (
          <Link to={`/admin/leads/${p.leadId}`} className="block text-sm font-medium text-zinc-900 hover:underline truncate max-w-[14rem]">{p.customerName ?? 'Lead'}</Link>
        ) : (
          <div className="text-sm text-zinc-900 truncate max-w-[14rem]">{p.customerName ?? '—'}</div>
        )}
        {p.email ? <div className="text-xxs font-mono text-zinc-400 truncate max-w-[14rem]">{p.email}</div> : null}
      </td>
      <td className="px-4 py-3 align-top">
        {p.abn ? <div className="text-xxs font-mono tabular-nums text-zinc-500">{p.abn}</div> : null}
        {p.businessNames.length > 0 ? (
          <div className="text-sm text-zinc-900 truncate max-w-[16rem]" title={p.businessNames.join(', ')}>{p.businessNames.join(', ')}</div>
        ) : p.abn ? null : <span className="text-sm text-zinc-400">—</span>}
      </td>
      <td className="px-4 py-3 align-top">
        {p.cardLast4 ? (
          <span className="text-xxs font-mono text-zinc-600">{(p.cardBrand ?? 'card').toUpperCase()} •••• {p.cardLast4}</span>
        ) : p.cardholderName ? (
          <span className="text-xxs font-mono text-zinc-500 truncate max-w-[10rem] inline-block" title={p.cardholderName}>{p.cardholderName}</span>
        ) : (
          <span className="text-xxs font-mono text-zinc-400">—</span>
        )}
      </td>
      <td className="px-4 py-3 align-top text-right">
        {p.renewals.length > 0 ? (
          <Link to={`/admin/renewals/${p.renewals[0].id}`} className="text-sm font-medium text-brand-700 hover:text-brand-800">
            {p.renewals.length > 1 ? `${p.renewals.length} renewals →` : 'Renewal →'}
          </Link>
        ) : null}
      </td>
    </tr>
  )
}
