import { type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { type OverviewCharts } from '../api/client'
import { fmtMoney0 } from './_utils'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

/*
 * The Overview's charts. Each answers one question David asks, says the answer in a sentence
 * above it, and keeps its numbers one click away. Colours are the validated categorical slots
 * (blue, orange, aqua, in that order; grey for "the rest"), the same entity the same colour on
 * every chart; text never wears a series colour.
 */

const C = { blue: '#2a78d6', orange: '#eb6834', aqua: '#1baf7a', grey: '#a1a1aa' } as const

type Series<K extends string> = { key: K; label: string; color: string }

const DAY = new Intl.DateTimeFormat('en-AU', { day: 'numeric', month: 'short', timeZone: 'UTC' })
const shortDay = (iso: string) => DAY.format(new Date(`${iso}T00:00:00Z`))
const tick = { fontSize: 12, fill: 'var(--muted-foreground)' }

/** Sum of a field over the last n items (or n items before that, with `skip`). */
function sum<T>(rows: T[], f: (r: T) => number, n: number, skip = 0) {
  return rows.slice(Math.max(0, rows.length - n - skip), rows.length - skip).reduce((a, r) => a + f(r), 0)
}

function changeWords(now: number, before: number): string {
  if (before === 0) return now === 0 ? 'no change' : 'up from nothing'
  const pct = Math.round(((now - before) / before) * 100)
  return pct === 0 ? 'level' : pct > 0 ? `up ${pct}%` : `down ${-pct}%`
}

/* ─────────────────── The frame every chart shares ─────────────────── */

function ChartCard<K extends string, R extends Record<string, unknown>>({ title, takeaway, series, rows, rowLabel, money, children, link }: {
  title: string
  /** The answer, in a sentence. */
  takeaway: ReactNode
  series: Series<K>[]
  rows: R[]
  rowLabel: (r: R) => string
  money?: boolean
  children: ReactNode
  link?: { to: string; label: string }
}) {
  const fmt = (v: number) => (money ? fmtMoney0(v) : v.toLocaleString())
  return (
    <Card className="gap-3 py-4">
      <CardHeader className="gap-1 px-4 sm:px-5">
        <CardTitle className="text-sm font-medium text-muted-foreground">{title}</CardTitle>
        <p className="text-sm font-medium text-pretty">{takeaway}</p>
      </CardHeader>
      <CardContent className="space-y-3 px-4 sm:px-5">
        <ul className="flex flex-wrap gap-x-4 gap-y-1 text-caption text-muted-foreground" aria-label="Legend">
          {series.map((s) => (
            <li key={s.key} className="inline-flex items-center gap-1.5">
              <span aria-hidden className="size-2.5 rounded-[3px]" style={{ background: s.color }} />{s.label}
            </li>
          ))}
        </ul>
        <div className="h-56">{children}</div>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <details className="group text-caption">
            <summary className="cursor-pointer text-muted-foreground hover:text-foreground">See the numbers</summary>
            <div className="mt-2 max-h-64 overflow-auto rounded-md border">
              <table className="w-full text-caption tabular-nums">
                <thead className="sticky top-0 bg-card">
                  <tr className="border-b text-muted-foreground">
                    <th className="px-2 py-1.5 text-left font-medium" />
                    {series.map((s) => <th key={s.key} className="px-2 py-1.5 text-right font-medium">{s.label}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {[...rows].reverse().map((r, i) => (
                    <tr key={i} className="border-b last:border-0">
                      <td className="px-2 py-1 text-muted-foreground">{rowLabel(r)}</td>
                      {series.map((s) => <td key={s.key} className="px-2 py-1 text-right">{fmt(Number(r[s.key] ?? 0))}</td>)}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </details>
          {link ? <Link to={link.to} className="text-caption text-link hover:underline">{link.label}</Link> : null}
        </div>
      </CardContent>
    </Card>
  )
}

/** Hover: the row's label, then each series with its swatch and value (in text ink). */
function tip<K extends string>(series: Series<K>[], label: (p: Record<string, unknown>) => string, money?: boolean) {
  return function TooltipBody({ active, payload }: { active?: boolean; payload?: ReadonlyArray<{ payload?: unknown }> }) {
    if (!active || !payload?.length) return null
    const p = (payload[0].payload ?? {}) as Record<string, unknown>
    return (
      <div className="rounded-md border bg-card px-2.5 py-1.5 text-caption shadow-md">
        <div className="font-medium">{label(p)}</div>
        {series.map((s) => (
          <div key={s.key} className="flex items-center gap-1.5 text-muted-foreground tabular-nums">
            <span aria-hidden className="size-2 rounded-[2px]" style={{ background: s.color }} />
            {s.label}: <span className="text-foreground">{money ? fmtMoney0(Number(p[s.key] ?? 0)) : Number(p[s.key] ?? 0).toLocaleString()}</span>
          </div>
        ))}
      </div>
    )
  }
}

const moneyAxis = (v: number) => (v >= 1000 ? `$${Math.round(v / 1000)}k` : `$${v}`)

/* ─────────────────── 1. Money in, by channel ─────────────────── */

const CHANNELS: Series<'direct' | 'ontraport'>[] = [
  { key: 'direct', label: 'Direct (website)', color: C.blue },
  { key: 'ontraport', label: 'Ontraport', color: C.orange },
]

export function ChannelChart({ weeks }: { weeks: OverviewCharts['weeks'] }) {
  // Whole weeks only: this week isn't over.
  const done = weeks.slice(0, -1)
  const d4 = sum(done, (w) => w.direct, 4), d4b = sum(done, (w) => w.direct, 4, 4)
  const o4 = sum(done, (w) => w.ontraport, 4), o4b = sum(done, (w) => w.ontraport, 4, 4)
  const share = d4 + o4 > 0 ? Math.round((o4 / (d4 + o4)) * 100) : 0
  const label = (r: { week: string }) => `Week of ${shortDay(r.week)}`
  return (
    <ChartCard title="Money in, by channel" series={CHANNELS} rows={weeks} rowLabel={label} money
      takeaway={<>Last 4 weeks: direct {fmtMoney0(d4)} ({changeWords(d4, d4b)}), Ontraport {fmtMoney0(o4)} ({changeWords(o4, o4b)}). Ontraport brought {share}% of it.</>}>
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={weeks} margin={{ top: 4, right: 8, bottom: 0, left: -8 }}>
          <CartesianGrid vertical={false} stroke="var(--border)" />
          <XAxis dataKey="week" tickFormatter={shortDay} tick={tick} tickLine={false} axisLine={false} minTickGap={28} />
          <YAxis tickFormatter={moneyAxis} tick={tick} tickLine={false} axisLine={false} width={48} />
          <Tooltip cursor={{ stroke: 'var(--border)' }} wrapperStyle={{ outline: 'none' }} content={tip(CHANNELS, (p) => label(p as { week: string }), true)} />
          {CHANNELS.map((s) => <Line key={s.key} dataKey={s.key} type="monotone" stroke={s.color} strokeWidth={2} dot={false} activeDot={{ r: 4, strokeWidth: 2, stroke: 'var(--card)' }} isAnimationActive={false} />)}
        </LineChart>
      </ResponsiveContainer>
    </ChartCard>
  )
}

/* ─────────────────── 2. What became of each week's money ─────────────────── */

const OUTCOMES: Series<'renewed' | 'underWay' | 'needsPerson' | 'closed'>[] = [
  { key: 'renewed', label: 'Renewed', color: C.blue },
  { key: 'underWay', label: 'On its way', color: C.aqua },
  { key: 'needsPerson', label: 'Needs a person', color: C.orange },
  { key: 'closed', label: 'Closed (elsewhere, refunded…)', color: C.grey },
]

export function OutcomeChart({ weeks, waitingAmount, waitingCount }: { weeks: OverviewCharts['weeks']; waitingAmount: number; waitingCount: number }) {
  const inRange = weeks.reduce((a, w) => a + w.needsPerson, 0)
  const oldest = weeks.find((w) => w.needsPerson > 0)
  const label = (r: { week: string }) => `Paid week of ${shortDay(r.week)}`
  return (
    <ChartCard title="What became of each week’s money" series={OUTCOMES} rows={weeks} rowLabel={label} money
      link={waitingCount > 0 ? { to: '/admin/renewal-orders?view=to-fix', label: 'Open the ones that need a person →' } : undefined}
      takeaway={waitingCount === 0
        ? <>Every renewal paid for is renewed, on its way, or closed.</>
        : <>{fmtMoney0(waitingAmount)} paid by {waitingCount.toLocaleString()} clients is waiting on a person{oldest && inRange > 0 ? <>; the oldest in this view was paid the week of {shortDay(oldest.week)}</> : null}.</>}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={weeks} margin={{ top: 4, right: 8, bottom: 0, left: -8 }} barCategoryGap="20%">
          <CartesianGrid vertical={false} stroke="var(--border)" />
          <XAxis dataKey="week" tickFormatter={shortDay} tick={tick} tickLine={false} axisLine={false} minTickGap={28} />
          <YAxis tickFormatter={moneyAxis} tick={tick} tickLine={false} axisLine={false} width={48} />
          <Tooltip cursor={{ fill: 'var(--muted)' }} wrapperStyle={{ outline: 'none' }} content={tip(OUTCOMES, (p) => label(p as { week: string }), true)} />
          {OUTCOMES.map((s, i) => (
            // A 2px card-coloured gap between stacked segments; the top one rounded.
            <Bar key={s.key} dataKey={s.key} stackId="a" fill={s.color} stroke="var(--card)" strokeWidth={1}
              radius={i === OUTCOMES.length - 1 ? [3, 3, 0, 0] : 0} isAnimationActive={false} />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </ChartCard>
  )
}

/* ─────────────────── 3. ASIC, day by day ─────────────────── */

const ASIC: Series<'card' | 'bpay' | 'failed'>[] = [
  { key: 'card', label: 'Lodged, paid by card', color: C.blue },
  { key: 'bpay', label: 'Lodged, BPAY bill', color: C.aqua },
  { key: 'failed', label: 'Failed', color: C.orange },
]

export function AsicChart({ days }: { days: OverviewCharts['asic'] }) {
  const lodged = sum(days, (d) => d.card + d.bpay, 7)
  const bpay = sum(days, (d) => d.bpay, 7)
  const failed = sum(days, (d) => d.failed, 7)
  const failedBefore = sum(days, (d) => d.failed, 7, 7)
  const label = (r: { date: string }) => shortDay(r.date)
  return (
    <ChartCard title="ASIC, day by day" series={ASIC} rows={days} rowLabel={label}
      takeaway={<>Last 7 days: {lodged.toLocaleString()} lodged{lodged > 0 ? ` (${Math.round((bpay / lodged) * 100)}% by BPAY)` : ''}, {failed.toLocaleString()} failed ({changeWords(failed, failedBefore)} on the week before).</>}>
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={days} margin={{ top: 4, right: 8, bottom: 0, left: -16 }}>
          <CartesianGrid vertical={false} stroke="var(--border)" />
          <XAxis dataKey="date" tickFormatter={shortDay} tick={tick} tickLine={false} axisLine={false} minTickGap={28} />
          <YAxis allowDecimals={false} tick={tick} tickLine={false} axisLine={false} width={40} />
          <Tooltip cursor={{ stroke: 'var(--border)' }} wrapperStyle={{ outline: 'none' }} content={tip(ASIC, (p) => label(p as { date: string }))} />
          {ASIC.map((s) => <Line key={s.key} dataKey={s.key} type="monotone" stroke={s.color} strokeWidth={2} dot={false} activeDot={{ r: 4, strokeWidth: 2, stroke: 'var(--card)' }} isAnimationActive={false} />)}
        </LineChart>
      </ResponsiveContainer>
    </ChartCard>
  )
}

/* ─────────────────── 4. Coming due ─────────────────── */

const AHEAD: Series<'paid' | 'notPaid'>[] = [
  { key: 'paid', label: 'Already paid', color: C.blue },
  { key: 'notPaid', label: 'Not paid yet', color: C.orange },
]

export function AheadChart({ weeks, avgBasket }: { weeks: OverviewCharts['ahead']; avgBasket: number }) {
  const notPaid = weeks.reduce((a, w) => a + w.notPaid, 0)
  const next4 = weeks.slice(0, 4).reduce((a, w) => a + w.notPaid, 0)
  const label = (r: { week: string }) => `Due week of ${shortDay(r.week)}`
  return (
    <ChartCard title="Clients’ names coming due" series={AHEAD} rows={weeks} rowLabel={label}
      link={notPaid > 0 ? { to: '/admin/clients', label: 'Open clients →' } : undefined}
      takeaway={notPaid === 0
        ? <>Every name due in the next 12 weeks is already paid for.</>
        : <>{notPaid.toLocaleString()} names due in the next 12 weeks aren’t paid yet (about {fmtMoney0(notPaid * avgBasket)}); {next4.toLocaleString()} of them in the next 4 weeks.</>}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={weeks} margin={{ top: 4, right: 8, bottom: 0, left: -16 }} barCategoryGap="20%">
          <CartesianGrid vertical={false} stroke="var(--border)" />
          <XAxis dataKey="week" tickFormatter={shortDay} tick={tick} tickLine={false} axisLine={false} minTickGap={20} />
          <YAxis allowDecimals={false} tick={tick} tickLine={false} axisLine={false} width={40} />
          <Tooltip cursor={{ fill: 'var(--muted)' }} wrapperStyle={{ outline: 'none' }} content={tip(AHEAD, (p) => label(p as { week: string }))} />
          {AHEAD.map((s, i) => (
            <Bar key={s.key} dataKey={s.key} stackId="a" fill={s.color} stroke="var(--card)" strokeWidth={1}
              radius={i === AHEAD.length - 1 ? [3, 3, 0, 0] : 0} isAnimationActive={false} />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </ChartCard>
  )
}
