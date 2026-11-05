import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { TriangleAlert } from 'lucide-react'
import { Bar, CartesianGrid, ComposedChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api, type FunnelResponse } from '../api/client'
import { Chip, EmptyState, PageHeader, RefreshButton } from './_ui'
import { TrendChart } from '@/components/trend-chart'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { cn } from '@/lib/utils'

function isoDaysAgo(days: number) {
  const d = new Date()
  d.setDate(d.getDate() - days)
  return d.toISOString().slice(0, 10)
}

const RANGES = [
  { value: '7', label: '7 days' },
  { value: '30', label: '30 days' },
  { value: '90', label: '90 days' },
]

/** Radix Select can't hold an empty value; this stands for "all sources". */
const ALL_SOURCES = '__all'

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

const shortDate = (d: string) => new Date(`${d}T00:00:00`).toLocaleDateString('en-AU', { day: 'numeric', month: 'short' })

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
  const daily = data?.daily ?? []
  const period = `Last ${days} days`
  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <PageHeader
        title="Funnel"
        subtitle="How far people get through the renewal wizard."
        right={
          <>
            <ToggleGroup type="single" variant="outline" size="sm" value={days} onValueChange={(v) => v && setDays(v)} aria-label="Range">
              {RANGES.map((r) => <ToggleGroupItem key={r.value} value={r.value} className="px-3">{r.label}</ToggleGroupItem>)}
            </ToggleGroup>
            <Select value={source || ALL_SOURCES} onValueChange={(v) => setSource(v === ALL_SOURCES ? '' : v)}>
              <SelectTrigger size="sm" aria-label="Source" className="min-w-36">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_SOURCES}>All sources</SelectItem>
                {allSources.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}
              </SelectContent>
            </Select>
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
        <Alert variant="destructive" className="mb-6">
          <TriangleAlert />
          <AlertTitle>Couldn’t load the funnel</AlertTitle>
          <AlertDescription>{error instanceof Error ? error.message : 'Load failed.'}</AlertDescription>
        </Alert>
      ) : null}

      <div className="space-y-6">
        <Card className="gap-0 overflow-hidden py-0">
          <div className="grid grid-cols-2 gap-px bg-border lg:grid-cols-4">
            <Stat
              label="Visitors"
              value={(data?.totalVisitors ?? 0).toLocaleString()}
              sub={withDelta(period, prev ? change(data?.totalVisitors ?? 0, prev.totalVisitors) : null)}
              trend={<TrendChart data={daily.map((d) => ({ date: shortDate(d.date), count: d.visitors }))} valueLabel="visitors" />}
            />
            <Stat
              label="Renewed"
              value={(data?.completedVisitors ?? 0).toLocaleString()}
              sub={withDelta('Paid and confirmed', prev ? change(data?.completedVisitors ?? 0, prev.completedVisitors) : null)}
              trend={<TrendChart data={daily.map((d) => ({ date: shortDate(d.date), count: d.renewed }))} valueLabel="renewed" />}
            />
            <Stat
              label="Conversion"
              value={`${data?.conversionPct ?? 0}%`}
              sub={withDelta('Visitors who renewed', prev ? `${signed(round1((data?.conversionPct ?? 0) - prev.conversionPct))} pts` : null)}
            />
            <Stat
              label="Biggest leak"
              dot={!!leak && leak.stoppedHere > 0}
              value={leak && leak.stoppedHere > 0 ? leak.title : '—'}
              sub={leak && leak.stoppedHere > 0
                ? `${leak.stoppedHere.toLocaleString()} stopped here (${pct(leak.stoppedHere, landed)}% of visitors)`
                : 'Not enough data yet'}
            />
          </div>
        </Card>

        {/* The journey */}
        <Card className="gap-0 py-0">
          <CardHeader className="border-b py-4 [.border-b]:pb-4">
            <CardTitle>Where people stop</CardTitle>
            <CardAction><Legend items={[{ cls: 'bg-muted-foreground/20', label: 'Reached the step' }, { cls: 'bg-link', label: 'Completed it' }]} /></CardAction>
          </CardHeader>
          {landed === 0 ? (
            <div className="p-6"><EmptyState title="No visitors in this period" message="Steps are recorded as people move through the wizard." /></div>
          ) : (
            <ol className="divide-y">
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
        </Card>

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-5">
          <Card className="lg:col-span-3">
            <CardHeader>
              <CardTitle>Visitors and renewals</CardTitle>
              <CardAction><Legend items={[{ cls: 'bg-muted-foreground/25', label: 'Visitors' }, { cls: 'h-0.5 w-3 bg-link', label: 'Renewed' }]} /></CardAction>
            </CardHeader>
            <CardContent className="h-56">
              <DailyChart data={daily} />
            </CardContent>
          </Card>

          <Card className="gap-0 py-0 lg:col-span-2">
            <CardHeader className="border-b py-4 [.border-b]:pb-4">
              <CardTitle>Which traffic renews</CardTitle>
            </CardHeader>
            <SourceList rows={data?.bySource ?? []} active={source} onPick={(s) => setSource(source === s ? '' : s)} />
          </Card>
        </div>
      </div>
    </div>
  )
}

