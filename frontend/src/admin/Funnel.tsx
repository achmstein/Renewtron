import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Bar, CartesianGrid, ComposedChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api, type FunnelResponse } from '../api/client'
import { Chip, EmptyState, KickerLabel, PageHeader, RefreshButton } from './_ui'

function isoDaysAgo(days: number) {
  const d = new Date()
  d.setDate(d.getDate() - days)
  return d.toISOString().slice(0, 10)
}

const RANGES = [
  { value: '7', label: '7D' },
  { value: '30', label: '30D' },
  { value: '90', label: '90D' },
]

/** Outcomes a "not eligible" check leaves on the lead, for the drill-down link. */
const NOT_ELIGIBLE_OUTCOMES = 'NotDueForRenewal,RenewalInProgress,NoBusinessNames'

/**
 * The wizard's pages, each made of the tracked steps that happen on it. `reached` is the
 * step that says someone got to the page, `done` the one that says they finished it.
 */
const STAGES: Array<{
  title: string
  reached: string
  done?: string
  doneLabel?: string
  steps: string[]
  exits?: Array<{ step: string; label: string; to?: string }>
}> = [
  { title: 'Landed', reached: 'abn_viewed', done: 'abn_submitted', doneLabel: 'entered an ABN', steps: ['abn_viewed', 'abn_submitted'] },
  { title: 'Contact details', reached: 'details_viewed', done: 'details_submitted', doneLabel: 'submitted their details', steps: ['details_viewed', 'details_submitted'] },
  {
    title: 'ASIC check', reached: 'check_started', done: 'check_available', doneLabel: 'could renew',
    steps: ['check_started', 'check_available', 'check_unavailable', 'check_failed'],
    exits: [
      { step: 'check_unavailable', label: 'not eligible', to: `/admin/leads?outcome=${NOT_ELIGIBLE_OUTCOMES}` },
      { step: 'check_failed', label: 'check errored' },
    ],
  },
  { title: 'Choose names', reached: 'select_viewed', done: 'select_submitted', doneLabel: 'picked names', steps: ['select_viewed', 'select_submitted'] },
  {
    title: 'Payment', reached: 'payment_viewed', done: 'payment_submitted', doneLabel: 'submitted payment',
    steps: ['payment_viewed', 'payment_submitted', 'payment_failed'],
    exits: [{ step: 'payment_failed', label: 'payment failed' }],
  },
  { title: 'Renewed', reached: 'renewal_complete', steps: ['renewal_complete'] },
]

