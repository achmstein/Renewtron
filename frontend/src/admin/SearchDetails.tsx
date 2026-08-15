import { useEffect, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { FunnelPills, KickerLabel, searchFunnelStages, StatusPill, type Tone } from './_ui'
import { fmtMoney2, relativeTime } from './_utils'

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
  return `${date} · ${time}`
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

  if (isLoading) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 lg:px-8 text-center">
        <svg className="mx-auto h-8 w-8 animate-spin text-brand-600" fill="none" viewBox="0 0 24 24">
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
        </svg>
        <p className="mt-3 text-sm text-zinc-500">Loading…</p>
      </div>
    )
  }

  if (notFound || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 lg:px-8 text-center">
        <h3 className="text-base font-semibold text-zinc-900">Search not found</h3>
        <p className="mt-1 text-sm text-zinc-500">The search you're looking for doesn't exist.</p>
        <Link to="/admin/searches" className="mt-4 inline-flex items-center text-sm font-medium text-brand-700 hover:text-brand-800">← Back to searches</Link>
      </div>
    )
  }

  const funnelStages = searchFunnelStages(data.funnel)

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      {/* Top bar */}
      <div className="mb-6">
        <Link to="/admin/searches" className="inline-flex items-center gap-1 text-xs font-mono uppercase tracking-[0.14em] text-zinc-500 hover:text-zinc-900">
          <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5L8.25 12l7.5-7.5" /></svg>
          Back to searches
        </Link>
      </div>

      {/* Hero */}
      <div className="rounded-2xl bg-white p-6 ring-1 ring-zinc-200 shadow-sm">
        <div className="flex items-start justify-between gap-4 flex-wrap">
          <div className="min-w-0 flex-1">
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">
              SEARCH · DETAIL
            </div>
            <h1 className="mt-1 text-2xl font-semibold text-zinc-900 tracking-tight font-mono tabular-nums">{formatAbn(data.abn)}</h1>
            <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-zinc-500">
              <span>{relativeTime(data.searchedAt)}</span>
              <span>·</span>
              <InitiatedByBadge value={data.initiatedBy} />
              <span>·</span>
              <span><span className="font-mono tabular-nums text-zinc-900">{data.resultsCount}</span> result{data.resultsCount === 1 ? '' : 's'}</span>
            </div>
          </div>
          <div className="text-right">
            {data.success ? <BigStatusPill tone="emerald" label="SUCCESS" /> : <BigStatusPill tone="red" label="FAILED" />}
            <div className="mt-2"><FunnelPills stages={funnelStages} /></div>
          </div>
        </div>

        {!data.success && data.errorMessage ? (
          <div className="mt-5 rounded-lg bg-red-50 ring-1 ring-red-100 p-4">
            <div className="flex items-start gap-3">
              <svg className="h-4 w-4 text-red-600 mt-0.5 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
              </svg>
              <div className="min-w-0 flex-1">
                <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-red-700">FAILURE</div>
                <div className="mt-1 text-sm text-red-900 break-words font-mono">{data.errorMessage}</div>
              </div>
            </div>
          </div>
        ) : null}
      </div>

      {/* Detail grid */}
      <div className="mt-6 grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Lead block */}
        {data.lead ? (
          <Section kicker="LEAD" title="Captured lead">
            <Link to={`/admin/leads/${data.lead.id}`} className="block rounded-lg border border-zinc-200 p-3 hover:border-brand-200 hover:bg-brand-50/30 transition">
              <div className="text-sm font-medium text-zinc-900">{data.lead.fullName}</div>
              <div className="text-xs font-mono text-zinc-500 mt-0.5">{data.lead.email}</div>
              {data.lead.mobileNumber ? <div className="text-xs font-mono text-zinc-500 tabular-nums">{data.lead.mobileNumber}</div> : null}
              <div className="mt-2 flex items-center justify-between">
                <OutcomePill outcome={data.lead.outcome} />
                <span className="text-xxs font-mono text-brand-700">View lead →</span>
              </div>
            </Link>
          </Section>
        ) : (
          <Section kicker="LEAD" title="Captured lead">
            <p className="text-sm text-zinc-500">No lead was captured for this search.</p>
          </Section>
        )}

        {/* Search meta */}
        <Section kicker="META" title="Search metadata">
          <Row label="Searched" value={fmtDateTime(data.searchedAt)} mono />
          <Row label="Initiated by" value={<InitiatedByBadge value={data.initiatedBy} />} />
          <Row label="Status" value={data.success ? <StatusPill tone="emerald">SUCCESS</StatusPill> : <StatusPill tone="red">FAILED</StatusPill>} />
          <Row label="IP address" value={data.ipAddress ?? '—'} mono />
          <Row label="Session ID" value={<span className="break-all">{data.sessionId ?? '—'}</span>} mono />
          <Row label="Search ID" value={<span className="break-all">{data.id}</span>} mono />
        </Section>
      </div>

      {/* Found names */}
      <div className="mt-6 rounded-xl bg-white p-5 ring-1 ring-zinc-200 shadow-sm">
        <div className="mb-4 flex items-end justify-between gap-3">
          <div>
            <KickerLabel>RESULTS</KickerLabel>
            <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">Business names found</h2>
          </div>
          <span className="text-xs font-mono tabular-nums text-zinc-500">{data.results.length} {data.results.length === 1 ? 'name' : 'names'}</span>
        </div>

        {data.results.length === 0 ? (
          <div className="rounded-lg border border-dashed border-zinc-200 bg-white/60 p-8 text-center">
            <p className="text-sm text-zinc-500">No business names found in this search.</p>
          </div>
        ) : (
          <div className="space-y-3">
            {data.results.map((r) => <ResultCard key={r.id} r={r} />)}
          </div>
        )}
      </div>
    </div>
  )
}

