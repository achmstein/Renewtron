import type { ReactNode } from 'react'
import { LayoutGrid, Loader2, RefreshCw, Rows3 } from 'lucide-react'
import { TrendChart } from '@/components/trend-chart'

/* ─────────────────── Page chrome ─────────────────── */

export function PageHeader({ kicker, title, subtitle, right }: { kicker: string; title: string; subtitle?: string; right?: ReactNode }) {
  return (
    <div className="mb-8 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">{kicker}</div>
        <h1 className="mt-1 text-2xl font-semibold text-zinc-900 tracking-tight">{title}</h1>
        {subtitle ? <p className="mt-1 text-sm text-zinc-500">{subtitle}</p> : null}
      </div>
      {right ? <div className="flex flex-wrap items-center gap-2">{right}</div> : null}
    </div>
  )
}

export function SectionTitle({ kicker, title, right }: { kicker: string; title: string; right?: ReactNode }) {
  return (
    <div className="flex items-end justify-between gap-3">
      <div>
        <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500">{kicker}</div>
        <h2 className="mt-0.5 text-base font-semibold text-zinc-900 tracking-tight">{title}</h2>
      </div>
      {right ? <div className="text-xs font-mono text-zinc-500 tabular-nums">{right}</div> : null}
    </div>
  )
}

export function KickerLabel({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`text-xxs font-mono font-medium uppercase tracking-[0.16em] text-zinc-500 ${className}`}>{children}</div>
}

/* ─────────────────── Table chrome ─────────────────── */

export function Th({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <th className={`px-4 py-2.5 text-left text-xxs font-mono font-medium uppercase tracking-[0.12em] text-zinc-500 border-b border-zinc-200 ${className}`}>
      {children}
    </th>
  )
}

/** Definition-list cell (used in Cards views): mono kicker label + value below. */
export function Cell({ label, value, mono }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div>
      <dt className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-500">{label}</dt>
      <dd className={`mt-1 text-sm text-zinc-900 ${mono ? 'font-mono tabular-nums' : ''}`}>{value}</dd>
    </div>
  )
}

/* ─────────────────── Pills + dots ─────────────────── */

export type Tone = 'emerald' | 'amber' | 'red' | 'indigo' | 'zinc'

