import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import AdminPage from './AdminPage'
import { DefList, DefRow, Section, StatusPill } from './_shared'

type Detail = Awaited<ReturnType<typeof api.admin.search>>

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
function fmtShortDateTime(s: string) {
  const d = new Date(s)
  const date = d.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })
  const time = d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })
  return `${date} ${time}`
}

export default function SearchDetails() {
  const { id } = useParams()
  const [data, setData] = useState<Detail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)

  useEffect(() => {
    if (!id) return
    setIsLoading(true)
    api.admin.search(id)
      .then(setData)
      .catch(() => setNotFound(true))
      .finally(() => setIsLoading(false))
  }, [id])

  const back = <Link to="/admin/searches" className="bureau-btn">← Back to Searches</Link>

  if (isLoading) {
    return (
      <AdminPage title="Search Dossier" classification="Searches" actions={back}>
        <div className="bureau-meta animate-pulse">Loading search…</div>
      </AdminPage>
    )
  }

  if (notFound || !data) {
    return (
      <AdminPage title="Search Not Found" classification="Searches" actions={back}>
        <div className="bureau-card p-6">
          <p className="text-[14px]" style={{ color: 'var(--ink)' }}>The search record you're looking for doesn't exist.</p>
        </div>
      </AdminPage>
    )
  }

  return (
    <AdminPage
      title={`Search · ABN ${formatAbn(data.abn)}`}
      caseId={`SRCH-${data.id.slice(0, 8).toUpperCase()}`}
      classification="Searches · Dossier"
      actions={back}
    >
      <div className="space-y-6">
        {data.lead ? (
          <Section
            kicker="A"
            title="Lead Information"
            actions={
              <Link
                to={`/admin/leads/${data.lead.id}`}
                className="bureau-mono text-[11px] uppercase tracking-wider"
                style={{ color: 'var(--stamp)', borderBottom: '1px solid var(--stamp)', paddingBottom: 1 }}
              >
                View lead →
              </Link>
            }
          >
            <DefList>
              <DefRow label="Name">{data.lead.fullName}</DefRow>
              <DefRow label="Email">
                <a href={`mailto:${data.lead.email}`} className="bureau-link">{data.lead.email}</a>
              </DefRow>
              <DefRow label="Mobile">{data.lead.mobileNumber}</DefRow>
              <DefRow label="Outcome"><StatusPill status={data.lead.outcome} /></DefRow>
            </DefList>
          </Section>
        ) : null}

        <Section kicker="B" title="Search Information">
          <DefList>
            <DefRow label="ABN" mono>{formatAbn(data.abn)}</DefRow>
            <DefRow label="Searched At">{fmtDateTime(data.searchedAt)}</DefRow>
            <DefRow label="IP Address" mono>{data.ipAddress ?? '—'}</DefRow>
            <DefRow label="Session ID" mono>{data.sessionId ?? '—'}</DefRow>
            <DefRow label="Results">{data.resultsCount} business name(s)</DefRow>
            <DefRow label="Initiated By">{data.initiatedBy}</DefRow>
            <DefRow label="Status"><StatusPill status={data.success ? 'OK' : 'Failed'} /></DefRow>
            {data.errorMessage ? (
              <DefRow label="Error" full>
                <span style={{ color: 'var(--stamp)' }}>{data.errorMessage}</span>
              </DefRow>
            ) : null}
          </DefList>
        </Section>

        <Section kicker="C" title={`Business Names · ${data.results.length}`} dense>
          {data.results.length > 0 ? (
            <ul className="divide-y divide-[var(--hairline)]">
              {data.results.map((r) => (
                <li key={r.id} className="px-5 py-4">
                  <h3 className="bureau-display text-[15px] font-semibold" style={{ color: 'var(--ink)' }}>{r.businessName}</h3>
                  <DefList>
                    <DefRow label="Result ID" mono>{r.id}</DefRow>
                    <DefRow label="Account">{r.accountNumber}</DefRow>
                    <DefRow label="Registration Date">{r.registrationDate}</DefRow>
                  </DefList>
                  {r.renewalRequest ? (
                    <div className="mt-3 flex items-center justify-between border-t border-[var(--hairline)] pt-3">
                      <div className="flex items-center gap-3">
                        <StatusPill status={r.renewalRequest.status} />
                        <span className="bureau-mono text-[11px]" style={{ color: 'var(--ledger)' }}>
                          {fmtShortDateTime(r.renewalRequest.initiatedAt)} · {r.renewalRequest.renewalYears}yr · ${r.renewalRequest.amount.toFixed(2)}
                        </span>
                      </div>
                      <Link
                        to={`/admin/renewals/${r.renewalRequest.id}`}
                        className="bureau-mono text-[11px] uppercase tracking-wider"
                        style={{ color: 'var(--stamp)', borderBottom: '1px solid var(--stamp)', paddingBottom: 1 }}
                      >
                        Open dossier →
                      </Link>
                    </div>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : (
            <div className="bureau-meta py-12 text-center">No business names returned for this search.</div>
          )}
        </Section>
      </div>
    </AdminPage>
  )
}
