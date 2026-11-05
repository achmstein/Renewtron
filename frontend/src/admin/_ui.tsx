import { Children, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { AlignLeft, LayoutGrid, Loader2, RefreshCw, Rows3, X } from 'lucide-react'
import { TrendChart } from '@/components/trend-chart'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { cn } from '@/lib/utils'

/*
 * The admin's shared building blocks, on shadcn/ui and the admin theme (see STYLE.md).
 * Friendly and focused: sentence case, one short line of supporting text at most, details on
 * the record page or on hover.
 */

/* ─────────────────── Page chrome ─────────────────── */

/** Page title, one optional line under it, and the page's actions on the right. */
export function PageHeader({ title, subtitle, right }: { kicker?: string; title: string; subtitle?: string; right?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        <h1 className="text-title">{title}</h1>
        {subtitle ? <p className="mt-1 text-sm text-muted-foreground">{subtitle}</p> : null}
      </div>
      {right ? <div className="flex flex-wrap items-center gap-2 sm:shrink-0">{right}</div> : null}
    </div>
  )
}

/** A section's title, with a short fact (a count, a total) on the right. */
export function SectionTitle({ title, right }: { kicker?: string; title: string; right?: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <h2 className="text-heading">{title}</h2>
      {right ? <div className="text-sm text-muted-foreground tabular-nums">{right}</div> : null}
    </div>
  )
}

/** A small muted label above a value or a group. */
export function KickerLabel({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('text-xs font-medium text-muted-foreground', className)}>{children}</div>
}

/* ─────────────────── Table chrome ─────────────────── */

export function Th({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <th className={cn('h-10 border-b px-4 text-left align-middle text-xs font-medium text-muted-foreground', className)}>
      {children}
    </th>
  )
}

/** A label and its value, for details laid out in a grid. */
export function Cell({ label, value, mono }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div>
      <dt className="text-caption text-muted-foreground">{label}</dt>
      <dd className={cn('mt-0.5 text-sm', mono && 'font-mono tabular-nums')}>{value}</dd>
    </div>
  )
}

/* ─────────────────── Status ─────────────────── */

export type Tone = 'emerald' | 'amber' | 'red' | 'indigo' | 'zinc'

export const TONE_BADGE = { emerald: 'success', amber: 'warning', red: 'danger', indigo: 'info', zinc: 'neutral' } as const
const TONE_DOT: Record<Tone, string> = { emerald: 'bg-success', amber: 'bg-warning', red: 'bg-destructive', indigo: 'bg-info', zinc: 'bg-muted-foreground/50' }

/** A soft status badge with a dot. */
export function StatusPill({ tone, children, className }: { tone: Tone; children: ReactNode; className?: string }) {
  return (
    <Badge variant={TONE_BADGE[tone]} className={className}>
      <span aria-hidden className={cn('size-1.5 rounded-full', TONE_DOT[tone])} />
      {children}
    </Badge>
  )
}

/** A status as a dot and a word, for dense places. */
export function StatusDot({ tone, label }: { tone: Tone; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
      <span aria-hidden className={cn('size-1.5 rounded-full', TONE_DOT[tone])} />
      {label}
    </span>
  )
}

/* ─────────────────── Stats (see STYLE.md "Stats") ─────────────────── */

/** A stat's status dot: something to act on, or something waiting. */
const STAT_DOT = { action: 'bg-destructive', waiting: 'bg-warning' } as const

/**
 * Related stats in one card, split by thin lines (stacked on phones). Two to four in a row read
 * best. For more, pass `cols` (e.g. `grid-cols-2 sm:grid-cols-3 lg:grid-cols-6`): the strip then
 * wraps onto rows, with the lines drawn by a 1px gap. `bare` drops the card, for a strip inside
 * another card (needs `cols`).
 */
export function StatStrip({ children, cols, bare, className }: {
  children: ReactNode
  cols?: string
  bare?: boolean
  className?: string
}) {
  if (cols) {
    const grid = <div className={cn('grid gap-px bg-border [&>*]:bg-card', cols, bare && className)}>{children}</div>
    return bare ? grid : <Card className={cn('gap-0 overflow-hidden py-0', className)}>{grid}</Card>
  }
  const n = Children.toArray(children).length
  return (
    <Card className={cn(
      'grid gap-0 divide-y py-0 sm:divide-x sm:divide-y-0',
      n >= 4 ? 'sm:grid-cols-4' : n === 3 ? 'sm:grid-cols-3' : 'sm:grid-cols-2',
      className,
    )}>
      {children}
    </Card>
  )
}

/**
 * One quiet stat: a muted label, a black number, one muted line. `dot` marks a status beside the
 * label; `trend` draws a small sparkline under the number (the stat's only colour). With `to` the
 * whole cell is a link.
 */