function pct(part: number, whole: number) {
  return whole > 0 ? Math.round((part / whole) * 1000) / 10 : 0
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

  const stages = useMemo(() => buildStages(data), [data])
  const landed = stages[0]?.reached ?? 0
  const leak = stages.slice(0, -1).reduce<(typeof stages)[number] | undefined>(
    (worst, s) => (s.stoppedHere > (worst?.stoppedHere ?? 0) ? s : worst),
    undefined,
  )

  const prev = data?.previous
  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        kicker="OPS"
        title="Funnel"
        subtitle="How far people get through the renewal wizard, counted per person."
        right={
          <>
            <Segmented value={days} options={RANGES} onChange={setDays} />
            <select
              value={source}
              onChange={(e) => setSource(e.target.value)}
              aria-label="Source"
              className="rounded-md border-zinc-300 text-sm shadow-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 px-3 py-2"
            >
              <option value="">All sources</option>
              {allSources.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
            <RefreshButton onClick={() => void refetch()} busy={isFetching} />
          </>
        }
      />

      {source ? (
        <div className="-mt-4 mb-6 flex flex-wrap items-center gap-2">
          <Chip label={`Source: ${source}`} onRemove={() => setSource('')} />
        </div>
      ) : null}

      {error ? (
        <div className="mb-6 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 shadow-sm">{error instanceof Error ? error.message : 'Could not load the funnel.'}</div>
      ) : null}

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <KpiTile
          label="Visitors"
          value={(data?.totalVisitors ?? 0).toLocaleString()}
          delta={prev ? change(data?.totalVisitors ?? 0, prev.totalVisitors) : null}
          sub={`last ${days} days`}
        />
        <KpiTile
          label="Renewed"
          value={(data?.completedVisitors ?? 0).toLocaleString()}
          delta={prev ? change(data?.completedVisitors ?? 0, prev.completedVisitors) : null}
          sub="paid and confirmed"
        />
        <KpiTile
          label="Conversion"
          value={`${data?.conversionPct ?? 0}%`}
          delta={prev ? { text: `${signed(round1((data?.conversionPct ?? 0) - prev.conversionPct))} pts`, good: (data?.conversionPct ?? 0) >= prev.conversionPct } : null}
          sub="visitors who renewed"
        />
        <div className="rounded-xl border border-amber-200 bg-amber-50 p-4 shadow-sm">
          <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-amber-800">Biggest leak</div>
          {leak && leak.stoppedHere > 0 ? (
            <>
              <div className="mt-1 text-lg font-semibold text-amber-950">{leak.title}</div>
              <div className="text-sm text-amber-900">
                <span className="font-semibold tabular-nums">{leak.stoppedHere.toLocaleString()}</span> people stopped here
                {' '}({pct(leak.stoppedHere, landed)}% of visitors)
              </div>
            </>
          ) : (
            <div className="mt-1 text-sm text-amber-900">Not enough data yet.</div>
          )}
        </div>
      </div>

      {/* The journey */}
      <section className="mt-6 rounded-xl border border-zinc-200 bg-white shadow-sm">
        <div className="flex flex-wrap items-end justify-between gap-3 border-b border-zinc-200 px-5 py-4">
          <div>
            <KickerLabel>Journey</KickerLabel>
            <h2 className="mt-0.5 text-base font-semibold text-zinc-900">Where people stop</h2>
          </div>
          <Legend />
        </div>
        {landed === 0 ? (
          <div className="p-6"><EmptyState title="No visitors in this period." message="Steps are recorded as people move through the wizard." /></div>
        ) : (
          <ol className="divide-y divide-zinc-100">
            {stages.map((s, i) => (
              <StageRow
                key={s.title}
                index={i + 1}
                stage={s}
                landed={landed}
                isLeak={s === leak && s.stoppedHere > 0}
                isLast={i === stages.length - 1}
              />
            ))}
          </ol>
        )}
      </section>

      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-5">
        <section className="rounded-xl border border-zinc-200 bg-white p-5 shadow-sm lg:col-span-3">
          <div className="flex items-end justify-between gap-3">
            <div>
              <KickerLabel>Daily</KickerLabel>
              <h2 className="mt-0.5 text-base font-semibold text-zinc-900">Visitors and renewals</h2>
            </div>
            <div className="flex items-center gap-4 text-xs text-zinc-600">
              <span className="inline-flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-sm bg-[#A3C5E6]" />Visitors</span>
              <span className="inline-flex items-center gap-1.5"><span className="h-0.5 w-3 rounded bg-emerald-600" />Renewed</span>
            </div>
          </div>
          <div className="mt-4 h-56">
            <DailyChart data={data?.daily ?? []} />
          </div>
        </section>

        <section className="rounded-xl border border-zinc-200 bg-white shadow-sm lg:col-span-2">
          <div className="border-b border-zinc-200 px-5 py-4">
            <KickerLabel>Sources</KickerLabel>
            <h2 className="mt-0.5 text-base font-semibold text-zinc-900">Which traffic renews</h2>
          </div>
          <SourceList rows={data?.bySource ?? []} active={source} onPick={(s) => setSource(source === s ? '' : s)} />
        </section>
      </div>
    </div>
  )
}

type Stage = ReturnType<typeof buildStages>[number]

function buildStages(data: FunnelResponse | undefined) {
  const visitorsOf = (step?: string) => (step ? data?.steps.find((s) => s.step === step)?.visitors ?? 0 : 0)
  const exitVisitors = (step: string) => data?.exits.find((e) => e.step === step)?.visitors ?? 0
  const stoppedAt = data?.stoppedAt ?? []
  return STAGES.map((s) => ({
    title: s.title,
    reached: visitorsOf(s.reached),
    done: s.done ? visitorsOf(s.done) : null,
    doneLabel: s.doneLabel,
    // Exact per person: the stage holding the furthest step each visitor reached.
    stoppedHere: s.reached === 'renewal_complete'
      ? 0
      : stoppedAt.filter((x) => s.steps.includes(x.step)).reduce((sum, x) => sum + x.visitors, 0),
    exits: (s.exits ?? []).map((e) => ({ ...e, visitors: exitVisitors(e.step) })).filter((e) => e.visitors > 0),
  }))
}

