import { type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ChevronLeft, CircleCheck, Loader2 } from 'lucide-react'
import { api } from '../api/client'
import { FunnelPills, KickerLabel, StatusPill, type Tone } from './_ui'
import { fmtMoney2, relativeTime } from './_utils'

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

function fmtDob(s: string) {
  const d = new Date(s)
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  return `${dd}/${mm}/${d.getFullYear()}`
}

export default function LeadDetails() {
  const { id } = useParams()
  const { data, isPending, isError } = useQuery({
    queryKey: ['admin-lead', id],
    queryFn: () => api.admin.lead(id!),
    enabled: !!id,
  })

  if (isPending) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 lg:px-8 text-center">
        <Loader2 className="mx-auto h-8 w-8 animate-spin text-brand-600" />
        <p className="mt-3 text-sm text-zinc-500">Loading…</p>
      </div>
    )
  }

  if (isError || !data) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 lg:px-8 text-center">
        <h3 className="text-base font-semibold text-zinc-900">Lead not found</h3>
        <p className="mt-1 text-sm text-zinc-500">The lead you're looking for doesn't exist.</p>
        <Link to="/admin/leads" className="mt-4 inline-flex items-center text-sm font-medium text-brand-700 hover:text-brand-800">← Back to leads</Link>
      </div>
    )
  }

  const hasPaid = data.renewalRequests.some((r) => r.status === 'Completed')
  const hasInflight = data.renewalRequests.some((r) => r.status === 'Pending' || r.status === 'Processing')
  const hasFailed = data.renewalRequests.some((r) => r.status === 'Failed')

  const funnelStages: Array<{ label: string; tone: Tone; active: boolean }> = [
    { label: 'SRCH', tone: 'emerald', active: !!data.searchLog },
    { label: 'LEAD', tone: 'emerald', active: true },
    hasFailed && !hasPaid
      ? { label: 'FAIL', tone: 'red',     active: true }
      : { label: 'PAID', tone: hasPaid ? 'emerald' : hasInflight ? 'amber' : 'zinc', active: hasPaid || hasInflight },
  ]

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      {/* Top bar */}
      <div className="mb-6">
        <Link to="/admin/leads" className="inline-flex items-center gap-1 text-xs font-mono uppercase tracking-[0.14em] text-zinc-500 hover:text-zinc-900">
          <ChevronLeft className="h-3 w-3" />
          Back to leads
        </Link>
      </div>

      {/* Hero */}
      <div className="rounded-2xl bg-white p-6 ring-1 ring-zinc-200 shadow-sm">
        <div className="flex items-start justify-between gap-4 flex-wrap">
          <div className="min-w-0 flex-1">
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">
              LEAD · DETAIL
            </div>
            <h1 className="mt-1 text-2xl font-semibold text-zinc-900 tracking-tight truncate">{data.fullName}</h1>
            <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-zinc-500">
              <span>{relativeTime(data.createdAt)}</span>
              <span>·</span>
              <span className="font-mono tabular-nums text-zinc-900">{formatAbn(data.abn)}</span>
              {data.email ? (<><span>·</span><span className="font-mono">{data.email}</span></>) : null}
            </div>
          </div>
          <div className="text-right">
            <BigOutcomePill outcome={data.outcome} />
            <div className="mt-2"><FunnelPills stages={funnelStages} /></div>
            {data.reminderOptIn ? (
              <div className="mt-2 inline-flex items-center gap-1 text-xxs font-mono uppercase tracking-[0.14em] text-emerald-700">
                <CircleCheck className="h-3 w-3" />
                Reminder opt-in
              </div>
            ) : null}
          </div>
        </div>

        {data.outcomeMessage ? (
          <div className="mt-5 rounded-lg bg-zinc-50 ring-1 ring-zinc-100 p-4">
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">OUTCOME MESSAGE</div>
            <div className="mt-1 text-sm text-zinc-900 break-words font-mono">{data.outcomeMessage}</div>
          </div>
        ) : null}
      </div>

      {/* Detail grid */}
      <div className="mt-6 grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Section kicker="CONTACT" title="Contact information">
          <Row label="Full name" value={data.fullName} />
          <Row label="Email" value={<a href={`mailto:${data.email}`} className="text-brand-700 hover:text-brand-800 break-all">{data.email}</a>} mono />
          <Row label="Mobile" value={<a href={`tel:${data.mobileNumber}`} className="text-brand-700 hover:text-brand-800">{data.mobileNumber}</a>} mono />
          <Row label="Date of birth" value={fmtDob(data.dateOfBirth)} mono />
        </Section>

        <Section kicker="LEAD" title="Lead info">
          <Row label="Lead ID" value={<span className="break-all">{data.id}</span>} mono />
          <Row label="ABN" value={formatAbn(data.abn)} mono />
          <Row label="Outcome" value={<OutcomePill outcome={data.outcome} />} />
          <Row label="Created" value={fmtDateTime(data.createdAt)} mono />
          {data.convertedToRenewal && data.convertedAt ? (
            <Row label="Converted" value={fmtDateTime(data.convertedAt)} mono />
          ) : null}
        </Section>

        <Section kicker="TRACKING" title="Tracking">
          <Row label="IP address" value={data.ipAddress ?? '—'} mono />
          <Row label="Session ID" value={<span className="break-all">{data.sessionId ?? '—'}</span>} mono />
          <Row label="User agent" value={<span className="break-all text-xs">{data.userAgent ?? '—'}</span>} mono />
        </Section>

        {data.searchLog ? (
          <Section kicker="SEARCH" title="ASIC search">
            <Row label="Searched" value={fmtDateTime(data.searchLog.searchedAt)} mono />
            <Row label="Status" value={data.searchLog.success
              ? <StatusPill tone="emerald">SUCCESS</StatusPill>
              : <StatusPill tone="red">FAILED</StatusPill>} />
            <Row label="Results" value={`${data.searchLog.resultsCount} business name${data.searchLog.resultsCount === 1 ? '' : 's'}`} />
            {data.searchLog.errorMessage ? (
              <Row label="Error" value={<span className="text-red-700 break-words">{data.searchLog.errorMessage}</span>} mono />
            ) : null}
          </Section>
        ) : null}
      </div>

      {/* Business names found */}
      {data.searchLog && data.searchLog.results.length > 0 ? (
        <div className="mt-6 rounded-xl bg-white p-5 ring-1 ring-zinc-200 shadow-sm">
          <div className="mb-4 flex items-end justify-between gap-3">
            <div>
              <KickerLabel>NAMES</KickerLabel>
              <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">Business names found</h2>
            </div>
            <span className="text-xs font-mono tabular-nums text-zinc-500">{data.searchLog.results.length} {data.searchLog.results.length === 1 ? 'name' : 'names'}</span>
          </div>
          <div className="space-y-3">
            {data.searchLog.results.map((r) => (
              <div key={r.id} className="rounded-lg border border-zinc-200 bg-white p-4 hover:border-zinc-300 transition">
                <div className="text-sm font-medium text-zinc-900 truncate">{r.businessName}</div>
                <div className="mt-2 grid grid-cols-2 gap-x-6 gap-y-1.5 text-xs font-mono tabular-nums text-zinc-500 max-w-md">
                  <div className="flex justify-between"><span className="text-zinc-400">acct</span><span className="text-zinc-700">{r.accountNumber}</span></div>
                  <div className="flex justify-between"><span className="text-zinc-400">reg</span><span className="text-zinc-700">{r.registrationDate}</span></div>
                </div>
              </div>
            ))}
          </div>
        </div>
      ) : null}

      {/* Renewal requests */}
      {data.renewalRequests.length > 0 ? (
        <div className="mt-6 rounded-xl bg-white p-5 ring-1 ring-zinc-200 shadow-sm">
          <div className="mb-4 flex items-end justify-between gap-3">
            <div>
              <KickerLabel>RENEWALS</KickerLabel>
              <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">Renewal requests</h2>
            </div>
            <span className="text-xs font-mono tabular-nums text-zinc-500">{data.renewalRequests.length} {data.renewalRequests.length === 1 ? 'request' : 'requests'}</span>
          </div>
          <div className="space-y-3">
            {data.renewalRequests.map((r) => (
              <Link
                key={r.id}
                to={`/admin/renewals/${r.id}`}
                className="block rounded-lg border border-zinc-200 bg-white p-4 hover:border-brand-200 hover:bg-brand-50/30 transition"
              >
                <div className="flex items-start justify-between gap-4 flex-wrap">
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium text-zinc-900 truncate">{r.businessName ?? 'N/A'}</div>
                    <div className="mt-2 grid grid-cols-3 gap-x-6 gap-y-1.5 text-xs font-mono tabular-nums text-zinc-500 max-w-md">
                      <div className="flex justify-between"><span className="text-zinc-400">amt</span><span className="text-zinc-700">{fmtMoney2(r.amount)}</span></div>
                      <div className="flex justify-between"><span className="text-zinc-400">yr</span><span className="text-zinc-700">{r.renewalYears}</span></div>
                      <div className="flex justify-between"><span className="text-zinc-400">init</span><span className="text-zinc-700">{relativeTime(r.initiatedAt)}</span></div>
                    </div>
                  </div>
                  <div className="text-right">
                    <RenewalStatusPill status={r.status} />
                    <div className="mt-1 text-xxs font-mono text-brand-700">View renewal →</div>
                  </div>
                </div>
              </Link>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  )
}

/* ───── Pills + helpers ───── */

function BigOutcomePill({ outcome }: { outcome: string }) {
  const m = outcomeMeta(outcome)
  const map: Record<Tone, string> = {
    emerald: 'bg-emerald-50 text-emerald-700 ring-emerald-100',
    indigo:  'bg-indigo-50 text-indigo-700 ring-indigo-100',
    amber:   'bg-amber-50 text-amber-700 ring-amber-100',
    red:     'bg-red-50 text-red-700 ring-red-100',
    zinc:    'bg-zinc-100 text-zinc-700 ring-zinc-200',
  }
  return (
    <span className={`inline-flex items-center rounded-md px-3 py-1.5 text-xs font-mono font-medium tracking-[0.14em] ring-1 ring-inset uppercase ${map[m.tone]}`}>
      {m.label}
    </span>
  )
}

function OutcomePill({ outcome }: { outcome: string }) {
  const m = outcomeMeta(outcome)
  return <StatusPill tone={m.tone}>{m.label}</StatusPill>
}

function outcomeMeta(outcome: string | null | undefined): { tone: Tone; label: string } {
  switch (outcome) {
    case 'RenewalCompleted':  return { tone: 'emerald', label: 'CONVERTED' }
    case 'RenewalAvailable':  return { tone: 'emerald', label: 'AVAILABLE' }
    case 'NotDueForRenewal':  return { tone: 'amber',   label: 'NOT DUE' }
    case 'RenewalInProgress': return { tone: 'indigo',  label: 'IN PROGRESS' }
    case 'NoBusinessNames':   return { tone: 'zinc',    label: 'NO NAMES' }
    case 'Pending':           return { tone: 'zinc',    label: 'PENDING' }
    default:                  return { tone: 'zinc',    label: outcome ? outcome.toUpperCase() : 'PENDING' }
  }
}

function RenewalStatusPill({ status }: { status: string }) {
  switch (status) {
    case 'Completed':  return <StatusPill tone="emerald">PAID</StatusPill>
    case 'Processing': return <StatusPill tone="indigo">PROC.</StatusPill>
    case 'Pending':    return <StatusPill tone="amber">PEND.</StatusPill>
    case 'Failed':     return <StatusPill tone="red">FAILED</StatusPill>
    default:           return <StatusPill tone="zinc">{status.toUpperCase()}</StatusPill>
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
