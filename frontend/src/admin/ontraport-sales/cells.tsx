import { CircleAlert, Info } from 'lucide-react'
import { HANDLED, Status } from '../_status'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { blockedStatuses, prettyStatus, retryBanner, retryHint, type SaleRow } from './model'

/** "3 days overdue", "today", "in 9 days", with a dot when it's close. */
export function DueWords({ days }: { days: number }) {
  const dot = days < 0 ? 'bg-destructive' : days <= 7 ? 'bg-warning' : null
  return (
    <span className="inline-flex items-center gap-1.5">
      {dot ? <span aria-hidden className={cn('size-2 rounded-full', dot)} /> : null}
      {days < 0 ? `${Math.abs(days)} day${days === -1 ? '' : 's'} overdue` : days === 0 ? 'Due today' : `Due in ${days} day${days === 1 ? '' : 's'}`}
    </span>
  )
}

export function DueCell({ s }: { s: SaleRow }) {
  if (!s.renewalDueDate || s._daysUntil === null) {
    return <span className="text-muted-foreground">—</span>
  }
  return (
    <div className="whitespace-nowrap">
      <div className="tabular-nums">{new Date(s.renewalDueDate).toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' })}</div>
      <div className="text-caption text-muted-foreground tabular-nums"><DueWords days={s._daysUntil} /></div>
    </div>
  )
}

export function SaleStatusCell({ s, onShowError }: { s: SaleRow; onShowError: (title: string, body: string) => void }) {
  if (s.handledAt && s.status !== 'RenewalCompleted') return <Status entry={HANDLED} />
  const isFailed = s.status === 'RenewalFailed'
  const isBlocked = blockedStatuses.includes(s.status)

  // Compose the error body from either the OntraportSale.errorMessage or the
  // linked RenewalRequest's full failure context. Renewal step + message is the
  // most useful for debugging — show it first if available.
  const errorBody = (() => {
    const parts: string[] = []
    if (isBlocked) parts.push(retryBanner(s.status))
    if (s.renewalFailedAtStep) parts.push(`Failed at: ${s.renewalFailedAtStep}`)
    if (s.renewalErrorMessage) parts.push(s.renewalErrorMessage)
    if (s.errorMessage && !parts.some(p => p.includes(s.errorMessage!))) parts.push(s.errorMessage)
    return parts.join('\n\n') || 'No error details recorded.'
  })()

  const modalTitle = isFailed
    ? `Why ${s.businessName || 'this sale'} failed`
    : isBlocked
      ? `${s.businessName || 'This sale'} — ${prettyStatus(s.status).toLowerCase()}`
      : 'Sale details'

  return (
    <div>
      <div className="flex items-center gap-1">
        <OntraportStatusPill status={s.status} />
        {isFailed ? (
          <Button variant="ghost" size="icon-sm" className="sm:size-6" onClick={() => onShowError(modalTitle, errorBody)} title="Why did this fail?" aria-label="Why did this fail?">
            <CircleAlert className="text-destructive" />
          </Button>
        ) : isBlocked ? (
          <Button variant="ghost" size="icon-sm" className="sm:size-6" onClick={() => onShowError(modalTitle, errorBody)} title={retryBanner(s.status)} aria-label="More about this status">
            <Info className="text-muted-foreground" />
          </Button>
        ) : null}
      </div>
      {isFailed && s.renewalFailedAtStep ? (
        <div className="mt-1 text-caption text-muted-foreground">Stopped at {s.renewalFailedAtStep}</div>
      ) : null}
      {isBlocked ? (
        <div className="mt-1 text-caption text-muted-foreground">{retryHint(s.status)}</div>
      ) : null}
    </div>
  )
}

export function OntraportStatusPill({ status }: { status: string }) {
  return <Status kind="sale" status={status} />
}
