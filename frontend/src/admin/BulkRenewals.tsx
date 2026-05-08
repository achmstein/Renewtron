import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import { EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, Th, Toast, type Tone } from './_ui'
import { fmtMoney0, fmtMoney2, relativeTime } from './_utils'

type BulkResponse = Awaited<ReturnType<typeof api.admin.bulkRenewals>>
type Upload = BulkResponse['items'][number]
type Stats = BulkResponse['stats']

export default function BulkRenewals() {
  const [data, setData] = useState<BulkResponse | null>(null)
  const [isUploading, setIsUploading] = useState(false)
  const [isProcessing, setIsProcessing] = useState(false)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [isRetrying, setIsRetrying] = useState(false)
  const [batchFilter, setBatchFilter] = useState('')
  const [toast, setToast] = useState<{ tone: Tone; message: string } | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const showToast = (tone: Tone, message: string) => {
    setToast({ tone, message })
    setTimeout(() => setToast(null), 5000)
  }

  const load = async () => {
    setIsRefreshing(true)
    try {
      setData(await api.admin.bulkRenewals())
    } finally {
      setIsRefreshing(false)
    }
  }
  useEffect(() => { void load() }, [])

  const upload = async (file: File) => {
    if (!file.name.toLowerCase().endsWith('.xlsx')) {
      showToast('red', 'Only .xlsx files are supported.')
      if (inputRef.current) inputRef.current.value = ''
      return
    }
    setIsUploading(true)
    try {
      const r = await api.admin.uploadBulkRenewals(file)
      let msg = `Processed ${r.totalRows} rows: ${r.importedCount} imported, ${r.skippedDuplicates} duplicates skipped, ${r.skippedInvalid} invalid.`
      if (r.errors.length > 0) msg += ' First errors: ' + r.errors.slice(0, 3).join('; ')
      showToast('emerald', msg)
      await load()
    } catch (e) {
      showToast('red', `Upload failed: ${e instanceof Error ? e.message : 'unknown error'}`)
    } finally {
      setIsUploading(false)
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  const processEligible = async () => {
    setIsProcessing(true)
    try {
      const r = await api.admin.processEligibleBulk()
      showToast('emerald', r.message)
    } catch (e) {
      showToast('red', e instanceof Error ? e.message : 'Failed')
    } finally {
      setIsProcessing(false)
    }
  }

  const retryFailed = async () => {
    setIsRetrying(true)
    try {
      const r = await api.admin.retryFailedBulk()
      showToast('emerald', `Re-queued ${r.retried} failed bulk renewals.`)
      await load()
    } catch (e) {
      showToast('red', e instanceof Error ? e.message : 'Retry failed')
    } finally {
      setIsRetrying(false)
    }
  }

  const items = data?.items ?? []
  const stats: Stats = data?.stats ?? defaultStats()
  const failedCount = data?.failedCount ?? 0

  const filtered = batchFilter ? items.filter((i) => (i.sourceFile || '(direct)') === batchFilter) : items

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="PIPELINE"
        title="Bulk renewals"
        subtitle="Renewals imported from spreadsheet uploads — auto-fire when the due date enters the renewal window."
        right={
          <>
            <input ref={inputRef} type="file" accept=".xlsx" className="hidden" onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])} />
            <button
              onClick={() => inputRef.current?.click()}
              disabled={isUploading}
              className="inline-flex items-center gap-2 rounded-md bg-zinc-900 text-white px-3 py-2 text-sm font-medium hover:bg-zinc-800 shadow-sm disabled:opacity-50 transition"
            >
              <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" /></svg>
              {isUploading ? 'Uploading…' : 'Upload .xlsx'}
            </button>
            <button
              onClick={processEligible}
              disabled={isProcessing}
              className="inline-flex items-center rounded-md bg-brand-600 text-white px-3 py-2 text-sm font-medium hover:bg-brand-700 shadow-sm disabled:opacity-50 transition"
            >
              {isProcessing ? 'Queueing…' : 'Process eligible'}
            </button>
            {failedCount > 0 ? (
              <button
                onClick={retryFailed}
                disabled={isRetrying}
                className="inline-flex items-center gap-2 rounded-md bg-amber-600 text-white px-3 py-2 text-sm font-medium hover:bg-amber-700 shadow-sm disabled:opacity-50 transition"
              >
                {isRetrying ? 'Retrying…' : `Retry ${failedCount} failed`}
              </button>
            ) : null}
            <RefreshButton onClick={() => void load()} busy={isRefreshing} />
          </>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatTile
          kicker="30D"
          label="Pipeline next 30d"
          value={fmtMoney0(stats.pipelineValueNext30d)}
          sub={`${data?.waitingCount ?? 0} waiting · ${data?.queuedCount ?? 0} queued`}
          tone="emerald"
        />
        <StatTile
          kicker="STATUS"
          label="Failed"
          value={failedCount.toLocaleString()}
          sub={failedCount > 0 ? 'click "Retry" to re-queue' : 'all clear'}
          tone={failedCount > 0 ? 'red' : 'zinc'}
        />
        <StatTile
          kicker="UPLOAD"
          label="Last upload"
          value={stats.lastUploadAt ? relativeTime(stats.lastUploadAt) : '—'}
          sub={`${data?.totalCount ?? 0} total rows`}
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

      {/* Batches strip */}
      {stats.byBatch.length > 0 ? (
        <div className="mt-6">
          <div className="flex items-end justify-between mb-3">
            <div>
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">BATCHES</div>
              <div className="text-sm text-zinc-500">Click a batch to filter the table.</div>
            </div>
            {batchFilter ? (
              <button onClick={() => setBatchFilter('')} className="text-xs font-medium text-zinc-600 hover:text-zinc-900 px-2">Show all</button>
            ) : null}
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
            {stats.byBatch.map((b) => {
              const active = batchFilter === b.sourceFile
              return (
                <button
                  key={b.sourceFile}
                  onClick={() => setBatchFilter(active ? '' : b.sourceFile)}
                  className={`text-left rounded-xl border p-4 transition ${
                    active
                      ? 'border-brand-300 bg-brand-50 ring-1 ring-brand-200'
                      : 'border-zinc-200 bg-white hover:border-zinc-300'
                  }`}
                >
                  <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500 truncate">{b.sourceFile}</div>
                  <div className="mt-1 flex items-baseline gap-2">
                    <div className="text-lg font-semibold tabular-nums text-zinc-900">{b.total.toLocaleString()}</div>
                    <div className="text-xs text-zinc-500">rows</div>
                  </div>
                  <div className="mt-1 text-xxs font-mono tabular-nums">
                    <span className="text-brand-700">{b.completed} done</span>
                    {' · '}
                    <span className={b.failed > 0 ? 'text-red-700' : 'text-zinc-400'}>{b.failed} failed</span>
                    {' · '}
                    <span className="text-zinc-700">{fmtMoney0(b.pipelineValue)} pipeline</span>
                  </div>
                </button>
              )
            })}
          </div>
        </div>
      ) : null}

      <div className="mt-6 overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="min-w-full">
            <thead className="bg-zinc-50/80 backdrop-blur">
              <tr>
                <Th>Business · ABN</Th>
                <Th>Owner</Th>
                <Th>Due date</Th>
                <Th className="text-right">Amount</Th>
                <Th>Status</Th>
                <Th>Source · Uploaded</Th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-100">
              {filtered.length === 0 ? (
                <tr><td colSpan={6} className="px-4 py-12"><EmptyState title="No rows in this batch." /></td></tr>
              ) : filtered.map((u) => <UploadRow key={u.id} u={u} />)}
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
    pipelineValueNext30d: 0, lastUploadAt: null,
    today: 0, yesterday: 0, deltaPct: null, daily14d: [], byBatch: [],
  }
}

