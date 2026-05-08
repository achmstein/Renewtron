import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { ErrorModal } from './_components'
import { EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, Th, Toast, type Tone } from './_ui'
import { fmtMoney0, fmtMoney2, relativeTime } from './_utils'

type SalesResponse = Awaited<ReturnType<typeof api.admin.ontraportSales>>
type Sale = SalesResponse['items'][number]
type Stats = SalesResponse['stats']

type DueFilter = 'overdue' | '7d' | '30d' | 'all'

const filledStatuses = ['Synced', 'WaitingForRenewalWindow', 'RenewalQueued', 'RenewalFailed']

export default function OntraportSales() {
  const [data, setData] = useState<SalesResponse | null>(null)
  const [isSyncing, setIsSyncing] = useState(false)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [isProcessing, setIsProcessing] = useState(false)
  const [dueFilter, setDueFilter] = useState<DueFilter>('30d')
  const [toast, setToast] = useState<{ tone: Tone; message: string } | null>(null)
  const [errorModal, setErrorModal] = useState<{ title: string; body: string } | null>(null)

  const showToast = (tone: Tone, message: string) => {
    setToast({ tone, message })
    setTimeout(() => setToast(null), 4000)
  }

  const load = async () => {
    setIsRefreshing(true)
    try {
      setData(await api.admin.ontraportSales())
    } finally {
      setIsRefreshing(false)
    }
  }

  useEffect(() => { void load() }, [])

  const sales = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const failedCount = data?.failedCount ?? 0

  // Sales that are still in-flight (not Completed) and have a due date — these are the "pipeline".
  const pipeline = useMemo(() => {
    const now = Date.now()
    return sales
      .filter((s) => filledStatuses.includes(s.status))
      .filter((s) => !!s.renewalDueDate)
      .map((s) => ({
        ...s,
        _daysUntil: Math.floor((new Date(s.renewalDueDate!).getTime() - now) / 86400000),
      }))
      .sort((a, b) => a._daysUntil - b._daysUntil)
  }, [sales])

  const byBucket = useMemo(() => ({
    overdue: pipeline.filter((s) => s._daysUntil < 0),
    soon7:   pipeline.filter((s) => s._daysUntil >= 0 && s._daysUntil <= 7),
    soon30:  pipeline.filter((s) => s._daysUntil >= 0 && s._daysUntil <= 30),
    all:     pipeline,
  }), [pipeline])

  const filteredPipeline = useMemo(() => {
    switch (dueFilter) {
      case 'overdue': return byBucket.overdue
      case '7d':      return byBucket.soon7
      case '30d':     return byBucket.soon30
      case 'all':     return byBucket.all
    }
  }, [dueFilter, byBucket])

  const upNext = byBucket.all.slice(0, 5)
  const upNextValue = upNext.reduce((sum, s) => sum + s.amountPaid, 0)

  const sync = async () => {
    setIsSyncing(true)
    try {
      const r = await api.admin.syncOntraport()
      showToast('emerald', r.message)
      await load()
    } catch (e) {
      showToast('red', e instanceof Error ? `Sync failed: ${e.message}` : 'Sync failed')
    } finally {
      setIsSyncing(false)
    }
  }

  const processEligible = async () => {
    setIsProcessing(true)
    try {
      const r = await api.admin.processEligibleOntraport()
      showToast('emerald', r.message)
    } catch (e) {
      showToast('red', e instanceof Error ? e.message : 'Failed to enqueue')
    } finally {
      setIsProcessing(false)
    }
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="PIPELINE"
        title="Ontraport sales"
        subtitle="Sales paid in Ontraport — fired at ASIC when the due date enters the renewal window."
        right={
          <>
            <button
              onClick={sync}
              disabled={isSyncing}
              className="inline-flex items-center gap-2 rounded-md bg-zinc-900 text-white px-3 py-2 text-sm font-medium hover:bg-zinc-800 shadow-sm disabled:opacity-50 transition"
            >
              {isSyncing ? (
                <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" className="opacity-25"></circle><path fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" className="opacity-75"></path></svg>
              ) : (
                <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" /></svg>
              )}
              Sync now
            </button>
            <button
              onClick={processEligible}
              disabled={isProcessing}
              className="inline-flex items-center rounded-md bg-brand-600 text-white px-3 py-2 text-sm font-medium hover:bg-brand-700 shadow-sm disabled:opacity-50 transition"
            >
              {isProcessing ? 'Queueing…' : 'Process eligible'}
            </button>
            <RefreshButton onClick={() => void load()} busy={isRefreshing} />
          </>
        }
      />

      {/* Stats strip */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatTile
          kicker="30D"
          label="Pipeline next 30d"
          value={fmtMoney0(stats.pipelineValueNext30d)}
          sub={`${data?.waitingCount ?? 0} waiting · ${data?.queuedCount ?? 0} queued`}
          tone="emerald"
        />
        <StatTile
          kicker="OVERDUE"
          label="Past their due date"
          value={byBucket.overdue.length.toLocaleString()}
          sub={byBucket.overdue.length > 0 ? `${fmtMoney0(byBucket.overdue.reduce((s, x) => s + x.amountPaid, 0))} unrecovered` : 'all on track'}
          tone={byBucket.overdue.length > 0 ? 'red' : 'zinc'}
        />
        <StatTile
          kicker="STATUS"
          label="Failed"
          value={failedCount.toLocaleString()}
          sub={failedCount > 0 ? 'review the table' : 'all clear'}
          tone={failedCount > 0 ? 'red' : 'zinc'}
        />
        <SparklineTile
          kicker="SYNC · 14D"
          label={stats.lastSyncAt ? relativeTime(stats.lastSyncAt) : 'No sync yet'}
          value={stats.today.toLocaleString()}
          sub={stats.nextSyncAt ? `next at ${new Date(stats.nextSyncAt).toLocaleString(undefined, { hour: '2-digit', minute: '2-digit' })} · ${stats.yesterday.toLocaleString()} yesterday` : `${stats.yesterday.toLocaleString()} yesterday`}
          deltaPct={stats.deltaPct}
          data={stats.daily14d.map((d) => d.count)}
        />
      </div>

      {/* Up next hero */}
      {upNext.length > 0 ? (
        <div className="mt-8 rounded-2xl bg-zinc-950 ring-1 ring-white/5 px-6 py-6 sm:px-8 sm:py-8 relative overflow-hidden">
          <div className="absolute inset-0 pointer-events-none" aria-hidden="true">
            <div className="absolute -right-24 -top-24 h-64 w-64 rounded-full bg-brand-500/15 blur-3xl" />
            <div className="absolute -left-24 -bottom-24 h-64 w-64 rounded-full bg-brand-400/10 blur-3xl" />
          </div>
          <div className="relative">
            <div className="flex items-end justify-between gap-4 flex-wrap mb-5">
              <div>
                <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-400">UP NEXT</div>
                <h2 className="mt-1 text-xl font-semibold text-white tracking-tight">Closest renewals due</h2>
                <p className="mt-1 text-sm text-zinc-300">{upNext.length} sales · {fmtMoney0(upNextValue)} in pipeline</p>
              </div>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3">
              {upNext.map((s) => (
                <UpNextCard key={s.id} s={s} />
              ))}
            </div>
          </div>
        </div>
      ) : null}

      {/* Due-date filter chips */}
      <div className="mt-8 flex items-end justify-between gap-3 flex-wrap">
        <div>
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">VIEW</div>
          <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">All pipeline</h2>
        </div>
        <div className="inline-flex rounded-md bg-white ring-1 ring-zinc-200 shadow-sm" role="group">
          <FilterTab active={dueFilter === 'overdue'} onClick={() => setDueFilter('overdue')} count={byBucket.overdue.length} tone="red">Overdue</FilterTab>
          <FilterTab active={dueFilter === '7d'}      onClick={() => setDueFilter('7d')}      count={byBucket.soon7.length}    tone="amber">Next 7d</FilterTab>
          <FilterTab active={dueFilter === '30d'}     onClick={() => setDueFilter('30d')}     count={byBucket.soon30.length}   tone="emerald">Next 30d</FilterTab>
          <FilterTab active={dueFilter === 'all'}     onClick={() => setDueFilter('all')}     count={byBucket.all.length}      tone="zinc">All</FilterTab>
        </div>
      </div>

      <div className="mt-4 overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="min-w-full">
            <thead className="bg-zinc-50/80 backdrop-blur">
              <tr>
                <Th>Business · ABN</Th>
                <Th>Customer</Th>
                <Th>Due</Th>
                <Th className="text-right">Amount paid</Th>
                <Th>Status</Th>
                <Th>Synced</Th>
                <Th><span className="sr-only">Actions</span></Th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-100">
              {filteredPipeline.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-12"><EmptyState title={emptyMessage(dueFilter)} /></td></tr>
              ) : filteredPipeline.map((s) => <SaleRow key={s.id} s={s} onShowError={(title, body) => setErrorModal({ title, body })} />)}
            </tbody>
          </table>
        </div>
      </div>

      {toast ? (
        <div className="fixed bottom-6 right-6 z-50 fade-in"><Toast tone={toast.tone} message={toast.message} /></div>
      ) : null}

      <ErrorModal
        open={errorModal !== null}
        title={errorModal?.title ?? 'Renewal error details'}
        message={errorModal?.body ?? ''}
        onClose={() => setErrorModal(null)}
      />
    </div>
  )
}