function StageRow({ index, stage, landed, isLeak, isLast }: { index: number; stage: Stage; landed: number; isLeak: boolean; isLast: boolean }) {
  const reachedPct = pct(stage.reached, landed)
  const donePct = stage.done != null ? pct(stage.done, landed) : reachedPct
  return (
    <li className={`relative px-5 py-4 ${isLeak ? 'bg-amber-50/60' : ''}`}>
      {isLeak ? <span className="absolute inset-y-0 left-0 w-1 bg-amber-400" aria-hidden /> : null}
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:gap-6">
        <div className="flex min-w-0 items-center gap-3 md:w-56 md:shrink-0">
          <span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${isLast ? 'bg-emerald-100 text-emerald-800' : 'bg-zinc-100 text-zinc-700'}`}>
            {index}
          </span>
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <span className="text-sm font-semibold text-zinc-900">{stage.title}</span>
              {isLeak ? <span className="rounded bg-amber-200 px-1.5 py-0.5 text-xxs font-semibold uppercase tracking-wide text-amber-900">Biggest leak</span> : null}
            </div>
            <div className="text-xs text-zinc-500 tabular-nums">{stage.reached.toLocaleString()} people · {reachedPct}%</div>
          </div>
        </div>

        <div className="min-w-0 flex-1">
          <div
            className="relative h-7 w-full overflow-hidden rounded-md bg-zinc-100"
            title={`${stage.reached.toLocaleString()} reached${stage.done != null ? `, ${stage.done.toLocaleString()} ${stage.doneLabel}` : ''}`}
          >
            <div className="absolute inset-y-0 left-0 rounded-md bg-[#A3C5E6]" style={{ width: `${reachedPct}%` }} />
            <div className={`absolute inset-y-0 left-0 rounded-md ${isLast ? 'bg-emerald-600' : 'bg-brand-600'}`} style={{ width: `${donePct}%` }} />
          </div>
          <div className="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-zinc-600">
            {stage.done != null ? (
              <span><span className="font-semibold tabular-nums text-zinc-900">{stage.done.toLocaleString()}</span> {stage.doneLabel} ({pct(stage.done, stage.reached)}%)</span>
            ) : (
              <span className="text-emerald-700">Paid and confirmed</span>
            )}
            {stage.exits.map((e) =>
              e.to ? (
                <Link key={e.step} to={e.to} className="inline-flex items-center gap-1 rounded-full bg-zinc-100 px-2 py-0.5 text-zinc-700 ring-1 ring-zinc-200 hover:bg-zinc-200">
                  <span className="font-semibold tabular-nums">{e.visitors.toLocaleString()}</span> {e.label} →
                </Link>
              ) : (
                <span key={e.step} className="inline-flex items-center gap-1 rounded-full bg-red-50 px-2 py-0.5 text-red-700 ring-1 ring-red-100">
                  <span className="font-semibold tabular-nums">{e.visitors.toLocaleString()}</span> {e.label}
                </span>
              ),
            )}
          </div>
        </div>

        <div className="md:w-36 md:shrink-0 md:text-right">
          {isLast ? null : stage.stoppedHere > 0 ? (
            <>
              <div className={`text-sm font-semibold tabular-nums ${isLeak ? 'text-amber-800' : 'text-red-700'}`}>−{stage.stoppedHere.toLocaleString()}</div>
              <div className="text-xs text-zinc-500">stopped here</div>
            </>
          ) : (
            <div className="text-xs text-zinc-400">nobody stopped</div>
          )}
        </div>
      </div>
    </li>
  )
}

function Legend() {
  return (
    <div className="flex items-center gap-4 text-xs text-zinc-600">
      <span className="inline-flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-sm bg-[#A3C5E6]" />Reached the step</span>
      <span className="inline-flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-sm bg-brand-600" />Completed it</span>
    </div>
  )
}

function KpiTile({ label, value, sub, delta }: { label: string; value: string; sub: string; delta: { text: string; good: boolean } | null }) {
  return (
    <div className="rounded-xl border border-zinc-200 bg-white p-4 shadow-sm">
      <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">{label}</div>
      <div className="mt-1 flex items-baseline gap-2">
        <span className="text-2xl font-semibold tabular-nums text-zinc-900">{value}</span>
        {delta ? <span className={`text-xs font-medium tabular-nums ${delta.good ? 'text-emerald-700' : 'text-red-700'}`}>{delta.text}</span> : null}
      </div>
      <div className="text-xs text-zinc-500">{sub}{delta ? ' · vs previous period' : ''}</div>
    </div>
  )
}

function round1(n: number) {
  return Math.round(n * 10) / 10
}

function signed(n: number) {
  return `${n > 0 ? '+' : n < 0 ? '−' : '±'}${Math.abs(n)}`
}

function change(current: number, previous: number): { text: string; good: boolean } | null {
  if (previous === 0) return current > 0 ? { text: 'new', good: true } : null
  const p = round1(((current - previous) / previous) * 100)
  return { text: `${signed(p)}%`, good: p >= 0 }
}

function Segmented({ value, options, onChange }: { value: string; options: Array<{ value: string; label: string }>; onChange: (v: string) => void }) {
  return (
    <div className="inline-flex rounded-md shadow-sm" role="group" aria-label="Range">
      {options.map((o, i) => {
        const active = o.value === value
        const radius = i === 0 ? 'rounded-l-md' : i === options.length - 1 ? 'rounded-r-md -ml-px' : '-ml-px'
        return (
          <button
            key={o.value}
            type="button"
            aria-pressed={active}
            onClick={() => onChange(o.value)}
            className={`${radius} px-3 py-2 text-sm font-medium ring-1 ring-inset transition ${active ? 'z-10 bg-zinc-900 text-white ring-zinc-900' : 'bg-white text-zinc-700 ring-zinc-300 hover:bg-zinc-50'}`}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

function DailyChart({ data }: { data: FunnelResponse['daily'] }) {
  if (data.length === 0) return <div className="flex h-full items-center justify-center text-sm text-zinc-400">No data</div>
  const short = (d: string) => new Date(`${d}T00:00:00`).toLocaleDateString('en-AU', { day: 'numeric', month: 'short' })
  return (
    <ResponsiveContainer width="100%" height="100%">
      <ComposedChart data={data} margin={{ top: 4, right: 4, bottom: 0, left: -16 }}>
        <CartesianGrid vertical={false} stroke="#F4F4F5" />
        <XAxis dataKey="date" tickFormatter={short} tick={{ fontSize: 11, fill: '#71717A' }} tickLine={false} axisLine={false} minTickGap={24} />
        <YAxis yAxisId="v" allowDecimals={false} tick={{ fontSize: 11, fill: '#71717A' }} tickLine={false} axisLine={false} />
        <YAxis yAxisId="r" orientation="right" allowDecimals={false} hide />
        <Tooltip
          cursor={{ fill: '#F4F4F5' }}
          wrapperStyle={{ outline: 'none' }}
          content={({ active, payload }) => {
            if (!active || !payload?.length) return null
            const p = payload[0].payload as FunnelResponse['daily'][number]
            return (
              <div className="rounded-md border border-zinc-200 bg-white px-2.5 py-1.5 text-xs shadow-md">
                <div className="font-medium text-zinc-900">{short(p.date)}</div>
                <div className="tabular-nums text-zinc-600">{p.visitors.toLocaleString()} visitors</div>
                <div className="tabular-nums text-emerald-700">{p.renewed.toLocaleString()} renewed</div>
              </div>
            )
          }}
        />
        <Bar yAxisId="v" dataKey="visitors" fill="#A3C5E6" radius={[3, 3, 0, 0]} isAnimationActive={false} />
        <Line yAxisId="r" dataKey="renewed" type="monotone" stroke="#059669" strokeWidth={2} dot={false} isAnimationActive={false} />
      </ComposedChart>
    </ResponsiveContainer>
  )
}

function SourceList({ rows, active, onPick }: { rows: FunnelResponse['bySource']; active: string; onPick: (source: string) => void }) {
  if (rows.length === 0) return <div className="p-5"><EmptyState title="No traffic recorded yet." /></div>
  const best = Math.max(...rows.map((r) => r.conversionPct), 1)
  return (
    <ul className="divide-y divide-zinc-100">
      {rows.map((r) => (
        <li key={r.source}>
          <button
            type="button"
            onClick={() => onPick(r.source)}
            className={`w-full px-5 py-3 text-left transition hover:bg-zinc-50 ${active === r.source ? 'bg-brand-50' : ''}`}
            title={active === r.source ? 'Clear filter' : 'Show only this source'}
          >
            <div className="flex items-baseline justify-between gap-3">
              <span className={`truncate text-sm font-medium ${active === r.source ? 'text-brand-800' : 'text-zinc-900'}`}>{r.source}</span>
              <span className="shrink-0 text-sm font-semibold tabular-nums text-zinc-900">{r.conversionPct}%</span>
            </div>
            <div className="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-zinc-100">
              <div className="h-full rounded-full bg-emerald-500" style={{ width: `${(r.conversionPct / best) * 100}%` }} />
            </div>
            <div className="mt-1 text-xs text-zinc-500 tabular-nums">
              {r.completed.toLocaleString()} renewed of {r.visitors.toLocaleString()} visitors
            </div>
          </button>
        </li>
      ))}
    </ul>
  )
}