export function Stat({ label, value, sub, period, dot, hint, trend, trendLabel, to }: {
  label: string
  value: ReactNode
  sub?: ReactNode
  /** "Last 30 days", "This month": sentence case, on the right of the label. */
  period?: string
  dot?: keyof typeof STAT_DOT | null
  /** Explanation on hover. */
  hint?: string
  trend?: Array<{ date?: string; count: number }>
  /** What the sparkline counts, for its tooltip: "leads", "letters". */
  trendLabel?: string
  to?: string
}) {
  const body = (
    <>
      <div className="flex items-center justify-between gap-2 text-sm text-muted-foreground">
        <span className="flex min-w-0 items-center gap-2">
          {dot ? <span aria-hidden className={cn('size-2 shrink-0 rounded-full', STAT_DOT[dot])} /> : null}
          <span className="truncate">{label}</span>
        </span>
        {period ? <span className="shrink-0 text-caption">{period}</span> : null}
      </div>
      <div className="mt-1 truncate text-2xl font-semibold tracking-tight tabular-nums">{value}</div>
      {sub ? (
        <div className="truncate text-caption text-muted-foreground tabular-nums" title={!hint && typeof sub === 'string' ? sub : undefined}>{sub}</div>
      ) : null}
      {trend && trend.length > 0 ? <div className="mt-2 -mb-1 h-8"><TrendChart data={trend} valueLabel={trendLabel} /></div> : null}
    </>
  )
  const cls = 'flex min-w-0 flex-col px-5 py-4'
  return to
    ? <Link to={to} title={hint} className={cn(cls, 'transition-colors hover:bg-muted/50')}>{body}</Link>
    : <div title={hint} className={cls}>{body}</div>
}

/* ─────────────────── Funnel ─────────────────── */

/** Search → lead → paid, as three small badges; stages not reached are grey. */
export function FunnelPills({ stages }: { stages: Array<{ label: string; tone: Tone; active: boolean }> }) {
  return (
    <span className="inline-flex items-center gap-1">
      {stages.map((s, i) => (
        <Badge key={i} variant={s.active ? TONE_BADGE[s.tone] : 'outline'} className={cn(!s.active && 'text-muted-foreground/60')}>
          {s.label}
        </Badge>
      ))}
    </span>
  )
}

/** The facts a search's funnel is derived from. Computed once on the server and returned by both
 *  the searches list and the search-detail endpoint, so the two views can never disagree. */
export type SearchFunnel = { hasLead: boolean; anyPaid: boolean; anyInflight: boolean; anyFailed: boolean }

/** Canonical Search → funnel badges, shared by the list column and the detail page.
 *  Stage-3 priority: paid > in-flight > failed > none. */
export function searchFunnelStages(f: SearchFunnel): Array<{ label: string; tone: Tone; active: boolean }> {
  const third = f.anyPaid
    ? { label: 'Paid', tone: 'emerald' as Tone, active: true }
    : f.anyInflight
      ? { label: 'Paying', tone: 'amber' as Tone, active: true }
      : f.anyFailed
        ? { label: 'Failed', tone: 'red' as Tone, active: true }
        : { label: 'Paid', tone: 'zinc' as Tone, active: false }
  return [
    { label: 'Search', tone: 'emerald', active: true },
    { label: 'Lead', tone: 'emerald', active: f.hasLead },
    third,
  ]
}

/* ─────────────────── Filter chip ─────────────────── */

export function Chip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <Badge variant="secondary" className="h-7 gap-1 pr-1 pl-2.5 text-xs">
      {label}
      <button onClick={onRemove} type="button" aria-label={`Remove ${label}`} className="inline-flex size-5 items-center justify-center rounded-full hover:bg-foreground/10">
        <X className="size-3" />
      </button>
    </Badge>
  )
}

/* ─────────────────── View toggle (Sentences / Table / Cards) ─────────────────── */

type ViewModeOption<T extends string> = { value: T; label: string }

export function ViewToggle<T extends string>({ value, options, onChange }: {
  value: T
  options: ViewModeOption<T>[]
  onChange: (v: T) => void
}) {
  return (
    <ToggleGroup type="single" variant="outline" size="sm" value={value} onValueChange={(v) => v && onChange(v as T)}>
      {options.map((opt) => {
        const label = opt.label.toLowerCase()
        const icon = label.includes('table') ? <Rows3 /> : label.includes('sentence') ? <AlignLeft /> : <LayoutGrid />
        return (
          <ToggleGroupItem key={opt.value} value={opt.value} title={opt.label} aria-label={opt.label}>
            {icon}
          </ToggleGroupItem>
        )
      })}
    </ToggleGroup>
  )
}

/* ─────────────────── Form helpers ─────────────────── */

/** A label and its input, for filter and settings forms. */
export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-1.5">
      <Label>{label}</Label>
      {children}
    </div>
  )
}

/* ─────────────────── Toolbar buttons ─────────────────── */

/** The refresh button in a page's toolbar. */
export function RefreshButton({ onClick, busy = false, title = 'Refresh' }: { onClick: () => void; busy?: boolean; title?: string }) {
  return (
    <Button variant="outline" size="icon-sm" onClick={onClick} disabled={busy} title={title} aria-label={title}>
      {busy ? <Loader2 className="animate-spin" /> : <RefreshCw />}
    </Button>
  )
}

/* ─────────────────── Empty state ─────────────────── */

export function EmptyState({ title, message }: { title: string; message?: string }) {
  return (
    <div className="rounded-xl border border-dashed bg-card/60 p-10 text-center">
      <p className="text-sm font-medium">{title}</p>
      {message ? <p className="mt-1 text-sm text-muted-foreground">{message}</p> : null}
    </div>
  )
}
