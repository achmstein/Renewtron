import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { KickerLabel, StatusPill, Toast, type Tone } from './_ui'
import { fmtMoney2, relativeTime } from './_utils'

type Detail = Awaited<ReturnType<typeof api.admin.renewal>>

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

function statusTone(status: string): Tone {
  switch (status) {
    case 'Completed':  return 'emerald'
    case 'Processing': return 'indigo'
    case 'Pending':    return 'amber'
    case 'Failed':     return 'red'
    default:           return 'zinc'
  }
}

export default function RenewalDetails() {
  const { id } = useParams()
  const [data, setData] = useState<Detail | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [isRetrying, setIsRetrying] = useState(false)
  const [toast, setToast] = useState<{ tone: Tone; message: string } | null>(null)
  const pollRef = useRef<number | undefined>(undefined)

  const showToast = (tone: Tone, message: string) => {
    setToast({ tone, message })
    setTimeout(() => setToast(null), 4000)
  }

  // Foreground load — toggles the full-page spinner. Use only for the first fetch
  // and explicit user actions (retry).
  const load = async () => {
    if (!id) return
    setIsLoading(true)
    try {
      const r = await api.admin.renewal(id)
      setData(r)
    } catch {
      setNotFound(true)
    } finally {
      setIsLoading(false)
    }
  }

  // Background poll — updates state silently without flashing the spinner.
  const refreshQuietly = async () => {
    if (!id) return
    try {
      const r = await api.admin.renewal(id)
      setData(r)
    } catch {
      // ignore transient errors during polling
    }
  }

  useEffect(() => { void load() }, [id]) // eslint-disable-line react-hooks/exhaustive-deps

  // Auto-poll while processing
  useEffect(() => {
    if (!data) return
    if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = undefined }
    if (data.status === 'Processing') {
      pollRef.current = window.setInterval(() => { void refreshQuietly() }, 3000)
    }
    return () => {
      if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = undefined }
    }
  }, [data?.status]) // eslint-disable-line react-hooks/exhaustive-deps

  const retry = async () => {
    if (!id) return
    setIsRetrying(true)
    try {
      const r = await api.admin.retryRenewal(id)
      showToast('emerald', r.message)
      await load()
    } catch (e) {
      showToast('red', e instanceof Error ? e.message : 'Retry failed')
    } finally {
      setIsRetrying(false)
    }
  }

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
        <h3 className="text-base font-semibold text-zinc-900">Renewal request not found</h3>
        <p className="mt-1 text-sm text-zinc-500">The renewal you're looking for doesn't exist.</p>
        <Link to="/admin/renewals" className="mt-4 inline-flex items-center text-sm font-medium text-brand-700 hover:text-brand-800">← Back to renewals</Link>
      </div>
    )
  }

  const canRetry = data.status === 'Failed' && ((data.paymentType === 'Stripe' && data.stripePayment?.paymentStatus === 'succeeded') || data.paymentType === 'External')

  return (
    <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
      {/* Top bar — back link + retry */}
      <div className="mb-6 flex items-center justify-between gap-3">
        <Link to="/admin/renewals" className="inline-flex items-center gap-1 text-xs font-mono uppercase tracking-[0.14em] text-zinc-500 hover:text-zinc-900">
          <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5L8.25 12l7.5-7.5" /></svg>
          Back to renewals
        </Link>
        {canRetry ? (
          <button
            onClick={retry}
            disabled={isRetrying}
            className="inline-flex items-center gap-2 rounded-md bg-amber-600 text-white px-3 py-2 text-sm font-medium hover:bg-amber-700 shadow-sm disabled:opacity-50 transition"
          >
            {isRetrying ? (
              <>
                <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" className="opacity-25"></circle><path fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" className="opacity-75"></path></svg>
                Retrying…
              </>
            ) : (
              <>
                <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" /></svg>
                Retry renewal
              </>
            )}
          </button>
        ) : null}
      </div>

      {/* Hero */}
      <div className="rounded-2xl bg-white p-6 ring-1 ring-zinc-200 shadow-sm">
        <div className="flex items-start justify-between gap-4 flex-wrap">
          <div className="min-w-0 flex-1">
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">
              RENEWAL · DETAIL
            </div>
            <h1 className="mt-1 text-2xl font-semibold text-zinc-900 tracking-tight truncate">{data.businessName}</h1>
            <div className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-zinc-500">
              <span className="font-mono tabular-nums">{data.abn}</span>
              <span>·</span>
              <span>{data.renewalYears}-year renewal</span>
              <span>·</span>
              <span className="font-mono tabular-nums text-zinc-900">{fmtMoney2(data.amount)}</span>
            </div>
          </div>
          <div className="text-right">
            <BigStatusPill status={data.status} />
            {data.completedAt ? (
              <div className="mt-1.5 text-xxs font-mono text-zinc-500 tabular-nums">closed {relativeTime(data.completedAt)}</div>
            ) : data.status === 'Processing' || data.status === 'Pending' ? (
              <div className="mt-1.5 text-xxs font-mono text-zinc-500 tabular-nums">started {relativeTime(data.initiatedAt)}</div>
            ) : null}
          </div>
        </div>

        {/* Status timeline */}
        <div className="mt-6">
          <Timeline data={data} />
        </div>

        {/* Error banner — when failed */}
        {data.status === 'Failed' && (data.errorMessage || data.failedAtStep) ? (
          <div className="mt-5 rounded-lg bg-red-50 ring-1 ring-red-100 p-4">
            <div className="flex items-start gap-3">
              <svg className="h-4 w-4 text-red-600 mt-0.5 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
              </svg>
              <div className="min-w-0 flex-1">
                <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-red-700">FAILED{data.failedAtStep ? ` · ${data.failedAtStep}` : ''}</div>
                {data.errorMessage ? <div className="mt-1 text-sm text-red-900 break-words font-mono">{data.errorMessage}</div> : null}
              </div>
            </div>
          </div>
        ) : null}
      </div>

      {/* Detail cards */}
      <div className="mt-6 grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Section kicker="CUSTOMER" title="Customer & lead">
          {data.lead ? (
            <Link to={`/admin/leads/${data.lead.id}`} className="block rounded-lg border border-zinc-200 p-3 hover:border-brand-200 hover:bg-brand-50/30 transition">
              <div className="text-sm font-medium text-zinc-900">{data.lead.fullName}</div>
              <div className="text-xs font-mono text-zinc-500 mt-0.5">{data.lead.email}</div>
              <div className="mt-2 text-xxs font-mono text-brand-700">View lead →</div>
            </Link>
          ) : (
            <Row label="Email" value={data.email ?? '—'} />
          )}
          <Row label="Mobile" value={data.mobileNumber ?? '—'} mono />
          <Row label="Date of birth" value={data.dateOfBirth ? fmtDob(data.dateOfBirth) : '—'} mono />
          <Row label="IP address" value={data.ipAddress ?? '—'} mono />
          <Row label="Initiated by" value={<InitiatedByBadge value={data.initiatedByLabel} />} />
        </Section>

        <Section kicker="RENEWAL" title="Renewal info">
          <Row label="Request ID" value={<span className="break-all">{data.id}</span>} mono />
          <Row label="Initiated" value={<span title={fmtDateTime(data.initiatedAt)}>{fmtDateTime(data.initiatedAt)}</span>} mono />
          {data.completedAt ? <Row label="Completed" value={fmtDateTime(data.completedAt)} mono /> : null}
          <Row label="Period" value={`${data.renewalYears} year${data.renewalYears > 1 ? 's' : ''}`} />
          <Row label="Source" value={<SourcePill source={data.source} />} />
        </Section>

        <Section kicker="PAYMENT" title="Payment">
          <Row label="Method" value={
            data.paymentType === 'External'
              ? <StatusPill tone="zinc">EXTERNAL</StatusPill>
              : <StatusPill tone="indigo">STRIPE</StatusPill>
          } />
          <Row label="Amount" value={<span className="text-base font-semibold text-zinc-900 font-mono tabular-nums">{fmtMoney2(data.amount)}</span>} />
          {data.stripePayment ? (
            <>
              <Row label="Payment intent" value={<span className="break-all">{data.stripePayment.paymentIntentId}</span>} mono />
              <Row label="Status" value={
                data.stripePayment.paymentStatus === 'succeeded'
                  ? <StatusPill tone="emerald">SUCCEEDED</StatusPill>
                  : <StatusPill tone="red">{data.stripePayment.paymentStatus.toUpperCase()}</StatusPill>
              } />
              {data.stripePayment.paidAt ? <Row label="Paid at" value={fmtDateTime(data.stripePayment.paidAt)} mono /> : null}
            </>
          ) : null}
        </Section>

        <Section kicker="ASIC" title="ASIC transaction">
          <Row label="Reference" value={data.transactionReference ?? '—'} mono />
          <Row label="Hosted token" value={<span className="break-all">{data.hostedTokenizationId ?? '—'}</span>} mono />
          {data.failedAtStep ? <Row label="Failed at step" value={<StatusPill tone="red">{data.failedAtStep}</StatusPill>} /> : null}
        </Section>

        {data.stripePayment?.cardLast4 ? (
          <Section kicker="CARD" title="Customer card on file">
            <Row label="Cardholder" value={data.stripePayment.cardholderName ?? '—'} />
            <Row label="Number" value={`•••• •••• •••• ${data.stripePayment.cardLast4}`} mono />
            <Row label="Brand" value={data.stripePayment.cardBrand?.toUpperCase() ?? '—'} mono />
            <Row label="Expiry" value={`${data.stripePayment.cardExpMonth ?? '--'} / ${data.stripePayment.cardExpYear ?? '----'}`} mono />
          </Section>
        ) : null}
      </div>

      {toast ? (
        <div className="fixed bottom-6 right-6 z-50 fade-in"><Toast tone={toast.tone} message={toast.message} /></div>
      ) : null}
    </div>
  )
}

