import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { Chip, EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, Th, Toast, type Tone } from './_ui'
import { durationShort, fmtDate, fmtTime, relativeTime } from './_utils'

type Response = Awaited<ReturnType<typeof api.admin.atoOnboarding>>
type Stats = Response['stats']

export default function AtoOnboarding() {
  const [data, setData] = useState<Response | null>(null)
  const [statusFilter, setStatusFilter] = useState('')
  const [search, setSearch] = useState('')
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [retryingId, setRetryingId] = useState<string | null>(null)
  const [bulkRetrying, setBulkRetrying] = useState(false)
  const [toast, setToast] = useState<{ tone: Tone; message: string } | null>(null)

  const showToast = (tone: Tone, message: string) => {
    setToast({ tone, message })
    setTimeout(() => setToast(null), 4000)
  }

  const load = async () => {
    setIsRefreshing(true)
    try {
      setData(await api.admin.atoOnboarding({
        status: statusFilter || undefined,
        search: search || undefined,
        take: 200,
      }))
    } finally {
      setIsRefreshing(false)
    }
  }

  useEffect(() => { void load() }, [statusFilter])  // eslint-disable-line react-hooks/exhaustive-deps

  // Poll every 10s
  useEffect(() => {
    const t = setInterval(load, 10_000)
    return () => clearInterval(t)
  }, [statusFilter, search])  // eslint-disable-line react-hooks/exhaustive-deps

  const items = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const failedCount = data?.failedCount ?? 0

  const retryOne = async (id: string) => {
    setRetryingId(id)
    try {
      const r = await api.admin.retryAtoOnboarding(id)
      showToast('emerald', r.message ?? 'Re-enqueued.')
      await load()
    } catch (e) {
      showToast('red', e instanceof Error ? e.message : 'Retry failed')
    } finally {
      setRetryingId(null)
    }
  }

  const retryAllFailed = async () => {
    setBulkRetrying(true)
    try {
      const r = await api.admin.retryAllFailedAtoOnboarding()
      showToast('emerald', `Re-queued ${r.retried} failed onboardings.`)
      await load()
    } catch (e) {
      showToast('red', e instanceof Error ? e.message : 'Bulk retry failed')
    } finally {
      setBulkRetrying(false)
    }
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="COMPLIANCE"
        title="ATO onboarding"
        subtitle="Clients enqueued to the ATO portal after a successful renewal."
        right={
          <>
            {failedCount > 0 ? (
              <button
                onClick={retryAllFailed}
                disabled={bulkRetrying}
                className="inline-flex items-center gap-2 rounded-md bg-amber-600 text-white px-3 py-2 text-sm font-medium hover:bg-amber-700 shadow-sm disabled:opacity-50 transition"
              >
                {bulkRetrying ? 'Retrying…' : `Retry ${failedCount} failed`}
              </button>
            ) : null}
            <RefreshButton onClick={() => void load()} busy={isRefreshing} />
          </>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatTile
          kicker="30D"
          label="Success rate"
          value={`${stats.successRate30d}%`}
          sub={`${stats.completed30d.toLocaleString()} of ${stats.total30d.toLocaleString()}`}
          tone={stats.successRate30d >= 90 ? 'emerald' : stats.successRate30d >= 70 ? 'amber' : 'red'}
        />
        <StatTile
          kicker="AVG"
          label="Onboarding time"
          value={stats.avgOnboardingMinutes != null ? durationShort(Number(stats.avgOnboardingMinutes) / 60) : '—'}
          sub="renewal complete → ATO complete"
        />
        <StatTile
          kicker=">24H"
          label="Stuck"
          value={stats.stuck24h.toLocaleString()}
          sub="renewal completed, ATO not done"
          tone={stats.stuck24h > 0 ? 'red' : 'emerald'}
        />
        <SparklineTile
          kicker="14D"
          label="Today"
          value={stats.today.toLocaleString()}
          sub={`${stats.yesterday.toLocaleString()} yesterday`}
          deltaPct={stats.deltaPct}
          data={stats.daily14d.map((d) => d.count)}
        />
      </div>

      {/* Filters */}
      <div className="mt-6 flex flex-wrap items-end gap-3">
        <FilterSelect label="Status" value={statusFilter} onChange={setStatusFilter} options={[
          { value: '', label: 'All statuses' },
          { value: 'Pending', label: 'Pending' },
          { value: 'InProgress', label: 'In Progress' },
          { value: 'AwaitingAuth', label: 'Awaiting Auth' },
          { value: 'Completed', label: 'Completed' },
          { value: 'Failed', label: 'Failed' },
        ]} />
        <div className="flex-1 min-w-[220px]">
          <label className="block text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">Search</label>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') void load() }}
            placeholder="ABN, email, or job ID"
            className="w-full rounded-md border-zinc-300 text-sm font-mono shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2"
          />
        </div>
      </div>

      {(statusFilter || search) ? (
        <div className="mt-4 flex flex-wrap items-center gap-2">
          {statusFilter ? <Chip label={`Status ${statusFilter}`} onRemove={() => setStatusFilter('')} /> : null}
          {search ? <Chip label={`Search ${search}`} onRemove={() => setSearch('')} /> : null}
        </div>
      ) : null}

      <div className="mt-6 overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="min-w-full">
            <thead className="bg-zinc-50/80 backdrop-blur">
              <tr>
                <Th>Status</Th>
                <Th>Business · ABN</Th>
                <Th>Customer</Th>
                <Th>Renewed</Th>
                <Th>Time in flight</Th>
                <Th>Job ID</Th>
                <Th><span className="sr-only">Actions</span></Th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-100">
              {items.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-12"><EmptyState title="No ATO onboarding records." /></td></tr>
              ) : items.map((r) => (
                <tr key={r.renewalRequestId} className="hover:bg-zinc-50 transition-colors">
                  <td className="px-4 py-3 align-top"><AtoStatusPill status={r.atoStatus} /></td>
                  <td className="px-4 py-3 align-top">
                    <div className="text-sm font-medium text-zinc-900 truncate max-w-[16rem]">{r.businessName}</div>
                    <div className="text-xxs font-mono tabular-nums text-zinc-500">{r.abn}</div>
                  </td>
                  <td className="px-4 py-3 align-top">
                    <div className="text-sm text-zinc-700">{r.fullName ?? '—'}</div>
                    <div className="text-xxs font-mono text-zinc-400 truncate max-w-[14rem]">{r.email ?? ''}</div>
                  </td>
                  <td className="px-4 py-3 align-top">
                    <div className="text-sm text-zinc-700 tabular-nums">{r.completedAt ? relativeTime(r.completedAt) : '—'}</div>
                    <div className="text-xxs font-mono text-zinc-400 tabular-nums">{r.completedAt ? `${fmtDate(r.completedAt)} ${fmtTime(r.completedAt)}` : ''}</div>
                  </td>
                  <td className="px-4 py-3 align-top">
                    {r.timeInFlightHours != null ? (
                      <span className={`text-sm tabular-nums ${r.timeInFlightHours > 24 ? 'text-red-700 font-medium' : 'text-zinc-700'}`}>
                        {durationShort(Number(r.timeInFlightHours))}
                      </span>
                    ) : <span className="text-xxs font-mono text-zinc-400">—</span>}
                  </td>
                  <td className="px-4 py-3 align-top text-xxs font-mono text-zinc-500 truncate max-w-[14rem]">{r.atoJobId}</td>
                  <td className="px-4 py-3 align-top text-right">
                    <div className="flex items-center justify-end gap-3">
                      {r.atoStatus === 'Failed' ? (
                        <button onClick={() => retryOne(r.renewalRequestId)} disabled={retryingId === r.renewalRequestId} className="text-sm font-medium text-amber-700 hover:text-amber-800 disabled:opacity-50">
                          {retryingId === r.renewalRequestId ? 'Retrying…' : 'Retry'}
                        </button>
                      ) : null}
                      <Link to={`/admin/ato-onboarding/${r.renewalRequestId}`} className="text-sm font-medium text-brand-700 hover:text-brand-800">View →</Link>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {toast ? (
        <div className="fixed bottom-6 right-6 z-50 fade-in"><Toast tone={toast.tone} message={toast.message} /></div>
      ) : null}
    </div>
  )
}

function defaultStats(): Stats {
  return {
    successRate30d: 0, completed30d: 0, total30d: 0, avgOnboardingMinutes: null,
    stuck24h: 0, today: 0, yesterday: 0, deltaPct: null, daily14d: [],
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

function AtoStatusPill({ status }: { status?: string | null }) {
  switch (status) {
    case 'Completed':    return <StatusPill tone="emerald">COMPLETED</StatusPill>
    case 'Failed':       return <StatusPill tone="red">FAILED</StatusPill>
    case 'AwaitingAuth': return <StatusPill tone="amber">AWAITING</StatusPill>
    case 'InProgress':   return <StatusPill tone="indigo">IN PROGRESS</StatusPill>
    case 'Pending':      return <StatusPill tone="amber">PENDING</StatusPill>
    default:             return <StatusPill tone="zinc">{(status ?? '—').toUpperCase()}</StatusPill>
  }
}

