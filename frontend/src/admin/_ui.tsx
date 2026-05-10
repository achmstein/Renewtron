import type { ReactNode } from 'react'

/* ─────────────────── Page chrome ─────────────────── */

export function PageHeader({ kicker, title, subtitle, right }: { kicker: string; title: string; subtitle?: string; right?: ReactNode }) {
  return (
    <div className="mb-8 flex items-end justify-between gap-4 flex-wrap">
      <div>
        <div className="text-xxs font-mono font-medium uppercase tracking-[0.16em] text-brand-700">{kicker}</div>
        <h1 className="mt-1 text-2xl font-semibold text-zinc-900 tracking-tight">{title}</h1>
        {subtitle ? <p className="mt-1 text-sm text-zinc-500">{subtitle}</p> : null}
      </div>
      {right ? <div className="flex items-center gap-2">{right}</div> : null}
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
 * SparklineTile — same shape as StatTile but with a sparkline at the bottom and a delta% pill.
 * Used as the "today / 14d trend" tile in stats strips.
 */
export function SparklineTile({ kicker, label, value, sub, deltaPct, data }: {
  kicker?: string
  label: string
  value: string
  sub?: string
  deltaPct?: number | null
  data: number[]
}) {
  const max = Math.max(1, ...data)
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
      <div className="mt-2 -mb-1 flex-1 min-h-[28px]">
        <Sparkline data={data} max={max} />
      </div>
    </div>
  )
}

export function Sparkline({ data, max }: { data: number[]; max: number }) {
  if (data.length === 0) return null
  const w = 100, h = 24
  const safeMax = Math.max(1, max)
  const step = data.length > 1 ? w / (data.length - 1) : 0
  const pts = data.map((v, i) => `${(i * step).toFixed(2)},${(h - (v / safeMax) * h).toFixed(2)}`).join(' ')
  const lastX = (data.length - 1) * step
  const lastY = h - (data[data.length - 1] / safeMax) * h
  return (
    <svg viewBox={`0 0 ${w} ${h}`} className="w-full h-full" preserveAspectRatio="none">
      <polyline points={pts} fill="none" stroke="currentColor" strokeWidth="1.25" className="text-brand-500" vectorEffect="non-scaling-stroke" />
      <circle cx={lastX} cy={lastY} r="1.6" className="fill-brand-600" vectorEffect="non-scaling-stroke" />
    </svg>
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
  const TableIcon = (
    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 6h18M3 12h18M3 18h18" />
    </svg>
  )
  const CardsIcon = (
    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z" />
    </svg>
  )
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
      {busy ? (
        <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
        </svg>
      ) : (
        <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="1.5" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
        </svg>
      )}
    </button>
  )
}

/* ─────────────────── Toast ─────────────────── */

export function Toast({ tone = 'emerald', message }: { tone?: Tone; message: string }) {
  const map: Record<Tone, string> = {
    emerald: 'bg-emerald-50 text-emerald-800 ring-emerald-100',
    amber:   'bg-amber-50 text-amber-800 ring-amber-100',
    red:     'bg-red-50 text-red-800 ring-red-100',
    indigo:  'bg-indigo-50 text-indigo-800 ring-indigo-100',
    zinc:    'bg-zinc-100 text-zinc-800 ring-zinc-200',
  }
  return (
    <div className={`inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium ring-1 ${map[tone]}`}>
      {tone === 'emerald' ? (
        <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
        </svg>
      ) : null}
      {message}
    </div>
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