function UploadRow({ u }: { u: Upload }) {
  const days = u.renewalDueDate ? Math.floor((new Date(u.renewalDueDate).getTime() - Date.now()) / 86400000) : null
  const daysClass = days == null ? 'text-zinc-400' : days <= 0 ? 'text-red-700 font-medium' : days <= 30 ? 'text-brand-700 font-medium' : 'text-zinc-500'
  return (
    <tr className="hover:bg-zinc-50 transition-colors">
      <td className="px-4 py-3 align-top">
        <div className="text-sm font-medium text-zinc-900 truncate max-w-[18rem]">{u.businessName}</div>
        <div className="text-xxs font-mono tabular-nums text-zinc-500">{u.abn}</div>
      </td>
      <td className="px-4 py-3 align-top text-sm text-zinc-700 truncate max-w-[14rem]">{u.ownerName ?? '—'}</td>
      <td className="px-4 py-3 align-top">
        <div className="text-sm text-zinc-700 tabular-nums">{u.renewalDueDate ? new Date(u.renewalDueDate).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) : '—'}</div>
        <div className={`text-xxs font-mono tabular-nums ${daysClass}`}>{days == null ? '' : `${days} days`}</div>
      </td>
      <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-900">{fmtMoney2(u.amount)}</td>
      <td className="px-4 py-3 align-top">
        <BulkStatusPill status={u.status} />
        {u.errorMessage ? <div className="mt-1 text-xxs font-mono text-red-700 truncate max-w-[12rem]" title={u.errorMessage}>{u.errorMessage}</div> : null}
      </td>
      <td className="px-4 py-3 align-top">
        <div className="text-xxs font-mono text-zinc-500 truncate max-w-[10rem]">{u.sourceFile ?? '(direct)'}</div>
        <div className="text-xs text-zinc-700 tabular-nums">{relativeTime(u.uploadedAt)}</div>
      </td>
    </tr>
  )
}

function BulkStatusPill({ status }: { status: string }) {
  switch (status) {
    case 'WaitingForRenewalWindow': return <StatusPill tone="amber">WAITING</StatusPill>
    case 'RenewalQueued':           return <StatusPill tone="indigo">QUEUED</StatusPill>
    case 'RenewalCompleted':        return <StatusPill tone="emerald">COMPLETED</StatusPill>
    case 'RenewalFailed':           return <StatusPill tone="red">FAILED</StatusPill>
    case 'NotDueForRenewal':        return <StatusPill tone="zinc">NOT DUE</StatusPill>
    case 'Skipped':                 return <StatusPill tone="zinc">SKIPPED</StatusPill>
    default:                        return <StatusPill tone="zinc">{status.toUpperCase()}</StatusPill>
  }
}
