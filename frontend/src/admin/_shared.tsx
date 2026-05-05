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
    case 'Synced':
      return 'bureau-stamp bureau-stamp-info'
    case 'NotDueForRenewal':
    case 'WaitingForRenewalWindow':
      return 'bureau-stamp bureau-stamp-warn'
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
