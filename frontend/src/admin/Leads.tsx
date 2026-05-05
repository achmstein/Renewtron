import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import AdminPage from './AdminPage'

type Item = {
  id: string
  abn: string
  fullName: string
  email: string
  mobileNumber: string
  createdAt: string
  outcome: string
  reminderOptIn: boolean
  convertedToRenewal: boolean
}

function getInitials(name: string) {
  if (!name) return '?'
  const parts = name.split(' ').filter(Boolean)
  if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
  return name.slice(0, 2).toUpperCase()
}

function formatAbn(abn: string) {
  const clean = (abn ?? '').replace(/\s+/g, '')
  if (clean.length === 11) return `${clean.slice(0, 2)} ${clean.slice(2, 5)} ${clean.slice(5, 8)} ${clean.slice(8)}`
  return abn
}

function fmtDate(s: string) {
  const d = new Date(s)
  return d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
}
function fmtTime(s: string) {
  const d = new Date(s)
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })
}

export default function Leads() {
  const [outcomeFilter, setOutcomeFilter] = useState('')
  const [reminderFilter, setReminderFilter] = useState('')
  const [searchTerm, setSearchTerm] = useState('')
  const [data, setData] = useState<{ totalCount: number; convertedCount: number; notDueCount: number; reminderCount: number; items: Item[] } | null>(null)

  const load = useMemo(() => async () => {
    const r = await api.admin.leads({
      outcome: outcomeFilter || undefined,
      reminder: reminderFilter || undefined,
      search: searchTerm || undefined,
    })
    setData(r)
  }, [outcomeFilter, reminderFilter, searchTerm])

  useEffect(() => { void load() }, [load])

  const totalCount = data?.totalCount ?? 0
  const convertedCount = data?.convertedCount ?? 0
  const notDueCount = data?.notDueCount ?? 0
  const reminderCount = data?.reminderCount ?? 0
  const leads = data?.items ?? []

  const headerActions = (
    <div className="hidden md:flex items-baseline gap-5 bureau-mono text-[11px]" style={{ letterSpacing: '0.06em', textTransform: 'uppercase' }}>
      <span><strong style={{ color: 'var(--ink)' }}>{totalCount}</strong> <span style={{ color: 'var(--ledger)' }}>total</span></span>
      <span style={{ color: 'var(--hairline-deep)' }}>|</span>
      <span><strong style={{ color: 'var(--verdict)' }}>{convertedCount}</strong> <span style={{ color: 'var(--ledger)' }}>conv.</span></span>
      <span style={{ color: 'var(--hairline-deep)' }}>|</span>
      <span><strong style={{ color: 'var(--caution)' }}>{notDueCount}</strong> <span style={{ color: 'var(--ledger)' }}>not due</span></span>
      <span style={{ color: 'var(--hairline-deep)' }}>|</span>
      <span><strong style={{ color: 'var(--info)' }}>{reminderCount}</strong> <span style={{ color: 'var(--ledger)' }}>rem.</span></span>
    </div>
  )

  return (
    <AdminPage title="Leads" subtitle="Customer enquiries entered into the registry — track, convert, follow up." classification="Pipeline" actions={headerActions}>
      <div className="space-y-6">
        {/* Filter row */}
        <div className="bureau-card p-4">
          <div className="bureau-label mb-3">Filter</div>
          <div className="flex flex-wrap items-end gap-4">
            <div className="min-w-[180px]">
              <label className="bureau-label mb-1.5 block">Outcome</label>
              <select value={outcomeFilter} onChange={(e) => setOutcomeFilter(e.target.value)} className="bureau-select">
                <option value="">All Outcomes</option>
                <option value="RenewalCompleted">Converted</option>
                <option value="RenewalAvailable">Available</option>
                <option value="NotDueForRenewal">Not Due</option>
                <option value="RenewalInProgress">In Progress</option>
                <option value="NoBusinessNames">No Names</option>
                <option value="Pending">Pending</option>
              </select>
            </div>
            <div className="min-w-[160px]">
              <label className="bureau-label mb-1.5 block">Reminder</label>
              <select value={reminderFilter} onChange={(e) => setReminderFilter(e.target.value)} className="bureau-select">
                <option value="">All</option>
                <option value="true">Opted In</option>
                <option value="false">Not Opted In</option>
              </select>
            </div>
            <div className="flex-1 min-w-[220px]">
              <label className="bureau-label mb-1.5 block">Search</label>
              <input
                type="text"
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                placeholder="ABN, name, or email…"
                className="bureau-input"
              />
            </div>
          </div>
        </div>

        {/* Leads Table */}
        <div className="bureau-card">
          <div className="overflow-x-auto">
            <table className="bureau-table">
              <thead>
                <tr>
                  <th>Lead</th>
                  <th>ABN</th>
                  <th>Outcome</th>
                  <th>Reminder</th>
                  <th>Created</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {!leads.length ? (
                  <tr>
                    <td colSpan={6} className="bureau-meta py-12 text-center">No leads on file.</td>
                  </tr>
                ) : leads.map((lead) => (
                  <tr key={lead.id}>
                    <td>
                      <div className="flex items-center gap-3">
                        <div
                          className="flex h-9 w-9 shrink-0 items-center justify-center bureau-mono text-[11px] font-semibold uppercase"
                          style={{
                            background: 'var(--paper-deep)',
                            border: '1px solid var(--hairline-deep)',
                            color: 'var(--ink)',
                            letterSpacing: '0.04em',
                          }}
                        >
                          {getInitials(lead.fullName)}
                        </div>
                        <div className="min-w-0">
                          <div className="truncate text-[13px] font-medium" style={{ color: 'var(--ink)' }}>{lead.fullName}</div>
                          <div className="bureau-mono truncate text-[11.5px]" style={{ color: 'var(--ledger)' }}>{lead.email}</div>
                          <div className="bureau-mono text-[11px]" style={{ color: 'var(--ledger-soft)' }}>{lead.mobileNumber}</div>
                        </div>
                      </div>
                    </td>
                    <td className="bureau-mono whitespace-nowrap text-[12.5px]" style={{ color: 'var(--ink)' }}>{formatAbn(lead.abn)}</td>
                    <td className="whitespace-nowrap">
                      <OutcomeBadge outcome={lead.outcome} />
                    </td>
                    <td className="whitespace-nowrap">
                      {lead.reminderOptIn ? (
                        <span style={{ color: 'var(--verdict)' }}>● Opted in</span>
                      ) : (
                        <span style={{ color: 'var(--ledger-soft)' }}>—</span>
                      )}
                    </td>
                    <td className="whitespace-nowrap">
                      <div style={{ color: 'var(--ink)' }}>{fmtDate(lead.createdAt)}</div>
                      <div className="bureau-mono text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtTime(lead.createdAt)}</div>
                    </td>
                    <td className="whitespace-nowrap text-right">
                      <Link
                        to={`/admin/leads/${lead.id}`}
                        className="bureau-mono text-[11px] uppercase tracking-wider"
                        style={{ color: 'var(--stamp)', borderBottom: '1px solid var(--stamp)', paddingBottom: 1 }}
                      >
                        Open
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </AdminPage>
  )
}

function OutcomeBadge({ outcome }: { outcome: string }) {
  switch (outcome) {
    case 'RenewalCompleted':
      return <span className="bureau-stamp bureau-stamp-ok">Converted</span>
    case 'RenewalAvailable':
      return <span className="bureau-stamp bureau-stamp-info">Available</span>
    case 'NotDueForRenewal':
      return <span className="bureau-stamp bureau-stamp-warn">Not Due</span>
    case 'RenewalInProgress':
      return <span className="bureau-stamp bureau-stamp-info">In Progress</span>
    case 'NoBusinessNames':
      return <span className="bureau-stamp bureau-stamp-neutral">No Names</span>
    default:
      return <span className="bureau-stamp bureau-stamp-neutral">Pending</span>
  }
}