/** One quiet stat cell: muted label, black number, one muted line, and a sparkline when it changes over time. */
function Stat({ label, value, sub, dot, trend }: { label: string; value: string; sub: string; dot?: boolean; trend?: ReactNode }) {
  return (
    <div className="flex flex-col bg-card px-5 py-4">
      <span className="flex items-center gap-2 text-sm text-muted-foreground">
        {dot ? <span aria-hidden className="size-2 shrink-0 rounded-full bg-warning" /> : null}
        {label}
      </span>
      <span className="mt-1 truncate text-2xl font-semibold tracking-tight tabular-nums">{value}</span>
      <span className="truncate text-caption text-muted-foreground tabular-nums">{sub}</span>
      {trend ? <div className="mt-2 -mb-1 h-8">{trend}</div> : null}
    </div>
  )
}

function withDelta(text: string, delta: string | null) {
  return !delta ? text : delta === 'new' ? `${text} · new` : `${text} · ${delta} vs before`
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
    <li className="px-6 py-4">
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:gap-6">
        <div className="flex min-w-0 items-center gap-3 md:w-56 md:shrink-0">
          <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-muted text-caption font-medium">{index}</span>
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">{stage.title}</span>
              {isLeak ? <Badge variant="warning">Biggest leak</Badge> : null}
            </div>
            <div className="text-caption text-muted-foreground tabular-nums">{stage.reached.toLocaleString()} people · {reachedPct}%</div>
          </div>
        </div>

        <div className="min-w-0 flex-1">
          <div
            className="relative h-6 w-full overflow-hidden rounded-md bg-muted"
            title={`${stage.reached.toLocaleString()} reached${stage.done != null ? `, ${stage.done.toLocaleString()} ${stage.doneLabel}` : ''}`}
          >
            <div className="absolute inset-y-0 left-0 rounded-md bg-muted-foreground/20" style={{ width: `${reachedPct}%` }} />
            <div className="absolute inset-y-0 left-0 rounded-md bg-link" style={{ width: `${donePct}%` }} />
          </div>
          <div className="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-caption text-muted-foreground">
            {stage.done != null ? (
              <span><span className="font-medium text-foreground tabular-nums">{stage.done.toLocaleString()}</span> {stage.doneLabel} ({pct(stage.done, stage.reached)}%)</span>
            ) : (
              <span>Paid and confirmed</span>
            )}
            {stage.exits.map((e) =>
              e.to ? (
                <Badge key={e.step} variant="outline" asChild>
                  <Link to={e.to}><span className="tabular-nums">{e.visitors.toLocaleString()}</span> {e.label} →</Link>
                </Badge>
              ) : (
                <Badge key={e.step} variant="outline">
                  <span aria-hidden className="size-1.5 rounded-full bg-destructive" />
                  <span className="tabular-nums">{e.visitors.toLocaleString()}</span> {e.label}
                </Badge>
              ),
            )}
          </div>
        </div>

        <div className="md:w-36 md:shrink-0 md:text-right">
          {isLast ? null : stage.stoppedHere > 0 ? (
            <>
              <div className="text-sm font-medium tabular-nums">−{stage.stoppedHere.toLocaleString()}</div>
              <div className="text-caption text-muted-foreground">stopped here</div>
            </>
          ) : (
            <div className="text-caption text-muted-foreground">Nobody stopped</div>
          )}
        </div>
      </div>
    </li>
  )
}

