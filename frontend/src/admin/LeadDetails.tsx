import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { DefList, DefRow, Section, StatusPill } from './_shared'

type Detail = Awaited<ReturnType<typeof api.admin.lead>>

function formatAbn(abn: string) {
  const clean = (abn ?? '').replace(/\s+/g, '')
  if (clean.length === 11) return `${clean.slice(0, 2)} ${clean.slice(2, 5)} ${clean.slice(5, 8)} ${clean.slice(8)}`
  return abn
}
function fmtDateTime(s: string) {
  const d = new Date(s)
  const date = d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
  const time = d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
  return `${date} ${time}`
}
function fmtDob(s: string) {
  const d = new Date(s)
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  return `${dd}/${mm}/${d.getFullYear()}`
}
function fmtShort(s: string) {
  const d = new Date(s)
  return `${d.toLocaleDateString(undefined, { month: 'short', day: '2-digit' })} ${d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })}`
}

const outcomeLabel = (o: string) => o.replace(/([a-z])([A-Z])/g, '$1 $2')

export default function LeadDetails() {
  const { id } = useParams()
  const [data, setData] = useState<Detail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)

  useEffect(() => {
    if (!id) return
    setIsLoading(true)
    api.admin.lead(id).then(setData).catch(() => setNotFound(true)).finally(() => setIsLoading(false))
  }, [id])

  const back = (
    <Link to="/admin/leads" className="bureau-btn">← Back to Leads</Link>
  )

  if (isLoading) {
    return (
      <AdminPage title="Lead Dossier" classification="Pipeline" actions={back}>
        <div className="bureau-meta animate-pulse">Loading lead…</div>
      </AdminPage>
    )
  }

  if (notFound || !data) {
    return (
      <AdminPage title="Lead Not Found" classification="Pipeline" actions={back}>
        <div className="bureau-card p-6">
          <p className="text-[14px]" style={{ color: 'var(--ink)' }}>The lead you're looking for doesn't exist.</p>
        </div>
      </AdminPage>
    )
  }

  return (
    <AdminPage
      title={data.fullName}
      subtitle={`ABN ${formatAbn(data.abn)}`}
      caseId={`LEAD-${data.id.slice(0, 8).toUpperCase()}`}
      classification="Pipeline · Lead"
      actions={back}
    >
      <div className="space-y-6">
        {/* Outcome strip */}
        <div className="bureau-card flex flex-wrap items-center justify-between gap-3 px-5 py-4">
          <div className="bureau-display text-[18px] font-semibold" style={{ color: 'var(--ink)' }}>
            {outcomeLabel(data.outcome)}
          </div>
          <div className="flex items-center gap-3">
            <StatusPill status={data.outcome} />
            {data.reminderOptIn ? (
              <span className="bureau-stamp bureau-stamp-info">Reminder Opt-in</span>
            ) : null}
          </div>
        </div>

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <Section kicker="A" title="Contact">
            <DefList>
              <DefRow label="Full Name">{data.fullName}</DefRow>
              <DefRow label="Email">
                <a href={`mailto:${data.email}`} className="bureau-link">{data.email}</a>
              </DefRow>
              <DefRow label="Mobile">
                <a href={`tel:${data.mobileNumber}`} className="bureau-link">{data.mobileNumber}</a>
              </DefRow>
              <DefRow label="Date of Birth">{fmtDob(data.dateOfBirth)}</DefRow>
            </DefList>
          </Section>

          <Section kicker="B" title="Lead Record">
            <DefList>
              <DefRow label="Lead ID" mono>{data.id}</DefRow>
              <DefRow label="ABN" mono>{formatAbn(data.abn)}</DefRow>
              <DefRow label="Created">{fmtDateTime(data.createdAt)}</DefRow>
              {data.convertedToRenewal && data.convertedAt ? (
                <DefRow label="Converted">{fmtDateTime(data.convertedAt)}</DefRow>
              ) : null}
              {data.outcomeMessage ? (
                <DefRow label="Outcome Message" full>{data.outcomeMessage}</DefRow>
              ) : null}
            </DefList>
          </Section>

          <Section kicker="C" title="Tracking">
            <DefList>
              <DefRow label="IP Address" mono>{data.ipAddress ?? '—'}</DefRow>
              <DefRow label="Session ID" mono>{data.sessionId ?? '—'}</DefRow>
              <DefRow label="User Agent" mono full>{data.userAgent ?? '—'}</DefRow>
            </DefList>
          </Section>

          {data.searchLog ? (
            <Section kicker="D" title="ASIC Search Results">
              <DefList>
                <DefRow label="Search Date">{fmtDateTime(data.searchLog.searchedAt)}</DefRow>
                <DefRow label="Status">
                  <StatusPill status={data.searchLog.success ? 'OK' : 'Failed'} />
                </DefRow>
                <DefRow label="Results">{data.searchLog.resultsCount} business name(s)</DefRow>
                {data.searchLog.errorMessage ? (
                  <DefRow label="Error" full>
                    <span style={{ color: 'var(--stamp)' }}>{data.searchLog.errorMessage}</span>
                  </DefRow>
                ) : null}
              </DefList>

              {data.searchLog.results.length > 0 ? (
                <div className="mt-5 border-t border-[var(--hairline)] pt-4">
                  <div className="bureau-label mb-3">Business Names Found</div>
                  <ul className="divide-y divide-[var(--hairline)]">
                    {data.searchLog.results.map((r) => (
                      <li key={r.id} className="flex items-center justify-between py-2">
                        <div className="min-w-0">
                          <div className="text-[13px] font-medium" style={{ color: 'var(--ink)' }}>{r.businessName}</div>
                          <div className="bureau-mono text-[11px]" style={{ color: 'var(--ledger)' }}>Account {r.accountNumber}</div>
                        </div>
                        <div className="bureau-meta">Reg {r.registrationDate}</div>
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </Section>
          ) : null}
        </div>

        {data.renewalRequests.length > 0 ? (
          <Section kicker="E" title="Renewal Requests" dense>
            <div className="overflow-x-auto">
              <table className="bureau-table">
                <thead>
                  <tr>
                    <th>Business</th>
                    <th>Status</th>
                    <th>Amount</th>
                    <th>Years</th>
                    <th>Initiated</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {data.renewalRequests.map((r) => (
                    <tr key={r.id}>
                      <td className="text-[13px]" style={{ color: 'var(--ink)' }}>{r.businessName ?? '—'}</td>
                      <td className="whitespace-nowrap"><StatusPill status={r.status} /></td>
                      <td className="bureau-mono whitespace-nowrap" style={{ color: 'var(--ink)' }}>${r.amount.toFixed(2)}</td>
                      <td className="bureau-mono whitespace-nowrap">{r.renewalYears}yr</td>
                      <td className="bureau-mono whitespace-nowrap text-[11px]" style={{ color: 'var(--ledger)' }}>{fmtShort(r.initiatedAt)}</td>
                      <td className="whitespace-nowrap text-right">
                        <Link
                          to={`/admin/renewals/${r.id}`}
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
          </Section>
        ) : null}
      </div>
    </AdminPage>
  )
}
