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
  const cls = 'inline-flex rounded-full px-2 text-xs font-semibold leading-5'
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

  useEffect(() => { load() }, [statusFilter])
  // Poll every 10s
  useEffect(() => {
    const t = setInterval(load, 10000)
    return () => clearInterval(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter, search])

  return (
    <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-6 space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-gray-900">ATO Onboarding History</h1>
        <p className="mt-1 text-sm text-gray-500">Clients enqueued to the ATO portal after a successful renewal.</p>
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <div className="rounded-md border border-gray-200 bg-white px-4 py-3">
          <div className="text-xs uppercase tracking-wide text-gray-500">Total</div>
          <div className="mt-1 text-2xl font-semibold">{stats.totalCount}</div>
        </div>
        <div className="rounded-md border border-gray-200 bg-white px-4 py-3">
          <div className="text-xs uppercase tracking-wide text-gray-500">In flight</div>
          <div className="mt-1 text-2xl font-semibold text-yellow-600">{stats.pendingCount}</div>
        </div>
        <div className="rounded-md border border-gray-200 bg-white px-4 py-3">
          <div className="text-xs uppercase tracking-wide text-gray-500">Completed</div>
          <div className="mt-1 text-2xl font-semibold text-green-600">{stats.completedCount}</div>
        </div>
        <div className="rounded-md border border-gray-200 bg-white px-4 py-3">
          <div className="text-xs uppercase tracking-wide text-gray-500">Failed</div>
          <div className="mt-1 text-2xl font-semibold text-red-600">{stats.failedCount}</div>
        </div>
      </div>

      <div className="rounded-md border border-gray-200 bg-white">
        <div className="px-4 py-3 border-b border-gray-200 flex items-center gap-3">
          <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} className="rounded border-gray-300 text-sm">
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
            placeholder="Search by ABN, email, or job ID"
            className="flex-1 rounded border-gray-300 text-sm"
          />
          <button onClick={load} className="rounded-md bg-white border border-gray-300 px-3 py-1 text-sm hover:bg-gray-50">Search</button>
          {loading ? <span className="text-xs text-gray-500">Loading…</span> : null}
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200 text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-2 text-left font-medium text-gray-500">Status</th>
                <th className="px-4 py-2 text-left font-medium text-gray-500">Business Name</th>
                <th className="px-4 py-2 text-left font-medium text-gray-500">ABN</th>
                <th className="px-4 py-2 text-left font-medium text-gray-500">Customer</th>
                <th className="px-4 py-2 text-left font-medium text-gray-500">Renewed</th>
                <th className="px-4 py-2 text-left font-medium text-gray-500">ATO Completed</th>
                <th className="px-4 py-2 text-left font-medium text-gray-500">Job ID</th>
                <th className="px-4 py-2"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {rows.map((r) => (
                <tr key={r.renewalRequestId} className="hover:bg-gray-50">
                  <td className="px-4 py-2"><StatusBadge status={r.atoStatus} /></td>
                  <td className="px-4 py-2 text-gray-900">{r.businessName}</td>
                  <td className="px-4 py-2 font-mono text-xs text-gray-700">{r.abn}</td>
                  <td className="px-4 py-2 text-gray-700">
                    <div>{r.fullName ?? '—'}</div>
                    <div className="text-xs text-gray-500">{r.email}</div>
                  </td>
                  <td className="px-4 py-2 text-xs text-gray-500">{fmtDateTime(r.completedAt ?? r.initiatedAt)}</td>
                  <td className="px-4 py-2 text-xs text-gray-500">{fmtDateTime(r.atoCompletedAt)}</td>
                  <td className="px-4 py-2 font-mono text-xs text-gray-500">{r.atoJobId}</td>
                  <td className="px-4 py-2 text-right">
                    <Link to={`/admin/ato-onboarding/${r.renewalRequestId}`} className="text-indigo-600 hover:underline">Details</Link>
                  </td>
                </tr>
              ))}
              {rows.length === 0 && !loading ? (
                <tr><td colSpan={8} className="px-4 py-6 text-center text-gray-500">No ATO onboarding records.</td></tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
