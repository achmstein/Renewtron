import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

type Row = Awaited<ReturnType<typeof api.admin.atoOnboarding>>['items'][number]

function fmtDateTime(s: string | null | undefined) {
  if (!s) return ''
  const d = new Date(s)
  return `${d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })} ${d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })}`
}

function StatusBadge({ status }: { status?: string | null }) {
  const cls = 'inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium'
  switch (status) {
    case 'Completed': return <span className={`${cls} bg-green-100 text-green-800`}>Completed</span>
    case 'InProgress': return <span className={`${cls} bg-blue-100 text-blue-800`}>In Progress</span>
    case 'Pending': return <span className={`${cls} bg-yellow-100 text-yellow-800`}>Pending</span>
    case 'AwaitingAuth': return <span className={`${cls} bg-orange-100 text-orange-800`}>Awaiting Auth</span>
    case 'Failed': return <span className={`${cls} bg-red-100 text-red-800`}>Failed</span>
    default: return <span className={`${cls} bg-gray-100 text-gray-700`}>{status ?? '—'}</span>
  }
}

export default function AtoOnboarding() {
  const [rows, setRows] = useState<Row[]>([])
  const [stats, setStats] = useState({ totalCount: 0, pendingCount: 0, completedCount: 0, failedCount: 0 })
  const [statusFilter, setStatusFilter] = useState('')
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(false)

  async function load() {
    setLoading(true)
    try {
      const data = await api.admin.atoOnboarding({
        status: statusFilter || undefined,
        search: search || undefined,
        take: 200,
      })
      setRows(data.items)
      setStats({
        totalCount: data.totalCount,
        pendingCount: data.pendingCount,
        completedCount: data.completedCount,
        failedCount: data.failedCount,
      })
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [statusFilter])  // eslint-disable-line react-hooks/exhaustive-deps

  // Poll every 10s
  useEffect(() => {
    const t = setInterval(load, 10000)
    return () => clearInterval(t)
  }, [statusFilter, search])  // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <>
      <header className="relative bg-white shadow-sm">
        <div className="mx-auto max-w-7xl px-4 py-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between gap-4">
            <div>
              <h1 className="text-lg/6 font-semibold text-gray-900">ATO Onboarding</h1>
              <p className="mt-1 text-sm text-gray-500">Clients enqueued to the ATO portal after a successful renewal.</p>
            </div>
          </div>
        </div>
      </header>

      <main>
        <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          {/* Summary cards */}
          <div className="mb-6 grid grid-cols-2 gap-4 sm:grid-cols-4">
            <SummaryCard label="Total" value={stats.totalCount} />
            <SummaryCard label="In flight" value={stats.pendingCount} tone="amber" />
            <SummaryCard label="Completed" value={stats.completedCount} tone="green" />
            <SummaryCard label="Failed" value={stats.failedCount} tone="red" />
          </div>

          {/* Filters + table */}
          <div className="overflow-hidden rounded-lg bg-white shadow">
            <div className="px-4 py-3 sm:px-6 border-b border-gray-200 flex flex-wrap items-center gap-3">
              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                className="rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm"
              >
                <option value="">All statuses</option>
                <option value="Pending">Pending</option>
                <option value="InProgress">In Progress</option>
                <option value="AwaitingAuth">Awaiting Auth</option>
                <option value="Completed">Completed</option>
                <option value="Failed">Failed</option>
              </select>
              <input
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') load() }}
                placeholder="ABN, email, or job ID"
                className="flex-1 min-w-[220px] rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm"
              />
              <button
                onClick={load}
                className="inline-flex items-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50"
              >
                Search
              </button>
              {loading ? <span className="text-xs text-gray-500">Loading…</span> : null}
            </div>

            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Status</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Business Name</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">ABN</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Customer</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Renewed</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">ATO Completed</th>
                    <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Job ID</th>
                    <th className="relative px-6 py-3"><span className="sr-only">Actions</span></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200 bg-white">
                  {rows.length === 0 && !loading ? (
                    <tr><td colSpan={8} className="px-6 py-12 text-center text-sm text-gray-500">No ATO onboarding records.</td></tr>
                  ) : rows.map((r) => (
                    <tr key={r.renewalRequestId} className="hover:bg-gray-50">
                      <td className="whitespace-nowrap px-6 py-4"><StatusBadge status={r.atoStatus} /></td>
                      <td className="px-6 py-4 text-sm text-gray-900">{r.businessName}</td>
                      <td className="whitespace-nowrap px-6 py-4 text-sm font-mono text-gray-700">{r.abn}</td>
                      <td className="px-6 py-4 text-sm text-gray-700">
                        <div>{r.fullName ?? '—'}</div>
                        <div className="text-xs text-gray-500">{r.email}</div>
                      </td>
                      <td className="whitespace-nowrap px-6 py-4 text-xs text-gray-500">{fmtDateTime(r.completedAt ?? r.initiatedAt)}</td>
                      <td className="whitespace-nowrap px-6 py-4 text-xs text-gray-500">{fmtDateTime(r.atoCompletedAt)}</td>
                      <td className="whitespace-nowrap px-6 py-4 text-xs font-mono text-gray-500">{r.atoJobId}</td>
                      <td className="whitespace-nowrap px-6 py-4 text-right text-sm">
                        <Link to={`/admin/ato-onboarding/${r.renewalRequestId}`} className="text-blue-600 hover:text-blue-900">View</Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </main>
    </>
  )
}

function SummaryCard({ label, value, tone = 'default' }: { label: string; value: number; tone?: 'default' | 'green' | 'amber' | 'red' }) {
  const valueColor =
    tone === 'green' ? 'text-green-600' :
    tone === 'amber' ? 'text-amber-600' :
    tone === 'red'   ? 'text-red-600' :
    'text-gray-900'
  return (
    <div className="overflow-hidden rounded-lg bg-white shadow">
      <div className="px-4 py-5">
        <dt className="truncate text-sm font-medium text-gray-500">{label}</dt>
        <dd className={`mt-1 text-2xl font-semibold ${valueColor}`}>{value}</dd>
      </div>
    </div>
  )
}