function BigStatusPill({ status }: { status: string }) {
  const tone = statusTone(status)
  const map: Record<Tone, string> = {
    emerald: 'bg-emerald-50 text-emerald-700 ring-emerald-100',
    indigo:  'bg-indigo-50 text-indigo-700 ring-indigo-100',
    amber:   'bg-amber-50 text-amber-700 ring-amber-100',
    red:     'bg-red-50 text-red-700 ring-red-100',
    zinc:    'bg-zinc-100 text-zinc-700 ring-zinc-200',
  }
  return (
    <span className={`inline-flex items-center rounded-md px-3 py-1.5 text-xs font-mono font-medium tracking-[0.14em] ring-1 ring-inset uppercase ${map[tone]}`}>
      {status}
    </span>
  )
}

function Timeline({ data }: { data: Detail }) {
  // Steps:
  //   Initiated  →  Processing  →  Completed (or Failed)
  // Each step has a tone (zinc=upcoming, emerald=done, indigo=current, red=failed).
  const initiatedAt = data.initiatedAt
  const completedAt = data.completedAt
  const status = data.status
  const failed = status === 'Failed'

  type Step = { label: string; tone: Tone; subline?: string; current?: boolean }
  const steps: Step[] = [
    { label: 'Initiated', tone: 'emerald', subline: relativeTime(initiatedAt) },
    {
      label: 'Processing',
      tone: status === 'Pending' ? 'amber' : status === 'Processing' ? 'indigo' : failed ? 'red' : 'emerald',
      current: status === 'Pending' || status === 'Processing',
      subline: status === 'Processing' ? 'in progress' : status === 'Pending' ? 'queued' : completedAt ? relativeTime(completedAt) : '',
    },
    failed
      ? { label: 'Failed', tone: 'red', subline: data.failedAtStep ?? 'see error below', current: true }
      : { label: 'Completed', tone: status === 'Completed' ? 'emerald' : 'zinc', subline: completedAt ? relativeTime(completedAt) : '', current: status === 'Completed' },
  ]

  const dotMap: Record<Tone, string> = { emerald: 'bg-emerald-500', indigo: 'bg-indigo-500', amber: 'bg-amber-500', red: 'bg-red-500', zinc: 'bg-zinc-300' }
  const ringMap: Record<Tone, string> = { emerald: 'ring-emerald-100', indigo: 'ring-indigo-100', amber: 'ring-amber-100', red: 'ring-red-100', zinc: 'ring-zinc-100' }

  return (
    <div className="rounded-lg bg-zinc-50 ring-1 ring-zinc-100 p-4">
      <div className="flex items-start justify-between gap-4">
        {steps.map((s, i) => (
          <div key={i} className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-2">
              <span className={`h-2.5 w-2.5 rounded-full ${dotMap[s.tone]} ring-4 ${ringMap[s.tone]} ${s.current ? 'pulse-ring' : ''}`} />
              {i < steps.length - 1 ? <span className={`h-px flex-1 ${s.tone === 'zinc' ? 'bg-zinc-200' : 'bg-zinc-300'}`} /> : null}
            </div>
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-700">{s.label}</div>
            {s.subline ? <div className="text-xxs font-mono text-zinc-400 tabular-nums truncate">{s.subline}</div> : null}
          </div>
        ))}
      </div>
    </div>
  )
}

function SourcePill({ source }: { source: string }) {
  switch (source) {
    case 'Renewtron':  return <StatusPill tone="emerald">DIRECT</StatusPill>
    case 'Ontraport':  return <StatusPill tone="indigo">ONTRAPORT</StatusPill>
    case 'BulkUpload': return <StatusPill tone="zinc">BULK</StatusPill>
    default:           return <StatusPill tone="zinc">{source.toUpperCase()}</StatusPill>
  }
}

function InitiatedByBadge({ value }: { value: string }) {
  switch (value) {
    case 'Admin':    return <StatusPill tone="indigo">ADMIN</StatusPill>
    case 'Customer': return <StatusPill tone="zinc">CUSTOMER</StatusPill>
    case 'System':   return <StatusPill tone="amber">SYSTEM</StatusPill>
    default:         return <StatusPill tone="zinc">{value.toUpperCase()}</StatusPill>
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