function ResultCard({ r }: { r: Detail['results'][number] }) {
  return (
    <div className="rounded-lg border border-zinc-200 bg-white p-4 hover:border-zinc-300 transition">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div className="min-w-0 flex-1">
          <div className="text-sm font-medium text-zinc-900 truncate">{r.businessName}</div>
          <div className="mt-2 grid grid-cols-2 gap-x-6 gap-y-1.5 text-xs font-mono tabular-nums text-zinc-500 max-w-md">
            <div className="flex justify-between"><span className="text-zinc-400">acct</span><span className="text-zinc-700">{r.accountNumber}</span></div>
            <div className="flex justify-between"><span className="text-zinc-400">reg</span><span className="text-zinc-700">{r.registrationDate}</span></div>
          </div>
        </div>
        {r.renewalRequest ? (
          <Link
            to={`/admin/renewals/${r.renewalRequest.id}`}
            className="rounded-lg bg-zinc-50 ring-1 ring-zinc-200 hover:ring-brand-200 hover:bg-brand-50/30 transition px-3 py-2 min-w-[14rem]"
          >
            <div className="flex items-center justify-between gap-2 mb-1">
              <KickerLabel>RENEWAL</KickerLabel>
              <RenewalStatusPill status={r.renewalRequest.status} />
            </div>
            <div className="text-sm font-mono tabular-nums text-zinc-900">{fmtMoney2(r.renewalRequest.amount)}</div>
            <div className="text-xxs font-mono text-zinc-500 tabular-nums">
              {r.renewalRequest.renewalYears}-year · {relativeTime(r.renewalRequest.initiatedAt)}
            </div>
            <div className="mt-1 text-xxs font-mono text-brand-700">View renewal →</div>
          </Link>
        ) : (
          <div className="rounded-lg border border-dashed border-zinc-200 px-3 py-2 min-w-[14rem]">
            <KickerLabel>RENEWAL</KickerLabel>
            <div className="mt-1 text-xs text-zinc-500">No renewal yet.</div>
          </div>
        )}
      </div>
    </div>
  )
}

/* ───── Pills + helpers ───── */

function BigStatusPill({ tone, label }: { tone: Tone; label: string }) {
  const map: Record<Tone, string> = {
    emerald: 'bg-emerald-50 text-emerald-700 ring-emerald-100',
    indigo:  'bg-indigo-50 text-indigo-700 ring-indigo-100',
    amber:   'bg-amber-50 text-amber-700 ring-amber-100',
    red:     'bg-red-50 text-red-700 ring-red-100',
    zinc:    'bg-zinc-100 text-zinc-700 ring-zinc-200',
  }
  return (
    <span className={`inline-flex items-center rounded-md px-3 py-1.5 text-xs font-mono font-medium tracking-[0.14em] ring-1 ring-inset ${map[tone]}`}>
      {label}
    </span>
  )
}

function OutcomePill({ outcome }: { outcome: string | null | undefined }) {
  switch (outcome) {
    case 'RenewalAvailable':  return <StatusPill tone="emerald">RENEW. AVAIL.</StatusPill>
    case 'RenewalCompleted':  return <StatusPill tone="emerald">CONVERTED</StatusPill>
    case 'NotDueForRenewal':  return <StatusPill tone="zinc">NOT DUE</StatusPill>
    case 'RenewalInProgress': return <StatusPill tone="indigo">IN PROGRESS</StatusPill>
    case 'NoBusinessNames':   return <StatusPill tone="amber">NO NAMES</StatusPill>
    case 'Pending':           return <StatusPill tone="zinc">PENDING</StatusPill>
    default:                  return <StatusPill tone="zinc">{outcome ? outcome.toUpperCase() : '—'}</StatusPill>
  }
}

function RenewalStatusPill({ status }: { status: string | null | undefined }) {
  switch (status) {
    case 'Completed':  return <StatusPill tone="emerald">PAID</StatusPill>
    case 'Processing': return <StatusPill tone="indigo">PROC.</StatusPill>
    case 'Pending':    return <StatusPill tone="amber">PEND.</StatusPill>
    case 'Failed':     return <StatusPill tone="red">FAILED</StatusPill>
    default:           return <StatusPill tone="zinc">{status ? status.toUpperCase() : '—'}</StatusPill>
  }
}

function InitiatedByBadge({ value }: { value: string | null | undefined }) {
  switch (value) {
    case 'Admin':    return <StatusPill tone="indigo">ADMIN</StatusPill>
    case 'Customer': return <StatusPill tone="zinc">CUSTOMER</StatusPill>
    case 'System':   return <StatusPill tone="amber">SYSTEM</StatusPill>
    default:         return <StatusPill tone="zinc">{value ? value.toUpperCase() : '—'}</StatusPill>
  }
}

function Section({ kicker, title, children }: { kicker: string; title: string; children: ReactNode }) {
  return (
    <div className="rounded-xl bg-white p-5 ring-1 ring-zinc-200 shadow-sm">
      <div className="mb-4">
        <KickerLabel>{kicker}</KickerLabel>
        <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">{title}</h2>
      </div>
      <dl className="space-y-3">
        {children}
      </dl>
    </div>
  )
}

function Row({ label, value, mono }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div className="grid grid-cols-[10rem_1fr] gap-3 items-baseline">
      <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">{label}</dt>
      <dd className={`text-sm text-zinc-900 ${mono ? 'font-mono tabular-nums' : ''}`}>{value}</dd>
    </div>
  )
}