function emptyMessage(f: DueFilter) {
  switch (f) {
    case 'overdue': return 'No overdue sales — everything\'s on schedule.'
    case '7d':      return 'Nothing due in the next 7 days.'
    case '30d':     return 'Nothing due in the next 30 days.'
    case 'all':     return 'No Ontraport sales in the pipeline.'
  }
}

function defaultStats(): Stats {
  return {
    pipelineValueNext30d: 0, lastSyncAt: null, nextSyncAt: null,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [],
  }
}

function FilterTab({ active, count, onClick, tone, children }: { active: boolean; count: number; onClick: () => void; tone: Tone; children: React.ReactNode }) {
  const dotMap = { emerald: 'bg-emerald-500', amber: 'bg-amber-500', red: 'bg-red-500', indigo: 'bg-indigo-500', zinc: 'bg-zinc-400' }
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-2 px-3 py-2 text-sm font-medium transition first:rounded-l-md last:rounded-r-md ${
        active ? 'bg-zinc-900 text-white' : 'text-zinc-700 hover:bg-zinc-50'
      }`}
    >
      <span className={`h-1.5 w-1.5 rounded-full ${dotMap[tone]}`} />
      <span>{children}</span>
      <span className={`text-xxs font-mono tabular-nums ${active ? 'text-white/70' : 'text-zinc-400'}`}>{count.toLocaleString()}</span>
    </button>
  )
}

function UpNextCard({ s }: { s: Sale & { _daysUntil: number } }) {
  const days = s._daysUntil
  const tone: Tone = days < 0 ? 'red' : days <= 7 ? 'amber' : 'emerald'
  const toneTextMap = { emerald: 'text-emerald-300', amber: 'text-amber-300', red: 'text-red-300', indigo: 'text-indigo-300', zinc: 'text-zinc-300' }
  const toneRingMap = { emerald: 'ring-emerald-400/30', amber: 'ring-amber-400/30', red: 'ring-red-400/30', indigo: 'ring-indigo-400/30', zinc: 'ring-zinc-400/30' }

  const inner = (
    <>
      <div className={`text-xxs font-mono font-medium uppercase tracking-[0.14em] ${toneTextMap[tone]} tabular-nums`}>
        {days < 0 ? `${Math.abs(days)}D OVERDUE` : days === 0 ? 'TODAY' : `${days}D`}
      </div>
      <div className="mt-1 text-sm font-medium text-white truncate" title={s.businessName}>{s.businessName}</div>
      <div className="text-xxs font-mono text-zinc-400 truncate">{s.contactName || s.email}</div>
      <div className="mt-2 flex items-center justify-between">
        <span className="text-xs font-mono tabular-nums text-zinc-300">{fmtMoney0(s.amountPaid)}</span>
        <OntraportStatusPillDark status={s.status} />
      </div>
    </>
  )

  // When a renewal exists, the whole card deep-links into it. Otherwise it's a static tile.
  if (s.renewalRequestId) {
    return (
      <Link
        to={`/admin/renewals/${s.renewalRequestId}`}
        className={`block rounded-xl bg-white/5 ring-1 ${toneRingMap[tone]} px-3 py-3 hover:bg-white/10 hover:ring-white/30 transition`}
      >
        {inner}
      </Link>
    )
  }
  return (
    <div className={`rounded-xl bg-white/5 ring-1 ${toneRingMap[tone]} px-3 py-3`}>
      {inner}
    </div>
  )
}

function SaleRow({ s, onShowError }: { s: Sale & { _daysUntil: number }; onShowError: (title: string, body: string) => void }) {
  const days = s._daysUntil
  const daysClass = days < 0 ? 'text-red-700 font-medium' : days <= 7 ? 'text-amber-700 font-medium' : days <= 30 ? 'text-emerald-700 font-medium' : 'text-zinc-500'

  const isFailed = s.status === 'RenewalFailed'
  // Compose the error body from either the OntraportSale.errorMessage or the
  // linked RenewalRequest's full failure context. Renewal step + message is the
  // most useful for debugging — show it first if available.
  const errorBody = (() => {
    const parts: string[] = []
    if (s.renewalFailedAtStep) parts.push(`Failed at: ${s.renewalFailedAtStep}`)
    if (s.renewalErrorMessage) parts.push(s.renewalErrorMessage)
    if (parts.length === 0 && s.errorMessage) parts.push(s.errorMessage)
    return parts.join('\n\n') || 'No error details recorded.'
  })()

  return (
    <tr className="hover:bg-zinc-50 transition-colors">
      <td className="px-4 py-3 align-top">
        <div className="text-sm font-medium text-zinc-900 truncate max-w-[18rem]">{s.businessName}</div>
        <div className="text-xxs font-mono tabular-nums text-zinc-500">{s.abn}</div>
      </td>
      <td className="px-4 py-3 align-top">
        <div className="text-sm text-zinc-700">{s.contactName || '—'}</div>
        <div className="text-xxs font-mono text-zinc-400 truncate max-w-[16rem]">{s.email}</div>
      </td>
      <td className="px-4 py-3 align-top">
        <div className="text-sm text-zinc-700 tabular-nums">{s.renewalDueDate ? new Date(s.renewalDueDate).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) : '—'}</div>
        <div className={`text-xxs font-mono tabular-nums ${daysClass}`}>{days < 0 ? `${Math.abs(days)} days overdue` : days === 0 ? 'today' : `in ${days} days`}</div>
      </td>
      <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-900">{fmtMoney2(s.amountPaid)}</td>
      <td className="px-4 py-3 align-top">
        <div className="flex items-center gap-1.5">
          <OntraportStatusPill status={s.status} />
          {isFailed ? (
            <button
              type="button"
              onClick={() => onShowError(`Why ${s.businessName || 'this sale'} failed`, errorBody)}
              className="inline-flex items-center justify-center h-5 w-5 rounded-full bg-red-50 text-red-600 hover:bg-red-100 transition"
              title="Why did this fail?"
            >
              <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
              </svg>
            </button>
          ) : null}
        </div>
        {isFailed && s.renewalFailedAtStep ? (
          <div className="mt-1 text-xxs font-mono text-red-700">Failed: {s.renewalFailedAtStep}</div>
        ) : null}
      </td>
      <td className="px-4 py-3 align-top">
        <div className="text-sm text-zinc-700 tabular-nums">{relativeTime(s.syncedAt)}</div>
      </td>
      <td className="px-4 py-3 align-top text-right">
        {s.renewalRequestId ? (
          <Link
            to={`/admin/renewals/${s.renewalRequestId}`}
            className={`inline-flex items-center text-sm font-medium ${isFailed ? 'text-red-700 hover:text-red-800' : 'text-brand-700 hover:text-brand-800'}`}
          >
            {isFailed ? 'Investigate →' : 'View renewal →'}
          </Link>
        ) : (
          <span className="text-xxs font-mono text-zinc-400">—</span>
        )}
      </td>
    </tr>
  )
}

function OntraportStatusPill({ status }: { status: string }) {
  switch (status) {
    case 'Synced':                   return <StatusPill tone="zinc">SYNCED</StatusPill>
    case 'WaitingForRenewalWindow':  return <StatusPill tone="amber">WAITING</StatusPill>
    case 'RenewalQueued':            return <StatusPill tone="indigo">QUEUED</StatusPill>
    case 'RenewalCompleted':         return <StatusPill tone="emerald">COMPLETED</StatusPill>
    case 'RenewalFailed':            return <StatusPill tone="red">FAILED</StatusPill>
    case 'NotDueForRenewal':         return <StatusPill tone="zinc">NOT DUE</StatusPill>
    default:                         return <StatusPill tone="zinc">{status.toUpperCase()}</StatusPill>
  }
}

// Dark-background variant for the Up Next hero card
function OntraportStatusPillDark({ status }: { status: string }) {
  const map: Record<string, [string, string]> = {
    Synced:                   ['SYNCED',    'bg-zinc-700/40 text-zinc-200 ring-zinc-500/30'],
    WaitingForRenewalWindow:  ['WAITING',   'bg-amber-500/20 text-amber-200 ring-amber-400/30'],
    RenewalQueued:            ['QUEUED',    'bg-indigo-500/20 text-indigo-200 ring-indigo-400/30'],
    RenewalCompleted:         ['COMPLETED', 'bg-emerald-500/20 text-emerald-200 ring-emerald-400/30'],
    RenewalFailed:            ['FAILED',    'bg-red-500/20 text-red-200 ring-red-400/30'],
    NotDueForRenewal:         ['NOT DUE',   'bg-zinc-700/40 text-zinc-300 ring-zinc-500/30'],
  }
  const [label, cls] = map[status] ?? [status.toUpperCase(), 'bg-zinc-700/40 text-zinc-200 ring-zinc-500/30']
  return <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-xxs font-mono font-medium tracking-[0.12em] ring-1 ring-inset ${cls}`}>{label}</span>
}
