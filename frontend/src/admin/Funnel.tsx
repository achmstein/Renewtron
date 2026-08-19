import { useEffect, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, type FunnelResponse } from '../api/client'
import { Chip, EmptyState, PageHeader, RefreshButton, SparklineTile, StatTile, StatusPill, Th } from './_ui'

function isoDaysAgo(days: number) {
  const d = new Date()
  d.setDate(d.getDate() - days)
  return d.toISOString().slice(0, 10)
}

export default function Funnel() {
  const [days, setDays] = useState('30')
  const [source, setSource] = useState('')
  // Held separately: the response is filtered once a source is picked, which would
  // otherwise strip every other option out of the dropdown.
  const [allSources, setAllSources] = useState<string[]>([])

  const { data, error, isFetching, isPlaceholderData, refetch } = useQuery({
    queryKey: ['admin-funnel', { days, source }],
    queryFn: () => api.admin.funnel({ dateFrom: isoDaysAgo(Number(days)), source: source || undefined }),
    placeholderData: keepPreviousData,
  })

  useEffect(() => {
    if (!source && data && !isPlaceholderData) setAllSources(data.bySource.map((s) => s.source))
  }, [data, source, isPlaceholderData])

  const steps = data?.steps ?? []
  const stoppedAt = data?.stoppedAt ?? []
  const bySource = data?.bySource ?? []
  const exits = data?.exits ?? []

  const enteredCount = steps[0]?.visitors ?? 0
  const stoppedTotal = stoppedAt.reduce((sum, s) => sum + s.visitors, 0)
  const biggestDrop = steps.slice(1).reduce(
    (worst, s) => (s.droppedFromPrevious > (worst?.droppedFromPrevious ?? 0) ? s : worst),
    undefined as FunnelResponse['steps'][number] | undefined,
  )

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Funnel"
        subtitle="Every step of the public renewal flow, counted per person rather than per page view."
        right={<RefreshButton onClick={() => void refetch()} busy={isFetching} />}
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatTile
          kicker={`${days}D`}
          label="Visitors"
          value={(data?.totalVisitors ?? 0).toLocaleString()}
          sub="reached the wizard"
        />
        <StatTile
          kicker={`${days}D`}
          label="Renewed"
          value={(data?.completedVisitors ?? 0).toLocaleString()}
          sub="paid and confirmed"
          tone="emerald"
        />
        <StatTile
          kicker={`${days}D`}
          label="Conversion"
          value={`${data?.conversionPct ?? 0}%`}
          sub="landed → renewed"
          tone={(data?.conversionPct ?? 0) >= 5 ? 'emerald' : 'amber'}
        />
        <SparklineTile
          kicker="14D"
          label="Daily visitors"
          value={(data?.daily14d[data.daily14d.length - 1]?.count ?? 0).toLocaleString()}
          sub="today"
          data={(data?.daily14d ?? []).map((d) => d.count)}
        />
      </div>

      {/* Filters */}
      <div className="mt-6 flex flex-wrap items-end gap-3">
        <FilterSelect label="Range" value={days} onChange={setDays} options={[
          { value: '7', label: 'Last 7 days' },
          { value: '30', label: 'Last 30 days' },
          { value: '90', label: 'Last 90 days' },
        ]} />
        <FilterSelect label="Source" value={source} onChange={setSource} options={[
          { value: '', label: 'All sources' },
          ...allSources.map((s) => ({ value: s, label: s })),
        ]} />
      </div>

      {source ? (
        <div className="mt-4 flex flex-wrap items-center gap-2">
          <Chip label={`Source: ${source}`} onRemove={() => setSource('')} />
        </div>
      ) : null}

      {error ? (
        <div className="mt-6 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 shadow-sm">{error instanceof Error ? error.message : 'Could not load the funnel.'}</div>
      ) : null}

      {biggestDrop && biggestDrop.droppedFromPrevious > 0 ? (
        <div className="mt-6 rounded-xl border border-amber-200 bg-amber-50 p-4 shadow-sm">
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-amber-800">BIGGEST DROP-OFF · {days}D</div>
          <div className="mt-1 text-sm text-amber-900">
            <span className="font-mono tabular-nums font-semibold">{biggestDrop.droppedFromPrevious.toLocaleString()}</span>{' '}
            people didn't get past <span className="font-semibold">{biggestDrop.label.toLowerCase()}</span>.
          </div>
        </div>
      ) : null}

      {/* Step by step */}
      <div className="mt-6 overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
        <div className="overflow-x-auto">
          <table className="min-w-full">
            <thead className="bg-zinc-50/80 backdrop-blur">
              <tr>
                <Th>Step</Th>
                <Th className="w-40">Reach</Th>
                <Th className="text-right">Visitors</Th>
                <Th className="text-right">Of start</Th>
                <Th className="text-right">Step rate</Th>
                <Th className="text-right">Drop-off</Th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-100">
              {steps.length === 0 ? (
                <tr><td colSpan={6} className="px-4 py-12"><EmptyState title="No funnel data for this period." message="Steps are recorded as visitors move through the wizard." /></td></tr>
              ) : steps.map((s) => (
                <tr key={s.step} className="hover:bg-zinc-50 transition-colors">
                  <td className="px-4 py-3 align-top">
                    <div className="text-sm text-zinc-900">{s.label}</div>
                    <div className="text-xxs font-mono text-zinc-400">{s.step}</div>
                  </td>
                  <td className="px-4 py-3 align-middle">
                    <div className="h-1.5 w-full overflow-hidden rounded-full bg-zinc-100">
                      <div
                        className="h-full rounded-full bg-brand-600"
                        style={{ width: `${enteredCount > 0 ? Math.round((s.visitors / enteredCount) * 100) : 0}%` }}
                      />
                    </div>
                  </td>
                  <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-900">{s.visitors.toLocaleString()}</td>
                  <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-500">{s.overallConversionPct}%</td>
                  <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-500">{s.stepConversionPct}%</td>
                  <td className="px-4 py-3 align-top text-right">
                    {s.droppedFromPrevious > 0
                      ? <span className="text-sm font-mono tabular-nums text-red-700">−{s.droppedFromPrevious.toLocaleString()}</span>
                      : <span className="text-xxs font-mono text-zinc-400">—</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="mt-6 grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* Where each person stopped */}
        <div className="overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
          <div className="border-b border-zinc-200 px-4 py-3">
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">WHERE THEY STOPPED</div>
            <div className="text-sm text-zinc-500">The furthest step each visitor reached.</div>
          </div>
          <div className="overflow-x-auto">
            <table className="min-w-full">
              <thead className="bg-zinc-50/80 backdrop-blur">
                <tr>
                  <Th>Last step reached</Th>
                  <Th className="text-right">Visitors</Th>
                  <Th className="text-right">Share</Th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {stoppedAt.length === 0 ? (
                  <tr><td colSpan={3} className="px-4 py-12"><EmptyState title="Nothing recorded yet." /></td></tr>
                ) : stoppedAt.map((s) => (
                  <tr key={s.step} className="hover:bg-zinc-50 transition-colors">
                    <td className="px-4 py-3 align-top">
                      <div className="flex items-center gap-2">
                        {s.step === 'renewal_complete'
                          ? <StatusPill tone="emerald">DONE</StatusPill>
                          : <StatusPill tone="zinc">EXIT</StatusPill>}
                        <span className="text-sm text-zinc-900">{s.label}</span>
                      </div>
                    </td>
                    <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-900">{s.visitors.toLocaleString()}</td>
                    <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-500">
                      {stoppedTotal > 0 ? Math.round((s.visitors / stoppedTotal) * 100) : 0}%
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {exits.some((e) => e.visitors > 0) ? (
            <div className="border-t border-zinc-100 px-4 py-3">
              <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">DEAD ENDS HIT</div>
              <dl className="mt-2 grid grid-cols-1 gap-x-4 gap-y-1.5 sm:grid-cols-3">
                {exits.map((e) => (
                  <div key={e.step}>
                    <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">{e.label}</dt>
                    <dd className="text-sm font-mono tabular-nums text-zinc-900">{e.visitors.toLocaleString()}</dd>
                  </div>
                ))}
              </dl>
            </div>
          ) : null}
        </div>

        {/* By source */}
        <div className="overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
          <div className="border-b border-zinc-200 px-4 py-3">
            <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">BY SOURCE</div>
            <div className="text-sm text-zinc-500">Click a source to filter the page.</div>
          </div>
          <div className="overflow-x-auto">
            <table className="min-w-full">
              <thead className="bg-zinc-50/80 backdrop-blur">
                <tr>
                  <Th>Source</Th>
                  <Th className="text-right">Visitors</Th>
                  <Th className="text-right">Renewed</Th>
                  <Th className="text-right">Rate</Th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {bySource.length === 0 ? (
                  <tr><td colSpan={4} className="px-4 py-12"><EmptyState title="No traffic recorded yet." /></td></tr>
                ) : bySource.map((s) => (
                  <tr key={s.source} className="hover:bg-zinc-50 transition-colors">
                    <td className="px-4 py-3 align-top">
                      <button
                        type="button"
                        onClick={() => setSource(source === s.source ? '' : s.source)}
                        className={`text-sm font-mono hover:underline ${source === s.source ? 'text-brand-700' : 'text-zinc-900'}`}
                      >
                        {s.source}
                      </button>
                    </td>
                    <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-900">{s.visitors.toLocaleString()}</td>
                    <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-700">{s.completed.toLocaleString()}</td>
                    <td className="px-4 py-3 align-top text-right text-sm font-mono tabular-nums text-zinc-500">{s.conversionPct}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  )
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
