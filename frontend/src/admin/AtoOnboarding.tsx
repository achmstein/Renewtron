import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { Section, StatTile, StatusPill } from './_shared'

type Row = Awaited<ReturnType<typeof api.admin.atoOnboarding>>['items'][number]

function fmtDateTime(s: string | null | undefined) {
  if (!s) return '—'
  const d = new Date(s)
  return `${d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })} ${d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })}`
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

  // Reload on filter change
  useEffect(() => { load() }, [statusFilter])  // eslint-disable-line react-hooks/exhaustive-deps

  // Poll every 10s
  useEffect(() => {
    const t = setInterval(load, 10000)
    return () => clearInterval(t)
  }, [statusFilter, search])  // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <AdminPage
      title="ATO Onboarding"
      subtitle="Clients enqueued to the ATO portal after a successful renewal."
      classification="Onboarding · Live"
    >
      <div className="space-y-6">
        <div className="grid grid-cols-1 gap-px border border-[var(--hairline)] bg-[var(--hairline)] sm:grid-cols-2 lg:grid-cols-4">
          <StatTile label="Total"     value={stats.totalCount}      kicker="A.01" />
          <StatTile label="In flight" value={stats.pendingCount}   tone="caution" kicker="A.02" />
          <StatTile label="Completed" value={stats.completedCount} tone="verdict" kicker="A.03" />
          <StatTile label="Failed"    value={stats.failedCount}    tone="stamp"   kicker="A.04" />
        </div>

        <Section
          kicker="B"
          title="Onboarding Records"
          dense
          actions={loading ? <span className="bureau-meta"><span className="bureau-spin inline-block">↻</span> Loading</span> : null}
        >
          <div className="border-b border-[var(--hairline)] px-4 py-3 sm:px-5 flex flex-wrap items-end gap-3" style={{ background: 'var(--paper-deep)' }}>
            <div className="min-w-[180px]">
              <label className="bureau-label mb-1.5 block">Status</label>
              <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} className="bureau-select">
                <option value="">All Statuses</option>
                <option value="Pending">Pending</option>
                <option value="InProgress">In Progress</option>
                <option value="AwaitingAuth">Awaiting Auth</option>
                <option value="Completed">Completed</option>
                <option value="Failed">Failed</option>
              </select>
            </div>
            <div className="flex-1 min-w-[240px]">
              <label className="bureau-label mb-1.5 block">Search</label>
              <input
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') load() }}
                placeholder="ABN, email, or job ID"
                className="bureau-input"
              />
            </div>
            <button onClick={load} className="bureau-btn">⌕ Search</button>
          </div>

          <div className="overflow-x-auto">
            <table className="bureau-table">
              <thead>
                <tr>
                  <th>Status</th>
                  <th>Business</th>
                  <th>ABN</th>
                  <th>Customer</th>
                  <th>Renewed</th>
                  <th>ATO Completed</th>
                  <th>Job ID</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.renewalRequestId}>
                    <td className="whitespace-nowrap"><StatusPill status={r.atoStatus ?? 'Pending'} /></td>
                    <td className="text-[13px]" style={{ color: 'var(--ink)' }}>{r.businessName}</td>
                    <td className="bureau-mono whitespace-nowrap text-[12px]" style={{ color: 'var(--ink)' }}>{r.abn}</td>
                    <td>
                      <div style={{ color: 'var(--ink)' }}>{r.fullName ?? '—'}</div>
                      <div className="bureau-mono text-[11px]" style={{ color: 'var(--ledger)' }}>{r.email}</div>
                    </td>
                    <td className="bureau-mono whitespace-nowrap text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtDateTime(r.completedAt ?? r.initiatedAt)}</td>
                    <td className="bureau-mono whitespace-nowrap text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtDateTime(r.atoCompletedAt)}</td>
                    <td className="bureau-mono whitespace-nowrap text-[11px]" style={{ color: 'var(--ledger)' }}>{r.atoJobId ?? '—'}</td>
                    <td className="whitespace-nowrap text-right">
                      <Link
                        to={`/admin/ato-onboarding/${r.renewalRequestId}`}
                        className="bureau-mono text-[11px] uppercase tracking-wider"
                        style={{ color: 'var(--stamp)', borderBottom: '1px solid var(--stamp)', paddingBottom: 1 }}
                      >
                        Open
                      </Link>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && !loading ? (
                  <tr><td colSpan={8} className="bureau-meta py-12 text-center">No ATO onboarding records.</td></tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </Section>
      </div>
    </AdminPage>
  )
}