export function StatusPill({ tone, children }: { tone: Tone; children: ReactNode }) {
  const map: Record<Tone, string> = {
    emerald: 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-100',
    amber:   'bg-amber-50 text-amber-700 ring-1 ring-amber-100',
    red:     'bg-red-50 text-red-700 ring-1 ring-red-100',
    indigo:  'bg-indigo-50 text-indigo-700 ring-1 ring-indigo-100',
    zinc:    'bg-zinc-100 text-zinc-700 ring-1 ring-zinc-200',
  }
  return (
    <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-xxs font-mono font-medium tracking-[0.12em] ${map[tone]}`}>
      {children}
    </span>
  )
}

export function StatusDot({ tone, label }: { tone: Tone; label: string }) {
  const dotMap: Record<Tone, string> = { emerald: 'bg-emerald-500', amber: 'bg-amber-500', red: 'bg-red-500', indigo: 'bg-indigo-500', zinc: 'bg-zinc-400' }
  const textMap: Record<Tone, string> = { emerald: 'text-emerald-700', amber: 'text-amber-700', red: 'text-red-700', indigo: 'text-indigo-700', zinc: 'text-zinc-500' }
  return (
    <div className={`inline-flex items-center gap-1.5 text-xxs font-mono uppercase tracking-[0.14em] ${textMap[tone]}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${dotMap[tone]}`}></span>
      {label}
    </div>
  )
}

/* ─────────────────── Stats ─────────────────── */

export function StatTile({ kicker, label, value, sub, tone = 'zinc' }: { kicker?: string; label: string; value: string; sub?: string; tone?: Tone }) {
  const valueColor =
    tone === 'emerald' ? 'text-emerald-700' :
    tone === 'amber'   ? 'text-amber-700' :
    tone === 'red'     ? 'text-red-700' :
    tone === 'indigo'  ? 'text-indigo-700' :
    'text-zinc-900'
  return (
    <div className="rounded-xl border border-zinc-200 bg-white p-4 shadow-sm">
      <div className="flex items-center justify-between gap-2">
        <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">{label}</div>
        {kicker ? <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-400">{kicker}</div> : null}
      </div>
      <div className={`mt-1 text-2xl font-semibold tabular-nums ${valueColor}`}>{value}</div>
      {sub ? <div className="text-xxs font-mono text-zinc-400 tabular-nums">{sub}</div> : null}
    </div>
  )
}

/**
 * SparklineTile — same shape as StatTile but with a hoverable area trend at the
 * bottom and a delta% pill. Used as the "today / 14d trend" tile in stats strips.
 * Pass `labels` (same length as data) to show dates in the hover tooltip.
 */
export function SparklineTile({ kicker, label, value, sub, deltaPct, data, labels }: {
  kicker?: string
  label: string
  value: string
  sub?: string
  deltaPct?: number | null
  data: number[]
  labels?: string[]
}) {
  const points = data.map((count, i) => ({ count, date: labels?.[i] }))
  return (
    <div className="rounded-xl border border-zinc-200 bg-white p-4 shadow-sm flex flex-col">
      <div className="flex items-center justify-between gap-2">
        <div className="text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500">{label}</div>
        <div className="flex items-center gap-2">
          {kicker ? <div className="text-xxs font-mono uppercase tracking-[0.14em] text-zinc-400">{kicker}</div> : null}
          {deltaPct != null ? (
            <div className={`text-xxs font-mono tabular-nums ${deltaPct >= 0 ? 'text-emerald-700' : 'text-red-700'}`}>
              {deltaPct >= 0 ? '+' : ''}{deltaPct}%
            </div>
          ) : null}
        </div>
      </div>
      <div className="mt-1 text-2xl font-semibold tabular-nums text-zinc-900">{value}</div>
      {sub ? <div className="text-xxs font-mono text-zinc-400 tabular-nums">{sub}</div> : null}
      <div className="mt-2 -mb-1 flex-1 min-h-[32px]">
        <TrendChart data={points} />
      </div>
    </div>
  )
}

/* ─────────────────── Funnel pills ─────────────────── */

export function FunnelPills({ stages }: { stages: Array<{ label: string; tone: Tone; active: boolean }> }) {
  return (
    <div className="inline-flex items-center gap-0.5 text-xxs font-mono font-medium tracking-[0.12em]">
      {stages.map((s, i) => (
        <span key={i} className="inline-flex items-center gap-0.5">
          {i > 0 ? <span className="text-zinc-300 mx-0.5">·</span> : null}
          <FunnelStage label={s.label} tone={s.tone} active={s.active} />
        </span>
      ))}
    </div>
  )
}

/** The facts a search's funnel is derived from. Computed once on the server and returned by both
 *  the searches list and the search-detail endpoint, so the two views can never disagree. */
export type SearchFunnel = { hasLead: boolean; anyPaid: boolean; anyInflight: boolean; anyFailed: boolean }

/** Canonical Search → funnel pills, shared by the list column and the detail page.
 *  Stage-3 priority: paid > in-flight > failed > none. */
export function searchFunnelStages(f: SearchFunnel): Array<{ label: string; tone: Tone; active: boolean }> {
  const third = f.anyPaid
    ? { label: 'PAID', tone: 'emerald' as Tone, active: true }
    : f.anyInflight
      ? { label: 'PAID', tone: 'amber' as Tone, active: true }
      : f.anyFailed
        ? { label: 'FAIL', tone: 'red' as Tone, active: true }
        : { label: 'PAID', tone: 'zinc' as Tone, active: false }
  return [
    { label: 'SRCH', tone: 'emerald', active: true },
    { label: 'LEAD', tone: 'emerald', active: f.hasLead },
    third,
  ]
}

function FunnelStage({ label, tone, active }: { label: string; tone: Tone; active: boolean }) {
  // Inactive stages are always zinc-grey regardless of `tone` (greys-out the future).
  // Active stages render in their tone.
  if (!active) {
    return <span className="px-1.5 py-0.5 rounded ring-1 ring-inset bg-zinc-50 text-zinc-400 ring-zinc-200">{label}</span>
  }
  const map: Record<Tone, string> = {
    emerald: 'bg-emerald-100 text-emerald-700 ring-emerald-200',
    amber:   'bg-amber-100 text-amber-700 ring-amber-200',
    red:     'bg-red-100 text-red-700 ring-red-200',
    indigo:  'bg-indigo-100 text-indigo-700 ring-indigo-200',
    zinc:    'bg-zinc-100 text-zinc-700 ring-zinc-200',
  }
  return <span className={`px-1.5 py-0.5 rounded ring-1 ring-inset ${map[tone]}`}>{label}</span>
}

/* ─────────────────── Filter chip ─────────────────── */

export function Chip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex items-center gap-x-1.5 rounded-full bg-brand-50 px-2.5 py-1 text-xs font-medium text-brand-800 ring-1 ring-brand-100">
      {label}
      <button onClick={onRemove} type="button" className="-mr-0.5 h-3.5 w-3.5 rounded hover:bg-brand-100 inline-flex items-center justify-center">
        <svg className="h-3 w-3" fill="none" viewBox="0 0 8 8" strokeWidth="1.5" stroke="currentColor">
          <path strokeLinecap="round" d="M1 1l6 6m0-6L1 7" />
        </svg>
      </button>
    </span>
  )
}

/* ─────────────────── View toggle (Table / Cards) ─────────────────── */

type ViewModeOption<T extends string> = { value: T; label: string }

export function ViewToggle<T extends string>({ value, options, onChange }: {
  value: T
  options: ViewModeOption<T>[]
  onChange: (v: T) => void
}) {
  const TableIcon = <Rows3 className="h-4 w-4" />
  const CardsIcon = <LayoutGrid className="h-4 w-4" />
  return (
    <div className="inline-flex" role="group">
      {options.map((opt, i) => {
        const active = value === opt.value
        const isFirst = i === 0
        const isLast = i === options.length - 1
        const radius = isFirst ? 'rounded-l-md' : isLast ? 'rounded-r-md' : ''
        const cls = active ? 'bg-zinc-900 text-white ring-zinc-900' : 'bg-white text-zinc-700 hover:bg-zinc-50 ring-zinc-300'
        const icon = opt.label.toLowerCase().includes('table') ? TableIcon : CardsIcon
        return (
          <button
            key={opt.value as string}
            type="button"
            onClick={() => onChange(opt.value)}
            title={opt.label}
            className={`${cls} ${radius} inline-flex items-center px-2.5 py-2 text-sm font-medium ring-1 ring-inset transition`}
          >
            {icon}
          </button>
        )
      })}
    </div>
  )
}

/* ─────────────────── Form helpers ─────────────────── */

/** Mono uppercase label + child input. Used inside FilterPopover bodies. */
export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <label className="block text-xxs font-mono font-medium uppercase tracking-[0.14em] text-zinc-500 mb-1">{label}</label>
      {children}
    </div>
  )
}

/* ─────────────────── Toolbar buttons ─────────────────── */

/** Refresh button used in the right-side toolbar of every list page. */
export function RefreshButton({ onClick, busy = false, title = 'Refresh' }: { onClick: () => void; busy?: boolean; title?: string }) {
  return (
    <button
      onClick={onClick}
      disabled={busy}
      className="inline-flex items-center rounded-md bg-white px-3 py-2 text-sm font-medium text-zinc-700 ring-1 ring-inset ring-zinc-300 hover:bg-zinc-50 disabled:opacity-50 disabled:cursor-not-allowed transition"
      title={title}
    >
      {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
    </button>
  )
}

/* ─────────────────── Empty state ─────────────────── */

export function EmptyState({ title, message }: { title: string; message?: string }) {
  return (
    <div className="rounded-xl border border-dashed border-zinc-200 bg-white/60 p-10 text-center">
      <p className="text-sm font-medium text-zinc-700">{title}</p>
      {message ? <p className="mt-1 text-sm text-zinc-500">{message}</p> : null}
    </div>
  )
}
