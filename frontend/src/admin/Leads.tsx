import { useEffect, useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { Chip, EmptyState, FunnelPills, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, Th, Toast, type Tone } from './_ui'
import { durationShort, fmtMoney0, relativeTime } from './_utils'

type LeadsResponse = Awaited<ReturnType<typeof api.admin.leads>>
type Lead = LeadsResponse['items'][number]
type Stats = LeadsResponse['stats']

export default function Leads() {
  const [data, setData] = useState<LeadsResponse | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [outcome, setOutcome] = useState('')
  const [reminder, setReminder] = useState('')
  const [search, setSearch] = useState('')
  const [hasRenewal, setHasRenewal] = useState('')
  const [winBackOpen, setWinBackOpen] = useState(false)
  const [toast, setToast] = useState<{ tone: Tone; message: string } | null>(null)

  const load = useMemo(() => async () => {
    setIsLoading(true)
    try {
      const r = await api.admin.leads({
        outcome: outcome || undefined,
        reminder: reminder || undefined,
        search: search || undefined,
        hasRenewal: hasRenewal || undefined,
      })
      setData(r)
    } finally {
      setIsLoading(false)
    }
  }, [outcome, reminder, search, hasRenewal])

  useEffect(() => { void load() }, [load])

  const leads = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()

  const activeFilters = [outcome, reminder, search, hasRenewal].filter(Boolean).length
  const winBackEnabled = stats.winBackEligible > 0

  const onWinBackSent = (count: number) => {
    setWinBackOpen(false)
    setToast({ tone: 'emerald', message: `Win-back email sent to ${count} lead${count === 1 ? '' : 's'}.` })
    setTimeout(() => setToast(null), 4000)
    void load()
  }

  const flashOutcome = (label: string) => {
    setOutcome(label === outcome ? '' : label)
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Leads"
        subtitle="Customer enquiries entered into the registry — track, convert, and follow up."
        right={
          <>
            <button
              type="button"
              onClick={() => setWinBackOpen(true)}
              disabled={!winBackEnabled}
              className={`inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition ${
                winBackEnabled
                  ? 'bg-brand-600 text-white hover:bg-brand-700 shadow-sm'
                  : 'bg-zinc-100 text-zinc-400 cursor-not-allowed'
              }`}
            >
              <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M21.75 6.75v10.5a2.25 2.25 0 01-2.25 2.25h-15a2.25 2.25 0 01-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0019.5 4.5h-15a2.25 2.25 0 00-2.25 2.25m19.5 0v.243a2.25 2.25 0 01-1.07 1.916l-7.5 4.615a2.25 2.25 0 01-2.36 0L3.32 8.91a2.25 2.25 0 01-1.07-1.916V6.75" />
              </svg>
              Send win-back
              {winBackEnabled ? <span className="ml-0.5 inline-flex items-center justify-center rounded bg-white/20 text-white text-xxs font-mono tabular-nums px-1.5 py-0.5 leading-none">{stats.winBackEligible}</span> : null}
            </button>
            <RefreshButton onClick={() => void load()} busy={isLoading} />
          </>
        }
      />

      <StatsStrip stats={stats} />

      {/* Filters */}
      <div className="mt-6 flex flex-wrap items-end gap-3">
        <FilterSelect label="Outcome" value={outcome} onChange={setOutcome} options={[
          { value: '', label: 'All outcomes' },
          { value: 'RenewalAvailable', label: 'Available' },
          { value: 'RenewalCompleted', label: 'Converted' },
          { value: 'NotDueForRenewal', label: 'Not due' },
          { value: 'RenewalInProgress', label: 'In progress' },
          { value: 'NoBusinessNames', label: 'No names' },
          { value: 'Pending', label: 'Pending' },
        ]} />
        <FilterSelect label="Reminder" value={reminder} onChange={setReminder} options={[
          { value: '', label: 'All' },
          { value: 'true', label: 'Opted in' },
          { value: 'false', label: 'Not opted in' },
        ]} />
        <FilterSelect label="Renewal" value={hasRenewal} onChange={setHasRenewal} options={[
          { value: '', label: 'All' },
          { value: 'true', label: 'Has renewal' },
          { value: 'false', label: 'No renewal' },
        ]} />
        <div className="flex-1 min-w-[220px]">
          <label className="block text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">Search</label>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="ABN, name, or email"
            className="w-full rounded-md border-zinc-300 text-sm shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2"
          />
        </div>
      </div>

      {/* Active chips */}
      {activeFilters > 0 ? (
        <div className="mt-4 flex flex-wrap items-center gap-2">
          {outcome ? <Chip label={`Outcome: ${outcome}`} onRemove={() => setOutcome('')} /> : null}
          {reminder ? <Chip label={`Reminder: ${reminder === 'true' ? 'opted in' : 'not opted in'}`} onRemove={() => setReminder('')} /> : null}
          {hasRenewal ? <Chip label={`Renewal: ${hasRenewal === 'true' ? 'yes' : 'no'}`} onRemove={() => setHasRenewal('')} /> : null}
          {search ? <Chip label={`Search: ${search}`} onRemove={() => setSearch('')} /> : null}
          <button onClick={() => { setOutcome(''); setReminder(''); setHasRenewal(''); setSearch('') }} className="text-xs font-medium text-zinc-600 hover:text-zinc-900 px-2">Clear all</button>
        </div>
      ) : null}

      {/* Table */}
      <div className="mt-6 overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="min-w-full">
            <thead className="bg-zinc-50/80 backdrop-blur">
              <tr>
                <Th>Customer · ABN</Th>
                <Th>Outcome</Th>
                <Th>Names found</Th>
                <Th>Funnel</Th>
                <Th className="text-right">Renewal</Th>
                <Th>Reminder</Th>
                <Th>Age</Th>
                <Th><span className="sr-only">Actions</span></Th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-100">
              {leads.length === 0 ? (
                <tr><td colSpan={8} className="px-4 py-12"><EmptyState title="No leads match the current filters." /></td></tr>
              ) : leads.map((l) => (
                <tr key={l.id} className="hover:bg-zinc-50 transition-colors">
                  <td className="px-4 py-3 align-top">
                    <div className="flex items-center gap-3">
                      <Avatar name={l.fullName} />
                      <div className="min-w-0">
                        <Link to={`/admin/leads/${l.id}`} className="text-sm font-medium text-zinc-900 hover:underline">{l.fullName || '—'}</Link>
                        <div className="text-xxs font-mono text-zinc-400 truncate max-w-[18rem]">{l.email}</div>
                        <div className="text-xs font-mono tabular-nums text-zinc-500 mt-0.5"><button type="button" onClick={() => flashOutcome(l.outcome)} className="hover:underline">{l.abn}</button></div>
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-3 align-top"><OutcomePill outcome={l.outcome} /></td>
                  <td className="px-4 py-3 align-top">
                    {l.searchLog == null ? (
                      <span className="text-xxs font-mono text-zinc-400">no search</span>
                    ) : l.searchLog.resultsCount === 0 ? (
                      <span className="text-xxs font-mono text-zinc-400">none</span>
                    ) : (
                      <div>
                        <div className="text-sm text-zinc-900 truncate max-w-[16rem]">{l.firstBusinessName ?? '—'}</div>
                        {l.searchLog.resultsCount > 1 ? <div className="text-xxs font-mono text-zinc-400 tabular-nums">+{l.searchLog.resultsCount - 1} more</div> : null}
                      </div>
                    )}
                  </td>
                  <td className="px-4 py-3 align-top"><FunnelPills stages={leadFunnelStages(l)} /></td>
                  <td className="px-4 py-3 align-top text-right">
                    {l.renewal ? (
                      <div>
                        <div className="text-sm font-mono tabular-nums text-zinc-900">{fmtMoney0(l.renewal.amount)}</div>
                        <RenewalStatusPill status={l.renewal.status} />
                      </div>
                    ) : <span className="text-xxs font-mono text-zinc-400">—</span>}
                  </td>
                  <td className="px-4 py-3 align-top">
                    {l.reminderOptIn ? <StatusPill tone="emerald">OPT-IN</StatusPill> : <span className="text-xxs font-mono text-zinc-400">—</span>}
                  </td>
                  <td className="px-4 py-3 align-top">
                    <div className="text-sm text-zinc-700 tabular-nums">{relativeTime(l.createdAt)}</div>
                  </td>
                  <td className="px-4 py-3 align-top text-right">
                    <Link to={`/admin/leads/${l.id}`} className="inline-flex items-center text-sm font-medium text-brand-700 hover:text-brand-800">View →</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Toast */}
      {toast ? (
        <div className="fixed bottom-6 right-6 z-50 fade-in">
          <Toast tone={toast.tone} message={toast.message} />
        </div>
      ) : null}

      {/* Win-back modal */}
      {winBackOpen ? (
        <WinBackModal
          filter={{ outcome: outcome || undefined, search: search || undefined, reminderOptIn: reminder ? reminder === 'true' : undefined }}
          onClose={() => setWinBackOpen(false)}
          onSent={onWinBackSent}
        />
      ) : null}
    </div>
  )
}

function defaultStats(): Stats {
  return {
    totalAllTime: 0,
    total30d: 0,
    converted30d: 0,
    conversionRate30d: 0,
    avgHoursToConvert: null,
    today: 0,
    yesterday: 0,
    deltaPct: null,
    daily14d: [],
    outcomeBreakdown: {},
    winBackEligible: 0,
    winBackRecoverableValue: 0,
    avgBasket: 0,
  }
}

function StatsStrip({ stats }: { stats: Stats }) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <StatTile
        kicker="ALL TIME"
        label="Total leads"
        value={stats.totalAllTime.toLocaleString()}
        sub={`${stats.total30d.toLocaleString()} in last 30d`}
      />
      <StatTile
        kicker="30D"
        label="Conversion rate"
        value={`${stats.conversionRate30d}%`}
        sub={`${stats.converted30d.toLocaleString()} converted`}
        tone={stats.conversionRate30d >= 30 ? 'emerald' : stats.conversionRate30d >= 10 ? 'amber' : 'zinc'}
      />
      <StatTile
        kicker="WIN-BACK"
        label="Recoverable"
        value={fmtMoney0(stats.winBackRecoverableValue)}
        sub={`${stats.winBackEligible.toLocaleString()} eligible · ${fmtMoney0(stats.avgBasket)} avg basket`}
        tone={stats.winBackEligible > 0 ? 'emerald' : 'zinc'}
      />
      <SparklineTile
        kicker="14D"
        label="Today"
        value={stats.today.toLocaleString()}
        sub={
          stats.avgHoursToConvert != null
            ? `${stats.yesterday.toLocaleString()} yesterday · avg ${durationShort(Number(stats.avgHoursToConvert))} to convert`
            : `${stats.yesterday.toLocaleString()} yesterday`
        }
        deltaPct={stats.deltaPct}
        data={stats.daily14d.map((d) => d.count)}
      />
    </div>
  )
}

function leadFunnelStages(l: Lead): Array<{ label: string; tone: Tone; active: boolean }> {
  const renewalStatus = l.renewal?.status
  const paid = renewalStatus === 'Completed'
  const inflight = renewalStatus === 'Pending' || renewalStatus === 'Processing'
  const failed = renewalStatus === 'Failed'

  return [
    { label: 'LEAD', tone: 'emerald', active: true },
    { label: 'SRCH', tone: 'emerald', active: !!l.searchLog },
    failed
      ? { label: 'FAIL', tone: 'red',     active: true }
      : { label: 'PAID', tone: paid ? 'emerald' : inflight ? 'amber' : 'zinc', active: paid || inflight },
  ]
}

function OutcomePill({ outcome }: { outcome: string }) {
  switch (outcome) {
    case 'RenewalAvailable':  return <StatusPill tone="emerald">RENEW. AVAIL.</StatusPill>
    case 'RenewalCompleted':  return <StatusPill tone="emerald">CONVERTED</StatusPill>
    case 'NotDueForRenewal':  return <StatusPill tone="zinc">NOT DUE</StatusPill>
    case 'RenewalInProgress': return <StatusPill tone="indigo">IN PROGRESS</StatusPill>
    case 'NoBusinessNames':   return <StatusPill tone="amber">NO NAMES</StatusPill>
    case 'Pending':           return <StatusPill tone="zinc">PENDING</StatusPill>
    default:                  return <StatusPill tone="zinc">{outcome.toUpperCase()}</StatusPill>
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

function FilterSelect({ label, value, onChange, options }: { label: string; value: string; onChange: (v: string) => void; options: Array<{ value: string; label: string }> }) {
  return (
    <div>
      <label className="block text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">{label}</label>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="rounded-md border-zinc-300 text-sm shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2"
      >
        {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    </div>
  )
}

function Avatar({ name }: { name: string }) {
  const initials = (() => {
    if (!name) return '?'
    const parts = name.split(' ').filter(Boolean)
    if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
    return name.slice(0, 2).toUpperCase()
  })()
  return (
    <div className="h-9 w-9 shrink-0 rounded-full bg-zinc-100 ring-1 ring-zinc-200 flex items-center justify-center">
      <span className="text-xxs font-mono font-medium text-zinc-600">{initials}</span>
    </div>
  )
}

/* ───────── Win-back modal ───────── */

function WinBackModal({ filter, onClose, onSent }: {
  filter: { outcome?: string; search?: string; reminderOptIn?: boolean }
  onClose: () => void
  onSent: (count: number) => void
}) {
  const [preview, setPreview] = useState<Awaited<ReturnType<typeof api.admin.winBackPreview>> | null>(null)
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    const run = async () => {
      setLoading(true)
      setError(null)
      try {
        const r = await api.admin.winBackPreview(filter)
        if (!cancelled) setPreview(r)
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Preview failed')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    void run()
    return () => { cancelled = true }
  }, [JSON.stringify(filter)]) // eslint-disable-line react-hooks/exhaustive-deps

  const onConfirm = async () => {
    if (!preview) return
    setSending(true)
    setError(null)
    try {
      const r = await api.admin.winBackSend(filter)
      onSent(r.enqueued)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Send failed')
    } finally {
      setSending(false)
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-[100]" role="dialog" aria-modal="true">
      <div className="absolute inset-0 bg-zinc-950/70 backdrop-blur-sm" onClick={onClose} />
      <div className="absolute inset-0 overflow-y-auto">
        <div className="flex min-h-full items-center justify-center p-4">
          <div className="relative w-full max-w-xl rounded-xl bg-white shadow-xl">
            <div className="px-6 py-5 border-b border-zinc-100">
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">WIN-BACK</div>
              <h2 className="mt-0.5 text-lg font-semibold text-zinc-900 tracking-tight">Send win-back email</h2>
              <p className="mt-1 text-sm text-zinc-500">A follow-up email to leads who found a renewable name but never paid.</p>
            </div>
            <div className="px-6 py-5">
              {loading ? (
                <p className="text-sm text-zinc-500">Loading recipient preview…</p>
              ) : error ? (
                <p className="text-sm text-red-700">{error}</p>
              ) : preview ? (
                <PreviewBody preview={preview} />
              ) : null}
            </div>
            <div className="px-6 py-4 border-t border-zinc-100 flex items-center justify-end gap-2">
              <button onClick={onClose} className="inline-flex items-center rounded-md bg-white px-3 py-2 text-sm font-medium text-zinc-700 ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 transition">Cancel</button>
              <button
                onClick={onConfirm}
                disabled={!preview || preview.recipientCount === 0 || sending}
                className="inline-flex items-center gap-2 rounded-md bg-brand-600 text-white px-4 py-2 text-sm font-medium hover:bg-brand-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                {sending ? 'Sending…' : preview ? `Send to ${preview.recipientCount}` : 'Send'}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  )
}

function PreviewBody({ preview }: { preview: Awaited<ReturnType<typeof api.admin.winBackPreview>> }) {
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-4">
        <div>
          <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">Recipients</div>
          <div className="mt-1 text-2xl font-semibold tabular-nums text-zinc-900">{preview.recipientCount.toLocaleString()}</div>
          <div className="text-xxs font-mono text-zinc-400 truncate">{preview.sampleNames.length > 0 ? `e.g. ${preview.sampleNames.join(' · ')}` : '—'}</div>
        </div>
        <div>
          <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">Recoverable</div>
          <div className="mt-1 text-2xl font-semibold tabular-nums text-brand-700">{fmtMoney0(preview.recoverableValue)}</div>
          <div className="text-xxs font-mono text-zinc-400">{fmtMoney0(preview.avgBasket)} avg basket × recipients</div>
        </div>
      </div>

      <div>
        <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 mb-1">Subject</div>
        <div className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-3 py-2 text-sm text-zinc-900 font-medium">{preview.subject}</div>
      </div>

      <div>
        <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 mb-1">Body preview</div>
        <pre className="rounded-md bg-zinc-50 ring-1 ring-zinc-200 px-3 py-2 text-xs text-zinc-700 font-mono whitespace-pre-wrap max-h-48 overflow-y-auto">{preview.bodyPreview}</pre>
        <p className="mt-1 text-xxs font-mono text-zinc-400">Edit the template in Settings → Win-back. Merge tags: {'{{FullName}}'}, {'{{Abn}}'}, {'{{BusinessName}}'}, {'{{Email}}'}.</p>
      </div>
    </div>
  )
}

