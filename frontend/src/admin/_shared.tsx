import { type ReactNode } from 'react'

export function Pager({ total, skip, take, onPrev, onNext, label }: { total: number; skip: number; take: number; onPrev: () => void; onNext: () => void; label: string }) {
  const from = total === 0 ? 0 : skip + 1
  const to = Math.min(skip + take, total)
  return (
    <div className="mt-4 flex items-center justify-between gap-3 px-1">
      <span className="bureau-meta">
        {total === 0 ? `0 ${label}` : `${from}–${to} of ${total} ${label}`}
      </span>
      <div className="flex gap-2">
        <button onClick={onPrev} disabled={skip === 0} className="bureau-btn">← Prev</button>
        <button onClick={onNext} disabled={skip + take >= total} className="bureau-btn">Next →</button>
      </div>
    </div>
  )
}

const stampClassFor = (status: string): string => {
  switch (status) {
    case 'Completed':
    case 'OK':
    case 'RenewalCompleted':
      return 'bureau-stamp bureau-stamp-ok'
    case 'Failed':
    case 'RenewalFailed':
      return 'bureau-stamp bureau-stamp-fail'
    case 'Pending':
    case 'Processing':
    case 'InProgress':
    case 'Synced':
    case 'RenewalQueued':
      return 'bureau-stamp bureau-stamp-info'
    case 'NotDueForRenewal':
    case 'WaitingForRenewalWindow':
    case 'AwaitingAuth':
      return 'bureau-stamp bureau-stamp-warn'
    case 'Skipped':
    case 'NoBusinessNames':
      return 'bureau-stamp bureau-stamp-neutral'
    default:
      return 'bureau-stamp bureau-stamp-neutral'
  }
}

export function StatusPill({ status }: { status: string }) {
  // Insert space before each capital so "RenewalCompleted" → "Renewal Completed"
  const display = status.replace(/([a-z])([A-Z])/g, '$1 $2')
  return <span className={stampClassFor(status)}>{display}</span>
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`bureau-card ${className}`}>{children}</div>
}

export function Table({ children }: { children: ReactNode }) {
  return (
    <div className="bureau-card">
      <div className="overflow-x-auto">
        <table className="bureau-table">{children}</table>
      </div>
    </div>
  )
}

/* ─────────────────────────────────────────────
   Higher-level bureau primitives reused across
   list and detail pages.
   ───────────────────────────────────────────── */

export function Banner({ kind, message }: { kind: 'ok' | 'fail' | 'info'; message: string }) {
  const cls = kind === 'ok'
    ? 'bureau-stamp-ok'
    : kind === 'fail'
      ? 'bureau-stamp-fail'
      : 'bureau-stamp-info'
  const verb = kind === 'ok' ? 'Verdict' : kind === 'fail' ? 'Incident' : 'Notice'
  return (
    <div className={`bureau-card flex items-start gap-4 px-4 py-3 ${cls}`} style={{ borderLeftWidth: 3 }}>
      <span className="bureau-stamp" style={{ background: 'transparent', border: '1.5px solid currentColor', flexShrink: 0 }}>
        {verb}
      </span>
      <p className="text-[13px] leading-snug" style={{ color: 'var(--ink)' }}>{message}</p>
    </div>
  )
}

export function Section({ title, kicker, actions, children, dense = false }: {
  title: string
  kicker?: string
  actions?: ReactNode
  children: ReactNode
  dense?: boolean
}) {
  return (
    <div className="bureau-card">
      <div
        className="flex items-center justify-between gap-3 border-b-2 border-[var(--ink)] px-5 py-3"
        style={{ background: 'var(--paper-deep)' }}
      >
        <div className="flex items-baseline gap-3 min-w-0">
          {kicker ? (
            <span className="bureau-mono text-[10px] shrink-0" style={{ color: 'var(--ledger-soft)', letterSpacing: '0.1em' }}>{kicker}</span>
          ) : null}
          <h3 className="bureau-display text-[15px] font-semibold tracking-tight truncate" style={{ color: 'var(--ink)' }}>{title}</h3>
        </div>
        {actions ? <div className="shrink-0 flex items-center gap-2">{actions}</div> : null}
      </div>
      <div className={dense ? '' : 'p-5'}>{children}</div>
    </div>
  )
}

export function DefList({ children }: { children: ReactNode }) {
  return <dl className="grid grid-cols-1 gap-y-3 sm:grid-cols-2 sm:gap-x-8 sm:gap-y-4">{children}</dl>
}

export function DefRow({ label, children, mono = false, full = false }: { label: string; children: ReactNode; mono?: boolean; full?: boolean }) {
  return (
    <div className={full ? 'sm:col-span-2' : ''}>
      <dt className="bureau-label" style={{ marginBottom: 4 }}>{label}</dt>
      <dd className={`text-[13px] ${mono ? 'bureau-mono' : ''}`} style={{ color: 'var(--ink)', wordBreak: mono ? 'break-all' : undefined }}>
        {children}
      </dd>
    </div>
  )
}

export function StatTile({ label, value, tone = 'ink', kicker }: {
  label: string
  value: string | number
  tone?: 'ink' | 'verdict' | 'caution' | 'info' | 'stamp' | 'ledger'
  kicker?: string
}) {
  const colorMap: Record<string, string> = {
    ink: 'var(--ink)',
    verdict: 'var(--verdict)',
    caution: 'var(--caution)',
    info: 'var(--info)',
    stamp: 'var(--stamp)',
    ledger: 'var(--ledger)',
  }
  return (
    <div className="bureau-ledger" style={{ background: 'var(--card)' }}>
      <div className="flex items-baseline justify-between gap-2">
        <div className="bureau-label">{label}</div>
        {kicker ? <span className="bureau-mono text-[10px]" style={{ color: 'var(--ledger-soft)' }}>{kicker}</span> : null}
      </div>
      <div className="bureau-ledger-num mt-3" style={{ color: colorMap[tone] ?? 'var(--ink)' }}>
        {value}
      </div>
    </div>
  )
}