function Legend({ items }: { items: Array<{ cls: string; label: string }> }) {
  return (
    <div className="flex items-center gap-4 text-caption text-muted-foreground">
      {items.map((i) => (
        <span key={i.label} className="inline-flex items-center gap-1.5">
          <span aria-hidden className={cn('size-2.5 rounded-sm', i.cls)} />
          {i.label}
        </span>
      ))}
    </div>
  )
}

function round1(n: number) {
  return Math.round(n * 10) / 10
}

function signed(n: number) {
  return `${n > 0 ? '+' : n < 0 ? '−' : '±'}${Math.abs(n)}`
}

function change(current: number, previous: number): string | null {
  if (previous === 0) return current > 0 ? 'new' : null
  return `${signed(round1(((current - previous) / previous) * 100))}%`
}

function DailyChart({ data }: { data: FunnelResponse['daily'] }) {
  if (data.length === 0) return <div className="flex h-full items-center justify-center text-sm text-muted-foreground">No data</div>
  const tick = { fontSize: 12, fill: 'var(--muted-foreground)' }
  return (
    <ResponsiveContainer width="100%" height="100%">
      <ComposedChart data={data} margin={{ top: 4, right: 4, bottom: 0, left: -16 }}>
        <CartesianGrid vertical={false} stroke="var(--border)" />
        <XAxis dataKey="date" tickFormatter={shortDate} tick={tick} tickLine={false} axisLine={false} minTickGap={24} />
        <YAxis yAxisId="v" allowDecimals={false} tick={tick} tickLine={false} axisLine={false} />
        <YAxis yAxisId="r" orientation="right" allowDecimals={false} hide />
        <Tooltip
          cursor={{ fill: 'var(--muted)' }}
          wrapperStyle={{ outline: 'none' }}
          content={({ active, payload }) => {
            if (!active || !payload?.length) return null
            const p = payload[0].payload as FunnelResponse['daily'][number]
            return (
              <div className="rounded-md border bg-card px-2.5 py-1.5 text-caption shadow-md">
                <div className="font-medium">{shortDate(p.date)}</div>
                <div className="text-muted-foreground tabular-nums">{p.visitors.toLocaleString()} visitors</div>
                <div className="text-muted-foreground tabular-nums">{p.renewed.toLocaleString()} renewed</div>
              </div>
            )
          }}
        />
        <Bar yAxisId="v" dataKey="visitors" fill="var(--muted-foreground)" fillOpacity={0.25} radius={[3, 3, 0, 0]} isAnimationActive={false} />
        <Line yAxisId="r" dataKey="renewed" type="monotone" stroke="var(--link)" strokeWidth={2} dot={false} isAnimationActive={false} />
      </ComposedChart>
    </ResponsiveContainer>
  )
}

function SourceList({ rows, active, onPick }: { rows: FunnelResponse['bySource']; active: string; onPick: (source: string) => void }) {
  if (rows.length === 0) return <div className="p-6"><EmptyState title="No traffic recorded yet" /></div>
  const best = Math.max(...rows.map((r) => r.conversionPct), 1)
  return (
    <ul className="divide-y">
      {rows.map((r) => {
        const on = active === r.source
        return (
          <li key={r.source}>
            <button
              type="button"
              onClick={() => onPick(r.source)}
              aria-pressed={on}
              className={cn('w-full px-6 py-3 text-left transition-colors hover:bg-muted/50', on && 'bg-muted')}
              title={on ? 'Clear filter' : 'Show only this source'}
            >
              <div className="flex items-baseline justify-between gap-3">
                <span className={cn('truncate text-sm font-medium', on && 'text-link')}>{r.source}</span>
                <span className="shrink-0 text-sm font-medium tabular-nums">{r.conversionPct}%</span>
              </div>
              <div className="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-muted">
                <div className="h-full rounded-full bg-link" style={{ width: `${(r.conversionPct / best) * 100}%` }} />
              </div>
              <div className="mt-1 text-caption text-muted-foreground tabular-nums">
                {r.completed.toLocaleString()} renewed of {r.visitors.toLocaleString()} visitors
              </div>
            </button>
          </li>
        )
      })}
    </ul>
  )
}
